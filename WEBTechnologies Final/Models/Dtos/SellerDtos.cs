using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models.Dtos
{
    /// <summary>
    /// Contracts for the seller panel. These exist so the panel can be built as a separate
    /// front-end on its own domain later: everything /Seller/* does through the MVC views is
    /// available here over HTTP with a bearer token.
    /// </summary>
    public record SellerProfileDto(
        int UserId,
        string Username,
        bool IsSeller,
        SellerType SellerType,
        string? DisplayName,
        string? Location,
        bool IsVerified,
        DateTime? SellerSinceUtc);

    public class BecomeSellerRequest
    {
        public SellerType SellerType { get; set; } = SellerType.Private;

        [StringLength(80)] public string? DisplayName { get; set; }
        [StringLength(80)] public string? Location { get; set; }
    }

    public class UpdateSellerProfileRequest : BecomeSellerRequest { }

    public record SellerDashboardDto(
        int ActiveListings,
        int Drafts,
        int Reserved,
        int Sold,
        int PendingOffers,
        int TotalOffers,
        int UnreadMessages,
        decimal SoldValue);

    // ---------- Messaging ----------

    public record ConversationSummaryDto(
        int Id,
        int CarId,
        string CarTitle,
        string CarImage,
        string ViewerRole,
        string? LastMessage,
        string? LastSender,
        DateTime LastMessageUtc,
        int UnreadCount,
        bool IsClosed);

    public record MessageDto(
        int Id,
        int ConversationId,
        int? SenderId,
        string SenderUsername,
        string Body,
        DateTime SentUtc,
        DateTime? ReadUtc)
    {
        public static MessageDto From(Message m) => new(
            m.Id, m.ConversationId, m.SenderId, m.SenderUsername, m.Body, m.SentUtc, m.ReadUtc);
    }

    public record ConversationDto(
        int Id,
        int CarId,
        string CarTitle,
        string CarImage,
        int BuyerId,
        int? SellerId,
        string ViewerRole,
        bool IsClosed,
        IReadOnlyList<MessageDto> Messages);

    public record SendMessageRequest([Required][StringLength(2000)] string Body);

    public record OpenConversationRequest(int CarId, int? BuyerId);
}
