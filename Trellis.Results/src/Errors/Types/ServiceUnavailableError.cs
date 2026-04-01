namespace Trellis;

/// <summary>
/// Represents a temporary service unavailability error.
/// Use this when the service is temporarily unable to handle the request due to maintenance or overload.
/// Maps to HTTP 503 Service Unavailable.
/// </summary>
/// <remarks>
/// This indicates a temporary condition - the service is expected to be available again.
/// Include retry-after information in the detail message when appropriate.
/// For permanent unavailability or deprecated endpoints, consider using other error types.
/// </remarks>
/// <example>
/// <code>
/// Error.ServiceUnavailable("Service is under maintenance. Please try again later")
/// Error.ServiceUnavailable("Database connection pool exhausted. Retry in 30 seconds")
/// Error.ServiceUnavailable("External payment gateway is temporarily unavailable")
/// </code>
/// </example>
public sealed class ServiceUnavailableError : Error
{
    /// <summary>
    /// Gets the optional retry-after value indicating when the service may resume.
    /// When present, Trellis response mappers emit the <c>Retry-After</c> header.
    /// </summary>
    public RetryAfterValue? RetryAfter { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceUnavailableError"/> class.
    /// </summary>
    /// <param name="detail">Description of why the service is unavailable.</param>
    /// <param name="code">The error code identifying this type of service unavailable error.</param>
    /// <param name="instance">Optional identifier for the unavailable service or resource.</param>
    public ServiceUnavailableError(string detail, string code, string? instance = null) : base(detail, code, instance)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceUnavailableError"/> class with retry-after metadata.
    /// </summary>
    /// <param name="detail">Description of why the service is unavailable.</param>
    /// <param name="code">The error code identifying this type of service unavailable error.</param>
    /// <param name="retryAfter">The retry-after value indicating when the service may resume.</param>
    /// <param name="instance">Optional identifier for the unavailable service or resource.</param>
    public ServiceUnavailableError(string detail, string code, RetryAfterValue retryAfter, string? instance = null)
        : base(detail, code, instance) => RetryAfter = retryAfter;
}