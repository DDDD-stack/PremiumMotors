using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// The "start selling" opt-in for a PERSONAL account. It no longer asks private-or-dealer:
    /// a dealer registers through "Register as a business", which collects everything a dealer
    /// account needs up front, so anyone reaching this form is by definition a private seller.
    /// </summary>
    public class BecomeSellerViewModel
    {
        [Display(Name = "Display name")]
        [StringLength(80)]
        public string? DisplayName { get; set; }

        [Display(Name = "Where are you based?")]
        [StringLength(80)]
        public string? Location { get; set; }

        // Was [Range(typeof(bool), "true", "true")], whose client-side rule rejects a TICKED
        // checkbox — see MustBeTrueAttribute for the mechanism.
        [Display(Name = "I accept the seller terms")]
        [MustBeTrue(ErrorMessage = "You must accept the seller terms to continue.")]
        public bool AcceptTerms { get; set; }
    }

    /// <summary>
    /// Editable part of a seller's public profile. SellerType is NOT editable here — it is
    /// set by which registration route the account came through, and a private seller who
    /// starts trading should register a business account rather than flip a dropdown.
    /// </summary>
    public class SellerProfileViewModel
    {
        public SellerType SellerType { get; set; }

        [Display(Name = "Display name")]
        [StringLength(80)]
        public string? DisplayName { get; set; }

        [Display(Name = "Location")]
        [StringLength(80)]
        public string? Location { get; set; }

        /// <summary>
        /// Optional, and public the moment it is filled in — it appears on every listing this
        /// seller has, to signed-out visitors included. That is the point: ringing the seller
        /// is the one way to make contact without an account. Separate from the account's
        /// private Phone, which is still only released once an offer is accepted.
        /// </summary>
        [Display(Name = "Public phone number")]
        [StringLength(40)]
        [Phone(ErrorMessage = "Enter a phone number people can actually dial.")]
        public string? PublicPhone { get; set; }

        public bool IsVerified { get; set; }
        public bool IsBusiness { get; set; }
        public DateTime? SellerSinceUtc { get; set; }
    }
}
