namespace Trellis.Primitives;

using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Trellis;

/// <summary>
/// Represents a phone number value object with E.164 format validation.
/// Ensures that phone numbers follow international standards for telephony.
/// </summary>
/// <remarks>
/// <para>
/// PhoneNumber is a domain primitive that encapsulates phone number validation and provides:
/// <list type="bullet">
/// <item>E.164 format validation (international phone number standard)</item>
/// <item>Type safety preventing mixing of phone numbers with other strings</item>
/// <item>Immutability ensuring phone numbers cannot be changed after creation</item>
/// <item>IParsable implementation for .NET parsing conventions</item>
/// <item>JSON serialization support for APIs and persistence</item>
/// <item>Activity tracing for monitoring and diagnostics</item>
/// </list>
/// </para>
/// <para>
/// E.164 format rules:
/// <list type="bullet">
/// <item>Starts with a '+' sign</item>
/// <item>Country code (1-3 digits)</item>
/// <item>Subscriber number (up to 12 digits)</item>
/// <item>Maximum total length: 15 digits (excluding the '+' sign)</item>
/// <item>Minimum total length: 8 digits (excluding the '+' sign)</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Contact information in entities</item>
/// <item>SMS notification recipients</item>
/// <item>Two-factor authentication</item>
/// <item>Customer support systems</item>
/// </list>
/// </para>
/// <para>
/// <b>Note: Opinionated Implementation</b> - If you need different phone number formats
/// (e.g., with extensions like +1-415-555-1234 ext. 123), create your own PhoneNumber
/// value object using the <see cref="ScalarValueObject{TSelf, T}"/> base class.
/// </para>
/// </remarks>
/// <example>
/// Basic usage:
/// <code>
/// var phone = PhoneNumber.TryCreate("+14155551234");
/// // Returns: Success(PhoneNumber("+14155551234"))
/// 
/// var invalid = PhoneNumber.TryCreate("555-1234");
/// // Returns: Failure(Error.UnprocessableContent with detail "Phone number must be in E.164 format (e.g., +14155551234).")
/// </code>
/// </example>
[JsonConverter(typeof(ParsableJsonConverter<PhoneNumber>))]
public partial class PhoneNumber : ScalarValueObject<PhoneNumber, string>, IScalarValue<PhoneNumber, string>, IParsable<PhoneNumber>
{
    private static readonly FrozenSet<string> s_twoDigitCountryCodes = new[]
    {
        "20", "27", "30", "31", "32", "33", "34", "36", "39", "40", "41", "43", "44", "45", "46", "47", "48", "49",
        "51", "52", "53", "54", "55", "56", "57", "58", "60", "61", "62", "63", "64", "65", "66",
        "81", "82", "84", "86", "90", "91", "92", "93", "94", "95", "98"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> s_threeDigitCountryCodes = new[]
    {
        "211", "212", "213", "216", "218",
        "220", "221", "222", "223", "224", "225", "226", "227", "228", "229",
        "230", "231", "232", "233", "234", "235", "236", "237", "238", "239",
        "240", "241", "242", "243", "244", "245", "246", "247", "248", "249",
        "250", "251", "252", "253", "254", "255", "256", "257", "258",
        "260", "261", "262", "263", "264", "265", "266", "267", "268", "269",
        "290", "291", "297", "298", "299",
        "350", "351", "352", "353", "354", "355", "356", "357", "358", "359",
        "370", "371", "372", "373", "374", "375", "376", "377", "378", "379",
        "380", "381", "382", "383", "385", "386", "387", "389",
        "420", "421", "423",
        "500", "501", "502", "503", "504", "505", "506", "507", "508", "509",
        "590", "591", "592", "593", "594", "595", "596", "597", "598", "599",
        "670", "672", "673", "674", "675", "676", "677", "678", "679",
        "680", "681", "682", "683", "685", "686", "687", "688", "689",
        "690", "691", "692",
        "800", "808",
        "850", "852", "853", "855", "856",
        "870", "880", "881", "882", "883", "886",
        "960", "961", "962", "963", "964", "965", "966", "967", "968",
        "970", "971", "972", "973", "974", "975", "976", "977", "979",
        "992", "993", "994", "995", "996", "998"
    }.ToFrozenSet(StringComparer.Ordinal);

    private PhoneNumber(string value) : base(value) { }

    /// <summary>
    /// Attempts to create a <see cref="PhoneNumber"/> from the specified string.
    /// </summary>
    /// <param name="value">The phone number string to validate.</param>
    /// <param name="fieldName">Optional field name to use in validation error messages.</param>
    /// <returns>
    /// Success with the PhoneNumber if the string is in E.164 format; otherwise Failure with <see cref="Error.UnprocessableContent"/>.
    /// </returns>
    public static Result<PhoneNumber> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(PhoneNumber) + '.' + nameof(TryCreate));

        var field = fieldName.NormalizeFieldName("phoneNumber");

        if (string.IsNullOrWhiteSpace(value))
            return Result.Fail<PhoneNumber>(Error.UnprocessableContent.ForField(field, "validation.error", "Phone number is required."));

        // Normalize: remove spaces, dashes, and parentheses for validation
        var normalized = NormalizeRegex().Replace(value.Trim(), "");

        // Validate E.164 format
        if (!E164Regex().IsMatch(normalized))
            return Result.Fail<PhoneNumber>(Error.UnprocessableContent.ForField(field, "validation.error", "Phone number must be in E.164 format (e.g., +14155551234)."));

        return Result.Ok(new PhoneNumber(normalized));
    }

    /// <summary>
    /// Parses the string representation of a phone number to its <see cref="PhoneNumber"/> equivalent.
    /// </summary>
    public static PhoneNumber Parse(string? s, IFormatProvider? provider) =>
        StringExtensions.ParseScalarValue<PhoneNumber>(s);

    /// <summary>
    /// Tries to parse a string into a <see cref="PhoneNumber"/>.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out PhoneNumber result) =>
        StringExtensions.TryParseScalarValue(s, out result);

    /// <summary>
    /// Gets the country calling code portion of the phone number.
    /// </summary>
    /// <returns>The country calling code (digits after <c>+</c> and before the subscriber number).</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the phone number's prefix does not match any assigned ITU-T E.164 country
    /// calling code. <see cref="TryCreate"/> validates only E.164 shape, not assigned-code
    /// membership, so a phone number that passes <c>TryCreate</c> can still have a prefix that
    /// is not a real calling code (e.g. unassigned ranges, or a code added after this library
    /// shipped).
    /// </exception>
    public string GetCountryCode()
    {
        // E.164 country codes are 1-3 digits and require longest-prefix matching
        // against the assigned calling-code set.
        var digits = Value[1..]; // Skip the '+'

        if (digits.StartsWith('1') || digits.StartsWith('7'))
            return digits[..1];

        if (digits.Length >= 3 && s_threeDigitCountryCodes.Contains(digits[..3]))
            return digits[..3];

        if (digits.Length >= 2 && s_twoDigitCountryCodes.Contains(digits[..2]))
            return digits[..2];

        throw new InvalidOperationException(
            $"Phone number '{Value}' passed E.164 format validation but does not start with a recognized " +
            $"country calling code. This may indicate stale lookup tables in Trellis.Primitives or a " +
            $"malformed input that happens to satisfy the E.164 length/character rules.");
    }

    /// <summary>
    /// Compiled regex for E.164 phone number format validation.
    /// Format: +[country code][subscriber number]
    /// Length: 8-15 digits after the +
    /// </summary>
    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164Regex();

    /// <summary>
    /// Regex to remove common formatting characters for normalization.
    /// </summary>
    [GeneratedRegex(@"[\s\-\(\)]")]
    private static partial Regex NormalizeRegex();
}