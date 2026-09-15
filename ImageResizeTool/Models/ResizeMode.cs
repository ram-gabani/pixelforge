namespace PixelForge.Models;

/// <summary>
/// Mode specifying how the image dimensions should be adjusted.
/// </summary>
public enum ResizeMode
{
    /// <summary>
    /// Resize proportionally based on a percentage scale factor.
    /// </summary>
    Percentage,

    /// <summary>
    /// Resize using explicitly specified width and/or height dimensions.
    /// </summary>
    CustomDimensions
}

