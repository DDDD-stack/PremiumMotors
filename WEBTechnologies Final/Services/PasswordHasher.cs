using System.Security.Cryptography;
using System.Text;

namespace WEBTechnologies_Final.Services
{
    public enum PasswordVerificationResult
    {
        /// <summary>Wrong password (or an unusable stored hash).</summary>
        Failed,

        /// <summary>Correct password, stored in the current format.</summary>
        Success,

        /// <summary>
        /// Correct password, but stored in a legacy format (plaintext or bare SHA-256).
        /// The caller must re-hash and persist immediately.
        /// </summary>
        SuccessRehashNeeded
    }

    /// <summary>
    /// PBKDF2-HMAC-SHA256 password hashing.
    ///
    /// Stored format: <c>pbkdf2-sha256$&lt;iterations&gt;$&lt;base64 salt&gt;$&lt;base64 key&gt;</c>
    ///
    /// Two legacy formats are still *verifiable* so that accounts created before this change
    /// can still log in, and are transparently upgraded on next successful login:
    ///   * 64 hex characters  -> the old unsalted SHA-256 hash written by the API controller.
    ///   * anything else      -> a plaintext password, which the old MVC registration path
    ///                           wrote straight into PasswordHash.
    /// Neither legacy format is ever written again.
    /// </summary>
    public static class PasswordHasher
    {
        private const string Prefix = "pbkdf2-sha256";
        private const int SaltBytes = 16;
        private const int KeyBytes = 32;
        private const int Iterations = 210_000;

        public static string Hash(string password)
        {
            ArgumentNullException.ThrowIfNull(password);

            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var key = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);

            return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        }

        public static PasswordVerificationResult Verify(string password, string? stored)
        {
            if (string.IsNullOrEmpty(stored) || password is null)
                return PasswordVerificationResult.Failed;

            if (stored.StartsWith(Prefix + "$", StringComparison.Ordinal))
                return VerifyPbkdf2(password, stored)
                    ? PasswordVerificationResult.Success
                    : PasswordVerificationResult.Failed;

            if (IsLegacySha256(stored))
                return FixedTimeEquals(LegacySha256(password), stored)
                    ? PasswordVerificationResult.SuccessRehashNeeded
                    : PasswordVerificationResult.Failed;

            // Legacy plaintext.
            return FixedTimeEquals(password, stored)
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }

        private static bool VerifyPbkdf2(string password, string stored)
        {
            var parts = stored.Split('$');
            if (parts.Length != 4) return false;
            if (!int.TryParse(parts[1], out var iterations) || iterations <= 0) return false;

            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static bool IsLegacySha256(string stored) =>
            stored.Length == 64 && stored.All(Uri.IsHexDigit);

        private static string LegacySha256(string password) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();

        private static bool FixedTimeEquals(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }
}
