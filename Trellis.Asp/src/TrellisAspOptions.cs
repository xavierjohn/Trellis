namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Trellis;

/// <summary>
/// Configuration options for Trellis ASP.NET Core integration.
/// Controls how domain error types are mapped to HTTP status codes.
/// </summary>
/// <remarks>
/// <para>
/// By default, all standard error types are mapped to conventional HTTP status codes:
/// <list type="table">
///     <listheader>
///         <term>Error Type</term>
///         <description>Default HTTP Status</description>
///     </listheader>
///     <item><term><see cref="ValidationError"/></term><description>400 Bad Request</description></item>
///     <item><term><see cref="BadRequestError"/></term><description>400 Bad Request</description></item>
///     <item><term><see cref="UnauthorizedError"/></term><description>401 Unauthorized</description></item>
///     <item><term><see cref="ForbiddenError"/></term><description>403 Forbidden</description></item>
///     <item><term><see cref="NotFoundError"/></term><description>404 Not Found</description></item>
///     <item><term><see cref="ConflictError"/></term><description>409 Conflict</description></item>
///     <item><term><see cref="DomainError"/></term><description>422 Unprocessable Entity</description></item>
///     <item><term><see cref="RateLimitError"/></term><description>429 Too Many Requests</description></item>
///     <item><term><see cref="UnexpectedError"/></term><description>500 Internal Server Error</description></item>
///     <item><term><see cref="ServiceUnavailableError"/></term><description>503 Service Unavailable</description></item>
/// </list>
/// </para>
/// <para>
/// Use <see cref="MapError{TError}"/> to override any mapping.
/// Unmapped error types fall back to 500 Internal Server Error.
/// </para>
/// </remarks>
/// <example>
/// Zero-config — uses defaults:
/// <code>
/// builder.Services.AddTrellisAsp();
/// </code>
/// </example>
/// <example>
/// Override specific mappings:
/// <code>
/// builder.Services.AddTrellisAsp(options =>
/// {
///     options.MapError&lt;DomainError&gt;(StatusCodes.Status400BadRequest);
/// });
/// </code>
/// </example>
public sealed class TrellisAspOptions
{
    private static readonly TrellisAspOptions _defaultOptions = new();
    private static readonly AsyncLocal<TrellisAspOptions?> _current = new();

    internal static TrellisAspOptions Default => _current.Value ?? _defaultOptions;

    internal static void SetCurrent(TrellisAspOptions options) => _current.Value = options;

    private readonly Dictionary<Type, int> _errorMappings = new()
    {
        [typeof(ValidationError)] = StatusCodes.Status400BadRequest,
        [typeof(BadRequestError)] = StatusCodes.Status400BadRequest,
        [typeof(UnauthorizedError)] = StatusCodes.Status401Unauthorized,
        [typeof(ForbiddenError)] = StatusCodes.Status403Forbidden,
        [typeof(NotFoundError)] = StatusCodes.Status404NotFound,
        [typeof(ConflictError)] = StatusCodes.Status409Conflict,
        [typeof(DomainError)] = StatusCodes.Status422UnprocessableEntity,
        [typeof(RateLimitError)] = StatusCodes.Status429TooManyRequests,
        [typeof(UnexpectedError)] = StatusCodes.Status500InternalServerError,
        [typeof(ServiceUnavailableError)] = StatusCodes.Status503ServiceUnavailable,
    };

    /// <summary>
    /// Maps an error type to an HTTP status code. Overrides the default mapping if one exists.
    /// </summary>
    /// <typeparam name="TError">The error type to map. Must derive from <see cref="Error"/>.</typeparam>
    /// <param name="statusCode">The HTTP status code to return for this error type.</param>
    /// <returns>The options instance for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// options.MapError&lt;DomainError&gt;(StatusCodes.Status400BadRequest)
    ///        .MapError&lt;ConflictError&gt;(StatusCodes.Status422UnprocessableEntity);
    /// </code>
    /// </example>
    public TrellisAspOptions MapError<TError>(int statusCode) where TError : Error
    {
        _errorMappings[typeof(TError)] = statusCode;
        return this;
    }

    /// <summary>
    /// Resolves the HTTP status code for the given error by walking up the type hierarchy.
    /// Returns 500 Internal Server Error if no mapping is found.
    /// </summary>
    internal int GetStatusCode(Error error)
    {
        var type = error.GetType();
        while (type is not null && type != typeof(object))
        {
            if (_errorMappings.TryGetValue(type, out var statusCode))
                return statusCode;
            type = type.BaseType;
        }

        return StatusCodes.Status500InternalServerError;
    }

}