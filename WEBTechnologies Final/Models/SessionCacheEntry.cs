namespace WEBTechnologies_Final.Models
{
    /// <summary>
    /// One website session, stored in Postgres rather than in the process.
    ///
    /// AddDistributedMemoryCache is not distributed despite the name: it is a dictionary in
    /// the process. Every restart signed every web user out, and two instances behind a load
    /// balancer would have signed people out at random as requests bounced between them. The
    /// mobile app was never affected - JWT is stateless - which is exactly why the problem
    /// could sit unnoticed.
    ///
    /// Postgres rather than Redis because the database already exists, is already backed up
    /// and is already paid for. Redis would be faster; a second piece of infrastructure to
    /// provision, secure and monitor is not obviously worth it at this size.
    /// </summary>
    public class SessionCacheEntry
    {
        /// <summary>The cache key. Session ids are GUID strings.</summary>
        public string Id { get; set; } = string.Empty;

        public byte[] Value { get; set; } = Array.Empty<byte>();

        /// <summary>When this entry stops being valid, sliding renewals included.</summary>
        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>Sliding window in seconds, if the entry has one.</summary>
        public double? SlidingSeconds { get; set; }

        /// <summary>A hard ceiling that sliding renewal can never push past.</summary>
        public DateTime? AbsoluteExpirationUtc { get; set; }
    }
}
