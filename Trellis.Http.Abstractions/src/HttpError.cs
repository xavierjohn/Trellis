namespace Trellis;

using System.Collections.Generic;
using System.Diagnostics;

/// <summary>
/// Closed discriminated union of HTTP-transport failures. Each case mirrors a status from
/// the IANA HTTP Status Code Registry (RFC 9110, RFC 6585) and carries header data needed
/// to emit an RFC-compliant response. Flows through <see cref="Result{TValue}"/> via the
/// envelope <see cref="Error.TransportFault"/>: <c>new Error.TransportFault(new HttpError.NotAcceptable(...))</c>.
/// </summary>
/// <remarks>
/// Domain code does not construct or pattern-match <c>HttpError</c>. Construction is the
/// responsibility of HTTP-aware boundary code: server-side (<c>Trellis.Asp</c>) and
/// client-side (<c>Trellis.Http</c>).
/// </remarks>
[DebuggerDisplay("{Kind,nq}: {Detail ?? Code,nq}")]
public abstract record HttpError : ICodedTransportFault
{
    private readonly HttpError? _cause;

    private HttpError() { }

    /// <summary>
    /// Gets the stable, HTTP-aligned identifier for this case (for example
    /// <c>"method-not-allowed"</c> or <c>"precondition-failed"</c>).
    /// </summary>
    public abstract string Kind { get; }

    /// <summary>
    /// Gets the machine-readable code for this fault. Defaults to <see cref="Kind"/>;
    /// cases whose payload identifies a specific conditional header override it.
    /// </summary>
    public virtual string Code => Kind;

    /// <summary>
    /// Gets optional human-readable detail. When non-null, boundary renderers prefer this
    /// over default templates for the fault.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Gets the optional structured cause of this HTTP fault. Never holds a live
    /// <see cref="System.Exception"/>; use a child <see cref="HttpError"/> to attach
    /// lower-level causal context.
    /// </summary>
    public HttpError? Cause
    {
        get => _cause;
        init
        {
            if (value is not null) EnsureAcyclic(value);
            _cause = value;
        }
    }

    private void EnsureAcyclic(HttpError candidate)
    {
        var seen = new HashSet<HttpError>(ReferenceEqualityComparer.Instance) { this };
        var current = candidate;
        while (current is not null)
        {
            if (!seen.Add(current))
                throw new InvalidOperationException("HttpError.Cause chain contains a cycle.");
            current = current.Cause;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}: {Detail ?? Code}";

    /// <summary>
    /// Value equality over the discriminator (<see cref="EqualityContract"/>) and <see cref="Detail"/>,
    /// plus each derived case's positional payload. <see cref="Cause"/> is intentionally excluded.
    /// </summary>
    public virtual bool Equals(HttpError? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (EqualityContract != other.EqualityContract) return false;
        return string.Equals(Detail, other.Detail, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(EqualityContract, Detail);

    /// <summary>HTTP 405 — the HTTP method is not supported by the target resource.</summary>
    /// <param name="Allow">The set of methods supported by the resource (becomes the <c>Allow</c> header).</param>
    public sealed record MethodNotAllowed(EquatableArray<string> Allow) : HttpError
    {
        /// <inheritdoc />
        public override string Kind => "method-not-allowed";

        /// <inheritdoc />
        public override string ToString() => base.ToString();
    }

    /// <summary>HTTP 406 — none of the available representations are acceptable to the client.</summary>
    /// <param name="Available">Media types the server can produce.</param>
    public sealed record NotAcceptable(EquatableArray<string> Available) : HttpError
    {
        /// <inheritdoc />
        public override string Kind => "not-acceptable";

        /// <inheritdoc />
        public override string ToString() => base.ToString();
    }

    /// <summary>HTTP 415 — the request's media type is not supported.</summary>
    /// <param name="Supported">Media types the resource can accept.</param>
    public sealed record UnsupportedMediaType(EquatableArray<string> Supported) : HttpError
    {
        /// <inheritdoc />
        public override string Kind => "unsupported-media-type";

        /// <inheritdoc />
        public override string ToString() => base.ToString();
    }

    /// <summary>HTTP 416 — the requested byte range cannot be satisfied.</summary>
    /// <param name="CompleteLength">The full length of the resource (used to synthesize the <c>Content-Range</c> header).</param>
    /// <param name="Unit">The range unit (typically <c>"bytes"</c>).</param>
    public sealed record RangeNotSatisfiable(long CompleteLength, string Unit = "bytes") : HttpError
    {
        /// <inheritdoc />
        public override string Kind => "range-not-satisfiable";

        /// <inheritdoc />
        public override string ToString() => base.ToString();
    }

    /// <summary>HTTP 413 — the request payload exceeds size limits.</summary>
    /// <param name="MaxBytes">Optional maximum accepted size in bytes.</param>
    public sealed record ContentTooLarge(long? MaxBytes = null) : HttpError
    {
        /// <inheritdoc />
        public override string Kind => "content-too-large";

        /// <inheritdoc />
        public override string ToString() => base.ToString();
    }

    /// <summary>HTTP 412 — a request precondition (for example <c>If-Match</c>) failed.</summary>
    /// <param name="Resource">The resource the precondition was evaluated against.</param>
    /// <param name="Condition">Which precondition failed.</param>
    public sealed record PreconditionFailed(ResourceRef Resource, PreconditionKind Condition) : HttpError
    {
        /// <inheritdoc />
        public override string Kind => "precondition-failed";

        /// <inheritdoc />
        public override string Code => Condition.ToString();

        /// <inheritdoc />
        public override string ToString() => base.ToString();
    }

    /// <summary>HTTP 428 — the resource requires a precondition that the request did not include.</summary>
    /// <param name="Condition">The precondition that must be supplied.</param>
    public sealed record PreconditionRequired(PreconditionKind Condition) : HttpError
    {
        /// <inheritdoc />
        public override string Kind => "precondition-required";

        /// <inheritdoc />
        public override string Code => Condition.ToString();

        /// <inheritdoc />
        public override string ToString() => base.ToString();
    }
}