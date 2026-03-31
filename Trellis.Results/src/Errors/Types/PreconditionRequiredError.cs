namespace Trellis;

/// <summary>
/// Represents a precondition required error per RFC 6585 §3.
/// The server requires the request to be conditional — the client must include
/// a precondition header (e.g., <c>If-Match</c>) to proceed.
/// Maps to HTTP 428 Precondition Required.
/// </summary>
/// <remarks>
/// <para>
/// This error is returned when a mutating operation (PUT, PATCH, DELETE) is attempted
/// without an <c>If-Match</c> header. Requiring conditional requests prevents the
/// "lost update" problem where a client overwrites changes made by another client.
/// </para>
/// <para>
/// This is distinct from <see cref="PreconditionFailedError"/> (412), which indicates
/// that a precondition WAS provided but evaluated to false.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Error.PreconditionRequired("This operation requires an If-Match header.")
/// </code>
/// </example>
public sealed class PreconditionRequiredError : Error
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PreconditionRequiredError"/> class.
    /// </summary>
    /// <param name="detail">Description of what precondition is required.</param>
    /// <param name="code">The error code identifying this type of error.</param>
    /// <param name="instance">Optional identifier for the affected resource.</param>
    public PreconditionRequiredError(string detail, string code, string? instance = null) : base(detail, code, instance)
    {
    }
}
