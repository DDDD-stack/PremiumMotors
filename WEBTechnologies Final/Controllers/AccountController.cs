using Microsoft.AspNetCore.Mvc;
using WEBTechnologies_Final.Models;
using WEBTechnologies_Final.Services;

namespace WEBTechnologies_Final.Controllers
{
    public class AccountController : Controller
    {
        private const string AdminUsername = "admin";
        private const string AdminPassword = "admin123";

        private readonly ApiClient _api;

        public AccountController(ApiClient api) => _api = api;

        [HttpGet]
        public IActionResult Login(string? returnUrl = null) =>
            View(new LoginViewModel { ReturnUrl = returnUrl });

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            if (model.Username == AdminUsername && model.Password == AdminPassword)
            {
                SignIn(AdminUsername, isAdmin: true);
                return RedirectToLocalOr(model.ReturnUrl, "Admin", "Index");
            }

            var (user, error) = await _api.ValidateAsync(model.Username, model.Password);
            if (user is not null)
            {
                SignIn(user.Username, isAdmin: false);
                return RedirectToLocalOr(model.ReturnUrl, "Cars", "Index");
            }

            ModelState.AddModelError(string.Empty, error ?? "Invalid username or password.");
            return View(model);
        }

        [HttpGet]
        public IActionResult Register() => View(new RegisterViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var (user, error) = await _api.RegisterAsync(model.Username, model.Email, model.Phone, model.Password);
            if (user is null)
            {
                ModelState.AddModelError(string.Empty, error ?? "Registration failed.");
                return View(model);
            }

            SignIn(user.Username, isAdmin: false);
            TempData["Success"] = $"Welcome, {user.Username}! Your account has been created.";
            return RedirectToAction("Index", "Cars");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Cars");
        }

        private void SignIn(string username, bool isAdmin)
        {
            HttpContext.Session.SetString(SessionKeys.Username, username);
            HttpContext.Session.SetString(SessionKeys.IsAdmin, isAdmin ? "true" : "false");
        }

        private IActionResult RedirectToLocalOr(string? returnUrl, string controller, string action)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction(action, controller);
        }
    }
}
