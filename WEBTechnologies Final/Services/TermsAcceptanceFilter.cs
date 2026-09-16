using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Asks a signed-in user to accept the current Terms and Privacy Policy when the version
    /// they accepted is not the one published now.
    ///
    /// Registration records a version against every account, and that record is only worth
    /// something if a change of version is acted on. Without this, everyone who registered
    /// before a change goes on using the site under documents they never saw.
    ///
    /// Only signed-in website sessions are stopped. Visitors who are not signed in agree by
    /// registering, as before; API clients carry no session and are out of scope here. The
    /// check reads the database once per session per version - the answer is cached in the
    /// session - so an ordinary page view costs nothing.
    ///
    /// While the question is open, the user can still read the documents, change language,
    /// sign out, download their data and erase their account. Being asked to accept terms you
    /// are not allowed to read would be absurd, and leaving must never require agreeing first.
    /// </summary>
    public sealed class TermsAcceptanceFilter : IAsyncActionFilter
    {
        /// <summary>The version this session has already confirmed the user accepted.</summary>
        public const string AcceptedKey = "TermsVersionOk";

        private static readonly HashSet<(string Controller, string Action)> AlwaysAllowed = new()
        {
            ("Account", "AcceptTerms"),
            ("Account", "Logout"),
            // The rights to a copy of your data and to erasure do not depend on accepting
            // new terms. Somebody who will not accept must still be able to leave cleanly.
            ("Account", "Security"),
            ("Account", "ExportData"),
            ("Account", "DeleteAccount"),
            ("Home", "Terms"),
            ("Home", "Privacy"),
            ("Home", "Error"),
            ("Culture", "Set")
        };

        private readonly AppDbContext _db;

        public TermsAcceptanceFilter(AppDbContext db) => _db = db;

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;

            if (http.Request.Path.StartsWithSegments("/api")
                || http.Session.GetInt32(SessionKeys.UserId) is not int userId
                || http.Session.GetString(AcceptedKey) == LegalDocuments.Version
                || IsAllowed(context))
            {
                await next();
                return;
            }

            var accepted = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.TermsVersion)
                .FirstOrDefaultAsync(http.RequestAborted);

            if (accepted == LegalDocuments.Version)
            {
                http.Session.SetString(AcceptedKey, LegalDocuments.Version);
                await next();
                return;
            }

            // Come back to where they were going once they have accepted - but only for a GET.
            // Replaying a form post after an interruption would be a surprise nobody wants.
            var returnUrl = HttpMethods.IsGet(http.Request.Method)
                ? http.Request.Path + http.Request.QueryString
                : null;

            context.Result = new RedirectToActionResult("AcceptTerms", "Account", new { returnUrl });
        }

        private static bool IsAllowed(ActionExecutingContext context) =>
            context.ActionDescriptor is ControllerActionDescriptor action
            && AlwaysAllowed.Contains((action.ControllerName, action.ActionName));
    }
}
