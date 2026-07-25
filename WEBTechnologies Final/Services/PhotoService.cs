namespace WEBTechnologies_Final.Services
{
    // Shared car-photo upload handling, used by both the admin and user "sell" flows.
    public class PhotoService
    {
        private static readonly string[] AllowedImageExtensions =
            { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

        private readonly IWebHostEnvironment _env;

        public PhotoService(IWebHostEnvironment env) => _env = env;

        public async Task<List<string>> SaveAsync(List<IFormFile>? photos)
        {
            var saved = new List<string>();
            if (photos is null || photos.Count == 0) return saved;

            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var uploadDir = Path.Combine(webRoot, "uploads", "cars");
            Directory.CreateDirectory(uploadDir);

            foreach (var photo in photos)
            {
                if (photo.Length == 0) continue;
                var ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
                if (!AllowedImageExtensions.Contains(ext)) continue;
                var fileName = $"{Guid.NewGuid():N}{ext}";
                var fullPath = Path.Combine(uploadDir, fileName);
                await using var stream = new FileStream(fullPath, FileMode.Create);
                await photo.CopyToAsync(stream);
                saved.Add($"/uploads/cars/{fileName}");
            }

            return saved;
        }
    }
}
