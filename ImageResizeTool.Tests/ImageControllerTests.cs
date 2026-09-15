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

public class ImageControllerTests
{
    private readonly ImageController _controller;
    private readonly ImageProcessingOptions _options;

    public ImageControllerTests()
    {
        _options = new ImageProcessingOptions();
        var optionsWrapper = Options.Create(_options);
        var service = new ImageResizeService(optionsWrapper, NullLogger<ImageResizeService>.Instance);
        _controller = new ImageController(service, optionsWrapper, NullLogger<ImageController>.Instance);
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
        canvas.Clear(SKColors.DarkGreen);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    [Fact]
    public async Task Inspect_NullFile_ReturnsBadRequest()
    {
        var result = await _controller.Inspect(null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var inspectResult = Assert.IsType<ImageInspectResult>(badRequest.Value);
        Assert.False(inspectResult.Success);
        Assert.Equal("The uploaded file is empty.", inspectResult.ErrorMessage);
    }

    [Fact]
    public async Task Inspect_OversizedFile_ReturnsPayloadTooLarge()
    {
        // Set small limit options
        var smallOptions = new ImageProcessingOptions { MaxFileSizeBytes = 100 };
        var smallOptionsWrapper = Options.Create(smallOptions);
        var service = new ImageResizeService(smallOptionsWrapper, NullLogger<ImageResizeService>.Instance);
        var controller = new ImageController(service, smallOptionsWrapper, NullLogger<ImageController>.Instance);

        var file = CreateFormFile(new byte[500], "test.jpg", "image/jpeg");

        var result = await controller.Inspect(file, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, objectResult.StatusCode);
        var inspectResult = Assert.IsType<ImageInspectResult>(objectResult.Value);
        Assert.False(inspectResult.Success);
    }

    [Fact]
    public async Task Inspect_ValidImage_ReturnsOkWithMetadata()
    {
        byte[] imageBytes = CreateSampleImageBytes(640, 480, SKEncodedImageFormat.Jpeg);
        var file = CreateFormFile(imageBytes, "photo.jpg", "image/jpeg");

        var result = await _controller.Inspect(file, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var inspectResult = Assert.IsType<ImageInspectResult>(okResult.Value);
        Assert.True(inspectResult.Success);
        Assert.NotNull(inspectResult.Metadata);
        Assert.Equal(640, inspectResult.Metadata.Width);
        Assert.Equal(480, inspectResult.Metadata.Height);
        Assert.Equal("4:3", inspectResult.Metadata.AspectRatioFormatted);
    }

    [Fact]
    public async Task Resize_NullFile_ReturnsBadRequest()
    {
        var request = new ResizeRequest
        {
            File = null,
            Mode = ResizeMode.Percentage,
            Percentage = 100
        };

        var result = await _controller.Resize(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var resizeResult = Assert.IsType<ResizeResult>(badRequest.Value);
        Assert.False(resizeResult.Success);
        Assert.Equal("The uploaded file is empty.", resizeResult.ErrorMessage);
    }

    [Fact]
    public async Task Resize_ValidRequest_ReturnsOkWithResizedData()
    {
        byte[] imageBytes = CreateSampleImageBytes(1000, 500, SKEncodedImageFormat.Png);
        var file = CreateFormFile(imageBytes, "banner.png", "image/png");

        var request = new ResizeRequest
        {
            File = file,
            Mode = ResizeMode.Percentage,
            Percentage = 50,
            TargetFormat = "jpeg",
            Quality = 90
        };

        var result = await _controller.Resize(request, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var resizeResult = Assert.IsType<ResizeResult>(okResult.Value);
        Assert.True(resizeResult.Success);
        Assert.NotNull(resizeResult.ProcessedBytes);
        Assert.NotEmpty(resizeResult.ProcessedBytes);
        Assert.NotNull(resizeResult.OutputMetadata);
        Assert.Equal(500, resizeResult.OutputMetadata.Width);
        Assert.Equal(250, resizeResult.OutputMetadata.Height);
        Assert.Equal("image/jpeg", resizeResult.MimeType);
    }
}

