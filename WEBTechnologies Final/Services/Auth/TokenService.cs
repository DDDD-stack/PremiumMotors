using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Models.Dtos;

namespace WEBTechnologies_Final.Services.Auth
{
    /// <summary>
    /// Issues short-lived JWT access tokens plus long-lived, rotating refresh tokens.
    /// This is what makes one account usable from the web app and a native mobile app at the
    /// same time: both hold the same kind of credential, independent of any server session.
    /// </summary>
    public class TokenService
    {
        // Claim names are used verbatim (inbound claim mapping is disabled in Program.cs) so
        // the same names appear on the wire, in the mobile client, and in [Authorize].
        public const string SubClaim = "sub";
        public const string NameClaim = "name";
        public const string EmailClaim = "email";
        public const string RoleClaim = "role";

        private readonly AppDbContext _db;
        private readonly JwtOptions _options;

        public TokenService(AppDbContext db, IOptions<JwtOptions> options)
        {
            _db = db;
            _options = options.Value;
        }

        public async Task<AuthResponse> IssueAsync(User user, string? device, CancellationToken ct = default)
        {
            var (accessToken, accessExpires) = CreateAccessToken(user);
            var (refreshToken, refreshEntity) = CreateRefreshToken(user.Id, device);

            _db.RefreshTokens.Add(refreshEntity);
            user.LastLoginUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new AuthResponse(
                accessToken, accessExpires,
                refreshToken, refreshEntity.ExpiresUtc,
                UserDto.From(user));
        }

        /// <summary>
        /// Exchanges a refresh token for a new pair, rotating the old one out. Returns null if
        /// the token is unknown, expired, belongs to a disabled account, or was already used.
        /// </summary>
        public async Task<AuthResponse?> RefreshAsync(string refreshToken, string? device, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) return null;

            var hash = HashToken(refreshToken);
            var existing = await _db.RefreshTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

            if (existing is null) return null;

            // A token presented after it was rotated out means it leaked. Revoke every live
            // session for that user rather than silently issuing the attacker a fresh pair.
            if (existing.RevokedUtc is not null)
            {
                await RevokeAllAsync(existing.UserId, ct);
                return null;
            }

            if (existing.ExpiresUtc <= DateTime.UtcNow) return null;

            var user = existing.User;
            if (user is null || !user.IsActive) return null;

            var (accessToken, accessExpires) = CreateAccessToken(user);
            var (newToken, newEntity) = CreateRefreshToken(user.Id, device ?? existing.Device);

            _db.RefreshTokens.Add(newEntity);
            await _db.SaveChangesAsync(ct);

            existing.RevokedUtc = DateTime.UtcNow;
            existing.ReplacedByTokenId = newEntity.Id;
            await _db.SaveChangesAsync(ct);

            return new AuthResponse(
                accessToken, accessExpires,
                newToken, newEntity.ExpiresUtc,
                UserDto.From(user));
        }

        /// <summary>Signs out one device. False if the token was already unusable.</summary>
        public async Task<bool> RevokeAsync(string refreshToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) return false;

            var hash = HashToken(refreshToken);
            var existing = await _db.RefreshTokens
                .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedUtc == null, ct);

            if (existing is null) return false;

            existing.RevokedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>Signs out every device (password change, suspected compromise).</summary>
        public async Task RevokeAllAsync(int userId, CancellationToken ct = default)
        {
            await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, DateTime.UtcNow), ct);
        }

        public async Task<List<SessionDto>> ListSessionsAsync(
            int userId, string? currentRefreshToken, CancellationToken ct = default)
        {
            var currentHash = string.IsNullOrWhiteSpace(currentRefreshToken) ? null : HashToken(currentRefreshToken);
            var now = DateTime.UtcNow;

            return await _db.RefreshTokens
                .Where(t => t.UserId == userId && t.RevokedUtc == null && t.ExpiresUtc > now)
                .OrderByDescending(t => t.CreatedUtc)
                .Select(t => new SessionDto(t.Id, t.Device, t.CreatedUtc, t.ExpiresUtc, t.TokenHash == currentHash))
                .ToListAsync(ct);
        }

        private (string token, DateTime expiresUtc) CreateAccessToken(User user)
        {
            var expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

            var claims = new List<Claim>
            {
                new(SubClaim, user.Id.ToString()),
                new(NameClaim, user.Username),
                new(EmailClaim, user.Email),
                new(RoleClaim, user.Role),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expires,
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

            return (new JwtSecurityTokenHandler().WriteToken(token), expires);
        }

        private (string token, RefreshToken entity) CreateRefreshToken(int userId, string? device)
        {
            // Opaque, high-entropy and URL-safe so it survives a header or a JSON body.
            var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

            var label = device?.Trim();
            if (!string.IsNullOrEmpty(label) && label.Length > 64) label = label.Substring(0, 64);

            var entity = new RefreshToken
            {
                UserId = userId,
                TokenHash = HashToken(token),
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddDays(_options.RefreshTokenDays),
                Device = string.IsNullOrEmpty(label) ? null : label
            };

            return (token, entity);
        }

        private static string HashToken(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}
