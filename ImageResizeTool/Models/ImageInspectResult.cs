namespace PixelForge.Models;

/// <summary>
/// Result of inspecting an uploaded image.
/// </summary>
public class ImageInspectResult
{
    /// <summary>
    /// Indicates whether the inspection was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// User-facing error message if the inspection failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Technical metadata of the inspected image if inspection was successful.
    /// </summary>
    public ImageMetadata? Metadata { get; set; }

    /// <summary>
    /// Optional Base64 data URL for client-side preview.
    /// </summary>
    public string? PreviewDataUrl { get; set; }
}

