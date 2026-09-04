namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// One row per listing per day, holding that day's view count.
    ///
    /// Car.ViewCount alone answers "how many views" but never "is interest rising or falling",
    /// which is the only version of the question a seller actually acts on. A daily bucket is
    /// the cheapest shape that answers both: one upsert per view, one row per active listing
    /// per day, and a trend line that needs no per-request event table.
    /// </summary>
    public class ListingViewDaily
    {
        public int Id { get; set; }
        public int CarId { get; set; }

        /// <summary>UTC midnight of the day being counted.</summary>
        public DateTime Day { get; set; }

        public int Count { get; set; }

        public Car? Car { get; set; }
    }
}
