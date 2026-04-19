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
/// // Returns: Failure(ValidationError("Percentage must be between 0 and 100."))
/// 
/// var invalidNegative = Percentage.TryCreate(-5m);
/// // Returns: Failure(ValidationError("Percentage must be between 0 and 100."))
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

    /// <summary>
    /// Attempts to create a <see cref="Percentage"/> from the specified decimal.
    /// </summary>
    /// <param name="value">The decimal value to validate (0-100).</param>
    /// <param name="fieldName">Optional field name to use in validation error messages.</param>
    /// <returns>
    /// Success with the Percentage if the value is between 0 and 100; otherwise Failure with a ValidationError.
    /// </returns>
    public static Result<Percentage> TryCreate(decimal value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Percentage) + '.' + nameof(TryCreate));

        var field = fieldName.NormalizeFieldName("percentage");

        if (value is < 0m or > 100m)
            return Result.Fail<Percentage>(Error.Validation("Percentage must be between 0 and 100.", field));

        return Result.Ok(new Percentage(value));
    }

    /// <summary>
    /// Attempts to create a <see cref="Percentage"/> from the specified nullable decimal.
    /// </summary>
    /// <param name="value">The nullable decimal value to validate (0-100).</param>
    /// <param name="fieldName">Optional field name to use in validation error messages.</param>
    /// <returns>
    /// Success with the Percentage if the value is between 0 and 100; otherwise Failure with a ValidationError.
    /// </returns>
    public static Result<Percentage> TryCreate(decimal? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Percentage) + '.' + nameof(TryCreate));

        var field = fieldName.NormalizeFieldName("percentage");

        if (value is null)
            return Result.Fail<Percentage>(Error.Validation("Percentage is required.", field));

        if (value.Value is < 0m or > 100m)
            return Result.Fail<Percentage>(Error.Validation("Percentage must be between 0 and 100.", field));

        return Result.Ok(new Percentage(value.Value));
    }

    /// <summary>
    /// Attempts to create a <see cref="Percentage"/> from a string representation.
    /// Strips a trailing <c>%</c> suffix if present before parsing.
    /// </summary>
    /// <param name="value">The string value to parse (must be a valid decimal, optionally with a trailing %).</param>
    /// <param name="fieldName">Optional field name for validation error messages.</param>
    /// <returns>Success with the Percentage if valid; Failure with ValidationError otherwise.</returns>
    public static Result<Percentage> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Percentage) + '.' + nameof(TryCreate));
        var field = fieldName.NormalizeFieldName("percentage");

        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<Percentage>(Error.Validation("Percentage is required.", field));

        var trimmed = value.TrimEnd('%', ' ');

        if (!decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return Result.Fail<Percentage>(Error.Validation("Percentage must be a valid decimal.", field));

        return TryCreate(parsed, fieldName);
    }

    /// <summary>
    /// Attempts to create a <see cref="Percentage"/> from a string using the specified format provider.
    /// Strips a trailing <c>%</c> suffix if present before parsing.
    /// </summary>
    /// <param name="value">The string value to parse (must be a valid decimal, optionally with a trailing %).</param>
    /// <param name="provider">The format provider for culture-sensitive parsing. Defaults to <see cref="System.Globalization.CultureInfo.InvariantCulture"/> when null.</param>
    /// <param name="fieldName">Optional field name for validation error messages.</param>
    /// <returns>Success with the Percentage if valid; Failure with ValidationError otherwise.</returns>
    public static Result<Percentage> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Percentage) + '.' + nameof(TryCreate));
        var field = fieldName.NormalizeFieldName("percentage");

        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<Percentage>(Error.Validation("Percentage is required.", field));

        var trimmed = value.TrimEnd('%', ' ');

        if (!decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return Result.Fail<Percentage>(Error.Validation("Percentage must be a valid decimal.", field));

        return TryCreate(parsed, fieldName);
    }

    /// <summary>
    /// Creates a <see cref="Percentage"/> from a fraction (0.0 to 1.0).
    /// </summary>
    /// <param name="fraction">The fraction value (0.0 to 1.0).</param>
    /// <param name="fieldName">Optional field name to use in validation error messages.</param>
    /// <returns>
    /// Success with the Percentage; otherwise Failure with a ValidationError.
    /// </returns>
    public static Result<Percentage> FromFraction(decimal fraction, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Percentage) + '.' + nameof(FromFraction));

        var field = fieldName.NormalizeFieldName("fraction");

        if (fraction is < 0m or > 1m)
            return Result.Fail<Percentage>(Error.Validation("Fraction must be between 0 and 1.", field));

        return TryCreate(fraction * 100m, fieldName);
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
            onFailure: error => throw new FormatException(error.Detail));

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
    public override string ToString() => $"{Value}%";
}