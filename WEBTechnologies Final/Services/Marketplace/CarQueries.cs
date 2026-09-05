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
        /// <summary>
        /// Mixes paid listings into the free ones, one advert every
        /// everyNth slots until the adverts run out, after which the
        /// rest of the page is free listings in their ordinary order.
        ///
        /// Note this cannot make adverts a majority of the page, and deliberately so - that
        /// would need the same few cars repeated down the grid, which reads as a broken page
        /// rather than a busy one and buries exactly the sellers this is meant to protect.
        /// </summary>
        public static List<Car> MixPromoted(
            IReadOnlyList<Car> free, IReadOnlyList<Car> promoted, int everyNth)
        {
            if (promoted.Count == 0) return free.ToList();

            var mixed = new List<Car>(free.Count + promoted.Count);
            var nextPromoted = 0;
            var nextFree = 0;

            while (nextPromoted < promoted.Count || nextFree < free.Count)
            {
                var slotWantsAnAdvert = mixed.Count % everyNth == 0;

                if (slotWantsAnAdvert && nextPromoted < promoted.Count)
                    mixed.Add(promoted[nextPromoted++]);
                else if (nextFree < free.Count)
                    mixed.Add(free[nextFree++]);
                else
                    mixed.Add(promoted[nextPromoted++]);
            }

            return mixed;
        }

    }
}
