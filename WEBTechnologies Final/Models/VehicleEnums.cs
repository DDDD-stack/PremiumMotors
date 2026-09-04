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

    /// <summary>Drives seller verification requirements and how a seller is presented.</summary>
    public enum SellerType
    {
        [Display(Name = "Private seller")] Private,
        [Display(Name = "Dealer")] Dealer
    }
}
