using Microsoft.AspNetCore.Authorization;
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
    /// Everything the signed-in account owns, in the shape a mobile "My account" tab wants:
    /// profile, offers placed, and listings won.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/me")]
    [Produces("application/json")]
    public class MeApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly AccountService _accounts;
        private readonly ICurrentUser _current;
        private readonly IMediaUrlResolver _urls;
        private readonly AccountDataService _data;

        public MeApiController(
            AppDbContext db, AccountService accounts, ICurrentUser current,
            IMediaUrlResolver urls, AccountDataService data)
        {
            _db = db;
            _accounts = accounts;
            _current = current;
            _urls = urls;
            _data = data;
        }

        private int UserId => _current.UserId!.Value;

        [HttpGet]
        [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
            if (user is null) return Unauthorized(new ApiError("Account not found.", "account_missing"));
            return Ok(UserDto.From(user));
        }

        [HttpPut]
        [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update([FromBody] UpdateProfileRequest req, CancellationToken ct)
        {
            var result = await _accounts.UpdateProfileAsync(UserId, req.Email, req.Phone, ct);
            if (!result.Succeeded)
                return BadRequest(new ApiError(result.Error!, "update_failed"));

            return Ok(UserDto.From(result.User!));
        }

        /// <summary>
        /// Right to data portability: everything held about this account, as JSON.
        /// </summary>
        [HttpGet("export")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Export(CancellationToken ct)
        {
            var data = await _data.ExportAsync(UserId, ct);
            if (data is null) return Unauthorized(new ApiError("Account not found.", "account_missing"));

            Response.Headers.ContentDisposition =
                $"attachment; filename=premiummotors-account-{UserId}.json";
            return Ok(data);
        }

        /// <summary>
        /// Right to erasure. Personal data is scrubbed everywhere it appears; auction history
        /// survives attributed to an anonymous handle, because the counterparty to a completed
        /// sale has a legitimate interest in that record. Irreversible.
        /// </summary>
        [HttpPost("delete")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest req, CancellationToken ct)
        {
            if (!string.Equals(req.Confirm?.Trim(), "DELETE", StringComparison.Ordinal))
                return BadRequest(new ApiError(
                    "Send \"confirm\": \"DELETE\" to confirm this is intentional.", "confirmation_required"));

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
            if (user is null) return Unauthorized(new ApiError("Account not found.", "account_missing"));

            // Re-authenticate: an access token alone must not be enough to erase an account.
            if (PasswordHasher.Verify(req.Password, user.PasswordHash) == PasswordVerificationResult.Failed)
                return BadRequest(new ApiError("Your password is incorrect.", "invalid_credentials"));

            if (user.IsAdmin)
                return BadRequest(new ApiError(
                    "An administrator account cannot be deleted through this endpoint.", "forbidden"));

            await _data.AnonymizeAsync(UserId, ct);
            return NoContent();
        }

        /// <summary>
        /// Offers this account has placed. Each shows only the users own amount, never the
        /// standing total on the listing, which stays sealed until the seller sees it.
        /// </summary>
        [HttpGet("offers")]
        [ProducesResponseType(typeof(IEnumerable<MyOfferDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> MyOffers(CancellationToken ct)
        {
            var username = _current.Username ?? string.Empty;

            var rows = await _db.Offers
                .Include(o => o.Car)
                .Where(o => o.BuyerId == UserId
                            || (o.BuyerId == null && o.BuyerUsername.ToLower() == username.ToLower()))
                .OrderByDescending(o => o.CreatedUtc)
                .ToListAsync(ct);

            var result = rows
                .Where(o => o.Car is not null)
                .Select(o => new MyOfferDto(
                    o.Id, o.Amount, o.Message, o.Status, o.CreatedUtc, o.RespondedUtc,
                    o.SellerResponse, o.ConversationId,
                    CarSummaryDto.From(o.Car!, _urls)))
                .ToList();

            return Ok(result);
        }

        /// <summary>Listings this account bought — an offer of theirs was accepted.</summary>
        [HttpGet("purchases")]
        [ProducesResponseType(typeof(IEnumerable<CarSummaryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Purchases(CancellationToken ct)
        {
            var username = _current.Username ?? string.Empty;

            var cars = await _db.Cars
                .Where(c => (c.Status == ListingStatus.Sold || c.Status == ListingStatus.Reserved)
                            && (c.SoldToUserId == UserId
                                || (c.SoldToUserId == null && c.SoldTo != null
                                    && c.SoldTo.ToLower() == username.ToLower())))
                .OrderByDescending(c => c.Id)
                .ToListAsync(ct);

            return Ok(cars.Select(c => CarSummaryDto.From(c, _urls)).ToList());
        }
    }
}
