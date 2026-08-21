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

        try
        {
            if (TryReadKnownPrimitive(ref reader, out value))
                return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
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
        out TPrimitive? value)
        where TPrimitive : IComparable
    {
        var primitiveType = typeof(TPrimitive);

        if (primitiveType == typeof(string))
        {
            value = (TPrimitive?)(object?)reader.GetString();
            return true;
        }

        if (primitiveType == typeof(Guid))
        {
            value = JitCast<Guid>(reader.GetGuid());
            return true;
        }

        if (primitiveType == typeof(int))
        {
            value = JitCast<int>(reader.GetInt32());
            return true;
        }

        if (primitiveType == typeof(long))
        {
            value = JitCast<long>(reader.GetInt64());
            return true;
        }

        if (primitiveType == typeof(short))
        {
            value = JitCast<short>(reader.GetInt16());
            return true;
        }

        if (primitiveType == typeof(byte))
        {
            value = JitCast<byte>(reader.GetByte());
            return true;
        }

        if (primitiveType == typeof(sbyte))
        {
            value = JitCast<sbyte>(reader.GetSByte());
            return true;
        }

        if (primitiveType == typeof(ushort))
        {
            value = JitCast<ushort>(reader.GetUInt16());
            return true;
        }

        if (primitiveType == typeof(uint))
        {
            value = JitCast<uint>(reader.GetUInt32());
            return true;
        }

        if (primitiveType == typeof(ulong))
        {
            value = JitCast<ulong>(reader.GetUInt64());
            return true;
        }

        if (primitiveType == typeof(double))
        {
            value = JitCast<double>(reader.GetDouble());
            return true;
        }

        if (primitiveType == typeof(float))
        {
            value = JitCast<float>(reader.GetSingle());
            return true;
        }

        if (primitiveType == typeof(decimal))
        {
            value = JitCast<decimal>(reader.GetDecimal());
            return true;
        }

        if (primitiveType == typeof(bool))
        {
            value = JitCast<bool>(reader.GetBoolean());
            return true;
        }

        if (primitiveType == typeof(DateTime))
        {
            value = JitCast<DateTime>(reader.GetDateTime());
            return true;
        }

        if (primitiveType == typeof(DateTimeOffset))
        {
            value = JitCast<DateTimeOffset>(reader.GetDateTimeOffset());
            return true;
        }

        if (primitiveType == typeof(DateOnly))
        {
            value = JitCast<DateOnly>(ReadDateOnly(ref reader));
            return true;
        }

        if (primitiveType == typeof(TimeOnly))
        {
            value = JitCast<TimeOnly>(ReadTimeOnly(ref reader));
            return true;
        }

        if (primitiveType == typeof(TimeSpan))
        {
            value = JitCast<TimeSpan>(ReadTimeSpan(ref reader));
            return true;
        }

        value = default;
        return false;

        static TPrimitive JitCast<TActual>(TActual actual)
            where TActual : IComparable => Unsafe.As<TActual, TPrimitive>(ref actual);
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