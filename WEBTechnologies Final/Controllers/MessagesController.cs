using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Marketplace;

namespace WEBTechnologies_Final.Controllers
{
    /// <summary>
    /// Buyer/seller messaging.
    ///
    /// PLACEHOLDER SCOPE — the thread is real (messages persist, access is enforced), but
    /// delivery is request-response only: you see new messages when the page loads. Realtime
    /// push, unread notifications by email, attachments and abuse reporting are all still to
    /// come. See docs/MESSAGING.md.
    /// </summary>
    [LoggedInOnly]
    public class MessagesController : Controller
    {
        private readonly ConversationService _conversations;
        private readonly ProfileNavService _nav;
        private readonly IStringLocalizer<SharedResource> _text;

        public MessagesController(
            ConversationService conversations, ProfileNavService nav,
            IStringLocalizer<SharedResource> text)
        {
            _text = text;
            _conversations = conversations;
            _nav = nav;
        }

        private int UserId => HttpContext.Session.GetInt32(SessionKeys.UserId)!.Value;
        private string Username => HttpContext.Session.GetString(SessionKeys.Username)!;
        private bool IsAdmin => HttpContext.Session.GetString(SessionKeys.IsAdmin) == "true";

        public async Task<IActionResult> Index()
        {
            await _nav.PopulateAsync(ViewData, UserId);
            return View(await _conversations.ListForUserAsync(UserId));
        }

        public async Task<IActionResult> Thread(int id)
        {
            var conversation = await _conversations.FindAsync(id);
            if (conversation is null) return NotFound();

            if (!ConversationService.CanAccess(conversation, UserId, IsAdmin)) return NotFound();

            // Opening the thread is what marks the other side's messages read.
            await _conversations.MarkReadAsync(id, UserId);

            ViewData["ViewerId"] = UserId;
            ViewData["ViewerRole"] = conversation.BuyerId == UserId ? "buyer" : "seller";
            return View(conversation);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Send(int id, string body)
        {
            var result = await _conversations.PostAsync(id, UserId, Username, body, IsAdmin);

            if (!result.Success)
            {
                if (result.Code == MarketplaceCodes.NotFound) return NotFound();
                TempData["Error"] = _text[result.Error!].Value;
            }

            return RedirectToAction(nameof(Thread), new { id });
        }
    }
}
