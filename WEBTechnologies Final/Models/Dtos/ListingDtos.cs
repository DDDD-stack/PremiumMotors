using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models.Dtos
{
    /// <summary>Create a draft listing from a mobile client (photos are uploaded separately).</summary>
    public class CreateListingRequest
    {
        [Required] public string Make { get; set; } = string.Empty;
        [Required] public string Model { get; set; } = string.Empty;
        public CarType Type { get; set; }

        [Range(1900, 2100)] public int Year { get; set; }

        public string Description { get; set; } = string.Empty;

        [Range(0, double.MaxValue)] public decimal Price { get; set; }

        [Required] public string Country { get; set; } = string.Empty;
        public string? City { get; set; }

        // ---------- Vehicle specification ----------
        [Range(0, 2_000_000)] public int Mileage { get; set; }
        public ServiceHistoryLevel ServiceHistory { get; set; } = ServiceHistoryLevel.Unspecified;
        public string? ServiceHistoryNotes { get; set; }
        public FuelType FuelType { get; set; }
        public TransmissionType Transmission { get; set; }
        public DrivetrainType Drivetrain { get; set; }
        [Range(0, 10000)] public int? EngineSizeCc { get; set; }
        [Range(0, 3000)] public int? PowerHp { get; set; }
        [Range(1, 7)] public int? Doors { get; set; }
        [Range(1, 9)] public int? Seats { get; set; }
        [StringLength(40)] public string? ExteriorColour { get; set; }
        [Range(0, 20)] public int? PreviousOwners { get; set; }
        public VehicleCondition Condition { get; set; } = VehicleCondition.Used;
        public bool HasAccidentHistory { get; set; }
        [StringLength(17, MinimumLength = 11)] public string? Vin { get; set; }
        public DateTime? FirstRegistration { get; set; }

        /// <summary>Copies the request onto an entity. Shared by create and update.</summary>
        public void ApplyTo(Car car)
        {
            car.Make = Make;
            car.Model = Model;
            car.Type = Type;
            car.Year = Year;
            car.Description = Description;
            car.Price = Price;
            car.Country = Country;
            car.City = City;

            car.Mileage = Mileage;
            car.ServiceHistory = ServiceHistory;
            car.ServiceHistoryNotes = ServiceHistoryNotes;
            car.FuelType = FuelType;
            car.Transmission = Transmission;
            car.Drivetrain = Drivetrain;
            car.EngineSizeCc = EngineSizeCc;
            car.PowerHp = PowerHp;
            car.Doors = Doors;
            car.Seats = Seats;
            car.ExteriorColour = ExteriorColour;
            car.PreviousOwners = PreviousOwners;
            car.Condition = Condition;
            car.HasAccidentHistory = HasAccidentHistory;
            car.Vin = Vin;
            car.FirstRegistration = FirstRegistration;
        }
    }

    public class UpdateListingRequest : CreateListingRequest { }

    /// <summary>
    /// What happens next after creating a listing. <see cref="Status"/> is one of:
    ///   published        — free-listing mode or a free relist; the listing is already live
    ///   payment_required — the client must open <see cref="CheckoutUrl"/> to pay the fee
    /// </summary>
    public record CreateListingResult(
        string Status,
        CarSummaryDto Listing,
        int? PaymentId,
        long? AmountCents,
        string? Currency,
        string? CheckoutUrl,
        string? Message);

    public record ListingPaymentDto(
        int PaymentId,
        int? CarId,
        long AmountCents,
        string Currency,
        string Status,
        bool OfferConsumed,
        int RelistCount,
        DateTime CreatedUtc,
        DateTime? PaidUtc);

    /// <summary>A listing as its own seller sees it, including the private offer counts.</summary>
    public record MyListingDto(
        CarSummaryDto Car,
        ListingStatus Status,
        int OfferCount,
        int PendingOffers,
        decimal? BestPendingOffer,
        string? SoldTo,
        decimal? SoldPrice,
        ListingPaymentDto? Payment);

    /// <summary>An offer the signed-in user placed on someone else's listing.</summary>
    public record MyOfferDto(
        int Id,
        decimal Amount,
        string? Message,
        OfferStatus Status,
        DateTime CreatedUtc,
        DateTime? RespondedUtc,
        string? SellerResponse,
        int? ConversationId,
        CarSummaryDto Car);

    public record PhotoUploadResult(
        int CarId,
        IReadOnlyList<string> ImagePaths,
        IReadOnlyList<string> Rejected);
}
