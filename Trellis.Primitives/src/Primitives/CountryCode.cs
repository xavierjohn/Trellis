namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// ISO 3166-1 alpha-2 country code value object.
/// </summary>
/// <remarks>
/// <b>Validation Rules (Opinionated):</b>
/// <list type="bullet">
/// <item>Exactly 2 ASCII letters (ISO 3166-1 alpha-2 format) — Unicode letters such as German umlauts, Greek, or Cyrillic are rejected.</item>
/// <item>Normalized to uppercase</item>
/// </list>
/// <para>
/// <b>If these rules don't fit your domain</b> (e.g., you need alpha-3 or numeric codes),
/// create your own CountryCode value object using the <see cref="ScalarValueObject{TSelf, T}"/> base class.
/// </para>
/// </remarks>
[JsonConverter(typeof(ParsableJsonConverter<CountryCode>))]
public class CountryCode : ScalarValueObject<CountryCode, string>, IScalarValue<CountryCode, string>, IParsable<CountryCode>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CountryCode"/> class.
    /// </summary>
    private CountryCode(string value) : base(value) { }

    // Field-normalization + InvalidInput failure in one place (default field name: "countryCode").
    private static Result<CountryCode> Invalid(string? fieldName, string reasonCode, string message) =>
        Result.Fail<CountryCode>(
            Error.InvalidInput.ForField(fieldName.NormalizeFieldName("countryCode"), reasonCode, message));

    /// <summary>
    /// Attempts to create a country code.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "countryCode" as the field name.
    /// </summary>
    /// <param name="value">The string value to validate.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "countryCode".</param>
    /// <returns>Success with the CountryCode if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    public static Result<CountryCode> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(CountryCode) + '.' + nameof(TryCreate));
        if (string.IsNullOrWhiteSpace(value))
            return Invalid(fieldName, value is null ? ValidationCodes.ValueNotNull : ValidationCodes.ValueNotEmpty, "Country code is required.");
        var code = value.Trim();
        if (code.Length != 2 || !code.All(char.IsAsciiLetter))
            return Invalid(fieldName, ValidationCodes.StringCountryCode, "Country code must be an ISO 3166-1 alpha-2 code.");
        return Result.Ok(new CountryCode(code.ToUpperInvariant()));
    }

    /// <summary>
    /// Parses a country code.
    /// </summary>
    public static CountryCode Parse(string? s, IFormatProvider? provider) =>
        StringExtensions.ParseScalarValue<CountryCode>(s);

    /// <summary>
    /// Tries to parse a country code.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out CountryCode result) =>
        StringExtensions.TryParseScalarValue(s, out result);
}