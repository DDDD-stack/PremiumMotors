using Microsoft.Extensions.Options;

namespace WEBTechnologies_Final.Services.Storage
{
    /// <summary>
    /// Writes to wwwroot/uploads/cars. Development only - see IPhotoStorage.
    /// </summary>
    public class LocalDiskPhotoStorage : IPhotoStorage
    {
        private readonly IWebHostEnvironment _env;
        private readonly StorageOptions _options;

        public LocalDiskPhotoStorage(IWebHostEnvironment env, IOptions<StorageOptions> options)
        {
            _env = env;
            _options = options.Value;
        }

        public async Task<PhotoSaveResult> SaveAsync(IEnumerable<IFormFile>? files, CancellationToken ct = default)
        {
            var saved = new List<string>();
            var errors = new List<string>();
            if (files is null) return new PhotoSaveResult(saved, errors);

            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadDir = Path.Combine(webRoot, "uploads", "cars");
            Directory.CreateDirectory(uploadDir);

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

                // Name from the verified content type, never from the uploaded filename.
                var fileName = $"{Guid.NewGuid():N}{check.Extension}";
                var fullPath = Path.Combine(uploadDir, fileName);

                await using (var stream = new FileStream(fullPath, FileMode.CreateNew))
                {
                    await file.CopyToAsync(stream, ct);
                }

                saved.Add($"/uploads/cars/{fileName}");
            }

            return new PhotoSaveResult(saved, errors);
        }

        public Task DeleteAsync(string path, CancellationToken ct = default)
        {
            // Only ever deletes inside the uploads folder. The path arrives from a form field,
            // so a crafted "/uploads/cars/../../appsettings.json" must not resolve outside it.
            if (string.IsNullOrWhiteSpace(path)) return Task.CompletedTask;

            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadDir = Path.GetFullPath(Path.Combine(webRoot, "uploads", "cars"));

            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName)) return Task.CompletedTask;

            var fullPath = Path.GetFullPath(Path.Combine(uploadDir, fileName));
            if (!fullPath.StartsWith(uploadDir, StringComparison.Ordinal)) return Task.CompletedTask;

            try
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return Task.CompletedTask;
        }
    }
}
