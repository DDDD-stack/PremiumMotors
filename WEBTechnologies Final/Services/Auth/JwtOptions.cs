namespace WEBTechnologies_Final.Services.Auth
{
    /// <summary>
    /// Bound from the "Jwt" config section. <see cref="Key"/> is a secret and must come from
    /// user-secrets or the Jwt__Key environment variable - never appsettings.json.
    /// </summary>
    public class JwtOptions
    {
        public string Issuer { get; set; } = "PremiumMotors";
        public string Audience { get; set; } = "PremiumMotors.Clients";

        /// <summary>HMAC-SHA256 signing key. Must be at least 32 bytes.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Access tokens are short-lived; clients refresh them silently.</summary>
        public int AccessTokenMinutes { get; set; } = 60;

        /// <summary>How long a mobile client can stay signed in without re-entering a password.</summary>
        public int RefreshTokenDays { get; set; } = 30;
    }
}
