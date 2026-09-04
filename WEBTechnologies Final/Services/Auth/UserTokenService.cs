using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Auth
{
    /// <summary>
    /// Issues and redeems the single-use tokens behind password reset and email verification.
    /// </summary>
    public class UserTokenService
    {
        public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromHours(1);
        public static readonly TimeSpan EmailVerificationLifetime = TimeSpan.FromDays(3);

        private readonly AppDbContext _db;

        public UserTokenService(AppDbContext db) => _db = db;

        /// <summary>
        /// Creates a token and returns the RAW value - the only time it is ever available.
        /// Any outstanding token for the same purpose is invalidated first, so a fresh request
        /// silently retires an older link.
        /// </summary>
        public async Task<string> IssueAsync(int userId, UserTokenPurpose purpose, CancellationToken ct = default)
        {
            await _db.UserTokens
                .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedUtc, DateTime.UtcNow), ct);

            var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

            _db.UserTokens.Add(new UserToken
            {
                UserId = userId,
                Purpose = purpose,
                TokenHash = Hash(raw),
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.Add(
                    purpose == UserTokenPurpose.PasswordReset ? PasswordResetLifetime : EmailVerificationLifetime)
            });

            await _db.SaveChangesAsync(ct);
            return raw;
        }

        /// <summary>
        /// Consumes a token, returning the user it belonged to, or null if it is unknown,
        /// expired or already used. Marking it used and acting on it must happen together.
        /// </summary>
        public async Task<User?> RedeemAsync(string? rawToken, UserTokenPurpose purpose, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(rawToken)) return null;

            var hash = Hash(rawToken);
            var token = await _db.UserTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);

            if (token is null || !token.IsUsable || token.User is null) return null;

            token.UsedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return token.User;
        }

        private static string Hash(string token) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }
}
