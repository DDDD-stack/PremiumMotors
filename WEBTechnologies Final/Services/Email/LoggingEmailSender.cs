using Microsoft.Extensions.Options;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Used when no email provider is configured. Writes the message to the log rather than
    /// discarding it, so a developer can still follow a password-reset link locally and so a
    /// misconfigured production deploy is loud instead of silent.
    /// </summary>
    public class LoggingEmailSender : IEmailSender
    {
        private readonly ILogger<LoggingEmailSender> _logger;
        private readonly IWebHostEnvironment _env;

        public LoggingEmailSender(ILogger<LoggingEmailSender> logger, IWebHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        public Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (_env.IsDevelopment())
            {
                _logger.LogWarning(
                    "EMAIL NOT SENT (no provider configured).\n  To: {To}\n  Subject: {Subject}\n  Body:\n{Body}",
                    toEmail, subject, htmlBody);
            }
            else
            {
                _logger.LogError(
                    "Email to {To} was NOT sent: no email provider is configured. Set Email:ApiKey " +
                    "and Email:From. Subject was {Subject}.", toEmail, subject);
            }

            // Reported as not sent so callers never claim delivery that did not happen.
            return Task.FromResult(false);
        }
    }
}
