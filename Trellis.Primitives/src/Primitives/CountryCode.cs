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
/// <item>Exactly 2 letters (ISO 3166-1 alpha-2 format)</item>
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

    /// <summary>
    /// Attempts to create a country code.
    /// </summary>
    public static Result<CountryCode> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(CountryCode) + '.' + nameof(TryCreate));
        var field = fieldName.NormalizeFieldName("countryCode");
        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<CountryCode>(Error.UnprocessableContent.ForField(field, "validation.error", "Country code is required."));
        var code = value.Trim();
        if (code.Length != 2 || !code.All(char.IsLetter))
            return Result.Fail<CountryCode>(Error.UnprocessableContent.ForField(field, "validation.error", "Country code must be an ISO 3166-1 alpha-2 code."));
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