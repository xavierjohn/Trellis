namespace Trellis;

using System.Globalization;

/// <summary>
/// Represents an RFC 9110 §10.2.3 <c>Retry-After</c> value, which can be either
/// a delay in seconds or an absolute HTTP-date indicating when the client may retry.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Retry-After</c> header is used with:
/// <list type="bullet">
/// <item><c>429 Too Many Requests</c> — indicates when rate limit resets</item>
/// <item><c>503 Service Unavailable</c> — indicates when service may resume</item>
/// <item><c>413 Content Too Large</c> — indicates when temporary limit may change</item>
/// <item><c>3xx Redirections</c> — indicates minimum wait before following redirect</item>
/// </list>
/// </para>
/// <para>
/// Create instances using <see cref="FromSeconds"/> or <see cref="FromDate"/>.
/// Format for HTTP headers using <see cref="ToHeaderValue"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var delay = RetryAfterValue.FromSeconds(60);
/// var date = RetryAfterValue.FromDate(DateTimeOffset.UtcNow.AddMinutes(5));
/// 
/// // Use in error creation:
/// new Error.TooManyRequests(delay) { Detail = "Too many requests." }
/// new Error.ServiceUnavailable(date) { Detail = "Service is under maintenance." }
/// </code>
/// </example>
public sealed class RetryAfterValue : IEquatable<RetryAfterValue>
{
    private readonly int? _delaySeconds;
    private readonly DateTimeOffset? _date;

    private RetryAfterValue(int? delaySeconds, DateTimeOffset? date)
    {
        _delaySeconds = delaySeconds;
        _date = date;
    }

    /// <summary>
    /// Gets whether this value represents a delay in seconds (as opposed to an absolute date).
    /// </summary>
    public bool IsDelaySeconds => _delaySeconds.HasValue;

    /// <summary>
    /// Gets whether this value represents an absolute HTTP-date (as opposed to a delay).
    /// </summary>
    public bool IsDate => _date.HasValue;

    /// <summary>
    /// Gets the delay in seconds. Only valid when <see cref="IsDelaySeconds"/> is <c>true</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when this value represents a date, not a delay.</exception>
    public int DelaySeconds => _delaySeconds ?? throw new InvalidOperationException("This RetryAfterValue represents a date, not a delay.");

    /// <summary>
    /// Gets the absolute date. Only valid when <see cref="IsDate"/> is <c>true</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when this value represents a delay, not a date.</exception>
    public DateTimeOffset Date => _date ?? throw new InvalidOperationException("This RetryAfterValue represents a delay, not a date.");

    /// <summary>
    /// Creates a <see cref="RetryAfterValue"/> representing a delay in seconds.
    /// </summary>
    /// <param name="seconds">The number of seconds the client should wait before retrying. Must be non-negative.</param>
    /// <returns>A new <see cref="RetryAfterValue"/> instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="seconds"/> is negative.</exception>
    public static RetryAfterValue FromSeconds(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        return new RetryAfterValue(seconds, null);
    }

    /// <summary>
    /// Creates a <see cref="RetryAfterValue"/> representing an absolute HTTP-date.
    /// </summary>
    /// <param name="date">The date and time when the client may retry.</param>
    /// <returns>A new <see cref="RetryAfterValue"/> instance.</returns>
    public static RetryAfterValue FromDate(DateTimeOffset date) =>
        new(null, date);

    /// <summary>
    /// Formats this value for use as an HTTP <c>Retry-After</c> header value.
    /// Returns either a decimal number of seconds or an HTTP-date in IMF-fixdate format.
    /// </summary>
    /// <returns>The formatted header value string.</returns>
    public string ToHeaderValue() =>
        _delaySeconds.HasValue
            ? _delaySeconds.Value.ToString(CultureInfo.InvariantCulture)
            : _date!.Value.ToString("R", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override string ToString() => ToHeaderValue();

    /// <inheritdoc />
    public bool Equals(RetryAfterValue? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _delaySeconds == other._delaySeconds && _date == other._date;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RetryAfterValue other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_delaySeconds, _date);
}