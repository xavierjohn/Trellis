namespace Trellis;

/// <summary>
/// Represents a rate limiting error when too many requests have been made.
/// Use this when a client has exceeded their request quota or rate limit.
/// Maps to HTTP 429 Too Many Requests.
/// </summary>
/// <remarks>
/// Include retry-after information in the detail message when appropriate.
/// Consider adding custom properties for RetryAfter seconds if needed for your API design.
/// </remarks>
/// <example>
/// <code>
/// Error.RateLimit("API rate limit exceeded. Please try again in 60 seconds")
/// Error.RateLimit("Daily quota of 1000 requests exceeded", userId)
/// Error.RateLimit("Too many login attempts. Account temporarily locked")
/// </code>
/// </example>
public sealed class RateLimitError : Error
{
    /// <summary>
    /// Gets the optional retry-after value indicating when the rate limit resets.
    /// When present, Trellis response mappers emit the <c>Retry-After</c> header.
    /// </summary>
    public RetryAfterValue? RetryAfter { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitError"/> class.
    /// </summary>
    /// <param name="detail">Description of the rate limit violation.</param>
    /// <param name="code">The error code identifying this type of rate limit error.</param>
    /// <param name="instance">Optional identifier for the client or resource being rate limited.</param>
    public RateLimitError(string detail, string code, string? instance = null) : base(detail, code, instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitError"/> class with retry-after metadata.
    /// </summary>
    /// <param name="detail">Description of the rate limit violation.</param>
    /// <param name="code">The error code identifying this type of rate limit error.</param>
    /// <param name="retryAfter">The retry-after value indicating when the client may retry.</param>
    /// <param name="instance">Optional identifier for the client or resource being rate limited.</param>
    public RateLimitError(string detail, string code, RetryAfterValue retryAfter, string? instance = null)
        : base(detail, code, instance) => RetryAfter = retryAfter;
}