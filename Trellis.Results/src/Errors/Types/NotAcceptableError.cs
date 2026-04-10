namespace Trellis;

/// <summary>
/// Represents a not acceptable error when the server cannot produce a representation
/// that matches the client's stated preferences (e.g., <c>Accept</c>, <c>Accept-Language</c>).
/// Maps to HTTP 406 Not Acceptable.
/// </summary>
/// <remarks>
/// <para>
/// Per RFC 9110 §15.5.7, a 406 response SHOULD generate content containing a list
/// of available representation characteristics so the user or user agent can choose.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Error.NotAcceptable("No representation available for the requested media type.")
/// </code>
/// </example>
public sealed class NotAcceptableError : Error
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotAcceptableError"/> class.
    /// </summary>
    /// <param name="detail">Description of why no acceptable representation is available.</param>
    /// <param name="code">The error code identifying this type of error.</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    public NotAcceptableError(string detail, string code, string? instance = null) : base(detail, code, instance)
    {
    }
}