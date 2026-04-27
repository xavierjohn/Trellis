namespace Trellis.FluentValidation;

using System.Linq;
using System.Runtime.CompilerServices;
using global::FluentValidation;
using global::FluentValidation.Results;
using Trellis;

/// <summary>
/// Provides extension methods to integrate FluentValidation with Railway Oriented Programming Result types.
/// Enables seamless conversion of FluentValidation results to Result&lt;T&gt; for functional error handling.
/// </summary>
/// <remarks>
/// <para>
/// This class bridges FluentValidation's imperative validation model with Trellis'
/// Railway Oriented Programming approach. Key benefits:
/// <list type="bullet">
/// <item>Automatic conversion of validation errors to <see cref="Error.UnprocessableContent"/></item>
/// <item>Field-level error grouping for structured error responses</item>
/// <item>Integration with Result type for consistent error handling</item>
/// <item>Support for both sync and async validation scenarios</item>
/// <item>Null-safety with automatic parameter name capture</item>
/// </list>
/// </para>
/// <para>
/// Common usage patterns:
/// <list type="bullet">
/// <item>Factory method validation in aggregates and entities</item>
/// <item>DTO/request validation in API endpoints</item>
/// <item>Complex business rule validation with FluentValidation's powerful API</item>
/// <item>Chaining validation with other Result operations using Bind</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Complete example with aggregate, validator, and factory method:
/// <code>
/// // Define the aggregate
/// public class User : Aggregate&lt;UserId&gt;
/// {
///     public FirstName FirstName { get; }
///     public LastName LastName { get; }
///     public EmailAddress Email { get; }
///     public string Password { get; }
/// 
///     // Factory method with validation
///     public static Result&lt;User&gt; TryCreate(
///         FirstName firstName, 
///         LastName lastName, 
///         EmailAddress email, 
///         string password)
///     {
///         var user = new User(firstName, lastName, email, password);
///         return s_validator.ValidateToResult(user);
///     }
/// 
///     private User(FirstName firstName, LastName lastName, EmailAddress email, string password)
///         : base(UserId.NewUnique())
///     {
///         FirstName = firstName;
///         LastName = lastName;
///         Email = email;
///         Password = password;
///     }
/// 
///     // Inline validator for business rules
///     private static readonly InlineValidator&lt;User&gt; s_validator = new()
///     {
///         v =&gt; v.RuleFor(x =&gt; x.FirstName).NotNull(),
///         v =&gt; v.RuleFor(x =&gt; x.LastName).NotNull(),
///         v =&gt; v.RuleFor(x =&gt; x.Email).NotNull(),
///         v =&gt; v.RuleFor(x =&gt; x.Password)
///             .Matches("(?=.*[a-z])(?=.*[A-Z])(?=.*[0-9])(?=.*[^A-Za-z0-9])(?=.{8,})")
///             .WithMessage("Password must be at least 8 characters with uppercase, lowercase, digit and special character")
///     };
/// }
/// 
/// // Usage in application code
/// var result = User.TryCreate(firstName, lastName, email, password);
/// // Returns: Success(user) or Failure(Error.UnprocessableContent with field violations)
/// </code>
/// </example>
/// <example>
/// Using with API request validation:
/// <code>
/// // Request DTO
/// public record CreateUserRequest(string FirstName, string LastName, string Email, string Password);
/// 
/// // FluentValidation validator
/// public class CreateUserRequestValidator : AbstractValidator&lt;CreateUserRequest&gt;
/// {
///     public CreateUserRequestValidator()
///     {
///         RuleFor(x =&gt; x.FirstName)
///             .NotEmpty().WithMessage("First name is required")
///             .MaximumLength(50);
///             
///         RuleFor(x =&gt; x.LastName)
///             .NotEmpty().WithMessage("Last name is required")
///             .MaximumLength(50);
///             
///         RuleFor(x =&gt; x.Email)
///             .NotEmpty()
///             .EmailAddress().WithMessage("Invalid email format");
///             
///         RuleFor(x =&gt; x.Password)
///             .MinimumLength(8)
///             .Matches("[A-Z]").WithMessage("Password must contain uppercase")
///             .Matches("[a-z]").WithMessage("Password must contain lowercase")
///             .Matches("[0-9]").WithMessage("Password must contain digit");
///     }
/// }
/// 
/// // API endpoint
/// app.MapPost("/users", (CreateUserRequest request, IUserService service) =&gt;
/// {
///     var validator = new CreateUserRequestValidator();
///     return validator.ValidateToResult(request)
///         .Bind(req =&gt; FirstName.TryCreate(req.FirstName)
///             .Combine(LastName.TryCreate(req.LastName))
///             .Combine(EmailAddress.TryCreate(req.Email))
///             .Bind((first, last, email) =&gt; service.CreateUser(first, last, email, req.Password)))
///         .ToHttpResponse();
/// });
/// 
/// // Validation errors automatically formatted with field names
/// </code>
/// </example>
public static class FluentValidationResultExtensions
{
    /// <summary>
    /// Converts a FluentValidation <see cref="ValidationResult"/> to a <see cref="Result{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value being validated.</typeparam>
    /// <param name="validationResult">The FluentValidation result containing validation state and errors.</param>
    /// <param name="value">The value that was validated.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>Success containing the value if validation passed</item>
    /// <item>Failure with validation errors if validation failed or value is null</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// Each <see cref="ValidationFailure"/> becomes one <see cref="FieldViolation"/>. The
    /// failure's <see cref="ValidationFailure.PropertyName"/> is normalized to an RFC 6901
    /// JSON Pointer before being placed on <see cref="FieldViolation.Field"/>: dotted member
    /// paths become path segments and array/list indexers become numeric segments
    /// (e.g., <c>"Address.PostCode"</c> → <c>/Address/PostCode</c>,
    /// <c>"Lines[0].Sku"</c> → <c>/Lines/0/Sku</c>). Special characters in segments are
    /// escaped per RFC 6901 (<c>~</c> → <c>~0</c>, <c>/</c> → <c>~1</c>). Property names that
    /// already start with <c>/</c> are passed through unchanged. When
    /// <see cref="ValidationFailure.PropertyName"/> is empty, the caller-captured
    /// <paramref name="paramName"/> is normalized and used as the pointer.
    /// </para>
    /// <para>
    /// The <see cref="ValidationFailure.ErrorCode"/> becomes <see cref="FieldViolation.ReasonCode"/>
    /// (falling back to <c>"validation.error"</c>), and the <see cref="ValidationFailure.ErrorMessage"/>
    /// becomes <see cref="FieldViolation.Detail"/>. Multiple failures on the same property produce
    /// multiple <see cref="FieldViolation"/> entries — no information is lost.
    /// </para>
    /// <para>
    /// The resulting <see cref="Error.UnprocessableContent"/> can be automatically converted to:
    /// <list type="bullet">
    /// <item>HTTP 422 Unprocessable Content with validation problem details (via ToHttpResponse)</item>
    /// <item>Structured error responses with field-level error messages</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// Manual validation and conversion:
    /// <code>
    /// var validator = new UserValidator();
    /// var user = new User { Name = "", Email = "invalid" };
    /// var validationResult = validator.Validate(user);
    /// var result = validationResult.ToResult(user);
    ///
    /// if (result.IsFailure)
    /// {
    ///     var error = (Error.UnprocessableContent)result.Error;
    ///     foreach (var fv in error.Fields)
    ///     {
    ///         Console.WriteLine($"{fv.Field.Path}: {fv.Detail} ({fv.ReasonCode})");
    ///     }
    /// }
    /// // Output:
    /// // /Name:  Name is required (validation.error)
    /// // /Email: Email must be a valid email address (validation.error)
    /// </code>
    /// </example>
    /// <param name="paramName">
    /// The caller argument expression for <paramref name="value"/>. Used as the field name when
    /// FluentValidation reports a root-level failure with no property name.
    /// </param>
    public static Result<T> ToResult<T>(
        this ValidationResult validationResult,
        T value,
        [CallerArgumentExpression(nameof(value))] string paramName = "value")
    {
        ArgumentNullException.ThrowIfNull(validationResult);

        if (validationResult.IsValid)
            return Result.Ok(value);

        var violations = validationResult.Errors
            .Select(e =>
            {
                var rawName = string.IsNullOrWhiteSpace(e.PropertyName) ? paramName : e.PropertyName;
                var pointerPath = JsonPointerNormalizer.ToJsonPointer(rawName);
                var reasonCode = string.IsNullOrWhiteSpace(e.ErrorCode) ? "validation.error" : e.ErrorCode;
                return new FieldViolation(new InputPointer(pointerPath), reasonCode) { Detail = e.ErrorMessage };
            })
            .ToArray();

        return Result.Fail<T>(new Error.UnprocessableContent(EquatableArray.Create(violations)));
    }

    /// <summary>
    /// Validates the specified value using FluentValidation and converts the result to <see cref="Result{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value being validated.</typeparam>
    /// <param name="validator">The FluentValidation validator to use.</param>
    /// <param name="value">The value to validate.</param>
    /// <param name="paramName">
    /// The parameter name for error messages. Automatically captured from the caller expression.
    /// </param>
    /// <param name="message">Optional custom error message when value is null.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>Success containing the value if validation passed</item>
    /// <item>Failure with validation errors if validation failed or value is null</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method provides null-safety by checking for null values before validation.
    /// If the value is null, it returns a validation failure without calling the validator.
    /// </para>
    /// <para>
    /// The parameter name is automatically captured using <c>[CallerArgumentExpression]</c>,
    /// making error messages more informative without manual string entry.
    /// </para>
    /// <para>
    /// Common usage patterns:
    /// <list type="bullet">
    /// <item>Aggregate/Entity factory methods</item>
    /// <item>Value object creation with complex validation rules</item>
    /// <item>DTO validation before processing</item>
    /// <item>Integration with Bind/Map for validation chains</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <example>
    /// Using in an aggregate factory method:
    /// <code>
    /// public class Order : Aggregate&lt;OrderId&gt;
    /// {
    ///     public CustomerId CustomerId { get; }
    ///     public IReadOnlyList&lt;OrderLine&gt; Lines { get; }
    ///     
    ///     public static Result&lt;Order&gt; TryCreate(CustomerId customerId, List&lt;OrderLine&gt; lines)
    ///     {
    ///         var order = new Order(customerId, lines);
    ///         return s_validator.ValidateToResult(order);
    ///     }
    ///     
    ///     private static readonly InlineValidator&lt;Order&gt; s_validator = new()
    ///     {
    ///         v =&gt; v.RuleFor(x =&gt; x.CustomerId).NotNull(),
    ///         v =&gt; v.RuleFor(x =&gt; x.Lines)
    ///             .NotEmpty().WithMessage("Order must have at least one line")
    ///             .Must(lines =&gt; lines.Count &lt;= 100).WithMessage("Order cannot exceed 100 lines")
    ///     };
    /// }
    /// </code>
    /// </example>
    /// <example>
    /// Chaining validation with other operations:
    /// <code>
    /// public Result&lt;User&gt; CreateUser(CreateUserRequest request)
    /// {
    ///     return _requestValidator.ValidateToResult(request)
    ///         .Bind(req =&gt; EmailAddress.TryCreate(req.Email)
    ///             .Combine(FirstName.TryCreate(req.FirstName))
    ///             .Bind((email, name) =&gt; User.TryCreate(email, name, req.Password)))
    ///         .Tap(user =&gt; _repository.Add(user));
    /// }
    /// </code>
    /// </example>
    /// <example>
    /// Null-safety demonstration:
    /// <code>
    /// var validator = new UserValidator();
    /// User? nullUser = null;
    /// var result = validator.ValidateToResult(nullUser);
    /// // Returns: Failure with error "'nullUser' must not be empty."
    /// // Validator.Validate() is never called
    /// </code>
    /// </example>
    public static Result<T> ValidateToResult<T>(
        this IValidator<T> validator,
        T value,
        [CallerArgumentExpression(nameof(value))] string paramName = "value",
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(validator);

        if (value is null)
        {
            var nullResult = new ValidationResult([new ValidationFailure(paramName, message ?? $"'{paramName}' must not be empty.")]);
            return nullResult.ToResult(value!, paramName);
        }

        return validator.Validate(value).ToResult(value, paramName);
    }

    /// <summary>
    /// Asynchronously validates the specified value using FluentValidation and converts the result to <see cref="Result{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value being validated.</typeparam>
    /// <param name="validator">The FluentValidation validator to use.</param>
    /// <param name="value">The value to validate.</param>
    /// <param name="paramName">
    /// The parameter name for error messages. Automatically captured from the caller expression.
    /// </param>
    /// <param name="message">Optional custom error message when value is null.</param>
    /// <param name="cancellationToken">Cancellation token to observe.</param>
    /// <returns>
    /// A task representing the asynchronous validation operation, containing:
    /// <list type="bullet">
    /// <item>Success with the value if validation passed</item>
    /// <item>Failure with validation errors if validation failed or value is null</item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// This async variant is essential when validation rules perform async operations such as:
    /// <list type="bullet">
    /// <item>Database uniqueness checks</item>
    /// <item>External API validations</item>
    /// <item>File system validations</item>
    /// <item>Any I/O-bound validation logic</item>
    /// </list>
    /// </para>
    /// <para>
    /// Like the synchronous variant, this method provides null-safety and automatic
    /// parameter name capture for informative error messages.
    /// </para>
    /// <para>
    /// Respects cancellation tokens to enable responsive cancellation of long-running validations.
    /// </para>
    /// </remarks>
    /// <example>
    /// Using with async validation rules:
    /// <code>
    /// public class CreateUserRequestValidator : AbstractValidator&lt;CreateUserRequest&gt;
    /// {
    ///     private readonly IUserRepository _repository;
    ///     
    ///     public CreateUserRequestValidator(IUserRepository repository)
    ///     {
    ///         _repository = repository;
    ///         
    ///         RuleFor(x =&gt; x.Email)
    ///             .NotEmpty()
    ///             .EmailAddress()
    ///             .MustAsync(async (email, ct) =&gt; 
    ///                 !await _repository.ExistsByEmailAsync(email, ct))
    ///             .WithMessage("Email already in use");
    ///     }
    /// }
    /// 
    /// // API endpoint with async validation
    /// app.MapPost("/users", async (
    ///     CreateUserRequest request,
    ///     CreateUserRequestValidator validator,
    ///     IUserService service,
    ///     CancellationToken ct) =&gt;
    ///     await validator.ValidateToResultAsync(request, cancellationToken: ct)
    ///         .BindAsync(req =&gt; service.CreateUserAsync(req, ct), ct)
    ///         .ToHttpResponseAsync());
    /// </code>
    /// </example>
    /// <example>
    /// Async validation in application service:
    /// <code>
    /// public async Task&lt;Result&lt;Product&gt;&gt; CreateProductAsync(
    ///     CreateProductRequest request,
    ///     CancellationToken ct)
    /// {
    ///     return await _createProductValidator.ValidateToResultAsync(request, cancellationToken: ct)
    ///         .BindAsync(req =&gt; ProductName.TryCreate(req.Name), ct)
    ///         .BindAsync(name =&gt; Money.TryCreate(req.Price), ct)
    ///         .BindAsync((name, price) =&gt; Product.CreateAsync(name, price, ct), ct)
    ///         .TapAsync(async product =&gt; await _repository.AddAsync(product, ct), ct);
    /// }
    /// </code>
    /// </example>
    /// <example>
    /// Handling validation errors in the caller:
    /// <code>
    /// var result = await validator.ValidateToResultAsync(request, cancellationToken: ct);
    /// 
    /// return result.Match(
    ///     onSuccess: user =&gt; Ok(new UserDto(user)),
    ///     onFailure: error =&gt; error is Error.UnprocessableContent uc
    ///         ? BadRequest(uc.Fields)
    ///         : StatusCode(500, error.Detail)
    /// );
    /// </code>
    /// </example>
    /// <example>
    /// Handling validation errors in the caller (sync variant):
    /// <code>
    /// var result = validator.ValidateToResult(request);
    ///
    /// return result.Match(
    ///     onSuccess: user =&gt; Ok(new UserDto(user)),
    ///     onFailure: error =&gt; error is Error.UnprocessableContent uc
    ///         ? BadRequest(uc.Fields)
    ///         : StatusCode(500, error.Detail)
    /// );
    /// </code>
    /// </example>
    public static async Task<Result<T>> ValidateToResultAsync<T>(
        this IValidator<T> validator,
        T value,
        [CallerArgumentExpression(nameof(value))] string paramName = "value",
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);

        if (value is null)
        {
            var nullResult = new ValidationResult([new ValidationFailure(paramName, message ?? $"'{paramName}' must not be empty.")]);
            return nullResult.ToResult(value!, paramName);
        }

        var result = await validator.ValidateAsync(value, cancellationToken).ConfigureAwait(false);
        return result.ToResult(value, paramName);
    }
}
