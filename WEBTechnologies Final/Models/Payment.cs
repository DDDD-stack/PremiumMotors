namespace WEBTechnologies_Final.Models
{
    public enum PaymentStatus
    {
        Pending,   // Order created with the provider, awaiting capture/confirmation.
        Paid,      // Fee captured — the attached listing may be published.
        Failed,    // Checkout failed or was abandoned.
        Refunded   // Money returned (rare; refunds are normally handled as free relists, not cash).
    }

    /// <summary>
    /// A paid listing token. One token funds one published listing. If that listing's
    /// auction closes with zero offers, the token is freed (OfferConsumed stays false)
    /// and can be reused for a free relist. The moment a listing receives its first
    /// offer the token is consumed and cannot be reclaimed — which is what closes the
    /// "never declare a winner to relist forever" exploit.
    /// </summary>
    /// LEGACY, AND NOTHING WRITES TO IT ANY MORE.
    ///
    /// This recorded a one-time listing fee taken through PayPal. Both are gone as of
    /// 5 September 2026: listing is free and always will be, and PayPal was removed because
    /// getting money out of it in Albania is the hard part. The table is kept rather than
    /// dropped so no migration destroys rows, and because whatever payment provider is chosen
    /// next will want somewhere very like this to record a charge.
    ///
    /// Paid ADVERTISING is recorded in Promotion, not here.
    public class Payment
    {
        public int Id { get; set; }

        // Stable identity of the seller who owns this token.
        public int? UserId { get; set; }

        // Denormalized display copy of the seller's username.
        public string Username { get; set; } = string.Empty;

        // The listing this token is currently applied to (null once a closed listing releases it).
        public int? CarId { get; set; }

        public long AmountCents { get; set; }
        public string Currency { get; set; } = "eur";

        public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

        // Set true on the listing's first received offer — token can no longer be reclaimed.
        public bool OfferConsumed { get; set; }

        // How many times this token has been reused for a free relist (capped by config).
        public int RelistCount { get; set; }

        // Payment provider identifier (e.g. "paypal") and its references for this charge.
        public string Provider { get; set; } = string.Empty;
        public string? ProviderOrderId { get; set; }
        public string? ProviderCaptureId { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? PaidUtc { get; set; }

        public Car? Car { get; set; }
    }
}
