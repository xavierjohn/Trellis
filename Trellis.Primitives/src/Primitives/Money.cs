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
[JsonConverter(typeof(MoneyJsonConverter))]
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
            return Result.Failure<Money>(Error.Validation("Amount cannot be negative.", field));

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
    public static Money Create(decimal amount, string currencyCode)
    {
        var result = TryCreate(amount, currencyCode);
        if (result.IsFailure)
            throw new InvalidOperationException($"Failed to create Money: {result.Error.Detail}");

        return result.Value;
    }

    /// <summary>
    /// Adds two Money amounts if they have the same currency.
    /// </summary>
    public Result<Money> Add(Money other)
    {
        if (!Currency.Equals(other.Currency))
            return Result.Failure<Money>(
                Error.Validation($"Cannot add {other.Currency} to {Currency}.", "currency"));

        return TryCreate(Amount + other.Amount, Currency);
    }

    /// <summary>
    /// Subtracts another Money amount if they have the same currency.
    /// </summary>
    public Result<Money> Subtract(Money other)
    {
        if (!Currency.Equals(other.Currency))
            return Result.Failure<Money>(
                Error.Validation($"Cannot subtract {other.Currency} from {Currency}.", "currency"));

        return TryCreate(Amount - other.Amount, Currency);
    }

    /// <summary>
    /// Multiplies the money amount by a scalar value.
    /// </summary>
    public Result<Money> Multiply(decimal multiplier)
    {
        if (multiplier < 0)
            return Result.Failure<Money>(
                Error.Validation("Multiplier cannot be negative.", nameof(multiplier)));

        return TryCreate(Amount * multiplier, Currency);
    }

    /// <summary>
    /// Multiplies the money amount by an integer quantity.
    /// </summary>
    public Result<Money> Multiply(int quantity)
    {
        if (quantity < 0)
            return Result.Failure<Money>(
                Error.Validation("Quantity cannot be negative.", nameof(quantity)));

        return TryCreate(Amount * quantity, Currency);
    }

    /// <summary>
    /// Divides the money amount by a scalar value.
    /// </summary>
    public Result<Money> Divide(decimal divisor)
    {
        if (divisor <= 0)
            return Result.Failure<Money>(
                Error.Validation("Divisor must be positive.", nameof(divisor)));

        return TryCreate(Amount / divisor, Currency);
    }

    /// <summary>
    /// Divides the money amount by an integer to split evenly.
    /// </summary>
    public Result<Money> Divide(int divisor)
    {
        if (divisor <= 0)
            return Result.Failure<Money>(
                Error.Validation("Divisor must be positive.", nameof(divisor)));

        return TryCreate(Amount / divisor, Currency);
    }

    /// <summary>
    /// Allocates money across multiple parts using ratios.
    /// Ensures no money is lost to rounding by allocating remainder to first portions.
    /// </summary>
    /// <param name="ratios">The ratios to split by (e.g., [1, 2, 1] for 25%, 50%, 25%).</param>
    /// <returns>Array of Money amounts matching the ratios.</returns>
    public Result<Money[]> Allocate(params int[] ratios)
    {
        if (ratios.Length == 0)
            return Result.Failure<Money[]>(Error.Validation("At least one ratio required.", nameof(ratios)));

        if (ratios.Any(r => r <= 0))
            return Result.Failure<Money[]>(Error.Validation("All ratios must be positive.", nameof(ratios)));

        var decimalPlaces = GetDecimalPlaces(Currency);
        var multiplier = (decimal)Math.Pow(10, decimalPlaces);
        var totalRatio = ratios.Sum();
        var amountInMinorUnits = (long)(Amount * multiplier);
        var remainder = amountInMinorUnits;
        var results = new Money[ratios.Length];

        for (int i = 0; i < ratios.Length; i++)
        {
            var share = amountInMinorUnits * ratios[i] / totalRatio;
            results[i] = new Money(share / multiplier, Currency);
            remainder -= share;
        }

        // Distribute remainder (due to rounding) to first portions
        var remainderUnits = (int)Math.Abs(remainder);
        for (int i = 0; i < remainderUnits && i < results.Length; i++)
        {
            var adjustment = 1m / multiplier;
            results[i] = new Money(results[i].Amount + adjustment, Currency);
        }

        return Result.Success(results);
    }

    /// <summary>
    /// Checks if this money is greater than another money amount (same currency required).
    /// </summary>
    public bool IsGreaterThan(Money other) =>
        Currency.Equals(other.Currency) && Amount > other.Amount;

    /// <summary>
    /// Checks if this money is greater than or equal to another amount (same currency required).
    /// </summary>
    public bool IsGreaterThanOrEqual(Money other) =>
        Currency.Equals(other.Currency) && Amount >= other.Amount;

    /// <summary>
    /// Checks if this money is less than another money amount (same currency required).
    /// </summary>
    public bool IsLessThan(Money other) =>
        Currency.Equals(other.Currency) && Amount < other.Amount;

    /// <summary>
    /// Checks if this money is less than or equal to another amount (same currency required).
    /// </summary>
    public bool IsLessThanOrEqual(Money other) =>
        Currency.Equals(other.Currency) && Amount <= other.Amount;

    /// <summary>
    /// Creates a zero-value Money instance for the specified currency.
    /// </summary>
    public static Result<Money> Zero(string currencyCode = "USD") => TryCreate(0, currencyCode);

    /// <summary>
    /// Gets the equality components for value comparison.
    /// </summary>
    protected override IEnumerable<IComparable> GetEqualityComponents()
    {
        yield return Math.Round(Amount, GetDecimalPlaces(Currency));
        yield return (string)Currency;
    }

    /// <summary>
    /// Returns a string representation of the money amount with currency.
    /// </summary>
    public override string ToString() => $"{Amount:F2} {Currency}";

    /// <summary>
    /// Gets the number of decimal places for a currency (e.g., 2 for USD/EUR, 0 for JPY).
    /// </summary>
    private static int GetDecimalPlaces(CurrencyCode currency) => currency.Value switch
    {
        "JPY" => 0, // Japanese Yen has no minor units
        "KRW" => 0, // Korean Won has no minor units
        "BHD" => 3, // Bahraini Dinar has 3 decimal places
        "KWD" => 3, // Kuwaiti Dinar has 3 decimal places
        "OMR" => 3, // Omani Rial has 3 decimal places
        "TND" => 3, // Tunisian Dinar has 3 decimal places
        _ => 2      // Most currencies have 2 decimal places
    };
}