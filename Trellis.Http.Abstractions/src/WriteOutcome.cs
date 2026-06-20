namespace Trellis;

/// <summary>
/// Represents the outcome of a write operation (create / replace / accept-for-async) returned
/// by Application-layer repositories. The case selected describes <em>what happened</em>; HTTP-aware
/// boundary adapters (for example <c>Trellis.Asp</c>) translate each case to status codes and headers.
/// </summary>
/// <remarks>
/// The case set aligns with the write outcomes enumerated in RFC 9110 §9.3.4 and lives in
/// <c>Trellis.Http.Abstractions</c> so server/client HTTP packages can share the same vocabulary.
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

/// <summary>
/// Factory helpers that build a <see cref="WriteOutcome{T}"/> case but return the
/// <em>base</em> type, so call sites flow through generic pipelines (for example
/// <c>Result.Map</c> / <c>ToHttpResponse</c>) without an explicit <c>(WriteOutcome&lt;T&gt;)</c>
/// cast to widen the nested case. Mirrors the non-generic <c>Result</c> / generic
/// <c>Result&lt;T&gt;</c> pairing.
/// </summary>
public static class WriteOutcome
{
    /// <summary>Builds the <c>Created</c> case (HTTP <c>201</c>), returned as the base <see cref="WriteOutcome{T}"/>.</summary>
    /// <typeparam name="T">The representation/body type.</typeparam>
    /// <param name="value">The created entity.</param>
    /// <param name="location">An address that identifies the newly created resource.</param>
    /// <param name="metadata">Optional representation metadata (ETag, Last-Modified, …).</param>
    /// <returns>The created outcome typed as <see cref="WriteOutcome{T}"/>.</returns>
    public static WriteOutcome<T> Created<T>(T value, string location, RepresentationMetadata? metadata = null)
        => new WriteOutcome<T>.Created(value, location, metadata);

    /// <summary>Builds the <c>Updated</c> case (HTTP <c>200</c>), returned as the base <see cref="WriteOutcome{T}"/>.</summary>
    /// <typeparam name="T">The representation/body type.</typeparam>
    /// <param name="value">The updated entity.</param>
    /// <param name="metadata">Optional representation metadata for the updated resource.</param>
    /// <returns>The updated outcome typed as <see cref="WriteOutcome{T}"/>.</returns>
    public static WriteOutcome<T> Updated<T>(T value, RepresentationMetadata? metadata = null)
        => new WriteOutcome<T>.Updated(value, metadata);

    /// <summary>Builds the <c>UpdatedNoContent</c> case (HTTP <c>204</c>), returned as the base <see cref="WriteOutcome{T}"/>. <typeparamref name="T"/> must be specified explicitly.</summary>
    /// <typeparam name="T">The representation/body type the surrounding pipeline carries.</typeparam>
    /// <param name="metadata">Optional representation metadata for the updated resource.</param>
    /// <returns>The no-content updated outcome typed as <see cref="WriteOutcome{T}"/>.</returns>
    public static WriteOutcome<T> UpdatedNoContent<T>(RepresentationMetadata? metadata = null)
        => new WriteOutcome<T>.UpdatedNoContent(metadata);

    /// <summary>Builds the <c>Accepted</c> case (HTTP <c>202</c>), returned as the base <see cref="WriteOutcome{T}"/>.</summary>
    /// <typeparam name="T">The status-body type.</typeparam>
    /// <param name="statusBody">A status body describing the in-flight operation.</param>
    /// <param name="monitorUri">Optional address where progress can be polled.</param>
    /// <param name="retryAfter">Optional hint for when to poll next.</param>
    /// <returns>The accepted outcome typed as <see cref="WriteOutcome{T}"/>.</returns>
    public static WriteOutcome<T> Accepted<T>(T statusBody, string? monitorUri = null, RetryAfterValue? retryAfter = null)
        => new WriteOutcome<T>.Accepted(statusBody, monitorUri, retryAfter);

    /// <summary>Builds the <c>AcceptedNoContent</c> case (HTTP <c>202</c>), returned as the base <see cref="WriteOutcome{T}"/>. <typeparamref name="T"/> must be specified explicitly.</summary>
    /// <typeparam name="T">The representation/body type the surrounding pipeline carries.</typeparam>
    /// <param name="monitorUri">Optional address where progress can be polled.</param>
    /// <param name="retryAfter">Optional hint for when to poll next.</param>
    /// <returns>The no-content accepted outcome typed as <see cref="WriteOutcome{T}"/>.</returns>
    public static WriteOutcome<T> AcceptedNoContent<T>(string? monitorUri = null, RetryAfterValue? retryAfter = null)
        => new WriteOutcome<T>.AcceptedNoContent(monitorUri, retryAfter);
}