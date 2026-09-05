using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// One row per paid placement ever granted - the receipt.
    ///
    /// Car.PromotionTier and Car.PromotedUntilUtc answer "is this listing promoted right now",
    /// which is what every marketplace query needs and what an index can serve. They cannot
    /// answer "what did this seller buy, when, and for how much", because starting a new
    /// placement overwrites them and the previous one vanishes without trace.
    ///
    /// That gap is a problem the moment money is involved: a seller emails asking why their
    /// promotion stopped, or asking for a refund, and nobody can say what they were sold.
    /// This table is the answer, and its Reference is what goes in the receipt email so the
    /// seller can quote one short code instead of describing a car.
    ///
    /// WRITTEN BY PromotionService AND NOTHING ELSE. It is the only place that updates both
    /// this table and the two cached fields on Car, so they cannot disagree.
    /// </summary>
    public class Promotion
    {
        public int Id { get; set; }

        /// <summary>
        /// The code the seller is given and the admin looks up: PM-XXXX-XXXX.
        ///
        /// Deliberately not the primary key. Sequential integers leak how much advertising
        /// the site has ever sold - a competitor ordering one placement a month can read the
        /// run rate straight off the receipt - and they make it trivial to guess somebody
        /// else's reference. This is random, from an alphabet with no O/0 or I/1, because it
        /// gets read down a phone.
        /// </summary>
        [MaxLength(20)]
        public string Reference { get; set; } = string.Empty;

        /// <summary>
        /// Null once the listing itself is deleted - listings are hard-deleted, and a receipt
        /// that vanishes with the thing it was sold against is no receipt at all. CarTitle is
        /// the copy that survives.
        /// </summary>
        public int? CarId { get; set; }

        /// <summary>
        /// The listing's title when the placement was bought. Kept as a copy because a seller
        /// can rename or delete the listing, and a receipt that reads "car #47, since deleted"
        /// is no use to whoever is answering the complaint.
        /// </summary>
        [MaxLength(200)]
        public string CarTitle { get; set; } = string.Empty;

        /// <summary>
        /// Who bought it. Null once that account is deleted - the receipt survives an erasure
        /// request, but no personal data is copied into this table to survive with it.
        /// </summary>
        public int? SellerUserId { get; set; }

        public PromotionTier Tier { get; set; }

        public DateTime StartedUtc { get; set; }

        /// <summary>What was sold: the date the placement was paid to run until.</summary>
        public DateTime EndsUtc { get; set; }

        /// <summary>
        /// Set when a placement was stopped before its end date, by an admin or by being
        /// replaced with another one. Never deleted, so "it was cut short" stays answerable.
        /// </summary>
        public DateTime? EndedEarlyUtc { get; set; }

        /// <summary>Why it was stopped early. Free text, admin-facing only.</summary>
        [MaxLength(200)]
        public string? EndedReason { get; set; }

        /// <summary>The admin who granted it, while placements are arranged by hand.</summary>
        public int? GrantedByUserId { get; set; }

        /// <summary>
        /// What the seller paid, in euro. Null for everything granted before checkout exists,
        /// which is currently everything - and null is the honest value for "arranged
        /// off-site", rather than a zero that would read as free.
        /// </summary>
        public decimal? PriceEur { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

        public Car? Car { get; set; }

        /// <summary>Running right now, as opposed to finished or cut short.</summary>
        public bool IsLive(DateTime nowUtc) => EndedEarlyUtc is null && EndsUtc > nowUtc;

        /// <summary>When it actually stopped, whichever came first.</summary>
        public DateTime EffectiveEnd => EndedEarlyUtc ?? EndsUtc;
    }
}
