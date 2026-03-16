namespace Trellis.Asp.ModelBinding;

using System.Globalization;

/// <summary>
/// Converts string values to primitive types for model binding.
/// Shared by <see cref="ScalarValueModelBinder{TValue, TPrimitive}"/>
/// and <see cref="MaybeModelBinder{TValue, TPrimitive}"/>.
/// </summary>
internal static class PrimitiveConverter
{
    /// <summary>
    /// Converts a string value to the specified primitive type.
    /// </summary>
    /// <typeparam name="TPrimitive">The target primitive type.</typeparam>
    /// <param name="value">The string value to convert.</param>
    /// <returns>A <see cref="Result{TPrimitive}"/> containing the converted value or a validation error.</returns>
    public static Result<TPrimitive> ConvertToPrimitive<TPrimitive>(string? value)
        where TPrimitive : IComparable
    {
        if (string.IsNullOrEmpty(value))
            return Error.Validation("Value is required.");

        var targetType = typeof(TPrimitive);
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (underlyingType == typeof(string))
                return (TPrimitive)(object)value;

            if (underlyingType.IsEnum)
            {
                if (Enum.TryParse(underlyingType, value, ignoreCase: true, out var enumValue)
                    && enumValue is not null
                    && (Enum.IsDefined(underlyingType, enumValue)
                        || underlyingType.IsDefined(typeof(FlagsAttribute), inherit: false)))
                    return (TPrimitive)enumValue;

                return Error.Validation($"'{value}' is not a valid {underlyingType.Name}.");
            }

            if (underlyingType == typeof(Guid))
                return Guid.TryParse(value, out var guid)
                    ? (TPrimitive)(object)guid
                    : Error.Validation($"'{value}' is not a valid GUID.");

            if (underlyingType == typeof(int))
                return int.TryParse(value, out var i)
                    ? (TPrimitive)(object)i
                    : Error.Validation($"'{value}' is not a valid integer.");

            if (underlyingType == typeof(long))
                return long.TryParse(value, out var l)
                    ? (TPrimitive)(object)l
                    : Error.Validation($"'{value}' is not a valid integer.");

            if (underlyingType == typeof(short))
                return short.TryParse(value, out var s)
                    ? (TPrimitive)(object)s
                    : Error.Validation($"'{value}' is not a valid integer.");

            if (underlyingType == typeof(byte))
                return byte.TryParse(value, out var by)
                    ? (TPrimitive)(object)by
                    : Error.Validation($"'{value}' is not a valid byte (0-255).");

            if (underlyingType == typeof(decimal))
                return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                    ? (TPrimitive)(object)d
                    : Error.Validation($"'{value}' is not a valid decimal.");

            if (underlyingType == typeof(double))
                return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl)
                    ? (TPrimitive)(object)dbl
                    : Error.Validation($"'{value}' is not a valid number.");

            if (underlyingType == typeof(float))
                return float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var flt)
                    ? (TPrimitive)(object)flt
                    : Error.Validation($"'{value}' is not a valid number.");

            if (underlyingType == typeof(bool))
                return bool.TryParse(value, out var b)
                    ? (TPrimitive)(object)b
                    : Error.Validation($"'{value}' is not a valid boolean.");

            if (underlyingType == typeof(DateTime))
                return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                    ? (TPrimitive)(object)dt
                    : Error.Validation($"'{value}' is not a valid date/time.");

            if (underlyingType == typeof(DateTimeOffset))
                return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                    ? (TPrimitive)(object)dto
                    : Error.Validation($"'{value}' is not a valid date/time.");

            if (underlyingType == typeof(DateOnly))
                return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly)
                    ? (TPrimitive)(object)dateOnly
                    : Error.Validation($"'{value}' is not a valid date.");

            if (underlyingType == typeof(TimeOnly))
                return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly)
                    ? (TPrimitive)(object)timeOnly
                    : Error.Validation($"'{value}' is not a valid time.");

            // Fallback: try Convert.ChangeType
            var converted = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
            return (TPrimitive)converted;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return Error.Validation($"'{value}' is not a valid {underlyingType.Name}.");
        }
    }
}