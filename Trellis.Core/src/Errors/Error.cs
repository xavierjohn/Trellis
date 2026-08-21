namespace Trellis;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json.Serialization;

/// <summary>
/// Closed discriminated union of Trellis error values. Domain-facing cases stay transport-neutral,
/// while boundary-layer protocols can attach typed lower-level payloads through
/// <see cref="TransportFault"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Closure.</b> The base record has a private constructor; only nested cases declared in this
/// file may inherit from <see cref="Error"/>. External code cannot extend the catalog, so
/// <c>switch</c> over an <see cref="Error"/> reference is exhaustive at the language level.
/// </para>
/// <para>
/// <b>Identity.</b> <see cref="Kind"/> is a stable domain slug suitable for telemetry and wire
/// serialization (e.g. <c>"not-found"</c>). <see cref="Code"/> defaults to <see cref="Kind"/> and
/// is overridden by cases whose payload carries a per-instance reason code (for example
/// <see cref="Conflict"/> returns its <c>ReasonCode</c>).
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
#pragma warning disable CA1716
public abstract record Error
#pragma warning restore CA1716
{
    private readonly Error? _cause;

    private Error() { }

    /// <summary>
    /// Gets the stable domain slug for this case (e.g. <c>"not-found"</c>,
    /// <c>"invalid-input"</c>). Suitable for telemetry, observability dimensions, and as
    /// the durable identifier that boundary layers translate into transport-specific
    /// type identifiers.
    /// </summary>
    public abstract string Kind { get; }

    /// <summary>
    /// Gets the per-instance machine-readable code. Defaults to <see cref="Kind"/>; cases
    /// whose payload carries a per-instance <c>ReasonCode</c> override this.
    /// </summary>
    public virtual string Code => Kind;

    /// <summary>
    /// Gets a value indicating whether this instance carries a machine-readable code of its
    /// own, as opposed to <see cref="Code"/> merely restating <see cref="Kind"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a <em>presence</em> test and never a value comparison: a payload whose code
    /// happens to equal its own <see cref="Kind"/> still carries an explicit code and reports
    /// <see langword="true"/>. Boundary renderers use this to decide whether to emit
    /// <see cref="Code"/> or a sentinel meaning "no finer reason available", so that a kind
    /// restated as a code is never mistaken for a reason.
    /// </para>
    /// <para>
    /// Declared <see langword="abstract"/> rather than <see langword="virtual"/> deliberately.
    /// <see cref="Error"/> is a closed union — its constructor is private and no external code
    /// can extend the catalog — so requiring every case to answer costs nothing and turns
    /// "a new case carries a code and forgets to say so" from a silent wire defect into a
    /// compile error.
    /// </para>
    /// </remarks>
    public abstract bool HasExplicitCode { get; }

    /// <summary>
    /// Gets the code a consumer sees: <see cref="Code"/> when this instance carries one of its own,
    /// and <see cref="ValidationCodes.Unspecified"/> when it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Code"/> defaults to <see cref="Kind"/>, so reading it directly at a boundary
    /// publishes a kind restated as a reason — a consumer switching on it would branch on
    /// <c>"not-found"</c> as though a producer had chosen it. This property applies
    /// <see cref="HasExplicitCode"/> and <see cref="ValidationCodes.Normalize"/> once, so every
    /// boundary answers the question the same way.
    /// </para>
    /// <para>
    /// <b>Read this, not <see cref="Code"/>, anywhere the value leaves the process</b> — an HTTP
    /// body, a span tag, a metric dimension, a log field. The value of a code is that an operator
    /// can carry it from a bug report into a trace query, and that only holds while the two agree.
    /// <see cref="Code"/> remains the in-process value for a producer that needs the raw decision.
    /// </para>
    /// </remarks>
    public virtual string WireCode => HasExplicitCode ? ValidationCodes.Normalize(Code) : ValidationCodes.Unspecified;

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
    /// any per-field violation messages (for <see cref="InvalidInput"/>) before
    /// falling back to <see cref="Code"/>.
    /// </summary>
    public virtual string GetDisplayMessage()
    {
        if (!string.IsNullOrEmpty(Detail))
        {
            return Detail;
        }

        if (this is InvalidInput ic)
        {
            var fieldItems = ic.Fields.Items;
            var ruleItems = ic.Rules.Items;

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
    /// <remarks>
    /// <para>
    /// <b>How per-derived payload comparison works:</b> this override deliberately checks
    /// only <c>EqualityContract</c> and <see cref="Detail"/>. Each derived <c>sealed record</c>
    /// (e.g., <see cref="NotFound"/>, <see cref="InvalidInput"/>) gets a compiler-generated
    /// <c>Equals(Derived?)</c> of the form
    /// <c>base.Equals(other) &amp;&amp; Field1 == other.Field1 &amp;&amp; ...</c>.
    /// The <c>base.Equals(other)</c> call dispatches virtually to this override, contributing
    /// the kind+detail check; the derived method then ANDs in its per-property comparison.
    /// The net effect is element-wise equality across both base and derived fields, without
    /// any per-derived override needed.
    /// </para>
    /// <para>
    /// <see cref="GetHashCode"/> uses the same compose-with-derived pattern: the override
    /// hashes <c>EqualityContract</c> and <see cref="Detail"/>, and each derived record's
    /// auto-generated <c>GetHashCode</c> combines <c>base.GetHashCode()</c> with hashes of
    /// its own properties.
    /// </para>
    /// </remarks>
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
    // Validation and invariants
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inbound request payload failed semantic validation: one or more fields or rules
    /// rejected the input.
    /// </summary>
    /// <param name="Fields">Per-field validation failures.</param>
    /// <param name="Rules">Global or multi-field business-rule failures detected against the inbound shape.</param>
    public sealed record InvalidInput(
        EquatableArray<FieldViolation> Fields,
        EquatableArray<RuleViolation> Rules = default) : Error
    {
        /// <inheritdoc />
        public override string Kind => "invalid-input";

        /// <inheritdoc />
        /// <remarks>Per-violation codes live on the individual field and rule violations, not on the error itself.</remarks>
        public override bool HasExplicitCode => false;

        /// <summary>
        /// Convenience factory that produces an <see cref="InvalidInput"/> carrying a
        /// single <see cref="FieldViolation"/> built from a property name. The property name is
        /// converted to a JSON Pointer via <see cref="InputPointer.ForProperty(string)"/>; pass
        /// an empty or <see langword="null"/> string to target the document root.
        /// </summary>
        /// <param name="propertyName">Simple property name or full JSON Pointer.</param>
        /// <param name="reasonCode">Stable machine-readable code identifying the rule that was violated.</param>
        /// <param name="detail">Optional human-readable detail; when supplied the boundary renderer prefers it over the default template for <paramref name="reasonCode"/>.</param>
        /// <returns>An <see cref="InvalidInput"/> wrapping the single field violation.</returns>
        public static InvalidInput ForField(string propertyName, string reasonCode, string? detail = null) =>
            ForField(InputPointer.ForProperty(propertyName), reasonCode, detail);

        /// <summary>
        /// Convenience factory that produces an <see cref="InvalidInput"/> carrying a
        /// single <see cref="FieldViolation"/> at the supplied <see cref="InputPointer"/>.
        /// </summary>
        /// <param name="field">JSON Pointer locating the offending field.</param>
        /// <param name="reasonCode">Stable machine-readable code identifying the rule that was violated.</param>
        /// <param name="detail">Optional human-readable detail; when supplied the boundary renderer prefers it over the default template for <paramref name="reasonCode"/>.</param>
        /// <returns>An <see cref="InvalidInput"/> wrapping the single field violation.</returns>
        public static InvalidInput ForField(InputPointer field, string reasonCode, string? detail = null) =>
            new(EquatableArray.Create(new FieldViolation(field, reasonCode, Detail: detail)));

        /// <summary>
        /// Convenience factory that produces an <see cref="InvalidInput"/> carrying a single
        /// <see cref="FieldViolation"/> with machine-readable operands attached.
        /// </summary>
        /// <param name="propertyName">Simple property name or full JSON Pointer.</param>
        /// <param name="reasonCode">Stable machine-readable code identifying the rule that was violated.</param>
        /// <param name="args">Operands of the rule (e.g. <c>maxLength</c>, <c>comparisonValue</c>), built with <see cref="ValidationArgs"/>. Never put the rejected value itself here.</param>
        /// <param name="detail">Optional human-readable detail; when supplied the boundary renderer prefers it over the default template for <paramref name="reasonCode"/>.</param>
        /// <returns>An <see cref="InvalidInput"/> wrapping the single field violation.</returns>
        public static InvalidInput ForField(string propertyName, string reasonCode, ImmutableDictionary<string, string>? args, string? detail = null) =>
            ForField(InputPointer.ForProperty(propertyName), reasonCode, args, detail);

        /// <summary>
        /// Convenience factory that produces an <see cref="InvalidInput"/> carrying a single
        /// <see cref="FieldViolation"/> at the supplied pointer with machine-readable operands attached.
        /// </summary>
        /// <param name="field">JSON Pointer locating the offending field.</param>
        /// <param name="reasonCode">Stable machine-readable code identifying the rule that was violated.</param>
        /// <param name="args">Operands of the rule (e.g. <c>maxLength</c>, <c>comparisonValue</c>), built with <see cref="ValidationArgs"/>. Never put the rejected value itself here.</param>
        /// <param name="detail">Optional human-readable detail; when supplied the boundary renderer prefers it over the default template for <paramref name="reasonCode"/>.</param>
        /// <returns>An <see cref="InvalidInput"/> wrapping the single field violation.</returns>
        public static InvalidInput ForField(InputPointer field, string reasonCode, ImmutableDictionary<string, string>? args, string? detail = null) =>
            new(EquatableArray.Create(new FieldViolation(field, reasonCode, args, detail)));

        /// <summary>
        /// Convenience factory that produces an <see cref="InvalidInput"/> carrying a
        /// single <see cref="RuleViolation"/> — the global / multi-field counterpart to
        /// <see cref="ForField(string, string, string?)"/>. Use for invariants that are not bound
        /// to a single field (e.g. <c>"order.must-have-items"</c>, <c>"password.mismatch"</c>).
        /// </summary>
        /// <param name="reasonCode">Stable machine-readable code identifying the rule.</param>
        /// <param name="detail">Optional human-readable detail; when supplied the boundary renderer prefers it over the default template for <paramref name="reasonCode"/>.</param>
        /// <returns>An <see cref="InvalidInput"/> wrapping the single rule violation.</returns>
        public static InvalidInput ForRule(string reasonCode, string? detail = null) =>
            new(EquatableArray<FieldViolation>.Empty,
                EquatableArray.Create(new RuleViolation(reasonCode, Detail: detail)))
            { Detail = detail };

        /// <summary>
        /// Convenience factory that produces an <see cref="InvalidInput"/> carrying a single
        /// <see cref="RuleViolation"/> with machine-readable operands attached.
        /// </summary>
        /// <param name="reasonCode">Stable machine-readable code identifying the rule.</param>
        /// <param name="args">Operands of the rule, built with <see cref="ValidationArgs"/>. Never put a rejected value itself here.</param>
        /// <param name="detail">Optional human-readable detail; when supplied the boundary renderer prefers it over the default template for <paramref name="reasonCode"/>.</param>
        /// <returns>An <see cref="InvalidInput"/> wrapping the single rule violation.</returns>
        public static InvalidInput ForRule(string reasonCode, ImmutableDictionary<string, string>? args, string? detail = null) =>
            new(EquatableArray<FieldViolation>.Empty,
                EquatableArray.Create(new RuleViolation(reasonCode, Args: args, Detail: detail)))
            { Detail = detail };
    }

    /// <summary>
    /// Global or multi-field business invariant was violated (e.g. cross-field rule,
    /// computed constraint) outside the inbound-validation pipeline.
    /// </summary>
    /// <param name="ReasonCode">Stable machine-readable code identifying the violated invariant.</param>
    /// <param name="Resource">Optional resource the invariant was evaluated against.</param>
    public sealed record InvariantViolation(string ReasonCode, ResourceRef? Resource = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "invariant-violation";

        /// <inheritdoc />
        public override bool HasExplicitCode => true;

        /// <inheritdoc />
        public override string Code => ReasonCode;

        /// <summary>
        /// Convenience factory that builds an <see cref="InvariantViolation"/> against the resource
        /// type <typeparamref name="TResource"/> (its CLR name becomes the resource name), mirroring
        /// the <see cref="Conflict"/> factories. <paramref name="reasonCode"/> leads because it is the
        /// invariant's required identity, with the resource id optional.
        /// </summary>
        /// <typeparam name="TResource">The resource the invariant was evaluated against.</typeparam>
        /// <param name="reasonCode">Stable machine-readable code identifying the violated invariant.</param>
        /// <param name="id">Identifier of the instance the invariant was evaluated against; pass <see langword="null"/> for an aggregate- or type-level invariant, or use <see cref="ForReason(string, string?)"/>.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>An <see cref="InvariantViolation"/> carrying the resource.</returns>
        public static InvariantViolation For<TResource>(string reasonCode, object? id = null, string? detail = null) =>
            new(reasonCode, ResourceRef.For<TResource>(id)) { Detail = detail };

        /// <summary>
        /// Convenience factory that builds an <see cref="InvariantViolation"/> from an explicit
        /// resource type name and identifier.
        /// </summary>
        /// <param name="resourceType">The resource type name the invariant was evaluated against.</param>
        /// <param name="reasonCode">Stable machine-readable code identifying the violated invariant.</param>
        /// <param name="id">Identifier of the instance the invariant was evaluated against.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>An <see cref="InvariantViolation"/> carrying the resource.</returns>
        public static InvariantViolation For(string resourceType, string reasonCode, object? id = null, string? detail = null) =>
            new(reasonCode, ResourceRef.For(resourceType, id)) { Detail = detail };

        /// <summary>
        /// Convenience factory for an invariant violation with no identifiable resource (e.g. a
        /// cross-field or workflow rule with no aggregate context). Bundles the optional
        /// <see cref="Error.Detail"/> that the primary constructor cannot set inline.
        /// </summary>
        /// <param name="reasonCode">Stable machine-readable code identifying the violated invariant.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>A resourceless <see cref="InvariantViolation"/>.</returns>
        public static InvariantViolation ForReason(string reasonCode, string? detail = null) =>
            new(reasonCode) { Detail = detail };
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Resource lifecycle
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>The requested resource does not exist.</summary>
    /// <param name="Resource">The resource that was looked up.</param>
    public sealed record NotFound(ResourceRef Resource) : Error
    {
        /// <inheritdoc />
        public override string Kind => "not-found";

        /// <inheritdoc />
        public override bool HasExplicitCode => false;

        /// <summary>
        /// Convenience factory that builds a <see cref="NotFound"/> for the resource type
        /// <typeparamref name="TResource"/> (its CLR name becomes the resource name).
        /// </summary>
        /// <typeparam name="TResource">The resource type whose name identifies the resource.</typeparam>
        /// <param name="id">Optional identifier of the specific instance; omit for a collection-level lookup.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>A <see cref="NotFound"/> wrapping the resource reference.</returns>
        public static NotFound For<TResource>(object? id = null, string? detail = null) =>
            new(ResourceRef.For<TResource>(id)) { Detail = detail };

        /// <summary>
        /// Convenience factory that builds a <see cref="NotFound"/> from an explicit resource
        /// type name and optional identifier.
        /// </summary>
        /// <param name="resourceType">The resource type name (e.g. <c>"Season"</c>).</param>
        /// <param name="id">Optional identifier of the specific instance.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>A <see cref="NotFound"/> wrapping the resource reference.</returns>
        public static NotFound For(string resourceType, object? id = null, string? detail = null) =>
            new(ResourceRef.For(resourceType, id)) { Detail = detail };
    }

    /// <summary>The resource was previously known but has been permanently removed (tombstone).</summary>
    /// <param name="Resource">The resource that has been removed.</param>
    public sealed record Gone(ResourceRef Resource) : Error
    {
        /// <inheritdoc />
        public override string Kind => "gone";

        /// <inheritdoc />
        public override bool HasExplicitCode => false;

        /// <summary>
        /// Convenience factory that builds a <see cref="Gone"/> for the resource type
        /// <typeparamref name="TResource"/> (its CLR name becomes the resource name).
        /// </summary>
        /// <typeparam name="TResource">The resource type whose name identifies the resource.</typeparam>
        /// <param name="id">Optional identifier of the specific instance.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>A <see cref="Gone"/> wrapping the resource reference.</returns>
        public static Gone For<TResource>(object? id = null, string? detail = null) =>
            new(ResourceRef.For<TResource>(id)) { Detail = detail };

        /// <summary>
        /// Convenience factory that builds a <see cref="Gone"/> from an explicit resource
        /// type name and optional identifier.
        /// </summary>
        /// <param name="resourceType">The resource type name (e.g. <c>"Season"</c>).</param>
        /// <param name="id">Optional identifier of the specific instance.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>A <see cref="Gone"/> wrapping the resource reference.</returns>
        public static Gone For(string resourceType, object? id = null, string? detail = null) =>
            new(ResourceRef.For(resourceType, id)) { Detail = detail };
    }

    /// <summary>The request conflicts with the current state of the resource.</summary>
    /// <param name="Resource">
    /// The conflicting resource, when one is identifiable. May be <see langword="null"/> for
    /// stateless conflicts (e.g. workflow / state-machine guards, library code with no aggregate
    /// context).
    /// </param>
    /// <param name="ReasonCode">Machine-readable code describing the kind of conflict (e.g. <c>"duplicate-key"</c>, <c>"invalid-state"</c>).</param>
    public sealed record Conflict(ResourceRef? Resource, string ReasonCode) : Error
    {
        /// <inheritdoc />
        public override string Kind => "conflict";

        /// <inheritdoc />
        public override bool HasExplicitCode => true;

        /// <inheritdoc />
        public override string Code => ReasonCode;

        /// <summary>
        /// Optional provider-reported constraint name when the conflict came from a database
        /// constraint violation (unique index, primary key, or foreign key). Populated on a
        /// best-effort basis by helpers such as
        /// <c>DbContext.TryInsertUniqueAsync</c> and <c>DbContext.SaveChangesResultAsync</c>;
        /// <see langword="null"/> for non-database conflicts or when the provider does not
        /// surface the name.
        /// </summary>
        /// <remarks>
        /// Telemetry-only. The value can reveal schema details (index names, constraint names)
        /// and is therefore excluded from default <c>System.Text.Json</c> serialization via
        /// <see cref="JsonIgnoreAttribute"/>. Use it for structured logging and observability
        /// dimensions; do not surface it directly in API responses.
        /// </remarks>
        [JsonIgnore]
        public string? ConstraintName { get; init; }

        /// <summary>
        /// Optional provider-reported table name associated with <see cref="ConstraintName"/>,
        /// when the provider surfaces it. <see langword="null"/> otherwise.
        /// </summary>
        /// <remarks>
        /// Telemetry-only with the same handling as <see cref="ConstraintName"/>: excluded
        /// from default JSON serialization and unsuitable for API responses.
        /// </remarks>
        [JsonIgnore]
        public string? ConstraintTableName { get; init; }

        /// <summary>
        /// Convenience factory that builds a <see cref="Conflict"/> against the resource type
        /// <typeparamref name="TResource"/> (its CLR name becomes the resource name).
        /// </summary>
        /// <typeparam name="TResource">The conflicting resource type.</typeparam>
        /// <param name="id">Identifier of the conflicting instance; pass <see langword="null"/> for a collection-level conflict, or use <see cref="ForReason(string, string?)"/>.</param>
        /// <param name="reasonCode">Machine-readable code describing the kind of conflict.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>A <see cref="Conflict"/> for the resource.</returns>
        public static Conflict For<TResource>(object? id, string reasonCode, string? detail = null) =>
            new(ResourceRef.For<TResource>(id), reasonCode) { Detail = detail };

        /// <summary>
        /// Convenience factory that builds a <see cref="Conflict"/> from an explicit resource
        /// type name and identifier.
        /// </summary>
        /// <param name="resourceType">The conflicting resource type name.</param>
        /// <param name="id">Identifier of the conflicting instance.</param>
        /// <param name="reasonCode">Machine-readable code describing the kind of conflict.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>A <see cref="Conflict"/> for the resource.</returns>
        public static Conflict For(string resourceType, object? id, string reasonCode, string? detail = null) =>
            new(ResourceRef.For(resourceType, id), reasonCode) { Detail = detail };

        /// <summary>
        /// Convenience factory for a stateless conflict with no identifiable resource (e.g. a
        /// workflow / state-machine guard, or library code with no aggregate context).
        /// </summary>
        /// <param name="reasonCode">Machine-readable code describing the kind of conflict.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>A resourceless <see cref="Conflict"/>.</returns>
        public static Conflict ForReason(string reasonCode, string? detail = null) =>
            new(Resource: null, ReasonCode: reasonCode) { Detail = detail };
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Identity and access
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>The operation requires authentication that was not supplied or could not be validated.</summary>
    /// <param name="Scheme">Optional authentication scheme name (e.g. <c>"Bearer"</c>) when the producer knows which scheme is expected.</param>
    /// <param name="ReasonCode">
    /// Optional machine-readable code distinguishing causes that share the 401 surface
    /// (e.g. <c>"Authentication.InvalidCredentials"</c>, <c>"Authentication.MissingCredentials"</c>,
    /// <c>"Authentication.TokenExpired"</c>). When supplied, <see cref="Code"/> returns this value
    /// instead of <see cref="Kind"/> so telemetry, dashboards, and client branching can distinguish
    /// the cases without parsing <see cref="Error.Detail"/>.
    /// </param>
    public sealed record AuthenticationRequired(string? Scheme = null, string? ReasonCode = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "authentication-required";

        /// <inheritdoc />
        public override bool HasExplicitCode => ReasonCode is not null;

        /// <inheritdoc />
        public override string Code => ReasonCode ?? Kind;
    }

    /// <summary>Authorization policy refused the request.</summary>
    /// <param name="PolicyId">Identifier of the policy that denied access.</param>
    /// <param name="Resource">Optional resource the policy was evaluated against.</param>
    public sealed record Forbidden(string PolicyId, ResourceRef? Resource = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "forbidden";

        /// <inheritdoc />
        public override bool HasExplicitCode => true;

        /// <inheritdoc />
        public override string Code => PolicyId;

        /// <summary>
        /// Convenience factory that builds a <see cref="Forbidden"/> for <paramref name="policyId"/>
        /// against the resource type <typeparamref name="TResource"/> (its CLR name becomes the resource name).
        /// </summary>
        /// <typeparam name="TResource">The resource the policy was evaluated against.</typeparam>
        /// <param name="policyId">Identifier of the policy that denied access.</param>
        /// <param name="id">Optional identifier of the specific instance.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>A <see cref="Forbidden"/> carrying the policy and resource.</returns>
        public static Forbidden For<TResource>(string policyId, object? id = null, string? detail = null) =>
            new(policyId, ResourceRef.For<TResource>(id)) { Detail = detail };

        /// <summary>
        /// Convenience factory for a policy denial with no specific resource context.
        /// </summary>
        /// <param name="policyId">Identifier of the policy that denied access.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        /// <returns>A resourceless <see cref="Forbidden"/>.</returns>
        public static Forbidden ForPolicy(string policyId, string? detail = null) =>
            new(policyId) { Detail = detail };
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Capacity and availability
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>The caller has exceeded a usage quota; retry per <paramref name="Retry"/>.</summary>
    /// <param name="Retry">Optional retry hint describing when the caller may try again.</param>
    public sealed record RateLimited(RetryAdvice? Retry = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "rate-limited";

        /// <inheritdoc />
        public override bool HasExplicitCode => false;
    }

    /// <summary>
    /// The system is temporarily unable to complete the operation; the caller should retry
    /// per <paramref name="Retry"/>.
    /// </summary>
    /// <param name="ReasonCode">Optional machine-readable code identifying the kind of unavailability.</param>
    /// <param name="Retry">Optional retry hint describing when the caller may try again.</param>
    public sealed record Unavailable(string? ReasonCode = null, RetryAdvice? Retry = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "unavailable";

        /// <inheritdoc />
        public override bool HasExplicitCode => ReasonCode is not null;

        /// <inheritdoc />
        public override string Code => ReasonCode ?? Kind;
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Internal failures
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An unhandled internal failure occurred. <paramref name="ReasonCode"/> identifies the
    /// kind of failure; <paramref name="FaultId"/> optionally correlates to deeper diagnostics.
    /// </summary>
    /// <param name="ReasonCode">Stable machine-readable code identifying the kind of unexpected condition (e.g. <c>"unhandled-exception"</c>, <c>"default-initialized"</c>, <c>"not_implemented"</c>).</param>
    /// <param name="FaultId">Optional opaque per-incident identifier correlating to richer diagnostics in the logging/telemetry layer.</param>
    public sealed record Unexpected(string ReasonCode, string? FaultId = null) : Error
    {
        /// <inheritdoc />
        public override string Kind => "unexpected";

        /// <inheritdoc />
        public override bool HasExplicitCode => true;

        /// <inheritdoc />
        public override string Code => ReasonCode;
    }

    /// <summary>
    /// Opaque envelope for transport-specific lower-layer failure payloads produced outside
    /// <c>Trellis.Core</c>. Domain code does not inspect the payload; the boundary layer that
    /// understands the transport is responsible for translation. The wrapped payload must
    /// implement <see cref="ITransportFault"/>.
    /// </summary>
    /// <param name="Fault">Transport-layer fault payload defined in a transport-specific package.</param>
    public sealed record TransportFault(ITransportFault Fault) : Error
    {
        /// <inheritdoc />
        public override string Kind => "transport-fault";

        /// <inheritdoc />
        /// <remarks>
        /// True when the wrapped fault is an <see cref="ICodedTransportFault"/> and can say what its
        /// code is. A bare <see cref="ITransportFault"/> is opaque and contributes none.
        /// </remarks>
        public override bool HasExplicitCode => Fault is ICodedTransportFault;

        /// <inheritdoc />
        public override string Code => Fault is ICodedTransportFault coded ? coded.Code : Kind;

        /// <inheritdoc />
        /// <remarks>
        /// Overridden to skip normalization. A transport code comes from another system and is not a
        /// <see cref="ValidationCodes"/> entry — an upstream service that happens to send
        /// <c>validation.error</c> means its own thing by it, and rewriting that into Trellis's
        /// sentinel would misreport a foreign vocabulary as this one.
        /// </remarks>
        public override string WireCode =>
            Fault is ICodedTransportFault coded ? coded.Code : ValidationCodes.Unspecified;
    }

    // ───────────────────────────────────────────────────────────────────────────
    // Composition
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Composition of multiple independent errors. Used when several failures occur
    /// together (e.g. parallel operations, batch validation). Nested <see cref="Aggregate"/>
    /// values are flattened at construction. The constructor accepts at least one error.
    /// Boundary layers decide how to render the collection on their own wire.
    /// </summary>
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

        /// <inheritdoc />
        /// <remarks>Codes belong to the individual children, not to the aggregate.</remarks>
        public override bool HasExplicitCode => false;

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