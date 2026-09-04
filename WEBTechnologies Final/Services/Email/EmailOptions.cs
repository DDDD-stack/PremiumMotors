namespace WEBTechnologies_Final.Services
{
    public class EmailOptions
    {
        /// <summary>Resend API key. SECRET - user-secrets or environment only.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Verified sender, e.g. "PremiumMotors &lt;no-reply@yourdomain.com&gt;".</summary>
        public string From { get; set; } = string.Empty;

        /// <summary>Base URL used to build links in emails, e.g. https://premiummotors.al</summary>
        public string PublicBaseUrl { get; set; } = string.Empty;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(From);
    }
}
