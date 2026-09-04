using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Models.Dtos;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Auth;
using WEBTechnologies_Final.Services.Marketplace;

namespace WEBTechnologies_Final.Controllers.Api
{
    /// <summary>
    /// The seller panel as an API.
    ///
    /// This exists so the panel is not tied to the MVC site. Every screen the /Seller/* pages
    /// render is available here, which is what makes the "own domain or integrated page"
    /// decision reversible later: a separate front-end can be pointed at these endpoints with
    /// a bearer token and needs nothing else from this app.
    /// </summary>
    [ApiController]
    [Route("api/v1/seller")]
    [Produces("application/json")]
    [Authorize]
    public class SellerApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ICurrentUser _current;
        private readonly IMediaUrlResolver _urls;
        private readonly SellerService _sellers;
        private readonly OfferService _offers;

        public SellerApiController(
            AppDbContext db, ICurrentUser current, IMediaUrlResolver urls,
            SellerService sellers, OfferService offers)
        {
            _db = db;
            _current = current;
            _urls = urls;
            _sellers = sellers;
            _offers = offers;
        }

        private int UserId => _current.UserId!.Value;

        // ---------- Becoming a seller ----------

        /// <summary>The signed-in account's seller profile, whether or not they sell yet.</summary>
        [HttpGet("profile")]
        [ProducesResponseType(typeof(SellerProfileDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProfile(CancellationToken ct)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId, ct);
            if (user is null) return NotFound(new ApiError("Account not found.", "not_found"));
            return Ok(ToDto(user));
        }

        /// <summary>Opts the account into selling and unlocks the panel.</summary>
        [HttpPost("profile")]
        [ProducesResponseType(typeof(SellerProfileDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> BecomeSeller([FromBody] BecomeSellerRequest req, CancellationToken ct)
        {
            var result = await _sellers.BecomeSellerAsync(
                UserId, req.SellerType, req.DisplayName, req.Location, ct);

            return result.Success
                ? Ok(ToDto(result.Value!))
                : BadRequest(new ApiError(result.Error!, result.Code!));
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile(
            [FromBody] UpdateSellerProfileRequest req, CancellationToken ct)
        {
            var result = await _sellers.UpdateProfileAsync(
                UserId, req.SellerType, req.DisplayName, req.Location, ct);

            return result.Success
                ? Ok(ToDto(result.Value!))
                : BadRequest(new ApiError(result.Error!, result.Code!));
        }

        // ---------- Panel ----------

        [HttpGet("dashboard")]
        [ProducesResponseType(typeof(SellerDashboardDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetDashboard(CancellationToken ct)
        {
            var d = await _sellers.GetDashboardAsync(UserId, ct);
            return Ok(new SellerDashboardDto(
                d.ActiveListings, d.Drafts, d.Reserved, d.Sold,
                d.PendingOffers, d.TotalOffers, d.UnreadMessages, d.SoldValue));
        }

        [HttpGet("listings")]
        [ProducesResponseType(typeof(IEnumerable<MyListingDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetListings([FromQuery] ListingStatus? status, CancellationToken ct)
        {
            var rows = await _sellers.GetListingsAsync(UserId, status, ct);

            return Ok(rows.Select(r => new MyListingDto(
                CarSummaryDto.From(r.Car, _urls),
                r.Car.Status,
                r.OfferCount,
                r.PendingOffers,
                r.BestPendingOffer,
                r.Car.SoldTo,
                r.Car.SoldPrice,
                null)).ToList());
        }

        [HttpGet("offers")]
        [ProducesResponseType(typeof(IEnumerable<OfferDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOffers([FromQuery] OfferStatus? status, CancellationToken ct)
        {
            var offers = await _sellers.GetOffersAsync(UserId, status, ct);
            return Ok(offers.Select(OfferDto.From).ToList());
        }

        [HttpPost("offers/{offerId:int}/accept")]
        [ProducesResponseType(typeof(OfferDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> AcceptOffer(
            int offerId, [FromBody] RespondToOfferRequest? req, CancellationToken ct)
        {
            var result = await _offers.AcceptAsync(offerId, UserId, _current.IsAdmin, req?.Response, ct);
            return Translate(result);
        }

        [HttpPost("offers/{offerId:int}/decline")]
        [ProducesResponseType(typeof(OfferDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> DeclineOffer(
            int offerId, [FromBody] RespondToOfferRequest? req, CancellationToken ct)
        {
            var result = await _offers.DeclineAsync(offerId, UserId, _current.IsAdmin, req?.Response, ct);
            return Translate(result);
        }

        /// <summary>Confirms the sale completed. Only valid on a Reserved listing.</summary>
        [HttpPost("listings/{carId:int}/sold")]
        public async Task<IActionResult> MarkSold(int carId, CancellationToken ct) =>
            Translate(await _offers.MarkSoldAsync(carId, UserId, _current.IsAdmin, ct));

        /// <summary>Undoes an accepted offer and puts the car back on the market.</summary>
        [HttpPost("listings/{carId:int}/reopen")]
        public async Task<IActionResult> Reopen(int carId, CancellationToken ct) =>
            Translate(await _offers.ReopenAsync(carId, UserId, _current.IsAdmin, ct));

        [HttpPost("listings/{carId:int}/archive")]
        public async Task<IActionResult> Archive(int carId, CancellationToken ct) =>
            await SetStatusAsync(carId, ListingStatus.Archived, ct);

        [HttpPost("listings/{carId:int}/publish")]
        public async Task<IActionResult> Publish(int carId, CancellationToken ct) =>
            await SetStatusAsync(carId, ListingStatus.Active, ct);

        // ---------- helpers ----------

        private async Task<IActionResult> SetStatusAsync(int carId, ListingStatus status, CancellationToken ct)
        {
            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == carId, ct);
            if (car is null) return NotFound(new ApiError("Listing not found.", "not_found"));

            if (!_current.IsAdmin && car.OwnerId != UserId)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new ApiError("This is not your listing.", "forbidden"));

            car.Status = status;
            if (status == ListingStatus.Active) car.PublishedUtc ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return Ok(CarSummaryDto.From(car, _urls));
        }

        private IActionResult Translate(MarketplaceResult<Offer> result)
        {
            if (result.Success) return Ok(OfferDto.From(result.Value!));
            return Problem(result.Error!, result.Code!);
        }

        private IActionResult Translate(MarketplaceResult result)
        {
            if (result.Success) return NoContent();
            return Problem(result.Error!, result.Code!);
        }

        // One place that decides which HTTP status a domain failure maps to, so the mobile
        // client can rely on 403 meaning "not yours" and 400 meaning "not right now".
        private IActionResult Problem(string error, string code) => code switch
        {
            MarketplaceCodes.NotFound => NotFound(new ApiError(error, code)),
            MarketplaceCodes.Forbidden =>
                StatusCode(StatusCodes.Status403Forbidden, new ApiError(error, code)),
            _ => BadRequest(new ApiError(error, code))
        };

        private static SellerProfileDto ToDto(User u) => new(
            u.Id, u.Username, u.IsSeller, u.SellerType,
            u.SellerDisplayName, u.SellerLocation, u.SellerVerified, u.SellerSinceUtc);
    }
}
