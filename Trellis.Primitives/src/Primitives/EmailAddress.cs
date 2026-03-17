namespace Trellis.Primitives;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Trellis;

/// <summary>
/// Represents an email address value object with RFC 5322-compliant validation.
/// Ensures that email addresses are syntactically valid and prevents invalid email data in the domain model.
/// </summary>
/// <remarks>
/// <para>
/// EmailAddress is a domain primitive that encapsulates email address validation and provides:
/// <list type="bullet">
/// <item>RFC 5322 email format validation using a compiled regex</item>
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
/// <item>Must not be null or empty</item>
/// <item>Must contain an @ symbol separating local and domain parts</item>
/// <item>Local part (before @): letters, digits, and special characters (!#$%&amp;'*+/=?^_`{|}~-)</item>
/// <item>Domain part (after @): letters, digits, hyphens, and dots with valid structure</item>
/// <item>Case-insensitive validation (email is stored as provided, not normalized)</item>
/// </list>
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
/// // Returns: Failure(ValidationError("Email address is not valid."))
/// 
/// var invalid2 = EmailAddress.TryCreate("@example.com");
/// // Returns: Failure(ValidationError("Email address is not valid."))
/// 
/// var invalid3 = EmailAddress.TryCreate(null);
/// // Returns: Failure(ValidationError("Email address is not valid."))
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
/// // Returns: Failure(ValidationError("Email address is not valid.", fieldName: "userEmail"))
/// 
/// // In API validation
/// public record RegisterUserRequest(string Email, string Password);
/// 
/// app.MapPost("/register", (RegisterUserRequest request) =>
///     EmailAddress.TryCreate(request.Email, nameof(request.Email))
///         .Bind(email => _authService.RegisterAsync(email, request.Password))
///         .ToHttpResult());
/// 
/// // Invalid email response:
/// // {
/// //   "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
/// //   "title": "One or more validation errors occurred.",
/// //   "status": 400,
/// //   "errors": {
/// //     "email": ["Email address is not valid."]
/// //   }
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
    private EmailAddress(string value) : base(value) { }

    /// <summary>
    /// Attempts to create an <see cref="EmailAddress"/> from the specified string.
    /// This overload is required by the <see cref="IScalarValue{TSelf, TPrimitive}"/> interface
    /// for automatic model binding and JSON deserialization.
    /// </summary>
    /// <param name="value">The email address string to validate.</param>
    /// <param name="fieldName">
    /// Optional field name to use in validation error messages. 
    /// If not provided, defaults to "email" (camelCase).
    /// </param>
    /// <returns>
    /// <list type="bullet">
    /// <item>Success with the EmailAddress if the string is a valid email</item>
    /// <item>Failure with a <see cref="ValidationError"/> if the email is invalid or null</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method performs comprehensive email validation using a regex pattern that matches
    /// RFC 5322 email address syntax. The validation is case-insensitive.
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
        if (value is not null)
        {
            // Normalize input: trim whitespace
            var trimmed = value.Trim();

            if (trimmed.Length > 0)
            {
                var isEmail = EmailRegEx().IsMatch(trimmed);
                if (isEmail)
                {
                    return new EmailAddress(trimmed);
                }
            }
        }

        return Result.Failure<EmailAddress>(Error.Validation("Email address is not valid.", fieldName.NormalizeFieldName("email")));
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
    /// Compiled regular expression for RFC 5322-compliant email validation.
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