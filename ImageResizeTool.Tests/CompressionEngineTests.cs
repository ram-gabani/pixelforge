using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PixelForge.Configuration;
using PixelForge.Exceptions;
using PixelForge.Models;
using PixelForge.Services;
using SkiaSharp;
using Xunit;

namespace PixelForge.Tests;

public class CompressionEngineTests
{
    private readonly ImageResizeService _resizeService;
    private readonly ImageCompressionService _compressionService;
    private readonly ImageProcessingOptions _options;

    public CompressionEngineTests()
    {
        _options = new ImageProcessingOptions();
        var optionsWrapper = Options.Create(_options);
        _resizeService = new ImageResizeService(optionsWrapper, NullLogger<ImageResizeService>.Instance);
        _compressionService = new ImageCompressionService(_resizeService, optionsWrapper, NullLogger<ImageCompressionService>.Instance);
    }

    private static MemoryStream CreateDetailedImage(int width, int height, SKEncodedImageFormat format, bool transparent = false)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, transparent ? SKAlphaType.Premul : SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);

        using var paint = new SKPaint { IsAntialias = true, StrokeWidth = 3 };
        for (int i = 0; i < Math.Min(width, height); i += 15)
        {
            paint.Color = new SKColor((byte)(i % 255), (byte)((i * 3) % 255), (byte)((i * 7) % 255), transparent ? (byte)(100 + (i % 155)) : (byte)255);
            canvas.DrawCircle(width / 2f, height / 2f, i / 2f, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return new MemoryStream(data.ToArray());
    }

    [Fact]
    public async Task CompressImage_Jpeg_SucceedsWithIdenticalDimensions()
    {
        using var stream = CreateDetailedImage(800, 600, SKEncodedImageFormat.Jpeg);
        var request = new CompressRequest { Quality = 75 };

        var result = await _compressionService.CompressImageAsync(stream, "sample.jpg", request);

        Assert.True(result.Success);
        Assert.NotNull(result.ProcessedBytes);
        Assert.NotEmpty(result.ProcessedBytes);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(800, result.OutputMetadata?.Width);
        Assert.Equal(600, result.OutputMetadata?.Height);
        Assert.Equal(result.OriginalMetadata?.Width, result.OutputMetadata?.Width);
        Assert.Equal(result.OriginalMetadata?.Height, result.OutputMetadata?.Height);

        // Verify decoded bytes match exact dimensions
        using var codec = SKCodec.Create(new SKMemoryStream(result.ProcessedBytes));
        Assert.NotNull(codec);
        Assert.Equal(800, codec.Info.Width);
        Assert.Equal(600, codec.Info.Height);
    }

    [Fact]
    public async Task CompressImage_Jpeg_Quality50ProducesSmallerSizeThanQuality95()
    {
        using var streamHigh = CreateDetailedImage(1000, 800, SKEncodedImageFormat.Jpeg);
        using var streamLow = new MemoryStream(streamHigh.ToArray());

        var resultHigh = await _compressionService.CompressImageAsync(streamHigh, "photo.jpg", new CompressRequest { Quality = 95 });
        var resultLow = await _compressionService.CompressImageAsync(streamLow, "photo.jpg", new CompressRequest { Quality = 50 });

        Assert.True(resultHigh.Success);
        Assert.True(resultLow.Success);
        Assert.True(resultLow.CompressedFileSizeBytes < resultHigh.CompressedFileSizeBytes,
            $"Expected Q50 ({resultLow.CompressedFileSizeBytes} bytes) to be smaller than Q95 ({resultHigh.CompressedFileSizeBytes} bytes)");
    }

    [Fact]
    public async Task CompressImage_WebP_SucceedsWithIdenticalDimensions()
    {
        using var stream = CreateDetailedImage(640, 480, SKEncodedImageFormat.Webp);
        var request = new CompressRequest { Quality = 70 };

        var result = await _compressionService.CompressImageAsync(stream, "graphic.webp", request);

        Assert.True(result.Success);
        Assert.NotNull(result.ProcessedBytes);
        Assert.Equal("image/webp", result.MimeType);
        Assert.Equal(640, result.OutputMetadata?.Width);
        Assert.Equal(480, result.OutputMetadata?.Height);

        using var codec = SKCodec.Create(new SKMemoryStream(result.ProcessedBytes));
        Assert.NotNull(codec);
        Assert.Equal(640, codec.Info.Width);
        Assert.Equal(480, codec.Info.Height);
    }

    [Fact]
    public async Task CompressImage_WebP_Quality50ProducesSmallerSizeThanQuality95()
    {
        using var streamHigh = CreateDetailedImage(800, 600, SKEncodedImageFormat.Webp);
        using var streamLow = new MemoryStream(streamHigh.ToArray());

        var resultHigh = await _compressionService.CompressImageAsync(streamHigh, "sample.webp", new CompressRequest { Quality = 95 });
        var resultLow = await _compressionService.CompressImageAsync(streamLow, "sample.webp", new CompressRequest { Quality = 50 });

        Assert.True(resultHigh.Success);
        Assert.True(resultLow.Success);
        Assert.True(resultLow.CompressedFileSizeBytes < resultHigh.CompressedFileSizeBytes);
    }

    [Fact]
    public async Task CompressImage_Png_SucceedsWithIdenticalDimensions()
    {
        using var stream = CreateDetailedImage(500, 500, SKEncodedImageFormat.Png);
        var request = new CompressRequest { PngCompressionLevel = 9 };

        var result = await _compressionService.CompressImageAsync(stream, "diagram.png", request);

        Assert.True(result.Success);
        Assert.NotNull(result.ProcessedBytes);
        Assert.Equal("image/png", result.MimeType);
        Assert.Equal(500, result.OutputMetadata?.Width);
        Assert.Equal(500, result.OutputMetadata?.Height);

        using var codec = SKCodec.Create(new SKMemoryStream(result.ProcessedBytes));
        Assert.NotNull(codec);
        Assert.Equal(500, codec.Info.Width);
        Assert.Equal(500, codec.Info.Height);
    }

    [Fact]
    public async Task CompressImage_Png_PreservesTransparency()
    {
        using var stream = CreateDetailedImage(300, 300, SKEncodedImageFormat.Png, transparent: true);
        var request = new CompressRequest { PngCompressionLevel = 9 };

        var result = await _compressionService.CompressImageAsync(stream, "transparent.png", request);

        Assert.True(result.Success);
        using var codec = SKCodec.Create(new SKMemoryStream(result.ProcessedBytes!));
        Assert.NotNull(codec);
        Assert.NotEqual(SKAlphaType.Opaque, codec.Info.AlphaType);

        using var bitmap = SKBitmap.Decode(codec);
        Assert.NotNull(bitmap);
        // Verify corner pixel remained transparent (0,0 was cleared to Transparent)
        SKColor cornerPixel = bitmap.GetPixel(0, 0);
        Assert.Equal(0, cornerPixel.Alpha);
    }

    [Fact]
    public async Task CompressImage_Png_LosslessFidelityPreserved()
    {
        // Simple test bitmap with specific pixel values
        using var origBitmap = new SKBitmap(50, 50, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(origBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.Crimson };
            canvas.DrawRect(10, 10, 30, 30, paint);
        }

        using var origImage = SKImage.FromBitmap(origBitmap);
        using var origPngData = origImage.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(origPngData.ToArray());

        var result = await _compressionService.CompressImageAsync(stream, "fidelity.png", new CompressRequest { PngCompressionLevel = 9 });

        Assert.True(result.Success);
        using var outCodec = SKCodec.Create(new SKMemoryStream(result.ProcessedBytes!));
        using var outBitmap = SKBitmap.Decode(outCodec);

        Assert.NotNull(outBitmap);
        Assert.Equal(origBitmap.Width, outBitmap.Width);
        Assert.Equal(origBitmap.Height, outBitmap.Height);

        // Verify crimson square pixel
        Assert.Equal(origBitmap.GetPixel(25, 25), outBitmap.GetPixel(25, 25));
        // Verify transparent pixel
        Assert.Equal(origBitmap.GetPixel(5, 5).Alpha, outBitmap.GetPixel(5, 5).Alpha);
    }

    [Fact]
    public async Task CompressImage_Gif_RejectedWithoutConversion()
    {
        // GIF header (GIF89a)
        byte[] gifBytes = new byte[] {
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, // GIF89a
            0x0A, 0x00, 0x0A, 0x00,             // 10x10
            0x80, 0x00, 0x00,                   // GCT flag
            0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, // palette
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x0A, 0x00, 0x00, // image descriptor
            0x02, 0x02, 0x44, 0x01, 0x00, 0x3B  // raster data & trailer
        };

        using var stream = new MemoryStream(gifBytes);

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            _compressionService.CompressImageAsync(stream, "animation.gif", new CompressRequest()));

        Assert.Equal("Compression is not supported for this format without conversion.", ex.Message);
    }

    [Fact]
    public async Task CompressImage_Bmp_RejectedWithoutConversion()
    {
        byte[] bmpBytes = new byte[]
        {
            0x42, 0x4D,             // 'BM'
            0x3A, 0x00, 0x00, 0x00, // File size: 58 bytes
            0x00, 0x00, 0x00, 0x00, // Reserved
            0x36, 0x00, 0x00, 0x00, // Offset to pixel data: 54
            0x28, 0x00, 0x00, 0x00, // Header size: 40
            0x01, 0x00, 0x00, 0x00, // Width: 1
            0x01, 0x00, 0x00, 0x00, // Height: 1
            0x01, 0x00,             // Planes: 1
            0x18, 0x00,             // Bits per pixel: 24 (RGB)
            0x00, 0x00, 0x00, 0x00, // Compression: 0 (BI_RGB)
            0x04, 0x00, 0x00, 0x00, // Image size: 4 bytes (padded)
            0x13, 0x0B, 0x00, 0x00, // X pixels per meter
            0x13, 0x0B, 0x00, 0x00, // Y pixels per meter
            0x00, 0x00, 0x00, 0x00, // Total colors
            0x00, 0x00, 0x00, 0x00, // Important colors
            0xFF, 0x00, 0x00, 0x00  // Pixel data: B=0xFF, G=0x00, R=0x00 + 1 padding byte
        };
        using var stream = new MemoryStream(bmpBytes);

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            _compressionService.CompressImageAsync(stream, "graphic.bmp", new CompressRequest()));

        Assert.Equal("Compression is not supported for this format without conversion.", ex.Message);
    }

    [Fact]
    public async Task CompressImage_FakeJpeg_Rejected()
    {
        // Plain text with .jpg extension
        byte[] fakeData = System.Text.Encoding.UTF8.GetBytes("Not an actual JPEG image file");
        using var stream = new MemoryStream(fakeData);

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            _compressionService.CompressImageAsync(stream, "fake.jpg", new CompressRequest()));

        Assert.Equal("The file contents do not match a supported image format.", ex.Message);
    }

    [Fact]
    public async Task CompressImage_FakePng_Rejected()
    {
        byte[] fakeData = System.Text.Encoding.UTF8.GetBytes("Not a PNG image file");
        using var stream = new MemoryStream(fakeData);

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            _compressionService.CompressImageAsync(stream, "fake.png", new CompressRequest()));

        Assert.Equal("The file contents do not match a supported image format.", ex.Message);
    }

    [Fact]
    public async Task CompressImage_FakeWebP_Rejected()
    {
        byte[] fakeData = System.Text.Encoding.UTF8.GetBytes("Not a WebP file");
        using var stream = new MemoryStream(fakeData);

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            _compressionService.CompressImageAsync(stream, "fake.webp", new CompressRequest()));

        Assert.Equal("The file contents do not match a supported image format.", ex.Message);
    }

    [Fact]
    public async Task CompressImage_OversizedFile_Rejected()
    {
        var smallOptions = Options.Create(new ImageProcessingOptions { MaxFileSizeBytes = 100 });
        var resizeService = new ImageResizeService(smallOptions, NullLogger<ImageResizeService>.Instance);
        var compressionService = new ImageCompressionService(resizeService, smallOptions, NullLogger<ImageCompressionService>.Instance);

        byte[] payload = new byte[200];
        using var stream = new MemoryStream(payload);

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            compressionService.CompressImageAsync(stream, "large.jpg", new CompressRequest()));

        Assert.Equal("The uploaded file exceeds the maximum allowed size.", ex.Message);
    }

    [Fact]
    public async Task CompressImage_ExcessiveDimensions_Rejected()
    {
        var customOptions = Options.Create(new ImageProcessingOptions
        {
            MaxInputWidth = 500,
            MaxInputHeight = 500
        });
        var resizeService = new ImageResizeService(customOptions, NullLogger<ImageResizeService>.Instance);
        var compressionService = new ImageCompressionService(resizeService, customOptions, NullLogger<ImageCompressionService>.Instance);

        using var stream = CreateDetailedImage(600, 600, SKEncodedImageFormat.Jpeg);

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            compressionService.CompressImageAsync(stream, "big.jpg", new CompressRequest()));

        Assert.Equal("The image dimensions exceed the maximum allowed size.", ex.Message);
    }

    [Fact]
    public async Task CompressImage_ExcessivePixelCount_DecompressionBomb_Rejected()
    {
        var customOptions = Options.Create(new ImageProcessingOptions
        {
            MaxInputWidth = 10000,
            MaxInputHeight = 10000,
            MaxInputPixels = 50000 // Limit to 50,000 pixels (e.g. 250x250 exceeds)
        });
        var resizeService = new ImageResizeService(customOptions, NullLogger<ImageResizeService>.Instance);
        var compressionService = new ImageCompressionService(resizeService, customOptions, NullLogger<ImageCompressionService>.Instance);

        using var stream = CreateDetailedImage(300, 300, SKEncodedImageFormat.Jpeg); // 90,000 pixels

        var ex = await Assert.ThrowsAsync<ImageValidationException>(() =>
            compressionService.CompressImageAsync(stream, "bomb.jpg", new CompressRequest()));

        Assert.Equal("The image contains too many pixels to be safely processed.", ex.Message);
    }

    [Fact]
    public async Task CompressImage_PathTraversalFileName_Sanitized()
    {
        using var stream = CreateDetailedImage(100, 100, SKEncodedImageFormat.Jpeg);

        var result = await _compressionService.CompressImageAsync(stream, "../../secret/passwords.jpg", new CompressRequest());

        Assert.True(result.Success);
        Assert.DoesNotContain("..", result.SuggestedFileName);
        Assert.DoesNotContain("/", result.SuggestedFileName);
        Assert.DoesNotContain("\\", result.SuggestedFileName);
        Assert.Equal("passwords_compressed.jpg", result.SuggestedFileName);
    }

    [Fact]
    public async Task CompressImage_MetricsCalculatedAccurately()
    {
        using var stream = CreateDetailedImage(800, 600, SKEncodedImageFormat.Jpeg);
        long originalStreamLength = stream.Length;

        var result = await _compressionService.CompressImageAsync(stream, "photo.jpg", new CompressRequest { Quality = 50 });

        Assert.True(result.Success);
        Assert.Equal(originalStreamLength, result.OriginalFileSizeBytes);
        Assert.Equal(result.ProcessedBytes!.Length, result.CompressedFileSizeBytes);
        Assert.False(result.IsLargerThanOriginal);

        if (result.CompressedFileSizeBytes < result.OriginalFileSizeBytes)
        {
            Assert.False(result.IsAlreadyOptimized);
            Assert.Equal(result.OriginalFileSizeBytes - result.CompressedFileSizeBytes, result.SavedBytes);
            Assert.True(result.ReductionPercentage > 0);
        }
        else
        {
            Assert.True(result.IsAlreadyOptimized);
            Assert.Equal(0, result.SavedBytes);
            Assert.Equal(0.0, result.ReductionPercentage);
        }
    }

    [Fact]
    public async Task CompressImage_WhenAlreadyOptimal_RetainsOriginalBytesWithoutBloat()
    {
        // Generate a tiny PNG that is already small
        using var bitmap = new SKBitmap(20, 20);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var originalPngData = image.Encode(SKEncodedImageFormat.Png, 100);
        byte[] originalBytes = originalPngData.ToArray();

        using var stream = new MemoryStream(originalBytes);

        var result = await _compressionService.CompressImageAsync(stream, "optimized.png", new CompressRequest { PngCompressionLevel = 1 });

        Assert.True(result.Success);
        Assert.True(result.CompressedFileSizeBytes <= result.OriginalFileSizeBytes);
        Assert.False(result.IsLargerThanOriginal);
        Assert.NotNull(result.ProcessedBytes);
        Assert.NotNull(result.StatusMessage);
    }

    [Fact]
    public async Task CompressImage_Png_CompressibleImage_ProducesValidNonBloatedResult()
    {
        // Detailed high-frequency image in PNG format
        using var stream = CreateDetailedImage(600, 600, SKEncodedImageFormat.Png);
        long originalLen = stream.Length;

        var result = await _compressionService.CompressImageAsync(stream, "graphic.png", new CompressRequest { PngCompressionLevel = 9 });

        Assert.True(result.Success);
        Assert.Equal(600, result.OutputMetadata?.Width);
        Assert.Equal(600, result.OutputMetadata?.Height);
        Assert.True(result.CompressedFileSizeBytes <= originalLen);
        Assert.False(result.IsLargerThanOriginal);
        Assert.True(result.ReductionPercentage >= 0.0);
        Assert.True(result.SavedBytes >= 0);
    }

    [Fact]
    public async Task CompressImage_Png_ZLibEffort_Level9ProducesEqualOrSmallerCandidateThanLevel1()
    {
        // Raw bitmap encoded at level 1 vs level 9 directly via SkiaSharp encoder
        var info = new SKImageInfo(200, 200, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.DarkSlateBlue);
            using var paint = new SKPaint { Color = SKColors.Gold };
            for (int i = 0; i < 200; i += 10)
            {
                canvas.DrawLine(0, i, 200, i, paint);
            }
        }

        using var pixmap = bitmap.PeekPixels();
        var opt1 = new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 1);
        var opt9 = new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 9);

        using var data1 = pixmap.Encode(opt1);
        using var data9 = pixmap.Encode(opt9);

        Assert.NotNull(data1);
        Assert.NotNull(data9);
        Assert.True(data9.Size <= data1.Size, $"Expected Level 9 ({data9.Size} bytes) <= Level 1 ({data1.Size} bytes)");
    }

    [Fact]
    public async Task CompressImage_Png_IndexedColorPng_HandledSafelyWithoutBloat()
    {
        // Construct a valid 2x2 indexed PNG binary with a PLTE chunk (Red and Green palette)
        byte[] indexedPng = CreateValid2x2IndexedPng();

        using var stream = new MemoryStream(indexedPng);

        var result = await _compressionService.CompressImageAsync(stream, "palette.png", new CompressRequest { PngCompressionLevel = 9 });

        Assert.True(result.Success);
        Assert.Equal(2, result.OutputMetadata?.Width);
        Assert.Equal(2, result.OutputMetadata?.Height);
        Assert.Equal("PNG", result.OutputMetadata?.Format);
        // Guarantee that the returned file length is not larger than original
        Assert.True(result.CompressedFileSizeBytes <= result.OriginalFileSizeBytes);
        Assert.False(result.IsLargerThanOriginal);
        Assert.True(result.IsAlreadyOptimized);
        Assert.Equal(0, result.SavedBytes);
        Assert.Equal(0.0, result.ReductionPercentage);
        Assert.Contains("original retained", result.StatusMessage ?? "", StringComparison.OrdinalIgnoreCase);

        // Verify decoded pixels match palette values
        using var outCodec = SKCodec.Create(new SKMemoryStream(result.ProcessedBytes!));
        using var outBmp = SKBitmap.Decode(outCodec);
        Assert.NotNull(outBmp);
        Assert.Equal(2, outBmp.Width);
        Assert.Equal(2, outBmp.Height);
    }

    [Fact]
    public async Task Verify_PngSampleCategories_ProduceAccurateMetricsAndLosslessPreservation()
    {
        // 1. Photographic / High-Detail PNG (400x400)
        using var photoStream = CreateDetailedImage(400, 400, SKEncodedImageFormat.Png);
        var photoResult = await _compressionService.CompressImageAsync(photoStream, "photo.png", new CompressRequest { PngCompressionLevel = 9 });
        Assert.True(photoResult.Success);
        Assert.Equal(400, photoResult.OutputMetadata?.Width);
        Assert.Equal(400, photoResult.OutputMetadata?.Height);
        Assert.True(photoResult.CompressedFileSizeBytes <= photoResult.OriginalFileSizeBytes);

        // 2. Transparent PNG (300x300 with Alpha)
        using var transStream = CreateDetailedImage(300, 300, SKEncodedImageFormat.Png, transparent: true);
        var transResult = await _compressionService.CompressImageAsync(transStream, "trans.png", new CompressRequest { PngCompressionLevel = 9 });
        Assert.True(transResult.Success);
        Assert.Equal(300, transResult.OutputMetadata?.Width);
        Assert.Equal(300, transResult.OutputMetadata?.Height);
        Assert.True(transResult.CompressedFileSizeBytes <= transResult.OriginalFileSizeBytes);

        // 3. Indexed / Paletted PNG (2x2 Color Type 3)
        byte[] indexedBytes = CreateValid2x2IndexedPng();
        using var indexedStream = new MemoryStream(indexedBytes);
        var indexedResult = await _compressionService.CompressImageAsync(indexedStream, "indexed.png", new CompressRequest { PngCompressionLevel = 9 });
        Assert.True(indexedResult.Success);
        Assert.Equal(2, indexedResult.OutputMetadata?.Width);
        Assert.Equal(2, indexedResult.OutputMetadata?.Height);
        Assert.Equal(indexedBytes.Length, indexedResult.CompressedFileSizeBytes);
        Assert.True(indexedResult.IsAlreadyOptimized);

        // 4. Simple Graphics / Logo PNG (200x200 flat shapes)
        using var logoBitmap = new SKBitmap(200, 200, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(logoBitmap))
        {
            canvas.Clear(SKColors.Navy);
            using var p = new SKPaint { Color = SKColors.Orange, IsAntialias = true };
            canvas.DrawCircle(100, 100, 60, p);
        }
        using var logoImg = SKImage.FromBitmap(logoBitmap);
        using var logoData = logoImg.Encode(SKEncodedImageFormat.Png, 100);
        using var logoStream = new MemoryStream(logoData.ToArray());
        var logoResult = await _compressionService.CompressImageAsync(logoStream, "logo.png", new CompressRequest { PngCompressionLevel = 9 });
        Assert.True(logoResult.Success);
        Assert.Equal(200, logoResult.OutputMetadata?.Width);
        Assert.Equal(200, logoResult.OutputMetadata?.Height);
        Assert.True(logoResult.CompressedFileSizeBytes <= logoResult.OriginalFileSizeBytes);

        // 5. Already Optimized PNG (small solid icon)
        using var iconBitmap = new SKBitmap(16, 16, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(iconBitmap))
        {
            canvas.Clear(SKColors.White);
        }
        using var iconImg = SKImage.FromBitmap(iconBitmap);
        using var iconData = iconImg.Encode(SKEncodedImageFormat.Png, 100);
        using var iconStream = new MemoryStream(iconData.ToArray());
        var iconResult = await _compressionService.CompressImageAsync(iconStream, "icon.png", new CompressRequest { PngCompressionLevel = 9 });
        Assert.True(iconResult.Success);
        Assert.Equal(16, iconResult.OutputMetadata?.Width);
        Assert.Equal(16, iconResult.OutputMetadata?.Height);
        Assert.True(iconResult.CompressedFileSizeBytes <= iconResult.OriginalFileSizeBytes);
    }

    [Fact]
    public async Task CompressImage_Png_PixelFidelity_Is100PercentLossless()
    {
        // Construct a multi-color bitmap
        int w = 50, h = 50;
        using var origBitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                origBitmap.SetPixel(x, y, new SKColor((byte)(x * 5), (byte)(y * 5), (byte)((x + y) * 2), 255));
            }
        }

        using var img = SKImage.FromBitmap(origBitmap);
        using var pngData = img.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(pngData.ToArray());

        var result = await _compressionService.CompressImageAsync(stream, "grid.png", new CompressRequest { PngCompressionLevel = 9 });

        Assert.True(result.Success);
        using var outCodec = SKCodec.Create(new SKMemoryStream(result.ProcessedBytes!));
        using var outBitmap = SKBitmap.Decode(outCodec);

        Assert.NotNull(outBitmap);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Assert.Equal(origBitmap.GetPixel(x, y), outBitmap.GetPixel(x, y));
            }
        }
    }

    private static byte[] CreateValid2x2IndexedPng()
    {
        // 2x2 indexed PNG with 2-color PLTE palette (Red: 255,0,0; Green: 0,255,0)
        // Header + IHDR (13 bytes) + PLTE (6 bytes) + IDAT + IEND
        using var ms = new MemoryStream();
        // PNG signature
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR: 2x2, 8-bit depth, color type 3 (Indexed), compression 0, filter 0, interlace 0
        WriteChunk(ms, "IHDR", new byte[] {
            0x00, 0x00, 0x00, 0x02, // Width = 2
            0x00, 0x00, 0x00, 0x02, // Height = 2
            0x08,                   // Bit depth = 8
            0x03,                   // Color type = 3 (Indexed)
            0x00,                   // Compression = 0
            0x00,                   // Filter = 0
            0x00                    // Interlace = 0
        });

        // PLTE: 2 entries (Red: FF 00 00, Green: 00 FF 00)
        WriteChunk(ms, "PLTE", new byte[] {
            0xFF, 0x00, 0x00,
            0x00, 0xFF, 0x00
        });

        // IDAT: Scanlines with Filter type 0 (None):
        // Row 0: filter(0), index 0 (Red), index 1 (Green)
        // Row 1: filter(0), index 1 (Green), index 0 (Red)
        byte[] uncompressedScanlines = new byte[] {
            0x00, 0x00, 0x01,
            0x00, 0x01, 0x00
        };

        using (var idatPayload = new MemoryStream())
        {
            // ZLib header: 0x78, 0x9C (Default compression)
            idatPayload.WriteByte(0x78);
            idatPayload.WriteByte(0x9C);
            using (var deflate = new System.IO.Compression.DeflateStream(idatPayload, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(uncompressedScanlines, 0, uncompressedScanlines.Length);
            }
            // Adler32 checksum for uncompressedScanlines:
            uint adler = CalculateAdler32(uncompressedScanlines);
            idatPayload.WriteByte((byte)((adler >> 24) & 0xFF));
            idatPayload.WriteByte((byte)((adler >> 16) & 0xFF));
            idatPayload.WriteByte((byte)((adler >> 8) & 0xFF));
            idatPayload.WriteByte((byte)(adler & 0xFF));

            WriteChunk(ms, "IDAT", idatPayload.ToArray());
        }

        // IEND
        WriteChunk(ms, "IEND", Array.Empty<byte>());

        return ms.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        byte[] lenBytes = BitConverter.GetBytes((uint)data.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);

        output.Write(lenBytes, 0, 4);
        output.Write(typeBytes, 0, 4);
        if (data.Length > 0)
        {
            output.Write(data, 0, data.Length);
        }

        // CRC32 over type + data
        uint crc = CalculateCrc32(typeBytes, data);
        byte[] crcBytes = BitConverter.GetBytes(crc);
        if (BitConverter.IsLittleEndian) Array.Reverse(crcBytes);
        output.Write(crcBytes, 0, 4);
    }

    private static uint CalculateCrc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFF;
    }

    private static readonly uint[] CrcTable = InitializeCrcTable();

    private static uint[] InitializeCrcTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[i] = c;
        }
        return table;
    }

    private static uint CalculateAdler32(byte[] data)
    {
        uint a = 1, b = 0;
        const uint MOD_ADLER = 65521;
        foreach (byte byteVal in data)
        {
            a = (a + byteVal) % MOD_ADLER;
            b = (b + a) % MOD_ADLER;
        }
        return (b << 16) | a;
    }
}
