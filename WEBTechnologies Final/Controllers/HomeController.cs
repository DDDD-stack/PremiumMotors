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
        /// Signed in already? Then the front pages have nothing to say to you.
        ///
        /// Both landing pages exist to answer "what is this and why should I use it", and
        /// somebody who has an account has already answered that. Making them scroll past the
        /// pitch every time they open the site is the thing that makes a marketplace feel like
        /// a brochure. They get the marketplace instead.
        ///
        /// The check is the MVC session, which is the only login state these pages have: the
        /// JWT is API-only and nothing sets an auth cookie. Sessions are stored in Postgres,
        /// so this survives an app restart - a returning visitor stays "signed in" and keeps
        /// skipping the pitch, which is the point.
        /// </summary>
        private bool IsSignedIn => HttpContext.Session.GetInt32(SessionKeys.UserId) is not null;

        /// <summary>
        /// The consumer front page. Carries three separate routes into the marketplace: the
        /// hero button, the header button that takes over once the hero scrolls away, and the
        /// featured listings - both the cards themselves and the button underneath them.
        ///
        /// Signed-out visitors only - see <see cref="IsSignedIn"/>. That is also why the view
        /// has no "if logged in" branches: they could never render.
        /// </summary>
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Index()
        {
            if (IsSignedIn) return RedirectToAction("Index", "Cars");

            // Newest first rather than "best", because there is no honest ranking signal yet
            // and pretending otherwise would just show the same six cars forever.
            var featured = await _db.Cars.AsNoTracking()
                .Where(c => c.Status == ListingStatus.Active)
                .OrderByDescending(c => c.Id)
                .Take(6)
                .ToListAsync();

            var promoted = await FrontPagePromotionsAsync();

            await LoadCardExtrasAsync(featured.Concat(promoted).DistinctBy(c => c.Id).ToList());

            var vm = new HomeLandingViewModel
            {
                Featured = featured,
                Promoted = promoted,
                Stats = await StatsAsync()
            };

            ViewData["HeaderCta"] = true;
            return View(vm);
        }

        /// <summary>
        /// The business front page. A dealer is buying a channel, not a philosophy, so this
        /// leads with margin, tooling and the fact that registering costs nothing.
        ///
        /// Signed-out visitors only, same as the consumer page.
        ///
        /// KNOWN TRADE-OFF: a signed-in private seller who wants to read the dealer pitch
        /// before upgrading cannot get to it either - they are bounced to the marketplace like
        /// everyone else. That is the instructed behaviour and it is deliberate, but it does
        /// close the one path a private seller had to talk themselves into a business account.
        /// If that conversion matters later, the fix is a bypass here, not a change of default.
        /// </summary>
        [Route("business")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ForBusiness()
        {
            if (IsSignedIn) return RedirectToAction("Index", "Cars");

            var (dealerships, _) = await _dealerships.DirectoryAsync(
                search: null, city: null, sort: "stock", page: 1, pageSize: 4);

            var promoted = await FrontPagePromotionsAsync();
            await LoadCardExtrasAsync(promoted);

            var vm = new BusinessLandingViewModel
            {
                Dealerships = dealerships,
                Promoted = promoted,
                Stats = await StatsAsync()
            };

            ViewData["HeaderCta"] = true;
            return View(vm);
        }

        /// <summary>
        /// The top advertising tier: listings placed on both front pages.
        ///
        /// Three at most. The value of this slot is that it is nearly the only thing on the
        /// page competing for attention, and it stops being worth paying for the moment it
        /// becomes a wall — so the scarcity is the product, not a limitation.
        /// </summary>
        private Task<List<Car>> FrontPagePromotionsAsync() =>
            _db.Cars.AsNoTracking()
                .WherePromoted(PromotionTier.FrontPage, DateTime.UtcNow)
                .OrderByPromotion()
                .Take(3)
                .ToListAsync();

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
