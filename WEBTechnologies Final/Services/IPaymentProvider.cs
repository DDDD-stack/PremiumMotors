using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services
{
    // The redirect target for a hosted checkout plus the provider's order reference to persist.
    public record CheckoutResult(string RedirectUrl, string OrderId);

    // Abstraction over the payment provider so the listing-fee flow stays provider-agnostic.
    // Currently implemented by PayPalProvider; a Stripe adapter could slot back in unchanged.
    public interface IPaymentProvider
    {
        /// <summary>Creates a hosted checkout for a listing fee and returns where to send the seller.</summary>
        Task<CheckoutResult> CreateListingCheckoutAsync(
            Payment payment, Car car, string returnUrl, string cancelUrl);

        /// <summary>Captures/settles an approved order. Returns the capture id if completed, else null.</summary>
        Task<string?> CaptureAsync(string orderId);
    }
}
