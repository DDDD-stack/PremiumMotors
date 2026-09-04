using WEBTechnologies_Final.Services.Marketplace;

namespace WEBTechnologies_Final.Models
{
    public class DealershipListViewModel
    {
        public List<DealershipCard> Dealerships { get; set; } = new();
        public string? Search { get; set; }
        public string? City { get; set; }
        public string Sort { get; set; } = "stock";
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 24;
        public int TotalCount { get; set; }
        public List<string> Cities { get; set; } = new();

        public int TotalPages => (int)Math.Ceiling(TotalCount / (double)Math.Max(PageSize, 1));
        public bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrWhiteSpace(City);
    }

    /// <summary>
    /// One shape serves both a dealership page and a private seller page. The two differ in
    /// what they show (a dealership has an address and opening hours; a private seller does
    /// not) but not in what they ARE - a seller, their stock and their reputation - so giving
    /// them separate models would mean maintaining the same page twice.
    /// </summary>
    public class PublicSellerViewModel
    {
        public Dealership? Dealership { get; set; }

        public int OwnerId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public SellerType SellerType { get; set; }
        public string? AvatarPath { get; set; }
        public string? Location { get; set; }
        public DateTime MemberSince { get; set; }
        public bool Verified { get; set; }

        public decimal RatingAverage { get; set; }
        public int RatingCount { get; set; }

        public List<Car> Cars { get; set; } = new();
        public List<SellerReview> Reviews { get; set; } = new();
        public int[] Distribution { get; set; } = new int[5];

        public string Tab { get; set; } = "stock";

        public bool IsDealer => SellerType == SellerType.Dealer;
        public int ActiveCount => Cars.Count(c => c.Status == ListingStatus.Active);
        public int SoldCount => Cars.Count(c => c.Status == ListingStatus.Sold);

        public string Initials
        {
            get
            {
                var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "?";
                return parts.Length == 1
                    ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
                    : $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
            }
        }
    }

    public class LeaveReviewViewModel
    {
        public int CarId { get; set; }
        public string CarTitle { get; set; } = string.Empty;
        public string CarImage { get; set; } = "/img/no-image.svg";
        public string SellerName { get; set; } = string.Empty;
        public bool SellerIsDealer { get; set; }
        public decimal? SoldPrice { get; set; }
        public DateTime? SoldUtc { get; set; }

        public int Rating { get; set; } = 5;
        public string? Comment { get; set; }
    }
}
