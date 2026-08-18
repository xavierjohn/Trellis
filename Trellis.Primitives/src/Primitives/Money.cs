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
    /// <remarks>
    /// <para>
    /// <paramref name="fieldName"/> names the <see cref="Money"/> value as a whole, and validation
    /// errors are reported against a nested JSON Pointer under it — one segment per component, matching
    /// the serialized shape <c>{ "amount": …, "currency": … }</c>. Passing <c>"price"</c> therefore
    /// yields <c>/price/amount</c> for amount failures and <c>/price/currency</c> for currency failures,
    /// so a client can bind each error to the field that produced it. When
    /// <paramref name="fieldName"/> is omitted the pointers are <c>/amount</c> and <c>/currency</c>.
    /// A <paramref name="fieldName"/> that is already a JSON Pointer (starts with <c>'/'</c>) is
    /// composed onto rather than escaped, so <c>"/items/0/price"</c> yields <c>/items/0/price/amount</c>.
    /// </para>
    /// <para>
    /// Use <see cref="TryCreate(decimal, string, string?, string?)"/> instead when the amount and
    /// currency arrive as sibling fields of a flat payload rather than as a nested object.
    /// </para>
    /// <para>
    /// Currency validation is delegated to <see cref="CurrencyCode.TryCreate"/> and is
    /// syntactic only (three ASCII letters per ISO 4217 format; input is case-insensitive,
    /// stored uppercase via <c>ToUpperInvariant</c>). The ISO 4217 active-code list is not
    /// enforced — values like <c>XXX</c>, <c>XTS</c>, or <c>ZZZ</c> satisfy the format and
    /// will be accepted. Applications that need to restrict to a specific set of currencies
    /// (e.g., those a payment processor supports, or excluding ISO reserved/test codes)
    /// should layer an allow-list at the application boundary before calling
    /// <see cref="TryCreate(decimal, string, string?)"/>.
    /// </para>
    /// </remarks>
    /// <param name="amount">The monetary amount.</param>
    /// <param name="currencyCode">ISO 4217 currency code (e.g., "USD", "EUR"); case-insensitive.</param>
    /// <param name="fieldName">Optional name of the money field itself; component errors are reported beneath it.</param>
    /// <returns>Result containing the Money instance or validation errors.</returns>
    public static Result<Money> TryCreate(decimal amount, string currencyCode, string? fieldName = null) =>
        TryCreateCore(
            amount,
            currencyCode,
            ComponentPointer(fieldName, AmountFieldName),
            ComponentPointer(fieldName, CurrencyFieldName));

    /// <summary>
    /// Creates a Money instance whose amount and currency are validated against independently
    /// named fields, for payloads that carry the two as siblings rather than as a nested object.
    /// </summary>
    /// <remarks>
    /// Use this overload when the incoming document looks like <c>{ "price": 10, "currency": "XX" }</c>.
    /// The nested-pointer composition performed by <see cref="TryCreate(decimal, string, string?)"/>
    /// would report such a failure at <c>/price/currency</c> — a location that does not exist in the
    /// payload. Each argument accepts a simple property name or a full JSON Pointer.
    /// </remarks>
    /// <param name="amount">The monetary amount.</param>
    /// <param name="currencyCode">ISO 4217 currency code (e.g., "USD", "EUR"); case-insensitive.</param>
    /// <param name="amountFieldName">Field carrying the amount; defaults to <c>amount</c> when null or empty.</param>
    /// <param name="currencyFieldName">Field carrying the currency; defaults to <c>currency</c> when null or empty.</param>
    /// <returns>Result containing the Money instance or validation errors.</returns>
    public static Result<Money> TryCreate(decimal amount, string currencyCode, string? amountFieldName, string? currencyFieldName) =>
        TryCreateCore(
            amount,
            currencyCode,
            InputPointer.ForProperty(amountFieldName.NormalizeFieldName(AmountFieldName)),
            InputPointer.ForProperty(currencyFieldName.NormalizeFieldName(CurrencyFieldName)));

    private const string AmountFieldName = "amount";
    private const string CurrencyFieldName = "currency";

    private static InputPointer ComponentPointer(string? fieldName, string component) =>
        new(InputPointer.ForProperty(fieldName.NormalizeFieldName(string.Empty)).Path + '/' + component);

    private static Result<Money> TryCreateCore(decimal amount, string currencyCode, InputPointer amountField, InputPointer currencyField)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Money) + '.' + nameof(TryCreate));

        if (amount < 0)
            return Result.Fail<Money>(Error.InvalidInput.ForField(amountField, "validation.error", "Amount cannot be negative."));

        return CurrencyCode.TryCreate(currencyCode, currencyField.Path)
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
    /// For user input, use <see cref="TryCreate(decimal, string, string?)"/> instead.
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
                Error.InvalidInput.ForField("currency", "validation.error", $"Cannot add {other.Currency} to {Currency}."));

        try { return TryCreate(Amount + other.Amount, Currency); }
        catch (OverflowException) { return Result.Fail<Money>(Error.InvalidInput.ForField("amount", "validation.error", "Addition would overflow.")); }
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
                Error.InvalidInput.ForField("currency", "validation.error", $"Cannot subtract {other.Currency} from {Currency}."));

        if (Amount < other.Amount)
            return Result.Fail<Money>(
                Error.InvalidInput.ForField("money", "validation.error", "Subtraction would result in a negative amount."));

        return TryCreate(Amount - other.Amount, Currency);
    }

    /// <summary>
    /// Multiplies the money amount by a scalar value.
    /// </summary>
    public Result<Money> Multiply(decimal multiplier)
    {
        if (multiplier < 0)
            return Result.Fail<Money>(
                Error.InvalidInput.ForField(nameof(multiplier), "validation.error", "Multiplier cannot be negative."));

        try { return TryCreate(Amount * multiplier, Currency); }
        catch (OverflowException) { return Result.Fail<Money>(Error.InvalidInput.ForField("amount", "validation.error", "Multiplication would overflow.")); }
    }

    /// <summary>
    /// Multiplies the money amount by an integer quantity.
    /// </summary>
    public Result<Money> Multiply(int quantity)
    {
        if (quantity < 0)
            return Result.Fail<Money>(
                Error.InvalidInput.ForField(nameof(quantity), "validation.error", "Quantity cannot be negative."));

        try { return TryCreate(Amount * quantity, Currency); }
        catch (OverflowException) { return Result.Fail<Money>(Error.InvalidInput.ForField("amount", "validation.error", "Multiplication would overflow.")); }
    }

    /// <summary>
    /// Divides the money amount by a scalar value.
    /// </summary>
    public Result<Money> Divide(decimal divisor)
    {
        if (divisor <= 0)
            return Result.Fail<Money>(
                Error.InvalidInput.ForField(nameof(divisor), "validation.error", "Divisor must be positive."));

        try { return TryCreate(Amount / divisor, Currency); }
        catch (OverflowException) { return Result.Fail<Money>(Error.InvalidInput.ForField("amount", "validation.error", "Division would overflow.")); }
    }

    /// <summary>
    /// Divides the money amount by an integer to split evenly.
    /// </summary>
    public Result<Money> Divide(int divisor)
    {
        if (divisor <= 0)
            return Result.Fail<Money>(
                Error.InvalidInput.ForField(nameof(divisor), "validation.error", "Divisor must be positive."));

        try { return TryCreate(Amount / divisor, Currency); }
        catch (OverflowException) { return Result.Fail<Money>(Error.InvalidInput.ForField("amount", "validation.error", "Division would overflow.")); }
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
            return Result.Fail<Money[]>(Error.InvalidInput.ForField(nameof(ratios), "validation.error", "At least one ratio required."));

        if (ratios.Any(r => r <= 0))
            return Result.Fail<Money[]>(Error.InvalidInput.ForField(nameof(ratios), "validation.error", "All ratios must be positive."));

        // Split overflow handling so the failure field accurately identifies the offending input:
        //   * ratios.Sum() overflowing int   -> "ratios" failure
        //   * Amount * multiplier / round / cast / share-mul overflowing -> "amount" failure
        int totalRatio;
        try { totalRatio = ratios.Sum(); }
        catch (OverflowException)
        {
            return Result.Fail<Money[]>(Error.InvalidInput.ForField(nameof(ratios), "validation.error", "Sum of ratios would overflow."));
        }

        try
        {
            var decimalPlaces = GetDecimalPlaces(Currency);
            var multiplier = (decimal)Math.Pow(10, decimalPlaces);
            var amountInMinorUnits = (long)Math.Round(Amount * multiplier, MidpointRounding.AwayFromZero);
            var remainder = amountInMinorUnits;
            var results = new Money[ratios.Length];

            for (int i = 0; i < ratios.Length; i++)
            {
                // The integral multiplication can overflow long for extreme amounts +
                // large ratios; force a checked context so the catch below converts
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
            return Result.Fail<Money[]>(Error.InvalidInput.ForField("amount", "validation.error", "Allocation arithmetic would overflow."));
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
    /// <param name="currencyCode">ISO 4217 currency code. Defaults to <c>"USD"</c>.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> wrapping the constructed <see cref="Money"/>. Returns the
    /// success branch when <paramref name="currencyCode"/> is a supported currency, and the
    /// failure branch when it is not — the currency code is validated at construction time,
    /// so this method is not a plain <see cref="Money"/> factory. Consumers must unwrap the
    /// result (typically with <c>.TryGetValue(...)</c> or <c>.Match(...)</c>) before using
    /// the value.
    /// </returns>
    public static Result<Money> Zero(string currencyCode = "USD") => TryCreate(0, currencyCode);

    /// <summary>
    /// Gets the equality components for value comparison.
    /// </summary>
    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Math.Round(Amount, GetDecimalPlaces(Currency)));
        components.Add((string)Currency);
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
    /// <remarks>
    /// Empty input fails because <see cref="Money"/> carries currency and there is no canonical
    /// "zero currency". Callers that have a meaningful currency for the empty case should use
    /// <see cref="Sum(IEnumerable{Money}, Money)"/> and pass a zero-amount <see cref="Money"/>
    /// in the desired currency as <c>fallback</c>.
    /// </remarks>
    public static Result<Money> Sum(IEnumerable<Money> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        using var enumerator = values.GetEnumerator();

        if (!enumerator.MoveNext())
            return Result.Fail<Money>(Error.InvalidInput.ForField(nameof(values), "validation.error", "Cannot sum an empty collection."));

        return SumNonEmpty(enumerator);
    }

    /// <summary>
    /// Sums a collection of <see cref="Money"/> values, returning <paramref name="fallback"/>
    /// when the collection is empty.
    /// </summary>
    /// <param name="values">The monetary values to sum.</param>
    /// <param name="fallback">The value to return when <paramref name="values"/> is empty.</param>
    /// <returns>
    /// Success with the total when <paramref name="values"/> is non-empty. Success with
    /// <paramref name="fallback"/> when <paramref name="values"/> is empty. Failure if the
    /// non-empty values contain mixed currencies or the addition overflows.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <paramref name="fallback"/> is consulted only when <paramref name="values"/> is empty.
    /// When <paramref name="values"/> is non-empty the result currency is inferred from the
    /// first element, exactly as in <see cref="Sum(IEnumerable{Money})"/> — the fallback's
    /// currency is irrelevant in that case. This mirrors <see cref="MonetaryAmount.Sum"/>'s
    /// empty-collection-yields-zero ergonomic for currencies that <see cref="Money"/> can
    /// represent.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Empty line items return USD 0 instead of failing.
    /// var total = Money.Sum(order.LineItems.Select(li => li.Subtotal), Money.Zero("USD").Unwrap());
    /// </code>
    /// </example>
    public static Result<Money> Sum(IEnumerable<Money> values, Money fallback)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(fallback);

        using var enumerator = values.GetEnumerator();

        if (!enumerator.MoveNext())
            return Result.Ok(fallback);

        return SumNonEmpty(enumerator);
    }

    // Sums the remaining elements of an enumerator that has already been advanced to its first
    // element. The current value of <paramref name="enumerator"/> seeds the result currency.
    // Callers are responsible for the empty-collection branch before invoking this helper.
    // The hard-coded "values" paramName matches both public overloads' parameter name.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly",
        Justification = "Continuation of Sum(values) / Sum(values, fallback); 'values' is the matching parameter name in both public callers.")]
    private static Result<Money> SumNonEmpty(IEnumerator<Money> enumerator)
    {
        var first = enumerator.Current;
        if (first is null)
            throw new ArgumentException("Collection contains a null element.", "values");

        var currency = first.Currency;
        var totalAmount = first.Amount;

        try
        {
            while (enumerator.MoveNext())
            {
                var current = enumerator.Current;
                if (current is null)
                    throw new ArgumentException("Collection contains a null element.", "values");

                if (!currency.Equals(current.Currency))
                    return Result.Fail<Money>(
                        Error.InvalidInput.ForField("currency", "validation.error", $"Cannot add {current.Currency} to {currency}."));

                totalAmount += current.Amount;
            }
        }
        catch (OverflowException)
        {
            return Result.Fail<Money>(Error.InvalidInput.ForField("amount", "validation.error", "Addition would overflow."));
        }

        return TryCreate(totalAmount, currency.Value);
    }
}