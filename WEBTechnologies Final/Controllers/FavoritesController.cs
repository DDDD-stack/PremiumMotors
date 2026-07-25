using Microsoft.AspNetCore.Mvc;
using WEBTechnologies_Final.Services;

namespace WEBTechnologies_Final.Controllers
{
    [LoggedInOnly]
    public class FavoritesController : Controller
    {
        private readonly ApiClient _api;

        public FavoritesController(ApiClient api) => _api = api;

        private string CurrentUser => HttpContext.Session.GetString(SessionKeys.Username)!;

        public async Task<IActionResult> Index()
        {
            var ids = await _api.GetFavoriteIdsAsync(CurrentUser);
            var cars = new List<Models.Car>();
            foreach (var id in ids)
            {
                var car = await _api.GetCarAsync(id);
                if (car is not null) cars.Add(car);
            }
            return View(cars.OrderByDescending(c => c.CreatedUtc).ToList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id, string? returnUrl = null)
        {
            var car = await _api.GetCarAsync(id);
            if (car is null) return NotFound();

            var nowFavorite = await _api.ToggleFavoriteAsync(CurrentUser, id);
            TempData["Success"] = nowFavorite
                ? "Added to your favourites."
                : "Removed from your favourites.";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Details", "Cars", new { id });
        }
    }
}
