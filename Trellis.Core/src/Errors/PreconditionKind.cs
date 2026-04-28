namespace Trellis;

/// <summary>
/// Identifies which conditional precondition was the subject of a
/// <see cref="Error.PreconditionFailed"/> or <see cref="Error.PreconditionRequired"/> error.
/// </summary>
public enum PreconditionKind
{
    /// <summary>The HTTP <c>If-Match</c> precondition (RFC 9110 §13.1.1).</summary>
    IfMatch,

    /// <summary>The HTTP <c>If-None-Match</c> precondition (RFC 9110 §13.1.2).</summary>
    IfNoneMatch,

    /// <summary>The HTTP <c>If-Modified-Since</c> precondition (RFC 9110 §13.1.3).</summary>
    IfModifiedSince,

    /// <summary>The HTTP <c>If-Unmodified-Since</c> precondition (RFC 9110 §13.1.4).</summary>
    IfUnmodifiedSince,
}