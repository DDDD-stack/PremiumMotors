using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{
    /// <summary>Personal account signup. A private seller opts into selling later.</summary>
    public class RegisterViewModel
    {
        [Required]
        [Display(Name = "Username")]
        [StringLength(30, MinimumLength = 3, ErrorMessage = "Username must be 3-30 characters.")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Phone]
        [Display(Name = "Phone")]
        public string Phone { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "I accept the Terms and Privacy Policy")]
        [MustBeTrue(ErrorMessage = "You must accept the Terms and Privacy Policy to create an account.")]
        public bool AcceptTerms { get; set; }
    }

    /// <summary>
    /// Business signup. Everything a dealer account needs is collected here, once, so that
    /// switching a personal account to selling never has to ask "private or dealer?" — a
    /// business account is already a verified-intent dealer from the moment it is created.
    /// </summary>
    public class RegisterBusinessViewModel
    {
        // ---------- Business ----------

        [Required(ErrorMessage = "Enter your registered business name.")]
        [Display(Name = "Business name")]
        [StringLength(80, MinimumLength = 2)]
        public string BusinessName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your business registration number.")]
        [Display(Name = "Business registration number (NIPT / company no.)")]
        [StringLength(40)]
        public string RegistrationNumber { get; set; } = string.Empty;

        [Display(Name = "VAT number (optional)")]
        [StringLength(40)]
        public string? VatNumber { get; set; }

        [Required(ErrorMessage = "Enter your trading address.")]
        [Display(Name = "Trading address")]
        [StringLength(160)]
        public string Address { get; set; } = string.Empty;

        [Required]
        [Display(Name = "City")]
        [StringLength(80)]
        public string City { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Country")]
        [StringLength(80)]
        public string Country { get; set; } = string.Empty;

        [Url(ErrorMessage = "Enter a full web address, including https://")]
        [Display(Name = "Website (optional)")]
        [StringLength(160)]
        public string? Website { get; set; }

        // ---------- Contact person ----------

        [Required(ErrorMessage = "Enter the name of the person responsible for this account.")]
        [Display(Name = "Contact person")]
        [StringLength(80)]
        public string ContactName { get; set; } = string.Empty;

        [Required]
        [Phone]
        [Display(Name = "Business phone")]
        public string Phone { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [Display(Name = "Business email")]
        public string Email { get; set; } = string.Empty;

        // ---------- Login ----------

        [Required]
        [Display(Name = "Username")]
        [StringLength(30, MinimumLength = 3, ErrorMessage = "Username must be 3-30 characters.")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Display(Name = "I accept the Terms, the seller terms and the Privacy Policy")]
        [MustBeTrue(ErrorMessage = "You must accept the Terms to register a business account.")]
        public bool AcceptTerms { get; set; }
    }

    public class LoginViewModel
    {
        [Required]
        [Display(Name = "Username or email")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        public string? ReturnUrl { get; set; }
    }

    /// <summary>Editable account details on the profile page.</summary>
    public class ProfileViewModel
    {
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Phone]
        [Display(Name = "Phone", Prompt = "Shared with the other party only when an offer is accepted.")]
        public string Phone { get; set; } = string.Empty;

        public bool IsSeller { get; set; }
        public bool IsBusiness { get; set; }
        public bool EmailVerified { get; set; }
        public DateTime RegisteredUtc { get; set; }

        /// <summary>Uploaded through the separate Avatar action, not this form.</summary>
        public string? AvatarPath { get; set; }

        public decimal RatingAverage { get; set; }
        public int RatingCount { get; set; }

        public string Initials =>
            Username.Length == 0 ? "?" : Username[..Math.Min(2, Username.Length)].ToUpperInvariant();
    }

    public class ChangePasswordViewModel
    {
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
