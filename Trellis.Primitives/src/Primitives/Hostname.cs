namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Trellis;

/// <summary>
/// RFC 1123 compliant hostname value object.
/// </summary>
[JsonConverter(typeof(ParsableJsonConverter<Hostname>))]
public partial class Hostname : ScalarValueObject<Hostname, string>, IScalarValue<Hostname, string>, IParsable<Hostname>
{
    private Hostname(string value) : base(value) { }

    /// <summary>
    /// Attempts to create a hostname.
    /// </summary>
    public static Result<Hostname> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Hostname) + '.' + nameof(TryCreate));
        var field = fieldName.NormalizeFieldName("hostname");
        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<Hostname>(Error.UnprocessableContent.ForField(field, "validation.error", "Hostname is required."));
        var trimmed = value.Trim();
        if (!HostnameRegex().IsMatch(trimmed))
            return Result.Fail<Hostname>(Error.UnprocessableContent.ForField(field, "validation.error", "Hostname must be RFC 1123 compliant."));
        return Result.Ok(new Hostname(trimmed));
    }

    /// <summary>
    /// Parses a hostname.
    /// </summary>
    public static Hostname Parse(string? s, IFormatProvider? provider) =>
        StringExtensions.ParseScalarValue<Hostname>(s);

    /// <summary>
    /// Tries to parse a hostname.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Hostname result) =>
        StringExtensions.TryParseScalarValue(s, out result);

    // RFC 1123 hostname: labels 1-63 chars, alphanum and hyphens, no leading/trailing hyphen, total <=255
    [GeneratedRegex(@"^(?=.{1,255}$)([a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$")]
    private static partial Regex HostnameRegex();
}