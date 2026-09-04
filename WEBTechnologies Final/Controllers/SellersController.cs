using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Marketplace;

namespace WEBTechnologies_Final.Controllers
{
    /// <summary>
    /// Public pages for PRIVATE sellers, and the flow for leaving a seller a review.
    ///
    /// Dealers get the richer shopfront at /Dealerships/{slug}; a private seller gets the same
    /// page without the trading details. Reviews live here for both kinds, because a review
    /// targets the seller's account rather than their shopfront - see ReviewService.
    /// </summary>
    public class SellersController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ReviewService _reviews;
        private readonly ListingExtrasService _extras;
        private readonly DealershipService _dealerships;

        public SellersController(
            AppDbContext db, ReviewService reviews, ListingExtrasService extras,
            DealershipService dealerships)
        {
            _db = db;
            _reviews = reviews;
            _extras = extras;
            _dealerships = dealerships;
        }

        // Every action here carries an explicit template, and that is not optional. A bare
        // [Route("seller/{username}")] silently swallowed /Seller/Dashboard — attribute
        // routes beat conventional ones, so "Dashboard" bound as a username and the whole
        // seller panel started returning 404. Pinning all three, method and all, leaves no
        // pattern loose enough to catch another controller's URLs.
        [HttpGet("sellers/{username}")]
        public async Task<IActionResult> Profile(string username, string tab = "stock")
        {
            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username);
            if (user is null) return NotFound();

            // A dealer's public identity is their shopfront. Two public pages for the same
            // business would split the reviews visually and the search engines would pick one.
            var dealership = await _dealerships.ForOwnerAsync(user.Id);
            if (dealership is not null)
                return RedirectToActionPermanent("Details", "Dealerships", new { slug = dealership.Slug });

            var cars = await _db.Cars
                .Where(c => c.OwnerId == user.Id && c.Status != ListingStatus.Draft
                            && c.Status != ListingStatus.Archived)
                .OrderByDescending(c => c.Status == ListingStatus.Active)
                .ThenByDescending(c => c.Id)
                .ToListAsync();

            await LoadFavouritesAsync(cars.Select(c => c.Id));
            var extras = await _extras.ForCarsAsync(cars);
            ViewData["SellerBadges"] = extras.Sellers;
            ViewData["PriceDrops"] = extras.PreviousPrices;

            var vm = new PublicSellerViewModel
            {
                OwnerId = user.Id,
                Username = user.Username,
                DisplayName = user.SellerName,
                SellerType = user.SellerType,
                AvatarPath = user.AvatarPath,
                Location = user.SellerLocation,
                MemberSince = user.SellerSinceUtc ?? user.RegisteredUtc,
                Verified = user.SellerVerified,
                RatingAverage = user.RatingAverage,
                RatingCount = user.RatingCount,
                Cars = cars,
                Reviews = await _reviews.ForSellerAsync(user.Id),
                Distribution = await _reviews.DistributionAsync(user.Id),
                Tab = tab
            };

            return View(vm);
        }

        /// <summary>
        /// Leave a review for a car you bought. Eligibility is checked here for the form and
        /// again in ReviewService for the post, because a GET check alone protects nothing.
        /// </summary>
        [HttpGet("sellers/review/{carId:int}")]
        [LoggedInOnly]
        public async Task<IActionResult> Review(int carId)
        {
            var userId = HttpContext.Session.GetInt32(SessionKeys.UserId)!.Value;

            var car = await _db.Cars.AsNoTracking().FirstOrDefaultAsync(c => c.Id == carId);
            if (car is null) return NotFound();

            if (car.Status != ListingStatus.Sold || car.SoldToUserId != userId)
            {
                TempData["Error"] = "You can only review a seller you actually bought a car from.";
                return RedirectToAction("Purchases", "Account");
            }

            if (await _db.SellerReviews.AnyAsync(r => r.CarId == carId))
            {
                TempData["Error"] = "You have already reviewed this purchase.";
                return RedirectToAction("Purchases", "Account");
            }

            return View(await BuildFormAsync(car));
        }

        [HttpPost("sellers/review/{carId:int}")]
        [LoggedInOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Review(LeaveReviewViewModel vm)
        {
            var userId = HttpContext.Session.GetInt32(SessionKeys.UserId)!.Value;

            var result = await _reviews.LeaveAsync(vm.CarId, userId, vm.Rating, vm.Comment);

            if (!result.Success)
            {
                var car = await _db.Cars.AsNoTracking().FirstOrDefaultAsync(c => c.Id == vm.CarId);
                if (car is null) return NotFound();

                ModelState.AddModelError(string.Empty, result.Error!);
                var form = await BuildFormAsync(car);
                form.Rating = vm.Rating;
                form.Comment = vm.Comment;
                return View(form);
            }

            TempData["Success"] = "Thanks — your review is now on the seller's profile.";
            return RedirectToAction("Purchases", "Account");
        }

        private async Task<LeaveReviewViewModel> BuildFormAsync(Car car)
        {
            var seller = car.OwnerId is int id
                ? await _extras.ForSellerAsync(id)
                : null;

            return new LeaveReviewViewModel
            {
                CarId = car.Id,
                CarTitle = car.Title,
                CarImage = car.PrimaryImage,
                SellerName = seller?.Name ?? car.OwnerUsername ?? "the seller",
                SellerIsDealer = seller?.IsDealer ?? false,
                SoldPrice = car.SoldPrice,
                SoldUtc = car.SoldUtc
            };
        }

        private async Task LoadFavouritesAsync(IEnumerable<int> carIds)
        {
            var userId = HttpContext.Session.GetInt32(SessionKeys.UserId);
            if (userId is null) return;

            var ids = carIds.ToList();
            if (ids.Count == 0) return;

            var favourites = await _db.UserFavoriteCars
                .Where(f => f.UserId == userId.Value && ids.Contains(f.CarId))
                .Select(f => f.CarId)
                .ToListAsync();

            foreach (var carId in favourites) ViewData[$"IsFav_{carId}"] = true;
        }
    }
}
