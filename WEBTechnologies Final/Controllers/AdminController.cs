using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Marketplace;
using WEBTechnologies_Final.Services.Storage;

namespace WEBTechnologies_Final.Controllers
{
    [AdminOnly]
    public class AdminController : Controller
    {
        private readonly ApiClient _api;
        private readonly IPhotoStorage _photos;
        private readonly AppDbContext _db;
        private readonly PromotionService _promotions;
        private readonly IStringLocalizer<SharedResource> _text;

        public AdminController(
            ApiClient api, IPhotoStorage photos, AppDbContext db, PromotionService promotions,
            IStringLocalizer<SharedResource> text)
        {
            _text = text;
            _api = api;
            _photos = photos;
            _db = db;
            _promotions = promotions;
        }

        public async Task<IActionResult> Index()
        {
            var cars = await _api.GetCarsAsync();
            var stats = await _api.GetStatsAsync();
            ViewData["Stats"] = stats;
            return View(cars);
        }

        public IActionResult Create() =>
            View(new Car { Year = DateTime.UtcNow.Year });

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(UploadLimits.ListingFormBytes)]
        public async Task<IActionResult> Create(Car car, List<IFormFile>? photos)
        {
            if (!ModelState.IsValid) return View(car);
            car.ImagePaths = (await _photos.SaveAsync(photos)).Paths.ToList();
            // Admin "house" listings publish immediately, like everyone else's.
            car.Status = ListingStatus.Active;
            car.PublishedUtc = DateTime.UtcNow;
            await _api.CreateCarAsync(car);
            TempData["Success"] = _text["\"{0}\" was posted to the marketplace.", car.Title].Value;
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var car = await _api.GetCarAsync(id);
            return car is null ? NotFound() : View(car);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(UploadLimits.ListingFormBytes)]
        public async Task<IActionResult> Edit(
            int id, Car car, List<IFormFile>? photos, List<string>? removePhotos)
        {
            if (id != car.Id) return BadRequest();
            if (!ModelState.IsValid) return View(car);

            // Photos are APPENDED, never replaced: an edit that uploads nothing must not wipe
            // the existing set. Removal is explicit, from the checkboxes on the form.
            var upload = await _photos.SaveAsync(photos);
            var result = await _api.UpdateCarAsync(car, upload.Paths, removePhotos);
            if (result is null) return NotFound();

            // Delete the blobs only after the listing no longer references them, so a failed
            // save never leaves a live listing pointing at a file that is already gone.
            foreach (var path in removePhotos ?? new List<string>())
                await _photos.DeleteAsync(path);

            if (upload.Errors.Count > 0) TempData["Error"] = string.Join(" ", upload.Errors);
            TempData["Success"] = _text["{0} was updated.", car.Title].Value;
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int id)
        {
            var car = await _api.GetCarAsync(id);
            return car is null ? NotFound() : View(car);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!await _api.DeleteCarAsync(id)) return NotFound();
            TempData["Success"] = _text["Listing deleted."].Value;
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Takes a listing off the market. Its offers and messages are kept.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            var success = await _api.ArchiveCarAsync(id);
            TempData[success ? "Success" : "Error"] = success
                ? "Listing archived."
                : "Could not archive that listing.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Puts a draft or archived listing back on the market.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            var success = await _api.PublishCarAsync(id);
            TempData[success ? "Success" : "Error"] = success
                ? "Listing published."
                : "Could not publish that listing.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- Paid placement ----------
        //
        // Advertising is the only thing anyone pays for here, and there is no checkout, so
        // for now an admin grants a placement by hand after the money is arranged off-site.
        // That is the honest shape of it: a self-service purchase flow would need a payment
        // provider, an invoice and a refund path, none of which exist. See the pre-release
        // checklist before wiring one up.

        /// <summary>Every listing that can carry advertising, promoted ones first.</summary>
        public async Task<IActionResult> Promotions()
        {
            var cars = await _db.Cars
                .Where(c => c.Status == ListingStatus.Active)
                .OrderByDescending(c => c.PromotionTier)
                .ThenByDescending(c => c.PromotedUntilUtc)
                .ThenByDescending(c => c.Id)
                .ToListAsync();

            // The reference for whatever is running on each listing, so the admin can read a
            // code off this table without opening a receipt. One query for the page rather
            // than one per row.
            var now = DateTime.UtcNow;
            var carIds = cars.Select(c => c.Id).ToList();
            ViewData["References"] = await _db.Promotions
                .AsNoTracking()
                .Where(p => p.CarId != null && carIds.Contains(p.CarId.Value)
                            && p.EndedEarlyUtc == null && p.EndsUtc > now)
                .ToDictionaryAsync(p => p.CarId!.Value, p => p.Reference);

            return View(cars);
        }

        /// <summary>
        /// Looks a placement up by the reference from the seller's receipt.
        ///
        /// This is the whole reason references exist: a seller writes in about "my promotion"
        /// and the only thing they can reliably quote is that code. Searching by car title
        /// fails as soon as they have two similar listings or have since renamed one.
        /// </summary>
        public async Task<IActionResult> Promotion(string? reference)
        {
            ViewData["Reference"] = reference;

            if (string.IsNullOrWhiteSpace(reference))
                return View((Promotion?)null);

            var promotion = await _promotions.FindByReferenceAsync(reference);

            if (promotion is null)
                ViewData["NotFound"] = true;
            else
                ViewData["History"] = await _promotions.HistoryForCarAsync(promotion.CarId ?? 0);

            return View(promotion);
        }

        /// <summary>
        /// Starts or replaces a placement. Always dated: an advert that never lapses can only
        /// be sold once, and someone would eventually have to remember to turn it off.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Promote(int id, PromotionTier tier, int days)
        {
            if (tier == PromotionTier.None)
                return RedirectToAction(nameof(EndPromotion), new { id });

            var promotion = await _promotions.GrantAsync(
                id, tier, days, HttpContext.Session.GetInt32(SessionKeys.UserId));

            if (promotion is null) return NotFound();

            // The reference is in the message because this is the moment somebody has to pass
            // it on: there is no checkout and no automatic receipt yet, so an admin who cannot
            // see the code here has no way to give it to the seller.
            TempData["Success"] = _text[
                "\"{0}\" is now {1} placement until {2}. Reference {3} - send this to the seller.",
                promotion.CarTitle,
                _text[tier == PromotionTier.FrontPage ? "Front page" : "Promoted"].Value,
                AppTime.ToDisplay(promotion.EndsUtc).ToString("dd MMM yyyy"),
                promotion.Reference].Value;

            return RedirectToAction(nameof(Promotions));
        }

        /// <summary>
        /// Ends a placement now. The tier is cleared as well as the date, so the row does not
        /// look like a promotion that merely lapsed.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EndPromotion(int id)
        {
            var ended = await _promotions.EndAsync(id, "Ended by an administrator");
            if (!ended) return NotFound();

            TempData["Success"] = _text["Placement ended. The receipt is kept and records the date."].Value;
            return RedirectToAction(nameof(Promotions));
        }
    }
}
