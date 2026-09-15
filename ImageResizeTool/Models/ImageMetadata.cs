namespace PixelForge.Models;

/// <summary>
/// Technical metadata describing an image.
/// </summary>
public class ImageMetadata
{
    /// <summary>
    /// Width of the image in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Height of the image in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Aspect ratio of the image (Width / Height).
    /// </summary>
    public double AspectRatio { get; set; }

    /// <summary>
    /// Formatted aspect ratio string for display (e.g. "16:9", "4:3", "1:1", or decimal ratio).
    /// </summary>
    public string AspectRatioFormatted { get; set; } = string.Empty;

    /// <summary>
    /// Size of the image file in bytes.
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// Image format name (e.g. "JPEG", "PNG", "WEBP", "GIF", "BMP").
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// MIME type of the image (e.g. "image/jpeg", "image/png").
    /// </summary>
    public string MimeType { get; set; } = string.Empty;
}

