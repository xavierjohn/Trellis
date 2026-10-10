namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Trellis;

/// <summary>
/// ASCII hostname value object using RFC 1123 label syntax and RFC 1035 name-length limits.
/// </summary>
/// <remarks>
/// Input is trimmed and its casing is preserved. Labels contain 1 to 63 letters, digits, or
/// hyphens, with no leading or trailing hyphen. A trailing root dot is not accepted.
/// The trimmed hostname is at most 253 characters, including separating dots: RFC 1035
/// sections 2.3.4 and 3.1 limit the encoded DNS name to 255 octets, including label-length
/// bytes and the root terminator, making the wire name two octets longer than this text form.
/// </remarks>
[JsonConverter(typeof(ParsableJsonConverter<Hostname>))]
public partial class Hostname : ScalarValueObject<Hostname, string>, IScalarValue<Hostname, string>, IParsable<Hostname>
{
    private Hostname(string value) : base(value) { }

    // Field-normalization + InvalidInput failure in one place (default field name: "hostname").
    private static Result<Hostname> Invalid(string? fieldName, string reasonCode, string message) =>
        Result.Fail<Hostname>(
            Error.InvalidInput.ForField(field: fieldName.NormalizeFieldName("hostname"), code: reasonCode, detail: message));

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
            return Invalid(fieldName, value is null ? ValidationCodes.ValueNotNull : ValidationCodes.ValueNotEmpty, "Hostname is required.");
        var trimmed = value.Trim();
        if (!HostnameRegex().IsMatch(trimmed))
            return Invalid(fieldName, ValidationCodes.StringHostname, "Hostname must be RFC 1123 compliant.");
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

    [GeneratedRegex(@"^(?=.{1,253}$)([a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$")]
    private static partial Regex HostnameRegex();
}