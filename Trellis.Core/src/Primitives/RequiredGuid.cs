namespace Trellis;

/// <summary>
/// Base class for creating strongly-typed GUID value objects. Rejects only <c>null</c> by
/// default; <see cref="System.Guid.Empty"/> rejection is opt-in via the
/// <see cref="NotDefaultAttribute"/> attribute. Recommended for any GUID type used as an
/// <c>Aggregate&lt;TId&gt;</c> / <c>Entity&lt;TId&gt;</c> ID or as an EF-mapped property to
/// preserve the database invariant guarantee enforced by <c>TrellisScalarConverter</c> on
/// rehydration.
/// </summary>
/// <remarks>
/// <para>
/// This class extends <see cref="ScalarValueObject{TSelf, T}"/> to provide a specialized base for GUID-based value objects.
/// When used with the <c>partial</c> keyword,
/// the PrimitiveValueObjectGenerator source generator automatically creates:
/// <list type="bullet">
/// <item><c>IScalarValue&lt;TSelf, Guid&gt;</c> implementation for ASP.NET Core automatic validation</item>
/// <item><c>NewUniqueV4()</c> - Factory method for generating new unique Version 4 (random) identifiers</item>
/// <item><c>NewUniqueV7()</c> / <c>NewUniqueV7(TimeProvider)</c> - Factory methods for generating new unique Version 7 (time-ordered) identifiers</item>
/// <item><c>TryCreate(Guid)</c> - Factory method for non-nullable GUIDs (required by IScalarValue)</item>
/// <item><c>TryCreate(Guid?, string?)</c> - Factory method with null-only rejection by default (add <see cref="NotDefaultAttribute"/> for <see cref="Guid.Empty"/> rejection) and custom field name</item>
/// <item><c>TryCreate(string?, string?)</c> - Factory method for parsing strings with validation</item>
/// <item><c>IParsable&lt;T&gt;</c> implementation (<c>Parse</c>, <c>TryParse</c>)</item>
/// <item>JSON serialization support via <c>ParsableJsonConverter&lt;T&gt;</c></item>
/// <item>Explicit cast operator from Guid</item>
/// <item>OpenTelemetry activity tracing</item>
/// </list>
/// </para>
/// <para>
/// Common use cases:
/// <list type="bullet">
/// <item>Entity identifiers (CustomerId, OrderId, ProductId)</item>
/// <item>Correlation IDs for distributed tracing</item>
/// <item>Session or transaction identifiers</item>
/// <item>Any domain concept requiring a globally unique, non-empty identifier</item>
/// </list>
/// </para>
/// <para>
/// Benefits over plain GUIDs:
/// <list type="bullet">
/// <item><strong>Type safety</strong>: Cannot accidentally use CustomerId where OrderId is expected</item>
/// <item><strong>Validation</strong>: Prevents null at creation time; opt into <see cref="Guid.Empty"/> rejection with <c>[NotDefault]</c></item>
/// <item><strong>Domain clarity</strong>: Makes code more self-documenting and expressive</item>
/// <item><strong>Serialization</strong>: Consistent JSON and database representation</item>
/// <item><strong>Factory methods</strong>: Clean API for creating new unique identifiers</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Creating a strongly-typed entity identifier:
/// <code>
/// // Define the value object (partial keyword enables source generation)
/// public partial class CustomerId : RequiredGuid&lt;CustomerId&gt;
/// {
/// }
/// 
/// // The source generator automatically creates:
/// // - IScalarValue&lt;CustomerId, Guid&gt; interface implementation
/// // - public static CustomerId NewUniqueV4() => new(Guid.NewGuid());        // Version 4 (random)
/// // - public static CustomerId NewUniqueV7() => new(Guid.CreateVersion7()); // Version 7 (time-ordered)
/// // - public static CustomerId NewUniqueV7(TimeProvider timeProvider) => new(Guid.CreateVersion7(timeProvider.GetUtcNow()));
/// // - public static Result&lt;CustomerId&gt; TryCreate(Guid value)
/// // - public static Result&lt;CustomerId&gt; TryCreate(Guid? value, string? fieldName = null)
/// // - public static Result&lt;CustomerId&gt; TryCreate(string? value, string? fieldName = null)
/// // - public static CustomerId Parse(string s, IFormatProvider? provider)
/// // - public static bool TryParse(string? s, IFormatProvider? provider, out CustomerId result)
/// // - public static explicit operator CustomerId(Guid value)
/// // - private CustomerId(Guid value) : base(value) { }
/// 
/// // Usage examples:
/// 
/// // Generate a new unique ID (Version 4 - random)
/// var customerId = CustomerId.NewUniqueV4();
/// 
/// // Generate a new unique ID (Version 7 - time-ordered, better for database indexes)
/// var customerId = CustomerId.NewUniqueV7();
/// 
/// // Create from existing GUID with validation
/// var result1 = CustomerId.TryCreate(existingGuid);
/// // Default (no [NotDefault]): Success regardless of value, including Guid.Empty.
/// // With [NotDefault] on CustomerId: Success when guid != Guid.Empty;
/// // Failure("Customer Id cannot be Guid.Empty.") when guid == Guid.Empty.
///
/// // Create from string with validation
/// var result2 = CustomerId.TryCreate("550e8400-e29b-41d4-a716-446655440000");
/// // Returns: Success(CustomerId) if valid GUID format
/// // Returns: Failure(Error.InvalidInput) if invalid format
/// // (or, when [NotDefault] is applied, also Failure if the parsed GUID is Guid.Empty)
/// 
/// // With custom field name for validation errors
/// var result3 = CustomerId.TryCreate(input, "customer.id");
/// // Error field will be "customer.id" instead of default "customerId"
/// 
/// // In entity constructors
/// public class Customer : Entity&lt;CustomerId&gt;
/// {
///     public EmailAddress Email { get; }
///     
///     private Customer(CustomerId id, EmailAddress email) : base(id)
///     {
///         Email = email;
///     }
///     
///     public static Result&lt;Customer&gt; Create(EmailAddress email) =>
///         email.ToResult()
///             .Map(e => new Customer(CustomerId.NewUniqueV7(), e));
/// }
/// </code>
/// </example>
/// <example>
/// ASP.NET Core automatic validation with route parameters:
/// <code>
/// // 1. Register automatic validation in Program.cs
/// builder.Services
///     .AddControllers()
///     .AddScalarValueObjectValidation(); // Enables automatic validation!
///
/// // 2. Use value objects directly in controller actions
/// [ApiController]
/// [Route("api/customers")]
/// public class CustomersController : ControllerBase
/// {
///     [HttpGet("{id}")]
///     public async Task&lt;ActionResult&lt;Customer&gt;&gt; Get(CustomerId id) // Automatically validated!
///     {
///         // If we reach here, 'id' is a non-null CustomerId (and, with [NotDefault], non-Guid.Empty)
///         // Model binding validated it automatically
///         var customer = await _repository.GetByIdAsync(id);
///         return Ok(customer);
///     }
///
///     [HttpDelete("{id}")]
///     public async Task&lt;IActionResult&gt; Delete(CustomerId id) // Also works here!
///     {
///         await _repository.DeleteAsync(id);
///         return NoContent();
///     }
/// }
///
/// // All-zero GUID (with [NotDefault] on CustomerId) is rejected as the sentinel value:
/// // GET /api/customers/00000000-0000-0000-0000-000000000000
/// // Response: 422 Unprocessable Content
/// // {
/// //   "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
/// //   "title": "One or more validation errors occurred.",
/// //   "status": 422,
/// //   "errors": {
/// //     "id": ["Customer Id cannot be Guid.Empty."]
/// //   }
/// // }
/// </code>
/// </example>
/// <example>
/// Using in API endpoints with automatic JSON serialization (manual approach):
/// <code>
/// // Request DTO
/// public record GetCustomerRequest(CustomerId CustomerId);
/// 
/// // API endpoint
/// app.MapGet("/customers/{id}", (string id) =>
///     CustomerId.TryCreate(id)
///         .Bind(_customerRepository.GetAsync)
///         .Map(customer => new CustomerDto(customer))
///         .ToHttpResponse());
/// 
/// // JSON request/response examples:
/// // Request: GET /customers/550e8400-e29b-41d4-a716-446655440000
/// // Response: { "customerId": "550e8400-e29b-41d4-a716-446655440000", "name": "John" }
/// // 
/// // All-zero GUID (rejected as the sentinel only when [NotDefault] is applied): GET /customers/00000000-0000-0000-0000-000000000000
/// // Response: 422 Unprocessable Content with "Customer Id cannot be Guid.Empty." detail
/// </code>
/// </example>
/// <example>
/// Multiple strongly-typed IDs in the same domain:
/// <code>
/// public partial class CustomerId : RequiredGuid&lt;CustomerId&gt; { }
/// public partial class OrderId : RequiredGuid&lt;OrderId&gt; { }
/// public partial class ProductId : RequiredGuid&lt;ProductId&gt; { }
/// 
/// public class Order : Entity&lt;OrderId&gt;
/// {
///     public CustomerId CustomerId { get; }
///     private readonly List&lt;OrderLine&gt; _lines = [];
///     
///     public Result&lt;Order&gt; AddLine(ProductId productId, int quantity) =>
///         this.ToResult()
///             .Ensure(_ => quantity > 0, new Error.InvalidInput(EquatableArray&lt;FieldViolation&gt;.Empty) { Detail = "Quantity must be positive" })
///             .Tap(_ => _lines.Add(new OrderLine(productId, quantity)));
///     
///     // Compiler prevents mixing IDs:
///     // AddLine(customerId, 5); // Won't compile - type safety!
/// }
/// </code>
/// </example>
/// <seealso cref="ScalarValueObject{TSelf, T}"/>
/// <seealso cref="RequiredString{TSelf}"/>
public abstract class RequiredGuid<TSelf> : ScalarValueObject<TSelf, Guid>
    where TSelf : RequiredGuid<TSelf>, IScalarValue<TSelf, Guid>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredGuid{TSelf}"/> class with the specified GUID value.
    /// </summary>
    /// <param name="value">The GUID value.</param>
    /// <remarks>
    /// <para>
    /// This constructor is protected and should be called by derived classes.
    /// When using the source generator (with <c>partial</c> keyword), a private constructor
    /// is automatically generated that includes validation.
    /// </para>
    /// <para>
    /// Direct instantiation should be avoided. Instead, use the generated factory methods:
    /// <list type="bullet">
    /// <item><c>NewUniqueV4()</c> - Generate a new random version 4 GUID</item>
    /// <item><c>NewUniqueV7()</c> / <c>NewUniqueV7(TimeProvider)</c> - Generate a new time-ordered version 7 GUID</item>
    /// <item><c>TryCreate(Guid?, string?)</c> - Create from GUID with validation</item>
    /// <item><c>TryCreate(string?, string?)</c> - Create from string with validation</item>
    /// </list>
    /// </para>
    /// </remarks>
    protected RequiredGuid(Guid value) : base(value)
    {
    }
}
