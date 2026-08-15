namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// Represents a percentage value object (value between 0 and 100 inclusive).
/// Ensures that percentage values are within the valid range for percentage calculations.
/// </summary>
/// <remarks>
/// <para>
/// Percentage is a domain primitive that encapsulates percentage validation and provides:
/// <list type="bullet">
/// <item>Validation ensuring value is between 0 and 100 inclusive</item>
/// <item>Type safety preventing mixing of percentages with other decimals</item>
/// <item>Immutability ensuring values cannot be changed after creation</item>
/// <item>IParsable implementation for .NET parsing conventions</item>
/// <item>JSON serialization support for APIs and persistence</item>
/// <item>Activity tracing for monitoring and diagnostics</item>
/// <item>Helper methods for percentage calculations</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Discount percentages</item>
/// <item>Tax rates</item>
/// <item>Commission rates</item>
/// <item>Progress indicators</item>
/// <item>Interest rates</item>
/// <item>Any value representing a percentage</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Basic usage:
/// <code>
/// var discount = Percentage.TryCreate(15.5m);
/// // Returns: Success(Percentage(15.5))
/// 
/// var full = Percentage.TryCreate(100m);
/// // Returns: Success(Percentage(100))
/// 
/// var zero = Percentage.TryCreate(0m);
/// // Returns: Success(Percentage(0))
/// 
/// var invalidHigh = Percentage.TryCreate(150m);
/// // Returns: Failure(Error.InvalidInput with detail "Percentage must be between 0 and 100.")
/// 
/// var invalidNegative = Percentage.TryCreate(-5m);
/// // Returns: Failure(Error.InvalidInput with detail "Percentage must be between 0 and 100.")
/// </code>
/// </example>
/// <example>
/// Using helper methods:
/// <code>
/// var percentage = Percentage.Create(20m);
/// var amount = 100m;
/// 
/// // Convert to fraction (0.2)
/// var fraction = percentage.AsFraction();
/// 
/// // Calculate percentage of a value
/// var result = percentage.Of(amount); // Returns 20m
/// </code>
/// </example>
[JsonConverter(typeof(ParsableJsonConverter<Percentage>))]
public class Percentage : ScalarValueObject<Percentage, decimal>, IScalarValue<Percentage, decimal>, IFormattableScalarValue<Percentage, decimal>, IParsable<Percentage>
{
    private Percentage(decimal value) : base(value) { }

    private static readonly Percentage _zero = new(0m);
    private static readonly Percentage _full = new(100m);

    /// <summary>
    /// Gets a <see cref="Percentage"/> representing 0%.
    /// </summary>
    public static Percentage Zero => _zero;

    /// <summary>
    /// Gets a <see cref="Percentage"/> representing 100%.
    /// </summary>
    public static Percentage Full => _full;

    // Field-normalization + InvalidInput failure in one place (default field name: "percentage").
    private static Result<Percentage> Invalid(string? fieldName, string message) =>
        Result.Fail<Percentage>(
            Error.InvalidInput.ForField(fieldName.NormalizeFieldName("percentage"), "validation.error", message));

    // Field-normalization + InvalidInput failure in one place (default field name: "fraction").
    private static Result<Percentage> InvalidFraction(string? fieldName, string message) =>
        Result.Fail<Percentage>(
            Error.InvalidInput.ForField(fieldName.NormalizeFieldName("fraction"), "validation.error", message));

    // No-span validation core. Every public factory opens exactly one span, then delegates here.
    private static Result<Percentage> Validate(decimal value, string? fieldName) =>
        value is < 0m or > 100m
            ? Invalid(fieldName, "Percentage must be between 0 and 100.")
            : Result.Ok(new Percentage(value));

    /// <summary>
    /// Attempts to create a <see cref="Percentage"/> from the specified decimal.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "percentage" as the field name.
    /// </summary>
    /// <param name="value">The decimal value to validate (0-100).</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "percentage".</param>
    /// <returns>
    /// Success with the Percentage if the value is between 0 and 100; otherwise Failure with <see cref="Error.InvalidInput"/>.
    /// </returns>
    public static Result<Percentage> TryCreate(decimal value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Percentage) + '.' + nameof(TryCreate));
        return Validate(value, fieldName);
    }

    /// <summary>
    /// Attempts to create a <see cref="Percentage"/> from the specified nullable decimal.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "percentage" as the field name.
    /// </summary>
    /// <param name="value">The nullable decimal value to validate (0-100).</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "percentage".</param>
    /// <returns>
    /// Success with the Percentage if the value is between 0 and 100; otherwise Failure with <see cref="Error.InvalidInput"/>.
    /// </returns>
    public static Result<Percentage> TryCreate(decimal? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Percentage) + '.' + nameof(TryCreate));
        return value is null
            ? Invalid(fieldName, "Percentage is required.")
            : Validate(value.Value, fieldName);
    }

    /// <summary>
    /// Attempts to create a <see cref="Percentage"/> from a string representation.
    /// Strips a trailing <c>%</c> suffix if present before parsing.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "percentage" as the field name.
    /// </summary>
    /// <param name="value">The string value to parse (must be a valid decimal, optionally with a trailing %).</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "percentage".</param>
    /// <returns>Success with the Percentage if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    /// <remarks>Delegates to the <see cref="TryCreate(string?, IFormatProvider?, string?)"/> overload using the invariant culture.</remarks>
    public static Result<Percentage> TryCreate(string? value, string? fieldName = null) =>
        TryCreate(value, null, fieldName);

    /// <summary>
    /// Attempts to create a <see cref="Percentage"/> from a string using the specified format provider.
    /// Strips a trailing <c>%</c> suffix if present before parsing.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "percentage" as the field name.
    /// </summary>
    /// <param name="value">The string value to parse (must be a valid decimal, optionally with a trailing %).</param>
    /// <param name="provider">The format provider for culture-sensitive parsing. Defaults to <see cref="System.Globalization.CultureInfo.InvariantCulture"/> when null.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "percentage".</param>
    /// <returns>Success with the Percentage if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    public static Result<Percentage> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Percentage) + '.' + nameof(TryCreate));

        if (string.IsNullOrWhiteSpace(value))
            return Invalid(fieldName, "Percentage is required.");

        var trimmed = value.TrimEnd('%', ' ');

        if (!decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return Invalid(fieldName, "Percentage must be a valid decimal.");

        return Validate(parsed, fieldName);
    }

    /// <summary>
    /// Creates a <see cref="Percentage"/> from a fraction (0.0 to 1.0).
    /// If <paramref name="fieldName"/> is not provided, validation errors use "fraction" as the field name.
    /// </summary>
    /// <param name="fraction">The fraction value (0.0 to 1.0).</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "fraction".</param>
    /// <returns>
    /// Success with the Percentage; otherwise Failure with <see cref="Error.InvalidInput"/>.
    /// </returns>
    public static Result<Percentage> FromFraction(decimal fraction, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Percentage) + '.' + nameof(FromFraction));

        if (fraction is < 0m or > 1m)
            return InvalidFraction(fieldName, "Fraction must be between 0 and 1.");

        return Validate(fraction * 100m, fieldName);
    }

    /// <summary>
    /// Returns the percentage as a fraction (0.0 to 1.0).
    /// </summary>
    /// <returns>The percentage value divided by 100.</returns>
    public decimal AsFraction() => Value / 100m;

    /// <summary>
    /// Calculates this percentage of the specified amount.
    /// </summary>
    /// <param name="amount">The amount to calculate the percentage of.</param>
    /// <returns>The percentage of the amount.</returns>
    public decimal Of(decimal amount) => amount * AsFraction();

    /// <summary>
    /// Parses the string representation of a decimal to its <see cref="Percentage"/> equivalent.
    /// </summary>
    public static Percentage Parse(string? s, IFormatProvider? provider) =>
        TryCreate(s, provider).Match(
            onSuccess: value => value,
            onFailure: error => throw new FormatException(error.GetDisplayMessage()));

    /// <summary>
    /// Tries to parse a string into a <see cref="Percentage"/>.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Percentage result)
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

    /// <summary>
    /// Explicitly converts a decimal to a <see cref="Percentage"/>.
    /// </summary>
    public static explicit operator Percentage(decimal value) => Create(value);

    /// <summary>
    /// Returns a string representation of the percentage with a % suffix.
    /// </summary>
    /// <remarks>
    /// The numeric part is formatted with <see cref="System.Globalization.CultureInfo.InvariantCulture"/>
    /// because this is the wire format <see cref="Parse"/> reads back with the invariant culture.
    /// </remarks>
    public override string ToString() => $"{Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}%";
}