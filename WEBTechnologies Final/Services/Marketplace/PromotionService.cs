using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// The only writer of paid placement.
    ///
    /// Two things have to move together every time: the Promotion row that is the receipt,
    /// and the two cached fields on Car that every marketplace query actually reads. Split
    /// that across the four or five places that will eventually grant a placement - an admin
    /// action, a checkout callback, an API, a refund - and they will drift, and the symptom
    /// is a seller who paid and whose car is not showing, or a car showing that nobody paid
    /// for. Both are silent until somebody complains.
    /// </summary>
    public class PromotionService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<PromotionService> _log;

        public PromotionService(AppDbContext db, ILogger<PromotionService> log)
        {
            _db = db;
            _log = log;
        }

        /// <summary>Longest single placement that can be sold. See Grant.</summary>
        public const int MaxDays = 365;

        /// <summary>
        /// Starts a placement, ending any that is already running on the same listing.
        ///
        /// Replacing rather than extending is deliberate: "add 7 days to whatever is left" is
        /// impossible to explain on a receipt, and the admin screen says "Replace" for
        /// exactly this reason.
        /// </summary>
        public async Task<Promotion?> GrantAsync(
            int carId, PromotionTier tier, int days, int? grantedByUserId,
            decimal? priceEur = null, string? note = null, CancellationToken ct = default)
        {
            if (tier == PromotionTier.None)
                throw new ArgumentException("Use EndAsync to remove a placement.", nameof(tier));

            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == carId, ct);
            if (car is null) return null;

            // A year is far longer than anything anyone should be sold in one go. The clamp is
            // here to stop a typo in the days box parking a car on the front page until 2094.
            days = Math.Clamp(days, 1, MaxDays);

            var now = DateTime.UtcNow;
            var reference = await NextFreeReferenceAsync(ct);

            await CloseLiveAsync(carId, now, $"Replaced by {reference}", ct);

            var promotion = new Promotion
            {
                Reference = reference,
                CarId = car.Id,
                CarTitle = Truncate(car.Title, 200),
                SellerUserId = car.OwnerId,
                Tier = tier,
                StartedUtc = now,
                EndsUtc = now.AddDays(days),
                GrantedByUserId = grantedByUserId,
                PriceEur = priceEur,
                Note = string.IsNullOrWhiteSpace(note) ? null : Truncate(note, 500)
            };

            _db.Promotions.Add(promotion);

            car.PromotionTier = tier;
            car.PromotedUntilUtc = promotion.EndsUtc;

            await _db.SaveChangesAsync(ct);

            // Logged at Information because this is the record of a sale. If the receipt table
            // is ever lost or a migration goes wrong, the log is the only other copy.
            _log.LogInformation(
                "Promotion {Reference} granted: car {CarId}, tier {Tier}, until {EndsUtc:o}",
                promotion.Reference, car.Id, tier, promotion.EndsUtc);

            return promotion;
        }

        /// <summary>
        /// Stops a placement now. The tier is cleared as well as the date, so the listing does
        /// not look like a promotion that merely lapsed, and the receipt records that it was
        /// cut short rather than being deleted.
        /// </summary>
        public async Task<bool> EndAsync(int carId, string reason, CancellationToken ct = default)
        {
            var car = await _db.Cars.FirstOrDefaultAsync(c => c.Id == carId, ct);
            if (car is null) return false;

            await CloseLiveAsync(carId, DateTime.UtcNow, reason, ct);

            car.PromotionTier = PromotionTier.None;
            car.PromotedUntilUtc = null;

            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Promotion ended on car {CarId}: {Reason}", carId, reason);
            return true;
        }

        /// <summary>
        /// Looks up a receipt from whatever the seller quoted. Returns null rather than
        /// throwing on a code that cannot exist, because "not found" is the same answer to
        /// the admin either way.
        /// </summary>
        public async Task<Promotion?> FindByReferenceAsync(string? input, CancellationToken ct = default)
        {
            var reference = PromotionReference.Normalise(input);
            if (reference is null) return null;

            return await _db.Promotions
                .AsNoTracking()
                .Include(p => p.Car)
                .FirstOrDefaultAsync(p => p.Reference == reference, ct);
        }

        /// <summary>Everything ever sold on one listing, newest first.</summary>
        public Task<List<Promotion>> HistoryForCarAsync(int carId, CancellationToken ct = default) =>
            _db.Promotions
                .AsNoTracking()
                .Where(p => p.CarId == carId)
                .OrderByDescending(p => p.StartedUtc)
                .ToListAsync(ct);

        private async Task CloseLiveAsync(int carId, DateTime now, string reason, CancellationToken ct)
        {
            var live = await _db.Promotions
                .Where(p => p.CarId == carId && p.EndedEarlyUtc == null && p.EndsUtc > now)
                .ToListAsync(ct);

            foreach (var p in live)
            {
                p.EndedEarlyUtc = now;
                p.EndedReason = Truncate(reason, 200);
            }
        }

        /// <summary>
        /// A collision is vanishingly unlikely and catastrophically confusing if it happens -
        /// two sellers quoting the same code - so it is checked rather than assumed. Ten
        /// attempts then a hard failure: if the RNG is broken, an exception at the point of
        /// sale beats silently issuing a duplicate.
        /// </summary>
        private async Task<string> NextFreeReferenceAsync(CancellationToken ct)
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var candidate = PromotionReference.Next();
                if (!await _db.Promotions.AnyAsync(p => p.Reference == candidate, ct))
                    return candidate;

                _log.LogWarning("Promotion reference collision on {Reference}", candidate);
            }

            throw new InvalidOperationException(
                "Could not generate a unique promotion reference after 10 attempts.");
        }

        private static string Truncate(string? value, int max) =>
            string.IsNullOrEmpty(value) ? string.Empty
            : value.Length <= max ? value
            : value[..max];
    }
}
