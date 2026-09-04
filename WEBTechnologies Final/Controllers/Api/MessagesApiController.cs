using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WEBTechnologies_Final.Models.Dtos;
using WEBTechnologies_Final.Services;
using WEBTechnologies_Final.Services.Auth;
using WEBTechnologies_Final.Services.Marketplace;

namespace WEBTechnologies_Final.Controllers.Api
{
    /// <summary>
    /// Buyer/seller messaging for mobile and any future seller front-end.
    ///
    /// PLACEHOLDER SCOPE — delivery is polling, not push. A client should re-fetch the thread
    /// on open and on foreground; there is no websocket and no push notification yet. The
    /// unread count endpoint exists so a client can badge the tab without pulling every thread.
    /// See docs/MESSAGING.md for what has to land before this is production messaging.
    /// </summary>
    [ApiController]
    [Route("api/v1/conversations")]
    [Produces("application/json")]
    [Authorize]
    public class MessagesApiController : ControllerBase
    {
        private readonly ConversationService _conversations;
        private readonly ICurrentUser _current;
        private readonly IMediaUrlResolver _urls;

        public MessagesApiController(
            ConversationService conversations, ICurrentUser current, IMediaUrlResolver urls)
        {
            _conversations = conversations;
            _current = current;
            _urls = urls;
        }

        private int UserId => _current.UserId!.Value;

        /// <summary>Every thread the caller takes part in, most recently active first.</summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<ConversationSummaryDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetConversations(CancellationToken ct)
        {
            var rows = await _conversations.ListForUserAsync(UserId, ct);

            return Ok(rows.Select(r => new ConversationSummaryDto(
                r.Id, r.CarId, r.CarTitle, _urls.Resolve(r.CarImage), r.ViewerRole,
                r.LastMessage, r.LastSender, r.LastMessageUtc, r.UnreadCount, r.IsClosed)).ToList());
        }

        /// <summary>Badge count for the messages tab.</summary>
        [HttpGet("unread-count")]
        [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetUnreadCount(CancellationToken ct) =>
            Ok(new { count = await _conversations.UnreadCountAsync(UserId, ct) });

        /// <summary>Opens or continues the thread for one buyer on one listing.</summary>
        [HttpPost]
        [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Open([FromBody] OpenConversationRequest req, CancellationToken ct)
        {
            var result = await _conversations.OpenAsync(
                req.CarId, req.BuyerId ?? UserId, UserId, _current.IsAdmin, ct);

            if (!result.Success)
                return result.Code == MarketplaceCodes.NotFound
                    ? NotFound(new ApiError(result.Error!, result.Code))
                    : StatusCode(StatusCodes.Status403Forbidden, new ApiError(result.Error!, result.Code!));

            return await GetConversation(result.Value!.Id, ct);
        }

        /// <summary>One thread with its messages. Reading it marks the other side's messages read.</summary>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(ConversationDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetConversation(int id, CancellationToken ct)
        {
            var conversation = await _conversations.FindAsync(id, ct);
            if (conversation is null)
                return NotFound(new ApiError("Conversation not found.", "not_found"));

            // A thread the caller is not part of is reported as missing rather than forbidden,
            // so the endpoint cannot be used to probe which listings have active chats.
            if (!ConversationService.CanAccess(conversation, UserId, _current.IsAdmin))
                return NotFound(new ApiError("Conversation not found.", "not_found"));

            await _conversations.MarkReadAsync(id, UserId, ct);

            return Ok(new ConversationDto(
                conversation.Id,
                conversation.CarId,
                conversation.Car?.Title ?? "Listing",
                _urls.Resolve(conversation.Car?.PrimaryImage ?? "/img/no-image.svg"),
                conversation.BuyerId,
                conversation.SellerId,
                conversation.BuyerId == UserId ? "buyer" : "seller",
                conversation.IsClosed,
                conversation.Messages.Select(MessageDto.From).ToList()));
        }

        [HttpPost("{id:int}/messages")]
        [ProducesResponseType(typeof(MessageDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Send(int id, [FromBody] SendMessageRequest req, CancellationToken ct)
        {
            var result = await _conversations.PostAsync(
                id, UserId, _current.Username ?? string.Empty, req.Body, _current.IsAdmin, ct);

            if (!result.Success)
                return result.Code switch
                {
                    MarketplaceCodes.NotFound => NotFound(new ApiError(result.Error!, result.Code)),
                    MarketplaceCodes.Forbidden =>
                        StatusCode(StatusCodes.Status403Forbidden, new ApiError(result.Error!, result.Code)),
                    _ => BadRequest(new ApiError(result.Error!, result.Code!))
                };

            return CreatedAtAction(nameof(GetConversation), new { id }, MessageDto.From(result.Value!));
        }
    }
}
