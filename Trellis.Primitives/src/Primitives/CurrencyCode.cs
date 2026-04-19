namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// Represents an ISO 4217 currency code as a value object.
/// </summary>
/// <remarks>
/// <b>Validation Rules (Opinionated):</b>
/// <para>
/// Valid codes are three alphabetic characters (e.g., USD, EUR, GBP). The stored value is uppercase.
/// </para>
/// <para>
/// <b>If these rules don't fit your domain</b> (e.g., cryptocurrency codes like BTC, ETH),
/// create your own CurrencyCode value object using the <see cref="ScalarValueObject{TSelf, T}"/> base class.
/// </para>
/// </remarks>
[JsonConverter(typeof(ParsableJsonConverter<CurrencyCode>))]
public class CurrencyCode : ScalarValueObject<CurrencyCode, string>, IScalarValue<CurrencyCode, string>, IParsable<CurrencyCode>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CurrencyCode"/> class.
    /// </summary>
    /// <param name="value">ISO 4217 currency code.</param>
    private CurrencyCode(string value) : base(value) { }

    /// <summary>
    /// Attempts to create a currency code from a 3-letter ISO 4217 code.
    /// </summary>
    public static Result<CurrencyCode> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(CurrencyCode) + '.' + nameof(TryCreate));

        var field = fieldName.NormalizeFieldName("currencyCode");

        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<CurrencyCode>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Currency code is required." })));

        var code = value.Trim();
        if (code.Length != 3 || !code.All(char.IsLetter))
            return Result.Fail<CurrencyCode>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Currency code must be a 3-letter ISO 4217 code." })));

        var upper = code.ToUpperInvariant();
        return Result.Ok(new CurrencyCode(upper));
    }

    /// <summary>
    /// Parses a currency code.
    /// </summary>
    public static CurrencyCode Parse(string? s, IFormatProvider? provider) =>
        StringExtensions.ParseScalarValue<CurrencyCode>(s);

    /// <summary>
    /// Tries to parse a currency code.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out CurrencyCode result) =>
        StringExtensions.TryParseScalarValue(s, out result);
}