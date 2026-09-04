using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Models.Dtos;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Auth;

namespace WEBTechnologies_Final.Controllers.Api
{
    /// <summary>
    /// Token-based authentication for every client: the website, a future PWA, and the planned
    /// iOS/Android apps. A client registers or logs in once, stores the refresh token in secure
    /// device storage, and from then on silently exchanges it for fresh access tokens.
    /// </summary>
    [ApiController]
    [Route("api/v1/auth")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [Produces("application/json")]
    public class AuthApiController : ControllerBase
    {
        private readonly AccountService _accounts;
        private readonly TokenService _tokens;
        private readonly AppDbContext _db;
        private readonly ICurrentUser _current;
        private readonly UserTokenService _userTokens;
        private readonly IEmailSender _email;
        private readonly EmailOptions _emailOptions;
        private readonly ILogger<AuthApiController> _logger;

        public AuthApiController(
            AccountService accounts, TokenService tokens, AppDbContext db, ICurrentUser current,
            UserTokenService userTokens, IEmailSender email,
            Microsoft.Extensions.Options.IOptions<EmailOptions> emailOptions,
            ILogger<AuthApiController> logger)
        {
            _accounts = accounts;
            _tokens = tokens;
            _db = db;
            _current = current;
            _userTokens = userTokens;
            _email = email;
            _emailOptions = emailOptions.Value;
            _logger = logger;
        }

        /// <summary>Creates an account and returns a signed-in token pair.</summary>
        [HttpPost("register")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
        {
            var result = await _accounts.RegisterAsync(req.Username, req.Email, req.Phone, req.Password, ct);
            if (!result.Succeeded)
                return Conflict(new ApiError(result.Error!, "registration_failed"));

            return Ok(await _tokens.IssueAsync(result.User!, DeviceLabel(req.Device), ct));
        }

        /// <summary>Signs in with a username *or* email address plus password.</summary>
        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
        {
            var result = await _accounts.ValidateAsync(req.Username, req.Password, ct);
            if (!result.Succeeded)
                return Unauthorized(new ApiError(result.Error!, "invalid_credentials"));

            return Ok(await _tokens.IssueAsync(result.User!, DeviceLabel(req.Device), ct));
        }

        /// <summary>
        /// Exchanges a refresh token for a new pair. The presented token is rotated out, so each
        /// refresh token is single-use; replaying one revokes every session for that account.
        /// </summary>
        [HttpPost("refresh")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
        {
            var response = await _tokens.RefreshAsync(req.RefreshToken, DeviceLabel(req.Device), ct);
            if (response is null)
                return Unauthorized(new ApiError("That session has expired. Please sign in again.", "invalid_refresh_token"));

            return Ok(response);
        }

        /// <summary>Signs out the device holding this refresh token.</summary>
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest req, CancellationToken ct)
        {
            await _tokens.RevokeAsync(req.RefreshToken, ct);

            // Idempotent on purpose: a client signing out should never have to handle a failure.
            return NoContent();
        }

        /// <summary>Signs out every device for the signed-in account.</summary>
        [Authorize]
        [HttpPost("logout-all")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> LogoutAll(CancellationToken ct)
        {
            await _tokens.RevokeAllAsync(_current.UserId!.Value, ct);
            return NoContent();
        }

        /// <summary>The signed-in account. Clients call this on launch to restore state.</summary>
        [Authorize]
        [HttpGet("me")]
        [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Me(CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == _current.UserId!.Value, ct);
            if (user is null) return Unauthorized(new ApiError("Account not found.", "account_missing"));
            return Ok(UserDto.From(user));
        }

        [Authorize]
        [HttpPost("change-password")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
        {
            var userId = _current.UserId!.Value;
            var result = await _accounts.ChangePasswordAsync(userId, req.CurrentPassword, req.NewPassword, ct);
            if (!result.Succeeded)
                return BadRequest(new ApiError(result.Error!, "change_password_failed"));

            if (req.RevokeOtherSessions)
                await _tokens.RevokeAllAsync(userId, ct);

            return NoContent();
        }

        /// <summary>Live sessions for this account, so a user can see and revoke their devices.</summary>
        [Authorize]
        [HttpGet("sessions")]
        [ProducesResponseType(typeof(List<SessionDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Sessions([FromQuery] string? currentRefreshToken, CancellationToken ct) =>
            Ok(await _tokens.ListSessionsAsync(_current.UserId!.Value, currentRefreshToken, ct));

        [Authorize]
        [HttpDelete("sessions/{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RevokeSession(int id, CancellationToken ct)
        {
            var session = await _db.RefreshTokens
                .FirstOrDefaultAsync(t => t.Id == id && t.UserId == _current.UserId!.Value, ct);
            if (session is null) return NotFound(new ApiError("Session not found.", "not_found"));

            session.RevokedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return NoContent();
        }

        /// <summary>
        /// Starts a password reset. Always returns 204, whether or not the address exists -
        /// otherwise this endpoint becomes an account-enumeration oracle.
        /// </summary>
        [HttpPost("forgot-password")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
        {
            var email = (req.Email ?? string.Empty).Trim().ToLower();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);

            if (user is not null && user.IsActive)
            {
                var token = await _userTokens.IssueAsync(user.Id, UserTokenPurpose.PasswordReset, ct);
                var link = $"{PublicBaseUrl()}/reset-password?token={Uri.EscapeDataString(token)}";

                var sent = await _email.SendAsync(user.Email, "Reset your PremiumMotors password",
                    $"<p>Hello {System.Net.WebUtility.HtmlEncode(user.Username)},</p>" +
                    $"<p>Use the link below to choose a new password. It expires in one hour and can " +
                    $"only be used once.</p><p><a href=\"{link}\">Reset your password</a></p>" +
                    $"<p>If you did not ask for this, you can ignore this email - nothing has changed.</p>", ct);

                if (!sent)
                    _logger.LogError("Password reset email for user {UserId} could not be sent.", user.Id);
            }

            return NoContent();
        }

        /// <summary>Completes a reset. The token is single-use and signs out every device.</summary>
        [HttpPost("reset-password")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
        {
            var user = await _userTokens.RedeemAsync(req.Token, UserTokenPurpose.PasswordReset, ct);
            if (user is null)
                return BadRequest(new ApiError(
                    "That reset link is invalid or has expired. Please request a new one.", "invalid_token"));

            var result = await _accounts.SetPasswordAsync(user.Id, req.NewPassword, ct);
            if (!result.Succeeded)
                return BadRequest(new ApiError(result.Error!, "reset_failed"));

            // A reset usually means the account may be compromised - drop every live session.
            await _tokens.RevokeAllAsync(user.Id, ct);
            return NoContent();
        }

        /// <summary>Sends (or resends) the email-verification link for the signed-in account.</summary>
        [Authorize]
        [HttpPost("send-verification")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> SendVerification(CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == _current.UserId!.Value, ct);
            if (user is null) return Unauthorized(new ApiError("Account not found.", "account_missing"));
            if (user.IsEmailVerified) return NoContent();

            var token = await _userTokens.IssueAsync(user.Id, UserTokenPurpose.EmailVerification, ct);
            var link = $"{PublicBaseUrl()}/verify-email?token={Uri.EscapeDataString(token)}";

            await _email.SendAsync(user.Email, "Confirm your PremiumMotors email",
                $"<p>Hello {System.Net.WebUtility.HtmlEncode(user.Username)},</p>" +
                $"<p>Please confirm this address: <a href=\"{link}\">Confirm my email</a></p>" +
                $"<p>This link expires in three days.</p>", ct);

            return NoContent();
        }

        [HttpPost("verify-email")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest req, CancellationToken ct)
        {
            var user = await _userTokens.RedeemAsync(req.Token, UserTokenPurpose.EmailVerification, ct);
            if (user is null)
                return BadRequest(new ApiError(
                    "That confirmation link is invalid or has expired.", "invalid_token"));

            await _accounts.MarkEmailVerifiedAsync(user.Id, ct);
            return NoContent();
        }

        // Links in emails must be absolute and must survive being opened on a phone, so they
        // are built from configuration rather than the current request where possible.
        private string PublicBaseUrl()
        {
            if (!string.IsNullOrWhiteSpace(_emailOptions.PublicBaseUrl))
                return _emailOptions.PublicBaseUrl.TrimEnd('/');
            return $"{Request.Scheme}://{Request.Host}";
        }

        // Prefer an explicit label from the client, then the standard header, then User-Agent.
        private string? DeviceLabel(string? fromBody)
        {
            if (!string.IsNullOrWhiteSpace(fromBody)) return fromBody;
            if (Request.Headers.TryGetValue("X-Client-Device", out var header) && !string.IsNullOrWhiteSpace(header))
                return header.ToString();
            return Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;
        }
    }
}
