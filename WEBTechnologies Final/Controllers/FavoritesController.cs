using Microsoft.AspNetCore.Mvc;
using WEBTechnologies_Final.Services;

namespace WEBTechnologies_Final.Controllers
{
    [LoggedInOnly]
    public class FavoritesController : Controller
    {
        private readonly ApiClient _api;
        private readonly ProfileNavService _nav;

        public FavoritesController(ApiClient api, ProfileNavService nav)
        {
            _api = api;
            _nav = nav;
        }

        // Favourites are keyed by the stable user id, so a rename keeps them intact.
        private int CurrentUserId => HttpContext.Session.GetInt32(SessionKeys.UserId)!.Value;

        public async Task<IActionResult> Index()
        {
            // One query, not one per favourite. Supabase is a remote database, so a loop of
            // round trips costs far more here than it appears to locally.
            var cars = await _api.GetFavoriteCarsAsync(CurrentUserId);

            // Favourites are part of the profile area, so they render the same sub-navigation.
            await _nav.PopulateAsync(ViewData, CurrentUserId);

            // One flag per card, so the heart renders filled without a query per card.
            foreach (var car in cars) ViewData[$"IsFav_{car.Id}"] = true;

            return View(cars);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id, string? returnUrl = null)
        {
            var car = await _api.GetCarAsync(id);
            if (car is null) return NotFound();

            var nowFavorite = await _api.ToggleFavoriteAsync(CurrentUserId, id);
            TempData["Success"] = nowFavorite
                ? "Added to your favourites."
                : "Removed from your favourites.";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Details", "Cars", new { id });
        }
    }
}
