using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PixelForge.Configuration;
using PixelForge.Exceptions;
using PixelForge.Models;
using PixelForge.Services;
using PixelForge.Services.Interfaces;

namespace PixelForge.Controllers;

/// <summary>
/// API controller handling image inspection and resizing operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("ImageProcessingPolicy")]
public class ImageController : ControllerBase
{
    private readonly IImageResizeService _imageService;
    private readonly IImageCompressionService _compressionService;
    private readonly ImageProcessingOptions _options;
    private readonly ILogger<ImageController> _logger;

    [ActivatorUtilitiesConstructor]
    public ImageController(
        IImageResizeService imageService,
        IImageCompressionService compressionService,
        IOptions<ImageProcessingOptions> options,
        ILogger<ImageController> logger)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        _compressionService = compressionService ?? throw new ArgumentNullException(nameof(compressionService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ImageController(
        IImageResizeService imageService,
        IOptions<ImageProcessingOptions> options,
        ILogger<ImageController> logger)
        : this(imageService, new ImageCompressionService(imageService, options, Microsoft.Extensions.Logging.Abstractions.NullLogger<ImageCompressionService>.Instance), options, logger)
    {
    }

    /// <summary>
    /// Inspects an uploaded image file and returns its technical metadata.
    /// </summary>
    /// <param name="file">Uploaded image file from multipart form data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Image metadata or structured error details.</returns>
    [HttpPost("inspect")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ImageInspectResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ImageInspectResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ImageInspectResult), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ImageInspectResult), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Inspect([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received image inspect request");

        if (file == null || file.Length == 0)
        {
            _logger.LogWarning("Inspect rejected: uploaded file is null or empty");
            return BadRequest(new ImageInspectResult
            {
                Success = false,
                ErrorMessage = "The uploaded file is empty."
            });
        }

        if (file.Length > _options.MaxFileSizeBytes)
        {
            _logger.LogWarning("Inspect rejected: file size {Size} bytes exceeds maximum limit of {Max} bytes",
                file.Length, _options.MaxFileSizeBytes);
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ImageInspectResult
            {
                Success = false,
                ErrorMessage = $"The uploaded file exceeds the maximum allowed size of {_options.MaxFileSizeBytes / (1024 * 1024)} MB."
            });
        }

        string safeFileName = SanitizeForLog(file.FileName);

        try
        {
            using var stream = file.OpenReadStream();
            var metadata = await _imageService.InspectImageAsync(stream, safeFileName, cancellationToken);

            _logger.LogInformation("Image inspection successful for {FileName}: {Width}x{Height} {Format}",
                safeFileName, metadata.Width, metadata.Height, metadata.Format);

            return Ok(new ImageInspectResult
            {
                Success = true,
                Metadata = metadata
            });
        }
        catch (ImageValidationException ex)
        {
            _logger.LogWarning(ex, "Image validation failed during inspect for {FileName}: {Message}", safeFileName, ex.Message);
            return BadRequest(new ImageInspectResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Image inspection was cancelled for {FileName}", safeFileName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during image inspection for {FileName}", safeFileName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ImageInspectResult
            {
                Success = false,
                ErrorMessage = "An unexpected error occurred while processing the image."
            });
        }
    }

    /// <summary>
    /// Resizes an uploaded image according to the specified request parameters.
    /// </summary>
    /// <param name="request">Multipart form data containing the image and resize parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resize result containing metadata and processed image bytes.</returns>
    [HttpPost("resize")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ResizeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResizeResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResizeResult), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ResizeResult), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Resize([FromForm] ResizeRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received image resize request");

        if (request == null || request.File == null || request.File.Length == 0)
        {
            _logger.LogWarning("Resize rejected: request or file is null/empty");
            return BadRequest(new ResizeResult
            {
                Success = false,
                ErrorMessage = "The uploaded file is empty."
            });
        }

        if (request.File.Length > _options.MaxFileSizeBytes)
        {
            _logger.LogWarning("Resize rejected: file size {Size} bytes exceeds maximum limit of {Max} bytes",
                request.File.Length, _options.MaxFileSizeBytes);
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ResizeResult
            {
                Success = false,
                ErrorMessage = $"The uploaded file exceeds the maximum allowed size of {_options.MaxFileSizeBytes / (1024 * 1024)} MB."
            });
        }

        if (!ModelState.IsValid)
        {
            string errorMessage = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault() ?? "Invalid resize request parameters.";

            _logger.LogWarning("Resize rejected: ModelState invalid: {Error}", errorMessage);
            return BadRequest(new ResizeResult
            {
                Success = false,
                ErrorMessage = errorMessage
            });
        }

        string safeFileName = SanitizeForLog(request.File.FileName);

        try
        {
            using var stream = request.File.OpenReadStream();
            var result = await _imageService.ResizeImageAsync(stream, safeFileName, request, cancellationToken);

            _logger.LogInformation("Image resize successful for {FileName}: output {Width}x{Height} ({Bytes} bytes)",
                safeFileName, result.OutputMetadata?.Width, result.OutputMetadata?.Height, result.ProcessedBytes?.Length);

            return Ok(result);
        }
        catch (ImageValidationException ex)
        {
            _logger.LogWarning(ex, "Image validation failed during resize for {FileName}: {Message}", safeFileName, ex.Message);
            return BadRequest(new ResizeResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Image resize was cancelled for {FileName}", safeFileName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during image resize for {FileName}", safeFileName);
            return StatusCode(StatusCodes.Status500InternalServerError, new ResizeResult
            {
                Success = false,
                ErrorMessage = "An unexpected error occurred while processing the image."
            });
        }
    }

    /// <summary>
    /// Compresses an uploaded image attempting to reduce encoded file size without changing dimensions.
    /// </summary>
    /// <param name="request">Multipart form data containing the image and compression parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compression result containing metadata, size reduction metrics, and processed image bytes.</returns>
    [HttpPost("compress")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CompressResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CompressResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CompressResult), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(CompressResult), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Compress([FromForm] CompressRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received image compress request");

        if (request == null || request.File == null || request.File.Length == 0)
        {
            _logger.LogWarning("Compress rejected: request or file is null/empty");
            return BadRequest(new CompressResult
            {
                Success = false,
                ErrorMessage = "The uploaded file is empty."
            });
        }

        if (request.File.Length > _options.MaxFileSizeBytes)
        {
            _logger.LogWarning("Compress rejected: file size {Size} bytes exceeds maximum limit of {Max} bytes",
                request.File.Length, _options.MaxFileSizeBytes);
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new CompressResult
            {
                Success = false,
                ErrorMessage = $"The uploaded file exceeds the maximum allowed size of {_options.MaxFileSizeBytes / (1024 * 1024)} MB."
            });
        }

        if (!ModelState.IsValid)
        {
            string errorMessage = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault() ?? "Invalid compress request parameters.";

            _logger.LogWarning("Compress rejected: ModelState invalid: {Error}", errorMessage);
            return BadRequest(new CompressResult
            {
                Success = false,
                ErrorMessage = errorMessage
            });
        }

        string safeFileName = SanitizeForLog(request.File.FileName);

        try
        {
            using var stream = request.File.OpenReadStream();
            var result = await _compressionService.CompressImageAsync(stream, safeFileName, request, cancellationToken);

            _logger.LogInformation("Image compression successful for {FileName}: {OrigBytes} -> {CompBytes} bytes ({Pct}% reduction)",
                safeFileName, result.OriginalFileSizeBytes, result.CompressedFileSizeBytes, result.ReductionPercentage);

            return Ok(result);
        }
        catch (ImageValidationException ex)
        {
            _logger.LogWarning(ex, "Image validation failed during compress for {FileName}: {Message}", safeFileName, ex.Message);
            return BadRequest(new CompressResult
            {
                Success = false,
                ErrorMessage = ex.Message
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Image compression was cancelled for {FileName}", safeFileName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during image compression for {FileName}", safeFileName);
            return StatusCode(StatusCodes.Status500InternalServerError, new CompressResult
            {
                Success = false,
                ErrorMessage = "An unexpected error occurred while processing the image."
            });
        }
    }

    /// <summary>
    /// Sanitizes untrusted client filenames for logging to prevent CRLF injection and log forging.
    /// </summary>
    private static string SanitizeForLog(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return string.Empty;
        }

        var sanitized = new char[fileName.Length];
        int index = 0;
        foreach (char c in fileName)
        {
            if (!char.IsControl(c) && c != '\r' && c != '\n')
            {
                sanitized[index++] = c;
            }
            else
            {
                sanitized[index++] = '_';
            }
        }

        string result = new string(sanitized, 0, index);
        return result.Length > 100 ? result.Substring(0, 100) : result;
    }
}

