namespace Trellis;

/// <summary>
/// Represents a content too large error when the server refuses a request because
/// the content is larger than the server is willing or able to process.
/// Maps to HTTP 413 Content Too Large (formerly Request Entity Too Large).
/// </summary>
/// <remarks>
/// <para>
/// Per RFC 9110 §15.5.14, if the condition is temporary, the server SHOULD generate
/// a <c>Retry-After</c> header to indicate when the client may try again.
/// Use the <see cref="RetryAfter"/> property to carry this metadata.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Error.ContentTooLarge("Request body exceeds the 10 MB limit.")
/// Error.ContentTooLarge("Request body exceeds the limit. Try again later.", RetryAfterValue.FromSeconds(60))
/// </code>
/// </example>
public sealed class ContentTooLargeError : Error
{
    /// <summary>
    /// Gets the optional retry-after value indicating when the client may retry.
    /// When present, Trellis response mappers emit the <c>Retry-After</c> header.
    /// </summary>
    public RetryAfterValue? RetryAfter { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentTooLargeError"/> class.
    /// </summary>
    /// <param name="detail">Description of why the content is too large.</param>
    /// <param name="code">The error code identifying this type of error.</param>
    /// <param name="retryAfter">Optional retry-after value for temporary conditions.</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    public ContentTooLargeError(string detail, string code, RetryAfterValue? retryAfter = null, string? instance = null)
        : base(detail, code, instance) => RetryAfter = retryAfter;
}
