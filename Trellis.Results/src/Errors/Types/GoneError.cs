namespace Trellis;

/// <summary>
/// Represents a permanent removal error when a resource is no longer available
/// and no forwarding address is known. This condition is expected to be permanent.
/// Use this instead of <see cref="NotFoundError"/> when the server knows the resource
/// previously existed and has been intentionally removed.
/// Maps to HTTP 410 Gone.
/// </summary>
/// <remarks>
/// <para>
/// Use this for intentional permanent removal scenarios:
/// - Soft-deleted resources that should not be re-fetched
/// - Deprecated API endpoints that have been retired
/// - Content that has been taken down permanently
/// </para>
/// <para>
/// If you don't know whether the condition is permanent, use <see cref="NotFoundError"/> instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Error.Gone("This product has been permanently discontinued")
/// Error.Gone("API endpoint /v1/users has been retired. Use /v2/users instead")
/// </code>
/// </example>
public sealed class GoneError : Error
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GoneError"/> class.
    /// </summary>
    /// <param name="detail">Description of why the resource is gone.</param>
    /// <param name="code">The error code identifying this type of gone error.</param>
    /// <param name="instance">Optional identifier for the removed resource.</param>
    public GoneError(string detail, string code, string? instance = null) : base(detail, code, instance)
    {
    }
}