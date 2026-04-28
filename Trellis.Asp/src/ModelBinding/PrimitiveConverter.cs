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
            return Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "Value is required." });

        var targetType = typeof(TPrimitive);
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (underlyingType == typeof(string))
                return Result.Ok((TPrimitive)(object)value);

            if (underlyingType.IsEnum)
            {
                if (Enum.TryParse(underlyingType, value, ignoreCase: true, out var enumValue)
                    && enumValue is not null
                    && (Enum.IsDefined(underlyingType, enumValue)
                        || underlyingType.IsDefined(typeof(FlagsAttribute), inherit: false)))
                    return Result.Ok((TPrimitive)enumValue);

                return Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a recognized option." });
            }

            if (underlyingType == typeof(Guid))
                return Guid.TryParse(value, out var guid)
                    ? Result.Ok((TPrimitive)(object)guid)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid GUID." });

            if (underlyingType == typeof(int))
                return int.TryParse(value, out var i)
                    ? Result.Ok((TPrimitive)(object)i)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid integer." });

            if (underlyingType == typeof(long))
                return long.TryParse(value, out var l)
                    ? Result.Ok((TPrimitive)(object)l)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid integer." });

            if (underlyingType == typeof(short))
                return short.TryParse(value, out var s)
                    ? Result.Ok((TPrimitive)(object)s)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid integer." });

            if (underlyingType == typeof(byte))
                return byte.TryParse(value, out var by)
                    ? Result.Ok((TPrimitive)(object)by)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid byte (0-255)." });

            if (underlyingType == typeof(decimal))
                return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                    ? Result.Ok((TPrimitive)(object)d)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid decimal." });

            if (underlyingType == typeof(double))
                return double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl)
                    ? Result.Ok((TPrimitive)(object)dbl)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid number." });

            if (underlyingType == typeof(float))
                return float.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var flt)
                    ? Result.Ok((TPrimitive)(object)flt)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid number." });

            if (underlyingType == typeof(bool))
                return bool.TryParse(value, out var b)
                    ? Result.Ok((TPrimitive)(object)b)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid boolean." });

            if (underlyingType == typeof(DateTime))
                return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                    ? Result.Ok((TPrimitive)(object)dt)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid date/time." });

            if (underlyingType == typeof(DateTimeOffset))
                return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
                    ? Result.Ok((TPrimitive)(object)dto)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid date/time." });

            if (underlyingType == typeof(DateOnly))
                return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly)
                    ? Result.Ok((TPrimitive)(object)dateOnly)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid date." });

            if (underlyingType == typeof(TimeOnly))
                return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly)
                    ? Result.Ok((TPrimitive)(object)timeOnly)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid time." });

            if (underlyingType == typeof(TimeSpan))
                return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeSpan)
                    ? Result.Ok((TPrimitive)(object)timeSpan)
                    : Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value is not a valid time span." });

            // Fallback: try Convert.ChangeType
            var converted = Convert.ChangeType(value, underlyingType, CultureInfo.InvariantCulture);
            return Result.Ok((TPrimitive)converted);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return Result.Fail<TPrimitive>(new Error.UnprocessableContent(EquatableArray<FieldViolation>.Empty) { Detail = "The value could not be converted to the expected type." });
        }
    }
}