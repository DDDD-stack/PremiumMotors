using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Gates the seller panel. A signed-in buyer is redirected to the opt-in page rather than
    /// being told "forbidden" — becoming a seller is a one-form self-service step, not a
    /// permission someone has to be granted.
    ///
    /// Admins pass through so they can see the panel while supporting a seller.
    /// </summary>
    public class SellerOnlyAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var session = context.HttpContext.Session;
            var userId = session.GetInt32(SessionKeys.UserId);

            if (userId is null)
            {
                var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
                context.Result = new RedirectToActionResult(
                    "Login", "Account", new { returnUrl });
                return;
            }

            if (session.GetString(SessionKeys.IsAdmin) == "true") return;
            if (session.GetString(SessionKeys.IsSeller) == "true") return;

            context.Result = new RedirectToActionResult("Start", "Seller", null);
        }
    }
}
