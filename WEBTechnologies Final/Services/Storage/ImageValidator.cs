namespace WEBTechnologies_Final.Services.Storage
{
    public record ImageCheck(bool Ok, string? Error, string Extension, string ContentType);

    /// <summary>
    /// Validates uploads by inspecting the actual bytes, not just the filename.
    ///
    /// The previous implementation trusted the file extension alone, so any file renamed to
    /// .jpg was accepted and written into the web root.
    /// </summary>
    public static class ImageValidator
    {
        public static ImageCheck Check(IFormFile file, long maxBytes)
        {
            if (file.Length == 0)
                return new ImageCheck(false, "The file is empty.", "", "");

            if (file.Length > maxBytes)
                return new ImageCheck(false,
                    $"\"{file.FileName}\" is larger than the {maxBytes / (1024 * 1024)} MB limit.", "", "");

            Span<byte> header = stackalloc byte[12];
            using (var stream = file.OpenReadStream())
            {
                var read = stream.Read(header);
                if (read < 12) return new ImageCheck(false, "The file is too small to be an image.", "", "");
            }

            // JPEG: FF D8 FF
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return new ImageCheck(true, null, ".jpg", "image/jpeg");

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
                return new ImageCheck(true, null, ".png", "image/png");

            // GIF: "GIF8"
            if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38)
                return new ImageCheck(true, null, ".gif", "image/gif");

            // WEBP: "RIFF" .... "WEBP"
            if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
                return new ImageCheck(true, null, ".webp", "image/webp");

            return new ImageCheck(false,
                $"\"{file.FileName}\" is not a JPEG, PNG, GIF or WebP image.", "", "");
        }
    }
}
