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
public class MonetaryAmount : ScalarValueObject<MonetaryAmount, decimal>, IScalarValue<MonetaryAmount, decimal>, IFormattableScalarValue<MonetaryAmount, decimal>, IParsable<MonetaryAmount>
{
    private const int DefaultDecimalPlaces = 2;

    private MonetaryAmount(decimal value) : base(value) { }

    private static readonly MonetaryAmount s_zero = new(0m);

    /// <summary>A zero monetary amount.</summary>
    public static MonetaryAmount Zero => s_zero;

    // Field-normalization + InvalidInput failure in one place (default field name: "amount").
    private static Result<MonetaryAmount> Invalid(string? fieldName, string message) =>
        Result.Fail<MonetaryAmount>(
            Error.InvalidInput.ForField(fieldName.NormalizeFieldName("amount"), "validation.error", message));

    // No-span validation core. Every public factory opens exactly one span, then delegates here.
    private static Result<MonetaryAmount> Validate(decimal value, string? fieldName)
    {
        if (value < 0)
            return Invalid(fieldName, "Amount cannot be negative.");

        var rounded = Math.Round(value, DefaultDecimalPlaces, MidpointRounding.AwayFromZero);
        return Result.Ok(new MonetaryAmount(rounded));
    }

    /// <summary>
    /// Attempts to create a <see cref="MonetaryAmount"/> from the specified decimal.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "amount" as the field name.
    /// </summary>
    /// <param name="value">The decimal value (must be non-negative).</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "amount".</param>
    /// <returns>Success with the MonetaryAmount if valid; Failure with <see cref="Error.InvalidInput"/> if negative.</returns>
    public static Result<MonetaryAmount> TryCreate(decimal value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(MonetaryAmount) + '.' + nameof(TryCreate));
        return Validate(value, fieldName);
    }

    /// <summary>
    /// Attempts to create a <see cref="MonetaryAmount"/> from the specified nullable decimal.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "amount" as the field name.
    /// </summary>
    /// <param name="value">The decimal value (must be non-negative). A null value fails validation.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "amount".</param>
    /// <returns>Success with the MonetaryAmount if valid; Failure with <see cref="Error.InvalidInput"/> if null or negative.</returns>
    public static Result<MonetaryAmount> TryCreate(decimal? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(MonetaryAmount) + '.' + nameof(TryCreate));
        return value is null
            ? Invalid(fieldName, "Amount is required.")
            : Validate(value.Value, fieldName);
    }

    /// <summary>
    /// Attempts to create a <see cref="MonetaryAmount"/> from a string representation.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "amount" as the field name.
    /// </summary>
    /// <param name="value">The string value to parse (must be a valid decimal).</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "amount".</param>
    /// <returns>Success with the MonetaryAmount if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    /// <remarks>Delegates to the <see cref="TryCreate(string?, IFormatProvider?, string?)"/> overload using the invariant culture.</remarks>
    public static Result<MonetaryAmount> TryCreate(string? value, string? fieldName = null) =>
        TryCreate(value, null, fieldName);

    /// <summary>
    /// Attempts to create a <see cref="MonetaryAmount"/> from a string using the specified format provider.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "amount" as the field name.
    /// </summary>
    /// <param name="value">The string value to parse (must be a valid decimal).</param>
    /// <param name="provider">The format provider for culture-sensitive parsing. Defaults to <see cref="System.Globalization.CultureInfo.InvariantCulture"/> when null.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "amount".</param>
    /// <returns>Success with the MonetaryAmount if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    public static Result<MonetaryAmount> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(MonetaryAmount) + '.' + nameof(TryCreate));

        if (string.IsNullOrWhiteSpace(value))
            return Invalid(fieldName, "Amount is required.");

        if (!decimal.TryParse(value, System.Globalization.NumberStyles.Number, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return Invalid(fieldName, "Amount must be a valid decimal.");

        return Validate(parsed, fieldName);
    }

    /// <summary>Adds two monetary amounts.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public Result<MonetaryAmount> Add(MonetaryAmount other)
    {
        ArgumentNullException.ThrowIfNull(other);

        try { return TryCreate(Value + other.Value); }
        catch (OverflowException) { return Result.Fail<MonetaryAmount>(Error.InvalidInput.ForField("amount", "validation.error", "Addition would overflow.")); }
    }

    /// <summary>Subtracts a monetary amount. Fails if result would be negative.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public Result<MonetaryAmount> Subtract(MonetaryAmount other)
    {
        ArgumentNullException.ThrowIfNull(other);

        try { return TryCreate(Value - other.Value); }
        catch (OverflowException) { return Result.Fail<MonetaryAmount>(Error.InvalidInput.ForField("amount", "validation.error", "Subtraction would overflow.")); }
    }

    /// <summary>Multiplies by a non-negative integer quantity.</summary>
    public Result<MonetaryAmount> Multiply(int quantity)
    {
        if (quantity < 0)
            return Result.Fail<MonetaryAmount>(
                Error.InvalidInput.ForField(nameof(quantity), "validation.error", "Quantity cannot be negative."));

        try { return TryCreate(Value * quantity); }
        catch (OverflowException) { return Result.Fail<MonetaryAmount>(Error.InvalidInput.ForField("amount", "validation.error", "Multiplication would overflow.")); }
    }

    /// <summary>Multiplies by a non-negative decimal multiplier.</summary>
    public Result<MonetaryAmount> Multiply(decimal multiplier)
    {
        if (multiplier < 0)
            return Result.Fail<MonetaryAmount>(
                Error.InvalidInput.ForField(nameof(multiplier), "validation.error", "Multiplier cannot be negative."));

        try { return TryCreate(Value * multiplier); }
        catch (OverflowException) { return Result.Fail<MonetaryAmount>(Error.InvalidInput.ForField("amount", "validation.error", "Multiplication would overflow.")); }
    }

    /// <inheritdoc/>
    public static MonetaryAmount Parse(string? s, IFormatProvider? provider) =>
        TryCreate(s, provider).Match(
            onSuccess: value => value,
            onFailure: error => throw new TrellisValidationFormatException(error.GetDisplayMessage(), error as Error.InvalidInput));

    /// <inheritdoc/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out MonetaryAmount result)
    {
        var r = TryCreate(s, provider);
        if (r.TryGetValue(out var value))
        {
            result = value;
            return true;
        }

        result = default!;
        return false;
    }

    /// <summary>Explicitly converts a decimal to a <see cref="MonetaryAmount"/>.</summary>
    public static explicit operator MonetaryAmount(decimal value) => Create(value);

    /// <summary>Returns the amount as an invariant-culture decimal string.</summary>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Sums a collection of <see cref="MonetaryAmount"/> values.
    /// </summary>
    /// <param name="values">The monetary amounts to sum.</param>
    /// <returns>
    /// Success with the total, or <see cref="Zero"/> if the collection is empty. Failure if addition overflows.
    /// </returns>
    public static Result<MonetaryAmount> Sum(IEnumerable<MonetaryAmount> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var total = Zero;

        foreach (var value in values)
        {
            if (value is null)
                throw new ArgumentException("Collection contains a null element.", nameof(values));

            var addResult = total.Add(value);
            if (!addResult.TryGetValue(out var next))
                return addResult;

            total = next;
        }

        return Result.Ok(total);
    }
}