using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// Turning a buyer account into a seller account, and the numbers behind the seller panel.
    ///
    /// Seller is a capability flag on the user rather than a role, deliberately: a seller keeps
    /// every buyer ability (browsing, favourites, making offers on other people's cars), and a
    /// role swap would have taken those away.
    ///
    /// PLACEHOLDER SCOPE: <see cref="User.SellerVerified"/> is never set here. Dealer accounts
    /// will need document checks (business registration, VAT number, ID) before they can be
    /// marked verified, and no upload or review flow exists yet.
    /// </summary>
    public class SellerService
    {
        private readonly AppDbContext _db;

        public SellerService(AppDbContext db) => _db = db;

        /// <summary>
        /// Opts an account into selling. Idempotent-ish: calling it on an existing seller
        /// fails rather than silently rewriting their profile, so an accidental double submit
        /// cannot clear a display name.
        /// </summary>
        public async Task<MarketplaceResult<User>> BecomeSellerAsync(
            int userId, SellerType type, string? displayName, string? location,
            CancellationToken ct = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return MarketplaceResult<User>.Fail("Account not found.", MarketplaceCodes.NotFound);

            if (user.IsSeller)
                return MarketplaceResult<User>.Fail(
                    "This account is already a seller account.", MarketplaceCodes.AlreadySeller);

            user.IsSeller = true;
            user.SellerSinceUtc = DateTime.UtcNow;
            user.SellerType = type;
            user.SellerDisplayName = Trim(displayName, 80);
            user.SellerLocation = Trim(location, 80);

            await _db.SaveChangesAsync(ct);
            return MarketplaceResult<User>.Ok(user);
        }

        public async Task<MarketplaceResult<User>> UpdateProfileAsync(
            int userId, SellerType type, string? displayName, string? location,
            CancellationToken ct = default)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return MarketplaceResult<User>.Fail("Account not found.", MarketplaceCodes.NotFound);

            if (!user.IsSeller)
                return MarketplaceResult<User>.Fail(
                    "This account is not a seller account.", MarketplaceCodes.NotSeller);

            user.SellerType = type;
            user.SellerDisplayName = Trim(displayName, 80);
            user.SellerLocation = Trim(location, 80);

            await _db.SaveChangesAsync(ct);
            return MarketplaceResult<User>.Ok(user);
        }

        /// <summary>Headline numbers for the seller panel, in one round trip per figure.</summary>
        public async Task<SellerDashboard> GetDashboardAsync(int userId, CancellationToken ct = default)
        {
            var listings = _db.Cars.Where(c => c.OwnerId == userId);

            var active = await listings.CountAsync(c => c.Status == ListingStatus.Active, ct);
            var drafts = await listings.CountAsync(c => c.Status == ListingStatus.Draft, ct);
            var reserved = await listings.CountAsync(c => c.Status == ListingStatus.Reserved, ct);
            var sold = await listings.CountAsync(c => c.Status == ListingStatus.Sold, ct);

            var offers = _db.Offers.Where(o => o.Car!.OwnerId == userId);
            var pendingOffers = await offers.CountAsync(o => o.Status == OfferStatus.Pending, ct);
            var totalOffers = await offers.CountAsync(ct);

            var unread = await _db.Messages.CountAsync(
                m => m.ReadUtc == null && m.SenderId != userId && m.Conversation!.SellerId == userId, ct);

            var soldValue = await listings
                .Where(c => c.Status == ListingStatus.Sold && c.SoldPrice != null)
                .SumAsync(c => c.SoldPrice ?? 0m, ct);

            return new SellerDashboard(
                active, drafts, reserved, sold, pendingOffers, totalOffers, unread, soldValue);
        }

        /// <summary>Listings owned by this seller, newest first, with their offer counts.</summary>
        public async Task<List<SellerListingRow>> GetListingsAsync(
            int userId, ListingStatus? status = null, CancellationToken ct = default)
        {
            var query = _db.Cars.Where(c => c.OwnerId == userId);
            if (status.HasValue) query = query.Where(c => c.Status == status.Value);

            var rows = await query
                .OrderByDescending(c => c.Id)
                .Select(c => new
                {
                    Car = c,
                    Offers = c.Offers.Count,
                    Pending = c.Offers.Count(o => o.Status == OfferStatus.Pending),
                    Best = c.Offers.Where(o => o.Status == OfferStatus.Pending)
                        .Max(o => (decimal?)o.Amount)
                })
                .ToListAsync(ct);

            return rows.Select(r => new SellerListingRow(r.Car, r.Offers, r.Pending, r.Best)).ToList();
        }

        /// <summary>The seller's offer inbox across every listing they own.</summary>
        public async Task<List<Offer>> GetOffersAsync(
            int userId, OfferStatus? status = null, CancellationToken ct = default)
        {
            var query = _db.Offers.Include(o => o.Car).Where(o => o.Car!.OwnerId == userId);
            if (status.HasValue) query = query.Where(o => o.Status == status.Value);

            return await query
                // Pending first, then most recent, so the inbox opens on what needs an answer.
                .OrderBy(o => o.Status == OfferStatus.Pending ? 0 : 1)
                .ThenByDescending(o => o.CreatedUtc)
                .ToListAsync(ct);
        }

        private static string? Trim(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            return value.Length <= max ? value : value[..max];
        }
    }

    public record SellerDashboard(
        int ActiveListings,
        int Drafts,
        int Reserved,
        int Sold,
        int PendingOffers,
        int TotalOffers,
        int UnreadMessages,
        decimal SoldValue);

    public record SellerListingRow(Car Car, int OfferCount, int PendingOffers, decimal? BestPendingOffer);
}
