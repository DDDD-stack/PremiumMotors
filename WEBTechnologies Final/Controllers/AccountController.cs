using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Auth;
using WEBTechnologies_Final.Services.Marketplace;
using WEBTechnologies_Final.Services.Storage;

namespace WEBTechnologies_Final.Controllers
{
    public class AccountController : Controller
    {
        private readonly AccountService _accounts;
        private readonly AppDbContext _db;
        private readonly TokenService _tokens;
        private readonly ProfileNavService _nav;
        private readonly IPhotoStorage _photos;
        private readonly DealershipService _dealerships;

        public AccountController(
            AccountService accounts, AppDbContext db, TokenService tokens, ProfileNavService nav,
            IPhotoStorage photos, DealershipService dealerships)
        {
            _accounts = accounts;
            _db = db;
            _tokens = tokens;
            _nav = nav;
            _photos = photos;
            _dealerships = dealerships;
        }

        private int? UserId => HttpContext.Session.GetInt32(SessionKeys.UserId);

        // ---------------------------------------------------------------- sign in

        [HttpGet]
        public IActionResult Login(string? returnUrl = null) =>
            View(new LoginViewModel { ReturnUrl = returnUrl });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            // Admin is an ordinary account row with the Admin role, rather than a hardcoded
            // username/password pair, so it has a user id and works over the API too.
            var result = await _accounts.ValidateAsync(model.Username, model.Password);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Invalid username or password.");
                return View(model);
            }

            var user = result.User!;
            SignIn(user);

            return user.IsAdmin
                ? RedirectToLocalOr(model.ReturnUrl, "Admin", "Index")
                : RedirectToLocalOr(model.ReturnUrl, "Cars", "Index");
        }

        // ---------------------------------------------------------------- sign up

        /// <summary>
        /// The fork in the road. Registration is split by WHO is signing up rather than asking
        /// later, so a dealer gives their business details once, at signup, and never sees the
        /// "are you a private seller or a dealer?" question again.
        /// </summary>
        [HttpGet]
        public IActionResult Signup() => View();

        [HttpGet]
        public IActionResult Register() => View(new RegisterViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _accounts.RegisterAsync(
                model.Username, model.Email, model.Phone, model.Password);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Registration failed.");
                return View(model);
            }

            SignIn(result.User!);
            TempData["Success"] = $"Welcome, {result.User!.Username}. Your account is ready.";
            return RedirectToAction("Index", "Cars");
        }

        [HttpGet]
        public IActionResult RegisterBusiness() => View(new RegisterBusinessViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterBusiness(RegisterBusinessViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _accounts.RegisterBusinessAsync(model);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Registration failed.");
                return View(model);
            }

            // Every business account gets a public shopfront immediately, so a dealer
            // appears in the directory from the moment they register rather than only once
            // somebody remembers to create one for them.
            await _dealerships.EnsureForAsync(result.User!);

            SignIn(result.User!);
            TempData["Success"] =
                $"Welcome, {result.User!.SellerName}. Your dealer account is ready — your seller panel and your dealership page are already live.";
            return RedirectToAction("Dashboard", "Seller");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Cars");
        }

        // ---------------------------------------------------------------- profile

        /// <summary>
        /// The profile hub. Everything about "me" lives under /Account, including the seller
        /// panel, which is reached through the same sub-navigation rather than being a separate
        /// area of the site.
        /// </summary>
        [HttpGet]
        [LoggedInOnly]
        public async Task<IActionResult> Profile()
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null) return SignOutAndHome();

            await LoadProfileCountsAsync(user);
            return View(ToProfileVm(user));
        }

        [HttpPost]
        [LoggedInOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(ProfileViewModel model)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null) return SignOutAndHome();

            if (!ModelState.IsValid)
            {
                await LoadProfileCountsAsync(user);
                return View(model);
            }

            var result = await _accounts.UpdateProfileAsync(user.Id, model.Email, model.Phone);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Could not save your details.");
                await LoadProfileCountsAsync(user);
                return View(model);
            }

            TempData["Success"] = "Your details were saved.";
            return RedirectToAction(nameof(Profile));
        }

        /// <summary>
        /// Sets or clears the account's profile picture.
        ///
        /// It is its own action rather than a field on the profile form, because a file input
        /// forces the whole form to multipart and a failed upload would then take the email
        /// and phone edits down with it. Separate actions fail separately.
        /// </summary>
        [HttpPost]
        [LoggedInOnly]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(UploadLimits.AvatarBytes)]
        public async Task<IActionResult> Avatar(IFormFile? avatar, bool remove = false)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null) return SignOutAndHome();

            var previous = user.AvatarPath;

            if (remove)
            {
                user.AvatarPath = null;
                await _db.SaveChangesAsync();
                if (previous is not null) await _photos.DeleteAsync(previous);
                TempData["Success"] = "Your profile picture was removed.";
                return RedirectToAction(nameof(Profile));
            }

            if (avatar is null || avatar.Length == 0)
            {
                TempData["Error"] = "Choose an image first.";
                return RedirectToAction(nameof(Profile));
            }

            // Reuses the listing photo pipeline, so an avatar gets the same magic-byte type
            // check, the same size cap and the same local/Supabase switch. One upload path
            // means one place where those rules can be wrong.
            var upload = await _photos.SaveAsync(new[] { avatar });
            if (upload.Paths.Count == 0)
            {
                TempData["Error"] = upload.Errors.Count > 0
                    ? string.Join(" ", upload.Errors)
                    : "That file could not be used as a picture.";
                return RedirectToAction(nameof(Profile));
            }

            user.AvatarPath = upload.Paths[0];
            await _db.SaveChangesAsync();

            // Only after the new one is safely stored: deleting first would leave the account
            // with no picture at all if the save failed.
            if (previous is not null) await _photos.DeleteAsync(previous);

            TempData["Success"] = "Your profile picture was updated.";
            return RedirectToAction(nameof(Profile));
        }

        /// <summary>Offers this account has made on other people's cars.</summary>
        [HttpGet]
        [LoggedInOnly]
        public async Task<IActionResult> Offers()
        {
            var offers = await _db.Offers
                .Include(o => o.Car)
                .Where(o => o.BuyerId == UserId)
                .OrderBy(o => o.Status == OfferStatus.Pending ? 0 : 1)
                .ThenByDescending(o => o.CreatedUtc)
                .ToListAsync();

            var carIds = offers.Select(o => o.CarId).Distinct().ToList();
            ViewData["Threads"] = await _db.Conversations
                .Where(c => c.BuyerId == UserId && carIds.Contains(c.CarId))
                .ToDictionaryAsync(c => c.CarId, c => c.Id);

            await LoadProfileCountsAsync(null);
            return View(offers);
        }

        /// <summary>Cars this account bought — an offer of theirs was accepted.</summary>
        [HttpGet]
        [LoggedInOnly]
        public async Task<IActionResult> Purchases()
        {
            var cars = await _db.Cars
                .Where(c => c.SoldToUserId == UserId
                            && (c.Status == ListingStatus.Reserved || c.Status == ListingStatus.Sold))
                .OrderByDescending(c => c.SoldUtc ?? c.CreatedUtc)
                .ToListAsync();

            // The seller's contact details, which acceptance released to this buyer.
            var sellerIds = cars.Where(c => c.OwnerId is not null).Select(c => c.OwnerId!.Value).Distinct().ToList();
            ViewData["Sellers"] = await _db.Users
                .Where(u => sellerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u);

            // Which of these the buyer has already reviewed, so a sold car offers the review
            // link exactly once and then stops asking.
            var boughtIds = cars.Select(x => x.Id).ToList();
            ViewData["Reviewed"] = await _db.SellerReviews
                .Where(r => r.CarId != null && boughtIds.Contains(r.CarId.Value))
                .Select(r => r.CarId!.Value)
                .ToListAsync();

            await LoadProfileCountsAsync(null);
            return View(cars);
        }

        [HttpGet]
        [LoggedInOnly]
        public async Task<IActionResult> Security()
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null) return SignOutAndHome();

            ViewData["Sessions"] = await _tokens.ListSessionsAsync(user.Id, null);
            await LoadProfileCountsAsync(user);
            return View(new ChangePasswordViewModel());
        }

        [HttpPost]
        [LoggedInOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null) return SignOutAndHome();

            if (!ModelState.IsValid)
            {
                ViewData["Sessions"] = await _tokens.ListSessionsAsync(user.Id, null);
                await LoadProfileCountsAsync(user);
                return View(nameof(Security), model);
            }

            var result = await _accounts.ChangePasswordAsync(
                user.Id, model.CurrentPassword, model.NewPassword);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Could not change your password.");
                ViewData["Sessions"] = await _tokens.ListSessionsAsync(user.Id, null);
                await LoadProfileCountsAsync(user);
                return View(nameof(Security), model);
            }

            // Changing a password signs out every other device, which is the whole point of
            // changing it after a scare.
            await _tokens.RevokeAllAsync(user.Id);

            TempData["Success"] = "Your password was changed and every other device was signed out.";
            return RedirectToAction(nameof(Security));
        }

        // ---------------------------------------------------------------- business

        [HttpGet]
        [LoggedInOnly]
        public async Task<IActionResult> Business()
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null) return SignOutAndHome();

            // A personal account has no business record to show; send them somewhere useful
            // instead of rendering an empty form they cannot meaningfully fill in.
            if (!user.IsBusiness)
            {
                TempData["Error"] = "This is a personal account. Business details apply to dealer accounts.";
                return RedirectToAction(nameof(Profile));
            }

            await LoadProfileCountsAsync(user);
            return View(new BusinessDetailsViewModel
            {
                BusinessName = user.SellerDisplayName ?? user.Username,
                RegistrationNumber = user.BusinessRegistrationNumber ?? string.Empty,
                VatNumber = user.VatNumber,
                Address = user.BusinessAddress ?? string.Empty,
                Location = user.SellerLocation,
                Website = user.Website,
                ContactName = user.ContactName ?? string.Empty,
                IsVerified = user.SellerVerified
            });
        }

        [HttpPost]
        [LoggedInOnly]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Business(BusinessDetailsViewModel model)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
            if (user is null) return SignOutAndHome();
            if (!user.IsBusiness) return RedirectToAction(nameof(Profile));

            if (!ModelState.IsValid)
            {
                await LoadProfileCountsAsync(user);
                return View(model);
            }

            var result = await _accounts.UpdateBusinessAsync(user.Id, model);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Error ?? "Could not save your business details.");
                await LoadProfileCountsAsync(user);
                return View(model);
            }

            TempData["Success"] = "Your business details were saved.";
            return RedirectToAction(nameof(Business));
        }

        // ---------------------------------------------------------------- helpers

        private void SignIn(User user)
        {
            HttpContext.Session.SetInt32(SessionKeys.UserId, user.Id);
            HttpContext.Session.SetString(SessionKeys.Username, user.Username);
            HttpContext.Session.SetString(SessionKeys.IsAdmin, user.IsAdmin ? "true" : "false");
            HttpContext.Session.SetString(SessionKeys.IsSeller, user.IsSeller ? "true" : "false");
        }

        /// <summary>
        /// A session that points at a user row that no longer exists (deleted account, wiped
        /// database) must not throw on every profile page — clear it and start over.
        /// </summary>
        private IActionResult SignOutAndHome()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Cars");
        }

        private async Task LoadProfileCountsAsync(User? user)
        {
            if (UserId is int id) await _nav.PopulateAsync(ViewData, id, user);
        }

        private ProfileViewModel ToProfileVm(User user) => new()
        {
            Username = user.Username,
            Email = user.Email,
            Phone = user.Phone,
            IsSeller = user.IsSeller,
            IsBusiness = user.IsBusiness,
            EmailVerified = user.IsEmailVerified,
            RegisteredUtc = user.RegisteredUtc,
            AvatarPath = user.AvatarPath,
            RatingAverage = user.RatingAverage,
            RatingCount = user.RatingCount
        };

        private IActionResult RedirectToLocalOr(string? returnUrl, string controller, string action)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(action, controller);
        }
    }
}
