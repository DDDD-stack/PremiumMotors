using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// A message thread between one buyer and the seller of one listing, so the seller can
    /// ask questions before accepting or declining an offer.
    ///
    /// PLACEHOLDER SCOPE: this is a working store-and-display thread. It deliberately does
    /// not yet do realtime delivery, push/email notification, attachments, typing state,
    /// moderation or abuse reporting. See docs/MESSAGING.md before extending it.
    /// </summary>
    public class Conversation
    {
        public int Id { get; set; }

        [Required]
        public int CarId { get; set; }

        /// <summary>The offer this thread was opened from, when it was opened from one.</summary>
        public int? OfferId { get; set; }

        [Required]
        public int BuyerId { get; set; }

        /// <summary>
        /// Null for an admin "house" listing, which has no owning user row. Such a thread is
        /// visible to any admin.
        /// </summary>
        public int? SellerId { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Denormalized so the inbox can sort without joining Messages.</summary>
        public DateTime LastMessageUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Set when the listing sells or the seller ends the conversation.</summary>
        public bool IsClosed { get; set; }

        [JsonIgnore]
        public Car? Car { get; set; }

        [JsonIgnore]
        public List<Message> Messages { get; set; } = new();
    }

    public class Message
    {
        public int Id { get; set; }

        [Required]
        public int ConversationId { get; set; }

        /// <summary>Nullable so a message survives its author being deleted.</summary>
        public int? SenderId { get; set; }

        [Required]
        public string SenderUsername { get; set; } = string.Empty;

        [Required(ErrorMessage = "Type a message before sending.")]
        [StringLength(2000)]
        public string Body { get; set; } = string.Empty;

        public DateTime SentUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Null until the other participant opens the thread.</summary>
        public DateTime? ReadUtc { get; set; }

        [JsonIgnore]
        public Conversation? Conversation { get; set; }
    }
}
