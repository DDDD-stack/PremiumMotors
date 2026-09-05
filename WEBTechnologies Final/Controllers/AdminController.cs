using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Storage;

namespace WEBTechnologies_Final.Controllers
{
    [AdminOnly]
    public class AdminController : Controller
    {
        private readonly ApiClient _api;
        private readonly IPhotoStorage _photos;
        private readonly AppDbContext _db;

        public AdminController(ApiClient api, IPhotoStorage photos, AppDbContext db)
        {
            _api = api;
            _photos = photos;
            _db = db;
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
            // Admin "house" listings publish immediately, no listing fee.
            car.Status = ListingStatus.Active;
            car.PublishedUtc = DateTime.UtcNow;
            await _api.CreateCarAsync(car);
            TempData["Success"] = $"\"{car.Title}\" was posted to the marketplace.";
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
            TempData["Success"] = car.Title + " was updated.";
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
            TempData["Success"] = "Listing deleted.";
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

            return View(cars);
        }

        /// <summary>
        /// Starts or replaces a placement. Always dated: an advert that never lapses can only
        /// be sold once, and someone would eventually have to remember to turn it off.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Promote(int id, PromotionTier tier, int days)
        {
            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return NotFound();

            if (tier == PromotionTier.None)
                return RedirectToAction(nameof(EndPromotion), new { id });

            // A year is far longer than anything anyone should be sold in one go, and it is
            // here only to stop a typo in the days box parking a car on the front page
            // indefinitely.
            days = Math.Clamp(days, 1, 365);

            car.PromotionTier = tier;
            car.PromotedUntilUtc = DateTime.UtcNow.AddDays(days);
            await _db.SaveChangesAsync();

            TempData["Success"] =
                $"\"{car.Title}\" is now {tier} placement until " +
                $"{AppTime.ToDisplay(car.PromotedUntilUtc.Value):dd MMM yyyy}.";

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
            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == id);
            if (car is null) return NotFound();

            car.PromotionTier = PromotionTier.None;
            car.PromotedUntilUtc = null;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Placement removed from \"{car.Title}\".";
            return RedirectToAction(nameof(Promotions));
        }
    }
}
