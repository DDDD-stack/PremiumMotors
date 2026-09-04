using Microsoft.AspNetCore.Http;
using WEBTechnologies_Final.Services.Storage;
using Xunit;

namespace PremiumMotors.Tests;

public class ImageValidatorTests
{
    private const long TenMb = 10 * 1024 * 1024;

    private static IFormFile File(byte[] bytes, string name = "photo.jpg")
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "photos", name);
    }

    private static byte[] WithHeader(params byte[] header)
    {
        var buffer = new byte[64];
        header.CopyTo(buffer, 0);
        return buffer;
    }

    [Fact]
    public void Jpeg_is_accepted()
    {
        var check = ImageValidator.Check(File(WithHeader(0xFF, 0xD8, 0xFF, 0xE0)), TenMb);
        Assert.True(check.Ok);
        Assert.Equal(".jpg", check.Extension);
    }

    [Fact]
    public void Png_is_accepted()
    {
        var check = ImageValidator.Check(
            File(WithHeader(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)), TenMb);
        Assert.True(check.Ok);
        Assert.Equal(".png", check.Extension);
    }

    [Fact]
    public void Webp_is_accepted()
    {
        var bytes = WithHeader(0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50);
        var check = ImageValidator.Check(File(bytes), TenMb);
        Assert.True(check.Ok);
        Assert.Equal(".webp", check.Extension);
    }

    [Fact]
    public void A_renamed_non_image_is_rejected_despite_the_extension()
    {
        // The old implementation trusted the filename alone, so this would have been stored.
        var executable = WithHeader(0x4D, 0x5A, 0x90, 0x00);
        var check = ImageValidator.Check(File(executable, "totally-a-photo.jpg"), TenMb);

        Assert.False(check.Ok);
    }

    [Fact]
    public void Oversized_files_are_rejected()
    {
        var check = ImageValidator.Check(File(WithHeader(0xFF, 0xD8, 0xFF)), maxBytes: 8);
        Assert.False(check.Ok);
        Assert.Contains("larger than", check.Error);
    }

    [Fact]
    public void Empty_files_are_rejected()
    {
        Assert.False(ImageValidator.Check(File(Array.Empty<byte>()), TenMb).Ok);
    }

    [Fact]
    public void Content_type_comes_from_the_bytes_not_the_filename()
    {
        // A PNG uploaded as ".jpg" is stored as a PNG.
        var png = WithHeader(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        var check = ImageValidator.Check(File(png, "photo.jpg"), TenMb);

        Assert.Equal("image/png", check.ContentType);
        Assert.Equal(".png", check.Extension);
    }
}
