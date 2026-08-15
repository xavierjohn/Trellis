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
    private static readonly bool s_isBooleanType = IsBooleanScalarType();

    /// <summary>
    /// Tells System.Text.Json to call <see cref="JsonConverter{T}.Read"/> even when the JSON
    /// token is <c>null</c>. Without this, the serializer bypasses the converter for null tokens
    /// on reference-type targets and yields a null reference, silently violating the
    /// non-nullable contract of a generated primitive.
    /// </summary>
    public override bool HandleNull => true;

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

        // IParsable<T>.Parse signals malformed input with FormatException (and the generated
        // primitives surface validation failures the same way). Callers of a converter expect
        // JsonException, which is what every other failure path here throws, so translate rather
        // than leaking the parser's exception type through the serializer.
        try
        {
            return T.Parse(raw, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            throw new JsonException($"Cannot deserialize '{raw}' to '{typeof(T).Name}'.", ex);
        }
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

        if (s_isBooleanType
            && bool.TryParse(stringValue, out var booleanValue))
        {
            writer.WriteBooleanValue(booleanValue);
        }
        else if (s_isNumericType
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

    private static bool IsNumericScalarType() =>
        TryGetScalarPrimitiveType(out var primitiveType)
        && (primitiveType == typeof(int)
            || primitiveType == typeof(long)
            || primitiveType == typeof(decimal)
            || primitiveType == typeof(double)
            || primitiveType == typeof(float)
            || primitiveType == typeof(short)
            || primitiveType == typeof(byte));

    private static bool IsBooleanScalarType() =>
        TryGetScalarPrimitiveType(out var primitiveType) && primitiveType == typeof(bool);

    private static bool TryGetScalarPrimitiveType(out Type? primitiveType)
    {
        var type = typeof(T);
        while (type is not null)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition().Name.StartsWith("ScalarValueObject", StringComparison.Ordinal))
            {
                primitiveType = type.GetGenericArguments()[1];
                return true;
            }

            type = type.BaseType;
        }

        primitiveType = null;
        return false;
    }
}
