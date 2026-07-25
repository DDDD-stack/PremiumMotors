using System.ComponentModel.DataAnnotations;

namespace WEBTechnologies_Final.Models
{

    public class Car
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Make")]
        public string Make { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Model")]
        public string Model { get; set; } = string.Empty;

        [Display(Name = "Body Type")]
        public CarType Type { get; set; }

        [Range(1900, 2100)]
        [Display(Name = "Model Year")]
        public int Year { get; set; }

        [Display(Name = "Description")]
        public string Description { get; set; } = string.Empty;

        [Range(0, double.MaxValue, ErrorMessage = "Asking price must be a positive number.")]
        [Display(Name = "Asking Price")]
        [DataType(DataType.Currency)]
        public decimal StartingPrice { get; set; }

        public List<string> ImagePaths { get; set; } = new();

        [Required(ErrorMessage = "An auction end date is required.")]
        [Display(Name = "Auction Ends")]
        public DateTime? AuctionEnd { get; set; }

        // Country the listing is for (used for filtering and, later, rental business verification).
        [Required]
        [Display(Name = "Country")]
        public string Country { get; set; } = string.Empty;

        // The user who listed the car. Admin "house" listings may leave this null.
        public string? OwnerUsername { get; set; }

        // A listing only appears publicly once its listing fee is paid (or it's an admin listing).
        public bool IsPublished { get; set; }

        public List<Bid> Bids { get; set; } = new();

        public bool IsSold { get; set; }
        public string? SoldTo { get; set; }

        // Set once the auto-close sweep has resolved this auction (winner picked or token
        // released), so closure is processed exactly once.
        public bool ClosureProcessed { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public string Title => $"{Year} {Make} {Model}";

        // Offers are private to the seller, so the highest offer is not part of the public view model.
        public Bid? HighestBid =>
            Bids.Count == 0 ? null : Bids.OrderByDescending(b => b.Amount).First();

        public bool IsClosed => IsSold || (AuctionEnd.HasValue && AuctionEnd.Value <= DateTime.Now);

        public string PrimaryImage =>
            ImagePaths.Count > 0 ? ImagePaths[0] : "/img/no-image.svg";
    }
}
