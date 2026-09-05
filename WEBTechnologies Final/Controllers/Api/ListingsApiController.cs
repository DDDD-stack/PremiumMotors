using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Models.Dtos;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Auth;
using WEBTechnologies_Final.Services.Storage;

namespace WEBTechnologies_Final.Controllers.Api
{
    /// <summary>
    /// The "sell your car" flow for API clients, mirroring SellController on the website:
    /// create a draft, attach photos, then either publish free (launch mode / free relist) or
    /// publish it. Listing is free and always will be.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/v1/listings")]
    [Produces("application/json")]
    public class ListingsApiController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IPhotoStorage _photos;
        private readonly IMediaUrlResolver _urls;
        private readonly ICurrentUser _current;
        private readonly ILogger<ListingsApiController> _logger;

        public ListingsApiController(
            AppDbContext db, IPhotoStorage photos, ICurrentUser current,
            IMediaUrlResolver urls, ILogger<ListingsApiController> logger)
        {
            _db = db;
            _photos = photos;
            _urls = urls;
            _current = current;
            _logger = logger;
        }

        private int UserId => _current.UserId!.Value;
        private string UserName => _current.Username ?? string.Empty;

        /// <summary>Listings owned by the signed-in seller, including drafts and offer counts.</summary>
        [HttpGet("mine")]
        [ProducesResponseType(typeof(IEnumerable<MyListingDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Mine(CancellationToken ct)
        {
            var cars = await _db.Cars
                .Include(c => c.Offers)
                .Where(c => c.OwnerId == UserId)
                .OrderByDescending(c => c.Id)
                .ToListAsync(ct);

            var result = cars.Select(c => new MyListingDto(
                CarSummaryDto.From(c, _urls),
                c.Status,
                c.Offers.Count,
                c.Offers.Count(o => o.Status == OfferStatus.Pending),
                c.Offers.Where(o => o.Status == OfferStatus.Pending)
                    .Select(o => (decimal?)o.Amount).DefaultIfEmpty(null).Max(),
                c.SoldTo,
                c.SoldPrice)).ToList();

            return Ok(result);
        }

        /// <summary>
        /// Creates a listing and publishes it. Listing is free and always will be, so there
        /// is nothing to settle first.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(CreateListingResult), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create(
            [FromBody] CreateListingRequest req, CancellationToken ct)
        {
            var car = new Car
            {
                OwnerId = UserId,
                OwnerUsername = UserName,
                Status = ListingStatus.Active,
                PublishedUtc = DateTime.UtcNow,
                CreatedUtc = DateTime.UtcNow
            };

            // One place copies the request onto the entity, so create and update cannot drift.
            req.ApplyTo(car);
            car.Make = car.Make.Trim();
            car.Model = car.Model.Trim();
            car.Country = car.Country.Trim();

            _db.Cars.Add(car);
            await _db.SaveChangesAsync(ct);

            return Created($"/api/cars/{car.Id}", new CreateListingResult(
                "published", CarSummaryDto.From(car, _urls), "Your listing is now live."));
        }

        /// <summary>Updates a draft. Published listings are frozen, as on the website.</summary>
        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(CarSummaryDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateListingRequest req, CancellationToken ct)
        {
            var car = await FindOwnedAsync(id, ct);
            if (car is null) return NotFound(new ApiError("Listing not found.", "not_found"));

            if (car.Status != ListingStatus.Draft && !_current.IsAdmin)
                return BadRequest(new ApiError("A published listing can no longer be edited.", "already_published"));

            req.ApplyTo(car);
            car.Make = car.Make.Trim();
            car.Model = car.Model.Trim();
            car.Country = car.Country.Trim();

            await _db.SaveChangesAsync(ct);
            return Ok(CarSummaryDto.From(car, _urls));
        }

        /// <summary>Uploads photos onto a listing as multipart form-data under the field "photos".</summary>
        [HttpPost("{id:int}/photos")]
        [ProducesResponseType(typeof(PhotoUploadResult), StatusCodes.Status200OK)]
        [RequestSizeLimit(30 * 1024 * 1024)]
        public async Task<IActionResult> UploadPhotos(int id, [FromForm] List<IFormFile> photos, CancellationToken ct)
        {
            var car = await FindOwnedAsync(id, ct);
            if (car is null) return NotFound(new ApiError("Listing not found.", "not_found"));

            if (photos is null || photos.Count == 0)
                return BadRequest(new ApiError("No photos were uploaded.", "no_photos"));

            var result = await _photos.SaveAsync(photos, ct);
            if (result.Paths.Count == 0)
                return BadRequest(new ApiError(
                    result.Errors.Count > 0 ? string.Join(" ", result.Errors) : "No usable images were uploaded.",
                    "unsupported_type"));

            // Reassign rather than mutate: ImagePaths is stored via a JSON value converter, so
            // an in-place Add is not seen as a change.
            car.ImagePaths = car.ImagePaths.Concat(result.Paths).ToList();
            await _db.SaveChangesAsync(ct);

            return Ok(new PhotoUploadResult(car.Id, _urls.ResolveAll(car.ImagePaths), result.Errors));
        }

        /// <summary>Deletes a draft listing and releases any unpaid fee row attached to it.</summary>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var car = await FindOwnedAsync(id, ct);
            if (car is null) return NotFound(new ApiError("Listing not found.", "not_found"));

            if (car.Status != ListingStatus.Draft && !_current.IsAdmin)
                return BadRequest(new ApiError("A published listing cannot be deleted.", "already_published"));

            // Captured before SaveChanges: once the row is gone nothing records where the
            // blobs were. See PhotoStorageExtensions.DeleteAllAsync for why the cleanup runs
            // after the delete and never before it.
            var photos = car.ImagePaths.ToList();

            _db.Cars.Remove(car);
            await _db.SaveChangesAsync(ct);

            await _photos.DeleteAllAsync(photos, ct);
            return NoContent();
        }

        // ---------- helpers ----------

        private async Task<Car?> FindOwnedAsync(int id, CancellationToken ct)
        {
            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (car is null) return null;
            if (_current.IsAdmin) return car;

            if (car.OwnerId is not null)
                return car.OwnerId == UserId ? car : null;

            return car.OwnerUsername is not null
                   && string.Equals(car.OwnerUsername, UserName, StringComparison.OrdinalIgnoreCase)
                ? car : null;
        }

    }
}
