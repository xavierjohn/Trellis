namespace Trellis;

/// <summary>
/// Represents a method not allowed error when the HTTP method is not supported
/// for the target resource. Maps to HTTP 405 Method Not Allowed.
/// </summary>
/// <remarks>
/// <para>
/// Per RFC 9110 §15.5.6, a 405 response <b>MUST</b> include an <c>Allow</c> header
/// listing the methods the target resource currently supports.
/// The <see cref="AllowedMethods"/> property carries this information so that
/// Trellis response mappers can emit the required header automatically.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// Error.MethodNotAllowed("DELETE is not supported on this resource.", ["GET", "PUT"])
/// </code>
/// </example>
public sealed class MethodNotAllowedError : Error
{
    /// <summary>
    /// Gets the HTTP methods that the target resource currently supports.
    /// This is emitted as the <c>Allow</c> response header per RFC 9110 §15.5.6.
    /// </summary>
    public IReadOnlyList<string> AllowedMethods { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MethodNotAllowedError"/> class.
    /// </summary>
    /// <param name="detail">Description of why the method is not allowed.</param>
    /// <param name="allowedMethods">The HTTP methods the resource supports.</param>
    /// <param name="code">The error code identifying this type of error.</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    public MethodNotAllowedError(string detail, IReadOnlyList<string> allowedMethods, string code, string? instance = null)
        : base(detail, code, instance)
    {
        ArgumentNullException.ThrowIfNull(allowedMethods);
        AllowedMethods = allowedMethods;
    }
}
