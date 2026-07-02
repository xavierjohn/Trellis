namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Trellis;

/// <summary>
/// URL-safe slug value object (lowercase letters, digits, single hyphens).
/// </summary>
/// <remarks>
/// <b>Validation Rules (Opinionated):</b>
/// <list type="bullet">
/// <item>Lowercase letters only (no uppercase)</item>
/// <item>Digits allowed</item>
/// <item>Hyphens allowed but not consecutive, leading, or trailing</item>
/// </list>
/// <para>
/// <b>If these rules don't fit your domain</b> (e.g., you allow uppercase in slugs),
/// create your own Slug value object using the <see cref="ScalarValueObject{TSelf, T}"/> base class.
/// </para>
/// </remarks>
[JsonConverter(typeof(ParsableJsonConverter<Slug>))]
public partial class Slug : ScalarValueObject<Slug, string>, IScalarValue<Slug, string>, IParsable<Slug>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Slug"/> class.
    /// </summary>
    private Slug(string value) : base(value) { }

    // Field-normalization + InvalidInput failure in one place (default field name: "slug").
    private static Result<Slug> Invalid(string? fieldName, string message) =>
        Result.Fail<Slug>(
            Error.InvalidInput.ForField(fieldName.NormalizeFieldName("slug"), "validation.error", message));

    /// <summary>
    /// Attempts to create a slug.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "slug" as the field name.
    /// </summary>
    /// <param name="value">The string value to validate.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "slug".</param>
    /// <returns>Success with the Slug if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    public static Result<Slug> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Slug) + '.' + nameof(TryCreate));
        if (string.IsNullOrWhiteSpace(value))
            return Invalid(fieldName, "Slug is required.");
        var trimmed = value.Trim();
        // lower-case, numbers, hyphens, single hyphen separators
        if (!SlugRegex().IsMatch(trimmed))
            return Invalid(fieldName, "Slug must contain lower-case letters, numbers, and hyphens, without leading/trailing hyphens.");
        return Result.Ok(new Slug(trimmed));
    }

    /// <summary>
    /// Parses a slug.
    /// </summary>
    public static Slug Parse(string? s, IFormatProvider? provider) =>
        StringExtensions.ParseScalarValue<Slug>(s);

    /// <summary>
    /// Tries to parse a slug.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Slug result) =>
        StringExtensions.TryParseScalarValue(s, out result);

    [GeneratedRegex(@"^(?!-)(?!.*--)[a-z0-9-]+(?<!-)$")]
    private static partial Regex SlugRegex();
}