using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PixelForge.Configuration;
using PixelForge.Exceptions;
using PixelForge.Models;
using PixelForge.Services;
using SkiaSharp;
using Xunit;

namespace PixelForge.Tests;

public class SecurityHardeningTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ImageResizeService _resizeService;
    private readonly ImageCompressionService _compressionService;
    private readonly ImageProcessingOptions _options;

    public SecurityHardeningTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _options = new ImageProcessingOptions();
        var optionsWrapper = Options.Create(_options);
        _resizeService = new ImageResizeService(optionsWrapper, NullLogger<ImageResizeService>.Instance);
        _compressionService = new ImageCompressionService(_resizeService, optionsWrapper, NullLogger<ImageCompressionService>.Instance);
    }

    private static byte[] CreateSamplePngBytes(int width = 10, int height = 10)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task Response_Includes_StrictSecurityHeaders()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 1. Content Security Policy
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        string csp = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);

        // 2. X-Content-Type-Options
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());

        // 3. X-Frame-Options
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());

        // 4. Referrer-Policy
        Assert.True(response.Headers.Contains("Referrer-Policy"));
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").First());

        // 5. Permissions-Policy
        Assert.True(response.Headers.Contains("Permissions-Policy"));
        string permissionsPolicy = string.Join(" ", response.Headers.GetValues("Permissions-Policy"));
        Assert.Contains("camera=()", permissionsPolicy);
        Assert.Contains("microphone=()", permissionsPolicy);
    }

    [Theory]
    [InlineData(2_000_000_000, 100)]
    [InlineData(-10, 100)]
    [InlineData(100, 2_000_000_000)]
    [InlineData(100, -50)]
    [InlineData(10_001, 100)]
    [InlineData(100, 10_001)]
    public void CalculateDimensions_ExtremeOrNegativeDimensions_ThrowsValidationException(int targetWidth, int targetHeight)
    {
        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetWidth = targetWidth,
            TargetHeight = targetHeight,
            PreserveAspectRatio = false
        };

        Assert.Throws<ImageValidationException>(() =>
            _resizeService.CalculateDimensions(1000, 1000, request));
    }

    [Fact]
    public void CalculateDimensions_ProportionalCalculation_OverflowOrExceedingLimits_ThrowsValidationException()
    {
        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetWidth = 6000,
            PreserveAspectRatio = true
        };

        // original is 10 x 100. If targetWidth is 6000, targetHeight = 6000 * 100 / 10 = 60,000, exceeding 10,000 max
        var ex = Assert.Throws<ImageValidationException>(() =>
            _resizeService.CalculateDimensions(10, 100, request));

        Assert.Contains("exceeds the maximum allowed size", ex.Message);
    }

    [Theory]
    [InlineData(10001, 100, false)]
    [InlineData(100, 10001, false)]
    [InlineData(0, 100, false)]
    [InlineData(100, 0, false)]
    [InlineData(-1, 100, false)]
    [InlineData(10000, 10000, true)]
    [InlineData(1, 1, true)]
    public void ResizeRequest_DataAnnotationValidation_EnforcesBounds(int targetWidth, int targetHeight, bool expectedValid)
    {
        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetWidth = targetWidth,
            TargetHeight = targetHeight
        };

        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(request);
        bool isValid = Validator.TryValidateObject(request, validationContext, validationResults, true);

        Assert.Equal(expectedValid, isValid);
    }

    [Theory]
    [InlineData("CON.png")]
    [InlineData("PRN.png")]
    [InlineData("AUX.png")]
    [InlineData("NUL.png")]
    [InlineData("COM1.png")]
    [InlineData("LPT1.png")]
    [InlineData("con.png")]
    [InlineData("aux.png")]
    public async Task ResizeImageAsync_ReservedWindowsDeviceNames_SanitizedToSafePrefix(string fileName)
    {
        byte[] pngBytes = CreateSamplePngBytes(10, 10);
        using var stream = new MemoryStream(pngBytes);

        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = 100,
            TargetFormat = "png"
        };

        var result = await _resizeService.ResizeImageAsync(stream, fileName, request);

        Assert.NotNull(result.SuggestedFileName);
        Assert.StartsWith("image_resized_", result.SuggestedFileName);
    }

    [Theory]
    [InlineData("CON.png")]
    [InlineData("PRN.png")]
    [InlineData("AUX.png")]
    [InlineData("NUL.png")]
    [InlineData("COM1.png")]
    [InlineData("LPT1.png")]
    public async Task CompressImageAsync_ReservedWindowsDeviceNames_SanitizedToSafePrefix(string fileName)
    {
        byte[] pngBytes = CreateSamplePngBytes(10, 10);
        using var stream = new MemoryStream(pngBytes);

        var request = new CompressRequest
        {
            Quality = 80
        };

        var result = await _compressionService.CompressImageAsync(stream, fileName, request);

        Assert.NotNull(result.SuggestedFileName);
        Assert.Equal("image_compressed.png", result.SuggestedFileName);
    }

    [Fact]
    public async Task ResizeImageAsync_PathTraversalCharacters_Sanitized()
    {
        byte[] pngBytes = CreateSamplePngBytes(10, 10);
        using var stream = new MemoryStream(pngBytes);

        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = 100,
            TargetFormat = "png"
        };

        var result = await _resizeService.ResizeImageAsync(stream, "../../path/traversal/test.png", request);

        Assert.NotNull(result.SuggestedFileName);
        Assert.DoesNotContain("/", result.SuggestedFileName);
        Assert.DoesNotContain("\\", result.SuggestedFileName);
        Assert.DoesNotContain("..", result.SuggestedFileName);
        Assert.StartsWith("test_resized_", result.SuggestedFileName);
    }

    [Fact]
    public async Task ResizeImageAsync_ExcessivelyLongFileName_ClampedTo64Chars()
    {
        byte[] pngBytes = CreateSamplePngBytes(10, 10);
        using var stream = new MemoryStream(pngBytes);

        string longName = new string('a', 150) + ".png";
        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = 100,
            TargetFormat = "png"
        };

        var result = await _resizeService.ResizeImageAsync(stream, longName, request);

        Assert.NotNull(result.SuggestedFileName);
        string basePart = result.SuggestedFileName.Split("_resized_")[0];
        Assert.Equal(64, basePart.Length);
    }
}
