namespace Trellis.Asp;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Trellis;

/// <summary>
/// The single Trellis verb for converting <see cref="Result{TValue}"/> /
/// <see cref="WriteOutcome{T}"/> / <see cref="Page{T}"/> values to ASP.NET Core HTTP responses. Works
/// in both Minimal API endpoints and MVC controllers (.NET 7+ executes
/// <see cref="Microsoft.AspNetCore.Http.IResult"/> natively in MVC). For typed
/// <c>ActionResult&lt;T&gt;</c> signatures, chain
/// <see cref="ActionResultAdapterExtensions.AsActionResult{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Configure protocol semantics via the optional fluent <see cref="HttpResponseOptionsBuilder{T}"/>:
/// <c>WithETag</c>, <c>WithLastModified</c>, <c>Vary</c>, <c>CreatedAtRoute</c>,
/// <c>EvaluatePreconditions</c>, <c>HonorPrefer</c>, etc. All selectors run against the domain value;
/// the response body may be projected separately via the <c>body</c> overload.
/// </para>
/// <para>
/// <b>No-payload success:</b> <c>Result&lt;Unit&gt;</c> is rendered as <c>204 No Content</c>
/// (with <c>HonorPrefer</c>/<c>Vary</c>/ETag headers still applied).
/// </para>
/// </remarks>
public static class HttpResponseExtensions
{
    #region Error

    /// <summary>
    /// Maps a standalone <see cref="Error"/> to a Problem Details HTTP response.
    /// Useful for endpoints that produce a deterministic error without a <see cref="Result{TValue}"/>
    /// pipeline (e.g. diagnostic / fault demonstration endpoints).
    /// </summary>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResponse(
        this Error error,
        Action<HttpResponseOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        var builder = new HttpResponseOptionsBuilder();
        configure?.Invoke(builder);
        return new TrellisErrorOnlyResult(error, builder.Build());
    }

    #endregion

    #region Result<T>

    /// <summary>
    /// Maps a <see cref="Result{TValue}"/> to a 200 OK (success, body = <typeparamref name="T"/>) or
    /// Problem Details (failure). When configured via
    /// <see cref="HttpResponseOptionsBuilder{T}.Created(string)"/> or
    /// <see cref="HttpResponseOptionsBuilder{T}.CreatedAtRoute(string, Func{T, Microsoft.AspNetCore.Routing.RouteValueDictionary})"/>, returns 201 Created with a
    /// Location header.
    /// </summary>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResponse<T>(
        this Result<T> result,
        Action<HttpResponseOptionsBuilder<T>>? configure = null)
    {
        var builder = new HttpResponseOptionsBuilder<T>();
        configure?.Invoke(builder);
        var opts = builder.Build();
        return new TrellisHttpResult<T, T>(result, null, opts);
    }

    /// <summary>
    /// Maps a <see cref="Result{TDomain}"/> to an HTTP response, projecting the body via
    /// <paramref name="body"/>. Selectors in the options builder still run against the domain value.
    /// </summary>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResponse<TDomain, TBody>(
        this Result<TDomain> result,
        Func<TDomain, TBody> body,
        Action<HttpResponseOptionsBuilder<TDomain>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        var builder = new HttpResponseOptionsBuilder<TDomain>();
        configure?.Invoke(builder);
        var opts = builder.Build();
        return new TrellisHttpResult<TDomain, TBody>(result, body, opts);
    }

    /// <summary>Async <see cref="Task"/> overload.</summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<T>(
        this Task<Result<T>> resultTask,
        Action<HttpResponseOptionsBuilder<T>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return (await resultTask.ConfigureAwait(false)).ToHttpResponse(configure);
    }

    /// <summary>Async <see cref="ValueTask"/> overload.</summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<T>(
        this ValueTask<Result<T>> resultTask,
        Action<HttpResponseOptionsBuilder<T>>? configure = null)
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(configure);

    /// <summary>Async <see cref="Task"/> overload with body projection.</summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<TDomain, TBody>(
        this Task<Result<TDomain>> resultTask,
        Func<TDomain, TBody> body,
        Action<HttpResponseOptionsBuilder<TDomain>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return (await resultTask.ConfigureAwait(false)).ToHttpResponse(body, configure);
    }

    /// <summary>Async <see cref="ValueTask"/> overload with body projection.</summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<TDomain, TBody>(
        this ValueTask<Result<TDomain>> resultTask,
        Func<TDomain, TBody> body,
        Action<HttpResponseOptionsBuilder<TDomain>>? configure = null)
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(body, configure);

    #endregion

    #region Result<WriteOutcome<T>>

    /// <summary>
    /// Maps a <see cref="Result{T}"/> of <see cref="WriteOutcome{T}"/> to an HTTP response with
    /// status driven by the outcome variant per RFC 9110:
    /// Created -&gt; 201 + Location, Updated -&gt; 200 (or 204 with <c>Prefer: return=minimal</c>),
    /// UpdatedNoContent -&gt; 204, Accepted -&gt; 202.
    /// </summary>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResponse<T>(
        this Result<WriteOutcome<T>> result,
        Action<HttpResponseOptionsBuilder<T>>? configure = null)
    {
        var builder = new HttpResponseOptionsBuilder<T>();
        configure?.Invoke(builder);
        var opts = builder.Build();
        return new TrellisWriteOutcomeResult<T, T>(result, null, opts);
    }

    /// <summary>
    /// Maps a <see cref="Result{T}"/> of <see cref="WriteOutcome{T}"/> with body projection.
    /// </summary>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResponse<TDomain, TBody>(
        this Result<WriteOutcome<TDomain>> result,
        Func<TDomain, TBody> body,
        Action<HttpResponseOptionsBuilder<TDomain>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        var builder = new HttpResponseOptionsBuilder<TDomain>();
        configure?.Invoke(builder);
        var opts = builder.Build();
        return new TrellisWriteOutcomeResult<TDomain, TBody>(result, body, opts);
    }

    /// <summary>Async <see cref="Task"/> overload.</summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<T>(
        this Task<Result<WriteOutcome<T>>> resultTask,
        Action<HttpResponseOptionsBuilder<T>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return (await resultTask.ConfigureAwait(false)).ToHttpResponse(configure);
    }

    /// <summary>Async <see cref="ValueTask"/> overload.</summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<T>(
        this ValueTask<Result<WriteOutcome<T>>> resultTask,
        Action<HttpResponseOptionsBuilder<T>>? configure = null)
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(configure);

    /// <summary>Async <see cref="Task"/> overload with body projection.</summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<TDomain, TBody>(
        this Task<Result<WriteOutcome<TDomain>>> resultTask,
        Func<TDomain, TBody> body,
        Action<HttpResponseOptionsBuilder<TDomain>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return (await resultTask.ConfigureAwait(false)).ToHttpResponse(body, configure);
    }

    /// <summary>Async <see cref="ValueTask"/> overload with body projection.</summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<TDomain, TBody>(
        this ValueTask<Result<WriteOutcome<TDomain>>> resultTask,
        Func<TDomain, TBody> body,
        Action<HttpResponseOptionsBuilder<TDomain>>? configure = null)
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(body, configure);

    #endregion

    #region Result<Page<T>>

    /// <summary>
    /// Maps a <see cref="Result{T}"/> of <see cref="Page{T}"/> to an HTTP response with the
    /// paginated envelope and an RFC 8288 <c>Link</c> header. Delegates to the existing paginated
    /// helper (which carries the RFC 8288 logic) and feeds the failure path through the unified
    /// error mapping.
    /// </summary>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResponse<T, TBody>(
        this Result<Page<T>> result,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TBody> body,
        Action<HttpResponseOptionsBuilder<Page<T>>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(nextUrlBuilder);
        ArgumentNullException.ThrowIfNull(body);

        var builder = new HttpResponseOptionsBuilder<Page<T>>();
        configure?.Invoke(builder);
        var opts = builder.Build();

        if (!result.TryGetValue(out var page, out var pageError))
        {
            return new TrellisErrorOnlyResult(pageError, new HttpResponseOptions<object>
            {
                ErrorMapper = opts.ErrorMapper,
                ErrorOverrides = opts.ErrorOverrides,
                VaryForActor = opts.VaryForActor,
                CacheControl = opts.CacheControl,
            });
        }

        // Detect any builder-option that the bare Results.Ok / PagedHttpResult would silently
        // drop on its way to the wire. The wrapper applies them before delegating to the
        // inner result so the paged path matches the non-paged TrellisHttpResult contract.
        var hasCacheControl = opts.CacheControl is not null || opts.CacheControlSelector is not null;
        var hasOptionalHeaders =
            (opts.Vary is { Count: > 0 })
            || (opts.ContentLanguage is { Count: > 0 })
            || (opts.Links is { Count: > 0 })
            || opts.ETagSelector is not null
            || opts.LastModifiedSelector is not null
            || opts.ContentLocationSelector is not null
            || opts.EvaluatePreconditions;

        // Lazy inner-result factory: defers PagedResponseBuilder.Build (which projects every
        // item via the `body` mapper and allocates the envelope list) until a success-emit
        // point is reached. This avoids running the body projector when a conditional GET
        // short-circuits to 304 or a precondition fails into 412.
        Microsoft.AspNetCore.Http.IResult BuildInner()
        {
            var (envelope, linkHeader) = PagedResponseBuilder.Build(page, nextUrlBuilder, body);
            var ok = Results.Ok(envelope);
            return linkHeader is null ? ok : new PagedHttpResult(ok, linkHeader);
        }

        if (opts.VaryForActor || hasCacheControl || hasOptionalHeaders)
        {
            // Capture `page` in closures so selectors evaluate inside ExecuteAsync — matches
            // the non-paged timing and avoids stale state if the IResult is constructed in
            // one scope and executed in another.
            Func<EntityTagValue?>? resolveETag = opts.ETagSelector is { } et
                ? () => et(page)
                : null;
            Func<DateTimeOffset?>? resolveLastModified = opts.LastModifiedSelector is { } lm
                ? () => lm(page)
                : null;
            Func<string?>? resolveContentLocation = opts.ContentLocationSelector is { } cls
                ? () => cls(page)
                : null;
            Func<System.Net.Http.Headers.CacheControlHeaderValue?>? resolveCacheControlSelector =
                opts.CacheControlSelector is { } ccSel ? () => ccSel(page) : null;
            Func<HttpContext, Error, int>? resolveErrorStatusCode = opts.EvaluatePreconditions
                ? (http, err) => ErrorStatusCodeResolver.Resolve(http, err, opts.ErrorMapper, opts.ErrorOverrides)
                : null;

            return new PagedSuccessHeaderWrapper(
                BuildInner,
                opts.VaryForActor,
                opts.CacheControl,
                resolveCacheControlSelector,
                opts.Vary,
                opts.ContentLanguage,
                opts.Links,
                resolveETag,
                resolveLastModified,
                resolveContentLocation,
                opts.EvaluatePreconditions,
                resolveErrorStatusCode,
                ResourceRef.For<Page<T>>());
        }

        // No wrapper required — build the envelope eagerly and return the bare inner. There
        // is no precondition / 304 / 412 path on this branch, so deferring would change
        // nothing observable.
        return BuildInner();
    }

    /// <summary>Async <see cref="Task"/> overload.</summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<T, TBody>(
        this Task<Result<Page<T>>> resultTask,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TBody> body,
        Action<HttpResponseOptionsBuilder<Page<T>>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        return (await resultTask.ConfigureAwait(false)).ToHttpResponse(nextUrlBuilder, body, configure);
    }

    /// <summary>Async <see cref="ValueTask"/> overload.</summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<T, TBody>(
        this ValueTask<Result<Page<T>>> resultTask,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TBody> body,
        Action<HttpResponseOptionsBuilder<Page<T>>>? configure = null)
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(nextUrlBuilder, body, configure);

    #endregion
}

/// <summary>Internal IResult that only writes a failure response (used for non-generic Result failures).</summary>
internal sealed class TrellisErrorOnlyResult : Microsoft.AspNetCore.Http.IResult
{
    private readonly Error _error;
    private readonly HttpResponseOptions<object> _options;

    public TrellisErrorOnlyResult(Error error, HttpResponseOptions<object> options)
    {
        _error = error;
        _options = options;
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        // Apply actor-vary headers BEFORE WriteAsync so the failure response partitions
        // correctly across actors (e.g. cacheable 404/422). Same fail-closed semantics
        // as the success path.
        if (_options.VaryForActor)
            TrellisHttpResult<object, object>.AppendActorVaryHeaders(httpContext);

        // Static Cache-Control flows through to failure responses too — the protection a
        // sensitive endpoint declares via `WithCacheControl(CacheControl.NoStore())` must
        // cover 403 / 404 / validation responses, not just the success-path body.
        if (_options.CacheControl is { } staticCc)
            httpContext.Response.Headers["Cache-Control"] = staticCc.ToString();

        var statusCode = ResolveStatusCode(httpContext, _error);
        return ResponseFailureWriter.WriteAsync(
            httpContext,
            _error,
            statusCode,
            err => ResolveStatusCode(httpContext, err));
    }

    private int ResolveStatusCode(HttpContext httpContext, Error error) =>
        ErrorStatusCodeResolver.Resolve(httpContext, error, _options.ErrorMapper, _options.ErrorOverrides);
}

/// <summary>
/// Internal IResult wrapper that applies <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/>
/// to a wrapped result. Used by paths that don't otherwise consult the builder options
/// (e.g. paginated success responses) so the actor-vary contract holds uniformly.
/// </summary>
internal sealed class ActorVaryWrapperResult(Microsoft.AspNetCore.Http.IResult inner) : Microsoft.AspNetCore.Http.IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        TrellisHttpResult<object, object>.AppendActorVaryHeaders(httpContext);
        return inner.ExecuteAsync(httpContext);
    }
}

/// <summary>
/// Internal IResult wrapper for paged success responses. Applies every
/// <see cref="HttpResponseOptionsBuilder{TDomain}"/>-driven header that the bare
/// <see cref="Microsoft.AspNetCore.Http.Results.Ok(object?)"/> / <see cref="PagedHttpResult"/>
/// would otherwise silently drop, so the paged contract matches the non-paged
/// <c>TrellisHttpResult</c>: <c>VaryForActor</c>, static and selector <c>Cache-Control</c>,
/// the explicit <c>Vary</c> list, <c>Content-Language</c>, <c>ETag</c>,
/// <c>Last-Modified</c>, <c>Content-Location</c>, and conditional-request preconditions
/// (<c>If-None-Match</c> / <c>If-Modified-Since</c> → 304; failing <c>If-Match</c> /
/// <c>If-Unmodified-Since</c> → 412).
/// </summary>
/// <remarks>
/// <para>
/// All domain-shaped selectors (ETag, Last-Modified, Content-Location, selector
/// Cache-Control) close over the <c>Page&lt;T&gt;</c> value at the call site and evaluate
/// inside <see cref="ExecuteAsync"/> — matching the non-paged result type's timing and
/// avoiding stale state if the IResult is constructed in one scope and executed in another.
/// </para>
/// <para>
/// ETag and Last-Modified are resolved exactly once per request and reused for both header
/// emission and precondition evaluation, so a non-deterministic or expensive selector does
/// not produce inconsistent header-vs-metadata values.
/// </para>
/// <para>
/// Selector-derived <c>Cache-Control</c> is applied only at the success-emit points (the
/// inner 200 OK and the 304 Not Modified short-circuit). A generated 412 PreconditionFailed
/// does not inherit the selector value — matching the non-paged contract that prevents
/// caching of mid-flow failures. Static <c>Cache-Control</c> remains applied to both
/// success and failure paths because it represents the consumer's policy for the endpoint
/// as a whole.
/// </para>
/// </remarks>
internal sealed class PagedSuccessHeaderWrapper(
    Func<Microsoft.AspNetCore.Http.IResult> buildInner,
    bool varyForActor,
    System.Net.Http.Headers.CacheControlHeaderValue? staticCacheControl,
    Func<System.Net.Http.Headers.CacheControlHeaderValue?>? resolveCacheControlSelector,
    IReadOnlyList<string>? vary,
    IReadOnlyList<string>? contentLanguage,
    IReadOnlyList<string>? links,
    Func<EntityTagValue?>? resolveETag,
    Func<DateTimeOffset?>? resolveLastModified,
    Func<string?>? resolveContentLocation,
    bool evaluatePreconditions,
    Func<HttpContext, Error, int>? resolveErrorStatusCode,
    ResourceRef preconditionFailedRef) : Microsoft.AspNetCore.Http.IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (varyForActor)
            TrellisHttpResult<object, object>.AppendActorVaryHeaders(httpContext);

        var response = httpContext.Response;

        // Static Cache-Control applies before any branch — consumer's policy for the endpoint
        // (e.g. private, no-store) should govern both the success body and any generated 412.
        if (staticCacheControl is not null)
            response.Headers["Cache-Control"] = staticCacheControl.ToString();

        if (vary is { Count: > 0 })
        {
            foreach (var v in vary)
                TrellisHttpResult<object, object>.AppendVaryUnique(response, v);
        }

        if (contentLanguage is { Count: > 0 })
            response.Headers.ContentLanguage = string.Join(", ", contentLanguage);

        LinkHeader.Append(response, links);

        // Resolve ETag and Last-Modified once per execution; the cached values are reused
        // for header emission AND precondition evaluation so non-deterministic selectors
        // cannot produce inconsistent header-vs-metadata values. Last-Modified is truncated
        // to second precision before caching because the wire-format `R` HTTP-date emitter
        // and HTTP clients both work at second granularity — keeping sub-second precision
        // in the metadata would cause `If-Modified-Since` revalidation with the exact
        // emitted header to miss the 304 path, since `selectorRaw > Parse(emittedHeader)`.
        var etag = resolveETag?.Invoke();
        var lastModified = TruncateToSeconds(resolveLastModified?.Invoke());

        if (etag is not null)
            response.Headers.ETag = etag.ToHeaderValue();

        if (lastModified.HasValue)
            response.Headers.LastModified = lastModified.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        if (resolveContentLocation?.Invoke() is { Length: > 0 } loc)
            response.Headers["Content-Location"] = loc;

        if (evaluatePreconditions)
        {
            var method = httpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            {
                var metadata = BuildMetadataForEvaluation(etag, lastModified);
                if (metadata is not null)
                {
                    var decision = ConditionalRequestEvaluator.Evaluate(httpContext.Request, metadata, out var failedKind);
                    if (decision == ConditionalDecision.NotModified)
                    {
                        // 304 is a success disposition — apply the selector so the revalidation
                        // response carries the same Cache-Control policy a fresh 200 would.
                        ApplyCacheControlSelector(response);
                        await Results.StatusCode(StatusCodes.Status304NotModified).ExecuteAsync(httpContext).ConfigureAwait(false);
                        return;
                    }

                    if (decision == ConditionalDecision.PreconditionFailed)
                    {
                        // 412 deliberately does NOT inherit the selector value — a transient
                        // client error must not be cached as a negative response.
                        var pf = new Error.TransportFault(new HttpError.PreconditionFailed(
                            preconditionFailedRef,
                            failedKind ?? PreconditionKind.IfMatch))
                        { Detail = "A conditional request header evaluated to false." };

                        var statusCode = resolveErrorStatusCode is not null
                            ? resolveErrorStatusCode(httpContext, pf)
                            : StatusCodes.Status412PreconditionFailed;
                        await ResponseFailureWriter.WriteAsync(httpContext, pf, statusCode).ConfigureAwait(false);
                        return;
                    }
                }
            }
        }

        // Success-emit point: apply the selector Cache-Control, then build the paged envelope
        // lazily (avoids running the body projector when a 304 / 412 short-circuit fires
        // upstream).
        ApplyCacheControlSelector(response);
        var inner = buildInner();
        await inner.ExecuteAsync(httpContext).ConfigureAwait(false);
    }

    private void ApplyCacheControlSelector(HttpResponse response)
    {
        if (resolveCacheControlSelector is null)
            return;
        var v = resolveCacheControlSelector();
        if (v is not null)
            response.Headers["Cache-Control"] = v.ToString();
    }

    private static RepresentationMetadata? BuildMetadataForEvaluation(EntityTagValue? etag, DateTimeOffset? lastModified)
    {
        if (etag is null && lastModified is null)
            return null;

        var b = RepresentationMetadata.Create();
        if (etag is not null)
            b = b.SetETag(etag);
        if (lastModified.HasValue)
            b = b.SetLastModified(lastModified.Value);
        return b.Build();
    }

    private static DateTimeOffset? TruncateToSeconds(DateTimeOffset? value) =>
        value.HasValue ? value.Value.AddTicks(-(value.Value.Ticks % TimeSpan.TicksPerSecond)) : null;
}