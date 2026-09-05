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
        /// Interleaves paid listings with free ones in repeating blocks:
        /// <paramref name="promotedPerBlock"/> adverts, then <paramref name="freePerBlock"/>
        /// free listings, over and over. Whichever side runs out first drops out, and the
        /// rest of the page is the other side in its ordinary order.
        ///
        /// Both sides keep the order they arrived in, and every listing handed in comes out
        /// exactly once. That is the property worth protecting: a bug here either repeats a
        /// car or drops one, and neither gets reported - the seller simply gets nothing for
        /// their money, or nothing for being on the site at all.
        ///
        /// The ratio is a commercial decision, not a technical one, and it is passed in
        /// rather than assumed here so it can be changed in one place and tested.
        /// </summary>
        public static List<Car> MixPromoted(
            IReadOnlyList<Car> free, IReadOnlyList<Car> promoted,
            int promotedPerBlock, int freePerBlock)
        {
            // Two zero-length blocks would loop forever without adding anything. Cheaper to
            // fail loudly at the call site than to hang a page request.
            if (promotedPerBlock < 1)
                throw new ArgumentOutOfRangeException(nameof(promotedPerBlock), "Must be at least 1.");
            if (freePerBlock < 1)
                throw new ArgumentOutOfRangeException(nameof(freePerBlock), "Must be at least 1.");

            if (promoted.Count == 0) return free.ToList();

            var mixed = new List<Car>(free.Count + promoted.Count);
            var nextPromoted = 0;
            var nextFree = 0;

            while (nextPromoted < promoted.Count || nextFree < free.Count)
            {
                for (var i = 0; i < promotedPerBlock && nextPromoted < promoted.Count; i++)
                    mixed.Add(promoted[nextPromoted++]);

                for (var i = 0; i < freePerBlock && nextFree < free.Count; i++)
                    mixed.Add(free[nextFree++]);
            }

            return mixed;
        }
    }
}
