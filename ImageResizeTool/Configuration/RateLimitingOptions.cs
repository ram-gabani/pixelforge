namespace PixelForge.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Maximum number of requests allowed within the sliding/fixed window per client IP. Default: 30 requests.
    /// </summary>
    public int PermitLimit { get; set; } = 30;

    /// <summary>
    /// Duration of the rate limit window in seconds. Default: 60 seconds (1 minute).
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of requests to queue when limit is reached. Default: 0 (reject immediately with 429).
    /// </summary>
    public int QueueLimit { get; set; } = 0;
}

