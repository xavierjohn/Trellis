namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Trellis;

/// <summary>
/// Configuration options for Trellis ASP.NET Core integration.
/// Controls how domain error types and wrapped HTTP transport faults are mapped to status codes.
/// </summary>
/// <remarks>
/// <para>
/// By default, all standard error types are mapped to conventional HTTP status codes:
/// <list type="table">
///     <listheader>
///         <term>Error Type</term>
///         <description>Default HTTP Status</description>
///     </listheader>
///     <item><term><see cref="Error.InvalidInput"/></term><description>422 Unprocessable Entity</description></item>
///     <item><term><see cref="Error.InvariantViolation"/></term><description>422 Unprocessable Entity</description></item>
///     <item><term><see cref="Error.AuthenticationRequired"/></term><description>401 Unauthorized</description></item>
///     <item><term><see cref="Error.Forbidden"/></term><description>403 Forbidden</description></item>
///     <item><term><see cref="Error.NotFound"/></term><description>404 Not Found</description></item>
///     <item><term><see cref="Error.Conflict"/></term><description>409 Conflict</description></item>
///     <item><term><see cref="Error.Gone"/></term><description>410 Gone</description></item>
///     <item><term><see cref="Error.TransportFault"/></term><description>Wrapped <see cref="HttpError"/> cases map by inner HTTP fault (405/406/412/413/415/416/428).</description></item>
///     <item><term><see cref="Error.RateLimited"/></term><description>429 Too Many Requests</description></item>
///     <item><term><see cref="Error.Unexpected"/></term><description>500 Internal Server Error</description></item>
///     <item><term><see cref="Error.Unavailable"/></term><description>503 Service Unavailable</description></item>
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

    /// <summary>
    /// The language tag emitted as <c>Content-Language</c> on problem responses, or
    /// <see langword="null"/> (the default) to emit no such header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every Trellis problem response ships prose in <c>title</c> and <c>detail</c>. Setting this
    /// declares what language that prose is in, e.g. <c>"en"</c>.
    /// </para>
    /// <para>
    /// The default is deliberately unset: <c>detail</c> is frequently application-supplied, so the
    /// framework cannot know its language, and emitting <c>Content-Language: en</c> unconditionally
    /// would assert something it has not checked as well as silently changing behaviour for every
    /// existing application.
    /// </para>
    /// <para>
    /// This is a single static value, not server-side negotiation. Nothing reads
    /// <c>Accept-Language</c>, and no <c>Vary</c> header is emitted, because the response does not
    /// in fact vary by it. This composes with rather than competes against the reason-code and
    /// arguments mechanism: <c>detail</c> is negotiated prose where negotiation exists, while codes
    /// and arguments let the <em>client</em> hold the catalog, which is the only option when the
    /// server has no translations at all.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTrellisAsp(options =&gt; options.ProblemContentLanguage = "en");
    /// </code>
    /// </example>
    public string? ProblemContentLanguage { get; set; }

    /// <summary>
    /// When <c>true</c>, calls to <c>.WithVersionedRoute()</c> (or its pinned overload)
    /// that would silently skip <c>api-version</c> injection because the target endpoint
    /// has no <c>ApiVersionMetadata</c> attached throw
    /// <see cref="InvalidOperationException"/> on every offending request instead of
    /// logging a single warning per endpoint. Defaults to <c>false</c> (warn-once-per-endpoint).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework's <c>.WithVersionedRoute()</c> resolver silently no-ops on hosts that
    /// never call <c>services.AddApiVersioning(...)</c> so the helper composes cleanly in
    /// unversioned hosts. Mid-migration — when <c>AddApiVersioning(...)</c> has been removed
    /// but legacy <c>.WithVersionedRoute()</c> chains remain — that silent skip can hide a
    /// downstream contract regression (clients stop receiving <c>api-version</c> in
    /// <c>Location</c> headers). Set this option to <c>true</c> in non-Production
    /// environments to surface the misconfiguration immediately rather than at the next
    /// client integration test.
    /// </para>
    /// <para>
    /// When <c>false</c> (default) the framework logs a single warning per
    /// <c>(endpoint, AppDomain)</c> pair via the <c>"Trellis.Asp.ApiVersioning"</c>
    /// <see cref="Microsoft.Extensions.Logging.ILogger"/> category. When <c>true</c>, every
    /// silent-skip occurrence throws; de-duplication does not apply because the request
    /// fails fast and never reaches the consumer.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddTrellisAsp(o =&gt; o.FailFastOnSilentVersionInjection = builder.Environment.IsDevelopment());
    /// </code>
    /// </example>
    public bool FailFastOnSilentVersionInjection { get; set; }

    /// <summary>
    /// When <c>true</c> (default), <see cref="ResponseFailureWriter"/> synthesises
    /// <c>ProblemDetails.Instance</c> from the error's <see cref="ResourceRef"/> when the
    /// request URL does not already identify the failing resource (for example, a
    /// <c>POST /orders</c> whose body references a missing customer becomes
    /// <c>Instance = "/customers/abc-123"</c> instead of <c>"/orders"</c>). The original
    /// request URL is preserved under <c>ProblemDetails.Extensions["request"]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default-on because the synthesised URI is strictly more informative than the
    /// request URL when synthesis applies; when it does not (no <see cref="ResourceRef"/>,
    /// no id, or the id already appears in the URL) the original request URL is left in
    /// place and no extension is added.
    /// </para>
    /// <para>
    /// Set to <c>false</c> only when strict backward compatibility with the previous
    /// behaviour (always emit <c>Instance = path+query</c>) is required.
    /// </para>
    /// </remarks>
    public bool SynthesizeProblemDetailsInstanceFromResourceRef { get; set; } = true;

    private static readonly Dictionary<Type, int> s_httpTransportMappings = new()
    {
        [typeof(HttpError.MethodNotAllowed)] = StatusCodes.Status405MethodNotAllowed,
        [typeof(HttpError.NotAcceptable)] = StatusCodes.Status406NotAcceptable,
        [typeof(HttpError.PreconditionFailed)] = StatusCodes.Status412PreconditionFailed,
        [typeof(HttpError.ContentTooLarge)] = StatusCodes.Status413RequestEntityTooLarge,
        [typeof(HttpError.UnsupportedMediaType)] = StatusCodes.Status415UnsupportedMediaType,
        [typeof(HttpError.RangeNotSatisfiable)] = StatusCodes.Status416RangeNotSatisfiable,
        [typeof(HttpError.PreconditionRequired)] = StatusCodes.Status428PreconditionRequired,
    };

    private readonly Dictionary<Type, int> _errorMappings = new()
    {
        [typeof(Error.InvalidInput)] = StatusCodes.Status422UnprocessableEntity,
        [typeof(Error.InvariantViolation)] = StatusCodes.Status422UnprocessableEntity,
        [typeof(Error.AuthenticationRequired)] = StatusCodes.Status401Unauthorized,
        [typeof(Error.Forbidden)] = StatusCodes.Status403Forbidden,
        [typeof(Error.NotFound)] = StatusCodes.Status404NotFound,
        [typeof(Error.Conflict)] = StatusCodes.Status409Conflict,
        [typeof(Error.Gone)] = StatusCodes.Status410Gone,
        [typeof(Error.RateLimited)] = StatusCodes.Status429TooManyRequests,
        [typeof(Error.Unexpected)] = StatusCodes.Status500InternalServerError,
        [typeof(Error.Unavailable)] = StatusCodes.Status503ServiceUnavailable,
    };

    /// <summary>
    /// Maps an error type to an HTTP status code. Overrides the default mapping if one exists.
    /// </summary>
    /// <typeparam name="TError">The error type to map. Must derive from <see cref="Error"/>.</typeparam>
    /// <param name="statusCode">The HTTP status code to return for this error type.</param>
    /// <returns>The options instance for fluent chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="statusCode"/> is outside 100–599.</exception>
    /// <example>
    /// <code>
    /// options.MapError&lt;Error.Conflict&gt;(StatusCodes.Status400BadRequest)
    ///        .MapError&lt;Error.InvalidInput&gt;(StatusCodes.Status422UnprocessableEntity);
    /// </code>
    /// </example>
    public TrellisAspOptions MapError<TError>(int statusCode) where TError : Error
    {
        ErrorStatusCodeResolver.ValidateStatusCode(statusCode, nameof(statusCode));
        _errorMappings[typeof(TError)] = statusCode;
        return this;
    }

    /// <summary>
    /// Resolves the HTTP status code for the given error by walking up the error type hierarchy.
    /// For <see cref="Error.TransportFault"/>, explicit outer-type overrides win first; otherwise
    /// wrapped <see cref="HttpError"/> cases are mapped by their inner subtype.
    /// Returns 500 Internal Server Error if no mapping is found.
    /// </summary>
    internal int GetStatusCode(Error error)
    {
        if (error is Error.Aggregate agg)
            return ComputeWorstStatus(agg, this);

        // Error.Unexpected carries an open-ended ReasonCode; "not_implemented" is the one
        // legacy code that historically mapped to 501 rather than 500. Checked before the
        // type-based lookup so an Unexpected mapping override doesn't shadow it.
        if (error is Error.Unexpected { ReasonCode: "not_implemented" })
            return StatusCodes.Status501NotImplemented;

        var mappedStatus = TryResolveMapping(_errorMappings, error.GetType());
        if (mappedStatus.HasValue)
            return mappedStatus.Value;

        if (error is Error.TransportFault { Fault: HttpError httpError })
        {
            mappedStatus = TryResolveMapping(s_httpTransportMappings, httpError.GetType());
            if (mappedStatus.HasValue)
                return mappedStatus.Value;
        }

        return StatusCodes.Status500InternalServerError;
    }

    // Aggregate worst-status rule: 5xx beats any 4xx; 4xx beats 1xx-3xx; tie-break by highest numeric.
    private static int ComputeWorstStatus(Error.Aggregate aggregate, TrellisAspOptions options)
    {
        var worst = 0;
        var worstClass = 0;
        foreach (var child in aggregate.Errors.Items)
        {
            var s = options.GetStatusCode(child);
            var cls = s / 100;
            if (cls > worstClass || (cls == worstClass && s > worst))
            {
                worst = s;
                worstClass = cls;
            }
        }

        return worst == 0 ? StatusCodes.Status500InternalServerError : worst;
    }

    private static int? TryResolveMapping(Dictionary<Type, int> mappings, Type type)
    {
        while (type != typeof(object))
        {
            if (mappings.TryGetValue(type, out var statusCode))
                return statusCode;
            type = type.BaseType!;
        }

        return null;
    }

}