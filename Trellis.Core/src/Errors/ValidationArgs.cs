namespace Trellis;

using System.Collections.Immutable;

/// <summary>
/// Builds the <c>Args</c> dictionary carried by a <see cref="FieldViolation"/> or
/// <see cref="RuleViolation"/>.
/// </summary>
/// <remarks>
/// <para>
/// Args are the machine-readable operands of a violation — the <c>50</c> in "must be at most 50",
/// the <c>0</c> and <c>255</c> in "must be between 0 and 255". A client that has them can render its
/// own localized message; a client that has only the English detail string cannot.
/// </para>
/// <para>
/// The values are <see cref="string"/> rather than <see cref="object"/> deliberately: they cross a
/// JSON boundary, and letting a producer hand over an arbitrary object invites culture-sensitive
/// formatting to leak into the wire. Callers format with
/// <see cref="System.Globalization.CultureInfo.InvariantCulture"/> — see <see cref="Of(string, IFormattable)"/>,
/// which does it for them.
/// </para>
/// <para>
/// Args are for operands, not for echoing what the caller sent. Never put the rejected value itself
/// in here — a rejected password or token would then be reflected back in an error response and into
/// whatever logs that response.
/// </para>
/// </remarks>
public static class ValidationArgs
{
    /// <summary>Builds a single-entry args dictionary.</summary>
    /// <param name="name">The operand name, in <c>camelCase</c>.</param>
    /// <param name="value">The operand value.</param>
    public static ImmutableDictionary<string, string> Of(string name, string value) =>
        ImmutableDictionary<string, string>.Empty.Add(name, value);

    /// <summary>
    /// Builds a single-entry args dictionary, formatting <paramref name="value"/> with the invariant
    /// culture so the wire representation does not vary by server locale.
    /// </summary>
    /// <param name="name">The operand name, in <c>camelCase</c>.</param>
    /// <param name="value">The operand value.</param>
    public static ImmutableDictionary<string, string> Of(string name, IFormattable value) =>
        Of(name, value.ToString(null, System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Builds a two-entry args dictionary.</summary>
    public static ImmutableDictionary<string, string> Of(string name1, string value1, string name2, string value2) =>
        ImmutableDictionary<string, string>.Empty.Add(name1, value1).Add(name2, value2);

    /// <summary>
    /// Builds a two-entry args dictionary, formatting both values with the invariant culture.
    /// </summary>
    public static ImmutableDictionary<string, string> Of(string name1, IFormattable value1, string name2, IFormattable value2) =>
        Of(
            name1,
            value1.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            name2,
            value2.ToString(null, System.Globalization.CultureInfo.InvariantCulture));
}
