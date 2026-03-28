namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// A non-negative monetary amount without currency — for single-currency systems.
/// <para>
/// Use <see cref="MonetaryAmount"/> when your bounded context operates in a single currency
/// (e.g., all USD). The currency is a system-wide policy, not per-row data.
/// Maps to a single <c>decimal(18,2)</c> column in EF Core via <c>ApplyTrellisConventions</c>.
/// </para>
/// <para>
/// For multi-currency systems where each value carries its own currency code,
/// use <see cref="Money"/> instead.
/// </para>
/// </summary>
[JsonConverter(typeof(ParsableJsonConverter<MonetaryAmount>))]
public class MonetaryAmount : ScalarValueObject<MonetaryAmount, decimal>, IScalarValue<MonetaryAmount, decimal>, IParsable<MonetaryAmount>
{
    private const int DefaultDecimalPlaces = 2;

    private MonetaryAmount(decimal value) : base(value) { }

    private static readonly MonetaryAmount s_zero = new(0m);

    /// <summary>A zero monetary amount.</summary>
    public static MonetaryAmount Zero => s_zero;

    /// <summary>
    /// Attempts to create a <see cref="MonetaryAmount"/> from the specified decimal.
    /// </summary>
    /// <param name="value">The decimal value (must be non-negative).</param>
    /// <param name="fieldName">Optional field name for validation error messages.</param>
    /// <returns>Success with the MonetaryAmount if valid; Failure with ValidationError if negative.</returns>
    public static Result<MonetaryAmount> TryCreate(decimal value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(MonetaryAmount) + '.' + nameof(TryCreate));

        var field = fieldName.NormalizeFieldName("amount");

        if (value < 0)
            return Result.Failure<MonetaryAmount>(Error.Validation("Amount cannot be negative.", field));

        var rounded = Math.Round(value, DefaultDecimalPlaces, MidpointRounding.AwayFromZero);
        return new MonetaryAmount(rounded);
    }

    /// <summary>
    /// Attempts to create a <see cref="MonetaryAmount"/> from the specified nullable decimal.
    /// </summary>
    public static Result<MonetaryAmount> TryCreate(decimal? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(MonetaryAmount) + '.' + nameof(TryCreate));

        var field = fieldName.NormalizeFieldName("amount");

        if (value is null)
            return Result.Failure<MonetaryAmount>(Error.Validation("Amount is required.", field));

        return TryCreate(value.Value, fieldName);
    }

    /// <summary>
    /// Attempts to create a <see cref="MonetaryAmount"/> from a string representation.
    /// </summary>
    /// <param name="value">The string value to parse (must be a valid decimal).</param>
    /// <param name="fieldName">Optional field name for validation error messages.</param>
    /// <returns>Success with the MonetaryAmount if valid; Failure with ValidationError otherwise.</returns>
    public static Result<MonetaryAmount> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(MonetaryAmount) + '.' + nameof(TryCreate));
        var field = fieldName.NormalizeFieldName("amount");

        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<MonetaryAmount>(Error.Validation("Amount is required.", field));

        if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return Result.Failure<MonetaryAmount>(Error.Validation("Amount must be a valid decimal.", field));

        return TryCreate(parsed, fieldName);
    }

    /// <summary>Adds two monetary amounts.</summary>
    public Result<MonetaryAmount> Add(MonetaryAmount other)
    {
        try { return TryCreate(Value + other.Value); }
        catch (OverflowException) { return Result.Failure<MonetaryAmount>(Error.Validation("Addition would overflow.", "amount")); }
    }

    /// <summary>Subtracts a monetary amount. Fails if result would be negative.</summary>
    public Result<MonetaryAmount> Subtract(MonetaryAmount other)
    {
        try { return TryCreate(Value - other.Value); }
        catch (OverflowException) { return Result.Failure<MonetaryAmount>(Error.Validation("Subtraction would overflow.", "amount")); }
    }

    /// <summary>Multiplies by a non-negative integer quantity.</summary>
    public Result<MonetaryAmount> Multiply(int quantity)
    {
        if (quantity < 0)
            return Result.Failure<MonetaryAmount>(
                Error.Validation("Quantity cannot be negative.", nameof(quantity)));

        try { return TryCreate(Value * quantity); }
        catch (OverflowException) { return Result.Failure<MonetaryAmount>(Error.Validation("Multiplication would overflow.", "amount")); }
    }

    /// <summary>Multiplies by a non-negative decimal multiplier.</summary>
    public Result<MonetaryAmount> Multiply(decimal multiplier)
    {
        if (multiplier < 0)
            return Result.Failure<MonetaryAmount>(
                Error.Validation("Multiplier cannot be negative.", nameof(multiplier)));

        try { return TryCreate(Value * multiplier); }
        catch (OverflowException) { return Result.Failure<MonetaryAmount>(Error.Validation("Multiplication would overflow.", "amount")); }
    }

    /// <inheritdoc/>
    public static MonetaryAmount Parse(string? s, IFormatProvider? provider)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new FormatException("Value must be a valid decimal.");

        if (!decimal.TryParse(s, System.Globalization.NumberStyles.Number, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new FormatException("Value must be a valid decimal.");

        var r = TryCreate(value);
        if (r.IsFailure)
        {
            var val = (ValidationError)r.Error;
            throw new FormatException(val.FieldErrors[0].Details[0]);
        }

        return r.Value;
    }

    /// <inheritdoc/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out MonetaryAmount result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(s))
            return false;

        if (!decimal.TryParse(s, System.Globalization.NumberStyles.Number, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var value))
            return false;

        var r = TryCreate(value);
        if (r.IsFailure)
            return false;

        result = r.Value;
        return true;
    }

    /// <summary>Explicitly converts a decimal to a <see cref="MonetaryAmount"/>.</summary>
    public static explicit operator MonetaryAmount(decimal value) => Create(value);

    /// <summary>Returns the amount as an invariant-culture decimal string.</summary>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
