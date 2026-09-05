using System.Text.Json.Serialization;

namespace WEBTechnologies_Final.Models
{
    // Role names are plain strings so they can be carried in a JWT "role" claim and used
    // directly by [Authorize(Roles = ...)] on both the MVC and API surfaces.
    public static class Roles
    {
        public const string User = "User";
        public const string Admin = "Admin";
    }

    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        // Contact number revealed to a seller only for the winning offer on their listing.
        public string Phone { get; set; } = string.Empty;

        // Never serialized: the API returns UserDto, but this guards against an entity
        // accidentally being returned directly from an action.
        [JsonIgnore]
        public string PasswordHash { get; set; } = string.Empty;

        public string Role { get; set; } = Roles.User;

        // A disabled account can still own listings but cannot log in or refresh a token.
        public bool IsActive { get; set; } = true;

        public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginUtc { get; set; }

        /// <summary>Null until the user follows the verification link emailed to them.</summary>
        public DateTime? EmailVerifiedUtc { get; set; }

        public bool IsEmailVerified => EmailVerifiedUtc is not null;

        // ---------- Seller profile ----------
        // Every account starts as a buyer. Opting in unlocks the seller panel; it is a
        // capability on the account rather than a separate role, so a seller keeps their
        // buyer abilities (browsing, favourites, making offers on other people's cars).

        public bool IsSeller { get; set; }

        public DateTime? SellerSinceUtc { get; set; }

        public SellerType SellerType { get; set; } = SellerType.Private;

        /// <summary>Trading name shown on listings. Falls back to the username.</summary>
        public string? SellerDisplayName { get; set; }

        /// <summary>Free-text town/region shown on the seller's listings.</summary>
        public string? SellerLocation { get; set; }

        /// <summary>
        /// A number the seller is happy for anyone to ring, shown on every one of their
        /// listings — including to signed-out visitors.
        ///
        /// Deliberately NOT <see cref="Phone"/>. That one is the account's private contact
        /// detail and is released only to the other party once an offer is accepted; putting
        /// it on a public page would retroactively publish a number every existing user gave
        /// us for a different purpose. This field starts null and stays null until a seller
        /// types one in, so opting in is an act rather than a default.
        /// </summary>
        public string? PublicPhone { get; set; }

        /// <summary>
        /// PLACEHOLDER: dealers will need document checks (business registration, VAT number)
        /// before this is set. Nothing sets it to true yet.
        /// </summary>
        public bool SellerVerified { get; set; }

        // ---------- Business details ----------
        // Collected once at "Register as a business" so that opting into selling never has to
        // ask "are you a dealer?" again — the answer is already on the account.

        public string? BusinessRegistrationNumber { get; set; }

        public string? VatNumber { get; set; }

        public string? BusinessAddress { get; set; }

        public string? Website { get; set; }

        /// <summary>Named person responsible for the account, for a dealer.</summary>
        public string? ContactName { get; set; }

        // ---------- Profile picture ----------

        /// <summary>
        /// Stored through IPhotoStorage like any listing photo, so it follows the same
        /// validation, the same size limits and the same local/Supabase switch. Null means
        /// the initials fall back to a generated monogram - never a broken image.
        /// </summary>
        public string? AvatarPath { get; set; }

        // ---------- Reputation ----------
        // Denormalized from SellerReview so a page of listing cards does not need a
        // correlated aggregate per card. Both values are RECOMPUTED from the table on every
        // review write rather than incremented, so they cannot drift out of step with it.

        public int RatingCount { get; set; }

        /// <summary>Mean of every review, 1.00 to 5.00. Zero when there are none.</summary>
        public decimal RatingAverage { get; set; }

        public bool HasRating => RatingCount > 0;

        public bool IsBusiness => SellerType == SellerType.Dealer;

        public string SellerName => string.IsNullOrWhiteSpace(SellerDisplayName) ? Username : SellerDisplayName;

        [JsonIgnore]
        public List<RefreshToken> RefreshTokens { get; set; } = new();

        public bool IsAdmin => string.Equals(Role, Roles.Admin, StringComparison.OrdinalIgnoreCase);
    }
}
