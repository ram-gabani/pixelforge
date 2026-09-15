using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PixelForge.Configuration;
using PixelForge.Exceptions;
using PixelForge.Models;
using PixelForge.Services;
using Xunit;

namespace PixelForge.Tests;

public class AspectRatioTests
{
    private readonly ImageResizeService _service;

    public AspectRatioTests()
    {
        var options = Options.Create(new ImageProcessingOptions());
        _service = new ImageResizeService(options, NullLogger<ImageResizeService>.Instance);
    }

    [Theory]
    [InlineData(1920, 1080, 200, 3840, 2160)]
    [InlineData(1920, 1080, 50, 960, 540)]
    [InlineData(1920, 1080, 100, 1920, 1080)]
    [InlineData(1920, 1080, 1, 19, 11)]
    [InlineData(1920, 1080, 500, 9600, 5400)]
    public void CalculateDimensions_PercentageMode_CalculatesExpectedDimensions(
        int originalWidth, int originalHeight, double percentage, int expectedWidth, int expectedHeight)
    {
        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = percentage
        };

        var (targetWidth, targetHeight) = _service.CalculateDimensions(originalWidth, originalHeight, request);

        Assert.Equal(expectedWidth, targetWidth);
        Assert.Equal(expectedHeight, targetHeight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-50)]
    [InlineData(500.1)]
    [InlineData(1000)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CalculateDimensions_PercentageMode_InvalidPercentage_ThrowsValidationException(double invalidPercentage)
    {
        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = invalidPercentage
        };

        Assert.Throws<ImageValidationException>(() => _service.CalculateDimensions(1920, 1080, request));
    }

    [Fact]
    public void CalculateDimensions_PercentageMode_NullPercentage_ThrowsValidationException()
    {
        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = null
        };

        Assert.Throws<ImageValidationException>(() => _service.CalculateDimensions(1920, 1080, request));
    }

    [Fact]
    public void CalculateDimensions_CustomDimensions_WidthControlling_PreservesAspectRatio()
    {
        // 1920x1080 with TargetWidth 2000 -> 2000 x 1125
        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetWidth = 2000,
            PreserveAspectRatio = true
        };

        var (targetWidth, targetHeight) = _service.CalculateDimensions(1920, 1080, request);

        Assert.Equal(2000, targetWidth);
        Assert.Equal(1125, targetHeight);
    }

    [Fact]
    public void CalculateDimensions_CustomDimensions_HeightControlling_PreservesAspectRatio()
    {
        // 1920x1080 with TargetHeight 1000 -> 1778 x 1000
        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetHeight = 1000,
            PreserveAspectRatio = true
        };

        var (targetWidth, targetHeight) = _service.CalculateDimensions(1920, 1080, request);

        Assert.Equal(1778, targetWidth);
        Assert.Equal(1000, targetHeight);
    }

    [Fact]
    public void CalculateDimensions_CustomDimensions_Portrait_PreservesAspectRatio()
    {
        // Portrait 1080x1920 with TargetWidth 540 -> 540 x 960
        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetWidth = 540,
            PreserveAspectRatio = true
        };

        var (targetWidth, targetHeight) = _service.CalculateDimensions(1080, 1920, request);

        Assert.Equal(540, targetWidth);
        Assert.Equal(960, targetHeight);
    }

    [Fact]
    public void CalculateDimensions_CustomDimensions_Square_PreservesAspectRatio()
    {
        // Square 1000x1000 with TargetHeight 500 -> 500 x 500
        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetHeight = 500,
            PreserveAspectRatio = true
        };

        var (targetWidth, targetHeight) = _service.CalculateDimensions(1000, 1000, request);

        Assert.Equal(500, targetWidth);
        Assert.Equal(500, targetHeight);
    }

    [Fact]
    public void CalculateDimensions_CustomDimensions_AspectRatioUnlocked_ReturnsExactDimensions()
    {
        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetWidth = 800,
            TargetHeight = 600,
            PreserveAspectRatio = false
        };

        var (targetWidth, targetHeight) = _service.CalculateDimensions(1920, 1080, request);

        Assert.Equal(800, targetWidth);
        Assert.Equal(600, targetHeight);
    }

    [Fact]
    public void CalculateDimensions_CustomDimensions_BothNull_ThrowsValidationException()
    {
        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetWidth = null,
            TargetHeight = null
        };

        Assert.Throws<ImageValidationException>(() => _service.CalculateDimensions(1920, 1080, request));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-10, null)]
    [InlineData(null, 0)]
    [InlineData(null, -5)]
    public void CalculateDimensions_CustomDimensions_NonPositiveValues_ThrowsValidationException(int? width, int? height)
    {
        var request = new ResizeRequest
        {
            Mode = ResizeMode.CustomDimensions,
            TargetWidth = width,
            TargetHeight = height
        };

        Assert.Throws<ImageValidationException>(() => _service.CalculateDimensions(1920, 1080, request));
    }
}

