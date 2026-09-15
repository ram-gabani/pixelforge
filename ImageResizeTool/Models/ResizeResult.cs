namespace PixelForge.Models;

/// <summary>
/// Result of an image resizing operation.
/// </summary>
public class ResizeResult
{
    /// <summary>
    /// Indicates whether the resize operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// User-friendly error message if the resize operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Binary data of the processed image in memory.
    /// </summary>
    public byte[]? ProcessedBytes { get; set; }

    /// <summary>
    /// MIME type of the processed image (e.g. "image/jpeg", "image/png").
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Suggested download file name (e.g. "sample_resized_800x600.png").
    /// </summary>
    public string? SuggestedFileName { get; set; }

    /// <summary>
    /// Metadata of the original image prior to resizing.
    /// </summary>
    public ImageMetadata? OriginalMetadata { get; set; }

    /// <summary>
    /// Metadata of the output image after resizing.
    /// </summary>
    public ImageMetadata? OutputMetadata { get; set; }

    /// <summary>
    /// Optional Base64 data URL representation for inline preview.
    /// </summary>
    public string? PreviewDataUrl { get; set; }
}

