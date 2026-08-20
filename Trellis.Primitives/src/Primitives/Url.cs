namespace Trellis.Primitives;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Trellis;

/// <summary>
/// Represents a URL value object with format validation.
/// Ensures that URLs are valid and well-formed for web and API scenarios.
/// </summary>
/// <remarks>
/// <para>
/// Url is a domain primitive that encapsulates URL validation and provides:
/// <list type="bullet">
/// <item>URI format validation using .NET's Uri class</item>
/// <item>Support for HTTP and HTTPS schemes</item>
/// <item>Type safety preventing mixing of URLs with other strings</item>
/// <item>Immutability ensuring URLs cannot be changed after creation</item>
/// <item>IParsable implementation for .NET parsing conventions</item>
/// <item>JSON serialization support for APIs and persistence</item>
/// <item>Activity tracing for monitoring and diagnostics</item>
/// </list>
/// </para>
/// <para>
/// Validation rules:
/// <list type="bullet">
/// <item>Must be a valid absolute URI</item>
/// <item>Scheme must be HTTP or HTTPS</item>
/// <item>Must have a valid host</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Website URLs in user profiles</item>
/// <item>API endpoints configuration</item>
/// <item>Webhook URLs</item>
/// <item>Image and resource links</item>
/// <item>Redirect URLs</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Basic usage:
/// <code>
/// var url = Url.TryCreate("https://example.com/path");
/// // Returns: Success(Url("https://example.com/path"))
/// 
/// var withQuery = Url.TryCreate("https://api.example.com/search?q=test");
/// // Returns: Success(Url("https://api.example.com/search?q=test"))
/// 
/// var invalid = Url.TryCreate("not-a-url");
/// // Returns: Failure(Error.InvalidInput with detail "URL must be a valid absolute HTTP or HTTPS URL.")
/// </code>
/// </example>
[JsonConverter(typeof(ParsableJsonConverter<Url>))]
public class Url : ScalarValueObject<Url, string>, IScalarValue<Url, string>, IParsable<Url>
{
    private readonly Uri _uri;

    private Url(Uri uri) : base(uri.AbsoluteUri)
        => _uri = uri;

    // Field-normalization + InvalidInput failure in one place (default field name: "url").
    private static Result<Url> Invalid(string? fieldName, string reasonCode, string message) =>
        Result.Fail<Url>(
            Error.InvalidInput.ForField(fieldName.NormalizeFieldName("url"), reasonCode, message));

    /// <summary>
    /// Attempts to create a <see cref="Url"/> from the specified string.
    /// If <paramref name="fieldName"/> is not provided, validation errors use "url" as the field name.
    /// </summary>
    /// <param name="value">The URL string to validate.</param>
    /// <param name="fieldName">Optional field name for validation error messages. If not provided, defaults to "url".</param>
    /// <returns>
    /// Success with the Url if the string is a valid HTTP/HTTPS URL; otherwise Failure with <see cref="Error.InvalidInput"/>.
    /// </returns>
    public static Result<Url> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(Url) + '.' + nameof(TryCreate));

        if (string.IsNullOrWhiteSpace(value))
            return Invalid(fieldName, value is null ? ValidationCodes.ValueNotNull : ValidationCodes.ValueNotEmpty, "URL is required.");

        // Normalize input to avoid issues with accidental whitespace
        var trimmed = value.Trim();

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return Invalid(fieldName, ValidationCodes.StringUrl, "URL must be a valid absolute HTTP or HTTPS URL.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return Invalid(fieldName, ValidationCodes.StringUrl, "URL must use HTTP or HTTPS scheme.");

        return Result.Ok(new Url(uri));
    }

    /// <summary>
    /// Gets the scheme of the URL (http or https).
    /// </summary>
    public string Scheme => _uri.Scheme;

    /// <summary>
    /// Gets the host of the URL.
    /// </summary>
    public string Host => _uri.Host;

    /// <summary>
    /// Gets the port of the URL.
    /// </summary>
    public int Port => _uri.Port;

    /// <summary>
    /// Gets the path of the URL.
    /// </summary>
    public string Path => _uri.AbsolutePath;

    /// <summary>
    /// Gets the query string of the URL (including the leading '?').
    /// </summary>
    public string Query => _uri.Query;

    /// <summary>
    /// Gets the underlying <see cref="Uri"/> object.
    /// </summary>
    public Uri ToUri() => _uri;

    /// <summary>
    /// Gets whether the URL uses HTTPS.
    /// </summary>
    public bool IsSecure => _uri.Scheme == Uri.UriSchemeHttps;

    /// <summary>
    /// Parses the string representation of a URL to its <see cref="Url"/> equivalent.
    /// </summary>
    public static Url Parse(string? s, IFormatProvider? provider) =>
        StringExtensions.ParseScalarValue<Url>(s);

    /// <summary>
    /// Tries to parse a string into a <see cref="Url"/>.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Url result) =>
        StringExtensions.TryParseScalarValue(s, out result);

    /// <inheritdoc/>
    /// <remarks>Normalizes URL comparison by using the absolute URI.</remarks>
    protected override void GetEqualityComponents(ref EqualityComponents components)
        => components.Add(_uri.AbsoluteUri);
}