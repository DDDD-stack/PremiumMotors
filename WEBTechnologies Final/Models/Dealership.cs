using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// The public shopfront for a business account: a dealer's listings gathered in one
    /// browsable place, with the trading details a buyer wants before they get in a car and
    /// drive somewhere.
    ///
    /// It deliberately does NOT own its listings. A dealership's cars are simply the cars
    /// whose OwnerId is <see cref="OwnerUserId"/>, so there is no Car.DealershipId to fall out
    /// of step with Car.OwnerId. One dealership per business account, created automatically at
    /// business signup.
    ///
    /// The legal record - registration number, VAT, responsible person - stays on the User.
    /// That is compliance data, not a shopfront, and only admins ever need to see it. This
    /// entity is the half that is public.
    /// </summary>
    public class Dealership
    {
        public int Id { get; set; }

        /// <summary>The business account that owns it. One dealership per account.</summary>
        public int OwnerUserId { get; set; }

        /// <summary>
        /// URL identity, e.g. /dealerships/adriatik-motors. Stable once created: changing it
        /// breaks every link a dealer has already put on a business card.
        /// </summary>
        [StringLength(80)]
        public string Slug { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Dealership name")]
        [StringLength(80)]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "About")]
        [StringLength(2000)]
        public string? About { get; set; }

        /// <summary>Square logo shown on the directory card and the listing byline.</summary>
        public string? LogoPath { get; set; }

        /// <summary>Wide header image for the dealership page.</summary>
        public string? BannerPath { get; set; }

        [Display(Name = "City / area")]
        [StringLength(80)]
        public string? City { get; set; }

        [Display(Name = "Country")]
        [StringLength(80)]
        public string? Country { get; set; }

        [Display(Name = "Address")]
        [StringLength(160)]
        public string? Address { get; set; }

        [Display(Name = "Phone")]
        [StringLength(40)]
        public string? Phone { get; set; }

        [Display(Name = "Website")]
        [StringLength(160)]
        public string? Website { get; set; }

        [Display(Name = "Opening hours")]
        [StringLength(200)]
        public string? OpeningHours { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public User? Owner { get; set; }

        public string Location =>
            string.Join(", ", new[] { City, Country }.Where(s => !string.IsNullOrWhiteSpace(s)));

        public string Logo => string.IsNullOrWhiteSpace(LogoPath) ? "/img/no-image.svg" : LogoPath;
    }
}
