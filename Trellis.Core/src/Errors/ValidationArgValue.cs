namespace Trellis;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// The value of a single entry in the <c>Args</c> carried by a <see cref="FieldViolation"/> or
/// <see cref="RuleViolation"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a closed union — <see cref="Text"/>, <see cref="Number"/>, or <see cref="List"/> — and
/// the constructor is private so it stays closed. A client can therefore switch over it
/// exhaustively, and the JSON shape of an arg is knowable from the type alone rather than from
/// whatever a producer happened to pass.
/// </para>
/// <para>
/// The predecessor of this type was <see cref="string"/>, which forced every operand onto the wire
/// quoted: a length bound arrived as <c>"maxLength": "50"</c>, and a client that wanted to compare
/// it against a length had to parse it back out, guessing at the format. Modelling the number as a
/// number removes the guess — <c>"maxLength": 50</c> — while keeping the property that made
/// <see cref="string"/> attractive in the first place, which is that no producer-side culture can
/// leak into the payload. <see cref="Number"/> holds a <see cref="decimal"/> and is written by
/// <see cref="System.Text.Json"/> invariantly, so a German server and an American one emit the same
/// bytes.
/// </para>
/// <para>
/// Args are still operands, not an echo of the caller's input. Never put the rejected value itself
/// in here — a rejected password or token would then be reflected back in an error response and
/// into whatever logs that response.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// ValidationArgs.Of("maxLength", 50)                     // {"maxLength": 50}
/// ValidationArgs.Of("min", 0, "max", 255)                // {"min": 0, "max": 255}
/// ValidationArgs.Of("allowed", ValidationArgValue.ListOf("red", "green"))
/// </code>
/// </example>
[JsonConverter(typeof(ValidationArgValueJsonConverter))]
public abstract record ValidationArgValue
{
    private ValidationArgValue()
    {
    }

    /// <summary>A textual operand, written to JSON as a string.</summary>
    /// <param name="Value">The text.</param>
    public sealed record Text(string Value) : ValidationArgValue;

    /// <summary>
    /// A numeric operand, written to JSON as a number.
    /// </summary>
    /// <remarks>
    /// <see cref="decimal"/> backs every numeric operand rather than a case per CLR numeric type,
    /// because JSON has a single number type and a client could not observe the distinction anyway.
    /// It is the widest choice that keeps integers exact, which a <see cref="double"/> would not.
    /// </remarks>
    /// <param name="Value">The number.</param>
    public sealed record Number(decimal Value) : ValidationArgValue;

    /// <summary>
    /// An ordered list of operands, written to JSON as an array.
    /// </summary>
    /// <remarks>
    /// This is what lets a violation name a set — the permitted members behind an
    /// <c>enum.name-undefined</c>, say — without a producer inventing a delimiter that a client
    /// would then have to know to split on.
    /// </remarks>
    /// <param name="Items">The list members, in order.</param>
    public sealed record List(EquatableArray<ValidationArgValue> Items) : ValidationArgValue;

    /// <summary>Builds a <see cref="List"/> from the supplied items.</summary>
    /// <param name="items">The list members, in order. May be empty.</param>
    public static ValidationArgValue ListOf(params ValidationArgValue[] items) =>
        new List(EquatableArray.Create(items ?? []));

    /// <summary>Builds a <see cref="List"/> from a sequence.</summary>
    /// <param name="items">The list members, in order.</param>
    public static ValidationArgValue ListFrom(IEnumerable<ValidationArgValue> items) =>
        new List(EquatableArray.From(items));

    /// <summary>Wraps a string as <see cref="Text"/>.</summary>
    /// <param name="value">The text.</param>
    public static implicit operator ValidationArgValue(string value) => new Text(value);

    /// <summary>Wraps an integer as <see cref="Number"/>.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator ValidationArgValue(int value) => new Number(value);

    /// <summary>Wraps a long as <see cref="Number"/>.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator ValidationArgValue(long value) => new Number(value);

    /// <summary>Wraps a decimal as <see cref="Number"/>.</summary>
    /// <param name="value">The number.</param>
    public static implicit operator ValidationArgValue(decimal value) => new Number(value);
}

/// <summary>
/// Writes a <see cref="ValidationArgValue"/> as its underlying JSON primitive, and reads one back.
/// </summary>
/// <remarks>
/// This is public because the <see cref="System.Text.Json"/> source generator requires an
/// accessible converter type: a trimmed or AOT-published application whose
/// <see cref="JsonSerializerContext"/> includes a violation payload cannot resolve an internal one,
/// and fails to generate metadata for it at compile time.
/// </remarks>
public sealed class ValidationArgValueJsonConverter : JsonConverter<ValidationArgValue>
{
    /// <inheritdoc />
    public override ValidationArgValue? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        ReadValue(ref reader);

    private static ValidationArgValue ReadValue(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.String => new ValidationArgValue.Text(reader.GetString()!),
        JsonTokenType.Number => ReadNumber(ref reader),
        JsonTokenType.StartArray => ReadList(ref reader),
        _ => throw new JsonException(
            $"A validation arg must be a string, a number, or an array of those; found {reader.TokenType}."),
    };

    /// <summary>
    /// Reads a numeric arg, rejecting any value a <see cref="decimal"/> cannot carry faithfully.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Utf8JsonReader.GetDecimal"/> alone is not safe here. It throws on a magnitude
    /// beyond <see cref="decimal"/>'s range — a <see cref="FormatException"/>, which is not the
    /// exception a <see cref="JsonConverter{T}"/> is expected to surface — and it silently
    /// <em>rounds</em> anything it cannot hold exactly: <c>1E-100</c> becomes <c>0</c>, and
    /// <c>0.00000000000000000000000000009</c> becomes <c>0.0000000000000000000000000001</c>.
    /// </para>
    /// <para>
    /// An arg is an operand a client renders a message from, so a rounded value states a bound the
    /// producer never wrote. Every such token is refused rather than rounded. Refusal is decided by
    /// comparing significant digits, not by comparing text, so the ordinary spellings a non-.NET
    /// producer may use — <c>1e2</c>, <c>1.50</c>, <c>-0</c> — are all accepted.
    /// </para>
    /// </remarks>
    private static ValidationArgValue.Number ReadNumber(ref Utf8JsonReader reader)
    {
        if (!reader.TryGetDecimal(out var number))
            throw new JsonException("A numeric validation arg is out of range for a decimal.");

        if (!RepresentsExactly(number, RawToken(ref reader)))
            throw new JsonException(
                "A numeric validation arg carries more precision than a decimal can represent.");

        return new ValidationArgValue.Number(number);
    }

    private static string RawToken(ref Utf8JsonReader reader) =>
        Encoding.UTF8.GetString(reader.HasValueSequence
            ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence)
            : reader.ValueSpan);

    /// <summary>
    /// True when <paramref name="number"/> carries exactly the value <paramref name="token"/> spells.
    /// </summary>
    private static bool RepresentsExactly(decimal number, string token) =>
        Significand(token) == Significand(number.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// Reduces a JSON number to the canonical form <c>sign, digits, exponent</c>, where
    /// <c>digits</c> carries no leading or trailing zero. Two spellings of the same value — and only
    /// those — share a form, so <c>1e2</c> and <c>100</c> agree while <c>1E-100</c> and <c>0</c>
    /// do not.
    /// </summary>
    private static (bool Negative, string Digits, int Exponent) Significand(string token)
    {
        var body = token;
        var negative = body.StartsWith('-');
        if (negative || body.StartsWith('+'))
            body = body[1..];

        var exponent = 0;
        var marker = body.IndexOfAny(['e', 'E']);
        if (marker >= 0)
        {
            exponent = int.Parse(body[(marker + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            body = body[..marker];
        }

        var point = body.IndexOf('.');
        if (point >= 0)
        {
            exponent -= body.Length - point - 1;
            body = string.Concat(body[..point], body[(point + 1)..]);
        }

        var digits = body.TrimStart('0');
        var trimmed = digits.TrimEnd('0');
        exponent += digits.Length - trimmed.Length;

        // Zero has one canonical form; -0, 0.0 and 0e5 must not read as three different values.
        return trimmed.Length == 0 ? (false, string.Empty, 0) : (negative, trimmed, exponent);
    }

    private static ValidationArgValue.List ReadList(ref Utf8JsonReader reader)
    {
        var items = ImmutableArray.CreateBuilder<ValidationArgValue>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            items.Add(ReadValue(ref reader));

        return new ValidationArgValue.List(new EquatableArray<ValidationArgValue>(items.ToImmutable()));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ValidationArgValue value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case ValidationArgValue.Text text:
                writer.WriteStringValue(text.Value);
                break;
            case ValidationArgValue.Number number:
                writer.WriteNumberValue(number.Value);
                break;
            case ValidationArgValue.List list:
                writer.WriteStartArray();
                foreach (var item in list.Items)
                    Write(writer, item, options);
                writer.WriteEndArray();
                break;
            default:
                throw new JsonException($"Unsupported validation arg value: {value.GetType().Name}.");
        }
    }
}
