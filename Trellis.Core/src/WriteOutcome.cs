namespace Trellis;

/// <summary>
/// Represents the outcome of a write operation (create / replace / accept-for-async) returned
/// by Application-layer repositories. The case selected describes <em>what happened</em> in
/// transport-agnostic terms; transport adapters (e.g. <c>Trellis.Asp</c>) translate each case to a
/// protocol-specific response (HTTP status code + headers, gRPC status, queue ack envelope, etc.).
/// </summary>
/// <remarks>
/// The case set aligns with the write outcomes enumerated in RFC 9110 §9.3.4 because HTTP is the
/// most commonly served transport, but the type itself takes no dependency on any transport package.
/// </remarks>
/// <typeparam name="T">The representation/body type returned for Created/Updated and the status payload type used by Accepted.</typeparam>
public abstract record WriteOutcome<T>
{
    private WriteOutcome() { }

    /// <summary>A new resource was created. Transports as HTTP <c>201 Created</c>.</summary>
    /// <param name="Value">The created entity.</param>
    /// <param name="Location">An address that identifies the newly created resource (e.g. a URI path).</param>
    /// <param name="Metadata">Optional representation metadata (ETag, Last-Modified, …) for the new resource.</param>
    public sealed record Created(T Value, string Location, RepresentationMetadata? Metadata = null) : WriteOutcome<T>;

    /// <summary>An existing resource was replaced/updated and the new representation is returned. Transports as HTTP <c>200 OK</c>.</summary>
    /// <param name="Value">The updated entity.</param>
    /// <param name="Metadata">Optional representation metadata for the updated resource.</param>
    public sealed record Updated(T Value, RepresentationMetadata? Metadata = null) : WriteOutcome<T>;

    /// <summary>An existing resource was replaced/updated and no body is returned. Transports as HTTP <c>204 No Content</c>.</summary>
    /// <param name="Metadata">Optional representation metadata for the updated resource.</param>
    public sealed record UpdatedNoContent(RepresentationMetadata? Metadata = null) : WriteOutcome<T>;

    /// <summary>The write was accepted for asynchronous processing and a status body is returned. Transports as HTTP <c>202 Accepted</c>.</summary>
    /// <param name="StatusBody">A status body describing the in-flight operation.</param>
    /// <param name="MonitorUri">Optional address where progress can be polled.</param>
    /// <param name="RetryAfter">Optional hint for when to poll next.</param>
    public sealed record Accepted(T StatusBody, string? MonitorUri = null, RetryAfterValue? RetryAfter = null) : WriteOutcome<T>;

    /// <summary>The write was accepted for asynchronous processing with no status body. Transports as HTTP <c>202 Accepted</c>.</summary>
    /// <param name="MonitorUri">Optional address where progress can be polled.</param>
    /// <param name="RetryAfter">Optional hint for when to poll next.</param>
    public sealed record AcceptedNoContent(string? MonitorUri = null, RetryAfterValue? RetryAfter = null) : WriteOutcome<T>;
}