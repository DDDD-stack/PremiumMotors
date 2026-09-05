using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace WEBTechnologies_Final.Controllers
{
    /// <summary>
    /// The one place a visitor's language choice is stored.
    /// </summary>
    public class CultureController : Controller
    {
        /// <summary>
        /// POST rather than a link, because this writes a cookie that changes every page the
        /// visitor sees afterwards. As a GET it could be triggered by any image tag on any
        /// site, and the victim's next page would silently be in a language they did not pick
        /// with nothing on screen explaining it.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Set(string culture, string? returnUrl)
        {
            // Anything not on the supported list is ignored rather than stored. The
            // middleware would fall back safely on its own, but a cookie holding a language
            // the site does not have is a bug that only shows up as "my choice does not
            // stick", which is a miserable thing to debug from a support email.
            var supported = Services.AppLanguages.Offered
                .Any(c => string.Equals(c, culture, StringComparison.OrdinalIgnoreCase));

            if (supported)
            {
                // A year, because a language preference is not something anyone expects to
                // set twice. Not HttpOnly: it is not a secret, and leaving it readable lets
                // client-side code match the server's choice if it ever needs to.
                Response.Cookies.Append(
                    CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                    new CookieOptions
                    {
                        Expires = DateTimeOffset.UtcNow.AddYears(1),
                        IsEssential = true,
                        SameSite = SameSiteMode.Lax,
                        Path = "/"
                    });
            }

            // Never trust the return path from a form post: an absolute URL here would turn
            // the language switcher into an open redirect on somebody else's domain.
            return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl!) : RedirectToAction("Index", "Home");
        }
    }
}
