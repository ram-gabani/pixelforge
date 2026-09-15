using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PixelForge.Configuration;
using Xunit;

namespace PixelForge.Tests;

public class RateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void RateLimitingOptions_DefaultValues_AreSensible()
    {
        var options = new RateLimitingOptions();
        Assert.Equal(30, options.PermitLimit);
        Assert.Equal(60, options.WindowSeconds);
        Assert.Equal(0, options.QueueLimit);
        Assert.Equal("RateLimiting", RateLimitingOptions.SectionName);
    }

    [Fact]
    public async Task StaticPages_DoNotExceedOrTriggerRateLimiter()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/Home/Privacy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ImageProcessingEndpoints_EnforceRateLimiting_WhenPermitExceeded()
    {
        // Create a dedicated factory configured with a strict rate limit for deterministic testing
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:PermitLimit"] = "2",
                    ["RateLimiting:WindowSeconds"] = "60",
                    ["RateLimiting:QueueLimit"] = "0"
                });
            });
        });

        var client = customFactory.CreateClient();

        // Prepare dummy multipart form data
        using var dummyImageContent1 = new ByteArrayContent(CreateMinimalPngBytes());
        using var formData1 = new MultipartFormDataContent();
        formData1.Add(dummyImageContent1, "file", "test1.png");

        using var dummyImageContent2 = new ByteArrayContent(CreateMinimalPngBytes());
        using var formData2 = new MultipartFormDataContent();
        formData2.Add(dummyImageContent2, "file", "test2.png");

        using var dummyImageContent3 = new ByteArrayContent(CreateMinimalPngBytes());
        using var formData3 = new MultipartFormDataContent();
        formData3.Add(dummyImageContent3, "file", "test3.png");

        // Request 1: Should succeed
        var response1 = await client.PostAsync("/api/image/inspect", formData1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Request 2: Should succeed
        var response2 = await client.PostAsync("/api/image/inspect", formData2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // Request 3: Should be rate limited (HTTP 429)
        var response3 = await client.PostAsync("/api/image/inspect", formData3);
        Assert.Equal(HttpStatusCode.TooManyRequests, response3.StatusCode);
        Assert.True(response3.Headers.Contains("Retry-After"));

        var content = await response3.Content.ReadFromJsonAsync<RateLimitResponse>();
        Assert.NotNull(content);
        Assert.False(content.Success);
        Assert.Contains("too quickly", content.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResizeEndpoint_IsProtectedByRateLimiter()
    {
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:PermitLimit"] = "1",
                    ["RateLimiting:WindowSeconds"] = "60",
                    ["RateLimiting:QueueLimit"] = "0"
                });
            });
        });

        var client = customFactory.CreateClient();

        using var dummyImage1 = new ByteArrayContent(CreateMinimalPngBytes());
        using var form1 = new MultipartFormDataContent();
        form1.Add(dummyImage1, "file", "test.png");
        form1.Add(new StringContent("Percentage"), "mode");
        form1.Add(new StringContent("50"), "percentage");

        using var dummyImage2 = new ByteArrayContent(CreateMinimalPngBytes());
        using var form2 = new MultipartFormDataContent();
        form2.Add(dummyImage2, "file", "test.png");
        form2.Add(new StringContent("Percentage"), "mode");
        form2.Add(new StringContent("50"), "percentage");

        var response1 = await client.PostAsync("/api/image/resize", form1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var response2 = await client.PostAsync("/api/image/resize", form2);
        Assert.Equal(HttpStatusCode.TooManyRequests, response2.StatusCode);
    }

    [Fact]
    public async Task CompressEndpoint_IsProtectedByRateLimiter()
    {
        var customFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:PermitLimit"] = "1",
                    ["RateLimiting:WindowSeconds"] = "60",
                    ["RateLimiting:QueueLimit"] = "0"
                });
            });
        });

        var client = customFactory.CreateClient();

        using var dummyImage1 = new ByteArrayContent(CreateMinimalPngBytes());
        using var form1 = new MultipartFormDataContent();
        form1.Add(dummyImage1, "file", "test.png");
        form1.Add(new StringContent("80"), "quality");

        using var dummyImage2 = new ByteArrayContent(CreateMinimalPngBytes());
        using var form2 = new MultipartFormDataContent();
        form2.Add(dummyImage2, "file", "test.png");
        form2.Add(new StringContent("80"), "quality");

        var response1 = await client.PostAsync("/api/image/compress", form1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var response2 = await client.PostAsync("/api/image/compress", form2);
        Assert.Equal(HttpStatusCode.TooManyRequests, response2.StatusCode);
    }

    private static byte[] CreateMinimalPngBytes()
    {
        using var bitmap = new SkiaSharp.SKBitmap(10, 10);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(SkiaSharp.SKColors.Red);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class RateLimitResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
