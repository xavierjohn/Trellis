namespace Trellis;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Converts value objects that implement <see cref="IParsable{TSelf}"/> to and from JSON.
/// </summary>
/// <typeparam name="T">The value-object type to convert.</typeparam>
/// <remarks>
/// Generated <c>Required*</c> value objects use this converter so Core-only consumers can
/// serialize and deserialize generated primitives without referencing <c>Trellis.Primitives</c>.
/// </remarks>
public class ParsableJsonConverter<T> : JsonConverter<T>
    where T : IParsable<T>
{
    private static readonly bool s_isNumericType = IsNumericScalarType();

    /// <inheritdoc />
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number when reader.TryGetDecimal(out var d) => d.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected JSON token type '{reader.TokenType}' when deserializing '{typeof(T).Name}'. Expected string, number, boolean, or null.")
        };

        if (raw is null)
            throw new JsonException($"Cannot deserialize null JSON value to non-nullable type '{typeof(T).Name}'.");

        return T.Parse(raw, default);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var stringValue = value.ToString();

        if (s_isNumericType
            && stringValue is not null
            && decimal.TryParse(stringValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var numericValue))
        {
            writer.WriteNumberValue(numericValue);
        }
        else
        {
            writer.WriteStringValue(stringValue);
        }
    }

    private static bool IsNumericScalarType()
    {
        var type = typeof(T);
        while (type is not null)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition().Name.StartsWith("ScalarValueObject", StringComparison.Ordinal))
            {
                var primitiveType = type.GetGenericArguments()[1];
                return primitiveType == typeof(int)
                    || primitiveType == typeof(long)
                    || primitiveType == typeof(decimal)
                    || primitiveType == typeof(double)
                    || primitiveType == typeof(float)
                    || primitiveType == typeof(short)
                    || primitiveType == typeof(byte);
            }

            type = type.BaseType;
        }

        return false;
    }
}
