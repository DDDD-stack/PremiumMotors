namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// Outcome of a marketplace operation. The services are shared by the MVC controllers and
    /// the API, which report failure very differently (TempData vs an ApiError body), so they
    /// return a result the caller translates rather than throwing or returning IActionResult.
    ///
    /// <see cref="Code"/> is the stable machine-readable reason; the React Native client
    /// switches on it, so do not reword existing codes.
    /// </summary>
    public record MarketplaceResult(bool Success, string? Error = null, string? Code = null)
    {
        public static MarketplaceResult Ok() => new(true);
        public static MarketplaceResult Fail(string error, string code) => new(false, error, code);
    }

    public record MarketplaceResult<T>(bool Success, T? Value, string? Error = null, string? Code = null)
    {
        public static MarketplaceResult<T> Ok(T value) => new(true, value);
        public static MarketplaceResult<T> Fail(string error, string code) => new(false, default, error, code);
    }

    /// <summary>Well-known <c>Code</c> values. Kept together so the client can mirror them.</summary>
    public static class MarketplaceCodes
    {
        public const string NotFound = "not_found";
        public const string Forbidden = "forbidden";
        public const string OwnListing = "own_listing";
        public const string NotAcceptingOffers = "not_accepting_offers";
        public const string InvalidAmount = "invalid_amount";
        public const string OfferNotPending = "offer_not_pending";
        public const string AlreadySeller = "already_seller";
        public const string NotSeller = "not_seller";
        public const string EmptyMessage = "empty_message";
        public const string ConversationClosed = "conversation_closed";
    }
}
