namespace PixelForge.Exceptions;

/// <summary>
/// Exception thrown when an uploaded image fails security, format, or dimension validation.
/// Contains safe, user-facing error messages without exposing internal stack traces.
/// </summary>
public class ImageValidationException : Exception
{
    public ImageValidationException(string message) : base(message)
    {
    }

    public ImageValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

