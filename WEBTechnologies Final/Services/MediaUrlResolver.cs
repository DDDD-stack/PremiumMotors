namespace WEBTechnologies_Final.Services
{
    /// <summary>
    /// Turns a stored image path into an absolute URL.
    ///
    /// The website can happily render "/uploads/cars/x.jpg", but a React Native Image cannot
    /// resolve a site-relative path - it needs a full URL. Every image the API returns therefore
    /// goes through here. Supabase Storage already returns absolute URLs, which are passed
    /// through untouched, so this keeps working after the storage move.
    /// </summary>
    public interface IMediaUrlResolver
    {
        string Resolve(string? path);
        IReadOnlyList<string> ResolveAll(IEnumerable<string>? paths);
    }

    public class MediaUrlResolver : IMediaUrlResolver
    {
        private readonly string? _configuredBase;
        private readonly IHttpContextAccessor _accessor;

        public MediaUrlResolver(IConfiguration config, IHttpContextAccessor accessor)
        {
            _configuredBase = config["App:PublicBaseUrl"]?.TrimEnd('/');
            _accessor = accessor;
        }

        public string Resolve(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            // Already absolute (Supabase Storage, or any CDN).
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return path;

            var root = _configuredBase;
            if (string.IsNullOrWhiteSpace(root))
            {
                // Fall back to the current request. Fine for local development; production
                // should set App:PublicBaseUrl so URLs stay stable behind a proxy.
                var req = _accessor.HttpContext?.Request;
                if (req is not null) root = $"{req.Scheme}://{req.Host}";
            }

            if (string.IsNullOrWhiteSpace(root)) return path;

            return $"{root}/{path.TrimStart('/')}";
        }

        public IReadOnlyList<string> ResolveAll(IEnumerable<string>? paths) =>
            paths?.Select(Resolve).ToList() ?? new List<string>();
    }
}
