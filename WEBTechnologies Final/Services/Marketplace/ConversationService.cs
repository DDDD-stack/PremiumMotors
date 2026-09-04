using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// Buyer/seller messaging around a listing.
    ///
    /// PLACEHOLDER SCOPE. This stores and returns messages correctly and enforces who may
    /// read a thread, which is the part that would be a security problem to fake. What it
    /// deliberately does not do yet: realtime delivery (the views poll on navigation only),
    /// push or email notification of a new message, attachments, block/report, and any
    /// moderation. docs/MESSAGING.md lists what has to land before this is production
    /// messaging rather than a working stub.
    /// </summary>
    public class ConversationService
    {
        private readonly AppDbContext _db;

        public ConversationService(AppDbContext db) => _db = db;

        /// <summary>
        /// The thread for one buyer on one listing, created on first use. Either participant
        /// may open it; an admin may open any thread on a listing they administer.
        /// </summary>
        public async Task<MarketplaceResult<Conversation>> OpenAsync(
            int carId, int buyerId, int actorId, bool actorIsAdmin, CancellationToken ct = default)
        {
            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == carId, ct);
            if (car is null)
                return MarketplaceResult<Conversation>.Fail("Listing not found.", MarketplaceCodes.NotFound);

            var isSeller = actorIsAdmin || (car.OwnerId is not null && car.OwnerId == actorId);
            if (!isSeller && actorId != buyerId)
                return MarketplaceResult<Conversation>.Fail(
                    "You are not part of this conversation.", MarketplaceCodes.Forbidden);

            var existing = await _db.Conversations
                .FirstOrDefaultAsync(c => c.CarId == carId && c.BuyerId == buyerId, ct);

            if (existing is not null) return MarketplaceResult<Conversation>.Ok(existing);

            var conversation = new Conversation
            {
                CarId = carId,
                BuyerId = buyerId,
                SellerId = car.OwnerId,
                CreatedUtc = DateTime.UtcNow,
                LastMessageUtc = DateTime.UtcNow
            };

            _db.Conversations.Add(conversation);
            await _db.SaveChangesAsync(ct);
            return MarketplaceResult<Conversation>.Ok(conversation);
        }

        /// <summary>True if the user may read and post to this thread.</summary>
        public static bool CanAccess(Conversation c, int userId, bool isAdmin) =>
            isAdmin || c.BuyerId == userId || (c.SellerId is not null && c.SellerId == userId);

        public async Task<Conversation?> FindAsync(int conversationId, CancellationToken ct = default) =>
            await _db.Conversations
                .Include(c => c.Car)
                .Include(c => c.Messages.OrderBy(m => m.SentUtc))
                .FirstOrDefaultAsync(c => c.Id == conversationId, ct);

        /// <summary>Every thread the user takes part in, most recently active first.</summary>
        public async Task<List<ConversationSummary>> ListForUserAsync(
            int userId, CancellationToken ct = default)
        {
            var rows = await _db.Conversations
                .Include(c => c.Car)
                .Where(c => c.BuyerId == userId || c.SellerId == userId)
                .OrderByDescending(c => c.LastMessageUtc)
                .Select(c => new
                {
                    Conversation = c,
                    Last = c.Messages.OrderByDescending(m => m.SentUtc)
                        .Select(m => new { m.Body, m.SentUtc, m.SenderUsername })
                        .FirstOrDefault(),
                    // Unread = sent by the other participant and not yet opened by this user.
                    Unread = c.Messages.Count(m => m.ReadUtc == null && m.SenderId != userId)
                })
                .ToListAsync(ct);

            return rows.Select(r => new ConversationSummary(
                r.Conversation.Id,
                r.Conversation.CarId,
                r.Conversation.Car?.Title ?? "Listing",
                r.Conversation.Car?.PrimaryImage ?? "/img/no-image.svg",
                r.Conversation.BuyerId == userId ? "buyer" : "seller",
                r.Last?.Body,
                r.Last?.SenderUsername,
                r.Conversation.LastMessageUtc,
                r.Unread,
                r.Conversation.IsClosed)).ToList();
        }

        /// <summary>Posts a message and stamps the thread so the inbox re-sorts.</summary>
        public async Task<MarketplaceResult<Message>> PostAsync(
            int conversationId, int senderId, string senderUsername, string body,
            bool senderIsAdmin = false, CancellationToken ct = default)
        {
            body = (body ?? string.Empty).Trim();
            if (body.Length == 0)
                return MarketplaceResult<Message>.Fail("Type a message before sending.", MarketplaceCodes.EmptyMessage);
            if (body.Length > 2000) body = body[..2000];

            var conversation = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == conversationId, ct);
            if (conversation is null)
                return MarketplaceResult<Message>.Fail("Conversation not found.", MarketplaceCodes.NotFound);

            if (!CanAccess(conversation, senderId, senderIsAdmin))
                return MarketplaceResult<Message>.Fail(
                    "You are not part of this conversation.", MarketplaceCodes.Forbidden);

            if (conversation.IsClosed)
                return MarketplaceResult<Message>.Fail(
                    "This conversation is closed.", MarketplaceCodes.ConversationClosed);

            var message = new Message
            {
                ConversationId = conversationId,
                SenderId = senderId,
                SenderUsername = senderUsername,
                Body = body,
                SentUtc = DateTime.UtcNow
            };

            _db.Messages.Add(message);
            conversation.LastMessageUtc = message.SentUtc;
            await _db.SaveChangesAsync(ct);

            return MarketplaceResult<Message>.Ok(message);
        }

        /// <summary>Marks everything the other participant sent as read.</summary>
        public async Task MarkReadAsync(int conversationId, int readerId, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            await _db.Messages
                .Where(m => m.ConversationId == conversationId && m.SenderId != readerId && m.ReadUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadUtc, now), ct);
        }

        public async Task<int> UnreadCountAsync(int userId, CancellationToken ct = default) =>
            await _db.Messages.CountAsync(
                m => m.ReadUtc == null
                     && m.SenderId != userId
                     && (m.Conversation!.BuyerId == userId || m.Conversation.SellerId == userId), ct);
    }

    public record ConversationSummary(
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
}
