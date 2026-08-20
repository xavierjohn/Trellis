namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// Age value object with validation for ages 0-150.
/// </summary>
/// <remarks>
/// <b>Validation Rules (Opinionated):</b>
/// <list type="bullet">
/// <item>Must be non-negative (>= 0)</item>
/// <item>Must be realistic (&lt;= 150)</item>
/// </list>
/// <para>
/// <b>If these rules don't fit your domain</b>, create your own Age value object
/// using the <see cref="ScalarValueObject{TSelf, T}"/> base class from the DomainDrivenDesign package.
/// </para>
/// </remarks>
[JsonConverter(typeof(ParsableJsonConverter<Age>))]
public class Age : ScalarValueObject<Age, int>, IScalarValue<Age, int>, IFormattableScalarValue<Age, int>, IParsable<Age>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Age"/> class.
    /// </summary>
    private Age(int value) : base(value) { }

    // Field-normalization + InvalidInput failure in one place (default field name: "age").
    private static Result<Age> Invalid(string? fieldName, string message) =>
        Result.Fail<Age>(
            Error.InvalidInput.ForField(fieldName.NormalizeFieldName("age"), "validation.error", message));

    // No-span validation core. Every public factory opens exactly one span, then delegates here.
    private static Result<Age> Validate(int value, string? fieldName)
    {
        if (value < 0)
            return Invalid(fieldName, "Age must be non-negative.");
        if (value > 150)
            return Invalid(fieldName, "Age is unrealistically high.");
        return Result.Ok(new Age(value));
    }

    /// <summary>
    /// Attempts to create an age.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "age" as the field name.
    /// </summary>
    /// <param name="value">The integer age value to validate.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "age".</param>
    /// <returns>Success with the Age if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    public static Result<Age> TryCreate(int value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Age) + '.' + nameof(TryCreate));
        return Validate(value, fieldName);
    }

    /// <summary>
    /// Attempts to create an <see cref="Age"/> from a string representation.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "age" as the field name.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "age".</param>
    /// <returns>Success with the Age if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    /// <remarks>Delegates to the <see cref="TryCreate(string?, IFormatProvider?, string?)"/> overload using the invariant culture.</remarks>
    public static Result<Age> TryCreate(string? value, string? fieldName = null) =>
        TryCreate(value, null, fieldName);

    /// <summary>
    /// Attempts to create an <see cref="Age"/> from a string using the specified format provider.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "age" as the field name.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <param name="provider">The format provider for culture-sensitive parsing. Defaults to <see cref="System.Globalization.CultureInfo.InvariantCulture"/> when null.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "age".</param>
    /// <returns>Success with the Age if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    public static Result<Age> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Age) + '.' + nameof(TryCreate));

        if (string.IsNullOrWhiteSpace(value))
            return Invalid(fieldName, "Age is required.");

        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, provider ?? System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return Invalid(fieldName, "Age must be a valid integer.");

        return Validate(parsed, fieldName);
    }

    /// <summary>
    /// Parses an age.
    /// </summary>
    public static Age Parse(string? s, IFormatProvider? provider) =>
        TryCreate(s, provider).Match(
            onSuccess: value => value,
            onFailure: error => throw new TrellisValidationFormatException(error.GetDisplayMessage(), error as Error.InvalidInput));

    /// <summary>
    /// Tries to parse an age.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Age result)
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
}