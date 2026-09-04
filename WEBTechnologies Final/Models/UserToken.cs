using System.Text.Json.Serialization;

namespace WEBTechnologies_Final.Models
{
    public enum UserTokenPurpose
    {
        PasswordReset,
        EmailVerification
    }

    /// <summary>
    /// A single-use, expiring, hashed token emailed to a user.
    ///
    /// Same discipline as RefreshToken: only a SHA-256 hash is stored, so a database leak
    /// cannot be replayed to take over accounts.
    /// </summary>
    public class UserToken
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        [JsonIgnore]
        public User? User { get; set; }

        public string TokenHash { get; set; } = string.Empty;

        public UserTokenPurpose Purpose { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresUtc { get; set; }
        public DateTime? UsedUtc { get; set; }

        public bool IsUsable => UsedUtc is null && ExpiresUtc > DateTime.UtcNow;
    }
}
