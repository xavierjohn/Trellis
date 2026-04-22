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
///     <item><term><see cref="Error.UnprocessableContent"/></term><description>422 Unprocessable Entity</description></item>
///     <item><term><see cref="Error.BadRequest"/></term><description>400 Bad Request</description></item>
///     <item><term><see cref="Error.Unauthorized"/></term><description>401 Unauthorized</description></item>
///     <item><term><see cref="Error.Forbidden"/></term><description>403 Forbidden</description></item>
///     <item><term><see cref="Error.NotFound"/></term><description>404 Not Found</description></item>
///     <item><term><see cref="Error.Conflict"/></term><description>409 Conflict</description></item>
///     <item><term><see cref="Error.PreconditionFailed"/></term><description>412 Precondition Failed</description></item>
///     <item><term><see cref="Error.PreconditionRequired"/></term><description>428 Precondition Required</description></item>
///     <item><term><see cref="Error.TooManyRequests"/></term><description>429 Too Many Requests</description></item>
///     <item><term><see cref="Error.InternalServerError"/></term><description>500 Internal Server Error</description></item>
///     <item><term><see cref="Error.Unexpected"/></term><description>500 Internal Server Error</description></item>
///     <item><term><see cref="Error.ServiceUnavailable"/></term><description>503 Service Unavailable</description></item>
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
    /// <summary>
    /// Zero-configuration default mappings used when the runtime cannot resolve
    /// <see cref="TrellisAspOptions"/> from the request's <see cref="IServiceProvider"/>
    /// (e.g. the host did not call <c>AddTrellisAsp</c>, or a result is executed outside an
    /// ASP.NET request). Production code paths obtain the configured instance from DI;
    /// this fallback exists strictly to keep <c>IResult.ExecuteAsync</c> total.
    /// </summary>
    /// <remarks>
    /// Internal to prevent consumers from accidentally calling
    /// <see cref="MapError{TError}"/> on this shared instance, which would mutate
    /// global state and reintroduce the cross-host / cross-test leakage that the
    /// DI-resolved options model exists to eliminate. Hosts that want a different
    /// default must call <c>AddTrellisAsp(options =&gt; ...)</c>.
    /// </remarks>
    internal static TrellisAspOptions SystemDefault { get; } = new();

    private readonly Dictionary<Type, int> _errorMappings = new()
    {
        [typeof(Error.UnprocessableContent)] = StatusCodes.Status422UnprocessableEntity,
        [typeof(Error.BadRequest)] = StatusCodes.Status400BadRequest,
        [typeof(Error.Unauthorized)] = StatusCodes.Status401Unauthorized,
        [typeof(Error.Forbidden)] = StatusCodes.Status403Forbidden,
        [typeof(Error.NotFound)] = StatusCodes.Status404NotFound,
        [typeof(Error.MethodNotAllowed)] = StatusCodes.Status405MethodNotAllowed,
        [typeof(Error.NotAcceptable)] = StatusCodes.Status406NotAcceptable,
        [typeof(Error.Conflict)] = StatusCodes.Status409Conflict,
        [typeof(Error.Gone)] = StatusCodes.Status410Gone,
        [typeof(Error.PreconditionFailed)] = StatusCodes.Status412PreconditionFailed,
        [typeof(Error.ContentTooLarge)] = StatusCodes.Status413RequestEntityTooLarge,
        [typeof(Error.UnsupportedMediaType)] = StatusCodes.Status415UnsupportedMediaType,
        [typeof(Error.RangeNotSatisfiable)] = StatusCodes.Status416RangeNotSatisfiable,
        [typeof(Error.PreconditionRequired)] = StatusCodes.Status428PreconditionRequired,
        [typeof(Error.TooManyRequests)] = StatusCodes.Status429TooManyRequests,
        [typeof(Error.InternalServerError)] = StatusCodes.Status500InternalServerError,
        [typeof(Error.Unexpected)] = StatusCodes.Status500InternalServerError,
        [typeof(Error.NotImplemented)] = StatusCodes.Status501NotImplemented,
        [typeof(Error.ServiceUnavailable)] = StatusCodes.Status503ServiceUnavailable,
    };

    /// <summary>
    /// Maps an error type to an HTTP status code. Overrides the default mapping if one exists.
    /// </summary>
    /// <typeparam name="TError">The error type to map. Must derive from <see cref="Error"/>.</typeparam>
    /// <param name="statusCode">The HTTP status code to return for this error type.</param>
    /// <returns>The options instance for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// options.MapError&lt;Error.Conflict&gt;(StatusCodes.Status400BadRequest)
    ///        .MapError&lt;Error.UnprocessableContent&gt;(StatusCodes.Status422UnprocessableEntity);
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