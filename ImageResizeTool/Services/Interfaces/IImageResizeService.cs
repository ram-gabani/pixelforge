using PixelForge.Models;

namespace PixelForge.Services.Interfaces;

/// <summary>
/// Service responsible for validating, inspecting, and resizing images.
/// </summary>
public interface IImageResizeService
{
    /// <summary>
    /// Safely inspects an image stream and extracts metadata without decoding full raster bitmaps.
    /// </summary>
    /// <param name="imageStream">Input stream containing image bytes.</param>
    /// <param name="fileName">Original file name (for preliminary extension checking).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extracted image metadata.</returns>
    Task<ImageMetadata> InspectImageAsync(
        Stream imageStream,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resizes an image according to the specified request parameters.
    /// </summary>
    /// <param name="imageStream">Input stream containing image bytes.</param>
    /// <param name="fileName">Original file name.</param>
    /// <param name="request">Resize parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resize result containing output bytes and metadata.</returns>
    Task<ResizeResult> ResizeImageAsync(
        Stream imageStream,
        string fileName,
        ResizeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates output target dimensions according to resize mode and aspect ratio rules.
    /// </summary>
    /// <param name="originalWidth">Source image width in pixels.</param>
    /// <param name="originalHeight">Source image height in pixels.</param>
    /// <param name="request">Resize parameters.</param>
    /// <returns>Target width and height in pixels.</returns>
    (int TargetWidth, int TargetHeight) CalculateDimensions(
        int originalWidth,
        int originalHeight,
        ResizeRequest request);

    /// <summary>
    /// Determines whether the stream contains a supported image binary signature.
    /// </summary>
    /// <param name="stream">Stream to inspect.</param>
    /// <param name="detectedFormat">Canonical name of the detected format (e.g. "JPEG", "PNG").</param>
    /// <param name="detectedMimeType">MIME type of the detected format (e.g. "image/jpeg").</param>
    /// <returns>True if supported; otherwise false.</returns>
    bool IsSupportedFormat(
        Stream stream,
        out string detectedFormat,
        out string detectedMimeType);
}

