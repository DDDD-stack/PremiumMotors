using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// A rating left for a seller after a completed sale.
    ///
    /// It targets the SELLER'S USER ACCOUNT, not a dealership, which is what lets one
    /// mechanism serve both: a private seller's reputation is their own reviews, and a
    /// dealership's is the reviews of the account that owns it. There is no second table and
    /// no second set of rules to keep in step.
    ///
    /// ELIGIBILITY IS THE WHOLE VALUE. Only the buyer of a listing that actually sold may
    /// review its seller, and only once per listing. Open reviews would be worth nothing
    /// within a week - anyone could rate anyone, and a competitor could bury a dealer for
    /// free. The sale is already recorded on Car (SoldToUserId, SoldUtc), so eligibility is
    /// something the database can prove rather than something a moderator has to judge.
    /// </summary>
    public class SellerReview
    {
        public int Id { get; set; }

        /// <summary>The seller being reviewed.</summary>
        public int SellerUserId { get; set; }

        /// <summary>Goes null if the author deletes their account; the review survives.</summary>
        public int? AuthorUserId { get; set; }

        /// <summary>Denormalized display copy, so a deleted author still shows a name.</summary>
        [StringLength(80)]
        public string AuthorUsername { get; set; } = string.Empty;

        /// <summary>The sale this review is attached to. One review per listing.</summary>
        public int? CarId { get; set; }

        [Range(1, 5, ErrorMessage = "Choose a rating from 1 to 5 stars.")]
        [Display(Name = "Rating")]
        public int Rating { get; set; }

        [Display(Name = "Your review")]
        [StringLength(1500)]
        public string? Comment { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>A seller may answer once. Answering is not editing what was said.</summary>
        [Display(Name = "Reply")]
        [StringLength(1000)]
        public string? SellerReply { get; set; }

        public DateTime? SellerRepliedUtc { get; set; }

        public Car? Car { get; set; }
    }
}
