using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// Who is selling each car, and whether its price has come down.
    ///
    /// Both are per-listing facts that live on other tables, and a results grid needs them for
    /// 24 cars at once. Fetching them one card at a time is the classic N+1: a page of
    /// listings would fire 48 extra queries to print a name and a badge. This loads both in
    /// two queries for the whole page, keyed by listing id.
    ///
    /// Views treat a missing entry as "no byline" rather than an error, so a page that does
    /// not build the lookup simply renders the card as it always did.
    /// </summary>
    public class ListingExtrasService
    {
        private readonly AppDbContext _db;

        public ListingExtrasService(AppDbContext db) => _db = db;

        public async Task<ListingExtras> ForCarsAsync(
            IEnumerable<Car> cars, CancellationToken ct = default)
        {
            var list = cars as IList<Car> ?? cars.ToList();
            var ownerIds = list.Where(c => c.OwnerId.HasValue)
                               .Select(c => c.OwnerId!.Value)
                               .Distinct()
                               .ToList();
            var carIds = list.Select(c => c.Id).ToList();

            var sellers = ownerIds.Count == 0
                ? new Dictionary<int, SellerBadge>()
                : (await (
                    from u in _db.Users.AsNoTracking()
                    where ownerIds.Contains(u.Id)
                    join d in _db.Dealerships.AsNoTracking() on u.Id equals d.OwnerUserId into ds
                    from d in ds.DefaultIfEmpty()
                    select new SellerBadge
                    {
                        UserId = u.Id,
                        Username = u.Username,
                        Name = d != null ? d.Name
                             : (u.SellerDisplayName ?? u.Username),
                        Type = u.SellerType,
                        AvatarPath = d != null && d.LogoPath != null ? d.LogoPath : u.AvatarPath,
                        RatingAverage = u.RatingAverage,
                        RatingCount = u.RatingCount,
                        DealershipSlug = d != null ? d.Slug : null,
                        Verified = u.SellerVerified
                    }).ToListAsync(ct)).ToDictionary(s => s.UserId);

            // The highest price a listing has ever been asked. Both columns matter: a change
            // row's Price is what it became, and PreviousPrice is what it was, so the very
            // first reduction has its original price ONLY in PreviousPrice. Taking the max of
            // Price alone missed exactly that case - the first price drop, which is the one
            // worth advertising. One grouped query for the page, not one per card.
            var drops = carIds.Count == 0
                ? new Dictionary<int, decimal>()
                : await _db.CarPriceChanges.AsNoTracking()
                    .Where(p => carIds.Contains(p.CarId))
                    .GroupBy(p => p.CarId)
                    .Select(g => new
                    {
                        CarId = g.Key,
                        High = g.Max(p => p.PreviousPrice != null && p.PreviousPrice > p.Price
                            ? p.PreviousPrice.Value
                            : p.Price)
                    })
                    .ToDictionaryAsync(x => x.CarId, x => x.High, ct);

            var effective = new Dictionary<int, decimal>();
            foreach (var car in list)
                if (drops.TryGetValue(car.Id, out var high) && high > car.Price)
                    effective[car.Id] = high;

            return new ListingExtras(sellers, effective);
        }

        public async Task<SellerBadge?> ForSellerAsync(int userId, CancellationToken ct = default)
        {
            var extras = await ForCarsAsync(new[] { new Car { OwnerId = userId } }, ct);
            return extras.Sellers.TryGetValue(userId, out var badge) ? badge : null;
        }
    }

    public record ListingExtras(
        IReadOnlyDictionary<int, SellerBadge> Sellers,
        IReadOnlyDictionary<int, decimal> PreviousPrices);

    /// <summary>
    /// The seller identity shown on a listing: who they are, whether they are a business, and
    /// what other buyers thought of them. A dealer's byline uses the dealership's name and
    /// logo; a private seller's uses their display name and avatar.
    /// </summary>
    public class SellerBadge
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public SellerType Type { get; set; }
        public string? AvatarPath { get; set; }
        public decimal RatingAverage { get; set; }
        public int RatingCount { get; set; }
        public string? DealershipSlug { get; set; }
        public bool Verified { get; set; }

        public bool IsDealer => Type == SellerType.Dealer;
        public bool HasRating => RatingCount > 0;

        public string TypeLabel => IsDealer ? "Dealership" : "Private seller";

        /// <summary>Two-letter monogram, used when there is no picture.</summary>
        public string Initials
        {
            get
            {
                var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "?";
                return parts.Length == 1
                    ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
                    : $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant();
            }
        }
    }
}
