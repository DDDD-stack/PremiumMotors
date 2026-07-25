using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WEBTechnologies_Final.Services
{

    public class LoggedInOnlyAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var username = context.HttpContext.Session.GetString(SessionKeys.Username);
            if (string.IsNullOrEmpty(username))
            {
                var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
                context.Result = new RedirectToActionResult(
                    "Login", "Account", new { returnUrl });
            }

            base.OnActionExecuting(context);
        }
    }
}
