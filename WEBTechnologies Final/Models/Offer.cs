using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// A buyer's offer on a listing. Replaces the old Bid: offers no longer compete on a
    /// clock and are never auto-resolved. The seller reads them, optionally opens a
    /// conversation with the buyer, and then accepts or declines each one explicitly.
    ///
    /// Offers remain private to the seller — a buyer sees only their own.
    /// </summary>
    public class Offer
    {
        public int Id { get; set; }

        [Required]
        public int CarId { get; set; }

        [Required]
        [Range(1, double.MaxValue, ErrorMessage = "Your offer must be greater than zero.")]
        public decimal Amount { get; set; }

        /// <summary>
        /// Stable identity of the buyer. Nullable so a row survives its user being deleted
        /// or anonymized.
        /// </summary>
        public int? BuyerId { get; set; }

        /// <summary>Denormalized display copy of the buyer's username when the offer was made.</summary>
        [Required]
        public string BuyerUsername { get; set; } = string.Empty;

        /// <summary>Optional note the buyer attaches, e.g. "can collect this weekend".</summary>
        [StringLength(1000)]
        public string? Message { get; set; }

        public OfferStatus Status { get; set; } = OfferStatus.Pending;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>When the seller accepted or declined.</summary>
        public DateTime? RespondedUtc { get; set; }

        /// <summary>Optional reason the seller gives with an accept or decline.</summary>
        [StringLength(1000)]
        public string? SellerResponse { get; set; }

        /// <summary>The message thread opened off this offer, if any.</summary>
        public int? ConversationId { get; set; }

        public bool IsPending => Status == OfferStatus.Pending;

        [JsonIgnore]
        public Car? Car { get; set; }

        [JsonIgnore]
        public User? Buyer { get; set; }

        [JsonIgnore]
        public Conversation? Conversation { get; set; }
    }
}
