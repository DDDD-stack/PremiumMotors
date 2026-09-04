using System.Text.Json.Serialization;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// A long-lived credential a client (mobile app, SPA) exchanges for a fresh access token.
    /// Only a SHA-256 hash of the token is stored, so a database leak cannot be replayed.
    /// Tokens rotate on every refresh: the old row is revoked and linked to its replacement,
    /// which makes token theft detectable (a reused token is already-revoked).
    /// </summary>
    public class RefreshToken
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        [JsonIgnore]
        public User? User { get; set; }

        // SHA-256 (hex) of the opaque token handed to the client. Never the token itself.
        public string TokenHash { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresUtc { get; set; }
        public DateTime? RevokedUtc { get; set; }

        // Set when this token was rotated out, pointing at the token that replaced it.
        public int? ReplacedByTokenId { get; set; }

        // Free-form client label ("ios", "android", "web") so a user can see and revoke sessions.
        public string? Device { get; set; }

        public bool IsActive => RevokedUtc is null && ExpiresUtc > DateTime.UtcNow;
    }
}
