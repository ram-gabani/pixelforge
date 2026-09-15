namespace PixelForge.Models;

/// <summary>
/// Result of an image compression operation.
/// </summary>
public class CompressResult
{
    /// <summary>
    /// Indicates whether the compression operation succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// User-friendly error message if the compression operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Binary data of the processed image in memory.
    /// </summary>
    public byte[]? ProcessedBytes { get; set; }

    /// <summary>
    /// MIME type of the processed image (e.g. "image/jpeg", "image/png", "image/webp").
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>
    /// Suggested download file name (e.g. "sample_compressed.jpg").
    /// </summary>
    public string? SuggestedFileName { get; set; }

    /// <summary>
    /// Metadata of the original image prior to compression.
    /// </summary>
    public ImageMetadata? OriginalMetadata { get; set; }

    /// <summary>
    /// Metadata of the output image after compression.
    /// </summary>
    public ImageMetadata? OutputMetadata { get; set; }

    /// <summary>
    /// Size of the original image file in bytes.
    /// </summary>
    public long OriginalFileSizeBytes { get; set; }

    /// <summary>
    /// Size of the compressed image file in bytes.
    /// </summary>
    public long CompressedFileSizeBytes { get; set; }

    /// <summary>
    /// Amount of bytes saved by compression. Never negative (clamped to 0 minimum).
    /// </summary>
    public long SavedBytes { get; set; }

    /// <summary>
    /// Percentage reduction in file size: ((original - compressed) / original) * 100.
    /// May be negative if compressed file is larger than original.
    /// </summary>
    public double ReductionPercentage { get; set; }

    /// <summary>
    /// Indicates whether the compressed output file is larger than the original file.
    /// </summary>
    public bool IsLargerThanOriginal { get; set; }

    /// <summary>
    /// Indicates whether the original image was already optimal and retained without modification.
    /// </summary>
    public bool IsAlreadyOptimized { get; set; }

    /// <summary>
    /// Descriptive user-facing status message explaining the compression outcome.
    /// </summary>
    public string? StatusMessage { get; set; }
}

