namespace Trellis;

/// <summary>
/// Identifies which part of an input an offending value came from.
/// </summary>
/// <remarks>
/// <para>
/// This is the <c>in</c> discriminator of a validation error's location on the wire. The
/// default, <see cref="Unspecified"/>, projects as <c>"unknown"</c>, which means
/// <em>"do not resolve the accompanying pointer as a document location"</em> — the producer
/// did not know where the value came from and says so rather than asserting a checkable
/// claim that may be false.
/// </para>
/// <para>
/// <see cref="Body"/> locations carry an RFC 6901 JSON Pointer into the request document.
/// <see cref="Query"/>, <see cref="Path"/> and <see cref="Header"/> locations carry a
/// parameter <em>name</em> instead, because there is no document for a pointer to address.
/// </para>
/// </remarks>
public enum InputLocation
{
    /// <summary>
    /// The location is not known. Projects as <c>"unknown"</c>.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The value came from the request body, addressed by a JSON Pointer.
    /// </summary>
    Body = 1,

    /// <summary>
    /// The value came from a query-string parameter, addressed by name.
    /// </summary>
    Query = 2,

    /// <summary>
    /// The value came from a route (path) parameter, addressed by name.
    /// </summary>
    Path = 3,

    /// <summary>
    /// The value came from a request header, addressed by name.
    /// </summary>
    Header = 4,
}
