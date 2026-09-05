using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Marketplace;

namespace WEBTechnologies_Final.Controllers
{
    /// <summary>
    /// Browsing dealers rather than cars.
    ///
    /// Some buyers shop by vehicle and some shop by who they are buying from - especially for
    /// a used car, where the seller is most of the risk. The directory serves the second kind,
    /// and a dealership page doubles as the link a dealer can put on their own advertising.
    /// </summary>
    public class DealershipsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly DealershipService _dealerships;
        private readonly ReviewService _reviews;
        private readonly ListingExtrasService _extras;

        public DealershipsController(
            AppDbContext db, DealershipService dealerships, ReviewService reviews,
            ListingExtrasService extras)
        {
            _db = db;
            _dealerships = dealerships;
            _reviews = reviews;
            _extras = extras;
        }

        // Explicit routes so a dealership's URL is /dealerships/adriatik-motors rather than
        // /Dealerships/Details?slug=... — this is the link a dealer prints on a business card,
        // and it has to be readable and stable.
        [Route("dealerships")]
        public async Task<IActionResult> Index(
            string? search, string? city, string? sort, int page = 1, int pageSize = 24)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 1 or > 96 ? 24 : pageSize;

            var (items, total) = await _dealerships.DirectoryAsync(search, city, sort, page, pageSize);

            var vm = new DealershipListViewModel
            {
                Dealerships = items,
                Search = search,
                City = city,
                Sort = sort ?? "stock",
                Page = page,
                PageSize = pageSize,
                TotalCount = total,
                Cities = await _dealerships.CitiesAsync()
            };

            return View(vm);
        }

        [Route("dealerships/{slug}")]
        public async Task<IActionResult> Details(string slug, string tab = "stock")
        {
            var dealership = await _dealerships.BySlugAsync(slug);
            if (dealership is null || dealership.Owner is null) return NotFound();

            var owner = dealership.Owner;

            var cars = await _db.Cars
                .Where(c => c.OwnerId == owner.Id && c.Status != ListingStatus.Draft
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
                Dealership = dealership,
                OwnerId = owner.Id,
                Username = owner.Username,
                DisplayName = dealership.Name,
                SellerType = owner.SellerType,
                AvatarPath = dealership.LogoPath ?? owner.AvatarPath,
                MemberSince = owner.SellerSinceUtc ?? owner.RegisteredUtc,
                RatingAverage = owner.RatingAverage,
                RatingCount = owner.RatingCount,
                Cars = cars,
                Reviews = await _reviews.ForSellerAsync(owner.Id),
                Distribution = await _reviews.DistributionAsync(owner.Id),
                Tab = tab
            };

            return View(vm);
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
