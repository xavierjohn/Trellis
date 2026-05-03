namespace Trellis;

/// <summary>
/// Base class for creating strongly-typed string value objects that cannot be null or empty.
/// Provides a foundation for domain primitives like names, descriptions, codes, and other textual concepts.
/// </summary>
/// <remarks>
/// <para>
/// This class extends <see cref="ScalarValueObject{TSelf, T}"/> to provide a specialized base for string-based value objects
/// with automatic validation that prevents null or empty strings. When used with the <c>partial</c> keyword,
/// the PrimitiveValueObjectGenerator source generator automatically creates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, string&gt;</c> implementation for ASP.NET Core automatic validation</item>
/// <item><c>TryCreate(string)</c> - Factory method for non-nullable strings (required by IScalarValue)</item>
/// <item><c>TryCreate(string?, string?)</c> - Factory method with null/empty/whitespace validation and custom field name</item>
/// <item><c>IParsable&lt;T&gt;</c> implementation (<c>Parse</c>, <c>TryParse</c>)</item>
/// <item>JSON serialization support via <c>ParsableJsonConverter&lt;T&gt;</c></item>
/// <item>Explicit cast operator from string</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Person names (FirstName, LastName, FullName)</item>
/// <item>Product attributes (ProductName, Description, SKU)</item>
/// <item>Location data (City, State, Country, PostalCode)</item>
/// <item>Business identifiers (CompanyName, TaxId, AccountNumber)</item>
/// <item>Any domain concept represented by required text</item>
/// </list>
/// </para>
/// <para>
/// <strong>String length constraints:</strong> Apply the <see cref="StringLengthAttribute"/> to enforce
/// minimum and/or maximum lengths at creation time:
/// <code>
/// [StringLength(50)]
/// public partial class FirstName : RequiredString&lt;FirstName&gt; { }
/// 
/// [StringLength(500, MinimumLength = 10)]
/// public partial class Description : RequiredString&lt;Description&gt; { }
/// </code>
/// </para>
/// <para>
/// Benefits over plain strings:
/// <list type="bullet">
/// <item><strong>Type safety</strong>: Cannot mix FirstName with LastName</item>
/// <item><strong>Validation</strong>: Prevents null/empty strings at creation time</item>
/// <item><strong>Length constraints</strong>: Optional min/max length via <see cref="StringLengthAttribute"/></item>
/// <item><strong>Domain clarity</strong>: Self-documenting code that expresses intent</item>
/// <item><strong>Consistency</strong>: Centralized trimming and normalization</item>
/// <item><strong>Testability</strong>: Easy to test validation rules in isolation</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Creating a strongly-typed name value object:
/// <code>
/// // Define the value object (partial keyword enables source generation)
/// public partial class FirstName : RequiredString&lt;FirstName&gt;
/// {
/// }
/// 
/// // The source generator automatically creates:
/// // - IScalarValue&lt;FirstName, string&gt; interface implementation
/// // - public static Result&lt;FirstName&gt; TryCreate(string value)
/// // - public static Result&lt;FirstName&gt; TryCreate(string? value, string? fieldName = null)
/// // - public static FirstName Parse(string s, IFormatProvider? provider)
/// // - public static bool TryParse(string? s, IFormatProvider? provider, out FirstName result)
/// // - public static explicit operator FirstName(string value)
/// // - private FirstName(string value) : base(value) { }
/// 
/// // Usage examples:
/// 
/// // Create with validation
/// var result1 = FirstName.TryCreate("John");
/// // Returns: Success(FirstName("John"))
/// 
/// var result2 = FirstName.TryCreate("");
/// // Returns: Failure(Error.UnprocessableContent with detail "First Name cannot be empty.")
/// 
/// var result3 = FirstName.TryCreate(null);
/// // Returns: Failure(Error.UnprocessableContent with detail "First Name cannot be empty.")
/// 
/// var result4 = FirstName.TryCreate("  John  ");
/// // Returns: Success(FirstName("John")) - automatically trimmed
/// 
/// // With custom field name for validation errors
/// var result5 = FirstName.TryCreate(input, "user.firstName");
/// // Error field will be "user.firstName" instead of default "firstName"
/// 
/// // Using in entity creation
/// public class Person : Entity&lt;PersonId&gt;
/// {
///     public FirstName FirstName { get; }
///     public LastName LastName { get; }
///     
///     public static Result&lt;Person&gt; Create(string firstName, string lastName) =>
///         FirstName.TryCreate(firstName)
///             .Combine(LastName.TryCreate(lastName))
///             .Map((first, last) => new Person(PersonId.NewUniqueV7(), first, last));
///     
///     private Person(PersonId id, FirstName firstName, LastName lastName) 
///         : base(id)
///     {
///         FirstName = firstName;
///         LastName = lastName;
///     }
/// }
/// </code>
/// </example>
/// <example>
/// ASP.NET Core automatic validation (no manual Result.Combine needed):
/// <code>
/// // 1. Register automatic validation in Program.cs
/// builder.Services
///     .AddControllers()
///     .AddScalarValueObjectValidation(); // Enables automatic validation!
///
/// // 2. Define your DTO with value objects
/// public record RegisterUserDto
/// {
///     public FirstName FirstName { get; init; } = null!;
///     public LastName LastName { get; init; } = null!;
///     public EmailAddress Email { get; init; } = null!;
/// }
///
/// // 3. Use in controllers - automatic validation!
/// [ApiController]
/// [Route("api/users")]
/// public class UsersController : ControllerBase
/// {
///     [HttpPost]
///     public IActionResult Register(RegisterUserDto dto)
///     {
///         // If we reach here, dto is FULLY validated!
///         // No Result.Combine() needed - validation happens automatically during model binding
///         var user = new User(dto.FirstName, dto.LastName, dto.Email);
///         return Ok(user);
///     }
/// }
///
/// // Invalid request automatically returns 422 Unprocessable Content:
/// // POST /api/users with { "firstName": "", "lastName": "Doe", "email": "test@example.com" }
/// // Response: 422 Unprocessable Content
/// // {
/// //   "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
/// //   "title": "One or more validation errors occurred.",
/// //   "status": 422,
/// //   "errors": {
/// //     "firstName": ["First Name cannot be empty."]
/// //   }
/// // }
/// </code>
/// </example>
/// <example>
/// Using in API validation (manual approach):
/// <code>
/// // Request DTO
/// public record CreateUserRequest(string FirstName, string LastName, string Email);
/// 
/// // API endpoint with automatic validation
/// app.MapPost("/users", (CreateUserRequest request) =>
///     FirstName.TryCreate(request.FirstName, nameof(request.FirstName))
///         .Combine(LastName.TryCreate(request.LastName, nameof(request.LastName)))
///         .Combine(EmailAddress.TryCreate(request.Email, nameof(request.Email)))
///         .Bind((first, last, email) => User.Create(first, last, email))
///         .ToHttpResponse());
/// 
/// // POST /users with empty FirstName:
/// // Response: 422 Unprocessable Content
/// // {
/// //   "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
/// //   "title": "One or more validation errors occurred.",
/// //   "status": 422,
/// //   "errors": {
/// //     "firstName": ["First Name cannot be empty."]
/// //   }
/// // }
/// </code>
/// </example>
/// <example>
/// Multiple string-based value objects:
/// <code>
/// public partial class FirstName : RequiredString&lt;FirstName&gt; { }
/// public partial class LastName : RequiredString&lt;LastName&gt; { }
/// public partial class CompanyName : RequiredString&lt;CompanyName&gt; { }
/// public partial class ProductName : RequiredString&lt;ProductName&gt; { }
/// public partial class Description : RequiredString&lt;Description&gt; { }
/// 
/// public class Product : Entity&lt;ProductId&gt;
/// {
///     public ProductName Name { get; private set; }
///     public Description Description { get; private set; }
///     
///     public Result&lt;Product&gt; UpdateName(ProductName newName) =>
///         newName.ToResult()
///             .Tap(name => Name = name)
///             .Map(_ => this);
///     
///     // Compiler prevents mixing types:
///     // UpdateName(description); // Won't compile!
///     // UpdateName(firstName);   // Won't compile!
/// }
/// </code>
/// </example>
/// <example>
/// Advanced: Adding custom validation to derived types:
/// <code>
/// // Use [StringLength] for length, add custom validation for format rules
/// [StringLength(20)]
/// public partial class ProductSKU : RequiredString&lt;ProductSKU&gt;
/// {
///     // Additional format validation via custom factory method
///     public static Result&lt;ProductSKU&gt; TryCreateWithValidation(string? value) =>
///         TryCreate(value) // Generated: validates non-empty + length &lt;= 20
///             .Ensure(sku => sku.Value.All(c => char.IsLetterOrDigit(c) || c == '-'),
///                    new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("sku"), "validation.error") { Detail = "SKU can only contain letters, digits, and hyphens" })));
/// }
/// 
/// // Usage
/// var result = ProductSKU.TryCreateWithValidation("PROD-12345");
/// // Success
/// 
/// var tooLong = ProductSKU.TryCreateWithValidation(new string('A', 21));
/// // Failure: "Product SKU must be 20 characters or fewer."
/// 
/// var invalid = ProductSKU.TryCreateWithValidation("PROD@12345");
/// // Failure: "SKU can only contain letters, digits, and hyphens"
/// </code>
/// </example>
/// <example>
/// String length constraints with <see cref="StringLengthAttribute"/>:
/// <code>
/// // Maximum length only — rejects strings longer than 50 characters
/// [StringLength(50)]
/// public partial class FirstName : RequiredString&lt;FirstName&gt; { }
/// 
/// var ok = FirstName.TryCreate("John");      // Success
/// var tooLong = FirstName.TryCreate(new string('x', 51)); // Failure: "First Name must be 50 characters or fewer."
/// 
/// // Both minimum and maximum length
/// [StringLength(500, MinimumLength = 10)]
/// public partial class Description : RequiredString&lt;Description&gt; { }
/// 
/// var tooShort = Description.TryCreate("Hi");        // Failure: "Description must be at least 10 characters."
/// var tooLong2 = Description.TryCreate(new string('x', 501)); // Failure: "Description must be 500 characters or fewer."
/// </code>
/// </example>
/// <seealso cref="ScalarValueObject{TSelf, T}"/>
/// <seealso cref="RequiredGuid{TSelf}"/>
/// <seealso href="xref:Trellis.Primitives.EmailAddress"/>
public abstract class RequiredString<TSelf> : ScalarValueObject<TSelf, string>
    where TSelf : RequiredString<TSelf>, IScalarValue<TSelf, string>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredString{TSelf}"/> class with the specified string value.
    /// </summary>
    /// <param name="value">The string value. Must not be null or empty.</param>
    /// <remarks>
    /// <para>
    /// This constructor is protected and should be called by derived classes.
    /// When using the source generator (with <c>partial</c> keyword), a private constructor
    /// is automatically generated that includes validation and trimming.
    /// </para>
    /// <para>
    /// Direct instantiation should be avoided. Instead, use the generated factory method:
    /// <list type="bullet">
    /// <item><c>TryCreate(string?, string?)</c> - Create from string with validation and trimming</item>
    /// </list>
    /// </para>
    /// <para>
    /// The generated TryCreate method automatically:
    /// <list type="bullet">
    /// <item>Returns validation error for null values</item>
    /// <item>Returns validation error for empty strings</item>
    /// <item>Returns validation error for whitespace-only strings</item>
    /// <item>Trims leading and trailing whitespace from valid strings</item>
    /// </list>
    /// </para>
    /// </remarks>
    protected RequiredString(string value) : base(value)
    {
    }

    /// <summary>
    /// Gets the length of the string value.
    /// Enables natural LINQ queries like <c>c.Name.Length > 5</c> without accessing <c>.Value</c>.
    /// </summary>
    public int Length => Value.Length;

    /// <summary>
    /// Returns whether the string value starts with the specified prefix.
    /// Enables natural LINQ queries like <c>c.Name.StartsWith("Al")</c> without accessing <c>.Value</c>.
    /// </summary>
    // Single-parameter overloads match what EF Core's ScalarValueExpressionRewriter
    // targets on string — the rewriter maps Name.StartsWith(x) to ((string)Name).StartsWith(x).
    // Adding StringComparison.Ordinal would produce a two-parameter call that EF Core cannot translate.
#pragma warning disable CA1310 // Specify StringComparison for correctness
    public bool StartsWith(string value) => Value.StartsWith(value);

    /// <summary>
    /// Returns whether the string value contains the specified substring.
    /// Enables natural LINQ queries like <c>c.Name.Contains("li")</c> without accessing <c>.Value</c>.
    /// </summary>
    public bool Contains(string value) => Value.Contains(value);

    /// <summary>
    /// Returns whether the string value ends with the specified suffix.
    /// Enables natural LINQ queries like <c>c.Name.EndsWith("ce")</c> without accessing <c>.Value</c>.
    /// </summary>
    public bool EndsWith(string value) => Value.EndsWith(value);
#pragma warning restore CA1310
}
