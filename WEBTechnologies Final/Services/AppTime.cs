namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Every instant in the database is UTC. This is the only place that converts between UTC
    /// and the timezone users think in.
    ///
    /// Before this existed, AuctionEnd was stored as local wall-clock while every other
    /// timestamp was UTC, and Car.IsClosed compared it against DateTime.Now. That happened to
    /// work on a developer laptop in CET and would have closed auctions at the wrong time on a
    /// UTC server or after a daylight-saving shift.
    ///
    /// Configured once at startup from App:DisplayTimeZone. Static because Razor views need it
    /// and threading a service through every view for a pure function is not worth it.
    /// </summary>
    public static class AppTime
    {
        // Albania is CET/CEST. Windows and Linux use different ids for the same zone, so both
        // are tried before falling back to UTC.
        private static readonly string[] DefaultZoneIds =
            { "Central European Standard Time", "Europe/Tirane", "Europe/Berlin" };

        public static TimeZoneInfo DisplayZone { get; private set; } = Resolve(null);

        public static DateTime UtcNow => DateTime.UtcNow;

        public static void Configure(string? timeZoneId) => DisplayZone = Resolve(timeZoneId);

        private static TimeZoneInfo Resolve(string? id)
        {
            var candidates = string.IsNullOrWhiteSpace(id)
                ? DefaultZoneIds
                : new[] { id }.Concat(DefaultZoneIds).ToArray();

            foreach (var candidate in candidates)
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Utc;
        }

        /// <summary>
        /// Interprets a wall-clock value the user typed (an HTML datetime-local field, which
        /// carries no offset) as being in the display timezone, and converts it to UTC.
        /// A value that already knows it is UTC is passed through untouched.
        /// </summary>
        public static DateTime FromDisplayToUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), DisplayZone)
        };

        public static DateTime? FromDisplayToUtc(DateTime? value) =>
            value.HasValue ? FromDisplayToUtc(value.Value) : null;

        /// <summary>UTC instant to wall-clock in the display timezone, for rendering only.</summary>
        public static DateTime ToDisplay(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), DisplayZone);

        public static DateTime? ToDisplay(DateTime? utc) =>
            utc.HasValue ? ToDisplay(utc.Value) : null;

        /// <summary>
        /// Guarantees a value is tagged UTC before it reaches Npgsql, which rejects any
        /// DateTime that is not Kind.Utc when writing to timestamptz.
        /// </summary>
        public static DateTime AsUtc(DateTime value) => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        public static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
    }
}
