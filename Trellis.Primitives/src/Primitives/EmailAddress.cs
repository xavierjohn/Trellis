namespace Trellis.Primitives;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Trellis;

/// <summary>
/// Represents an email address value object for a common subset of RFC 5322 address syntax.
/// Keeps the supported email format and length checks in the domain model.
/// </summary>
/// <remarks>
/// <para>
/// EmailAddress is a domain primitive that encapsulates email address validation and provides:
/// <list type="bullet">
/// <item>Dot-atom-style local parts and multi-label domains, rather than the complete RFC 5322 grammar</item>
/// <item>Type safety preventing mixing of email addresses with other strings</item>
/// <item>Immutability ensuring email addresses cannot be changed after creation</item>
/// <item>IParsable implementation for .NET parsing conventions</item>
/// <item>JSON serialization support for APIs and persistence</item>
/// <item>Activity tracing for monitoring and diagnostics</item>
/// </list>
/// </para>
/// <para>
/// Validation rules:
/// <list type="bullet">
/// <item>Must not be null, empty, or whitespace</item>
/// <item>Must contain an @ symbol separating local and domain parts</item>
/// <item>Local part (before @): letters, digits, and special characters (!#$%&amp;'*+/=?^_`{|}~-), optionally separated by single dots</item>
/// <item>No leading, trailing, or consecutive dots in the local part</item>
/// <item>Domain part (after @): at least two labels of letters, digits, and hyphens, with no leading or trailing label hyphens</item>
/// <item>No 63-character per-domain-label limit is enforced; the overall address-length limit still applies</item>
/// <item>At most 254 characters overall and 64 in the local part, based on RFC 5321 length limits</item>
/// <item>Case-insensitive validation; surrounding whitespace is trimmed and stored casing is preserved</item>
/// </list>
/// </para>
/// <para>
/// Quoted local parts, comments, display names, and domain literals are not accepted.
/// This is not full RFC 5322 message parsing or an email deliverability check.
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>User account email addresses</item>
/// <item>Contact information in entities</item>
/// <item>Email notification recipients</item>
/// <item>Authentication and identity management</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Basic email address validation:
/// <code>
/// // Valid email addresses
/// var email1 = EmailAddress.TryCreate("user@example.com");
/// // Returns: Success(EmailAddress("user@example.com"))
/// 
/// var email2 = EmailAddress.TryCreate("john.doe+tag@company.co.uk");
/// // Returns: Success(EmailAddress("john.doe+tag@company.co.uk"))
/// 
/// // Invalid email addresses
/// var invalid1 = EmailAddress.TryCreate("not-an-email");
/// // Returns: Failure(Error.InvalidInput with detail "Email address is not valid.")
/// 
/// var invalid2 = EmailAddress.TryCreate("@example.com");
/// // Returns: Failure(Error.InvalidInput with detail "Email address is not valid.")
/// 
/// var invalid3 = EmailAddress.TryCreate(null);
/// // Returns: Failure(Error.InvalidInput with detail "Email address is required.")
/// </code>
/// </example>
/// <example>
/// Using in domain entities:
/// <code>
/// public class User : Entity&lt;UserId&gt;
/// {
///     public EmailAddress Email { get; private set; }
///     public FirstName FirstName { get; }
///     
///     private User(UserId id, EmailAddress email, FirstName firstName) 
///         : base(id)
///     {
///         Email = email;
///         FirstName = firstName;
///     }
///     
///     public static Result&lt;User&gt; Create(string email, string firstName) =>
///         EmailAddress.TryCreate(email)
///             .Combine(FirstName.TryCreate(firstName))
///             .Map((emailAddr, name) => new User(UserId.NewUnique(), emailAddr, name));
///     
///     public Result&lt;User&gt; ChangeEmail(string newEmail) =>
///         EmailAddress.TryCreate(newEmail)
///             .Tap(email => Email = email)
///             .Map(_ => this);
/// }
/// </code>
/// </example>
/// <example>
/// Using with field name for validation errors:
/// <code>
/// // Specify field name for better error messages
/// var result = EmailAddress.TryCreate("invalid", "userEmail");
/// // Returns: Failure(Error.InvalidInput with field "userEmail")
/// 
/// // In API validation; the application-owned RegisterAsync returns Task&lt;Result&lt;UserDto&gt;&gt;.
/// public record RegisterUserRequest(string Email, string Password);
/// 
/// builder.Services.AddTrellisAsp();
/// builder.Services.AddEndpointsApiExplorer();
///
/// app.MapPost("/register", (RegisterUserRequest request) =>
///     EmailAddress.TryCreate(request.Email, nameof(request.Email))
///         .BindAsync(email => _authService.RegisterAsync(email, request.Password))
///         .ToHttpResponseAsync());
/// 
/// // POST /register with { "email": "invalid", "password": "example" }:
/// // Default 422 RFC 9457 Problem Details (request-specific traceId omitted):
/// // {
/// //   "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
/// //   "title": "One or more validation errors occurred.",
/// //   "status": 422,
/// //   "instance": "/register",
/// //   "errors": {
/// //     "email": ["Email address is not valid."]
/// //   },
/// //   "code": "error.unspecified",
/// //   "kind": "unprocessable-content",
/// //   "fieldViolations": [{
/// //     "code": "string.email",
/// //     "detail": "Email address is not valid.",
/// //     "location": { "in": "body", "pointer": "/email" }
/// //   }]
/// // }
/// </code>
/// </example>
/// <example>
/// Using IParsable for parsing scenarios:
/// <code>
/// // Standard .NET parsing pattern
/// var email = EmailAddress.Parse("user@example.com", null);
/// // Throws FormatException if invalid
/// 
/// // TryParse pattern
/// if (EmailAddress.TryParse("user@example.com", null, out var emailAddress))
/// {
///     Console.WriteLine($"Valid email: {emailAddress.Value}");
/// }
/// else
/// {
///     Console.WriteLine("Invalid email format");
/// }
/// </code>
/// </example>
/// <example>
/// JSON serialization in APIs:
/// <code>
/// public record UserDto(EmailAddress Email, string Name);
/// 
/// // Automatic JSON serialization/deserialization
/// var user = new UserDto(
///     EmailAddress.Create("user@example.com"),
///     "John Doe"
/// );
/// 
/// // Serializes to:
/// // {
/// //   "email": "user@example.com",
/// //   "name": "John Doe"
/// // }
/// 
/// // Deserializes from JSON string to EmailAddress value object
/// </code>
/// </example>
/// <seealso cref="ScalarValueObject{TSelf, T}"/>
/// <seealso cref="RequiredString{TSelf}"/>
/// <seealso cref="IParsable{TSelf}"/>
[JsonConverter(typeof(ParsableJsonConverter<EmailAddress>))]
public partial class EmailAddress : ScalarValueObject<EmailAddress, string>, IScalarValue<EmailAddress, string>, IParsable<EmailAddress>
{
    /// <summary>
    /// The maximum length of an address, per RFC 5321 section 4.5.3.1.3: the 256-octet forward-path
    /// limit less the two angle brackets that delimit it.
    /// </summary>
    private const int MaxLength = 254;

    /// <summary>
    /// The maximum length of the local part (the text before the '@'), per RFC 5321 section 4.5.3.1.1.
    /// </summary>
    private const int MaxLocalPartLength = 64;

    private static bool HasValidLocalPartLength(string address) =>
        address.IndexOf('@') <= MaxLocalPartLength;

    private EmailAddress(string value) : base(value) { }

    /// <summary>
    /// Attempts to create an <see cref="EmailAddress"/> from the specified string. If
    /// <paramref name="fieldName"/> is not provided, validation errors use "email" as the field name.
    /// </summary>
    /// <param name="value">The email address string to validate.</param>
    /// <param name="fieldName">
    /// Optional field name to use in validation error messages. 
    /// If not provided, defaults to "email".
    /// </param>
    /// <returns>
    /// <list type="bullet">
    /// <item>Success with the EmailAddress if the string is a valid email</item>
    /// <item>Failure with an <see cref="Error.InvalidInput"/> if the email is invalid or null</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method validates the supported dot-atom-style local part and multi-label domain
    /// profile, not the complete RFC 5322 address grammar. Matching is case-insensitive;
    /// surrounding whitespace is trimmed and stored casing is preserved. Length checks enforce
    /// 254 characters overall and 64 for the local part, based on RFC 5321 limits.
    /// No 63-character per-domain-label limit is enforced.
    /// </para>
    /// <para>
    /// Activity tracing is automatically enabled for this method, allowing you to monitor
    /// email validation performance and success rates in application insights or other
    /// observability platforms.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Basic usage
    /// var result = EmailAddress.TryCreate("user@example.com");
    /// 
    /// // With custom field name for validation errors
    /// var result2 = EmailAddress.TryCreate(userInput, "contactEmail");
    /// 
    /// // In a validation chain
    /// var user = EmailAddress.TryCreate(request.Email, nameof(request.Email))
    ///     .Combine(FirstName.TryCreate(request.FirstName))
    ///     .Bind((email, name) => User.Create(email, name));
    /// </code>
    /// </example>
    public static Result<EmailAddress> TryCreate(string? value, string? fieldName = null)
    {
        using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity(nameof(EmailAddress) + '.' + nameof(TryCreate));
        var field = fieldName.NormalizeFieldName("email");

        // Absent, blank, and malformed are three different failures. Reporting `string.email` for a
        // value that was never supplied tells the caller to fix an address they did not write, and
        // leaves them unable to distinguish "you missed this field" from "this address is wrong".
        if (value is null)
            return Result.Fail<EmailAddress>(Error.InvalidInput.ForField(field: field, code: ValidationCodes.ValueNotNull, detail: "Email address is required."));

        // Normalize input: trim whitespace
        var trimmed = value.Trim();

        if (trimmed.Length == 0)
            return Result.Fail<EmailAddress>(Error.InvalidInput.ForField(field: field, code: ValidationCodes.ValueNotEmpty, detail: "Email address is required."));

        if (trimmed.Length <= MaxLength && HasValidLocalPartLength(trimmed) && EmailRegEx().IsMatch(trimmed))
            return Result.Ok(new EmailAddress(trimmed));

        return Result.Fail<EmailAddress>(Error.InvalidInput.ForField(field: field, code: ValidationCodes.StringEmail, detail: "Email address is not valid."));
    }

    /// <summary>
    /// Converts the string representation of an email address to its <see cref="EmailAddress"/> equivalent.
    /// A return value indicates whether the conversion succeeded.
    /// </summary>
    /// <param name="s">A string containing an email address to parse.</param>
    /// <param name="provider">An object that provides culture-specific formatting information (not used for email parsing).</param>
    /// <returns>An <see cref="EmailAddress"/> equivalent to the email address contained in <paramref name="s"/>.</returns>
    /// <exception cref="FormatException">
    /// Thrown when <paramref name="s"/> is not in a valid email format.
    /// </exception>
    /// <remarks>
    /// This method implements the <see cref="IParsable{TSelf}"/> interface, providing standard
    /// .NET parsing behavior. For safer parsing without exceptions, use <see cref="TryParse"/> or <see cref="TryCreate(string?, string?)"/>.
    /// </remarks>
    public static EmailAddress Parse(string? s, IFormatProvider? provider) =>
        StringExtensions.ParseScalarValue<EmailAddress>(s);

    /// <summary>
    /// Tries to parse a string into an <see cref="EmailAddress"/>.
    /// </summary>
    /// <param name="s">The string to parse.</param>
    /// <param name="provider">An object that provides culture-specific formatting information (not used for email parsing).</param>
    /// <param name="result">
    /// When this method returns, contains the <see cref="EmailAddress"/> equivalent of the string,
    /// if the conversion succeeded, or <c>null</c> if the conversion failed.
    /// </param>
    /// <returns>
    /// <c>true</c> if <paramref name="s"/> was converted successfully; otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This method implements the <see cref="IParsable{TSelf}"/> interface, providing standard
    /// .NET try-parse pattern. This is a safe alternative to <see cref="Parse"/> that doesn't throw exceptions.
    /// </remarks>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out EmailAddress result) =>
        StringExtensions.TryParseScalarValue(s, out result);

    /// <summary>
    /// Source-generated regular expression for the supported dot-atom-style email profile.
    /// </summary>
    /// <returns>A compiled <see cref="Regex"/> for email validation.</returns>
    /// <remarks>
    /// <para>
    /// This regex is generated at compile-time using the <see cref="GeneratedRegexAttribute"/>,
    /// providing optimal performance without runtime regex compilation overhead.
    /// </para>
    /// <para>
    /// Pattern matches:
    /// <list type="bullet">
    /// <item>Local part: alphanumeric and special characters (!#$%&amp;'*+/=?^_`{|}~-)</item>
    /// <item>@ symbol separator</item>
    /// <item>Domain: alphanumeric with hyphens, multiple levels separated by dots</item>
    /// </list>
    /// </para>
    /// </remarks>
    [GeneratedRegex("\\A(?:[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)\\Z",
        RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegEx();
}