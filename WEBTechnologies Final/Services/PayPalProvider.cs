using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services
{
    // PayPal payment adapter using the REST Orders v2 API directly over HttpClient
    // (no SDK dependency). Flow: create an order -> redirect buyer to PayPal to approve
    // -> capture the order server-side to confirm payment.
    public class PayPalProvider : IPaymentProvider
    {
        private readonly HttpClient _http;
        private readonly PayPalOptions _options;
        private readonly ListingOptions _listing;
        private readonly ILogger<PayPalProvider> _logger;

        public PayPalProvider(
            HttpClient http, IOptions<PayPalOptions> options,
            IOptions<ListingOptions> listing, ILogger<PayPalProvider> logger)
        {
            _http = http;
            _options = options.Value;
            _listing = listing.Value;
            _logger = logger;
        }

        public async Task<CheckoutResult> CreateListingCheckoutAsync(
            Payment payment, Car car, string returnUrl, string cancelUrl)
        {
            var token = await GetAccessTokenAsync();

            var amount = (payment.AmountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
            var currency = (payment.Currency ?? _listing.Currency).ToUpperInvariant();

            var body = new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = payment.Id.ToString(),
                        custom_id = payment.Id.ToString(),
                        description = Truncate($"Listing fee — {car.Title}", 127),
                        amount = new { currency_code = currency, value = amount }
                    }
                },
                application_context = new
                {
                    brand_name = _options.BrandName,
                    user_action = "PAY_NOW",
                    shipping_preference = "NO_SHIPPING",
                    return_url = returnUrl,
                    cancel_url = cancelUrl
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/v2/checkout/orders")
            {
                Content = JsonContent(body)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal create-order failed ({Status}): {Body}", response.StatusCode, json);
                throw new InvalidOperationException("PayPal order creation failed.");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var orderId = root.GetProperty("id").GetString()!;

            string? approveUrl = null;
            foreach (var link in root.GetProperty("links").EnumerateArray())
            {
                var rel = link.GetProperty("rel").GetString();
                if (rel is "approve" or "payer-action")
                {
                    approveUrl = link.GetProperty("href").GetString();
                    break;
                }
            }

            if (approveUrl is null)
                throw new InvalidOperationException("PayPal did not return an approval link.");

            return new CheckoutResult(approveUrl, orderId);
        }

        public async Task<string?> CaptureAsync(string orderId)
        {
            var token = await GetAccessTokenAsync();

            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{_options.BaseUrl}/v2/checkout/orders/{orderId}/capture")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayPal capture failed for {OrderId} ({Status}): {Body}",
                    orderId, response.StatusCode, json);
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("status").GetString() != "COMPLETED") return null;

            // Dig out the capture id: purchase_units[0].payments.captures[0].id
            if (root.TryGetProperty("purchase_units", out var units) && units.GetArrayLength() > 0
                && units[0].TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("captures", out var captures) && captures.GetArrayLength() > 0)
            {
                return captures[0].GetProperty("id").GetString();
            }

            return orderId; // Completed, but capture id not found — fall back to the order id.
        }

        private async Task<string> GetAccessTokenAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                })
            };
            var basic = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.Secret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            using var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed ({Status}): {Body}", response.StatusCode, json);
                throw new InvalidOperationException("PayPal authentication failed.");
            }

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("access_token").GetString()!;
        }

        private static StringContent JsonContent(object value) =>
            new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];
    }
}
