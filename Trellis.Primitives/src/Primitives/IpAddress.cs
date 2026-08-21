namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// Represents an IP address (IPv4 or IPv6) as a value object.
/// </summary>
/// <remarks>
/// Validates using <see cref="System.Net.IPAddress.TryParse(string?, out System.Net.IPAddress?)"/>.
/// Provides parsing and JSON serialization support.
/// </remarks>
[JsonConverter(typeof(ParsableJsonConverter<IpAddress>))]
public class IpAddress : ScalarValueObject<IpAddress, string>, IScalarValue<IpAddress, string>, IParsable<IpAddress>
{
    private readonly IPAddress _ip;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpAddress"/> class.
    /// </summary>
    /// <param name="value">The original string representation.</param>
    /// <param name="ip">The parsed <see cref="System.Net.IPAddress"/>.</param>
    private IpAddress(string value, IPAddress ip) : base(value) => _ip = ip;

    // Field-normalization + InvalidInput failure in one place (default field name: "ipAddress").
    private static Result<IpAddress> Invalid(string? fieldName, string reasonCode, string message) =>
        Result.Fail<IpAddress>(
            Error.InvalidInput.ForField(fieldName.NormalizeFieldName("ipAddress"), reasonCode, message));

    /// <summary>
    /// Attempts to create an IP address.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "ipAddress" as the field name.
    /// </summary>
    /// <param name="value">The string value to validate.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "ipAddress".</param>
    /// <returns>Success with the IpAddress if valid; Failure with <see cref="Error.InvalidInput"/> otherwise.</returns>
    public static Result<IpAddress> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(IpAddress) + '.' + nameof(TryCreate));
        if (string.IsNullOrWhiteSpace(value))
            return Invalid(fieldName, value is null ? ValidationCodes.ValueNotNull : ValidationCodes.ValueNotEmpty, "IP address is required.");
        var trimmed = value.Trim();
        if (!IPAddress.TryParse(trimmed, out var ip))
            return Invalid(fieldName, ValidationCodes.StringIpAddress, "IP address must be a valid IPv4 or IPv6.");
        return Result.Ok(new IpAddress(trimmed, ip));
    }

    /// <summary>
    /// Gets the underlying <see cref="System.Net.IPAddress"/>.
    /// </summary>
    public IPAddress ToIPAddress() => _ip;

    /// <summary>
    /// Parses an IP address.
    /// </summary>
    public static IpAddress Parse(string? s, IFormatProvider? provider) =>
        StringExtensions.ParseScalarValue<IpAddress>(s);

    /// <summary>
    /// Tries to parse an IP address.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out IpAddress result) =>
        StringExtensions.TryParseScalarValue(s, out result);
}