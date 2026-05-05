namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// Represents a structured monetary value object composed of an amount and currency code.
/// Provides type-safe arithmetic operations that prevent mixing different currencies.
/// </summary>
/// <remarks>
/// Money is intentionally not a scalar value object because its semantic identity spans
/// both <see cref="Amount"/> and <see cref="Currency"/>.
/// Money uses decimal for precision and stores amounts in the currency's base unit.
/// All arithmetic operations enforce currency matching and return Result for error handling.
/// Amounts are automatically rounded to the appropriate number of decimal places for the currency.
/// </remarks>
[JsonConverter(typeof(CompositeValueObjectJsonConverter<Money>))]
public class Money : ValueObject
{
    /// <summary>
    /// Gets the monetary amount in the currency's base unit.
    /// </summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// Gets the ISO 4217 currency code.
    /// </summary>
    public CurrencyCode Currency { get; private set; }

    // ReSharper disable once UnusedMember.Local — used by EF Core for materialization
    private Money() => Currency = null!;

    private Money(decimal amount, CurrencyCode currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>
    /// Creates a Money instance with the specified amount and currency code.
    /// </summary>
    /// <param name="amount">The monetary amount.</param>
    /// <param name="currencyCode">ISO 4217 currency code (e.g., "USD", "EUR").</param>
    /// <param name="fieldName">Optional field name for validation errors.</param>
    /// <returns>Result containing the Money instance or validation errors.</returns>
    public static Result<Money> TryCreate(decimal amount, string currencyCode, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Money) + '.' + nameof(TryCreate));

        var field = fieldName.NormalizeFieldName("amount");

        if (amount < 0)
            return Result.Fail<Money>(Error.UnprocessableContent.ForField(field, "validation.error", "Amount cannot be negative."));

        return CurrencyCode.TryCreate(currencyCode, fieldName.NormalizeFieldName("currencyCode"))
            .Map(currency => new Money(Math.Round(amount, GetDecimalPlaces(currency), MidpointRounding.AwayFromZero), currency));
    }

    /// <summary>
    /// Creates a Money instance with the specified amount and currency code.
    /// Throws an exception if the amount or currency is invalid.
    /// </summary>
    /// <param name="amount">The monetary amount.</param>
    /// <param name="currencyCode">ISO 4217 currency code (e.g., "USD", "EUR").</param>
    /// <returns>The Money instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when amount is negative or currency is invalid.</exception>
    /// <remarks>
    /// Use this method when you know the values are valid (e.g., in tests or with constants).
    /// For user input, use <see cref="TryCreate"/> instead.
    /// </remarks>
    public static Money Create(decimal amount, string currencyCode) =>
        TryCreate(amount, currencyCode).Match(
            onSuccess: money => money,
            onFailure: error => throw new InvalidOperationException($"Failed to create Money: {error.GetDisplayMessage()}"));

    /// <summary>
    /// Adds two Money amounts if they have the same currency.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public Result<Money> Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!Currency.Equals(other.Currency))
            return Result.Fail<Money>(
                Error.UnprocessableContent.ForField("currency", "validation.error", $"Cannot add {other.Currency} to {Currency}."));

        try { return TryCreate(Amount + other.Amount, Currency); }
        catch (OverflowException) { return Result.Fail<Money>(Error.UnprocessableContent.ForField("amount", "validation.error", "Addition would overflow.")); }
    }

    /// <summary>
    /// Subtracts another Money amount if they have the same currency.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public Result<Money> Subtract(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!Currency.Equals(other.Currency))
            return Result.Fail<Money>(
                Error.UnprocessableContent.ForField("currency", "validation.error", $"Cannot subtract {other.Currency} from {Currency}."));

        if (Amount < other.Amount)
            return Result.Fail<Money>(
                Error.UnprocessableContent.ForField("money", "validation.error", "Subtraction would result in a negative amount."));

        return TryCreate(Amount - other.Amount, Currency);
    }

    /// <summary>
    /// Multiplies the money amount by a scalar value.
    /// </summary>
    public Result<Money> Multiply(decimal multiplier)
    {
        if (multiplier < 0)
            return Result.Fail<Money>(
                Error.UnprocessableContent.ForField(nameof(multiplier), "validation.error", "Multiplier cannot be negative."));

        try { return TryCreate(Amount * multiplier, Currency); }
        catch (OverflowException) { return Result.Fail<Money>(Error.UnprocessableContent.ForField("amount", "validation.error", "Multiplication would overflow.")); }
    }

    /// <summary>
    /// Multiplies the money amount by an integer quantity.
    /// </summary>
    public Result<Money> Multiply(int quantity)
    {
        if (quantity < 0)
            return Result.Fail<Money>(
                Error.UnprocessableContent.ForField(nameof(quantity), "validation.error", "Quantity cannot be negative."));

        try { return TryCreate(Amount * quantity, Currency); }
        catch (OverflowException) { return Result.Fail<Money>(Error.UnprocessableContent.ForField("amount", "validation.error", "Multiplication would overflow.")); }
    }

    /// <summary>
    /// Divides the money amount by a scalar value.
    /// </summary>
    public Result<Money> Divide(decimal divisor)
    {
        if (divisor <= 0)
            return Result.Fail<Money>(
                Error.UnprocessableContent.ForField(nameof(divisor), "validation.error", "Divisor must be positive."));

        try { return TryCreate(Amount / divisor, Currency); }
        catch (OverflowException) { return Result.Fail<Money>(Error.UnprocessableContent.ForField("amount", "validation.error", "Division would overflow.")); }
    }

    /// <summary>
    /// Divides the money amount by an integer to split evenly.
    /// </summary>
    public Result<Money> Divide(int divisor)
    {
        if (divisor <= 0)
            return Result.Fail<Money>(
                Error.UnprocessableContent.ForField(nameof(divisor), "validation.error", "Divisor must be positive."));

        try { return TryCreate(Amount / divisor, Currency); }
        catch (OverflowException) { return Result.Fail<Money>(Error.UnprocessableContent.ForField("amount", "validation.error", "Division would overflow.")); }
    }

    /// <summary>
    /// Allocates money across multiple parts using ratios.
    /// Ensures no money is lost to rounding by allocating remainder to first portions.
    /// </summary>
    /// <param name="ratios">The ratios to split by (e.g., [1, 2, 1] for 25%, 50%, 25%).</param>
    /// <returns>Array of Money amounts matching the ratios.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ratios"/> is null.</exception>
    public Result<Money[]> Allocate(params int[] ratios)
    {
        ArgumentNullException.ThrowIfNull(ratios);

        if (ratios.Length == 0)
            return Result.Fail<Money[]>(Error.UnprocessableContent.ForField(nameof(ratios), "validation.error", "At least one ratio required."));

        if (ratios.Any(r => r <= 0))
            return Result.Fail<Money[]>(Error.UnprocessableContent.ForField(nameof(ratios), "validation.error", "All ratios must be positive."));

        try
        {
            var decimalPlaces = GetDecimalPlaces(Currency);
            var multiplier = (decimal)Math.Pow(10, decimalPlaces);
            var totalRatio = ratios.Sum();
            var amountInMinorUnits = (long)Math.Round(Amount * multiplier, MidpointRounding.AwayFromZero);
            var remainder = amountInMinorUnits;
            var results = new Money[ratios.Length];

            for (int i = 0; i < ratios.Length; i++)
            {
                // The integral multiplication can overflow long for extreme amounts +
                // large ratios; force a checked context so the catch below can convert
                // it to Result.Fail rather than silently wrapping.
                var share = checked(amountInMinorUnits * ratios[i]) / totalRatio;
                results[i] = new Money(share / multiplier, Currency);
                remainder -= share;
            }

            // Distribute remainder (due to rounding) to first portions
            var adjustment = 1m / multiplier * Math.Sign(remainder);
            for (var i = 0; i < Math.Abs(remainder) && i < results.Length; i++)
                results[i] = new Money(results[i].Amount + adjustment, Currency);

            return Result.Ok(results);
        }
        catch (OverflowException)
        {
            return Result.Fail<Money[]>(Error.UnprocessableContent.ForField(nameof(ratios), "validation.error", "Allocation arithmetic would overflow."));
        }
    }

    /// <summary>
    /// Checks if this money is greater than another money amount (same currency required).
    /// </summary>
    /// <remarks>
    /// Returns <c>false</c> for cross-currency comparisons; mismatched currencies are
    /// neither greater than nor less than each other under this contract. Callers that need
    /// to distinguish "smaller" from "different currency" must compare <see cref="Currency"/>
    /// explicitly.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public bool IsGreaterThan(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Currency.Equals(other.Currency) && Amount > other.Amount;
    }

    /// <summary>
    /// Checks if this money is greater than or equal to another amount (same currency required).
    /// </summary>
    /// <remarks>
    /// Returns <c>false</c> for cross-currency comparisons. See <see cref="IsGreaterThan"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public bool IsGreaterThanOrEqual(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Currency.Equals(other.Currency) && Amount >= other.Amount;
    }

    /// <summary>
    /// Checks if this money is less than another money amount (same currency required).
    /// </summary>
    /// <remarks>
    /// Returns <c>false</c> for cross-currency comparisons. See <see cref="IsGreaterThan"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public bool IsLessThan(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Currency.Equals(other.Currency) && Amount < other.Amount;
    }

    /// <summary>
    /// Checks if this money is less than or equal to another amount (same currency required).
    /// </summary>
    /// <remarks>
    /// Returns <c>false</c> for cross-currency comparisons. See <see cref="IsGreaterThan"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="other"/> is null.</exception>
    public bool IsLessThanOrEqual(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Currency.Equals(other.Currency) && Amount <= other.Amount;
    }

    /// <summary>
    /// Creates a zero-value Money instance for the specified currency.
    /// </summary>
    public static Result<Money> Zero(string currencyCode = "USD") => TryCreate(0, currencyCode);

    /// <summary>
    /// Gets the equality components for value comparison.
    /// </summary>
    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Math.Round(Amount, GetDecimalPlaces(Currency));
        yield return (string)Currency;
    }

    /// <summary>
    /// Returns a string representation of the money amount with currency.
    /// </summary>
    public override string ToString() => $"{Amount.ToString($"F{GetDecimalPlaces(Currency)}", System.Globalization.CultureInfo.InvariantCulture)} {Currency}";

    /// <summary>
    /// Gets the number of decimal places for a currency per ISO 4217 minor-unit assignments.
    /// Currencies not in the table default to 2 decimal places.
    /// </summary>
    private static int GetDecimalPlaces(CurrencyCode currency) => currency.Value switch
    {
        // 0 minor units (whole-unit currencies)
        "BIF" => 0,    // Burundian Franc
        "CLP" => 0,    // Chilean Peso
        "DJF" => 0,    // Djiboutian Franc
        "GNF" => 0,    // Guinean Franc
        "ISK" => 0,    // Icelandic Króna
        "JPY" => 0,    // Japanese Yen
        "KMF" => 0,    // Comorian Franc
        "KRW" => 0,    // South Korean Won
        "PYG" => 0,    // Paraguayan Guaraní
        "RWF" => 0,    // Rwandan Franc
        "UGX" => 0,    // Ugandan Shilling
        "UYI" => 0,    // Uruguay Peso en Unidades Indexadas
        "VND" => 0,    // Vietnamese Đồng
        "VUV" => 0,    // Vanuatu Vatu
        "XAF" => 0,    // CFA Franc BEAC
        "XOF" => 0,    // CFA Franc BCEAO
        "XPF" => 0,    // CFP Franc

        // 3 minor units
        "BHD" => 3,    // Bahraini Dinar
        "IQD" => 3,    // Iraqi Dinar
        "JOD" => 3,    // Jordanian Dinar
        "KWD" => 3,    // Kuwaiti Dinar
        "LYD" => 3,    // Libyan Dinar
        "OMR" => 3,    // Omani Rial
        "TND" => 3,    // Tunisian Dinar

        // 4 minor units (unidad-de-fomento types)
        "CLF" => 4,    // Chilean Unidad de Fomento
        "UYW" => 4,    // Unidad Previsional (Uruguay)

        _ => 2          // Most currencies have 2 decimal places (default per ISO 4217)
    };

    /// <summary>
    /// Sums a collection of <see cref="Money"/> values. All values must share the same currency.
    /// </summary>
    /// <param name="values">The monetary values to sum.</param>
    /// <returns>
    /// Success with the total, or failure if the collection is empty, contains mixed currencies, or overflows.
    /// </returns>
    public static Result<Money> Sum(IEnumerable<Money> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        using var enumerator = values.GetEnumerator();

        if (!enumerator.MoveNext())
            return Result.Fail<Money>(Error.UnprocessableContent.ForField(nameof(values), "validation.error", "Cannot sum an empty collection."));

        var first = enumerator.Current;
        if (first is null)
            throw new ArgumentException("Collection contains a null element.", nameof(values));

        var currency = first.Currency;
        var totalAmount = first.Amount;

        try
        {
            while (enumerator.MoveNext())
            {
                var current = enumerator.Current;
                if (current is null)
                    throw new ArgumentException("Collection contains a null element.", nameof(values));

                if (!currency.Equals(current.Currency))
                    return Result.Fail<Money>(
                        Error.UnprocessableContent.ForField("currency", "validation.error", $"Cannot add {current.Currency} to {currency}."));

                totalAmount += current.Amount;
            }
        }
        catch (OverflowException)
        {
            return Result.Fail<Money>(Error.UnprocessableContent.ForField("amount", "validation.error", "Addition would overflow."));
        }

        return TryCreate(totalAmount, currency.Value);
    }
}