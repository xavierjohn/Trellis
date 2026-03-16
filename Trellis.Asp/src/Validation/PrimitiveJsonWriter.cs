namespace Trellis.Asp.Validation;

using System.Text.Json;

/// <summary>
/// Writes primitive values to a <see cref="Utf8JsonWriter"/>.
/// Shared by <see cref="ValidatingJsonConverter{TValue, TPrimitive}"/>
/// and <see cref="MaybeScalarValueJsonConverter{TValue, TPrimitive}"/>.
/// </summary>
internal static class PrimitiveJsonWriter
{
    /// <summary>
    /// Writes a primitive value directly to the JSON writer using the
    /// appropriate typed overload for maximum performance and correctness.
    /// </summary>
    /// <typeparam name="TPrimitive">The primitive type.</typeparam>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The value to write.</param>
    public static void Write<TPrimitive>(Utf8JsonWriter writer, TPrimitive value)
        where TPrimitive : IComparable
    {
        switch (value)
        {
            case string s:
                writer.WriteStringValue(s);
                break;
            case Guid g:
                writer.WriteStringValue(g);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case short s:
                writer.WriteNumberValue(s);
                break;
            case byte b:
                writer.WriteNumberValue(b);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;
            case DateTime dt:
                writer.WriteStringValue(dt);
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto);
                break;
            case DateOnly date:
                writer.WriteStringValue(date.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                break;
            case TimeOnly time:
                writer.WriteStringValue(time.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteStringValue(value?.ToString());
                break;
        }
    }
}