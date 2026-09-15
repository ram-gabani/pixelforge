using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PixelForge.Configuration;
using PixelForge.Exceptions;
using PixelForge.Models;
using PixelForge.Services.Interfaces;
using SkiaSharp;

namespace PixelForge.Services;

/// <summary>
/// Service responsible for validating and compressing images while strictly preserving original dimensions and format.
/// 
/// TECHNICAL ARCHITECTURAL NOTE ON PNG COMPRESSION IN SKIASHARP 4.151.1:
/// 1. Lossless PNG Semantics:
///    PNG compression is strictly lossless. Pixels, dimensions, and alpha transparency remain 100% intact.
/// 2. SkiaSharp / libpng Color Representation Limitations:
///    Modern Skia removed the Index8 (8-bit palette) color type in milestone 68. When SKCodec decodes an
///    indexed PNG (PNG Color Type 3) or Grayscale+Alpha (PNG Color Type 4), libpng automatically expands
///    the indexed pixels into full 32-bit RGBA (SKColorType.Rgba8888 / Bgra8888). Furthermore, Skia's
///    SKPngEncoderOptions does not provide an API to write custom 8-bit palette PLTE chunks.
/// 3. Mitigation & Smart Candidate Selection:
///    When re-encoding an already optimized or indexed PNG, 32-bit Deflate output can sometimes be equal
///    or larger than the original input. The compression engine encodes a candidate using
///    SKPixmap.Encode(SKPngEncoderOptions) with SKPngEncoderFilterFlags.AllFilters and ZLibLevel (0..9).
///    - If candidateBytes < originalBytes: The compressed candidate is returned with honest metrics.
///    - If candidateBytes >= originalBytes: The compressor NEVER returns a bloated file. The pristine original
///      bytes are retained (processedBytes = originalBytes), SavedBytes is 0, ReductionPercentage is 0.0%,
///      and an honest status message is reported.
/// </summary>
public class ImageCompressionService : IImageCompressionService
{
    private readonly IImageResizeService _resizeService;
    private readonly ImageProcessingOptions _options;
    private readonly ILogger<ImageCompressionService> _logger;

    public ImageCompressionService(
        IImageResizeService resizeService,
        IOptions<ImageProcessingOptions> options,
        ILogger<ImageCompressionService> logger)
    {
        _resizeService = resizeService ?? throw new ArgumentNullException(nameof(resizeService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<CompressResult> CompressImageAsync(
        Stream imageStream,
        string fileName,
        CompressRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // 1. Inspect and validate source image using established security infrastructure
        ImageMetadata originalMetadata = await _resizeService.InspectImageAsync(imageStream, fileName, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // 2. Validate supported format for compression
        string formatUpper = originalMetadata.Format.ToUpperInvariant();

        if (formatUpper is "GIF" or "BMP")
        {
            _logger.LogWarning("Compression rejected: format {Format} is not supported without format conversion for file {FileName}",
                formatUpper, fileName);
            throw new ImageValidationException("Compression is not supported for this format without conversion.");
        }

        if (formatUpper is not ("JPEG" or "PNG" or "WEBP"))
        {
            _logger.LogWarning("Compression rejected: format {Format} is unsupported for file {FileName}",
                formatUpper, fileName);
            throw new ImageValidationException($"Compression is not supported for '{originalMetadata.Format}'.");
        }

        // 3. Read stream safely (buffering non-seekable streams if needed) and capture original bytes
        MemoryStream? bufferStream = null;
        Stream effectiveStream = imageStream;
        byte[] originalBytes;

        try
        {
            if (!imageStream.CanSeek)
            {
                bufferStream = new MemoryStream();
                await imageStream.CopyToAsync(bufferStream, cancellationToken);
                originalBytes = bufferStream.ToArray();
                bufferStream.Position = 0;
                effectiveStream = bufferStream;
            }
            else
            {
                imageStream.Position = 0;
                using var copyMs = new MemoryStream();
                await imageStream.CopyToAsync(copyMs, cancellationToken);
                originalBytes = copyMs.ToArray();
                imageStream.Position = 0;
                effectiveStream = imageStream;
            }

            // 4. Decode source bitmap at exact original dimensions (no SKSurface scaling)
            using var managedStream = new SKManagedStream(effectiveStream, false);
            using var codec = SKCodec.Create(managedStream);
            if (codec == null)
            {
                _logger.LogWarning("SKCodec could not decode stream during compression of {FileName}", fileName);
                throw new ImageValidationException("The image could not be decoded.");
            }

            using var sourceBitmap = SKBitmap.Decode(codec);
            if (sourceBitmap == null)
            {
                _logger.LogWarning("SKBitmap.Decode returned null for {FileName}", fileName);
                throw new ImageValidationException("The image could not be decoded.");
            }

            // Explicit dimension invariance verification
            if (sourceBitmap.Width != originalMetadata.Width || sourceBitmap.Height != originalMetadata.Height)
            {
                _logger.LogError("Decoded dimensions ({DecodedW}x{DecodedH}) do not match inspected dimensions ({InspectW}x{InspectH}) for {FileName}",
                    sourceBitmap.Width, sourceBitmap.Height, originalMetadata.Width, originalMetadata.Height, fileName);
                throw new InvalidOperationException("Decoded image dimensions do not match inspected dimensions.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 5. Encode using format-specific encoder options (preserving format, strictly no conversion)
            using var pixmap = sourceBitmap.PeekPixels();
            if (pixmap == null)
            {
                _logger.LogError("Failed to peek pixels from decoded bitmap for {FileName}", fileName);
                throw new InvalidOperationException("Failed to access bitmap pixels for compression.");
            }

            SKData? encodedData;
            string outputExtension;
            string outputMimeType;
            string outputFormatName;

            switch (formatUpper)
            {
                case "JPEG":
                    {
                        int quality = request.Quality is >= 1 and <= 100 ? request.Quality : _options.DefaultJpegCompressQuality;
                        var jpegOptions = new SKJpegEncoderOptions(
                            quality,
                            SKJpegEncoderDownsample.Downsample420,
                            SKJpegEncoderAlphaOption.Ignore);

                        encodedData = pixmap.Encode(jpegOptions);
                        outputExtension = ".jpg";
                        outputMimeType = "image/jpeg";
                        outputFormatName = "JPEG";
                        break;
                    }
                case "WEBP":
                    {
                        int quality = request.Quality is >= 1 and <= 100 ? request.Quality : _options.DefaultWebpCompressQuality;
                        var webpOptions = new SKWebpEncoderOptions(
                            SKWebpEncoderCompression.Lossy,
                            quality);

                        encodedData = pixmap.Encode(webpOptions);
                        outputExtension = ".webp";
                        outputMimeType = "image/webp";
                        outputFormatName = "WEBP";
                        break;
                    }
                case "PNG":
                    {
                        int zLibLevel = request.PngCompressionLevel is >= 0 and <= 9 ? request.PngCompressionLevel : _options.DefaultPngZLibLevel;
                        var pngOptions = new SKPngEncoderOptions(
                            SKPngEncoderFilterFlags.AllFilters,
                            zLibLevel);

                        encodedData = pixmap.Encode(pngOptions);
                        outputExtension = ".png";
                        outputMimeType = "image/png";
                        outputFormatName = "PNG";
                        break;
                    }
                default:
                    throw new ImageValidationException($"Compression is not supported for '{originalMetadata.Format}'.");
            }

            if (encodedData == null)
            {
                _logger.LogError("Failed to encode compressed image into {Format} for {FileName}", formatUpper, fileName);
                throw new ImageValidationException($"Failed to encode image to {formatUpper}.");
            }

            using (encodedData)
            {
                byte[] candidateBytes = encodedData.ToArray();
                byte[] processedBytes;
                bool isAlreadyOptimized;
                string statusMessage;

                long originalSize = originalMetadata.FileSizeBytes;

                // Smart Result Selection:
                // If candidate is strictly smaller than original, use it.
                // If candidate is equal to or larger than original, retain original bytes to prevent file bloat.
                if (candidateBytes.Length < originalBytes.Length && originalBytes.Length > 0)
                {
                    processedBytes = candidateBytes;
                    isAlreadyOptimized = false;
                    long saved = originalSize - candidateBytes.Length;
                    double pct = Math.Round(((double)saved / originalSize) * 100.0, 2);
                    statusMessage = $"Compressed successfully — {pct}% smaller ({FormatBytes(saved)} saved)";
                }
                else
                {
                    processedBytes = originalBytes;
                    isAlreadyOptimized = true;
                    if (candidateBytes.Length > originalBytes.Length)
                    {
                        statusMessage = "Re-encoding would increase file size — original retained";
                    }
                    else
                    {
                        statusMessage = $"{outputFormatName} was already optimized — no smaller lossless representation found";
                    }
                }

                // 6. Output metadata (dimensions, aspect ratio strictly unchanged)
                var outputMetadata = new ImageMetadata
                {
                    Width = originalMetadata.Width,
                    Height = originalMetadata.Height,
                    AspectRatio = originalMetadata.AspectRatio,
                    AspectRatioFormatted = originalMetadata.AspectRatioFormatted,
                    FileSizeBytes = processedBytes.Length,
                    Format = outputFormatName,
                    MimeType = outputMimeType
                };

                // 7. Calculate first-class size metrics from actual returned bytes
                long compressedSize = processedBytes.Length;
                long savedBytes = Math.Max(0, originalSize - compressedSize);
                double reductionPercentage = originalSize > 0
                    ? Math.Round(((double)(originalSize - compressedSize) / originalSize) * 100.0, 2)
                    : 0.0;

                string suggestedFileName = GenerateSafeOutputFileName(fileName, outputExtension);

                return new CompressResult
                {
                    Success = true,
                    ErrorMessage = null,
                    ProcessedBytes = processedBytes,
                    MimeType = outputMimeType,
                    SuggestedFileName = suggestedFileName,
                    OriginalMetadata = originalMetadata,
                    OutputMetadata = outputMetadata,
                    OriginalFileSizeBytes = originalSize,
                    CompressedFileSizeBytes = compressedSize,
                    SavedBytes = savedBytes,
                    ReductionPercentage = reductionPercentage,
                    IsLargerThanOriginal = false,
                    IsAlreadyOptimized = isAlreadyOptimized,
                    StatusMessage = statusMessage
                };
            }
        }
        catch (ImageValidationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred while compressing image {FileName}", fileName);
            throw new InvalidOperationException("An error occurred while compressing the image.", ex);
        }
        finally
        {
            bufferStream?.Dispose();
        }
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM0",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT0"
    };

    /// <summary>
    /// Generates a sanitized, safe filename for the compressed output file without path traversal risks.
    /// Format: {originalBaseName}_compressed{cleanExt}
    /// </summary>
    private static string GenerateSafeOutputFileName(string rawFileName, string outputExtension)
    {
        string baseName = Path.GetFileNameWithoutExtension(rawFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "image";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedChars = baseName
            .Where(c => !invalidChars.Contains(c) && c != '/' && c != '\\' && c != ':' && c != '\0' && !char.IsControl(c))
            .ToArray();

        string sanitized = new string(sanitizedChars).Trim('.', ' ', '-');
        if (string.IsNullOrWhiteSpace(sanitized) || ReservedDeviceNames.Contains(sanitized))
        {
            sanitized = "image";
        }

        if (sanitized.Length > 64)
        {
            sanitized = sanitized.Substring(0, 64).TrimEnd('.', ' ', '-');
        }

        string cleanExt = outputExtension.StartsWith('.') ? outputExtension.ToLowerInvariant() : "." + outputExtension.ToLowerInvariant();
        return $"{sanitized}_compressed{cleanExt}";
    }

    /// <summary>
    /// Formats a byte count into human-readable string representation (B, KB, MB).
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024 * 1024)
        {
            return $"{(bytes / 1024.0):F1} KB";
        }
        return $"{(bytes / (1024.0 * 1024.0)):F2} MB";
    }
}
