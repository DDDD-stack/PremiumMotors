using WEBTechnologies_Final.Services.Marketplace;

namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// Numbers shown on the two front pages. They are read live rather than hard-coded
    /// because a marketing claim that contradicts the marketplace one click away is worse
    /// than no claim at all — if there are four listings, the page says four.
    /// </summary>
    public class MarketplaceStats
    {
        public int ActiveListings { get; set; }
        public int SoldCount { get; set; }
        public int DealershipCount { get; set; }
        public int SellerCount { get; set; }

        /// <summary>
        /// True once there is enough activity for the figures to read as a marketplace
        /// rather than as an empty room. Below this the pages show the story instead.
        /// </summary>
        public bool WorthShowing => ActiveListings >= 3;
    }

    /// <summary>The consumer front page: what this is, why it is different, and three ways in.</summary>
    public class HomeLandingViewModel
    {
        /// <summary>
        /// Real listings, not placeholders. These are one of the three routes into the
        /// marketplace — clicking any card goes straight to that car.
        /// </summary>
        public List<Car> Featured { get; set; } = new();

        /// <summary>
        /// Top-tier paid placement. The most expensive thing on the rate card, because it is
        /// the only inventory seen before a visitor has decided what they are looking for.
        /// Always labelled as advertising where it renders.
        /// </summary>
        public List<Car> Promoted { get; set; } = new();

        public MarketplaceStats Stats { get; set; } = new();
    }

    /// <summary>
    /// The business front page. Same product, different argument: a dealer does not care
    /// that offers are private, they care what it does to their margin and their admin.
    /// </summary>
    public class BusinessLandingViewModel
    {
        public List<DealershipCard> Dealerships { get; set; } = new();

        /// <summary>
        /// Top-tier paid placement. The most expensive thing on the rate card, because it is
        /// the only inventory seen before a visitor has decided what they are looking for.
        /// Always labelled as advertising where it renders.
        /// </summary>
        public List<Car> Promoted { get; set; } = new();


        public MarketplaceStats Stats { get; set; } = new();
    }
}
