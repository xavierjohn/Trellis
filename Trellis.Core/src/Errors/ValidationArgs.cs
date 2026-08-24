namespace Trellis;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

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

    /// <summary>
    /// The largest member list <see cref="Allowed"/> will publish.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A list a client cannot act on is not worth what it costs to send. The 248 ISO country names
    /// serialize to roughly 3 KB, attached to <em>every</em> rejection; a request carrying several
    /// invalid enum fields multiplies that, so a small request provokes a large response, which is
    /// an amplification vector rather than mere waste. Past a few dozen options a client is not
    /// rendering "choose one of…" from an error payload anyway — it wants a schema or an
    /// enumeration endpoint, and the error is the wrong channel.
    /// </para>
    /// <para>
    /// The bound is a member count rather than a serialized length because member names are
    /// identifiers in every producer — CLR enum names, or the static field names behind a
    /// <c>RequiredEnum</c> — so their length is already bounded in practice, and a count is
    /// something a client can be told and can predict.
    /// </para>
    /// </remarks>
    public const int MaxAllowedMembers = 64;

    /// <summary>
    /// Builds the <c>allowed</c> entry naming the members a symbolic value may take.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every producer that rejects a value for not being one of a fixed set routes through here,
    /// so none of them can disagree about the entry's name, the order of its members, or the point
    /// at which the list becomes too long to send. The producers derive their members from
    /// unrelated places — <see cref="System.Enum.GetNames(System.Type)"/> for query binding, a
    /// registry of declared statics for <c>RequiredEnum</c> — and nothing else would force those
    /// to line up. A client that compares the list across producers, or caches it, must not see a
    /// difference that is only ordering.
    /// </para>
    /// <para>
    /// Ordinal sorting is what makes the order total and culture-independent; sorting by the
    /// current culture would let the same enum serialize differently on two machines.
    /// </para>
    /// <para>
    /// Beyond <see cref="MaxAllowedMembers"/> the list is dropped <em>whole</em> and replaced by an
    /// <c>allowedCount</c> entry. Truncating instead would publish a false statement: a client
    /// cannot tell a shortened list from a complete one, so it would render "choose one of…" over a
    /// wrong set and reject valid input when validating against it. Omission is already a case
    /// every client handles, since a blank value carries no list either; the count is what
    /// distinguishes "too many to send" from "not applicable here".
    /// </para>
    /// </remarks>
    /// <param name="names">The permitted member names. May be empty.</param>
    public static ImmutableDictionary<string, ValidationArgValue> Allowed(IEnumerable<string> names)
    {
        var members = (names ?? []).ToArray();
        if (members.Length > MaxAllowedMembers)
            return Of("allowedCount", members.Length);

        Array.Sort(members, StringComparer.Ordinal);
        return Of(
            "allowed",
            ValidationArgValue.ListFrom(members.Select(name => (ValidationArgValue)new ValidationArgValue.Text(name))));
    }
}
