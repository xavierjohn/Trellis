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

    /// <summary>
    /// Attempts to create a slug.
    /// </summary>
    public static Result<Slug> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Slug) + '.' + nameof(TryCreate));
        var field = fieldName.NormalizeFieldName("slug");
        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<Slug>(Error.Validation("Slug is required.", field));
        var trimmed = value.Trim();
        // lower-case, numbers, hyphens, single hyphen separators
        if (!SlugRegex().IsMatch(trimmed))
            return Result.Fail<Slug>(Error.Validation("Slug must contain lower-case letters, numbers, and hyphens, without leading/trailing hyphens.", field));
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