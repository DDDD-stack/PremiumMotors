using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// A marketplace listing. This used to be an auction: it carried a required AuctionEnd,
    /// a ClosureProcessed flag and an IsSold bool that a background sweep set when the clock
    /// ran out. The marketplace has no clock — a listing stays Active until its seller accepts
    /// an offer or archives it, so all of that is gone and <see cref="Status"/> is the single
    /// source of truth for where a listing is in its life.
    /// </summary>
    public class Car
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Make")]
        public string Make { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Model")]
        public string Model { get; set; } = string.Empty;

        [Display(Name = "Body type")]
        public CarType Type { get; set; }

        [Range(1900, 2100)]
        [Display(Name = "Model year")]
        public int Year { get; set; }

        [Display(Name = "Description")]
        public string Description { get; set; } = string.Empty;

        [Range(0, double.MaxValue, ErrorMessage = "Asking price must be a positive number.")]
        [Display(Name = "Asking price")]
        [DataType(DataType.Currency)]
        public decimal Price { get; set; }

        public List<string> ImagePaths { get; set; } = new();

        // ---------- Vehicle specification ----------
        // Mileage and service history are what a buyer looks at first on any
        // used-car marketplace; everything below them is optional detail.

        [Range(0, 2_000_000, ErrorMessage = "Enter the mileage in kilometres.")]
        [Display(Name = "Mileage (km)")]
        public int Mileage { get; set; }

        [Display(Name = "Service history")]
        public ServiceHistoryLevel ServiceHistory { get; set; } = ServiceHistoryLevel.Unspecified;

        [Display(Name = "Service history notes")]
        [StringLength(2000)]
        public string? ServiceHistoryNotes { get; set; }

        [Display(Name = "Fuel type")]
        public FuelType FuelType { get; set; }

        [Display(Name = "Transmission")]
        public TransmissionType Transmission { get; set; }

        [Display(Name = "Drivetrain")]
        public DrivetrainType Drivetrain { get; set; }

        [Range(0, 10000)]
        [Display(Name = "Engine size (cc)")]
        public int? EngineSizeCc { get; set; }

        [Range(0, 3000)]
        [Display(Name = "Power (hp)")]
        public int? PowerHp { get; set; }

        [Range(1, 7)]
        [Display(Name = "Doors")]
        public int? Doors { get; set; }

        [Range(1, 9)]
        [Display(Name = "Seats")]
        public int? Seats { get; set; }

        [Display(Name = "Exterior colour")]
        [StringLength(40)]
        public string? ExteriorColour { get; set; }

        [Range(0, 20)]
        [Display(Name = "Previous owners")]
        public int? PreviousOwners { get; set; }

        [Display(Name = "Condition")]
        public VehicleCondition Condition { get; set; } = VehicleCondition.Used;

        [Display(Name = "Has accident/damage history")]
        public bool HasAccidentHistory { get; set; }

        // Kept optional and never shown in full publicly: a VIN identifies a specific
        // vehicle and is worth something to a scraper.
        [Display(Name = "VIN (optional)")]
        [StringLength(17, MinimumLength = 11)]
        public string? Vin { get; set; }

        [Display(Name = "First registered")]
        [DataType(DataType.Date)]
        public DateTime? FirstRegistration { get; set; }

        // ---------- Location ----------

        [Required]
        [Display(Name = "Country")]
        public string Country { get; set; } = string.Empty;

        [Display(Name = "City / area")]
        [StringLength(80)]
        public string? City { get; set; }

        // ---------- Ownership and lifecycle ----------

        /// <summary>Stable identity of the seller. Null for admin "house" listings.</summary>
        public int? OwnerId { get; set; }

        /// <summary>Denormalized display copy of the seller's username.</summary>
        public string? OwnerUsername { get; set; }

        [Display(Name = "Status")]
        public ListingStatus Status { get; set; } = ListingStatus.Draft;

        public List<Offer> Offers { get; set; } = new();

        /// <summary>Set when the seller accepts an offer.</summary>
        public string? SoldTo { get; set; }
        public int? SoldToUserId { get; set; }
        public DateTime? SoldUtc { get; set; }

        /// <summary>The accepted offer amount, which need not equal the asking price.</summary>
        public decimal? SoldPrice { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? PublishedUtc { get; set; }

        /// <summary>
        /// Detail-page views, deduplicated per browser session so a refresh does not inflate
        /// it. Sellers ask for this on day one, and every "how is my listing doing" number on
        /// the analytics page starts here.
        /// </summary>
        public int ViewCount { get; set; }

        // ---------- Paid placement ----------

        /// <summary>What the seller bought. See <see cref="PromotionTier"/>.</summary>
        public PromotionTier PromotionTier { get; set; } = PromotionTier.None;

        /// <summary>
        /// When the placement lapses. Null means no promotion has ever been bought.
        ///
        /// Expiry is stored rather than inferred so that a lapsed promotion leaves a record of
        /// what was sold. Nothing sweeps expired rows: every query filters on this date, so an
        /// expired promotion simply stops matching. A background job that reset the tier would
        /// be one more thing to get wrong for no benefit.
        /// </summary>
        public DateTime? PromotedUntilUtc { get; set; }

        // ---------- Derived ----------

        public string Title => $"{Year} {Make} {Model}";

        /// <summary>
        /// Paid placement that is live right now. Checked against the clock at render time, so
        /// a promotion that lapsed a second ago stops being shown without anything having to
        /// run.
        ///
        /// Active only, deliberately — narrower than IsPubliclyVisible. A reserved or sold car
        /// stays browsable, but advertising one is worse than advertising nothing: the visitor
        /// clicks the most prominent thing on the page and lands on a car they cannot buy.
        ///
        /// NOTE: this is a C# property and cannot be translated to SQL. Queries express the
        /// same condition explicitly — see CarQueries.WherePromoted.
        /// </summary>
        public bool IsPromoted =>
            PromotionTier != PromotionTier.None
            && PromotedUntilUtc > DateTime.UtcNow
            && Status == ListingStatus.Active;

        /// <summary>Live placement on the two front pages — the top tier.</summary>
        public bool IsFrontPagePromoted =>
            IsPromoted && PromotionTier == PromotionTier.FrontPage;

        /// <summary>Draft and Archived listings exist only for their seller and admins.</summary>
        public bool IsPubliclyVisible =>
            Status is ListingStatus.Active or ListingStatus.Reserved or ListingStatus.Sold;

        /// <summary>Only an Active listing takes new offers; Reserved is already spoken for.</summary>
        public bool AcceptsOffers => Status == ListingStatus.Active;

        public bool IsSold => Status == ListingStatus.Sold;

        public string PrimaryImage =>
            ImagePaths.Count > 0 ? ImagePaths[0] : "/img/no-image.svg";

        /// <summary>Offers are private to the seller, so this is never surfaced publicly.</summary>
        public Offer? BestOffer =>
            Offers.Count == 0 ? null : Offers.OrderByDescending(o => o.Amount).First();
    }
}
