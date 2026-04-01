namespace Trellis;

using System.Collections.Immutable;
using System.Diagnostics;
using static Trellis.ValidationError;

/// <summary>
/// Base class for all error types in the functional DDD library.
/// Errors represent failure states and contain structured information about what went wrong.
/// </summary>
/// <remarks>
/// Use the static factory methods (Validation, NotFound, etc.) to create specific error types.
/// All errors have a Code for programmatic handling and a Detail for human-readable messages.
/// </remarks>
[DebuggerDisplay("{Detail}")]
#pragma warning disable CA1716 // Identifiers should not match keywords
public class Error : IEquatable<Error>
#pragma warning restore CA1716 // Identifiers should not match keywords
{
    /// <summary>
    /// Gets the machine-readable error code. Use this for programmatic error handling.
    /// </summary>
    /// <value>A string code like "validation.error" or "not.found.error".</value>
    public string Code { get; }

    /// <summary>
    /// Gets the human-readable error description. Use this for displaying error messages to users or in logs.
    /// </summary>
    /// <value>A descriptive message explaining what went wrong.</value>
    public string Detail { get; }

    /// <summary>
    /// Gets an optional identifier for the specific instance that caused the error (e.g., a resource ID).
    /// </summary>
    /// <value>An instance identifier, or null if not applicable.</value>
    public string? Instance { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Error"/> class with the specified detail and code.
    /// </summary>
    /// <param name="detail">The human-readable error description.</param>
    /// <param name="code">The machine-readable error code.</param>
    public Error(string detail, string code)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(code);
        Detail = detail;
        Code = code;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Error"/> class with the specified detail, code, and instance identifier.
    /// </summary>
    /// <param name="detail">The human-readable error description.</param>
    /// <param name="code">The machine-readable error code.</param>
    /// <param name="instance">An optional identifier for the specific instance that caused the error.</param>
    public Error(string detail, string code, string? instance)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(code);
        Detail = detail;
        Code = code;
        Instance = instance;
    }

    /// <summary>
    /// Determines equality based solely on the error Code.
    /// </summary>
    /// <param name="other">The error to compare with.</param>
    /// <returns>True if the error codes match; false otherwise.</returns>
    /// <remarks>
    /// IMPORTANT: Two errors are considered equal if they have the same Code, 
    /// regardless of their Detail, Instance, or concrete type.
    /// This design allows treating errors with the same code as equivalent for programmatic handling.
    /// Example: Error.NotFound("User not found", "user-1") == Error.NotFound("Product not found", "user-1") returns true
    /// because both have the code "not.found.error".
    /// </remarks>
    public bool Equals(Error? other)
    {
        if (other == null) return false;
        return Code == other.Code;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current Error instance.
    /// </summary>
    /// <remarks>This method supports object-based equality comparison. Use this method when the type of the
    /// object to compare is not known at compile time.</remarks>
    /// <param name="obj">The object to compare with the current Error instance.</param>
    /// <returns>true if the specified object is an Error and is equal to the current instance; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is Error error)
            return Equals(error);
        else
            return false;
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode() => Code.GetHashCode();

    /// <summary>
    /// Returns a string that represents the current object, including its type, code, detail, and instance information.
    /// </summary>
    /// <returns>A string containing the type name, code, detail, and instance values of the object. If the instance is null,
    /// "N/A" is used.</returns>
    public override string ToString()
        => $"Type: {GetType().Name}, Code: {Code}, Detail: {Detail}, Instance: {Instance ?? "N/A"}";

    /// <summary>
    /// Creates a <see cref="ValidationError"/> for a single field validation failure.
    /// </summary>
    /// <param name="fieldDetail">Description of what's wrong with the field value.</param>
    /// <param name="fieldName">Name of the field that failed validation. Empty string if not field-specific.</param>
    /// <param name="detail">Optional overall error detail. If null, uses fieldDetail.</param>
    /// <param name="instance">Optional identifier for the instance being validated.</param>
    /// <returns>A <see cref="ValidationError"/> representing the validation failure.</returns>
    /// <example>
    /// <code>
    /// Error.Validation("Email address is not valid", "email")
    /// Error.Validation("Age must be 18 or older", "age")
    /// </code>
    /// </example>
    public static ValidationError Validation(string fieldDetail, string fieldName = "", string? detail = null, string? instance = null)
        => new(fieldDetail, fieldName, "validation.error", detail, instance);

    /// <summary>
    /// Creates a <see cref="ValidationError"/> for multiple field validation failures.
    /// </summary>
    /// <param name="fieldDetails">Collection of field-specific validation errors.</param>
    /// <param name="detail">Overall error description.</param>
    /// <param name="instance">Optional identifier for the instance being validated.</param>
    /// <returns>A <see cref="ValidationError"/> containing all field errors.</returns>
    /// <example>
    /// <code>
    /// var errors = ImmutableArray.Create(
    ///     new FieldError("email", "Invalid format"),
    ///     new FieldError("age", "Must be 18 or older")
    /// );
    /// Error.Validation(errors, "User validation failed")
    /// </code>
    /// </example>
    public static ValidationError Validation(ImmutableArray<FieldError> fieldDetails, string detail = "", string? instance = null)
        => new(fieldDetails, "validation.error", detail, instance);

    /// <summary>
    /// Creates a <see cref="ValidationError"/> for multiple field validation failures with a custom error code.
    /// </summary>
    /// <param name="fieldDetails">Collection of field-specific validation errors.</param>
    /// <param name="detail">Overall error description.</param>
    /// <param name="instance">Optional identifier for the instance being validated.</param>
    /// <param name="code">Custom error code to use instead of the default "validation.error".</param>
    /// <returns>A <see cref="ValidationError"/> containing all field errors with the specified code.</returns>
    public static ValidationError Validation(ImmutableArray<FieldError> fieldDetails, string detail, string? instance, string code)
        => new(fieldDetails, code, detail, instance);

    /// <summary>
    /// Creates a <see cref="BadRequestError"/> indicating the request was malformed or invalid.
    /// </summary>
    /// <param name="detail">Description of why the request is bad.</param>
    /// <param name="instance">Optional identifier for the bad request.</param>
    /// <returns>A <see cref="BadRequestError"/>.</returns>
    /// <remarks>Use this for syntactic errors or malformed requests, not for business rule violations (use Validation instead).</remarks>
    public static BadRequestError BadRequest(string detail, string? instance = null) =>
        new(detail, "bad.request.error", instance);

    /// <summary>
    /// Creates a <see cref="ConflictError"/> indicating a conflict with the current state.
    /// </summary>
    /// <param name="detail">Description of the conflict.</param>
    /// <param name="instance">Optional identifier for the conflicting resource.</param>
    /// <returns>A <see cref="ConflictError"/>.</returns>
    /// <example>
    /// <code>
    /// Error.Conflict("Email address already in use")
    /// Error.Conflict("Cannot delete user with active subscriptions")
    /// </code>
    /// </example>
    public static ConflictError Conflict(string detail, string? instance = null) =>
        new(detail, "conflict.error", instance);

    /// <summary>
    /// Creates a <see cref="PreconditionFailedError"/> indicating a conditional request header
    /// (e.g., <c>If-Match</c>) evaluated to false per RFC 9110 §13.1.1.
    /// </summary>
    /// <param name="detail">Description of the precondition failure.</param>
    /// <param name="instance">Optional identifier for the affected resource.</param>
    /// <returns>A <see cref="PreconditionFailedError"/>.</returns>
    /// <example>
    /// <code>
    /// Error.PreconditionFailed("Resource has been modified. Please reload and retry.")
    /// </code>
    /// </example>
    public static PreconditionFailedError PreconditionFailed(string detail, string? instance = null) =>
        new(detail, "precondition.failed.error", instance);

    /// <summary>
    /// Creates a <see cref="PreconditionRequiredError"/> indicating the server requires
    /// a conditional request header (e.g., <c>If-Match</c>) per RFC 6585 §3.
    /// </summary>
    /// <param name="detail">Description of the required precondition.</param>
    /// <param name="instance">Optional identifier for the affected resource.</param>
    /// <returns>A <see cref="PreconditionRequiredError"/>.</returns>
    public static PreconditionRequiredError PreconditionRequired(string detail, string? instance = null) =>
        new(detail, "precondition.required.error", instance);

    /// <summary>
    /// Creates a <see cref="NotFoundError"/> indicating a requested resource was not found.
    /// </summary>
    /// <param name="detail">Description of what was not found.</param>
    /// <param name="instance">Optional identifier for the missing resource.</param>
    /// <returns>A <see cref="NotFoundError"/>.</returns>
    /// <example>
    /// <code>
    /// Error.NotFound($"User with ID {userId} not found", userId)
    /// Error.NotFound("Product not found in catalog")
    /// </code>
    /// </example>
    public static NotFoundError NotFound(string detail, string? instance = null) =>
        new(detail, "not.found.error", instance);

    /// <summary>
    /// Creates an <see cref="UnauthorizedError"/> indicating authentication is required.
    /// </summary>
    /// <param name="detail">Description of why authorization failed.</param>
    /// <param name="instance">Optional identifier for the unauthorized request.</param>
    /// <returns>An <see cref="UnauthorizedError"/>.</returns>
    /// <remarks>Use this when the user is not authenticated (not logged in).</remarks>
    public static UnauthorizedError Unauthorized(string detail, string? instance = null) =>
        new(detail, "unauthorized.error", instance);

    /// <summary>
    /// Creates a <see cref="ForbiddenError"/> indicating the user lacks permission.
    /// </summary>
    /// <param name="detail">Description of why access is forbidden.</param>
    /// <param name="instance">Optional identifier for the forbidden resource.</param>
    /// <returns>A <see cref="ForbiddenError"/>.</returns>
    /// <remarks>Use this when the user is authenticated but doesn't have permission to access the resource.</remarks>
    public static ForbiddenError Forbidden(string detail, string? instance = null) =>
        new(detail, "forbidden.error", instance);

    /// <summary>
    /// Creates an <see cref="UnexpectedError"/> indicating an unexpected system error occurred.
    /// </summary>
    /// <param name="detail">Description of what went wrong.</param>
    /// <param name="instance">Optional identifier for the operation that failed.</param>
    /// <returns>An <see cref="UnexpectedError"/>.</returns>
    /// <remarks>
    /// Use this for system errors, infrastructure failures, or exceptions.
    /// This typically maps to HTTP 500 Internal Server Error.
    /// </remarks>
    public static UnexpectedError Unexpected(string detail, string? instance = null) =>
        new(detail, "unexpected.error", instance);

    /// <summary>
    /// Creates a <see cref="DomainError"/> indicating a business rule violation.
    /// </summary>
    /// <param name="detail">Description of the business rule that was violated.</param>
    /// <param name="instance">Optional identifier for the entity or operation.</param>
    /// <returns>A <see cref="DomainError"/>.</returns>
    /// <remarks>
    /// Use this for domain logic violations that prevent the operation from completing.
    /// This is distinct from validation errors (field-level) and conflicts (state-based).
    /// Maps to HTTP 422 Unprocessable Entity.
    /// </remarks>
    /// <example>
    /// <code>
    /// Error.Domain("Cannot withdraw more than account balance")
    /// Error.Domain("Minimum order quantity is 10 units")
    /// Error.Domain("Cannot cancel order after shipment")
    /// </code>
    /// </example>
    public static DomainError Domain(string detail, string? instance = null) =>
        new(detail, "domain.error", instance);

    /// <summary>
    /// Creates a <see cref="RateLimitError"/> indicating too many requests have been made.
    /// </summary>
    /// <param name="detail">Description of the rate limit violation.</param>
    /// <param name="instance">Optional identifier for the client or resource being rate limited.</param>
    /// <returns>A <see cref="RateLimitError"/>.</returns>
    /// <remarks>
    /// Use this when a client has exceeded their request quota or rate limit.
    /// Maps to HTTP 429 Too Many Requests.
    /// </remarks>
    /// <example>
    /// <code>
    /// Error.RateLimit("API rate limit exceeded. Please try again in 60 seconds")
    /// Error.RateLimit("Too many login attempts. Account temporarily locked")
    /// </code>
    /// </example>
    public static RateLimitError RateLimit(string detail, string? instance = null) =>
        new(detail, "rate.limit.error", instance);

    /// <summary>
    /// Creates a <see cref="RateLimitError"/> with retry-after metadata.
    /// </summary>
    /// <param name="detail">Description of the rate limit violation.</param>
    /// <param name="retryAfter">The retry-after value indicating when the client may retry.</param>
    /// <param name="instance">Optional identifier for the client or resource being rate limited.</param>
    /// <returns>A <see cref="RateLimitError"/> with retry-after metadata.</returns>
    public static RateLimitError RateLimit(string detail, RetryAfterValue retryAfter, string? instance = null) =>
        new(detail, "rate.limit.error", retryAfter, instance);

    /// <summary>
    /// Creates a <see cref="ServiceUnavailableError"/> indicating temporary service unavailability.
    /// </summary>
    /// <param name="detail">Description of why the service is unavailable.</param>
    /// <param name="instance">Optional identifier for the unavailable service or resource.</param>
    /// <returns>A <see cref="ServiceUnavailableError"/>.</returns>
    /// <remarks>
    /// Use this when the service is temporarily unable to handle the request.
    /// This indicates a temporary condition - the service is expected to be available again.
    /// Maps to HTTP 503 Service Unavailable.
    /// </remarks>
    /// <example>
    /// <code>
    /// Error.ServiceUnavailable("Service is under maintenance. Please try again later")
    /// Error.ServiceUnavailable("External payment gateway is temporarily unavailable")
    /// </code>
    /// </example>
    public static ServiceUnavailableError ServiceUnavailable(string detail, string? instance = null) =>
        new(detail, "service.unavailable.error", instance);

    /// <summary>
    /// Creates a <see cref="ServiceUnavailableError"/> with retry-after metadata.
    /// </summary>
    /// <param name="detail">Description of why the service is unavailable.</param>
    /// <param name="retryAfter">The retry-after value indicating when the service may resume.</param>
    /// <param name="instance">Optional identifier for the unavailable service or resource.</param>
    /// <returns>A <see cref="ServiceUnavailableError"/> with retry-after metadata.</returns>
    public static ServiceUnavailableError ServiceUnavailable(string detail, RetryAfterValue retryAfter, string? instance = null) =>
        new(detail, "service.unavailable.error", retryAfter, instance);

    /// <summary>
    /// Creates a <see cref="BadRequestError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of why the request is bad.</param>
    /// <param name="code">Custom error code to use instead of the default "bad.request.error".</param>
    /// <param name="instance">Optional identifier for the bad request.</param>
    /// <returns>A <see cref="BadRequestError"/> with the specified code.</returns>
    public static BadRequestError BadRequest(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="ConflictError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of the conflict.</param>
    /// <param name="code">Custom error code to use instead of the default "conflict.error".</param>
    /// <param name="instance">Optional identifier for the conflicting resource.</param>
    /// <returns>A <see cref="ConflictError"/> with the specified code.</returns>
    public static ConflictError Conflict(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="PreconditionFailedError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of the precondition failure.</param>
    /// <param name="code">Custom error code to use instead of the default "precondition.failed.error".</param>
    /// <param name="instance">Optional identifier for the affected resource.</param>
    /// <returns>A <see cref="PreconditionFailedError"/> with the specified code.</returns>
    public static PreconditionFailedError PreconditionFailed(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="PreconditionRequiredError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of the required precondition.</param>
    /// <param name="code">Custom error code to use instead of the default "precondition.required.error".</param>
    /// <param name="instance">Optional identifier for the affected resource.</param>
    /// <returns>A <see cref="PreconditionRequiredError"/> with the specified code.</returns>
    public static PreconditionRequiredError PreconditionRequired(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="NotFoundError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of what was not found.</param>
    /// <param name="code">Custom error code to use instead of the default "not.found.error".</param>
    /// <param name="instance">Optional identifier for the missing resource.</param>
    /// <returns>A <see cref="NotFoundError"/> with the specified code.</returns>
    public static NotFoundError NotFound(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates an <see cref="UnauthorizedError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of why authorization failed.</param>
    /// <param name="code">Custom error code to use instead of the default "unauthorized.error".</param>
    /// <param name="instance">Optional identifier for the unauthorized request.</param>
    /// <returns>An <see cref="UnauthorizedError"/> with the specified code.</returns>
    public static UnauthorizedError Unauthorized(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="ForbiddenError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of why access is forbidden.</param>
    /// <param name="code">Custom error code to use instead of the default "forbidden.error".</param>
    /// <param name="instance">Optional identifier for the forbidden resource.</param>
    /// <returns>A <see cref="ForbiddenError"/> with the specified code.</returns>
    public static ForbiddenError Forbidden(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates an <see cref="UnexpectedError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of what went wrong.</param>
    /// <param name="code">Custom error code to use instead of the default "unexpected.error".</param>
    /// <param name="instance">Optional identifier for the operation that failed.</param>
    /// <returns>An <see cref="UnexpectedError"/> with the specified code.</returns>
    public static UnexpectedError Unexpected(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="DomainError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of the business rule that was violated.</param>
    /// <param name="code">Custom error code to use instead of the default "domain.error".</param>
    /// <param name="instance">Optional identifier for the entity or operation.</param>
    /// <returns>A <see cref="DomainError"/> with the specified code.</returns>
    public static DomainError Domain(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="RateLimitError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of the rate limit violation.</param>
    /// <param name="code">Custom error code to use instead of the default "rate.limit.error".</param>
    /// <param name="instance">Optional identifier for the client or resource being rate limited.</param>
    /// <returns>A <see cref="RateLimitError"/> with the specified code.</returns>
    public static RateLimitError RateLimit(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="ServiceUnavailableError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of why the service is unavailable.</param>
    /// <param name="code">Custom error code to use instead of the default "service.unavailable.error".</param>
    /// <param name="instance">Optional identifier for the unavailable service or resource.</param>
    /// <returns>A <see cref="ServiceUnavailableError"/> with the specified code.</returns>
    public static ServiceUnavailableError ServiceUnavailable(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="GoneError"/> indicating a resource has been permanently removed.
    /// </summary>
    /// <param name="detail">Description of why the resource is gone.</param>
    /// <param name="instance">Optional identifier for the removed resource.</param>
    /// <returns>A <see cref="GoneError"/>.</returns>
    /// <remarks>
    /// Use this instead of <see cref="NotFound(string, string?)"/> when the server knows the resource
    /// previously existed and has been intentionally removed. Maps to HTTP 410 Gone.
    /// </remarks>
    public static GoneError Gone(string detail, string? instance = null) =>
        new(detail, "gone.error", instance);

    /// <summary>
    /// Creates a <see cref="GoneError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of why the resource is gone.</param>
    /// <param name="code">Custom error code to use instead of the default "gone.error".</param>
    /// <param name="instance">Optional identifier for the removed resource.</param>
    /// <returns>A <see cref="GoneError"/> with the specified code.</returns>
    public static GoneError Gone(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="MethodNotAllowedError"/> indicating the HTTP method is not supported.
    /// </summary>
    /// <param name="detail">Description of why the method is not allowed.</param>
    /// <param name="allowedMethods">The HTTP methods the target resource supports. Emitted as the <c>Allow</c> header.</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    /// <returns>A <see cref="MethodNotAllowedError"/>.</returns>
    /// <remarks>
    /// Per RFC 9110 §15.5.6, a 405 response MUST include an <c>Allow</c> header.
    /// Maps to HTTP 405 Method Not Allowed.
    /// </remarks>
    public static MethodNotAllowedError MethodNotAllowed(string detail, IReadOnlyList<string> allowedMethods, string? instance = null) =>
        new(detail, allowedMethods, "method.not.allowed.error", instance);

    /// <summary>
    /// Creates a <see cref="MethodNotAllowedError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of why the method is not allowed.</param>
    /// <param name="allowedMethods">The HTTP methods the target resource supports.</param>
    /// <param name="code">Custom error code to use instead of the default "method.not.allowed.error".</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    /// <returns>A <see cref="MethodNotAllowedError"/> with the specified code.</returns>
    public static MethodNotAllowedError MethodNotAllowed(string detail, IReadOnlyList<string> allowedMethods, string code, string? instance) =>
        new(detail, allowedMethods, code, instance);

    /// <summary>
    /// Creates a <see cref="NotAcceptableError"/> indicating no acceptable representation is available.
    /// </summary>
    /// <param name="detail">Description of why no acceptable representation is available.</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    /// <returns>A <see cref="NotAcceptableError"/>.</returns>
    /// <remarks>Maps to HTTP 406 Not Acceptable.</remarks>
    public static NotAcceptableError NotAcceptable(string detail, string? instance = null) =>
        new(detail, "not.acceptable.error", instance);

    /// <summary>
    /// Creates a <see cref="NotAcceptableError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of why no acceptable representation is available.</param>
    /// <param name="code">Custom error code to use instead of the default "not.acceptable.error".</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    /// <returns>A <see cref="NotAcceptableError"/> with the specified code.</returns>
    public static NotAcceptableError NotAcceptable(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates an <see cref="UnsupportedMediaTypeError"/> indicating the content type is not supported.
    /// </summary>
    /// <param name="detail">Description of why the media type is not supported.</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    /// <returns>An <see cref="UnsupportedMediaTypeError"/>.</returns>
    /// <remarks>Maps to HTTP 415 Unsupported Media Type.</remarks>
    public static UnsupportedMediaTypeError UnsupportedMediaType(string detail, string? instance = null) =>
        new(detail, "unsupported.media.type.error", instance);

    /// <summary>
    /// Creates an <see cref="UnsupportedMediaTypeError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of why the media type is not supported.</param>
    /// <param name="code">Custom error code to use instead of the default "unsupported.media.type.error".</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    /// <returns>An <see cref="UnsupportedMediaTypeError"/> with the specified code.</returns>
    public static UnsupportedMediaTypeError UnsupportedMediaType(string detail, string code, string? instance) =>
        new(detail, code, instance);

    /// <summary>
    /// Creates a <see cref="ContentTooLargeError"/> indicating the request content is too large.
    /// </summary>
    /// <param name="detail">Description of why the content is too large.</param>
    /// <param name="retryAfter">Optional retry-after value for temporary conditions.</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    /// <returns>A <see cref="ContentTooLargeError"/>.</returns>
    /// <remarks>Maps to HTTP 413 Content Too Large.</remarks>
    public static ContentTooLargeError ContentTooLarge(string detail, RetryAfterValue? retryAfter = null, string? instance = null) =>
        new(detail, "content.too.large.error", retryAfter, instance);

    /// <summary>
    /// Creates a <see cref="ContentTooLargeError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of why the content is too large.</param>
    /// <param name="code">Custom error code to use instead of the default "content.too.large.error".</param>
    /// <param name="retryAfter">Optional retry-after value for temporary conditions.</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    /// <returns>A <see cref="ContentTooLargeError"/> with the specified code.</returns>
    public static ContentTooLargeError ContentTooLarge(string detail, string code, RetryAfterValue? retryAfter = null, string? instance = null) =>
        new(detail, code, retryAfter, instance);

    /// <summary>
    /// Creates a <see cref="RangeNotSatisfiableError"/> indicating the requested range cannot be served.
    /// </summary>
    /// <param name="detail">Description of why the range is not satisfiable.</param>
    /// <param name="completeLength">The complete length of the representation for the <c>Content-Range</c> header.</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    /// <returns>A <see cref="RangeNotSatisfiableError"/>.</returns>
    /// <remarks>Maps to HTTP 416 Range Not Satisfiable.</remarks>
    public static RangeNotSatisfiableError RangeNotSatisfiable(string detail, long completeLength, string? instance = null) =>
        new(detail, completeLength, "range.not.satisfiable.error", instance: instance);

    /// <summary>
    /// Creates a <see cref="RangeNotSatisfiableError"/> with a custom error code.
    /// </summary>
    /// <param name="detail">Description of why the range is not satisfiable.</param>
    /// <param name="completeLength">The complete length of the representation.</param>
    /// <param name="code">Custom error code to use instead of the default "range.not.satisfiable.error".</param>
    /// <param name="unit">The range unit (defaults to "bytes").</param>
    /// <param name="instance">Optional identifier for the resource.</param>
    /// <returns>A <see cref="RangeNotSatisfiableError"/> with the specified code.</returns>
    public static RangeNotSatisfiableError RangeNotSatisfiable(string detail, long completeLength, string code, string unit = "bytes", string? instance = null) =>
        new(detail, completeLength, code, unit, instance);
}