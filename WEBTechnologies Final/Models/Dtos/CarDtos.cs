using WEBTechnologies_Final.Services;

namespace WEBTechnologies_Final.Models.Dtos
{
    /// <summary>
    /// Public list item. Deliberately carries no offer information.
    ///
    /// Enums serialize as strings and every DateTime is UTC with a trailing Z, so the React
    /// Native client can bind these straight into a list without a translation layer.
    /// </summary>
    public record CarSummaryDto(
        int Id,
        string Title,
        string Make,
        string Model,
        CarType Type,
        int Year,
        decimal Price,
        int Mileage,
        FuelType FuelType,
        TransmissionType Transmission,
        string Country,
        string? City,
        string PrimaryImage,
        ListingStatus Status,
        bool AcceptsOffers,
        DateTime CreatedUtc)
    {
        public static CarSummaryDto From(Car c, IMediaUrlResolver urls) => new(
            c.Id, c.Title, c.Make, c.Model, c.Type, c.Year, c.Price, c.Mileage,
            c.FuelType, c.Transmission, c.Country, c.City,
            urls.Resolve(c.PrimaryImage), c.Status, c.AcceptsOffers, c.CreatedUtc);
    }

    /// <summary>The vehicle specification block, split out so it can be rendered as one panel.</summary>
    public record VehicleSpecDto(
        int Mileage,
        ServiceHistoryLevel ServiceHistory,
        string? ServiceHistoryNotes,
        FuelType FuelType,
        TransmissionType Transmission,
        DrivetrainType Drivetrain,
        int? EngineSizeCc,
        int? PowerHp,
        int? Doors,
        int? Seats,
        string? ExteriorColour,
        int? PreviousOwners,
        VehicleCondition Condition,
        bool HasAccidentHistory,
        DateTime? FirstRegistration,
        bool HasVin)
    {
        // The VIN itself is never returned: it identifies a specific vehicle and is worth
        // scraping. Buyers get "the seller recorded one", and ask for it in the chat.
        public static VehicleSpecDto From(Car c) => new(
            c.Mileage, c.ServiceHistory, c.ServiceHistoryNotes, c.FuelType, c.Transmission,
            c.Drivetrain, c.EngineSizeCc, c.PowerHp, c.Doors, c.Seats, c.ExteriorColour,
            c.PreviousOwners, c.Condition, c.HasAccidentHistory, c.FirstRegistration,
            !string.IsNullOrWhiteSpace(c.Vin));
    }

    /// <summary>
    /// Full listing view. <see cref="Offers"/> and <see cref="BuyerContact"/> are populated
    /// only for the seller or an admin — offers are private and must never reach the public.
    /// <see cref="MyOffer"/> is the viewer's own offer, which they may always see.
    /// </summary>
    public record CarDetailDto(
        int Id,
        string Title,
        string Make,
        string Model,
        CarType Type,
        int Year,
        string Description,
        decimal Price,
        string Country,
        string? City,
        VehicleSpecDto Spec,
        IReadOnlyList<string> ImagePaths,
        string PrimaryImage,
        ListingStatus Status,
        bool AcceptsOffers,
        string? OwnerUsername,
        string? SellerDisplayName,
        DateTime CreatedUtc,
        DateTime? SoldUtc,
        bool IsViewerSeller,
        bool IsViewerFavorite,
        int? OfferCount,
        IReadOnlyList<OfferDto>? Offers,
        OfferDto? MyOffer,
        int? ConversationId,
        BuyerContactDto? BuyerContact);

    /// <summary>An offer. Returned to the listing's seller, or to the buyer who made it.</summary>
    public record OfferDto(
        int Id,
        int CarId,
        decimal Amount,
        string BuyerUsername,
        string? Message,
        OfferStatus Status,
        DateTime CreatedUtc,
        DateTime? RespondedUtc,
        string? SellerResponse,
        int? ConversationId)
    {
        public static OfferDto From(Offer o) => new(
            o.Id, o.CarId, o.Amount, o.BuyerUsername, o.Message, o.Status,
            o.CreatedUtc, o.RespondedUtc, o.SellerResponse, o.ConversationId);
    }

    /// <summary>Released to the seller once they accept that buyer's offer.</summary>
    public record BuyerContactDto(string Username, string Email, string Phone);

    public record PlaceOfferRequest(decimal Amount, string? Message);

    public record RespondToOfferRequest(string? Response);

    /// <summary>A page of results, so mobile lists can scroll without pulling the whole table.</summary>
    public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
    {
        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
        public bool HasMore => Page * PageSize < TotalCount;
    }

    public record CarFiltersDto(
        IReadOnlyList<string> Makes,
        IReadOnlyList<string> Models,
        IReadOnlyList<int> Years,
        IReadOnlyList<string> Types,
        IReadOnlyList<string> Countries,
        IReadOnlyList<string> FuelTypes,
        IReadOnlyList<string> Transmissions,
        IReadOnlyList<string> BodyConditions);

    public record CarStatsDto(int Total, int Active, int Reserved, int Sold, int TotalOffers);
}
