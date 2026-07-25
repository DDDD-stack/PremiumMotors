using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WEBTechnologies_Final.Services
{

    public class AdminOnlyAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var isAdmin = context.HttpContext.Session.GetString(SessionKeys.IsAdmin) == "true";
            if (!isAdmin)
            {
                context.Result = new RedirectToActionResult(
                    "Login", "Account",
                    new { returnUrl = context.HttpContext.Request.Path });
            }

            base.OnActionExecuting(context);
        }
    }
}
