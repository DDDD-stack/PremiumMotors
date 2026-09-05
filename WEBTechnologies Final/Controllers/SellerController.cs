using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Marketplace;
using WEBTechnologies_Final.Services.Storage;

namespace WEBTechnologies_Final.Controllers
{
    /// <summary>
    /// The seller panel: dashboard, listings, offer inbox and seller profile.
    ///
    /// PLACEHOLDER / ARCHITECTURE NOTE. This ships as pages inside the main site, but every
    /// action here is a thin wrapper over <see cref="SellerService"/> and
    /// <see cref="OfferService"/>, and the same operations are exposed over HTTP by
    /// SellerApiController under /api/v1/seller. So the panel can later be lifted out to its
    /// own front-end on its own domain without touching the domain logic — that front-end
    /// would call the API and this controller would simply be deleted. Keep the two in step:
    /// nothing that matters should live in this file.
    /// </summary>
    [LoggedInOnly]
    public class SellerController : Controller
    {
        private readonly AppDbContext _db;
        private readonly SellerService _sellers;
        private readonly OfferService _offers;
        private readonly ApiClient _api;
        private readonly IPhotoStorage _photos;
        private readonly ProfileNavService _nav;
        private readonly DealershipService _dealerships;
        private readonly ReviewService _reviews;
        private readonly SellerAnalyticsService _analytics;

        public SellerController(
            AppDbContext db, SellerService sellers, OfferService offers,
            ApiClient api, IPhotoStorage photos, ProfileNavService nav,
            DealershipService dealerships, ReviewService reviews, SellerAnalyticsService analytics)
        {
            _db = db;
            _sellers = sellers;
            _offers = offers;
            _api = api;
            _photos = photos;
            _nav = nav;
            _dealerships = dealerships;
            _reviews = reviews;
            _analytics = analytics;
        }

        /// <summary>Counts for the shared profile sub-navigation these views render.</summary>
        private Task LoadNavAsync() => _nav.PopulateAsync(ViewData, UserId);

        private int UserId => HttpContext.Session.GetInt32(SessionKeys.UserId)!.Value;
        private bool IsAdmin => HttpContext.Session.GetString(SessionKeys.IsAdmin) == "true";
        private bool IsSellerAccount => IsAdmin || HttpContext.Session.GetString(SessionKeys.IsSeller) == "true";

        // ---------- Opting in ----------

        [HttpGet]
        public async Task<IActionResult> Start()
        {
            if (IsSellerAccount) return RedirectToAction(nameof(Dashboard));

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            return View(new BecomeSellerViewModel { DisplayName = user?.Username });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Start(BecomeSellerViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            // Always Private: a dealer registers through "Register as a business", which
            // collects the business record up front, so nobody reaching this form is one.
            var result = await _sellers.BecomeSellerAsync(
                UserId, SellerType.Private, vm.DisplayName, vm.Location);

            if (!result.Success)
            {
                TempData["Error"] = result.Error;
                return RedirectToAction(nameof(Dashboard));
            }

            // The panel is gated on the session flag, so it has to be refreshed here or the
            // user would be bounced straight back to this page.
            HttpContext.Session.SetString(SessionKeys.IsSeller, "true");

            TempData["Success"] = "Your seller panel is unlocked. Post your first car whenever you're ready.";
            return RedirectToAction(nameof(Dashboard));
        }

        // ---------- Panel ----------

        [SellerOnly]
        public async Task<IActionResult> Dashboard()
        {
            await LoadNavAsync();
            ViewData["Dashboard"] = await _sellers.GetDashboardAsync(UserId);
            ViewData["Recent"] = await _sellers.GetOffersAsync(UserId, OfferStatus.Pending);
            return View();
        }

        [SellerOnly]
        public async Task<IActionResult> Listings(ListingStatus? status)
        {
            await LoadNavAsync();
            ViewData["StatusFilter"] = status;
            return View(await _sellers.GetListingsAsync(UserId, status));
        }

        [SellerOnly]
        public async Task<IActionResult> Offers(OfferStatus? status)
        {
            await LoadNavAsync();
            ViewData["StatusFilter"] = status;

            var offers = await _sellers.GetOffersAsync(UserId, status);

            // So each row can link straight to the buyer thread.
            var carIds = offers.Select(o => o.CarId).Distinct().ToList();
            ViewData["Threads"] = await _db.Conversations
                .Where(c => carIds.Contains(c.CarId))
                .ToDictionaryAsync(c => c.CarId + ":" + c.BuyerId, c => c.Id);

            return View(offers);
        }

        [HttpPost]
        [SellerOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptOffer(int offerId, string? response, string? returnUrl)
        {
            var result = await _offers.AcceptAsync(offerId, UserId, IsAdmin, response);

            TempData[result.Success ? "Success" : "Error"] = result.Success
                ? "Offer accepted. The car is reserved and the buyer's contact details are now on the listing."
                : result.Error;

            return Back(returnUrl);
        }

        [HttpPost]
        [SellerOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeclineOffer(int offerId, string? response, string? returnUrl)
        {
            var result = await _offers.DeclineAsync(offerId, UserId, IsAdmin, response);

            TempData[result.Success ? "Success" : "Error"] =
                result.Success ? "Offer declined." : result.Error;

            return Back(returnUrl);
        }

        [HttpPost]
        [SellerOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkSold(int carId, string? returnUrl)
        {
            var result = await _offers.MarkSoldAsync(carId, UserId, IsAdmin);

            TempData[result.Success ? "Success" : "Error"] =
                result.Success ? "Listing marked as sold." : result.Error;

            return Back(returnUrl);
        }

        [HttpPost]
        [SellerOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reopen(int carId, string? returnUrl)
        {
            var result = await _offers.ReopenAsync(carId, UserId, IsAdmin);

            TempData[result.Success ? "Success" : "Error"] =
                result.Success ? "Listing is back on the market." : result.Error;

            return Back(returnUrl);
        }

        [HttpPost]
        [SellerOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int carId, string? returnUrl)
        {
            var car = await OwnedAsync(carId);
            if (car is null) return Denied(returnUrl);

            car.Status = ListingStatus.Archived;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Listing archived. Buyers can no longer see it.";
            return Back(returnUrl);
        }

        [HttpPost]
        [SellerOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Relist(int carId, string? returnUrl)
        {
            var car = await OwnedAsync(carId);
            if (car is null) return Denied(returnUrl);

            car.Status = ListingStatus.Active;
            car.PublishedUtc ??= DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["Success"] = car.Title + " is live again.";
            return Back(returnUrl);
        }

        // ---------- Editing a listing ----------

        /// <summary>
        /// Sellers could not edit their own listings at all — the only edit form was the admin
        /// one. A seller who mistyped a price had to delete and re-post, losing the listing and
        /// its offers.
        /// </summary>
        [HttpGet]
        [SellerOnly]
        public async Task<IActionResult> Edit(int id)
        {
            var car = await OwnedAsync(id);
            if (car is null) return NotFound();
            return View(car);
        }

        [HttpPost]
        [SellerOnly]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(UploadLimits.ListingFormBytes)]
        public async Task<IActionResult> Edit(
            int id, Car car, List<IFormFile>? photos, List<string>? removePhotos)
        {
            if (id != car.Id) return BadRequest();

            // Re-check ownership on POST: the GET check is not a permission, it is a lookup.
            var owned = await OwnedAsync(id);
            if (owned is null) return NotFound();

            if (!ModelState.IsValid)
            {
                // Keep the stored photos on the redisplayed form; they are not posted back.
                car.ImagePaths = owned.ImagePaths;
                return View(car);
            }

            var upload = await _photos.SaveAsync(photos);
            var result = await _api.UpdateCarAsync(car, upload.Paths, removePhotos);
            if (result is null) return NotFound();

            foreach (var path in removePhotos ?? new List<string>())
                await _photos.DeleteAsync(path);

            if (upload.Errors.Count > 0) TempData["Error"] = string.Join(" ", upload.Errors);
            TempData["Success"] = car.Title + " was updated.";
            return RedirectToAction("Details", "Cars", new { id });
        }

        // ---------- Profile ----------

        [HttpGet]
        [SellerOnly]
        public async Task<IActionResult> Profile()
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null) return NotFound();

            await LoadNavAsync();
            return View(new SellerProfileViewModel
            {
                SellerType = user.SellerType,
                DisplayName = user.SellerDisplayName,
                Location = user.SellerLocation,
                PublicPhone = user.PublicPhone,
                IsBusiness = user.IsBusiness,
                SellerSinceUtc = user.SellerSinceUtc
            });
        }

        [HttpPost]
        [SellerOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(SellerProfileViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            // SellerType is not editable from the profile form — see SellerProfileViewModel.
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            var result = await _sellers.UpdateProfileAsync(
                UserId, user?.SellerType ?? SellerType.Private, vm.DisplayName, vm.Location,
                vm.PublicPhone);

            TempData[result.Success ? "Success" : "Error"] =
                result.Success ? "Seller profile updated." : result.Error;

            return RedirectToAction(nameof(Profile));
        }

        // ---------- helpers ----------

        private async Task<Car?> OwnedAsync(int carId)
        {
            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == carId);
            if (car is null) return null;
            return IsAdmin || car.OwnerId == UserId ? car : null;
        }

        private IActionResult Denied(string? returnUrl)
        {
            TempData["Error"] = "This is not your listing.";
            return Back(returnUrl);
        }

        // Only ever redirects to a local URL: returnUrl arrives from a form field, and an
        // open redirect would let a phishing page bounce through our domain.
        private IActionResult Back(string? returnUrl) =>
            !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? Redirect(returnUrl)
                : RedirectToAction(nameof(Offers));

        // ---------- Analytics ----------

        /// <summary>
        /// How the seller's listings are actually performing.
        ///
        /// The dashboard answers "what needs my attention today"; this answers "is any of
        /// this working". Views, offers, sales and revenue over time, plus a per-listing
        /// table, because the useful question is almost always which car is the problem
        /// rather than whether there is one.
        /// </summary>
        [HttpGet]
        [SellerOnly]
        public async Task<IActionResult> Analytics(int months = 12)
        {
            months = months is < 3 or > 24 ? 12 : months;

            await LoadNavAsync();
            ViewData["Months"] = months;
            ViewData["Dealership"] = await _dealerships.ForOwnerAsync(UserId);
            return View(await _analytics.ForSellerAsync(UserId, months));
        }

        // ---------- Reviews ----------

        [HttpGet]
        [SellerOnly]
        public async Task<IActionResult> Reviews()
        {
            await LoadNavAsync();

            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null) return NotFound();

            var dealership = await _dealerships.ForOwnerAsync(UserId);

            return View(new PublicSellerViewModel
            {
                Dealership = dealership,
                OwnerId = user.Id,
                Username = user.Username,
                DisplayName = dealership?.Name ?? user.SellerName,
                SellerType = user.SellerType,
                AvatarPath = dealership?.LogoPath ?? user.AvatarPath,
                Location = user.SellerLocation,
                MemberSince = user.SellerSinceUtc ?? user.RegisteredUtc,
                RatingAverage = user.RatingAverage,
                RatingCount = user.RatingCount,
                Reviews = await _reviews.ForSellerAsync(user.Id),
                Distribution = await _reviews.DistributionAsync(user.Id),
                Tab = "reviews"
            });
        }

        [HttpPost]
        [SellerOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReplyToReview(int reviewId, string reply)
        {
            var result = await _reviews.ReplyAsync(reviewId, UserId, reply ?? string.Empty);
            TempData[result.Success ? "Success" : "Error"] =
                result.Success ? "Your reply was posted." : result.Error;
            return RedirectToAction(nameof(Reviews));
        }

        // ---------- Dealership shopfront ----------

        [HttpGet]
        [SellerOnly]
        public async Task<IActionResult> Dealership()
        {
            await LoadNavAsync();

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null) return NotFound();

            // Only business accounts have a shopfront. A private seller reaching this URL is
            // told what to do rather than shown an empty form they cannot use.
            if (user.SellerType != SellerType.Dealer)
            {
                TempData["Error"] =
                    "Dealership pages are for business accounts. Register a business account to get one.";
                return RedirectToAction("Profile", "Account");
            }

            var dealership = await _dealerships.EnsureForAsync(user);
            return View(dealership);
        }

        [HttpPost]
        [SellerOnly]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(UploadLimits.ListingFormBytes)]
        public async Task<IActionResult> Dealership(
            Dealership form, IFormFile? logo, IFormFile? banner,
            bool removeLogo = false, bool removeBanner = false)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null || user.SellerType != SellerType.Dealer) return NotFound();

            var dealership = await _dealerships.EnsureForAsync(user);

            if (!ModelState.IsValid)
            {
                await LoadNavAsync();
                return View(dealership);
            }

            // Assigned field by field. Binding the whole entity would let a crafted post
            // rewrite OwnerUserId, Slug or Verified — the three things on this record that a
            // dealer must never be able to set for themselves.
            dealership.Name = form.Name;
            dealership.About = form.About;
            dealership.City = form.City;
            dealership.Country = form.Country;
            dealership.Address = form.Address;
            dealership.Phone = form.Phone;
            dealership.Website = form.Website;
            dealership.OpeningHours = form.OpeningHours;

            await ApplyImageAsync(logo, removeLogo,
                () => dealership.LogoPath, path => dealership.LogoPath = path);
            await ApplyImageAsync(banner, removeBanner,
                () => dealership.BannerPath, path => dealership.BannerPath = path);

            await _db.SaveChangesAsync();

            TempData["Success"] = "Your dealership page was updated.";
            return RedirectToAction(nameof(Dealership));
        }

        /// <summary>
        /// Shared by the logo and the banner: replace, remove, or leave alone. The old blob is
        /// only deleted once the new path is safely in hand, so a failed upload never leaves
        /// the page with a missing image.
        /// </summary>
        private async Task ApplyImageAsync(
            IFormFile? file, bool remove, Func<string?> get, Action<string?> set)
        {
            var previous = get();

            if (remove)
            {
                set(null);
                if (previous is not null) await _photos.DeleteAsync(previous);
                return;
            }

            if (file is null || file.Length == 0) return;

            var upload = await _photos.SaveAsync(new[] { file });
            if (upload.Paths.Count == 0)
            {
                if (upload.Errors.Count > 0) TempData["Error"] = string.Join(" ", upload.Errors);
                return;
            }

            set(upload.Paths[0]);
            if (previous is not null) await _photos.DeleteAsync(previous);
        }

    }
}
