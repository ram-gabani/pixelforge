using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PixelForge.Models;

/// <summary>
/// User request specifying parameters for resizing an image.
/// </summary>
public class ResizeRequest
{
    /// <summary>
    /// Uploaded image file from multipart form data.
    /// </summary>
    public IFormFile? File { get; set; }

    /// <summary>
    /// Selected resize mode (Percentage or CustomDimensions).
    /// </summary>
    public ResizeMode Mode { get; set; } = ResizeMode.Percentage;

    /// <summary>
    /// Scaling percentage for Percentage mode (e.g. 50 = 50%, 200 = 200%).
    /// Valid range: 1.0 to 500.0.
    /// </summary>
    [Range(1.0, 500.0, ErrorMessage = "Percentage must be between 1.0 and 500.0.")]
    public double? Percentage { get; set; }

    /// <summary>
    /// Explicit target width in pixels for CustomDimensions mode.
    /// </summary>
    [Range(1, 10000, ErrorMessage = "Target width must be between 1 and 10,000.")]
    public int? TargetWidth { get; set; }

    /// <summary>
    /// Explicit target height in pixels for CustomDimensions mode.
    /// </summary>
    [Range(1, 10000, ErrorMessage = "Target height must be between 1 and 10,000.")]
    public int? TargetHeight { get; set; }

    /// <summary>
    /// Indicates whether the original aspect ratio must be maintained.
    /// Default is true.
    /// </summary>
    public bool PreserveAspectRatio { get; set; } = true;

    /// <summary>
    /// Output encoding quality (1 to 100). Default is 85.
    /// </summary>
    [Range(1, 100, ErrorMessage = "Quality must be between 1 and 100.")]
    public int Quality { get; set; } = 85;

    /// <summary>
    /// Optional target output format (e.g. "jpg", "jpeg", "png", "webp", "bmp", "gif").
    /// If null or empty, the source image format is preserved.
    /// </summary>
    public string? TargetFormat { get; set; }
}

