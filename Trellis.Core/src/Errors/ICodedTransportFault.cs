namespace Trellis;

/// <summary>
/// A transport fault that carries its own machine-readable code and kind, so a boundary can publish
/// them without knowing which transport produced the failure.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITransportFault"/> is a marker: Core deliberately knows nothing about HTTP, gRPC, or a
/// message bus. But a fault that has a code and no way to say so forces every boundary to downcast to
/// a transport-specific type, and a boundary that forgets publishes the sentinel while another
/// publishes the real code — the two then disagree about the same failure.
/// </para>
/// <para>
/// The codes here are <b>outside</b> the <see cref="ValidationCodes"/> vocabulary. They come from
/// another system, so <see cref="Error.TransportFault"/> publishes them verbatim rather than
/// normalizing them into a vocabulary their producer never agreed to.
/// </para>
/// </remarks>
public interface ICodedTransportFault : ITransportFault
{
    /// <summary>
    /// Gets the stable slug for this fault's category, used as the wire kind.
    /// </summary>
    string Kind { get; }

    /// <summary>
    /// Gets the machine-readable code for this fault. Published verbatim.
    /// </summary>
    string Code { get; }
}
