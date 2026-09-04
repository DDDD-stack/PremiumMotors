namespace WEBTechnologies_Final.Services.Storage
{
    public class StorageOptions
    {
        /// <summary>"Local" (disk, development only) or "Supabase".</summary>
        public string Provider { get; set; } = "Local";

        /// <summary>Supabase project URL, e.g. https://xxxx.supabase.co</summary>
        public string SupabaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Supabase service_role key. SECRET - user-secrets or environment only. It bypasses
        /// row-level security, so it must never reach a browser or a mobile app bundle.
        /// </summary>
        public string SupabaseServiceKey { get; set; } = string.Empty;

        /// <summary>Storage bucket name. Must exist and be public-read.</summary>
        public string Bucket { get; set; } = "car-photos";

        public int MaxFileBytes { get; set; } = 8 * 1024 * 1024;
        public int MaxFilesPerListing { get; set; } = 12;

        public bool IsSupabase =>
            string.Equals(Provider, "Supabase", StringComparison.OrdinalIgnoreCase);

        public bool IsConfigured =>
            !IsSupabase ||
            (!string.IsNullOrWhiteSpace(SupabaseUrl) && !string.IsNullOrWhiteSpace(SupabaseServiceKey));
    }
}
