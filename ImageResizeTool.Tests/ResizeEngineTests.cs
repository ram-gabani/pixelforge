using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PixelForge.Configuration;
using PixelForge.Exceptions;
using PixelForge.Models;
using PixelForge.Services;
using SkiaSharp;
using Xunit;

namespace PixelForge.Tests;

public class ResizeEngineTests
{
    private readonly ImageResizeService _service;
    private readonly ImageProcessingOptions _options;

    public ResizeEngineTests()
    {
        _options = new ImageProcessingOptions();
        var optionsWrapper = Options.Create(_options);
        _service = new ImageResizeService(optionsWrapper, NullLogger<ImageResizeService>.Instance);
    }

    private static MemoryStream CreateTestImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        var data = image.Encode(format, 90);
        return new MemoryStream(data.ToArray());
    }

    [Fact]
    public async Task InspectImage_ValidJpeg_ReturnsAccurateMetadata()
    {
        using var stream = CreateTestImage(800, 600, SKEncodedImageFormat.Jpeg);

        var metadata = await _service.InspectImageAsync(stream, "sample.jpg");

        Assert.NotNull(metadata);
        Assert.Equal(800, metadata.Width);
        Assert.Equal(600, metadata.Height);
        Assert.Equal("4:3", metadata.AspectRatioFormatted);
        Assert.Equal("JPEG", metadata.Format);
        Assert.Equal("image/jpeg", metadata.MimeType);
        Assert.True(metadata.FileSizeBytes > 0);
    }

    [Fact]
    public async Task InspectImage_ValidPng_ReturnsAccurateMetadata()
    {
        using var stream = CreateTestImage(1920, 1080, SKEncodedImageFormat.Png);

        var metadata = await _service.InspectImageAsync(stream, "landscape.png");

        Assert.NotNull(metadata);
        Assert.Equal(1920, metadata.Width);
        Assert.Equal(1080, metadata.Height);
        Assert.Equal("16:9", metadata.AspectRatioFormatted);
        Assert.Equal("PNG", metadata.Format);
        Assert.Equal("image/png", metadata.MimeType);
        Assert.True(metadata.FileSizeBytes > 0);
    }

    [Fact]
    public async Task ResizeImage_PercentageMode_ResizesCorrectly()
    {
        using var stream = CreateTestImage(400, 200, SKEncodedImageFormat.Jpeg);

        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = 50,
            TargetFormat = "jpeg",
            Quality = 85
        };

        var result = await _service.ResizeImageAsync(stream, "test.jpg", request);

        Assert.True(result.Success);
        Assert.NotNull(result.ProcessedBytes);
        Assert.NotEmpty(result.ProcessedBytes);
        Assert.NotNull(result.OutputMetadata);
        Assert.Equal(200, result.OutputMetadata.Width);
        Assert.Equal(100, result.OutputMetadata.Height);
        Assert.Equal("image/jpeg", result.MimeType);

        // Verify decoded output matches dimensions
        using var codec = SKCodec.Create(new SKMemoryStream(result.ProcessedBytes));
        Assert.NotNull(codec);
        Assert.Equal(200, codec.Info.Width);
        Assert.Equal(100, codec.Info.Height);
    }

    [Fact]
    public async Task ResizeImage_CustomDimensions_PreserveAspectRatio_ResizesCorrectly()
    {
        using var stream = CreateTestImage(1920, 1080, SKEncodedImageFormat.Png);

        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetWidth = 960,
            PreserveAspectRatio = true,
            TargetFormat = "png"
        };

        var result = await _service.ResizeImageAsync(stream, "photo.png", request);

        Assert.True(result.Success);
        Assert.NotNull(result.OutputMetadata);
        Assert.Equal(960, result.OutputMetadata.Width);
        Assert.Equal(540, result.OutputMetadata.Height);
        Assert.Equal("image/png", result.MimeType);
        Assert.Equal("16:9", result.OutputMetadata.AspectRatioFormatted);
    }

    [Fact]
    public async Task ResizeImage_FormatConversion_PngToWebP_Succeeds()
    {
        using var stream = CreateTestImage(300, 300, SKEncodedImageFormat.Png);

        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = 100,
            TargetFormat = "webp",
            Quality = 90
        };

        var result = await _service.ResizeImageAsync(stream, "graphic.png", request);

        Assert.True(result.Success);
        Assert.Equal("image/webp", result.MimeType);
        Assert.Contains(".webp", result.SuggestedFileName);

        using var codec = SKCodec.Create(new SKMemoryStream(result.ProcessedBytes!));
        Assert.NotNull(codec);
        Assert.Equal(300, codec.Info.Width);
        Assert.Equal(300, codec.Info.Height);
    }

    [Fact]
    public async Task ResizeImage_SanitizesFileName_RemovesPathTraversal()
    {
        using var stream = CreateTestImage(100, 100, SKEncodedImageFormat.Jpeg);

        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = 100,
            TargetFormat = "jpeg"
        };

        var result = await _service.ResizeImageAsync(stream, "../../etc/passwd.jpg", request);

        Assert.True(result.Success);
        Assert.DoesNotContain("..", result.SuggestedFileName);
        Assert.DoesNotContain("/", result.SuggestedFileName);
        Assert.DoesNotContain("\\", result.SuggestedFileName);
        Assert.StartsWith("passwd_resized_100x100.jpg", result.SuggestedFileName);
    }

    [Fact]
    public async Task ResizeImage_UnsupportedTargetFormat_ThrowsValidationException()
    {
        using var stream = CreateTestImage(100, 100, SKEncodedImageFormat.Jpeg);

        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = 100,
            TargetFormat = "gif"
        };

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            _service.ResizeImageAsync(stream, "test.jpg", request));

        Assert.Equal("GIF output is not supported in the current configuration.", ex.Message);
    }
}

