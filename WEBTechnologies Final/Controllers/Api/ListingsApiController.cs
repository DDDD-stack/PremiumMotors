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
    /// pay the listing fee before it goes live.
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
        private readonly IPaymentProvider _pay;
        private readonly ListingOptions _listing;
        private readonly ICurrentUser _current;
        private readonly ILogger<ListingsApiController> _logger;

        public ListingsApiController(
            AppDbContext db, IPhotoStorage photos, IPaymentProvider pay,
            IOptions<ListingOptions> listing, ICurrentUser current,
            IMediaUrlResolver urls, ILogger<ListingsApiController> logger)
        {
            _db = db;
            _photos = photos;
            _urls = urls;
            _pay = pay;
            _listing = listing.Value;
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

            var carIds = cars.Select(c => c.Id).ToList();
            var payments = await _db.Payments
                .Where(p => p.CarId != null && carIds.Contains(p.CarId!.Value))
                .ToListAsync(ct);

            var result = cars.Select(c =>
            {
                var payment = payments.FirstOrDefault(p => p.CarId == c.Id);
                return new MyListingDto(
                    CarSummaryDto.From(c, _urls),
                    c.Status,
                    c.Offers.Count,
                    c.Offers.Count(o => o.Status == OfferStatus.Pending),
                    c.Offers.Where(o => o.Status == OfferStatus.Pending)
                        .Select(o => (decimal?)o.Amount).DefaultIfEmpty(null).Max(),
                    c.SoldTo,
                    c.SoldPrice,
                    payment is null ? null : ToPaymentDto(payment));
            }).ToList();

            return Ok(result);
        }

        /// <summary>
        /// Creates a draft listing and decides what has to happen before it is published:
        /// a free relist token, free-listing mode, or a paid checkout the client must open.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(CreateListingResult), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create(
            [FromBody] CreateListingRequest req,
            [FromQuery] string? returnUrl,
            [FromQuery] string? cancelUrl,
            CancellationToken ct)
        {
            var car = new Car
            {
                OwnerId = UserId,
                OwnerUsername = UserName,
                Status = ListingStatus.Draft,
                CreatedUtc = DateTime.UtcNow
            };

            // One place copies the request onto the entity, so create and update cannot drift.
            req.ApplyTo(car);
            car.Make = car.Make.Trim();
            car.Model = car.Model.Trim();
            car.Country = car.Country.Trim();

            _db.Cars.Add(car);
            await _db.SaveChangesAsync(ct);

            // 1) A free relist: the seller has a paid token from a previous zero-offer auction.
            var reusable = await _db.Payments
                .Where(p => p.UserId == UserId
                            && p.Status == PaymentStatus.Paid
                            && !p.OfferConsumed
                            && p.CarId == null
                            && p.RelistCount < _listing.MaxFreeRelists)
                .OrderBy(p => p.Id)
                .FirstOrDefaultAsync(ct);

            if (reusable is not null)
            {
                reusable.CarId = car.Id;
                reusable.RelistCount++;
                car.Status = ListingStatus.Active;
                car.PublishedUtc ??= DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                return Created($"/api/cars/{car.Id}", new CreateListingResult(
                    "published", CarSummaryDto.From(car, _urls), reusable.Id,
                    reusable.AmountCents, reusable.Currency, null,
                    "Your earlier listing got no offers, so this relist was free."));
            }

            var payment = new Payment
            {
                UserId = UserId,
                Username = UserName,
                CarId = car.Id,
                AmountCents = _listing.ListingFeeCents,
                Currency = _listing.Currency,
                Provider = "paypal"
            };

            // 2) Launch / free-listing mode: no fee configured, publish immediately.
            if (_listing.ListingFeeCents <= 0)
            {
                payment.Status = PaymentStatus.Paid;
                payment.PaidUtc = DateTime.UtcNow;
                car.Status = ListingStatus.Active;
                car.PublishedUtc ??= DateTime.UtcNow;
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync(ct);

                return Created($"/api/cars/{car.Id}", new CreateListingResult(
                    "published", CarSummaryDto.From(car, _urls), payment.Id,
                    payment.AmountCents, payment.Currency, null, "Your listing is now live."));
            }

            // 3) Paid listing: hand the client a checkout URL to open in a browser or web view.
            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(ct);

            var checkoutUrl = await StartCheckoutAsync(payment, car, returnUrl, cancelUrl, ct);

            return Created($"/api/cars/{car.Id}", new CreateListingResult(
                checkoutUrl is null ? "payment_failed" : "payment_required",
                CarSummaryDto.From(car, _urls), payment.Id,
                payment.AmountCents, payment.Currency, checkoutUrl,
                checkoutUrl is null
                    ? "Your listing was saved as a draft, but we could not start the payment. Try again from your listings."
                    : "Open the checkout URL to pay the listing fee and publish this listing."));
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

            var pending = await _db.Payments
                .Where(p => p.CarId == id && p.Status == PaymentStatus.Pending)
                .ToListAsync(ct);
            _db.Payments.RemoveRange(pending);

            // Captured before SaveChanges: once the row is gone nothing records where the
            // blobs were. See PhotoStorageExtensions.DeleteAllAsync for why the cleanup runs
            // after the delete and never before it.
            var photos = car.ImagePaths.ToList();

            _db.Cars.Remove(car);
            await _db.SaveChangesAsync(ct);

            await _photos.DeleteAllAsync(photos, ct);
            return NoContent();
        }

        /// <summary>
        /// Starts (or restarts) the listing-fee checkout for a draft and returns the URL the
        /// client should open. Mobile clients pass their own deep links as returnUrl/cancelUrl.
        /// </summary>
        [HttpPost("{id:int}/checkout")]
        [ProducesResponseType(typeof(CreateListingResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> Checkout(
            int id, [FromQuery] string? returnUrl, [FromQuery] string? cancelUrl, CancellationToken ct)
        {
            var car = await FindOwnedAsync(id, ct);
            if (car is null) return NotFound(new ApiError("Listing not found.", "not_found"));

            if (car.Status != ListingStatus.Draft)
                return BadRequest(new ApiError("This listing is already published.", "already_published"));

            var payment = await _db.Payments
                .Where(p => p.CarId == id && p.Status != PaymentStatus.Failed)
                .OrderByDescending(p => p.Id)
                .FirstOrDefaultAsync(ct);

            if (payment is null)
            {
                payment = new Payment
                {
                    UserId = UserId,
                    Username = UserName,
                    CarId = car.Id,
                    AmountCents = _listing.ListingFeeCents,
                    Currency = _listing.Currency,
                    Provider = "paypal"
                };
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync(ct);
            }

            if (payment.Status == PaymentStatus.Paid)
            {
                car.Status = ListingStatus.Active;
                car.PublishedUtc ??= DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return Ok(new CreateListingResult(
                    "published", CarSummaryDto.From(car, _urls), payment.Id,
                    payment.AmountCents, payment.Currency, null, "This listing is already paid for and live."));
            }

            var checkoutUrl = await StartCheckoutAsync(payment, car, returnUrl, cancelUrl, ct);

            return Ok(new CreateListingResult(
                checkoutUrl is null ? "payment_failed" : "payment_required",
                CarSummaryDto.From(car, _urls), payment.Id,
                payment.AmountCents, payment.Currency, checkoutUrl,
                checkoutUrl is null ? "We could not start the payment. Please try again." : null));
        }

        /// <summary>
        /// Confirms a listing-fee payment after the client returns from the provider. The
        /// redirect itself is never trusted: the capture call is what proves payment, exactly
        /// as the website does it.
        /// </summary>
        [HttpPost("payments/{paymentId:int}/capture")]
        [ProducesResponseType(typeof(ListingPaymentDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> CapturePayment(int paymentId, CancellationToken ct)
        {
            var payment = await _db.Payments.Include(p => p.Car)
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.UserId == UserId, ct);

            if (payment is null) return NotFound(new ApiError("Payment not found.", "not_found"));

            if (payment.Status != PaymentStatus.Paid && payment.ProviderOrderId is not null)
            {
                var captureId = await _pay.CaptureAsync(payment.ProviderOrderId);
                if (captureId is not null)
                {
                    payment.Status = PaymentStatus.Paid;
                    payment.PaidUtc = DateTime.UtcNow;
                    payment.ProviderCaptureId = captureId;
                    if (payment.Car is not null)
                    {
                        payment.Car.Status = ListingStatus.Active;
                        payment.Car.PublishedUtc ??= DateTime.UtcNow;
                    }
                    await _db.SaveChangesAsync(ct);
                }
            }

            return Ok(ToPaymentDto(payment));
        }

        /// <summary>Current fee status for a listing, for polling after a checkout redirect.</summary>
        [HttpGet("{id:int}/payment")]
        [ProducesResponseType(typeof(ListingPaymentDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPaymentStatus(int id, CancellationToken ct)
        {
            var car = await FindOwnedAsync(id, ct);
            if (car is null) return NotFound(new ApiError("Listing not found.", "not_found"));

            var payment = await _db.Payments
                .Where(p => p.CarId == id)
                .OrderByDescending(p => p.Id)
                .FirstOrDefaultAsync(ct);

            if (payment is null) return NotFound(new ApiError("No payment for this listing.", "not_found"));
            return Ok(ToPaymentDto(payment));
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

        private async Task<string?> StartCheckoutAsync(
            Payment payment, Car car, string? returnUrl, string? cancelUrl, CancellationToken ct)
        {
            // Default to the website pages so a browser-based client works with no extra setup.
            var success = string.IsNullOrWhiteSpace(returnUrl)
                ? Url.Action("Success", "Sell", new { paymentId = payment.Id }, Request.Scheme)!
                : returnUrl;

            var cancel = string.IsNullOrWhiteSpace(cancelUrl)
                ? Url.Action("Cancel", "Sell", new { carId = car.Id }, Request.Scheme)!
                : cancelUrl;

            try
            {
                var checkout = await _pay.CreateListingCheckoutAsync(payment, car, success, cancel);
                payment.ProviderOrderId = checkout.OrderId;
                await _db.SaveChangesAsync(ct);
                return checkout.RedirectUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not start listing checkout for payment {PaymentId}.", payment.Id);
                return null;
            }
        }

        private static ListingPaymentDto ToPaymentDto(Payment p) => new(
            p.Id, p.CarId, p.AmountCents, p.Currency, p.Status.ToString(),
            p.OfferConsumed, p.RelistCount, p.CreatedUtc, p.PaidUtc);
    }
}
