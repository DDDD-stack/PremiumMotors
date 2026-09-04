namespace WEBTechnologies_Final.Models
{
    public class MonthPoint
    {
        public DateTime Month { get; set; }
        public decimal Revenue { get; set; }
        public int Sales { get; set; }
        public int Offers { get; set; }

        public string Label => Month.ToString("MMM");
        public string LongLabel => Month.ToString("MMMM yyyy");
    }

    public class DayPoint
    {
        public DateTime Day { get; set; }
        public int Views { get; set; }
        public string Label => Day.ToString("d MMM");
    }

    public class ListingPerformance
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Image { get; set; } = "/img/no-image.svg";
        public ListingStatus Status { get; set; }
        public decimal Price { get; set; }
        public decimal? SoldPrice { get; set; }
        public int Views { get; set; }
        public int Offers { get; set; }
        public int Favourites { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? SoldUtc { get; set; }

        /// <summary>
        /// Views per offer is the number that tells a seller WHICH problem they have: plenty
        /// of views and no offers is a price problem, no views at all is a photo or title
        /// problem. Null until there is enough traffic for the ratio to mean anything.
        /// </summary>
        public double? ViewsPerOffer => Offers == 0 ? null : (double)Views / Offers;
    }

    public class SellerAnalytics
    {
        public int Listings { get; set; }
        public int Active { get; set; }
        public int Draft { get; set; }
        public int Reserved { get; set; }
        public int Archived { get; set; }
        public int Sold { get; set; }

        public int TotalViews { get; set; }
        public int Favourites { get; set; }

        public decimal Revenue { get; set; }
        public decimal LiveStockValue { get; set; }

        public int TotalOffers { get; set; }
        public int PendingOffers { get; set; }
        public int AcceptedOffers { get; set; }
        public int DeclinedOffers { get; set; }

        public List<MonthPoint> Monthly { get; set; } = new();
        public List<DayPoint> Daily { get; set; } = new();
        public List<ListingPerformance> PerListing { get; set; } = new();

        public decimal AverageSalePrice => Sold == 0 ? 0m : Math.Round(Revenue / Sold, 0);

        /// <summary>Of the offers that were answered, how many were accepted.</summary>
        public double? AcceptanceRate =>
            AcceptedOffers + DeclinedOffers == 0
                ? null
                : (double)AcceptedOffers / (AcceptedOffers + DeclinedOffers);

        public double? OffersPerListing =>
            Listings == 0 ? null : (double)TotalOffers / Listings;

        public decimal BestMonthRevenue =>
            Monthly.Count == 0 ? 0m : Monthly.Max(m => m.Revenue);

        public int PeakDailyViews =>
            Daily.Count == 0 ? 0 : Daily.Max(d => d.Views);

        public int ViewsLast30 => Daily.Sum(d => d.Views);
    }
}

namespace WEBTechnologies_Final.Models
{
    public record ChartPoint(string Label, double Value, string Display);

    /// <summary>
    /// A chart's data, kept separate from how it is drawn.
    ///
    /// The charts are hand-rolled inline SVG rather than a charting library, for three
    /// reasons that all matter here: the artifact CSP would have to allow another CDN script,
    /// a library is 60-200 KB to draw twelve bars, and inline SVG inherits the page's own
    /// colour tokens so the charts follow the theme for free.
    /// </summary>
    public class ChartSeries
    {
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public List<ChartPoint> Points { get; set; } = new();

        /// <summary>A CSS colour, usually one of the --pm-* tokens.</summary>
        public string Accent { get; set; } = "var(--pm-primary)";

        public double Max => Points.Count == 0 ? 0 : Points.Max(p => p.Value);
        public bool IsEmpty => Points.Count == 0 || Max <= 0;

        /// <summary>Bar height as a percentage of the tallest bar. Zero stays zero.</summary>
        public double Percent(ChartPoint point) =>
            Max <= 0 ? 0 : Math.Round(point.Value / Max * 100, 2);
    }
}
