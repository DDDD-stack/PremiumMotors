namespace WEBTechnologies_Final.Services
{
    // Bound from the "PayPal" config section. Secret values come from user-secrets/env,
    // never appsettings.json. Empty values are allowed so the app still boots before keys
    // are configured.
    public class PayPalOptions
    {
        public string ClientId { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;

        // "sandbox" for testing, "live" for production.
        public string Mode { get; set; } = "sandbox";

        // Webhook id from the PayPal dashboard, used to verify incoming webhook signatures.
        public string WebhookId { get; set; } = string.Empty;

        public string BrandName { get; set; } = "Car Auctions";

        public string BaseUrl =>
            string.Equals(Mode, "live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";
    }

    // Bound from the "Listing" config section. ListingFeeCents is the dial for launch
    // pricing: set it to 0 during cold-start (free listings) and to 500 (€5) once there
    // is buyer demand. MaxFreeRelists caps how many times a zero-offer token may be reused.
    public class ListingOptions
    {
        public long ListingFeeCents { get; set; } = 500;
        public string Currency { get; set; } = "eur";
        public int MaxFreeRelists { get; set; } = 3;
    }
}
