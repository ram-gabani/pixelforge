using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using PixelForge.Configuration;
using PixelForge.Services;
using PixelForge.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Configure ImageProcessingOptions from configuration
var imageSection = builder.Configuration.GetSection(ImageProcessingOptions.SectionName);
builder.Services.Configure<ImageProcessingOptions>(imageSection);
var imageOptions = imageSection.Get<ImageProcessingOptions>() ?? new ImageProcessingOptions();

// Configure RateLimitingOptions from configuration
var rateLimitSection = builder.Configuration.GetSection(RateLimitingOptions.SectionName);
builder.Services.Configure<RateLimitingOptions>(rateLimitSection);
var rateLimitOptions = rateLimitSection.Get<RateLimitingOptions>() ?? new RateLimitingOptions();

// Configure multipart and server request limits to match image processing configuration with safe multipart envelope margin
const int multipartEnvelopeMarginBytes = 512 * 1024; // 512 KB margin for multipart boundaries, headers, and form fields
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = imageOptions.MaxFileSizeBytes + multipartEnvelopeMarginBytes;
});
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = imageOptions.MaxFileSizeBytes + multipartEnvelopeMarginBytes;
    serverOptions.AddServerHeader = false; // Suppress Kestrel Server banner in responses
});

// Configure Rate Limiter for expensive image-processing endpoints
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        var currentOptions = context.HttpContext.RequestServices.GetService<Microsoft.Extensions.Options.IOptions<RateLimitingOptions>>()?.Value ?? new RateLimitingOptions();
        context.HttpContext.Response.ContentType = "application/json";
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }
        else
        {
            context.HttpContext.Response.Headers.RetryAfter = currentOptions.WindowSeconds.ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            Success = false,
            ErrorMessage = "You're processing images too quickly. Please wait a moment and try again."
        }, token);
    };

    options.AddPolicy("ImageProcessingPolicy", httpContext =>
    {
        var currentOptions = httpContext.RequestServices.GetService<Microsoft.Extensions.Options.IOptions<RateLimitingOptions>>()?.Value ?? new RateLimitingOptions();
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(
            clientIp,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = currentOptions.PermitLimit,
                Window = TimeSpan.FromSeconds(currentOptions.WindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = currentOptions.QueueLimit
            });
    });
});

// Add services to the container.
builder.Services.AddSingleton<IImageResizeService, ImageResizeService>();
builder.Services.AddSingleton<IImageCompressionService, ImageCompressionService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Production security response headers (Phase 10)
app.Use(async (context, next) =>
{
    // Content-Security-Policy: strict, no unsafe-inline or unsafe-eval for scripts
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: blob:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self';";

    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    await next();
});

app.UseRouting();

app.UseRateLimiter();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// Make Program accessible to WebApplicationFactory for integration tests
public partial class Program { }
