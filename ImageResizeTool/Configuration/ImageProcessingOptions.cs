namespace PixelForge.Configuration;

public class ImageProcessingOptions
{
    public const string SectionName = "ImageProcessing";

    /// <summary>
    /// Maximum allowed upload file size in bytes. Default: 20 MB (20,971,520 bytes).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Allowed file extensions for uploaded images.
    /// </summary>
    public string[] AllowedExtensions { get; set; } =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".bmp",
        ".gif"
    ];

    /// <summary>
    /// Allowed MIME types for uploaded images.
    /// </summary>
    public string[] AllowedMimeTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/bmp",
        "image/gif"
    ];

    /// <summary>
    /// Maximum allowable input image width in pixels.
    /// </summary>
    public int MaxInputWidth { get; set; } = 10000;

    /// <summary>
    /// Maximum allowable input image height in pixels.
    /// </summary>
    public int MaxInputHeight { get; set; } = 10000;

    /// <summary>
    /// Maximum allowable total pixels (width * height) to protect against decompression bombs.
    /// Default: 50,000,000 (50 MP).
    /// </summary>
    public long MaxInputPixels { get; set; } = 50_000_000;

    /// <summary>
    /// Maximum allowable output image width in pixels.
    /// </summary>
    public int MaxOutputWidth { get; set; } = 10000;

    /// <summary>
    /// Maximum allowable output image height in pixels.
    /// </summary>
    public int MaxOutputHeight { get; set; } = 10000;

    /// <summary>
    /// Minimum dimension allowed for width or height.
    /// </summary>
    public int MinDimension { get; set; } = 1;

    /// <summary>
    /// Default JPEG encoding quality (1 - 100).
    /// </summary>
    public int DefaultJpegQuality { get; set; } = 85;

    /// <summary>
    /// Default WebP encoding quality (1 - 100).
    /// </summary>
    public int DefaultWebpQuality { get; set; } = 85;

    /// <summary>
    /// Default quality for JPEG compression (1 - 100). Default: 75.
    /// </summary>
    public int DefaultJpegCompressQuality { get; set; } = 75;

    /// <summary>
    /// Default quality for WebP compression (1 - 100). Default: 75.
    /// </summary>
    public int DefaultWebpCompressQuality { get; set; } = 75;

    /// <summary>
    /// Default ZLib deflate compression effort for PNG (0 - 9). Default: 9.
    /// </summary>
    public int DefaultPngZLibLevel { get; set; } = 9;
}

