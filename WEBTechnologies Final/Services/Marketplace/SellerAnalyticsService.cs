using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// The numbers behind the seller analytics page.
    ///
    /// Everything is computed from data that already exists - sale records, offer timestamps
    /// and the daily view buckets - so there is no reporting table to keep in step and no
    /// nightly job that can silently stop running.
    ///
    /// Series are returned CONTINUOUS: a month with no sales comes back as zero rather than
    /// being absent. A chart built from sparse rows draws a straight line between March and
    /// June and quietly claims business in April and May that never happened.
    /// </summary>
    public class SellerAnalyticsService
    {
        private readonly AppDbContext _db;

        public SellerAnalyticsService(AppDbContext db) => _db = db;

        public async Task<SellerAnalytics> ForSellerAsync(
            int sellerUserId, int months = 12, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var firstMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMonths(-(months - 1));

            var cars = _db.Cars.AsNoTracking().Where(c => c.OwnerId == sellerUserId);

            var totals = await cars
                .GroupBy(c => 1)
                .Select(g => new
                {
                    Listings   = g.Count(),
                    Active     = g.Count(c => c.Status == ListingStatus.Active),
                    Draft      = g.Count(c => c.Status == ListingStatus.Draft),
                    Reserved   = g.Count(c => c.Status == ListingStatus.Reserved),
                    Archived   = g.Count(c => c.Status == ListingStatus.Archived),
                    Sold       = g.Count(c => c.Status == ListingStatus.Sold),
                    Views      = g.Sum(c => c.ViewCount),
                    Revenue    = g.Where(c => c.Status == ListingStatus.Sold)
                                  .Sum(c => (decimal?)c.SoldPrice) ?? 0m,
                    AskingLive = g.Where(c => c.Status == ListingStatus.Active)
                                  .Sum(c => (decimal?)c.Price) ?? 0m
                })
                .FirstOrDefaultAsync(ct);

            var offerRows = await _db.Offers.AsNoTracking()
                .Where(o => cars.Any(c => c.Id == o.CarId))
                .GroupBy(o => 1)
                .Select(g => new
                {
                    Total    = g.Count(),
                    Pending  = g.Count(o => o.Status == OfferStatus.Pending),
                    Accepted = g.Count(o => o.Status == OfferStatus.Accepted),
                    Declined = g.Count(o => o.Status == OfferStatus.Declined)
                })
                .FirstOrDefaultAsync(ct);

            var favourites = await _db.UserFavoriteCars.AsNoTracking()
                .CountAsync(f => cars.Any(c => c.Id == f.CarId), ct);

            // ---- monthly series ----
            var soldByMonth = await cars
                .Where(c => c.Status == ListingStatus.Sold && c.SoldUtc >= firstMonth)
                .GroupBy(c => new { c.SoldUtc!.Value.Year, c.SoldUtc!.Value.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Count = g.Count(),
                    Revenue = g.Sum(c => c.SoldPrice ?? 0m)
                })
                .ToListAsync(ct);

            var offersByMonth = await _db.Offers.AsNoTracking()
                .Where(o => cars.Any(c => c.Id == o.CarId) && o.CreatedUtc >= firstMonth)
                .GroupBy(o => new { o.CreatedUtc.Year, o.CreatedUtc.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync(ct);

            var monthly = new List<MonthPoint>(months);
            for (var i = 0; i < months; i++)
            {
                var month = firstMonth.AddMonths(i);
                var sale = soldByMonth.FirstOrDefault(s => s.Year == month.Year && s.Month == month.Month);
                var offer = offersByMonth.FirstOrDefault(o => o.Year == month.Year && o.Month == month.Month);

                monthly.Add(new MonthPoint
                {
                    Month = month,
                    Revenue = sale?.Revenue ?? 0m,
                    Sales = sale?.Count ?? 0,
                    Offers = offer?.Count ?? 0
                });
            }

            // ---- daily views, last 30 days ----
            var firstDay = now.Date.AddDays(-29);
            var viewRows = await _db.ListingViewDaily.AsNoTracking()
                .Where(v => v.Day >= firstDay && cars.Any(c => c.Id == v.CarId))
                .GroupBy(v => v.Day)
                .Select(g => new { Day = g.Key, Count = g.Sum(v => v.Count) })
                .ToListAsync(ct);

            var daily = new List<DayPoint>(30);
            for (var i = 0; i < 30; i++)
            {
                var day = DateTime.SpecifyKind(firstDay.AddDays(i), DateTimeKind.Utc);
                daily.Add(new DayPoint
                {
                    Day = day,
                    Views = viewRows.FirstOrDefault(v => v.Day.Date == day.Date)?.Count ?? 0
                });
            }

            // ---- per-listing table ----
            var listings = await cars
                .Select(c => new ListingPerformance
                {
                    Id = c.Id,
                    Title = c.Year + " " + c.Make + " " + c.Model,
                    Image = c.ImagePaths.Count > 0 ? c.ImagePaths[0] : "/img/no-image.svg",
                    Status = c.Status,
                    Price = c.Price,
                    SoldPrice = c.SoldPrice,
                    Views = c.ViewCount,
                    Offers = _db.Offers.Count(o => o.CarId == c.Id),
                    Favourites = _db.UserFavoriteCars.Count(f => f.CarId == c.Id),
                    CreatedUtc = c.CreatedUtc,
                    SoldUtc = c.SoldUtc
                })
                .OrderByDescending(l => l.Views)
                .ToListAsync(ct);

            return new SellerAnalytics
            {
                Listings = totals?.Listings ?? 0,
                Active = totals?.Active ?? 0,
                Draft = totals?.Draft ?? 0,
                Reserved = totals?.Reserved ?? 0,
                Archived = totals?.Archived ?? 0,
                Sold = totals?.Sold ?? 0,
                TotalViews = totals?.Views ?? 0,
                Revenue = totals?.Revenue ?? 0m,
                LiveStockValue = totals?.AskingLive ?? 0m,
                TotalOffers = offerRows?.Total ?? 0,
                PendingOffers = offerRows?.Pending ?? 0,
                AcceptedOffers = offerRows?.Accepted ?? 0,
                DeclinedOffers = offerRows?.Declined ?? 0,
                Favourites = favourites,
                Monthly = monthly,
                Daily = daily,
                PerListing = listings
            };
        }
    }
}
