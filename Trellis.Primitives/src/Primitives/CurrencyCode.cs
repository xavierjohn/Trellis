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
/// Valid codes are three ASCII letters (e.g., USD, EUR, GBP). Input is case-insensitive —
/// <c>"usd"</c>, <c>"USD"</c>, and <c>"Usd"</c> all parse; the stored value is uppercase via
/// <c>ToUpperInvariant()</c>. Non-ASCII letters such as German umlauts, Greek, or Cyrillic are
/// rejected, and codes shorter or longer than three characters are rejected.
/// </para>
/// <para>
/// <b>Scope of validation.</b> Only the ISO 4217 <i>format</i> (three ASCII letters) is
/// enforced. The ISO 4217 <i>active-code list</i> is not consulted, so syntactically valid
/// but reserved or unassigned codes such as <c>XXX</c>, <c>XTS</c>, and <c>ZZZ</c> are accepted.
/// Applications that need to restrict to currencies actually supported by a payment processor,
/// reject ISO reserved/test codes, or otherwise impose a narrower policy should layer an
/// allow-list at the application boundary.
/// </para>
/// <para>
/// <b>If these rules don't fit your domain</b> (e.g., cryptocurrency ticker symbols longer than
/// three characters like <c>USDT</c>, <c>DOGE</c>, <c>MATIC</c>), create your own currency-code
/// value object using the <see cref="ScalarValueObject{TSelf, T}"/> base class.
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

    private static Result<TValue> Invalid<TValue>(InputPointer field, string reasonCode, string message) =>
        Result.Fail<TValue>(
            Error.InvalidInput.ForField(field: field, code: reasonCode, detail: message));

    /// <summary>
    /// Attempts to create a currency code from a 3-letter ISO 4217 code.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "currencyCode" as the field name.
    /// </summary>
    /// <param name="value">The string value to validate.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "currencyCode".</param>
    /// <returns>Success with the CurrencyCode if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    public static Result<CurrencyCode> TryCreate(string? value, string? fieldName = null) =>
        TryCreate(value, fieldName, static name => InputPointer.ForProperty(name.NormalizeFieldName("currencyCode")),
            static (currency, _) => currency);

    internal static Result<TValue> TryCreate<TState, TValue>(
        string? value, TState state, Func<TState, InputPointer> getField, Func<CurrencyCode, TState, TValue> project)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(CurrencyCode) + '.' + nameof(TryCreate));

        var code = value.AsSpan().Trim();
        if (value is null || code.IsEmpty)
            return Invalid<TValue>(getField(state), value is null ? ValidationCodes.ValueNotNull : ValidationCodes.ValueNotEmpty, "Currency code is required.");

        var normalized = NormalizeCode(value, code);
        return normalized is null
            ? Invalid<TValue>(getField(state), ValidationCodes.StringCurrencyCode, "Currency code must be a 3-letter ISO 4217 code.")
            : Result.Ok(project(new CurrencyCode(normalized), state));
    }

    private static string? NormalizeCode(string value, ReadOnlySpan<char> trimmed)
    {
        if (trimmed.Length != 3)
            return null;

        var alreadyNormalized = value.Length == trimmed.Length;
        Span<char> normalized = stackalloc char[3];
        for (var i = 0; i < trimmed.Length; i++)
        {
            var letter = trimmed[i];
            if (!char.IsAsciiLetterUpper(letter))
            {
                if (!char.IsAsciiLetterLower(letter))
                    return null;

                letter = char.ToUpperInvariant(letter);
                alreadyNormalized = false;
            }

            normalized[i] = letter;
        }

        return alreadyNormalized ? value : new string(normalized);
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