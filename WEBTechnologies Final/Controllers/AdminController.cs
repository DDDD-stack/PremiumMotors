using Microsoft.AspNetCore.Mvc;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;

namespace WEBTechnologies_Final.Controllers
{
    [AdminOnly]
    public class AdminController : Controller
    {
        private readonly ApiClient _api;
        private readonly PhotoService _photos;

        public AdminController(ApiClient api, PhotoService photos)
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
        public async Task<IActionResult> Create(Car car, List<IFormFile>? photos)
        {
            if (!ModelState.IsValid) return View(car);
            car.ImagePaths = await _photos.SaveAsync(photos);
            car.IsPublished = true; // Admin "house" listings publish immediately, no listing fee.
            await _api.CreateCarAsync(car);
            TempData["Success"] = $"\"{car.Title}\" was posted to the auction.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            var car = await _api.GetCarAsync(id);
            return car is null ? NotFound() : View(car);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Car car, List<IFormFile>? photos)
        {
            if (id != car.Id) return BadRequest();
            if (!ModelState.IsValid) return View(car);
            car.ImagePaths = await _photos.SaveAsync(photos);
            var result = await _api.UpdateCarAsync(car);
            if (result is null) return NotFound();
            TempData["Success"] = $"\"{car.Title}\" was updated.";
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Close(int id)
        {
            var success = await _api.CloseAuctionAsync(id);
            TempData[success ? "Success" : "Error"] = success
                ? "Auction closed successfully."
                : "Could not close the auction.";
            return RedirectToAction(nameof(Index));
        }
    }
}
