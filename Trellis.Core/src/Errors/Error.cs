namespace Trellis;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

/// <summary>
/// Closed discriminated union of error values. Each case is a nested <see langword="sealed record"/>
/// that mirrors a status from the IANA HTTP Status Code Registry (RFC 9110, RFC 6585) and carries
/// a strongly-typed payload describing what went wrong.
/// </summary>
/// <remarks>
/// <para>
/// <b>Closure.</b> The base record has a private constructor; only nested cases declared in this
/// file may inherit from <see cref="Error"/>. External code cannot extend the catalog, so
/// <c>switch</c> over an <see cref="Error"/> reference is exhaustive at the language level.
/// </para>
/// <para>
/// <b>Identity.</b> <see cref="Kind"/> is a stable, IANA-aligned slug (e.g. <c>"not-found"</c>)
/// that survives CLR renames. <see cref="Code"/> defaults to <see cref="Kind"/> and is overridden
/// by cases whose payload carries a per-instance reason code (for example <see cref="Conflict"/>
/// returns its <c>ReasonCode</c>).
/// </para>
/// <para>
/// <b>Detail.</b> Every case inherits an optional <c>Detail</c> property from the base. Callers
/// supply it via object-initializer syntax: <c>new Error.NotFound(resource) { Detail = "..." }</c>.
/// The boundary renderer prefers <c>Detail</c> when present; otherwise it computes a localized
/// message from <see cref="Kind"/>, <see cref="Code"/>, and the typed payload.
/// </para>
/// <para>
/// <b>Equality.</b> Value-based equality over the discriminator, the typed payload, and
/// <see cref="Detail"/>. <see cref="Cause"/> is intentionally excluded from equality so that
/// two errors with identical surface payload compare equal regardless of how deeply they were
/// wrapped — see the <see cref="Equals(Error?)"/> override for the rationale.
/// Collection-bearing payloads use <see cref="EquatableArray{T}"/> for sequence equality.
/// </para>
/// <para>
/// <b>Cause chain.</b> <see cref="Cause"/> is a structured chain (never a live <see cref="System.Exception"/>).
/// Cycles are detected at <c>init</c> time and throw <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
[DebuggerDisplay("{Kind,nq}: {Detail ?? Code,nq}")]
#pragma warning disable CA1716 // Identifiers should not match keywords — "Error" is the framework's domain term.
public abstract record Error
#pragma warning restore CA1716
{
    private readonly Error? _cause;

    private Error() { }

    /// <summary>
    /// Gets the stable, IANA-aligned identifier for this case (e.g. <c>"not-found"</c>,
    /// <c>"unprocessable-content"</c>). Suitable for telemetry, problem-details
    /// <c>type</c> URI synthesis, and wire serialization.
    /// </summary>
    public abstract string Kind { get; }

    /// <summary>
    /// Gets the per-instance machine-readable code. Defaults to <see cref="Kind"/>; cases
    /// whose payload carries a per-instance <c>ReasonCode</c> override this.
    /// </summary>
    public virtual string Code => Kind;

    /// <summary>
    /// Gets the optional human-readable detail. When non-null the boundary renderer prefers
    /// this over the default template for <see cref="Code"/>.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Gets the optional structured cause of this error. Never holds a live <see cref="System.Exception"/>;
    /// use a child <see cref="Error"/> to attach causal context.
    /// </summary>
    public Error? Cause
    {
        get => _cause;
        init
        {
            if (value is not null) EnsureAcyclic(value);
            _cause = value;
        }
    }

    private void EnsureAcyclic(Error candidate)
    {
        var seen = new HashSet<Error>(ReferenceEqualityComparer.Instance) { this };
        var current = candidate;
        while (current is not null)
        {
            if (!seen.Add(current))
                throw new InvalidOperationException("Error.Cause chain contains a cycle.");
            current = current.Cause;
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}: {Detail ?? Code}";

    /// <summary>
    /// Returns a human-readable message suitable for logging, tracing, and diagnostic
    /// surfaces. Prefers the explicit <see cref="Detail"/> when set; otherwise flattens
    /// any per-field violation messages (for <see cref="UnprocessableContent"/>) before
    /// falling back to <see cref="Code"/>.
    /// </summary>
    public virtual string GetDisplayMessage()
    {
        if (!string.IsNullOrEmpty(Detail))
        {
            return Detail;
        }

        if (this is UnprocessableContent uc)
        {
            var fieldItems = uc.Fields.Items;
            var ruleItems = uc.Rules.Items;

            // Single-field, no-rule shortcut: return just the detail / path with no prefix.
            if (fieldItems.Length == 1 && ruleItems.Length == 0)
            {
                var only = fieldItems[0];
                return !string.IsNullOrEmpty(only.Detail) ? only.Detail : only.Field.Path;
            }

            var parts = new List<string>(fieldItems.Length + ruleItems.Length);
            foreach (var fv in fieldItems)
            {
                parts.Add(!string.IsNullOrEmpty(fv.Detail)
                    ? $"{fv.Field.Path}: {fv.Detail}"
                    : fv.Field.Path);
            }

            foreach (var rv in ruleItems)
            {
                parts.Add(!string.IsNullOrEmpty(rv.Detail)
                    ? $"{rv.ReasonCode}: {rv.Detail}"
                    : rv.ReasonCode);
            }

            if (parts.Count > 0)
            {
                return string.Join("; ", parts);
            }
        }

        return Code;
    }

    /// <summary>
    /// Value equality over the discriminator (<see cref="EqualityContract"/>) and <see cref="Detail"/>,
    /// plus each derived case's positional payload. <see cref="Cause"/> is intentionally
    /// <b>excluded</b> from equality and hashing — two errors with identical kind, payload,
    /// and detail represent the same logical failure regardless of how deeply they were
    /// wrapped. This mirrors <see cref="System.Exception"/>, whose equality does not recurse
    /// into <c>InnerException</c>, and keeps test assertions ergonomic (callers assert on
    /// the surface error without reconstructing the entire causal chain).
    /// </summary>
    public virtual bool Equals(Error? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (EqualityContract != other.EqualityContract) return false;
        return string.Equals(Detail, other.Detail, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(EqualityContract, Detail);

    // ───────────────────────────────────────────────────────────────────────────
    // 4xx Client Errors (RFC 9110, RFC 6585)
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>HTTP 400 — the request is syntactically or semantically malformed.</summary>
    /// <param name="ReasonCode">Machine-readable code identifying why the request was rejected.</param>
    /// <param name="At">Optional pointer locating the offending input.</param>
    public sealed record BadRequest(string ReasonCode, InputPointer? At = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "bad-request";

        /// <inheritdoc />
        public override string Code => ReasonCode;
    }

    /// <summary>HTTP 401 — authentication is required or has failed.</summary>
    /// <param name="Challenges">Authentication challenges to present to the client (round-trips <c>WWW-Authenticate</c>).</param>
    public sealed record Unauthorized(EquatableArray<AuthChallenge> Challenges = default) : Error
    {
        /// <inheritdoc />
        public override string Kind => "unauthorized";
    }

    /// <summary>HTTP 403 — authorization policy refused the request.</summary>
    /// <param name="PolicyId">Identifier of the policy that denied access.</param>
    /// <param name="Resource">Optional resource the policy was evaluated against.</param>
    public sealed record Forbidden(string PolicyId, ResourceRef? Resource = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "forbidden";

        /// <inheritdoc />
        public override string Code => PolicyId;
    }

    /// <summary>HTTP 404 — the requested resource does not exist.</summary>
    /// <param name="Resource">The resource that was looked up.</param>
    public sealed record NotFound(ResourceRef Resource) : Error
    {
        /// <inheritdoc />
        public override string Kind => "not-found";
    }

    /// <summary>HTTP 405 — the HTTP method is not supported by the target resource.</summary>
    /// <param name="Allow">The set of methods supported by the resource (becomes the <c>Allow</c> header).</param>
    public sealed record MethodNotAllowed(EquatableArray<string> Allow) : Error
    {
        /// <inheritdoc />
        public override string Kind => "method-not-allowed";
    }

    /// <summary>HTTP 406 — none of the available representations are acceptable to the client.</summary>
    /// <param name="Available">Media types the server can produce.</param>
    public sealed record NotAcceptable(EquatableArray<string> Available) : Error
    {
        /// <inheritdoc />
        public override string Kind => "not-acceptable";
    }

    /// <summary>HTTP 409 — the request conflicts with the current state of the resource.</summary>
    /// <param name="Resource">
    /// The conflicting resource, when one is identifiable. May be <see langword="null"/> for
    /// stateless conflicts (e.g. workflow / state-machine guards, library code with no aggregate
    /// context). RFC 9110 § 15.5.10 implies the target resource via the request URI; the response
    /// body is not required to identify it.
    /// </param>
    /// <param name="ReasonCode">Machine-readable code describing the kind of conflict (e.g. <c>"duplicate_key"</c>, <c>"invalid_state"</c>).</param>
    public sealed record Conflict(ResourceRef? Resource, string ReasonCode) : Error
    {
        /// <inheritdoc />
        public override string Kind => "conflict";

        /// <inheritdoc />
        public override string Code => ReasonCode;
    }

    /// <summary>HTTP 410 — the resource is permanently gone.</summary>
    /// <param name="Resource">The resource that has been removed.</param>
    public sealed record Gone(ResourceRef Resource) : Error
    {
        /// <inheritdoc />
        public override string Kind => "gone";
    }

    /// <summary>HTTP 412 — a request precondition (e.g. <c>If-Match</c>) failed.</summary>
    /// <param name="Resource">The resource the precondition was evaluated against.</param>
    /// <param name="Condition">Which precondition failed.</param>
    public sealed record PreconditionFailed(ResourceRef Resource, PreconditionKind Condition) : Error
    {
        /// <inheritdoc />
        public override string Kind => "precondition-failed";

        /// <inheritdoc />
        public override string Code => Condition.ToString();
    }

    /// <summary>HTTP 413 — the request payload exceeds size limits.</summary>
    /// <param name="MaxBytes">Optional maximum accepted size in bytes.</param>
    public sealed record ContentTooLarge(long? MaxBytes = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "content-too-large";
    }

    /// <summary>HTTP 415 — the request's media type is not supported.</summary>
    /// <param name="Supported">Media types the resource can accept.</param>
    public sealed record UnsupportedMediaType(EquatableArray<string> Supported) : Error
    {
        /// <inheritdoc />
        public override string Kind => "unsupported-media-type";
    }

    /// <summary>HTTP 416 — the requested byte range cannot be satisfied.</summary>
    /// <param name="CompleteLength">The full length of the resource (used to synthesize the <c>Content-Range</c> header).</param>
    /// <param name="Unit">The range unit (typically <c>"bytes"</c>).</param>
    public sealed record RangeNotSatisfiable(long CompleteLength, string Unit = "bytes") : Error
    {
        /// <inheritdoc />
        public override string Kind => "range-not-satisfiable";
    }

    /// <summary>HTTP 422 — the request was well-formed but the content failed semantic validation.</summary>
    /// <param name="Fields">Per-field validation failures.</param>
    /// <param name="Rules">Global or multi-field business-rule failures.</param>
    public sealed record UnprocessableContent(
        EquatableArray<FieldViolation> Fields,
        EquatableArray<RuleViolation> Rules = default) : Error
    {
        /// <inheritdoc />
        public override string Kind => "unprocessable-content";

        /// <summary>
        /// Convenience factory that produces an <see cref="UnprocessableContent"/> carrying a
        /// single <see cref="FieldViolation"/> built from a property name. The property name is
        /// converted to a JSON Pointer via <see cref="InputPointer.ForProperty(string)"/>; pass
        /// an empty or <see langword="null"/> string to target the document root.
        /// </summary>
        /// <param name="propertyName">Simple property name or full JSON Pointer.</param>
        /// <param name="reasonCode">Stable machine-readable code identifying the rule that was violated.</param>
        /// <param name="detail">Optional human-readable detail; when supplied the boundary renderer prefers it over the default template for <paramref name="reasonCode"/>.</param>
        /// <returns>A 422 error wrapping the single field violation.</returns>
        public static UnprocessableContent ForField(string propertyName, string reasonCode, string? detail = null) =>
            ForField(InputPointer.ForProperty(propertyName), reasonCode, detail);

        /// <summary>
        /// Convenience factory that produces an <see cref="UnprocessableContent"/> carrying a
        /// single <see cref="FieldViolation"/> at the supplied <see cref="InputPointer"/>. Use this
        /// overload when the pointer was already computed (e.g. nested or array-element pointers,
        /// or <see cref="InputPointer.Root"/> for object-level violations).
        /// </summary>
        /// <param name="field">JSON Pointer locating the offending field.</param>
        /// <param name="reasonCode">Stable machine-readable code identifying the rule that was violated.</param>
        /// <param name="detail">Optional human-readable detail; when supplied the boundary renderer prefers it over the default template for <paramref name="reasonCode"/>.</param>
        /// <returns>A 422 error wrapping the single field violation.</returns>
        public static UnprocessableContent ForField(InputPointer field, string reasonCode, string? detail = null) =>
            new(EquatableArray.Create(new FieldViolation(field, reasonCode, Detail: detail)));

        /// <summary>
        /// Convenience factory that produces an <see cref="UnprocessableContent"/> carrying a
        /// single <see cref="RuleViolation"/> — the global / multi-field counterpart to
        /// <see cref="ForField(string, string, string?)"/>. Use for invariants that are not bound
        /// to a single field (e.g. <c>"order_must_have_items"</c>, <c>"passwords_must_match"</c>).
        /// </summary>
        /// <param name="reasonCode">Stable machine-readable code identifying the rule.</param>
        /// <param name="detail">Optional human-readable detail; when supplied the boundary renderer prefers it over the default template for <paramref name="reasonCode"/>.</param>
        /// <returns>A 422 error wrapping the single rule violation.</returns>
        public static UnprocessableContent ForRule(string reasonCode, string? detail = null) =>
            new(EquatableArray<FieldViolation>.Empty,
                EquatableArray.Create(new RuleViolation(reasonCode, Detail: detail)));
    }

    /// <summary>HTTP 428 — the resource requires a precondition that the request did not include.</summary>
    /// <param name="Condition">The precondition that must be supplied.</param>
    public sealed record PreconditionRequired(PreconditionKind Condition) : Error
    {
        /// <inheritdoc />
        public override string Kind => "precondition-required";

        /// <inheritdoc />
        public override string Code => Condition.ToString();
    }

    /// <summary>HTTP 429 — the client has exceeded a rate limit.</summary>
    /// <param name="RetryAfter">Optional advice for when the client may retry.</param>
    public sealed record TooManyRequests(RetryAfterValue? RetryAfter = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "too-many-requests";
    }

    // ───────────────────────────────────────────────────────────────────────────
    // 5xx Server Errors
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>HTTP 500 — an unhandled server-side fault occurred.</summary>
    /// <param name="FaultId">An opaque identifier correlating to richer diagnostics in the logging/telemetry layer.</param>
    public sealed record InternalServerError(string FaultId) : Error
    {
        /// <inheritdoc />
        public override string Kind => "internal-server-error";

        /// <inheritdoc />
        public override string Code => FaultId;
    }

    /// <summary>
    /// HTTP 500 — a "shouldn't happen" condition. Used for default-initialized <see cref="Result"/>/<see cref="Result{TValue}"/>
    /// (per ADR-002 §3.5.1), exhausted match arms, or other internal invariant violations whose root cause
    /// is a programming error rather than a documented server-side fault.
    /// </summary>
    /// <param name="ReasonCode">A stable, machine-readable identifier of the invariant that was violated
    /// (e.g. <c>"default_initialized"</c>, <c>"invariant_violation"</c>). Distinct per logical cause; not a per-incident id.</param>
    /// <remarks>
    /// Distinct from <see cref="InternalServerError"/>: that case carries an opaque per-incident <c>FaultId</c>
    /// for correlating with telemetry. <see cref="Unexpected"/> identifies the *kind* of unexpected condition.
    /// Both map to HTTP 500 at the ASP boundary, but only <see cref="InternalServerError"/> attaches a
    /// <c>faultId</c> extension to the problem-details payload.
    /// </remarks>
    public sealed record Unexpected(string ReasonCode) : Error
    {
        /// <inheritdoc />
        public override string Kind => "unexpected";

        /// <inheritdoc />
        public override string Code => ReasonCode;
    }

    /// <summary>HTTP 501 — the requested feature is not implemented.</summary>
    /// <param name="Feature">Identifier of the feature that is not implemented.</param>
    public sealed record NotImplemented(string Feature) : Error
    {
        /// <inheritdoc />
        public override string Kind => "not-implemented";

        /// <inheritdoc />
        public override string Code => Feature;
    }

    /// <summary>HTTP 503 — the server is temporarily unable to handle the request.</summary>
    /// <param name="RetryAfter">Optional advice for when the client may retry.</param>
    public sealed record ServiceUnavailable(RetryAfterValue? RetryAfter = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "service-unavailable";
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Composition
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Composition of multiple independent errors. Used when several failures occur
    /// together (e.g. parallel operations, batch validation). On the wire this typically
    /// renders as a problem-details <c>extensions.errors</c> array; <c>207 Multi-Status</c>
    /// is reserved for explicit batch endpoints.
    /// </summary>
    /// <remarks>
    /// Nested <see cref="Aggregate"/> values are flattened at construction. The constructor
    /// accepts at least one error.
    /// </remarks>
    public sealed record Aggregate : Error
    {
        /// <summary>Gets the flattened list of errors composing this aggregate.</summary>
        public EquatableArray<Error> Errors { get; }

        /// <summary>Initializes a new aggregate from the supplied errors. Nested aggregates are flattened.</summary>
        /// <param name="errors">The errors to compose. Must be non-empty.</param>
        public Aggregate(EquatableArray<Error> errors)
        {
            if (errors.IsEmpty) throw new ArgumentException("Aggregate requires at least one error.", nameof(errors));
            Errors = Flatten(errors);
        }

        /// <summary>Initializes a new aggregate from the supplied errors.</summary>
        /// <param name="errors">The errors to compose.</param>
        public Aggregate(IEnumerable<Error> errors) : this(EquatableArray<Error>.From(errors)) { }

        /// <summary>Initializes a new aggregate from the supplied errors.</summary>
        /// <param name="errors">The errors to compose.</param>
        public Aggregate(params Error[] errors) : this(EquatableArray<Error>.Create(errors)) { }

        /// <inheritdoc />
        public override string Kind => "aggregate";

        private static EquatableArray<Error> Flatten(EquatableArray<Error> input)
        {
            var needsFlatten = false;
            foreach (var e in input)
            {
                if (e is Aggregate) { needsFlatten = true; break; }
            }

            if (!needsFlatten) return input;

            var builder = ImmutableArray.CreateBuilder<Error>(input.Length);
            foreach (var e in input)
            {
                if (e is Aggregate inner)
                    foreach (var child in inner.Errors) builder.Add(child);
                else
                    builder.Add(e);
            }

            return new EquatableArray<Error>(builder.ToImmutable());
        }
    }
}
