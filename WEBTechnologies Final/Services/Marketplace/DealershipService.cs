using System.Text;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// Dealer shopfronts: the browsable directory, one dealership's page, and keeping a
    /// dealership in existence for every business account.
    ///
    /// A dealership's listings are DERIVED (Car.OwnerId == Dealership.OwnerUserId) rather than
    /// linked by a Car.DealershipId column. One less foreign key, and more importantly one
    /// less thing that can disagree with Car.OwnerId about who is actually selling the car.
    /// </summary>
    public class DealershipService
    {
        private readonly AppDbContext _db;

        public DealershipService(AppDbContext db) => _db = db;

        /// <summary>
        /// Guarantees the business account has a shopfront, seeded from the details already
        /// collected at signup. Idempotent: safe to call on the signup path and from the
        /// startup backfill for accounts that predate the feature.
        /// </summary>
        public async Task<Dealership> EnsureForAsync(User user, CancellationToken ct = default)
        {
            var existing = await _db.Dealerships
                .FirstOrDefaultAsync(d => d.OwnerUserId == user.Id, ct);
            if (existing is not null) return existing;

            var name = string.IsNullOrWhiteSpace(user.SellerDisplayName)
                ? user.Username
                : user.SellerDisplayName!;

            var dealership = new Dealership
            {
                OwnerUserId = user.Id,
                Name = name,
                Slug = await UniqueSlugAsync(name, ct),
                City = user.SellerLocation,
                Address = user.BusinessAddress,
                Website = user.Website,
                Phone = user.Phone,
                CreatedUtc = DateTime.UtcNow
            };

            _db.Dealerships.Add(dealership);
            await _db.SaveChangesAsync(ct);
            return dealership;
        }

        public Task<Dealership?> ForOwnerAsync(int userId, CancellationToken ct = default) =>
            _db.Dealerships.FirstOrDefaultAsync(d => d.OwnerUserId == userId, ct);

        public Task<Dealership?> BySlugAsync(string slug, CancellationToken ct = default) =>
            _db.Dealerships
                .Include(d => d.Owner)
                .FirstOrDefaultAsync(d => d.Slug == slug, ct);

        /// <summary>
        /// The directory. The listing counts are computed in the database inside one projected
        /// query rather than per row: a page of 24 dealerships would otherwise be 24 extra
        /// round trips just to print "12 cars".
        /// </summary>
        public async Task<(List<DealershipCard> Items, int Total)> DirectoryAsync(
            string? search, string? city, string? sort, int page, int pageSize,
            CancellationToken ct = default)
        {
            var query = _db.Dealerships.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(d =>
                    EF.Functions.ILike(d.Name, "%" + s + "%") ||
                    (d.City != null && EF.Functions.ILike(d.City, "%" + s + "%")));
            }

            if (!string.IsNullOrWhiteSpace(city))
                query = query.Where(d => d.City == city);

            var projected =
                from d in query
                join u in _db.Users.AsNoTracking() on d.OwnerUserId equals u.Id
                select new DealershipCard
                {
                    Id = d.Id,
                    Slug = d.Slug,
                    Name = d.Name,
                    City = d.City,
                    Country = d.Country,
                    LogoPath = d.LogoPath,
                    CreatedUtc = d.CreatedUtc,
                    RatingAverage = u.RatingAverage,
                    RatingCount = u.RatingCount,
                    ListingCount = _db.Cars.Count(c =>
                        c.OwnerId == d.OwnerUserId && c.Status == ListingStatus.Active),
                    SoldCount = _db.Cars.Count(c =>
                        c.OwnerId == d.OwnerUserId && c.Status == ListingStatus.Sold)
                };

            projected = sort switch
            {
                "rating" => projected.OrderByDescending(d => d.RatingAverage)
                                     .ThenByDescending(d => d.RatingCount),
                "newest" => projected.OrderByDescending(d => d.CreatedUtc),
                "name"   => projected.OrderBy(d => d.Name),
                // Default puts dealers with stock first. A directory whose top result has
                // nothing to sell is a directory nobody opens twice.
                _        => projected.OrderByDescending(d => d.ListingCount)
                                     .ThenByDescending(d => d.RatingAverage)
            };

            var total = await projected.CountAsync(ct);
            var items = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return (items, total);
        }

        public Task<List<string>> CitiesAsync(CancellationToken ct = default) =>
            _db.Dealerships.AsNoTracking()
                .Where(d => d.City != null && d.City != "")
                .Select(d => d.City!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync(ct);

        /// <summary>
        /// Backfill for business accounts created before dealerships existed. Runs once at
        /// startup and is a no-op on every boot after the first.
        /// </summary>
        public async Task<int> BackfillAsync(CancellationToken ct = default)
        {
            var missing = await _db.Users
                .Where(u => u.SellerType == SellerType.Dealer)
                .Where(u => !_db.Dealerships.Any(d => d.OwnerUserId == u.Id))
                .ToListAsync(ct);

            foreach (var user in missing) await EnsureForAsync(user, ct);
            return missing.Count;
        }

        /// <summary>
        /// Slugs are generated once and then left alone. A dealer who renames keeps their
        /// original URL, because that URL is already on their business cards and in whatever
        /// they have advertised.
        /// </summary>
        private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
        {
            var slug = Slugify(name);
            if (slug.Length == 0) slug = "dealer";

            var candidate = slug;
            var suffix = 2;
            while (await _db.Dealerships.AnyAsync(d => d.Slug == candidate, ct))
                candidate = slug + "-" + suffix++;

            return candidate;
        }

        public static string Slugify(string value)
        {
            var sb = new StringBuilder(value.Length);
            var lastWasDash = false;

            foreach (var ch in value.Trim().ToLowerInvariant())
            {
                if (ch < 128 && char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    lastWasDash = false;
                }
                else if (!lastWasDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }

            return sb.ToString().Trim('-');
        }
    }

    /// <summary>One row of the dealership directory, projected in the database.</summary>
    public class DealershipCard
    {
        public int Id { get; set; }
        public string Slug { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? LogoPath { get; set; }
        public DateTime CreatedUtc { get; set; }
        public decimal RatingAverage { get; set; }
        public int RatingCount { get; set; }
        public int ListingCount { get; set; }
        public int SoldCount { get; set; }

        public string Logo => string.IsNullOrWhiteSpace(LogoPath) ? "/img/no-image.svg" : LogoPath;

        public string Location =>
            string.Join(", ", new[] { City, Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
