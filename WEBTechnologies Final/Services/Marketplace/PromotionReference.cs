using System.Security.Cryptography;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// Generates the short code that identifies a paid placement: PM-XXXX-XXXX.
    /// </summary>
    public static class PromotionReference
    {
        /// <summary>
        /// No O or 0, no I or 1, no U. The first two pairs are the ones people mistype when
        /// reading a code off a screen into a phone; U is out because dropping it removes
        /// most of the ways eight random letters spell something the seller would rather not
        /// read out to support.
        /// </summary>
        private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

        public const string Prefix = "PM-";

        /// <summary>
        /// Eight characters from a 30-character alphabet is about 6.5 x 10^11 codes. These
        /// are looked up, never guessed at scale, and they identify a receipt rather than
        /// authorise anything - but they are still generated with a cryptographic RNG,
        /// because System.Random seeded per-request has produced duplicate "unique" codes in
        /// more than one production system.
        /// </summary>
        public static string Next()
        {
            var chars = new char[8];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

            return $"{Prefix}{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
        }

        /// <summary>
        /// Accepts what a human actually types: lower case, missing prefix, missing or extra
        /// dashes, spaces from a copy-paste. Returns the canonical form, or null if what was
        /// typed cannot be a reference at all.
        ///
        /// This exists because the alternative is an admin typing a code the seller read out
        /// correctly and getting "not found", which looks like the receipt was lost.
        /// </summary>
        public static string? Normalise(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var body = new string(input
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());

            if (body.StartsWith("PM", StringComparison.Ordinal)) body = body[2..];
            if (body.Length != 8) return null;
            if (body.Any(c => !Alphabet.Contains(c))) return null;

            return $"{Prefix}{body[..4]}-{body[4..]}";
        }
    }
}
