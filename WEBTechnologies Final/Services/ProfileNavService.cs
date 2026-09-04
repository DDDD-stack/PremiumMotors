using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using WEBTechnologies_Final.Data;
using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Fills in the counts the profile sub-navigation shows.
    ///
    /// Every controller that renders _ProfileNav calls this, rather than each view running its
    /// own queries — otherwise the same badge would show different numbers on different pages,
    /// and a page could easily be added that forgets them entirely and renders bare labels.
    ///
    /// Deliberately NOT a global filter: these are four extra queries, and the browse page —
    /// by far the most requested page on the site — does not need any of them.
    /// </summary>
    public class ProfileNavService
    {
        private readonly AppDbContext _db;

        public ProfileNavService(AppDbContext db) => _db = db;

        public async Task PopulateAsync(
            ViewDataDictionary viewData, int userId, User? user = null, CancellationToken ct = default)
        {
            user ??= await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

            viewData["ProfileUser"] = user;
            viewData["CountOffers"] = await _db.Offers.CountAsync(o => o.BuyerId == userId, ct);
            viewData["CountPurchases"] = await _db.Cars.CountAsync(
                c => c.SoldToUserId == userId
                     && (c.Status == ListingStatus.Reserved || c.Status == ListingStatus.Sold), ct);
            viewData["CountFavorites"] = await _db.UserFavoriteCars.CountAsync(f => f.UserId == userId, ct);
            viewData["CountUnread"] = await _db.Messages.CountAsync(
                m => m.ReadUtc == null && m.SenderId != userId
                     && (m.Conversation!.BuyerId == userId || m.Conversation.SellerId == userId), ct);

            // Only for sellers: a buyer has no reviews of their own to read, and the query
            // would be a wasted round trip on every profile page they open.
            if (user?.IsSeller == true)
                viewData["CountReviews"] = await _db.SellerReviews
                    .CountAsync(r => r.SellerUserId == userId, ct);
        }
    }
}
