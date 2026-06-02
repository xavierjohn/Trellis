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
    public override TRequiredEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => ReadFromString(ref reader),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token type '{reader.TokenType}' when parsing {typeof(TRequiredEnum).Name}. Expected String.")
        };

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
        return RequiredEnum<TRequiredEnum>.TryFromName(name).Match(
            onSuccess: value => value,
            onFailure: _ =>
            {
                var validValues = string.Join(", ", RequiredEnum<TRequiredEnum>.GetAll()
                    .Select(value => value.Value)
                    .OrderBy(value => value, StringComparer.Ordinal));

                throw new JsonException(
                    $"Invalid {typeof(TRequiredEnum).Name} value: '{SanitizeForExceptionMessage(name)}'. " +
                    $"Valid values are: {validValues}.");
            });
    }

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