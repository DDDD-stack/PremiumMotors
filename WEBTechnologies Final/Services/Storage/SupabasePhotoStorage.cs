using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace WEBTechnologies_Final.Services.Storage
{
    /// <summary>
    /// Uploads to Supabase Storage over its REST API and returns absolute public URLs.
    ///
    /// Uses the service_role key, which bypasses row-level security - so this class must only
    /// ever run server-side. Returned URLs are absolute, which is also what the mobile app
    /// needs: a React Native Image cannot resolve a site-relative "/uploads/..." path.
    /// </summary>
    public class SupabasePhotoStorage : IPhotoStorage
    {
        private readonly HttpClient _http;
        private readonly StorageOptions _options;
        private readonly ILogger<SupabasePhotoStorage> _logger;

        public SupabasePhotoStorage(
            HttpClient http, IOptions<StorageOptions> options, ILogger<SupabasePhotoStorage> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<PhotoSaveResult> SaveAsync(IEnumerable<IFormFile>? files, CancellationToken ct = default)
        {
            var saved = new List<string>();
            var errors = new List<string>();
            if (files is null) return new PhotoSaveResult(saved, errors);

            if (!_options.IsConfigured)
            {
                errors.Add("Photo storage is not configured. Set Storage:SupabaseUrl and Storage:SupabaseServiceKey.");
                _logger.LogError("Supabase storage selected but SupabaseUrl/SupabaseServiceKey are missing.");
                return new PhotoSaveResult(saved, errors);
            }

            var baseUrl = _options.SupabaseUrl.TrimEnd('/');

            foreach (var file in files)
            {
                if (saved.Count >= _options.MaxFilesPerListing)
                {
                    errors.Add($"Only {_options.MaxFilesPerListing} photos are allowed per listing.");
                    break;
                }

                var check = ImageValidator.Check(file, _options.MaxFileBytes);
                if (!check.Ok)
                {
                    if (check.Error is not null) errors.Add(check.Error);
                    continue;
                }

                var objectName = $"cars/{DateTime.UtcNow:yyyy/MM}/{Guid.NewGuid():N}{check.Extension}";
                var uploadUrl = $"{baseUrl}/storage/v1/object/{_options.Bucket}/{objectName}";

                using var request = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SupabaseServiceKey);
                request.Headers.TryAddWithoutValidation("apikey", _options.SupabaseServiceKey);

                await using var source = file.OpenReadStream();
                var content = new StreamContent(source);
                content.Headers.ContentType = new MediaTypeHeaderValue(check.ContentType);
                request.Content = content;

                try
                {
                    using var response = await _http.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct);
                        _logger.LogError("Supabase storage upload failed ({Status}): {Body}",
                            (int)response.StatusCode, body);
                        errors.Add($"\"{file.FileName}\" could not be uploaded.");
                        continue;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Supabase storage upload threw for {File}.", file.FileName);
                    errors.Add($"\"{file.FileName}\" could not be uploaded.");
                    continue;
                }

                saved.Add($"{baseUrl}/storage/v1/object/public/{_options.Bucket}/{objectName}");
            }

            return new PhotoSaveResult(saved, errors);
        }

        public async Task DeleteAsync(string path, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (string.IsNullOrWhiteSpace(_options.SupabaseUrl) ||
                string.IsNullOrWhiteSpace(_options.SupabaseServiceKey)) return;

            var baseUrl = _options.SupabaseUrl.TrimEnd('/');
            var marker = "/storage/v1/object/public/" + _options.Bucket + "/";
            var index = path.IndexOf(marker, StringComparison.Ordinal);

            // A path that is not one of our public object URLs is not ours to delete — most
            // likely a seed image or a URL left over from a previous storage provider.
            if (index < 0) return;

            var objectName = path[(index + marker.Length)..];
            if (string.IsNullOrWhiteSpace(objectName)) return;

            var deleteUrl = baseUrl + "/storage/v1/object/" + _options.Bucket + "/" + objectName;

            using var request = new HttpRequestMessage(HttpMethod.Delete, deleteUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SupabaseServiceKey);
            request.Headers.TryAddWithoutValidation("apikey", _options.SupabaseServiceKey);

            try
            {
                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    // Best effort: the listing has already dropped the photo, so an orphaned
                    // blob is a cleanup problem rather than a user-facing failure.
                    _logger.LogWarning("Supabase storage delete failed ({Status}) for {Object}.",
                        (int)response.StatusCode, objectName);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Supabase storage delete threw for {Object}.", objectName);
            }
        }
    }
}
