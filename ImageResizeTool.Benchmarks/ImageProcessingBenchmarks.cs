using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PixelForge.Configuration;
using PixelForge.Models;
using PixelForge.Services;
using SkiaSharp;

namespace PixelForge.Benchmarks;

[MemoryDiagnoser]
public class ImageProcessingBenchmarks
{
    private ImageResizeService _resizeService = null!;
    private ImageCompressionService _compressionService = null!;

    // Deterministic test byte streams
    private byte[] _smallJpegBytes = null!;
    private byte[] _mediumJpegBytes = null!;
    private byte[] _largeJpegBytes = null!;

    private byte[] _smallPngBytes = null!;
    private byte[] _mediumPngBytes = null!;
    private byte[] _largePngBytes = null!;

    private byte[] _smallWebpBytes = null!;
    private byte[] _mediumWebpBytes = null!;
    private byte[] _largeWebpBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = Options.Create(new ImageProcessingOptions());
        var resizeLogger = NullLogger<ImageResizeService>.Instance;
        var compressLogger = NullLogger<ImageCompressionService>.Instance;

        _resizeService = new ImageResizeService(options, resizeLogger);
        _compressionService = new ImageCompressionService(_resizeService, options, compressLogger);

        // Generate deterministic synthetic test images (Small ~800x600, Medium ~1920x1080, Large ~4000x3000)
        _smallJpegBytes = GenerateSyntheticImage(800, 600, SKEncodedImageFormat.Jpeg, 90);
        _mediumJpegBytes = GenerateSyntheticImage(1920, 1080, SKEncodedImageFormat.Jpeg, 90);
        _largeJpegBytes = GenerateSyntheticImage(4000, 3000, SKEncodedImageFormat.Jpeg, 90);

        _smallPngBytes = GenerateSyntheticImage(800, 600, SKEncodedImageFormat.Png, 100);
        _mediumPngBytes = GenerateSyntheticImage(1920, 1080, SKEncodedImageFormat.Png, 100);
        _largePngBytes = GenerateSyntheticImage(4000, 3000, SKEncodedImageFormat.Png, 100);

        _smallWebpBytes = GenerateSyntheticImage(800, 600, SKEncodedImageFormat.Webp, 90);
        _mediumWebpBytes = GenerateSyntheticImage(1920, 1080, SKEncodedImageFormat.Webp, 90);
        _largeWebpBytes = GenerateSyntheticImage(4000, 3000, SKEncodedImageFormat.Webp, 90);
    }

    [Benchmark(Description = "Resize JPEG Medium (1080p -> 50% Scale)")]
    public async Task<ResizeResult> ResizeJpeg_Medium_50Percent()
    {
        using var stream = new MemoryStream(_mediumJpegBytes, false);
        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = 50,
            Quality = 85,
            TargetFormat = "jpeg"
        };
        return await _resizeService.ResizeImageAsync(stream, "test_1080p.jpg", request);
    }

    [Benchmark(Description = "Resize PNG Medium (1080p -> 50% Scale)")]
    public async Task<ResizeResult> ResizePng_Medium_50Percent()
    {
        using var stream = new MemoryStream(_mediumPngBytes, false);
        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = 50,
            Quality = 100,
            TargetFormat = "png"
        };
        return await _resizeService.ResizeImageAsync(stream, "test_1080p.png", request);
    }

    [Benchmark(Description = "Resize WebP Medium (1080p -> 50% Scale)")]
    public async Task<ResizeResult> ResizeWebp_Medium_50Percent()
    {
        using var stream = new MemoryStream(_mediumWebpBytes, false);
        var request = new ResizeRequest
        {
            Mode = ResizeMode.Percentage,
            Percentage = 50,
            Quality = 85,
            TargetFormat = "webp"
        };
        return await _resizeService.ResizeImageAsync(stream, "test_1080p.webp", request);
    }

    [Benchmark(Description = "Compress JPEG Medium (1080p -> Quality 75)")]
    public async Task<CompressResult> CompressJpeg_Medium()
    {
        using var stream = new MemoryStream(_mediumJpegBytes, false);
        var request = new CompressRequest
        {
            Quality = 75
        };
        return await _compressionService.CompressImageAsync(stream, "test_1080p.jpg", request);
    }

    [Benchmark(Description = "Compress PNG Medium (1080p -> ZLib Effort 9)")]
    public async Task<CompressResult> CompressPng_Medium()
    {
        using var stream = new MemoryStream(_mediumPngBytes, false);
        var request = new CompressRequest
        {
            PngCompressionLevel = 9
        };
        return await _compressionService.CompressImageAsync(stream, "test_1080p.png", request);
    }

    [Benchmark(Description = "Compress WebP Medium (1080p -> Quality 75)")]
    public async Task<CompressResult> CompressWebp_Medium()
    {
        using var stream = new MemoryStream(_mediumWebpBytes, false);
        var request = new CompressRequest
        {
            Quality = 75
        };
        return await _compressionService.CompressImageAsync(stream, "test_1080p.webp", request);
    }

    private static byte[] GenerateSyntheticImage(int width, int height, SKEncodedImageFormat format, int quality)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        // Draw deterministic gradient + shapes
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                [SKColors.Blue, SKColors.Green, SKColors.Red, SKColors.Yellow],
                [0.0f, 0.33f, 0.66f, 1.0f],
                SKShaderTileMode.Clamp)
        };
        canvas.DrawRect(new SKRect(0, 0, width, height), paint);

        using var circlePaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 4f, circlePaint);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }
}

