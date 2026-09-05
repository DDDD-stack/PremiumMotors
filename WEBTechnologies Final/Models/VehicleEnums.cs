using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{
    // These are serialized as strings on the API (Program.cs registers a
    // JsonStringEnumConverter), so the React Native client sees "Diesel"
    // rather than 1 and stays readable if the ordering ever changes.

    public enum FuelType
    {
        [Display(Name = "Petrol")] Petrol,
        [Display(Name = "Diesel")] Diesel,
        [Display(Name = "Hybrid")] Hybrid,
        [Display(Name = "Plug-in hybrid")] PluginHybrid,
        [Display(Name = "Electric")] Electric,
        [Display(Name = "LPG")] Lpg,
        [Display(Name = "CNG")] Cng,
        [Display(Name = "Other")] Other
    }

    public enum TransmissionType
    {
        [Display(Name = "Manual")] Manual,
        [Display(Name = "Automatic")] Automatic,
        [Display(Name = "Semi-automatic")] SemiAutomatic,
        [Display(Name = "CVT")] Cvt
    }

    public enum DrivetrainType
    {
        [Display(Name = "Front-wheel drive")] FrontWheel,
        [Display(Name = "Rear-wheel drive")] RearWheel,
        [Display(Name = "All-wheel drive")] AllWheel
    }

    /// <summary>How complete the vehicle's documented servicing is.</summary>
    public enum ServiceHistoryLevel
    {
        [Display(Name = "Not specified")] Unspecified,
        [Display(Name = "No service history")] None,
        [Display(Name = "Partial service history")] Partial,
        [Display(Name = "Full service history")] Full,
        [Display(Name = "Full main-dealer history")] FullDealer
    }

    public enum VehicleCondition
    {
        [Display(Name = "New")] New,
        [Display(Name = "Used")] Used,
        [Display(Name = "Damaged / repairable")] Damaged,
        [Display(Name = "For parts")] ForParts
    }

    /// <summary>
    /// Replaces the old IsPublished/IsSold/AuctionEnd triple. A listing is publicly
    /// visible in Active, Reserved and Sold; Draft and Archived are seller-only.
    /// </summary>
    public enum ListingStatus
    {
        [Display(Name = "Draft")] Draft,
        [Display(Name = "Active")] Active,
        [Display(Name = "Reserved")] Reserved,
        [Display(Name = "Sold")] Sold,
        [Display(Name = "Archived")] Archived
    }

    public enum OfferStatus
    {
        [Display(Name = "Pending")] Pending,
        [Display(Name = "Accepted")] Accepted,
        [Display(Name = "Declined")] Declined,
        [Display(Name = "Withdrawn")] Withdrawn
    }

    /// <summary>How a seller is presented on their listings and in the directory.</summary>
    public enum SellerType
    {
        [Display(Name = "Private seller")] Private,
        [Display(Name = "Dealer")] Dealer
    }

    /// <summary>
    /// Paid placement. Selling advertising is the only way money changes hands on this site —
    /// listing is free and we take nothing from a sale — so this enum is the entire product
    /// catalogue, and the tiers are ordered by what they cost.
    ///
    /// Each tier includes everything below it. A promotion always has an expiry
    /// (<see cref="Car.PromotedUntilUtc"/>): placement that never lapses can only be sold once.
    /// </summary>
    public enum PromotionTier
    {
        /// <summary>An ordinary listing. Nobody paid for anything.</summary>
        [Display(Name = "Not promoted")] None = 0,

        /// <summary>
        /// Highlighted card wherever it appears, plus a slot in the promoted strip at the top
        /// of the marketplace.
        /// </summary>
        [Display(Name = "Promoted in the marketplace")] Promoted = 1,

        /// <summary>
        /// The above, plus a slot on both front pages — the consumer one and the business one.
        /// The most expensive thing we sell, because it is the only inventory that is seen
        /// before a visitor has decided what they are looking for.
        /// </summary>
        [Display(Name = "Front page")] FrontPage = 2
    }
}
