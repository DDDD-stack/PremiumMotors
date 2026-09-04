using Microsoft.AspNetCore.Mvc;
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

        public AdminController(ApiClient api, IPhotoStorage photos)
        {
            _api = api;
            _photos = photos;
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
    }
}
