using WEBTechnologies_Final.Models;

namespace WEBTechnologies_Final.Services.Marketplace
{
    /// <summary>
    /// Query fragments that have to agree with the derived properties on <see cref="Car"/>.
    ///
    /// Car.IsPromoted is a C# property, so EF cannot translate it and every query would
    /// otherwise open-code the same three conditions. When they drift - and they do - the
    /// symptom is an expired promotion still showing in one place and gone in another, which
    /// is the sort of bug nobody reports and everybody notices.
    /// </summary>
    public static class CarQueries
    {
        /// <summary>
        /// Listings with live paid placement at <paramref name="minimum"/> or above. Must stay
        /// in step with <see cref="Car.IsPromoted"/>.
        /// </summary>
        public static IQueryable<Car> WherePromoted(
            this IQueryable<Car> query, PromotionTier minimum, DateTime nowUtc) =>
            query.Where(c => c.PromotionTier >= minimum
                             && c.PromotedUntilUtc != null
                             && c.PromotedUntilUtc > nowUtc
                             && c.Status == ListingStatus.Active);

        /// <summary>
        /// Most expensive placement first, then newest. Within a tier the order is arbitrary
        /// but stable; if promoted slots are ever oversubscribed this is where a rotation
        /// would go, so that everyone who paid gets seen rather than whoever listed last.
        /// </summary>
        public static IQueryable<Car> OrderByPromotion(this IQueryable<Car> query) =>
            query.OrderByDescending(c => c.PromotionTier).ThenByDescending(c => c.Id);
    }
}
