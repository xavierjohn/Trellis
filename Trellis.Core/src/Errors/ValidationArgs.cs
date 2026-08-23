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
/// The values are <see cref="ValidationArgValue"/> — a closed union of text, number, boolean, and
/// list —
/// rather than <see cref="object"/>. A number therefore reaches the wire as a number, so a client
/// can compare it without parsing, while the union still denies a producer the chance to hand over
/// an arbitrary object and let culture-sensitive formatting leak into the payload. The numeric
/// conversions are implicit, so <c>ValidationArgs.Of("max", 255)</c> needs no ceremony.
/// </para>
/// <para>
/// There is deliberately no <see cref="IFormattable"/> overload. Adding one would make
/// <c>ValidationArgs.Of("max", 255)</c> <em>ambiguous</em> — an <see cref="int"/> converts to
/// <see cref="IFormattable"/> by boxing and to <see cref="ValidationArgValue"/> by a user-defined
/// conversion, and neither target is better than the other — so every numeric call site would stop
/// compiling. Its absence is what lets the implicit conversions bind. A value with no numeric or
/// textual meaning of its own, such as a timestamp, must therefore be formatted explicitly and
/// invariantly by the caller.
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
    public static ImmutableDictionary<string, ValidationArgValue> Of(string name, ValidationArgValue value) =>
        ImmutableDictionary<string, ValidationArgValue>.Empty.Add(name, value);

    /// <summary>Builds a two-entry args dictionary.</summary>
    /// <param name="name1">The first operand name, in <c>camelCase</c>.</param>
    /// <param name="value1">The first operand value.</param>
    /// <param name="name2">The second operand name, in <c>camelCase</c>.</param>
    /// <param name="value2">The second operand value.</param>
    public static ImmutableDictionary<string, ValidationArgValue> Of(
        string name1,
        ValidationArgValue value1,
        string name2,
        ValidationArgValue value2) =>
        ImmutableDictionary<string, ValidationArgValue>.Empty.Add(name1, value1).Add(name2, value2);

    /// <summary>
    /// Builds an args dictionary of any size.
    /// </summary>
    /// <remarks>
    /// A rule with three or more operands is ordinary — a scale-and-precision failure carries four —
    /// and without this overload a producer that needed one had to abandon <see cref="ValidationArgs"/>
    /// and assemble the dictionary by hand.
    /// </remarks>
    /// <param name="pairs">The operand names, in <c>camelCase</c>, paired with their values.</param>
    public static ImmutableDictionary<string, ValidationArgValue> Of(
        params (string Name, ValidationArgValue Value)[] pairs)
    {
        if (pairs is null || pairs.Length == 0)
            return ImmutableDictionary<string, ValidationArgValue>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, ValidationArgValue>();
        foreach (var (name, value) in pairs)
            builder[name] = value;

        return builder.ToImmutable();
    }
}
