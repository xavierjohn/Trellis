namespace Trellis;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// JSON converter for <see cref="RequiredEnum{TSelf}"/> types.
/// Serializes to the string value and deserializes from string value.
/// </summary>
/// <typeparam name="TRequiredEnum">The enum value object type to convert.</typeparam>
/// <example>
/// <code><![CDATA[
/// [JsonConverter(typeof(RequiredEnumJsonConverter<OrderState>))]
/// public partial class OrderState : RequiredEnum<OrderState>
/// {
///     public static readonly OrderState Draft = new();
///     public static readonly OrderState Confirmed = new();
/// }
/// 
/// // Serialization
/// var json = JsonSerializer.Serialize(OrderState.Draft);  // "Draft"
/// 
/// // Deserialization
/// var state = JsonSerializer.Deserialize<OrderState>("\"Draft\"");
/// ]]></code>
/// </example>
public sealed class RequiredEnumJsonConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TRequiredEnum> : ResultJsonConverter<TRequiredEnum>
    where TRequiredEnum : RequiredEnum<TRequiredEnum>, IScalarValue<TRequiredEnum, string>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override Result<TRequiredEnum> TryRead(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return Result.Fail<TRequiredEnum>(Invalid(
                ValidationCodes.ValueNotNull,
                $"Cannot deserialize null into RequiredEnum<{typeof(TRequiredEnum).Name}>. " +
                "A required enum value must be a non-null string."));

        if (reader.TokenType == JsonTokenType.String)
            return ReadFromString(ref reader);

        var tokenType = reader.TokenType;
        if (!reader.TrySkip())
            throw new JsonException("Incomplete JSON value.");
        return Result.Fail<TRequiredEnum>(Invalid(
            ValidationCodes.FormatConversion,
            $"Unexpected token type '{tokenType}' when parsing {typeof(TRequiredEnum).Name}. Expected String."));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TRequiredEnum value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }

    private static Result<TRequiredEnum> ReadFromString(ref Utf8JsonReader reader)
    {
        var name = reader.GetString();
        var result = TRequiredEnum.TryCreate(name);
        if (result.TryGetValue(out _, out var error))
            return result;

        var memberNames = RequiredEnum<TRequiredEnum>.GetAll()
            .Select(value => value.Value)
            .ToArray();
        var validValues = EnumMemberProse.ListOrNull(memberNames);

        var reasonCode = ReasonCodeOf(error, ValidationCodes.EnumNameUndefined);
        return Result.Fail<TRequiredEnum>(Invalid(
            reasonCode,
            $"Invalid {typeof(TRequiredEnum).Name} value: '{SanitizeForExceptionMessage(name)}'." +
            (validValues is null ? string.Empty : $" Valid values are: {validValues}."),
            reasonCode == ValidationCodes.EnumNameUndefined
                ? ValidationArgs.Allowed(memberNames)
                : null));
    }

    private static string ReasonCodeOf(Error error, string fallback) =>
        error is Error.InvalidInput { Fields.Length: > 0 } invalid ? invalid.Fields[0].ReasonCode : fallback;

    /// <summary>
    /// Builds a structured validation failure carrying the converter's own curated message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The violation is root-relative: this converter reads a single scalar and does not know
    /// where in the document it sits. The caller re-roots it.
    /// </para>
    /// <para>
    /// The message deliberately comes from this converter rather than from the producer's error
    /// detail. The invalid-value message is sanitized here — a raw enum name is attacker-supplied
    /// input — and propagating the producer's unsanitized detail in its place would silently
    /// remove that guarantee.
    /// </para>
    /// </remarks>
    private static Error.InvalidInput Invalid(
        string reasonCode,
        string message,
        ImmutableDictionary<string, ValidationArgValue>? args = null) =>
        Error.InvalidInput.ForField(field: InputPointer.Root, code: reasonCode, args: args, detail: message) with
        {
            Detail = message,
        };

    private static string SanitizeForExceptionMessage(string? value, int maxLength = 64)
    {
        if (string.IsNullOrEmpty(value))
            return "<empty>";

        var sanitized = new StringBuilder(maxLength + 3);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var encodedLength = char.IsControl(c) ? 6 : 1;
            if (sanitized.Length + encodedLength > maxLength)
            {
                sanitized.Append("...");
                return sanitized.ToString();
            }

            if (char.IsControl(c))
                sanitized.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            else
                sanitized.Append(c);
        }

        return sanitized.ToString();
    }
}