namespace Trellis;

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
public sealed class RequiredEnumJsonConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TRequiredEnum> : JsonConverter<TRequiredEnum>
    where TRequiredEnum : RequiredEnum<TRequiredEnum>, IScalarValue<TRequiredEnum, string>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <summary>
    /// The placeholder reason code carried by this converter's violations; normalized to a
    /// neutral sentinel at the boundary.
    /// </summary>
    private const string LegacyUnspecifiedCode = "validation.error";

    /// <inheritdoc />
    public override TRequiredEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            throw Invalid(
                $"Cannot deserialize null into RequiredEnum<{typeof(TRequiredEnum).Name}>. " +
                "A required enum value must be a non-null string.");

        return reader.TokenType switch
        {
            JsonTokenType.String => ReadFromString(ref reader),
            _ => throw Invalid($"Unexpected token type '{reader.TokenType}' when parsing {typeof(TRequiredEnum).Name}. Expected String.")
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TRequiredEnum value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.Value);
    }

    private static TRequiredEnum ReadFromString(ref Utf8JsonReader reader)
    {
        var name = reader.GetString();
        return TRequiredEnum.TryCreate(name).Match(
            onSuccess: value => value,
            onFailure: _ =>
            {
                var validValues = string.Join(", ", RequiredEnum<TRequiredEnum>.GetAll()
                    .Select(value => value.Value)
                    .OrderBy(value => value, StringComparer.Ordinal));

                throw Invalid(
                    $"Invalid {typeof(TRequiredEnum).Name} value: '{SanitizeForExceptionMessage(name)}'. " +
                    $"Valid values are: {validValues}.");
            });
    }

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
    /// <para>
    /// This also moves these failures from 400 to 422. That is a correction: the boundary reserves
    /// 400 for bytes that are not valid JSON, and an enum name that is not a member parsed
    /// perfectly well and then failed semantic validation. The identical failure already returns
    /// 422 through the composite converter, so the two producers now agree.
    /// </para>
    /// </remarks>
    private static TrellisJsonValidationException Invalid(string message) =>
        new(message)
        {
            InvalidInput = Error.InvalidInput.ForField(InputPointer.Root, LegacyUnspecifiedCode, message) with
            {
                Detail = message,
            },
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