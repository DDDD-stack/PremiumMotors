using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// Business details as shown and edited on the profile. Separate from the signup model
    /// because signup also creates credentials, and a dealer editing their VAT number should
    /// not be re-typing a password.
    /// </summary>
    public class BusinessDetailsViewModel
    {
        [Required(ErrorMessage = "Enter your registered business name.")]
        [Display(Name = "Business name")]
        [StringLength(80, MinimumLength = 2)]
        public string BusinessName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your business registration number.")]
        [Display(Name = "Business registration number (NIPT / company no.)")]
        [StringLength(40)]
        public string RegistrationNumber { get; set; } = string.Empty;

        [Display(Name = "VAT number")]
        [StringLength(40)]
        public string? VatNumber { get; set; }

        [Required(ErrorMessage = "Enter your trading address.")]
        [Display(Name = "Trading address")]
        [StringLength(160)]
        public string Address { get; set; } = string.Empty;

        [Display(Name = "City / area")]
        [StringLength(80)]
        public string? Location { get; set; }

        [Url(ErrorMessage = "Enter a full web address, including https://")]
        [Display(Name = "Website")]
        [StringLength(160)]
        public string? Website { get; set; }

        [Required(ErrorMessage = "Enter the name of the person responsible for this account.")]
        [Display(Name = "Contact person")]
        [StringLength(80)]
        public string ContactName { get; set; } = string.Empty;

    }
}
