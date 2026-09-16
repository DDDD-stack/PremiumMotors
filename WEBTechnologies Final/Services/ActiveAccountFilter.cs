using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Ends every website session belonging to an account that has been erased or disabled.
    ///
    /// The website's sign-in is a session holding a user id, and every access check reads only
    /// the session. So an account erased from one browser stayed signed in, and fully usable,
    /// in every other browser it was open in - still able to post, message and make offers as
    /// the anonymised "deleted_user_N". The same applied to an account an administrator had
    /// disabled. An erasure that the person's own phone does not notice is not an erasure.
    ///
    /// The account is re-read at most once a minute per session rather than on every request,
    /// so a normal page view costs nothing extra. A minute is the longest an ended account can
    /// linger, which is an acceptable bound for both cases this exists for.
    /// </summary>
    public sealed class ActiveAccountFilter : IAsyncActionFilter
    {
        /// <summary>Unix seconds of the last successful check, stored in the session.</summary>
        public const string CheckedKey = "AccountCheckedAt";

        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

        private readonly AppDbContext _db;

        public ActiveAccountFilter(AppDbContext db) => _db = db;

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;

            // API requests carry a JWT, not a session, and are refused at the token layer.
            if (!http.Request.Path.StartsWithSegments("/api")
                && http.Session.GetInt32(SessionKeys.UserId) is int userId)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var last = long.TryParse(http.Session.GetString(CheckedKey), out var t) ? t : 0;

                if (now - last >= Interval.TotalSeconds)
                {
                    var active = await _db.Users.AsNoTracking()
                        .AnyAsync(u => u.Id == userId && u.IsActive && u.AnonymizedUtc == null,
                            http.RequestAborted);

                    if (!active)
                    {
                        http.Session.Clear();
                        context.Result = new RedirectToActionResult("Index", "Cars", null);
                        return;
                    }

                    http.Session.SetString(CheckedKey, now.ToString());
                }
            }

            await next();
        }
    }
}
