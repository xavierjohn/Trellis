namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// ISO 639-1 language code value object.
/// </summary>
[JsonConverter(typeof(ParsableJsonConverter<LanguageCode>))]
public class LanguageCode : ScalarValueObject<LanguageCode, string>, IScalarValue<LanguageCode, string>, IParsable<LanguageCode>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageCode"/> class.
    /// </summary>
    private LanguageCode(string value) : base(value) { }

    /// <summary>
    /// Attempts to create a language code.
    /// </summary>
    public static Result<LanguageCode> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(LanguageCode) + '.' + nameof(TryCreate));
        var field = fieldName.NormalizeFieldName("languageCode");
        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<LanguageCode>(Error.UnprocessableContent.ForField(field, "validation.error", "Language code is required."));
        var code = value.Trim();
        if (code.Length != 2 || !code.All(char.IsLetter))
            return Result.Fail<LanguageCode>(Error.UnprocessableContent.ForField(field, "validation.error", "Language code must be an ISO 639-1 alpha-2 code."));
        return Result.Ok(new LanguageCode(code.ToLowerInvariant()));
    }

    /// <summary>
    /// Parses a language code.
    /// </summary>
    public static LanguageCode Parse(string? s, IFormatProvider? provider) =>
        StringExtensions.ParseScalarValue<LanguageCode>(s);

    /// <summary>
    /// Tries to parse a language code.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out LanguageCode result) =>
        StringExtensions.TryParseScalarValue(s, out result);
}