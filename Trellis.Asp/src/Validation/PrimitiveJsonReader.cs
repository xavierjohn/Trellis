namespace Trellis.Asp.Validation;

using System.Globalization;
using System.Text.Json;

/// <summary>
/// Reads primitive values from a <see cref="Utf8JsonReader"/> without using reflection-based
/// <see cref="JsonSerializer"/> fallback APIs.
/// </summary>
internal static class PrimitiveJsonReader
{
    /// <summary>
    /// Reads a primitive value directly from the JSON reader using the typed reader API for
    /// supported primitive types.
    /// </summary>
    public static bool TryRead<TPrimitive>(
        ref Utf8JsonReader reader,
        string fieldName,
        out TPrimitive? value)
        where TPrimitive : IComparable
    {
        value = default;

        try
        {
            if (TryReadKnownPrimitive(ref reader, out value))
                return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            ValidationErrorsContext.AddError(fieldName, $"'{fieldName}' is not a valid {typeof(TPrimitive).Name}.");
            return false;
        }

        ValidationErrorsContext.AddError(
            fieldName,
            $"Primitive type '{typeof(TPrimitive).Name}' is not supported by the Trellis validation JSON converter. Provide a custom JsonConverter.");
        return false;
    }

    private static bool TryReadKnownPrimitive<TPrimitive>(
        ref Utf8JsonReader reader,
        out TPrimitive? value)
        where TPrimitive : IComparable
    {
        object? boxed;
        var primitiveType = typeof(TPrimitive);

        if (primitiveType == typeof(string))
        {
            boxed = reader.GetString();
        }
        else if (primitiveType == typeof(Guid))
        {
            boxed = reader.GetGuid();
        }
        else if (primitiveType == typeof(int))
        {
            boxed = reader.GetInt32();
        }
        else if (primitiveType == typeof(long))
        {
            boxed = reader.GetInt64();
        }
        else if (primitiveType == typeof(short))
        {
            boxed = reader.GetInt16();
        }
        else if (primitiveType == typeof(byte))
        {
            boxed = reader.GetByte();
        }
        else if (primitiveType == typeof(sbyte))
        {
            boxed = reader.GetSByte();
        }
        else if (primitiveType == typeof(ushort))
        {
            boxed = reader.GetUInt16();
        }
        else if (primitiveType == typeof(uint))
        {
            boxed = reader.GetUInt32();
        }
        else if (primitiveType == typeof(ulong))
        {
            boxed = reader.GetUInt64();
        }
        else if (primitiveType == typeof(double))
        {
            boxed = reader.GetDouble();
        }
        else if (primitiveType == typeof(float))
        {
            boxed = reader.GetSingle();
        }
        else if (primitiveType == typeof(decimal))
        {
            boxed = reader.GetDecimal();
        }
        else if (primitiveType == typeof(bool))
        {
            boxed = reader.GetBoolean();
        }
        else if (primitiveType == typeof(DateTime))
        {
            boxed = reader.GetDateTime();
        }
        else if (primitiveType == typeof(DateTimeOffset))
        {
            boxed = reader.GetDateTimeOffset();
        }
        else if (primitiveType == typeof(DateOnly))
        {
            boxed = ReadDateOnly(ref reader);
        }
        else if (primitiveType == typeof(TimeOnly))
        {
            boxed = ReadTimeOnly(ref reader);
        }
        else if (primitiveType == typeof(TimeSpan))
        {
            boxed = ReadTimeSpan(ref reader);
        }
        else
        {
            value = default;
            return false;
        }

        value = boxed is null ? default : (TPrimitive)boxed;
        return boxed is not null || primitiveType == typeof(string);
    }

    private static DateOnly ReadDateOnly(ref Utf8JsonReader reader)
    {
        var raw = reader.GetString();
        return DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new FormatException();
    }

    private static TimeOnly ReadTimeOnly(ref Utf8JsonReader reader)
    {
        var raw = reader.GetString();
        return TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new FormatException();
    }

    private static TimeSpan ReadTimeSpan(ref Utf8JsonReader reader)
    {
        var raw = reader.GetString();
        return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var duration)
            ? duration
            : throw new FormatException();
    }
}