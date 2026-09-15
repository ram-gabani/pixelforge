using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PixelForge.Configuration;
using PixelForge.Exceptions;
using PixelForge.Services;
using Xunit;

namespace PixelForge.Tests;

public class SecurityAndValidationTests
{
    private readonly ImageResizeService _service;
    private readonly ImageProcessingOptions _options;

    public SecurityAndValidationTests()
    {
        _options = new ImageProcessingOptions();
        var optionsWrapper = Options.Create(_options);
        _service = new ImageResizeService(optionsWrapper, NullLogger<ImageResizeService>.Instance);
    }

    [Fact]
    public async Task InspectImage_NullStream_ThrowsImageValidationException()
    {
        await Assert.ThrowsAsync<ImageValidationException>(() =>
            _service.InspectImageAsync(null!, "test.jpg"));
    }

    [Fact]
    public async Task InspectImage_EmptyStream_ThrowsImageValidationException()
    {
        using var stream = new MemoryStream();

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            _service.InspectImageAsync(stream, "test.jpg"));

        Assert.Equal("The uploaded file is empty.", ex.Message);
    }

    [Theory]
    [InlineData("malicious.exe")]
    [InlineData("script.sh")]
    [InlineData("document.pdf")]
    [InlineData("image.svg")]
    [InlineData("archive.zip")]
    [InlineData("notanextension")]
    public async Task InspectImage_DisallowedExtension_ThrowsImageValidationException(string fileName)
    {
        using var stream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            _service.InspectImageAsync(stream, fileName));

        Assert.Equal("The uploaded file extension is not supported.", ex.Message);
    }

    [Fact]
    public async Task InspectImage_FakeExtension_BinarySignatureMismatch_ThrowsImageValidationException()
    {
        // Text file content with .jpg extension
        byte[] textBytes = Encoding.UTF8.GetBytes("This is plain text and not an image file.");
        using var stream = new MemoryStream(textBytes);

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            _service.InspectImageAsync(stream, "fake_photo.jpg"));

        Assert.Equal("The file contents do not match a supported image format.", ex.Message);
    }

    [Fact]
    public async Task InspectImage_StreamExceedsMaxSize_ThrowsImageValidationException()
    {
        // Custom options with small 100-byte limit to test boundary
        var smallOptions = Options.Create(new ImageProcessingOptions { MaxFileSizeBytes = 100 });
        var service = new ImageResizeService(smallOptions, NullLogger<ImageResizeService>.Instance);

        byte[] largePayload = new byte[200];
        using var stream = new MemoryStream(largePayload);

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            service.InspectImageAsync(stream, "test.jpg"));

        Assert.Equal("The uploaded file exceeds the maximum allowed size.", ex.Message);
    }

    [Fact]
    public void IsSupportedFormat_ValidSignatures_ReturnsCorrectFormat()
    {
        // JPEG header
        using (var jpegStream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }))
        {
            Assert.True(_service.IsSupportedFormat(jpegStream, out var format, out var mime));
            Assert.Equal("JPEG", format);
            Assert.Equal("image/jpeg", mime);
        }

        // PNG header
        using (var pngStream = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            Assert.True(_service.IsSupportedFormat(pngStream, out var format, out var mime));
            Assert.Equal("PNG", format);
            Assert.Equal("image/png", mime);
        }

        // WebP header: RIFF....WEBP
        byte[] webpHeader = new byte[12]
        {
            0x52, 0x49, 0x46, 0x46, // RIFF
            0x00, 0x00, 0x00, 0x00,
            0x57, 0x45, 0x42, 0x50  // WEBP
        };
        using (var webpStream = new MemoryStream(webpHeader))
        {
            Assert.True(_service.IsSupportedFormat(webpStream, out var format, out var mime));
            Assert.Equal("WEBP", format);
            Assert.Equal("image/webp", mime);
        }

        // GIF header
        using (var gifStream = new MemoryStream(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }))
        {
            Assert.True(_service.IsSupportedFormat(gifStream, out var format, out var mime));
            Assert.Equal("GIF", format);
            Assert.Equal("image/gif", mime);
        }

        // BMP header
        using (var bmpStream = new MemoryStream(new byte[] { 0x42, 0x4D, 0x00, 0x00 }))
        {
            Assert.True(_service.IsSupportedFormat(bmpStream, out var format, out var mime));
            Assert.Equal("BMP", format);
            Assert.Equal("image/bmp", mime);
        }
    }
}

