namespace Trellis.FluentValidation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using global::FluentValidation;
using global::FluentValidation.Results;

/// <summary>
/// Projects a <see cref="ValidationFailure"/>'s placeholder values onto the structured
/// <c>Args</c> a client can use to render its own localized message.
/// </summary>
/// <remarks>
/// <para>
/// Blanket pass-through is unsafe on two independent counts, so two controls apply.
/// </para>
/// <para>
/// <b>Correctness.</b> FluentValidation populates placeholders its message never uses, and it
/// populates them with sentinels: a <c>MinimumLength(50)</c> failure carries
/// <c>MaxLength = -1</c>, and a <c>MaximumLength(2)</c> failure carries <c>MinLength = 0</c>. A
/// client rendering "must be between 50 and -1 characters" from those is a real bug, so the
/// allowlist is per validator.
/// </para>
/// <para>
/// <b>Disclosure.</b> An arg can carry submitted input — <c>comparisonValue</c> does whenever the
/// rule compares against another property — and no structural rule distinguishes the safe case
/// from the unsafe one, because FluentValidation reports <c>ComparisonProperty</c> only for a
/// simple member expression. So the control is not a discriminator but the containment gate in
/// <see cref="ShouldEmit"/>.
/// </para>
/// </remarks>
public static class ValidationArgsProjection
{
    /// <summary>
    /// Placeholders never emitted, whatever the validator and whoever authored them.
    /// </summary>
    /// <remarks>
    /// <c>PropertyValue</c> holds the user's submitted input and <em>is</em> rendered into some
    /// default messages, so containment alone would let it through; reflecting submitted input
    /// back into an error payload is a disclosure and PII hazard. <c>PropertyName</c> is dropped
    /// as redundant with the violation's own location. <c>CollectionIndex</c> is deliberately kept.
    /// </remarks>
    private static readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase)
    {
        "PropertyValue",
        "PropertyPath",
        "PropertyName",
    };

    /// <summary>
    /// Per-validator allowlist, keyed by FluentValidation's error code.
    /// </summary>
    /// <remarks>
    /// <c>ExactLengthValidator</c> allows <c>MaxLength</c> and <em>not</em> <c>MinLength</c>, which
    /// looks arbitrary because <c>ExactLengthValidator(n)</c> calls <c>base(n, n)</c> and so
    /// populates both with the same correct value. The allowlist does not act alone: it composes
    /// with <see cref="ShouldEmit"/>, which keeps an arg only when the active template names it.
    /// That template names <c>{MaxLength}</c>. Allowlisting <c>MinLength</c> instead would gate it
    /// out for being absent from the template while <c>MaxLength</c> was dropped for not being
    /// allowlisted, and the expected length would vanish from the wire entirely — leaving a client
    /// with the length it sent and no bound to compare it against.
    /// </remarks>
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["LengthValidator"] = ["MinLength", "MaxLength", "TotalLength"],
        ["MinimumLengthValidator"] = ["MinLength", "TotalLength"],
        ["MaximumLengthValidator"] = ["MaxLength", "TotalLength"],
        ["ExactLengthValidator"] = ["MaxLength", "TotalLength"],
        ["EqualValidator"] = ["ComparisonValue", "ComparisonProperty"],
        ["NotEqualValidator"] = ["ComparisonValue", "ComparisonProperty"],
        ["LessThanValidator"] = ["ComparisonValue", "ComparisonProperty"],
        ["LessThanOrEqualValidator"] = ["ComparisonValue", "ComparisonProperty"],
        ["GreaterThanValidator"] = ["ComparisonValue", "ComparisonProperty"],
        ["GreaterThanOrEqualValidator"] = ["ComparisonValue", "ComparisonProperty"],
        ["InclusiveBetweenValidator"] = ["From", "To"],
        ["ExclusiveBetweenValidator"] = ["From", "To"],
        ["ScalePrecisionValidator"] = ["ExpectedPrecision", "ExpectedScale", "ActualScale", "Digits"],
        ["RegularExpressionValidator"] = ["RegularExpression"],
    };

    private const int MaxStringLength = 64;

    /// <summary>
    /// Combines the framework allowlist with the application's widening for one error code.
    /// </summary>
    /// <remarks>
    /// An application may widen an error code the framework does not allowlist at all, which is the
    /// point: <c>PredicateValidator</c> has no default entry because Trellis cannot know what a
    /// <c>Must()</c> rule's placeholders mean, and the application does.
    /// </remarks>
    private static (IReadOnlyCollection<string> Names, HashSet<string>? OptedIn) ResolveAllowed(
        string errorCode,
        ValidationArgsOptions? options)
    {
        IReadOnlyCollection<string> defaults = Allowed.TryGetValue(errorCode, out var known) ? known : [];
        if (options is null || options.IsEmpty)
            return (defaults, null);

        var extra = options.AdditionalFor(errorCode);
        if (extra.Count == 0)
            return (defaults, null);

        var combined = new HashSet<string>(defaults, StringComparer.Ordinal);
        var optedIn = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in extra)
        {
            combined.Add(name);
            optedIn.Add(name);
        }

        return (combined, optedIn);
    }

    /// <summary>
    /// Builds the args for a failure, or <see langword="null"/> when none survive.
    /// </summary>
    /// <param name="failure">The FluentValidation failure to project.</param>
    /// <param name="options">
    /// The application's widening of the default allowlist, or <see langword="null"/> to apply the
    /// framework default alone.
    /// </param>
    public static ImmutableDictionary<string, ValidationArgValue>? Project(
        ValidationFailure failure,
        ValidationArgsOptions? options = null)
    {
        var placeholders = failure.FormattedMessagePlaceholderValues;
        if (placeholders is null || placeholders.Count == 0)
            return null;

        if (string.IsNullOrEmpty(failure.ErrorCode))
            return null;

        var (allowed, optedIn) = ResolveAllowed(failure.ErrorCode, options);
        if (allowed.Count == 0)
            return null;

        var template = ResolveTemplate(failure.ErrorCode);
        ImmutableDictionary<string, ValidationArgValue>.Builder? builder = null;

        foreach (var name in allowed)
        {
            if (Denied.Contains(name))
                continue;

            if (!placeholders.TryGetValue(name, out var raw) || raw is null)
                continue;

            var rendered = Convert.ToString(raw, CultureInfo.CurrentCulture);
            if (!ShouldEmit(rendered, template, name, failure.ErrorMessage, optedIn?.Contains(name) == true))
                continue;

            var encoded = Encode(raw);
            if (encoded is null)
                continue;

            if (!IsReconciled(encoded, rendered, failure.ErrorMessage))
                continue;

            (builder ??= ImmutableDictionary.CreateBuilder<string, ValidationArgValue>(StringComparer.Ordinal))
                [ToCamelCase(name)] = Lift(raw, encoded);
        }

        return builder?.ToImmutable();
    }

    /// <summary>
    /// Lifts the gated encoding onto the wire union without revisiting the gate's decision.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The gate above runs on strings and must keep doing so: it decides by comparing against the
    /// message FluentValidation rendered, and that message is text. Nothing here can admit an arg
    /// the gate rejected, because this runs only on values that already passed it.
    /// </para>
    /// <para>
    /// What changes is representation, and only for a value whose CLR type is numeric to begin
    /// with — so a threshold reaches the client as <c>50</c> rather than <c>"50"</c>, and a client
    /// comparing it against a length no longer has to parse it back out.
    /// </para>
    /// <para>
    /// The lift is applied only when the decimal round-trips to the very text the gate approved.
    /// That is stricter than "parses", deliberately: <c>decimal.TryParse</c> <em>succeeds</em> on
    /// <c>1E-100</c> and yields zero, so a bound the client was shown as <c>1E-100</c> would be
    /// published as <c>0</c> — an operand no producer wrote, and one the gate never saw. Requiring
    /// the round-trip also disposes of the overflow case, where the parse simply fails. Such a
    /// value stays text, which is exactly what it was before this union existed.
    /// </para>
    /// </remarks>
    private static ValidationArgValue Lift(object raw, string encoded) =>
        IsNumeric(raw)
        && decimal.TryParse(encoded, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && string.Equals(number.ToString(CultureInfo.InvariantCulture), encoded, StringComparison.Ordinal)
            ? new ValidationArgValue.Number(number)
            : new ValidationArgValue.Text(encoded);

    /// <summary>
    /// True for the CLR numeric types, which are the only ones whose args become JSON numbers.
    /// </summary>
    /// <remarks>
    /// An <see cref="Enum"/> is deliberately excluded despite its numeric backing: it is encoded by
    /// name, and a client matching on the name would be handed an ordinal it cannot interpret.
    /// </remarks>
    private static bool IsNumeric(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or decimal or float or double;

    /// <summary>
    /// The containment gate: an arg is emitted only when the active message template named that
    /// placeholder <em>and</em> its rendered value already appears in the message the client
    /// receives anyway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves are required, and each rules out a failure the other admits.
    /// </para>
    /// <para>
    /// Without the template check, plain containment is fooled by coincidence:
    /// <c>Matches("A")</c> on a property named <c>A</c> renders "'A' is not in the correct format",
    /// in which the regex <c>A</c> is trivially a substring. Emitting it would disclose an internal
    /// format an attacker would otherwise have to guess — and the collision gets likelier the
    /// shorter the arg, which is to say likeliest for numeric thresholds.
    /// </para>
    /// <para>
    /// Without the message check, the template check alone still passes after an application has
    /// replaced the message: <c>.WithMessage("bad")</c> leaves the default template — and its
    /// <c>{MinLength}</c> — untouched. An application that overrode the message deliberately took
    /// the value out of its prose, and Trellis must not put it back.
    /// </para>
    /// <para>
    /// The value is compared as <em>FluentValidation</em> rendered it, because that is what the
    /// message contains. What Trellis puts on the wire is a different string — see
    /// <see cref="IsReconciled"/>, which closes the gap the two representations open.
    /// </para>
    /// <para>
    /// An explicit opt-in through <see cref="ValidationArgsOptions"/> satisfies the template half
    /// without consulting the template. The template check defends against a placeholder Trellis
    /// <em>guessed</em> was safe, which is why coincidence can fool it; an application naming its
    /// own validator's placeholder is not guessing, and a custom validator has no entry in the
    /// language manager for the check to consult in the first place — so leaving it in force would
    /// make the opt-in inert, which is the same as not shipping it. The message half still holds,
    /// so an opted-in arg still cannot carry anything the client's own message did not.
    /// </para>
    /// </remarks>
    private static bool ShouldEmit(string? rendered, string template, string name, string message, bool optedIn)
    {
        if (!optedIn && !TemplateNamesPlaceholder(template, name))
            return false;

        return !string.IsNullOrEmpty(rendered)
            && message.Contains(rendered, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requires the value Trellis actually emits to be present in the message too, whenever it
    /// differs from the form FluentValidation rendered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, the gate is checked against one string and a different one is published. A
    /// temporal value is the clear case: FluentValidation renders a <see cref="DateTime"/>
    /// culture-sensitively and drops the fractional part, while <see cref="Encode"/> emits the
    /// round-trip <c>"O"</c> form. The gate would pass on <c>1/2/2026 3:04:05 AM</c> and the wire
    /// would carry <c>2026-01-02T03:04:05.1234567</c> — sub-second precision the client's message
    /// never contained, which is exactly the new disclosure the gate exists to prevent.
    /// </para>
    /// <para>
    /// In practice this makes temporal args a standing false negative, since the two renderings
    /// essentially never coincide. That is the intended direction: a false negative hides a safe
    /// arg and stays recoverable through an explicit opt-in, whereas a false positive discloses
    /// and cannot be taken back. Values whose encoding already matches what was rendered —
    /// numerics and booleans — are unaffected.
    /// </para>
    /// <para>
    /// Bounding and escaping are explicitly reconciled rather than treated as a mismatch.
    /// <see cref="Sanitize"/> derives every character it emits from a character of the value the
    /// gate already accepted — truncation omits, escaping re-encodes — so it can never introduce
    /// content the message lacked, even though its output is not byte-for-byte present there.
    /// A <c>\u0000</c> escape stands for a NUL the client already received, and a trailing
    /// <c>...</c> marks an omission rather than revealing one. Demanding verbatim presence would
    /// suppress exactly the long and control-bearing values the bound exists to serve.
    /// </para>
    /// </remarks>
    private static bool IsReconciled(string encoded, string? rendered, string message) =>
        string.Equals(encoded, rendered, StringComparison.Ordinal)
        || string.Equals(encoded, Sanitize(rendered), StringComparison.Ordinal)
        || message.Contains(encoded, StringComparison.Ordinal);

    /// <summary>
    /// Resolves the culture-active template for an error code.
    /// </summary>
    /// <remarks>
    /// An unrecognized or application-supplied error code yields <em>no</em> template — an empty
    /// string rather than null — which makes every placeholder fail the gate. That is fail-safe and
    /// consistent with a user-set error code always winning: the code is preserved, the args are
    /// not guessed at. Because this returns the culture-active template, the gate holds under
    /// localization with no special handling.
    /// </remarks>
    private static string ResolveTemplate(string errorCode)
    {
        try
        {
            return ValidatorOptions.Global.LanguageManager.GetString(errorCode) ?? string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// True when <paramref name="template"/> contains the placeholder, allowing for a format
    /// specifier (<c>{Digits:N0}</c>) or alignment (<c>{Digits,5}</c>).
    /// </summary>
    private static bool TemplateNamesPlaceholder(string template, string name)
    {
        var index = 0;
        while ((index = template.IndexOf('{', index)) >= 0)
        {
            var start = index + 1;
            if (string.CompareOrdinal(template, start, name, 0, name.Length) == 0)
            {
                var after = start + name.Length;
                if (after < template.Length && template[after] is '}' or ':' or ',')
                    return true;
            }

            index = start;
        }

        return false;
    }

    /// <summary>
    /// Encodes a placeholder value for the wire.
    /// </summary>
    /// <remarks>
    /// Per-type encoding is implementable only because placeholders arrive boxed rather than
    /// pre-stringified. Naive conversion is not acceptable: <c>Convert.ToString</c> on a
    /// <see cref="DateTime"/> under the invariant culture yields a month-first US format, not
    /// ISO 8601, which a client cannot parse portably.
    /// </remarks>
    private static string? Encode(object value) => value switch
    {
        string s => Sanitize(s),
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("O", CultureInfo.InvariantCulture),
        TimeOnly t => t.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan t => t.ToString("c", CultureInfo.InvariantCulture),
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Sanitize(value.ToString()),
    };

    /// <summary>
    /// Bounds a string value and escapes control characters.
    /// </summary>
    /// <remarks>
    /// The bound is universal rather than targeted because no structural rule identifies which
    /// string args can carry submitted input: <c>Equal(x =&gt; x.Other + "!")</c> carries the full
    /// submitted value with an empty <c>ComparisonProperty</c>, byte-for-byte indistinguishable
    /// from a safe literal comparison.
    /// </remarks>
    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var builder = new StringBuilder(MaxStringLength + 3);
        foreach (var c in value)
        {
            var encodedLength = char.IsControl(c) ? 6 : 1;
            if (builder.Length + encodedLength > MaxStringLength)
            {
                builder.Append("...");
                return builder.ToString();
            }

            if (char.IsControl(c))
                builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else
                builder.Append(c);
        }

        return builder.ToString();
    }

    private static string ToCamelCase(string name) =>
        name.Length > 0 && char.IsUpper(name[0])
            ? string.Concat(char.ToLowerInvariant(name[0]).ToString(), name.AsSpan(1))
            : name;
}
