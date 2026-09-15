using PixelForge.Models;

namespace PixelForge.Services.Interfaces;

/// <summary>
/// Service responsible for validating and compressing images without altering dimensions.
/// </summary>
public interface IImageCompressionService
{
    /// <summary>
    /// Compresses an image attempting to reduce encoded file size while strictly preserving original dimensions and format.
    /// </summary>
    /// <param name="imageStream">Input stream containing image bytes.</param>
    /// <param name="fileName">Original file name.</param>
    /// <param name="request">Compression parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compression result containing output bytes, dimensions, and size reduction metrics.</returns>
    Task<CompressResult> CompressImageAsync(
        Stream imageStream,
        string fileName,
        CompressRequest request,
        CancellationToken cancellationToken = default);
}

