namespace Trellis.Asp.Validation;

using System.Globalization;
using System.Runtime.CompilerServices;
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

        // A blank string token never became the target scalar, but neither did the caller attempt a
        // shape the `format.*` namespace names: they sent nothing. Classified before the typed read
        // so a JSON body agrees with query binding, which rejects blank before parsing too.
        if (typeof(TPrimitive) != typeof(string)
            && reader.TokenType == JsonTokenType.String
            && string.IsNullOrWhiteSpace(reader.GetString()))
        {
            ValidationErrorsContext.AddBodyError(
                fieldName,
                ValidationCodes.ValueNotEmpty,
                $"'{fieldName}' is required.");
            return false;
        }

        if (TryReadKnownPrimitive(ref reader, out value, out var supported))
            return true;

        if (supported)
        {
            // The same code a query-bound scalar of this type would report. A JSON body that said
            // `error.unspecified` here would make the wire answer depend on where the value arrived.
            ValidationErrorsContext.AddBodyError(
                fieldName,
                ValidationCodes.FormatCodeFor(typeof(TPrimitive)),
                $"'{fieldName}' is not a valid {ResourceRef.FormatTypeName(typeof(TPrimitive))}.");
            return false;
        }

        // An unsupported primitive type is a configuration defect in the consuming application, not
        // a fault in the submitted document, so it keeps the neutral sentinel: there is no rule the
        // caller could satisfy by sending different input.
        ValidationErrorsContext.AddBodyError(
            fieldName,
            $"Primitive type '{ResourceRef.FormatTypeName(typeof(TPrimitive))}' is not supported by the Trellis validation JSON converter. Provide a custom JsonConverter.");
        return false;
    }

    private static bool TryReadKnownPrimitive<TPrimitive>(
        ref Utf8JsonReader reader,
        out TPrimitive? value,
        out bool supported)
        where TPrimitive : IComparable
    {
        value = default;
        var primitiveType = typeof(TPrimitive);
        var stringToken = primitiveType == typeof(string) || primitiveType == typeof(Guid)
            || primitiveType == typeof(DateTime) || primitiveType == typeof(DateTimeOffset)
            || primitiveType == typeof(DateOnly) || primitiveType == typeof(TimeOnly)
            || primitiveType == typeof(TimeSpan);
        supported = stringToken || primitiveType == typeof(int) || primitiveType == typeof(long)
            || primitiveType == typeof(short) || primitiveType == typeof(byte) || primitiveType == typeof(sbyte)
            || primitiveType == typeof(ushort) || primitiveType == typeof(uint) || primitiveType == typeof(ulong)
            || primitiveType == typeof(double) || primitiveType == typeof(float) || primitiveType == typeof(decimal)
            || primitiveType == typeof(bool);
        if (!supported)
            return false;

        var expectedToken = stringToken ? JsonTokenType.String
            : primitiveType == typeof(bool) ? JsonTokenType.True : JsonTokenType.Number;
        if (reader.TokenType != expectedToken
            && !(primitiveType == typeof(bool) && reader.TokenType == JsonTokenType.False)
            && !(primitiveType == typeof(string) && reader.TokenType == JsonTokenType.Null))
        {
            return false;
        }

        if (primitiveType == typeof(string))
        {
            value = (TPrimitive?)(object?)reader.GetString();
            return true;
        }

        if (primitiveType == typeof(Guid))
            return reader.TryGetGuid(out var guidValue) && Set(guidValue, out value);
        if (primitiveType == typeof(int))
            return reader.TryGetInt32(out var intValue) && Set(intValue, out value);
        if (primitiveType == typeof(long))
            return reader.TryGetInt64(out var longValue) && Set(longValue, out value);
        if (primitiveType == typeof(short))
            return reader.TryGetInt16(out var shortValue) && Set(shortValue, out value);
        if (primitiveType == typeof(byte))
            return reader.TryGetByte(out var byteValue) && Set(byteValue, out value);
        if (primitiveType == typeof(sbyte))
            return reader.TryGetSByte(out var sbyteValue) && Set(sbyteValue, out value);
        if (primitiveType == typeof(ushort))
            return reader.TryGetUInt16(out var ushortValue) && Set(ushortValue, out value);
        if (primitiveType == typeof(uint))
            return reader.TryGetUInt32(out var uintValue) && Set(uintValue, out value);
        if (primitiveType == typeof(ulong))
            return reader.TryGetUInt64(out var ulongValue) && Set(ulongValue, out value);
        if (primitiveType == typeof(double))
            return reader.TryGetDouble(out var doubleValue) && Set(doubleValue, out value);
        if (primitiveType == typeof(float))
            return reader.TryGetSingle(out var floatValue) && Set(floatValue, out value);
        if (primitiveType == typeof(decimal))
            return reader.TryGetDecimal(out var decimalValue) && Set(decimalValue, out value);

        if (primitiveType == typeof(bool))
            return Set(reader.GetBoolean(), out value);

        if (primitiveType == typeof(DateTime))
            return reader.TryGetDateTime(out var dateTimeValue) && Set(dateTimeValue, out value);

        if (primitiveType == typeof(DateTimeOffset))
            return reader.TryGetDateTimeOffset(out var dateTimeOffsetValue) && Set(dateTimeOffsetValue, out value);

        if (primitiveType == typeof(DateOnly))
            return DateOnly.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnlyValue)
                && Set(dateOnlyValue, out value);

        if (primitiveType == typeof(TimeOnly))
            return TimeOnly.TryParse(reader.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnlyValue)
                && Set(timeOnlyValue, out value);

        if (primitiveType == typeof(TimeSpan))
            return TimeSpan.TryParse(reader.GetString(), CultureInfo.InvariantCulture, out var timeSpanValue)
                && Set(timeSpanValue, out value);

        value = default;
        return false;

        static bool Set<TActual>(TActual actual, out TPrimitive? primitive)
            where TActual : IComparable
        {
            primitive = Unsafe.As<TActual, TPrimitive>(ref actual);
            return true;
        }
    }
}