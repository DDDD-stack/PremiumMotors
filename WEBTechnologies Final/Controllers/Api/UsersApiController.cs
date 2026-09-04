using Microsoft.AspNetCore.Mvc;
using WEBTechnologies_Final.Models.Dtos;
using WEBTechnologies_Final.Services.Auth;

namespace WEBTechnologies_Final.Controllers.Api
{
    /// <summary>
    /// Backwards-compatible aliases for the original /api/users endpoints.
    ///
    /// These previously hashed passwords with unsalted SHA-256, which was incompatible with the
    /// plaintext the website wrote - an account made here could not sign in on the site. Both
    /// now go through <see cref="AccountService"/>. New clients should use /api/auth instead,
    /// which also returns tokens.
    /// </summary>
    [ApiController]
    [Route("api/v1/users")]
    [Produces("application/json")]
    public class UsersApiController : ControllerBase
    {
        private readonly AccountService _accounts;
        private readonly TokenService _tokens;

        public UsersApiController(AccountService accounts, TokenService tokens)
        {
            _accounts = accounts;
            _tokens = tokens;
        }

        [HttpPost("register")]
        [Obsolete("Use POST /api/auth/register, which also returns access and refresh tokens.")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
        {
            var result = await _accounts.RegisterAsync(req.Username, req.Email, req.Phone, req.Password, ct);
            if (!result.Succeeded)
                return Conflict(new ApiError(result.Error!, "registration_failed"));

            return Ok(await _tokens.IssueAsync(result.User!, req.Device, ct));
        }

        [HttpPost("validate")]
        [Obsolete("Use POST /api/auth/login, which also returns access and refresh tokens.")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Validate([FromBody] LoginRequest req, CancellationToken ct)
        {
            var result = await _accounts.ValidateAsync(req.Username, req.Password, ct);
            if (!result.Succeeded)
                return Unauthorized(new ApiError(result.Error!, "invalid_credentials"));

            return Ok(await _tokens.IssueAsync(result.User!, req.Device, ct));
        }
    }
}
