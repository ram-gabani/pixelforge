using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PixelForge.Configuration;
using PixelForge.Exceptions;
using PixelForge.Models;
using PixelForge.Services.Interfaces;
using SkiaSharp;

namespace PixelForge.Services;

/// <summary>
/// Service responsible for validating, inspecting, and resizing images using SkiaSharp.
/// </summary>
public class ImageResizeService : IImageResizeService
{
    private readonly ImageProcessingOptions _options;
    private readonly ILogger<ImageResizeService> _logger;

    public ImageResizeService(
        IOptions<ImageProcessingOptions> options,
        ILogger<ImageResizeService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ImageMetadata> InspectImageAsync(
        Stream imageStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (imageStream == null)
        {
            _logger.LogWarning("Validation failed: input stream is null for file {FileName}", fileName);
            throw new ImageValidationException("The uploaded file is empty.");
        }

        // Quick check on seekable empty stream
        if (imageStream.CanSeek && imageStream.Length == 0)
        {
            _logger.LogWarning("Validation failed: input stream has 0 bytes for file {FileName}", fileName);
            throw new ImageValidationException("The uploaded file is empty.");
        }

        // Quick check on seekable oversized stream
        if (imageStream.CanSeek && imageStream.Length > _options.MaxFileSizeBytes)
        {
            _logger.LogWarning("Validation failed: stream length {Length} exceeds max allowed {Max} bytes for {FileName}",
                imageStream.Length, _options.MaxFileSizeBytes, fileName);
            throw new ImageValidationException("The uploaded file exceeds the maximum allowed size.");
        }

        // 1. Extension validation (preliminary check)
        string extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension) ||
            !_options.AllowedExtensions.Any(ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning("Validation failed: file extension '{Extension}' is not allowed for {FileName}", extension, fileName);
            throw new ImageValidationException("The uploaded file extension is not supported.");
        }

        // Handle non-seekable streams safely by buffering into memory up to MaxFileSizeBytes
        MemoryStream? memoryStream = null;
        Stream effectiveStream = imageStream;
        long initialPosition = imageStream.CanSeek ? imageStream.Position : 0;
        long fileSizeBytes;

        try
        {
            if (!imageStream.CanSeek)
            {
                memoryStream = new MemoryStream();
                byte[] buffer = new byte[81920];
                long totalBytes = 0;
                int read;

                while ((read = await imageStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    totalBytes += read;
                    if (totalBytes > _options.MaxFileSizeBytes)
                    {
                        _logger.LogWarning("Validation failed: non-seekable stream exceeded max size {Max} bytes for {FileName}",
                            _options.MaxFileSizeBytes, fileName);
                        throw new ImageValidationException("The uploaded file exceeds the maximum allowed size.");
                    }
                    await memoryStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                if (totalBytes == 0)
                {
                    _logger.LogWarning("Validation failed: non-seekable stream is empty for {FileName}", fileName);
                    throw new ImageValidationException("The uploaded file is empty.");
                }

                memoryStream.Position = 0;
                effectiveStream = memoryStream;
                fileSizeBytes = totalBytes;
            }
            else
            {
                fileSizeBytes = imageStream.Length;
            }

            // 2. Binary signature (magic bytes) validation
            if (!IsSupportedFormat(effectiveStream, out string detectedFormat, out string detectedMimeType))
            {
                _logger.LogWarning("Validation failed: binary signature mismatch or unsupported format for {FileName}", fileName);
                throw new ImageValidationException("The file contents do not match a supported image format.");
            }

            // 3. SkiaSharp codec inspection (lightweight metadata extraction without allocating SKBitmap)
            effectiveStream.Position = 0;
            using var managedStream = new SKManagedStream(effectiveStream, false);
            using var codec = SKCodec.Create(managedStream);

            if (codec == null)
            {
                _logger.LogWarning("Validation failed: SKCodec.Create failed to decode stream for {FileName}", fileName);
                throw new ImageValidationException("The image could not be decoded.");
            }

            int width = codec.Info.Width;
            int height = codec.Info.Height;

            if (width <= 0 || height <= 0)
            {
                _logger.LogWarning("Validation failed: invalid dimensions {Width}x{Height} for {FileName}", width, height, fileName);
                throw new ImageValidationException("The image could not be decoded.");
            }

            // 4. Dimension & Decompression Bomb limits
            if (width > _options.MaxInputWidth || height > _options.MaxInputHeight)
            {
                _logger.LogWarning("Validation failed: dimensions {Width}x{Height} exceed maximum allowed {MaxWidth}x{MaxHeight} for {FileName}",
                    width, height, _options.MaxInputWidth, _options.MaxInputHeight, fileName);
                throw new ImageValidationException("The image dimensions exceed the maximum allowed size.");
            }

            long totalPixels = (long)width * (long)height;
            if (totalPixels > _options.MaxInputPixels)
            {
                _logger.LogWarning("Validation failed: total pixel count {Pixels} exceeds maximum allowed {MaxPixels} for {FileName}",
                    totalPixels, _options.MaxInputPixels, fileName);
                throw new ImageValidationException("The image contains too many pixels to be safely processed.");
            }

            // 5. Calculate aspect ratio
            var (aspectRatio, aspectRatioFormatted) = CalculateAspectRatio(width, height);

            return new ImageMetadata
            {
                Width = width,
                Height = height,
                AspectRatio = aspectRatio,
                AspectRatioFormatted = aspectRatioFormatted,
                FileSizeBytes = fileSizeBytes,
                Format = detectedFormat,
                MimeType = detectedMimeType
            };
        }
        finally
        {
            // Restore stream position for seekable input streams so callers can re-read
            if (imageStream.CanSeek)
            {
                imageStream.Position = initialPosition;
            }

            memoryStream?.Dispose();
        }
    }

    /// <inheritdoc />
    public bool IsSupportedFormat(
        Stream stream,
        out string detectedFormat,
        out string detectedMimeType)
    {
        detectedFormat = string.Empty;
        detectedMimeType = string.Empty;

        if (stream == null || !stream.CanRead)
        {
            return false;
        }

        long initialPosition = stream.CanSeek ? stream.Position : -1;

        try
        {
            byte[] header = new byte[16];
            int bytesRead = 0;

            while (bytesRead < header.Length)
            {
                int read = stream.Read(header, bytesRead, header.Length - bytesRead);
                if (read == 0) break;
                bytesRead += read;
            }

            if (bytesRead < 2)
            {
                return false;
            }

            // JPEG: FF D8 FF
            if (bytesRead >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                detectedFormat = "JPEG";
                detectedMimeType = "image/jpeg";
                return true;
            }

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (bytesRead >= 8 &&
                header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            {
                detectedFormat = "PNG";
                detectedMimeType = "image/png";
                return true;
            }

            // WebP: RIFF....WEBP (Bytes 0..3 = RIFF, Bytes 8..11 = WEBP)
            if (bytesRead >= 12 &&
                header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            {
                detectedFormat = "WEBP";
                detectedMimeType = "image/webp";
                return true;
            }

            // GIF: GIF87a or GIF89a (47 49 46 38 37/39 61)
            if (bytesRead >= 6 &&
                header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38 &&
                (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
            {
                detectedFormat = "GIF";
                detectedMimeType = "image/gif";
                return true;
            }

            // BMP: BM (42 4D)
            if (header[0] == 0x42 && header[1] == 0x4D)
            {
                detectedFormat = "BMP";
                detectedMimeType = "image/bmp";
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error occurred during binary signature inspection");
            return false;
        }
        finally
        {
            if (stream.CanSeek && initialPosition >= 0)
            {
                stream.Position = initialPosition;
            }
        }
    }

    /// <inheritdoc />
    public async Task<ResizeResult> ResizeImageAsync(
        Stream imageStream,
        string fileName,
        ResizeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        // 1. Inspect and validate source image (Phase 3 logic)
        ImageMetadata originalMetadata = await InspectImageAsync(imageStream, fileName, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // 2. Calculate target dimensions
        var (targetWidth, targetHeight) = CalculateDimensions(originalMetadata.Width, originalMetadata.Height, request);

        // 3. Enforce maximum output dimensions and pixel limits with integer overflow defense
        if (targetWidth <= 0 || targetHeight <= 0 || targetWidth > _options.MaxOutputWidth || targetHeight > _options.MaxOutputHeight)
        {
            _logger.LogWarning("Requested target dimensions {Width}x{Height} exceed maximum output limits of {MaxWidth}x{MaxHeight} for {FileName}",
                targetWidth, targetHeight, _options.MaxOutputWidth, _options.MaxOutputHeight, fileName);
            throw new ImageValidationException("The requested output dimensions exceed the maximum allowed size.");
        }

        long outputPixels = checked((long)targetWidth * (long)targetHeight);
        if (outputPixels <= 0 || outputPixels > _options.MaxInputPixels)
        {
            _logger.LogWarning("Requested output pixel count {Pixels} exceeds maximum allowed limit of {MaxPixels} for {FileName}",
                outputPixels, _options.MaxInputPixels, fileName);
            throw new ImageValidationException("The requested output dimensions exceed the maximum allowed size.");
        }

        // 4. Determine output format and MIME type
        var (outputFormat, outputExtension, outputMimeType) = DetermineOutputFormat(request.TargetFormat, originalMetadata.Format);

        if (outputFormat == SKEncodedImageFormat.Gif)
        {
            _logger.LogWarning("GIF encoding requested for {FileName}, but GIF encoding is not supported in current configuration", fileName);
            throw new ImageValidationException("GIF output is not supported in the current configuration.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 5. Read source image stream safely
        MemoryStream? bufferStream = null;
        Stream effectiveStream = imageStream;

        try
        {
            if (!imageStream.CanSeek)
            {
                bufferStream = new MemoryStream();
                await imageStream.CopyToAsync(bufferStream, cancellationToken);
                bufferStream.Position = 0;
                effectiveStream = bufferStream;
            }
            else
            {
                effectiveStream.Position = 0;
            }

            // Decode source image using Skia codec
            using var managedStream = new SKManagedStream(effectiveStream, false);
            using var codec = SKCodec.Create(managedStream);
            if (codec == null)
            {
                _logger.LogWarning("SKCodec could not parse stream during resize of {FileName}", fileName);
                throw new ImageValidationException("The image could not be decoded.");
            }

            using var sourceBitmap = SKBitmap.Decode(codec);
            if (sourceBitmap == null)
            {
                _logger.LogWarning("SKBitmap.Decode returned null for {FileName}", fileName);
                throw new ImageValidationException("The image could not be decoded.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 6. High-quality resampling using Lanczos / Bicubic filtering
            // Target image info: preserve source color type and alpha type
            SKColorType colorType = sourceBitmap.ColorType;
            SKAlphaType alphaType = sourceBitmap.AlphaType;

            // Transparency handling:
            // When converting to JPEG (which lacks an alpha channel), composite over white background
            bool flattenToWhite = (outputFormat == SKEncodedImageFormat.Jpeg && alphaType != SKAlphaType.Opaque);

            var targetInfo = new SKImageInfo(
                targetWidth,
                targetHeight,
                colorType,
                flattenToWhite ? SKAlphaType.Opaque : alphaType);

            using var resizedSurface = SKSurface.Create(targetInfo);
            if (resizedSurface == null)
            {
                _logger.LogError("Failed to create SKSurface with dimensions {Width}x{Height} for {FileName}", targetWidth, targetHeight, fileName);
                throw new InvalidOperationException("Failed to allocate image surface for resizing.");
            }

            var canvas = resizedSurface.Canvas;

            if (flattenToWhite)
            {
                // Solid white background for transparent images converted to JPEG
                canvas.Clear(SKColors.White);
            }
            else
            {
                // Transparent background for PNG / WebP / BMP
                canvas.Clear(SKColors.Transparent);
            }

            // High-quality cubic resampling (Catmull-Rom / Mitchell high fidelity)
            var samplingOptions = new SKSamplingOptions(SKCubicResampler.CatmullRom);

            using var sourceImage = SKImage.FromBitmap(sourceBitmap);
            var destRect = new SKRect(0, 0, targetWidth, targetHeight);
            var srcRect = new SKRect(0, 0, sourceBitmap.Width, sourceBitmap.Height);

            canvas.DrawImage(sourceImage, srcRect, destRect, samplingOptions);
            canvas.Flush();

            cancellationToken.ThrowIfCancellationRequested();

            // 7. Encode resized output image
            using var resizedImage = resizedSurface.Snapshot();

            int quality = request.Quality;
            if (outputFormat == SKEncodedImageFormat.Jpeg)
            {
                quality = request.Quality is >= 1 and <= 100 ? request.Quality : _options.DefaultJpegQuality;
            }
            else if (outputFormat == SKEncodedImageFormat.Webp)
            {
                quality = request.Quality is >= 1 and <= 100 ? request.Quality : _options.DefaultWebpQuality;
            }
            else
            {
                quality = 100; // Lossless for PNG / BMP
            }

            using var encodedData = resizedImage.Encode(outputFormat, quality);
            if (encodedData == null)
            {
                _logger.LogError("Failed to encode resized image into {Format} for {FileName}", outputFormat, fileName);
                throw new ImageValidationException($"Failed to encode image to {outputFormat}.");
            }

            // Copy encoded bytes to managed array so they remain valid after unmanaged Skia disposal
            byte[] processedBytes = encodedData.ToArray();

            // 8. Build output metadata and suggested filename
            var (outputRatio, outputRatioFormatted) = CalculateAspectRatio(targetWidth, targetHeight);

            var outputMetadata = new ImageMetadata
            {
                Width = targetWidth,
                Height = targetHeight,
                AspectRatio = outputRatio,
                AspectRatioFormatted = outputRatioFormatted,
                FileSizeBytes = processedBytes.Length,
                Format = outputFormat.ToString().ToUpperInvariant(),
                MimeType = outputMimeType
            };

            string suggestedFileName = GenerateSafeOutputFileName(fileName, targetWidth, targetHeight, outputExtension);

            return new ResizeResult
            {
                Success = true,
                ErrorMessage = null,
                ProcessedBytes = processedBytes,
                MimeType = outputMimeType,
                SuggestedFileName = suggestedFileName,
                OriginalMetadata = originalMetadata,
                OutputMetadata = outputMetadata
            };
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
            _logger.LogError(ex, "Unexpected error occurred while resizing image {FileName}", fileName);
            throw new InvalidOperationException("An error occurred while processing the image.", ex);
        }
        finally
        {
            bufferStream?.Dispose();
        }
    }

    /// <inheritdoc />
    public (int TargetWidth, int TargetHeight) CalculateDimensions(
        int originalWidth,
        int originalHeight,
        ResizeRequest request)
    {
        if (originalWidth <= 0 || originalHeight <= 0)
        {
            throw new ImageValidationException("Invalid original image dimensions.");
        }

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (request.Mode == ResizeMode.Percentage)
        {
            if (!request.Percentage.HasValue ||
                double.IsNaN(request.Percentage.Value) ||
                double.IsInfinity(request.Percentage.Value) ||
                request.Percentage.Value < 1.0 ||
                request.Percentage.Value > 500.0)
            {
                throw new ImageValidationException("The requested resize percentage is invalid. It must be between 1.0 and 500.0.");
            }

            double factor = request.Percentage.Value / 100.0;
            int targetWidth = (int)Math.Max(1, Math.Round(originalWidth * factor, MidpointRounding.AwayFromZero));
            int targetHeight = (int)Math.Max(1, Math.Round(originalHeight * factor, MidpointRounding.AwayFromZero));

            return (targetWidth, targetHeight);
        }
        else if (request.Mode == ResizeMode.CustomDimensions)
        {
            if (!request.TargetWidth.HasValue && !request.TargetHeight.HasValue)
            {
                throw new ImageValidationException("At least one dimension (width or height) must be specified for custom resizing.");
            }

            if (request.TargetWidth.HasValue && (request.TargetWidth.Value <= 0 || request.TargetWidth.Value > _options.MaxOutputWidth))
            {
                throw new ImageValidationException($"The requested dimensions are invalid. Width must be between 1 and {_options.MaxOutputWidth:N0}.");
            }

            if (request.TargetHeight.HasValue && (request.TargetHeight.Value <= 0 || request.TargetHeight.Value > _options.MaxOutputHeight))
            {
                throw new ImageValidationException($"The requested dimensions are invalid. Height must be between 1 and {_options.MaxOutputHeight:N0}.");
            }

            if (request.PreserveAspectRatio)
            {
                // Deterministic rule when aspect ratio is preserved:
                // If width is specified (or both are specified), width is the controlling dimension.
                // If only height is specified, height is the controlling dimension.
                if (request.TargetWidth.HasValue)
                {
                    int targetWidth = request.TargetWidth.Value;
                    double calcHeight = Math.Round((double)targetWidth * originalHeight / originalWidth, MidpointRounding.AwayFromZero);
                    if (double.IsNaN(calcHeight) || double.IsInfinity(calcHeight) || calcHeight < 1.0 || calcHeight > _options.MaxOutputHeight)
                    {
                        throw new ImageValidationException("The calculated proportional height exceeds the maximum allowed size.");
                    }
                    int targetHeight = (int)calcHeight;
                    return (targetWidth, targetHeight);
                }
                else
                {
                    int targetHeight = request.TargetHeight!.Value;
                    double calcWidth = Math.Round((double)targetHeight * originalWidth / originalHeight, MidpointRounding.AwayFromZero);
                    if (double.IsNaN(calcWidth) || double.IsInfinity(calcWidth) || calcWidth < 1.0 || calcWidth > _options.MaxOutputWidth)
                    {
                        throw new ImageValidationException("The calculated proportional width exceeds the maximum allowed size.");
                    }
                    int targetWidth = (int)calcWidth;
                    return (targetWidth, targetHeight);
                }
            }
            else
            {
                // Non-proportional: use provided dimensions; default to original if one dimension is missing
                int targetWidth = request.TargetWidth ?? originalWidth;
                int targetHeight = request.TargetHeight ?? originalHeight;
                return (targetWidth, targetHeight);
            }
        }
        else
        {
            throw new ImageValidationException("Invalid resize mode specified.");
        }
    }

    /// <summary>
    /// Maps a requested format name to an SKEncodedImageFormat, file extension, and MIME type.
    /// </summary>
    private static (SKEncodedImageFormat Format, string Extension, string MimeType) DetermineOutputFormat(
        string? requestedFormat,
        string sourceFormat)
    {
        string raw = !string.IsNullOrWhiteSpace(requestedFormat) ? requestedFormat : sourceFormat;
        string normalized = raw.Trim().ToLowerInvariant().TrimStart('.');

        return normalized switch
        {
            "jpg" or "jpeg" => (SKEncodedImageFormat.Jpeg, ".jpg", "image/jpeg"),
            "png" => (SKEncodedImageFormat.Png, ".png", "image/png"),
            "webp" => (SKEncodedImageFormat.Webp, ".webp", "image/webp"),
            "bmp" => (SKEncodedImageFormat.Bmp, ".bmp", "image/bmp"),
            "gif" => (SKEncodedImageFormat.Gif, ".gif", "image/gif"),
            _ => throw new ImageValidationException($"The requested output format '{requestedFormat}' is not supported.")
        };
    }

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM0",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT0"
    };

    /// <summary>
    /// Generates a sanitized, safe filename for the downloaded output file without path traversal risks.
    /// </summary>
    private static string GenerateSafeOutputFileName(
        string rawFileName,
        int targetWidth,
        int targetHeight,
        string outputExtension)
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
        return $"{sanitized}_resized_{targetWidth}x{targetHeight}{cleanExt}";
    }

    /// <summary>
    /// Calculates numeric aspect ratio and formatted ratio string (e.g. "16:9", "1:1", or "1.78:1").
    /// </summary>
    private static (double Ratio, string Formatted) CalculateAspectRatio(int width, int height)
    {
        if (height <= 0 || width <= 0)
        {
            return (0.0, "0:0");
        }

        double ratio = (double)width / height;

        // Reduce using greatest common divisor
        int gcd = GreatestCommonDivisor(width, height);
        int reducedWidth = width / gcd;
        int reducedHeight = height / gcd;

        // If reduced numbers are small exact integers (e.g. 16:9, 4:3, 1:1, 9:16, 3:2, 5:4)
        if (reducedWidth <= 25 && reducedHeight <= 25)
        {
            return (ratio, $"{reducedWidth}:{reducedHeight}");
        }

        // Check against common photographic / video standards with small epsilon
        var commonStandards = new (int W, int H, double Value)[]
        {
            (16, 9, 16.0 / 9.0),
            (9, 16, 9.0 / 16.0),
            (4, 3, 4.0 / 3.0),
            (3, 4, 3.0 / 4.0),
            (1, 1, 1.0),
            (3, 2, 3.0 / 2.0),
            (2, 3, 2.0 / 3.0),
            (5, 4, 5.0 / 4.0),
            (4, 5, 4.0 / 5.0),
            (21, 9, 21.0 / 9.0)
        };

        foreach (var (w, h, val) in commonStandards)
        {
            if (Math.Abs(ratio - val) < 0.005)
            {
                return (ratio, $"{w}:{h}");
            }
        }

        // Fallback to formatted decimal string
        string formattedDecimal = Math.Round(ratio, 2).ToString("0.##", CultureInfo.InvariantCulture) + ":1";
        return (ratio, formattedDecimal);
    }

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }
}

