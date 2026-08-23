namespace Trellis;

using System.Collections.Immutable;

/// <summary>
/// Describes a global or multi-field business-rule failure attached to a
/// <see cref="Error.InvalidInput"/>. Use this for invariant violations that are
/// not bound to a single field (e.g. <c>"order.must-have-items"</c>,
/// <c>"password.mismatch"</c>, <c>"order.cancel-after-ship"</c>).
/// </summary>
/// <param name="ReasonCode">
/// Stable machine-readable code identifying the rule. The framework's own cross-field rules
/// use the <c>fields.*</c> namespace of <see cref="ValidationCodes"/> (e.g.
/// <see cref="ValidationCodes.FieldsMutuallyExclusive"/>); an application-specific rule code
/// should follow the same convention — lower-case, dot-separated namespaces with
/// <c>kebab-case</c> inside a segment.
/// </param>
/// <param name="Fields">
/// Optional pointers to fields involved in the rule (used to highlight related inputs
/// in a UI when no single field carries the violation).
/// </param>
/// <param name="Args">
/// Optional structured arguments for the renderer. Build them with <see cref="ValidationArgs"/>.
/// Compared by value contents.
/// </param>
/// <param name="Detail">
/// Optional caller-supplied detail string. When non-null the boundary renderer prefers
/// this over the default template for <see cref="ReasonCode"/>.
/// </param>
public sealed record RuleViolation(
    string ReasonCode,
    EquatableArray<InputPointer> Fields = default,
    ImmutableDictionary<string, ValidationArgValue>? Args = null,
    string? Detail = null)
{
    /// <summary>
    /// Stable machine-readable code identifying the rule that was violated.
    /// </summary>
    /// <remarks>
    /// The initializer is the counting site for <see cref="ValidationMetrics"/>, for the reasons
    /// given on <see cref="FieldViolation.ReasonCode"/>.
    /// </remarks>
    public string ReasonCode { get; init; } = ValidationMetrics.Observe(ReasonCode, "rule");

    /// <inheritdoc />
    public bool Equals(RuleViolation? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(ReasonCode, other.ReasonCode, StringComparison.Ordinal)
            && Fields.Equals(other.Fields)
            && string.Equals(Detail, other.Detail, StringComparison.Ordinal)
            && FieldViolation.DictionaryEquals(Args, other.Args);
    }

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(ReasonCode, Fields, Detail, FieldViolation.ArgsHash(Args));
}