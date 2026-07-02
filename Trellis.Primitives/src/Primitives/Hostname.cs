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

    // Field-normalization + InvalidInput failure in one place (default field name: "hostname").
    private static Result<Hostname> Invalid(string? fieldName, string message) =>
        Result.Fail<Hostname>(
            Error.InvalidInput.ForField(fieldName.NormalizeFieldName("hostname"), "validation.error", message));

    /// <summary>
    /// Attempts to create a hostname.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "hostname" as the field name.
    /// </summary>
    /// <param name="value">The string value to validate.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "hostname".</param>
    /// <returns>Success with the Hostname if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    public static Result<Hostname> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Hostname) + '.' + nameof(TryCreate));
        if (string.IsNullOrWhiteSpace(value))
            return Invalid(fieldName, "Hostname is required.");
        var trimmed = value.Trim();
        if (!HostnameRegex().IsMatch(trimmed))
            return Invalid(fieldName, "Hostname must be RFC 1123 compliant.");
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