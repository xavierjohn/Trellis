namespace Trellis;

/// <summary>
/// Represents a precondition failure per RFC 9110 §13.1.1.
/// The server will not perform the request because a condition in the request headers
/// (e.g., <c>If-Match</c>, <c>If-None-Match</c>) evaluated to false.
/// Maps to HTTP 412 Precondition Failed.
/// </summary>
/// <remarks>
/// <para>
/// Common scenarios include:
/// - <c>If-Match</c> ETag mismatch — the resource has been modified since the client last retrieved it
/// - <c>If-None-Match</c> failure on non-safe methods — the resource already exists
/// - Optimistic concurrency failure when the client provides a stale ETag
/// </para>
/// <para>
/// This is distinct from <see cref="ConflictError"/> (409), which indicates a state-based conflict
/// without a client-supplied precondition (e.g., duplicate key, FK violation).
/// <c>PreconditionFailedError</c> specifically signals that a conditional header was present and
/// the condition was not met.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Error.PreconditionFailed("Resource has been modified. Please reload and retry.")
/// Error.PreconditionFailed("ETag mismatch — expected \"abc123\" but resource has \"def456\"")
/// </code>
/// </example>
public sealed class PreconditionFailedError : Error
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PreconditionFailedError"/> class.
    /// </summary>
    /// <param name="detail">Description of the precondition failure.</param>
    /// <param name="code">The error code identifying this type of precondition error.</param>
    /// <param name="instance">Optional identifier for the affected resource.</param>
    public PreconditionFailedError(string detail, string code, string? instance = null) : base(detail, code, instance)
    {
    }
}
