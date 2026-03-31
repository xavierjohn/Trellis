namespace Trellis;

/// <summary>
/// Represents a range not satisfiable error when the server cannot serve
/// the requested byte range for a resource. Maps to HTTP 416 Range Not Satisfiable.
/// </summary>
/// <remarks>
/// <para>
/// Per RFC 9110 §14.4/§15.5.17, a server generating 416 for a byte-range request
/// SHOULD include a <c>Content-Range: bytes */{completeLength}</c> header.
/// The <see cref="CompleteLength"/> and <see cref="Unit"/> properties carry this metadata
/// so that Trellis response mappers can emit the required header automatically.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Error.RangeNotSatisfiable("Requested range is not satisfiable.", 1024)
/// </code>
/// </example>
public sealed class RangeNotSatisfiableError : Error
{
    /// <summary>
    /// Gets the complete length of the selected representation in the specified unit.
    /// Used to emit the <c>Content-Range</c> header (e.g., <c>bytes */1024</c>).
    /// </summary>
    public long CompleteLength { get; }

    /// <summary>
    /// Gets the range unit (defaults to "bytes").
    /// </summary>
    public string Unit { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RangeNotSatisfiableError"/> class.
    /// </summary>
    /// <param name="detail">Description of why the range is not satisfiable.</param>
    /// <param name="completeLength">The complete length of the representation.</param>
    /// <param name="code">The error code identifying this type of error.</param>
    /// <param name="unit">The range unit (defaults to "bytes").</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    public RangeNotSatisfiableError(string detail, long completeLength, string code, string unit = "bytes", string? instance = null)
        : base(detail, code, instance)
    {
        ArgumentNullException.ThrowIfNull(unit);
        CompleteLength = completeLength;
        Unit = unit;
    }
}
