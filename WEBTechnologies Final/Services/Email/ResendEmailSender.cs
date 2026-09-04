using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Sends through Resend (https://resend.com). Swapping to SendGrid, Postmark or SMTP means
    /// writing one more IEmailSender - nothing else in the app knows which provider is used.
    /// </summary>
    public class ResendEmailSender : IEmailSender
    {
        private readonly HttpClient _http;
        private readonly EmailOptions _options;
        private readonly ILogger<ResendEmailSender> _logger;

        public ResendEmailSender(HttpClient http, IOptions<EmailOptions> options, ILogger<ResendEmailSender> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
                request.Content = JsonContent.Create(new
                {
                    from = _options.From,
                    to = new[] { toEmail },
                    subject,
                    html = htmlBody
                });

                using var response = await _http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode) return true;

                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Resend rejected the email ({Status}): {Body}", (int)response.StatusCode, body);
                return false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Sending email to {To} threw.", toEmail);
                return false;
            }
        }
    }
}
