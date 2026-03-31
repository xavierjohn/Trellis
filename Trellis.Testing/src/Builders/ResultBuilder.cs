namespace Trellis.Testing.Builders;

/// <summary>
/// Fluent builder for creating Result instances in tests.
/// </summary>
public static class ResultBuilder
{
    /// <summary>
    /// Creates a successful Result with the specified value.
    /// </summary>
    public static Result<T> Success<T>(T value) => Result.Success(value);

    /// <summary>
    /// Creates a failed Result with the specified error.
    /// </summary>
    public static Result<T> Failure<T>(Error error) => Result.Failure<T>(error);

    /// <summary>
    /// Creates a failed Result with a NotFoundError.
    /// </summary>
    public static Result<T> NotFound<T>(string detail) =>
        Result.Failure<T>(Error.NotFound(detail));

    /// <summary>
    /// Creates a failed Result with a NotFoundError for a specific entity.
    /// </summary>
    public static Result<T> NotFound<T>(string entity, string instance) =>
        Result.Failure<T>(Error.NotFound($"{entity} {instance} not found", instance));

    /// <summary>
    /// Creates a failed Result with a ValidationError.
    /// </summary>
    public static Result<T> Validation<T>(string detail, string fieldName = "") =>
        Result.Failure<T>(Error.Validation(detail, fieldName));

    /// <summary>
    /// Creates a failed Result with an UnauthorizedError.
    /// </summary>
    public static Result<T> Unauthorized<T>(string detail = "Unauthorized") =>
        Result.Failure<T>(Error.Unauthorized(detail));

    /// <summary>
    /// Creates a failed Result with a ForbiddenError.
    /// </summary>
    public static Result<T> Forbidden<T>(string detail = "Forbidden") =>
        Result.Failure<T>(Error.Forbidden(detail));

    /// <summary>
    /// Creates a failed Result with a ConflictError.
    /// </summary>
    public static Result<T> Conflict<T>(string detail) =>
        Result.Failure<T>(Error.Conflict(detail));

    /// <summary>
    /// Creates a failed Result with a ServiceUnavailableError.
    /// </summary>
    public static Result<T> ServiceUnavailable<T>(string detail) =>
        Result.Failure<T>(Error.ServiceUnavailable(detail));

    /// <summary>
    /// Creates a failed Result with an UnexpectedError.
    /// </summary>
    public static Result<T> Unexpected<T>(string detail) =>
        Result.Failure<T>(Error.Unexpected(detail));

    /// <summary>
    /// Creates a failed Result with a DomainError.
    /// </summary>
    public static Result<T> Domain<T>(string detail) =>
        Result.Failure<T>(Error.Domain(detail));

    /// <summary>
    /// Creates a failed Result with a RateLimitError.
    /// </summary>
    public static Result<T> RateLimit<T>(string detail) =>
        Result.Failure<T>(Error.RateLimit(detail));

    /// <summary>
    /// Creates a failed Result with a BadRequestError.
    /// </summary>
    public static Result<T> BadRequest<T>(string detail) =>
        Result.Failure<T>(Error.BadRequest(detail));

    /// <summary>
    /// Creates a failed Result with a GoneError.
    /// </summary>
    public static Result<T> Gone<T>(string detail) =>
        Result.Failure<T>(Error.Gone(detail));

    /// <summary>
    /// Creates a failed Result with a MethodNotAllowedError.
    /// </summary>
    public static Result<T> MethodNotAllowed<T>(string detail, IReadOnlyList<string> allowedMethods) =>
        Result.Failure<T>(Error.MethodNotAllowed(detail, allowedMethods));

    /// <summary>
    /// Creates a failed Result with a NotAcceptableError.
    /// </summary>
    public static Result<T> NotAcceptable<T>(string detail) =>
        Result.Failure<T>(Error.NotAcceptable(detail));

    /// <summary>
    /// Creates a failed Result with an UnsupportedMediaTypeError.
    /// </summary>
    public static Result<T> UnsupportedMediaType<T>(string detail) =>
        Result.Failure<T>(Error.UnsupportedMediaType(detail));

    /// <summary>
    /// Creates a failed Result with a ContentTooLargeError.
    /// </summary>
    public static Result<T> ContentTooLarge<T>(string detail) =>
        Result.Failure<T>(Error.ContentTooLarge(detail));

    /// <summary>
    /// Creates a failed Result with a RangeNotSatisfiableError.
    /// </summary>
    public static Result<T> RangeNotSatisfiable<T>(string detail, long completeLength) =>
        Result.Failure<T>(Error.RangeNotSatisfiable(detail, completeLength));
}