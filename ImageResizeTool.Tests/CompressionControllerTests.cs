using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PixelForge.Configuration;
using PixelForge.Controllers;
using PixelForge.Models;
using PixelForge.Services;
using SkiaSharp;
using Xunit;

namespace PixelForge.Tests;

public class CompressionControllerTests
{
    private readonly ImageController _controller;
    private readonly ImageProcessingOptions _options;

    public CompressionControllerTests()
    {
        _options = new ImageProcessingOptions();
        var optionsWrapper = Options.Create(_options);
        var resizeService = new ImageResizeService(optionsWrapper, NullLogger<ImageResizeService>.Instance);
        var compressionService = new ImageCompressionService(resizeService, optionsWrapper, NullLogger<ImageCompressionService>.Instance);
        _controller = new ImageController(resizeService, compressionService, optionsWrapper, NullLogger<ImageController>.Instance);
    }

    private static IFormFile CreateFormFile(byte[] content, string fileName, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] CreateSampleImageBytes(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.DarkSlateBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    [Fact]
    public async Task Compress_ValidJpeg_ReturnsOkWithCompressResult()
    {
        byte[] bytes = CreateSampleImageBytes(800, 600, SKEncodedImageFormat.Jpeg);
        var file = CreateFormFile(bytes, "photo.jpg", "image/jpeg");
        var request = new CompressRequest { File = file, Quality = 75 };

        var actionResult = await _controller.Compress(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var result = Assert.IsType<CompressResult>(okResult.Value);
        Assert.True(result.Success);
        Assert.NotNull(result.ProcessedBytes);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(800, result.OutputMetadata?.Width);
        Assert.Equal(600, result.OutputMetadata?.Height);
        Assert.Equal("photo_compressed.jpg", result.SuggestedFileName);
    }

    [Fact]
    public async Task Compress_ValidPng_ReturnsOkWithCompressResult()
    {
        byte[] bytes = CreateSampleImageBytes(400, 300, SKEncodedImageFormat.Png);
        var file = CreateFormFile(bytes, "graphic.png", "image/png");
        var request = new CompressRequest { File = file, PngCompressionLevel = 9 };

        var actionResult = await _controller.Compress(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var result = Assert.IsType<CompressResult>(okResult.Value);
        Assert.True(result.Success);
        Assert.NotNull(result.ProcessedBytes);
        Assert.Equal("image/png", result.MimeType);
        Assert.Equal(400, result.OutputMetadata?.Width);
        Assert.Equal(300, result.OutputMetadata?.Height);
        Assert.Equal("graphic_compressed.png", result.SuggestedFileName);
    }

    [Fact]
    public async Task Compress_ValidWebP_ReturnsOkWithCompressResult()
    {
        byte[] bytes = CreateSampleImageBytes(500, 500, SKEncodedImageFormat.Webp);
        var file = CreateFormFile(bytes, "hero.webp", "image/webp");
        var request = new CompressRequest { File = file, Quality = 70 };

        var actionResult = await _controller.Compress(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var result = Assert.IsType<CompressResult>(okResult.Value);
        Assert.True(result.Success);
        Assert.NotNull(result.ProcessedBytes);
        Assert.Equal("image/webp", result.MimeType);
        Assert.Equal(500, result.OutputMetadata?.Width);
        Assert.Equal(500, result.OutputMetadata?.Height);
        Assert.Equal("hero_compressed.webp", result.SuggestedFileName);
    }

    [Fact]
    public async Task Compress_NullFile_ReturnsBadRequest()
    {
        var request = new CompressRequest { File = null };

        var actionResult = await _controller.Compress(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var result = Assert.IsType<CompressResult>(badRequest.Value);
        Assert.False(result.Success);
        Assert.Equal("The uploaded file is empty.", result.ErrorMessage);
    }

    [Fact]
    public async Task Compress_OversizedFile_ReturnsPayloadTooLarge()
    {
        var smallOptions = Options.Create(new ImageProcessingOptions { MaxFileSizeBytes = 100 });
        var resizeService = new ImageResizeService(smallOptions, NullLogger<ImageResizeService>.Instance);
        var compressionService = new ImageCompressionService(resizeService, smallOptions, NullLogger<ImageCompressionService>.Instance);
        var controller = new ImageController(resizeService, compressionService, smallOptions, NullLogger<ImageController>.Instance);

        var file = CreateFormFile(new byte[500], "test.jpg", "image/jpeg");
        var request = new CompressRequest { File = file };

        var actionResult = await controller.Compress(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(actionResult);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, objectResult.StatusCode);
        var result = Assert.IsType<CompressResult>(objectResult.Value);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Compress_Gif_ReturnsBadRequestWithClearMessage()
    {
        byte[] gifBytes = new byte[] {
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x05, 0x00, 0x05, 0x00,
            0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x2C,
            0x00, 0x00, 0x00, 0x00, 0x05, 0x00, 0x05, 0x00, 0x00, 0x02,
            0x02, 0x44, 0x01, 0x00, 0x3B
        };
        var file = CreateFormFile(gifBytes, "anim.gif", "image/gif");
        var request = new CompressRequest { File = file };

        var actionResult = await _controller.Compress(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var result = Assert.IsType<CompressResult>(badRequest.Value);
        Assert.False(result.Success);
        Assert.Equal("Compression is not supported for this format without conversion.", result.ErrorMessage);
    }
}

