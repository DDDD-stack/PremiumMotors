using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Marketplace;

namespace WEBTechnologies_Final.Controllers
{
    /// <summary>
    /// The front of the site.
    ///
    /// The root used to redirect straight into the listings grid, which meant a first-time
    /// visitor's opening question - what is this, and why is it not the classifieds site I
    /// already use - was answered by a wall of cars. There are now two front pages instead,
    /// one per audience, and the marketplace is a deliberate second click.
    /// </summary>
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ListingExtrasService _extras;
        private readonly DealershipService _dealerships;

        public HomeController(
            AppDbContext db, ListingExtrasService extras, DealershipService dealerships)
        {
            _db = db;
            _extras = extras;
            _dealerships = dealerships;
        }

        /// <summary>
        /// The consumer front page. Carries three separate routes into the marketplace: the
        /// hero button, the header button that takes over once the hero scrolls away, and the
        /// featured listings - both the cards themselves and the button underneath them.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            // Newest first rather than "best", because there is no honest ranking signal yet
            // and pretending otherwise would just show the same six cars forever.
            var featured = await _db.Cars.AsNoTracking()
                .Where(c => c.Status == ListingStatus.Active)
                .OrderByDescending(c => c.Id)
                .Take(6)
                .ToListAsync();

            await LoadCardExtrasAsync(featured);

            var vm = new HomeLandingViewModel
            {
                Featured = featured,
                Stats = await StatsAsync(),
                IsLoggedIn = HttpContext.Session.GetInt32(SessionKeys.UserId) is not null
            };

            ViewData["HeaderCta"] = true;
            return View(vm);
        }

        /// <summary>
        /// The business front page. A dealer is buying a channel, not a philosophy, so this
        /// leads with margin, tooling and the fact that registering costs nothing.
        /// </summary>
        [Route("business")]
        public async Task<IActionResult> ForBusiness()
        {
            var (dealerships, _) = await _dealerships.DirectoryAsync(
                search: null, city: null, sort: "stock", page: 1, pageSize: 4);

            var userId = HttpContext.Session.GetInt32(SessionKeys.UserId);
            var isBusiness = false;
            if (userId is int id)
            {
                isBusiness = await _db.Users.AsNoTracking()
                    .Where(u => u.Id == id)
                    .Select(u => u.SellerType == SellerType.Dealer)
                    .FirstOrDefaultAsync();
            }

            var vm = new BusinessLandingViewModel
            {
                Dealerships = dealerships,
                Stats = await StatsAsync(),
                IsLoggedIn = userId is not null,
                IsBusinessAccount = isBusiness
            };

            ViewData["HeaderCta"] = true;
            return View(vm);
        }

        /// <summary>
        /// Four counts in one round trip each. Cheap enough to run per request at this size,
        /// and the alternative - caching - would have the front page contradict the grid.
        /// </summary>
        private async Task<MarketplaceStats> StatsAsync() => new()
        {
            ActiveListings = await _db.Cars.CountAsync(c => c.Status == ListingStatus.Active),
            SoldCount = await _db.Cars.CountAsync(c => c.Status == ListingStatus.Sold),
            DealershipCount = await _db.Dealerships.CountAsync(),
            SellerCount = await _db.Users.CountAsync(u => u.IsSeller)
        };

        /// <summary>
        /// The featured strip is built from _CarCard, so it needs exactly what the browse
        /// grid feeds that partial. Without these the cards still render - just without the
        /// seller byline or the price-drop flag.
        /// </summary>
        private async Task LoadCardExtrasAsync(IReadOnlyCollection<Car> cars)
        {
            if (cars.Count == 0) return;

            var extras = await _extras.ForCarsAsync(cars);
            ViewData["SellerBadges"] = extras.Sellers;
            ViewData["PriceDrops"] = extras.PreviousPrices;

            var userId = HttpContext.Session.GetInt32(SessionKeys.UserId);
            if (userId is null) return;

            var ids = cars.Select(c => c.Id).ToList();
            var favourites = await _db.UserFavoriteCars.AsNoTracking()
                .Where(f => f.UserId == userId.Value && ids.Contains(f.CarId))
                .Select(f => f.CarId)
                .ToListAsync();

            foreach (var carId in favourites) ViewData[$"IsFav_{carId}"] = true;
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Terms()
        {
            return View();
        }

        /// <summary>
        /// Explains the private-offer model. First-time visitors arrive expecting either
        /// fixed-price classifieds or an auction and get neither, so leaving it unexplained
        /// makes the site feel broken rather than different.
        /// </summary>
        public IActionResult HowItWorks()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
