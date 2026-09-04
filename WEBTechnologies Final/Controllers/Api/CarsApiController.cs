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
    /// Public marketplace browsing plus offer placement.
    ///
    /// Offers are private to the seller by design, so this controller never returns another
    /// buyer's offer on a public route — a viewer sees only their own.
    /// </summary>
    [ApiController]
    [Route("api/v1/cars")]
    [Produces("application/json")]
    public class CarsApiController : ControllerBase
    {
        private const int MaxPageSize = 100;

        private readonly AppDbContext _db;
        private readonly ICurrentUser _current;
        private readonly IMediaUrlResolver _urls;
        private readonly OfferService _offers;

        public CarsApiController(
            AppDbContext db, ICurrentUser current, IMediaUrlResolver urls, OfferService offers)
        {
            _db = db;
            _current = current;
            _urls = urls;
            _offers = offers;
        }

        /// <summary>Listed cars, filtered, sorted and paged for mobile list scrolling.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(PagedResult<CarSummaryDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<PagedResult<CarSummaryDto>>> GetCars(
            [FromQuery] string? search,
            [FromQuery] CarType? type,
            [FromQuery] string? make,
            [FromQuery] string? model,
            [FromQuery] int? year,
            [FromQuery] string? country,
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] int? maxMileage,
            [FromQuery] FuelType? fuelType,
            [FromQuery] TransmissionType? transmission,
            [FromQuery] bool availableOnly = false,
            [FromQuery] string sortBy = "newest",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > MaxPageSize ? 20 : pageSize;

            // Drafts and archived listings are seller-only; reserved and sold stay browsable.
            var query = _db.Cars
                .Where(c => c.Status != ListingStatus.Draft && c.Status != ListingStatus.Archived);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(c =>
                    c.Make.Contains(term) || c.Model.Contains(term) || c.Description.Contains(term));
            }
            if (type.HasValue) query = query.Where(c => c.Type == type.Value);
            if (!string.IsNullOrWhiteSpace(make)) query = query.Where(c => c.Make == make);
            if (!string.IsNullOrWhiteSpace(model)) query = query.Where(c => c.Model == model);
            if (year.HasValue) query = query.Where(c => c.Year == year.Value);
            if (!string.IsNullOrWhiteSpace(country)) query = query.Where(c => c.Country == country);
            if (minPrice.HasValue) query = query.Where(c => c.Price >= minPrice.Value);
            if (maxPrice.HasValue) query = query.Where(c => c.Price <= maxPrice.Value);
            if (maxMileage.HasValue) query = query.Where(c => c.Mileage <= maxMileage.Value);
            if (fuelType.HasValue) query = query.Where(c => c.FuelType == fuelType.Value);
            if (transmission.HasValue) query = query.Where(c => c.Transmission == transmission.Value);

            // "Still buyable": excludes reserved and sold.
            if (availableOnly) query = query.Where(c => c.Status == ListingStatus.Active);

            query = sortBy switch
            {
                "price_asc" => query.OrderBy(c => c.Price),
                "price_desc" => query.OrderByDescending(c => c.Price),
                "year_asc" => query.OrderBy(c => c.Year),
                "year_desc" => query.OrderByDescending(c => c.Year),
                "mileage_asc" => query.OrderBy(c => c.Mileage),
                _ => query.OrderByDescending(c => c.Id)
            };

            var total = await query.CountAsync(ct);

            // Materialize before projecting: Title and PrimaryImage are computed in C#.
            var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            var items = rows.Select(c => CarSummaryDto.From(c, _urls)).ToList();

            return Ok(new PagedResult<CarSummaryDto>(items, page, pageSize, total));
        }

        /// <summary>
        /// One listing. Drafts are visible only to their seller or an admin. The full offer
        /// list is attached for those same viewers; every other signed-in viewer gets only
        /// their own offer back.
        /// </summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(CarDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CarDetailDto>> GetCar(int id, CancellationToken ct)
        {
            var car = await _db.Cars.Include(c => c.Offers).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (car is null) return NotFound(new ApiError("Listing not found.", "not_found"));

            var isSeller = IsSeller(car);

            // Same rule as the website: a draft simply does not exist for anyone else.
            if (!car.IsPubliclyVisible && !isSeller)
                return NotFound(new ApiError("Listing not found.", "not_found"));

            var userId = _current.UserId;

            var isFavorite = userId is not null &&
                await _db.UserFavoriteCars.AnyAsync(f => f.UserId == userId.Value && f.CarId == id, ct);

            IReadOnlyList<OfferDto>? offers = null;
            OfferDto? myOffer = null;
            BuyerContactDto? buyerContact = null;
            int? conversationId = null;

            if (isSeller)
            {
                offers = car.Offers
                    .OrderBy(o => o.Status == OfferStatus.Pending ? 0 : 1)
                    .ThenByDescending(o => o.Amount)
                    .Select(OfferDto.From)
                    .ToList();

                // Contact details are released once the seller has accepted an offer.
                if (car.SoldToUserId is not null)
                {
                    var buyer = await _db.Users.FirstOrDefaultAsync(u => u.Id == car.SoldToUserId, ct);
                    if (buyer is not null)
                        buyerContact = new BuyerContactDto(buyer.Username, buyer.Email, buyer.Phone);
                }
            }
            else if (userId is not null)
            {
                var mine = car.Offers
                    .Where(o => o.BuyerId == userId.Value)
                    .OrderByDescending(o => o.CreatedUtc)
                    .FirstOrDefault();
                if (mine is not null) myOffer = OfferDto.From(mine);

                conversationId = (await _db.Conversations
                    .FirstOrDefaultAsync(c => c.CarId == id && c.BuyerId == userId.Value, ct))?.Id;
            }

            var sellerName = car.OwnerId is null
                ? null
                : await _db.Users.Where(u => u.Id == car.OwnerId)
                    .Select(u => u.SellerDisplayName).FirstOrDefaultAsync(ct);

            return Ok(new CarDetailDto(
                car.Id, car.Title, car.Make, car.Model, car.Type, car.Year, car.Description,
                car.Price, car.Country, car.City,
                VehicleSpecDto.From(car),
                _urls.ResolveAll(car.ImagePaths), _urls.Resolve(car.PrimaryImage),
                car.Status, car.AcceptsOffers,
                car.OwnerUsername, sellerName, car.CreatedUtc, car.SoldUtc,
                isSeller, isFavorite,
                isSeller ? car.Offers.Count : null,
                offers, myOffer, conversationId, buyerContact));
        }

        /// <summary>Every filter dropdown in one round trip, to keep mobile clients from chattering.</summary>
        [HttpGet("filters")]
        [ProducesResponseType(typeof(CarFiltersDto), StatusCodes.Status200OK)]
        public async Task<ActionResult<CarFiltersDto>> GetFilters([FromQuery] string? make, CancellationToken ct)
        {
            var listed = _db.Cars
                .Where(c => c.Status != ListingStatus.Draft && c.Status != ListingStatus.Archived);

            var makes = await listed.Select(c => c.Make).Distinct().OrderBy(m => m).ToListAsync(ct);

            var modelQuery = string.IsNullOrWhiteSpace(make) ? listed : listed.Where(c => c.Make == make);
            var models = await modelQuery.Select(c => c.Model).Distinct().OrderBy(m => m).ToListAsync(ct);

            var years = await listed.Select(c => c.Year).Distinct().OrderByDescending(y => y).ToListAsync(ct);
            var countries = await listed.Select(c => c.Country).Distinct().OrderBy(c => c).ToListAsync(ct);

            return Ok(new CarFiltersDto(
                makes, models, years,
                Enum.GetNames<CarType>(),
                countries,
                Enum.GetNames<FuelType>(),
                Enum.GetNames<TransmissionType>(),
                Enum.GetNames<VehicleCondition>()));
        }

        [HttpGet("stats")]
        public async Task<ActionResult<CarStatsDto>> GetStats(CancellationToken ct)
        {
            var listed = _db.Cars
                .Where(c => c.Status != ListingStatus.Draft && c.Status != ListingStatus.Archived);

            var total = await listed.CountAsync(ct);
            var active = await listed.CountAsync(c => c.Status == ListingStatus.Active, ct);
            var reserved = await listed.CountAsync(c => c.Status == ListingStatus.Reserved, ct);
            var sold = await listed.CountAsync(c => c.Status == ListingStatus.Sold, ct);
            var offers = await _db.Offers.CountAsync(ct);

            return Ok(new CarStatsDto(total, active, reserved, sold, offers));
        }

        /// <summary>
        /// Places a private offer. An offer does not have to beat the asking price or any
        /// other offer — offers are private, so there is no standing total to beat. The seller
        /// answers each one explicitly; nothing resolves on a timer.
        /// </summary>
        [Authorize]
        [HttpPost("{id:int}/offers")]
        [ProducesResponseType(typeof(OfferDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> PlaceOffer(int id, [FromBody] PlaceOfferRequest req, CancellationToken ct)
        {
            var result = await _offers.PlaceAsync(
                id, _current.UserId!.Value, _current.Username ?? string.Empty,
                req.Amount, req.Message, ct);

            if (!result.Success)
                return result.Code == MarketplaceCodes.NotFound
                    ? NotFound(new ApiError(result.Error!, result.Code))
                    : BadRequest(new ApiError(result.Error!, result.Code!));

            return CreatedAtAction(nameof(GetCar), new { id }, OfferDto.From(result.Value!));
        }

        /// <summary>The offers on a listing. Seller or admin only.</summary>
        [Authorize]
        [HttpGet("{id:int}/offers")]
        [ProducesResponseType(typeof(IEnumerable<OfferDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetOffers(int id, CancellationToken ct)
        {
            var car = await _db.Cars.Include(c => c.Offers).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (car is null) return NotFound(new ApiError("Listing not found.", "not_found"));

            if (!IsSeller(car))
                return StatusCode(StatusCodes.Status403Forbidden,
                    new ApiError("Offers are private to the seller of this listing.", "forbidden"));

            return Ok(car.Offers
                .OrderBy(o => o.Status == OfferStatus.Pending ? 0 : 1)
                .ThenByDescending(o => o.Amount)
                .Select(OfferDto.From)
                .ToList());
        }

        // Ownership only, deliberately NOT counting admins: an administrator is not the seller
        // of a user listing, so they must stay able to make an offer on one.
        private bool IsOwner(Car car) =>
            _current.UserId is not null && car.OwnerId is not null && car.OwnerId == _current.UserId;

        // Sellers and admins may both read a listing's private offers.
        private bool IsSeller(Car car) => _current.IsAdmin || IsOwner(car);
    }
}
