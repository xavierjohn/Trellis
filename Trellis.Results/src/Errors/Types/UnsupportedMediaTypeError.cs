namespace Trellis;

/// <summary>
/// Represents an unsupported media type error when the server refuses a request
/// because the content type is not supported for the target resource.
/// Maps to HTTP 415 Unsupported Media Type.
/// </summary>
/// <remarks>
/// <para>
/// Per RFC 9110 §15.5.16, this error indicates the content's media type or content encoding
/// is not supported. When the issue is an unsupported content coding, the response ought
/// to include an <c>Accept-Encoding</c> header listing the supported codings.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Error.UnsupportedMediaType("application/xml is not supported. Use application/json.")
/// </code>
/// </example>
public sealed class UnsupportedMediaTypeError : Error
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsupportedMediaTypeError"/> class.
    /// </summary>
    /// <param name="detail">Description of why the media type is not supported.</param>
    /// <param name="code">The error code identifying this type of error.</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    public UnsupportedMediaTypeError(string detail, string code, string? instance = null) : base(detail, code, instance)
    {
    }
}
