using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// Counts listing views: a running total on the listing and a daily bucket for the trend.
    ///
    /// DEDUPLICATION IS THE CALLER'S JOB and it is not optional. Without it a refresh, a
    /// back-button, or one bored seller inflates the number until it means nothing - and every
    /// figure on the analytics page is built on it. CarsController marks the listing in the
    /// visitor's session and only calls this the first time in that session.
    ///
    /// A seller's own visits are never counted. Sellers open their own listings constantly to
    /// check how they look, and counting that would make the trend line a graph of the
    /// seller's own anxiety.
    /// </summary>
    public class ListingViewService
    {
        private readonly AppDbContext _db;

        public ListingViewService(AppDbContext db) => _db = db;

        public async Task RecordAsync(int carId, CancellationToken ct = default)
        {
            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == carId, ct);
            if (car is null) return;

            car.ViewCount++;

            var day = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
            var bucket = await _db.ListingViewDaily
                .FirstOrDefaultAsync(v => v.CarId == carId && v.Day == day, ct);

            if (bucket is null)
                _db.ListingViewDaily.Add(new ListingViewDaily { CarId = carId, Day = day, Count = 1 });
            else
                bucket.Count++;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Two first-of-the-day views can race and both try to INSERT the bucket; the
                // unique index on (CarId, Day) rejects the loser. Losing a single view is not
                // worth surfacing, and retrying properly would need a second context - the
                // one here is already in a failed state.
            }
        }
    }
}
