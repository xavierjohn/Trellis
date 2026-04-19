namespace Trellis;

using System.Diagnostics.CodeAnalysis;
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
            onFailure: error => throw new JsonException($"Invalid {typeof(TRequiredEnum).Name} value: '{name}'. {error.Detail}"));
    }
}