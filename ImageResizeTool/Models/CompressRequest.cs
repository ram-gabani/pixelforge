using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PixelForge.Models;

/// <summary>
/// User request specifying parameters for compressing an image without altering dimensions.
/// </summary>
public class CompressRequest
{
    /// <summary>
    /// Uploaded image file from multipart form data.
    /// </summary>
    [Required(ErrorMessage = "An image file is required.")]
    public IFormFile? File { get; set; }

    /// <summary>
    /// Lossy compression quality for JPEG and WebP formats (1 to 100). Default is 75.
    /// </summary>
    [Range(1, 100, ErrorMessage = "Quality must be between 1 and 100.")]
    public int Quality { get; set; } = 75;

    /// <summary>
    /// Deflate compression effort level for PNG format (0 to 9). Default is 9 (maximum effort).
    /// </summary>
    [Range(0, 9, ErrorMessage = "PNG compression level must be between 0 and 9.")]
    public int PngCompressionLevel { get; set; } = 9;
}

