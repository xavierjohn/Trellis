namespace Trellis;

using System.Collections.Immutable;

/// <summary>
/// Describes a validation failure attached to a specific field of an input document.
/// Used inside <see cref="Error.UnprocessableContent"/>.
/// </summary>
/// <param name="Field">JSON Pointer locating the offending field.</param>
/// <param name="ReasonCode">
/// Stable machine-readable code identifying the rule that was violated
/// (e.g. <c>"required"</c>, <c>"length_out_of_range"</c>, <c>"invalid_format"</c>).
/// </param>
/// <param name="Args">
/// Optional structured arguments for the renderer (e.g. <c>{ "min": "3", "max": "50" }</c>
/// for a length-range violation). Compared by value contents.
/// </param>
/// <param name="Detail">
/// Optional caller-supplied detail string. When non-null the boundary renderer prefers
/// this over the default template for <see cref="ReasonCode"/>.
/// </param>
public sealed record FieldViolation(
    InputPointer Field,
    string ReasonCode,
    ImmutableDictionary<string, string>? Args = null,
    string? Detail = null)
{
    /// <inheritdoc />
    public bool Equals(FieldViolation? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Field.Equals(other.Field)
            && string.Equals(ReasonCode, other.ReasonCode, StringComparison.Ordinal)
            && string.Equals(Detail, other.Detail, StringComparison.Ordinal)
            && DictionaryEquals(Args, other.Args);
    }

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Field, ReasonCode, Detail, ArgsHash(Args));

    internal static bool DictionaryEquals(ImmutableDictionary<string, string>? a, ImmutableDictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        var ca = a?.Count ?? 0;
        var cb = b?.Count ?? 0;
        if (ca != cb) return false;
        if (ca == 0) return true;
        foreach (var kv in a!)
            if (!b!.TryGetValue(kv.Key, out var v) || !string.Equals(v, kv.Value, StringComparison.Ordinal))
                return false;
        return true;
    }

    internal static int ArgsHash(ImmutableDictionary<string, string>? p)
    {
        if (p is null || p.Count == 0) return 0;
        var hc = 0;
        foreach (var kv in p)
            hc ^= HashCode.Combine(kv.Key, kv.Value);
        return hc;
    }
}
