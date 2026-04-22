namespace Trellis.Asp;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;

/// <summary>
/// The single Trellis verb for converting <see cref="Result"/> / <see cref="Result{TValue}"/> /
/// <see cref="WriteOutcome{T}"/> / <see cref="Page{T}"/> values to ASP.NET Core HTTP responses. Works
/// in both Minimal API endpoints and MVC controllers (.NET 7+ executes
/// <see cref="Microsoft.AspNetCore.Http.IResult"/> natively in MVC). For typed
/// <c>ActionResult&lt;T&gt;</c> signatures, chain
/// <see cref="ActionResultAdapterExtensions.AsActionResult{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the v1 quartet of <c>ToHttpResult</c> / <c>ToActionResult</c> /
/// <c>ToCreatedHttpResult</c> / <c>ToUpdatedHttpResult</c> extensions across MVC and Minimal API.
/// </para>
/// <para>
/// Configure protocol semantics via the optional fluent <see cref="HttpResponseOptionsBuilder{T}"/>:
/// <c>WithETag</c>, <c>WithLastModified</c>, <c>Vary</c>, <c>CreatedAtRoute</c>,
/// <c>EvaluatePreconditions</c>, <c>HonorPrefer</c>, etc. All selectors run against the domain value;
/// the response body may be projected separately via the <c>body</c> overload.
/// </para>
/// </remarks>
public static class HttpResponseExtensions
{
    #region Result (non-generic)

    /// <summary>
    /// Maps a non-generic <see cref="Result"/> to a 204 No Content (success) or a Problem Details
    /// response (failure).
    /// </summary>
    public static Microsoft.AspNetCore.Http.IResult ToHttpResponse(
        this Result result,
        Action<HttpResponseOptionsBuilder>? configure = null)
    {
        var builder = new HttpResponseOptionsBuilder();
        configure?.Invoke(builder);
        var opts = builder.Build();

        if (result.IsSuccess)
            return new TrellisEmptyResult(opts);

        return new TrellisErrorOnlyResult(result.Error!, opts);
    }

    /// <summary>Async <see cref="Task"/> overload.</summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync(
        this Task<Result> resultTask,
        Action<HttpResponseOptionsBuilder>? configure = null)
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(configure);

    /// <summary>Async <see cref="ValueTask"/> overload.</summary>
    public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync(
        this ValueTask<Result> resultTask,
        Action<HttpResponseOptionsBuilder>? configure = null)
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(configure);

    #endregion

    #region Error

    /// <summary>
    /// Maps a standalone <see cref="Error"/> to a Problem Details HTTP response.
    /// Useful for endpoints that produce a deterministic error without a <see cref="Result"/>
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
    /// <see cref="HttpResponseOptionsBuilder{T}.CreatedAtRoute"/>, returns 201 Created with a
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
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(configure);

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
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(body, configure);

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
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(configure);

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
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(body, configure);

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

        if (!result.TryGetValue(out var page))
        {
            var sc = TrellisHttpResult<Page<T>, Page<T>>.ResolveErrorStatusCode(result.Error!, opts);
            return new TrellisErrorOnlyResult(result.Error!, new HttpResponseOptions<object>
            {
                ErrorMapper = opts.ErrorMapper,
                ErrorOverrides = opts.ErrorOverrides,
            });
        }

        var (envelope, linkHeader) = PagedResponseBuilder.Build(page, nextUrlBuilder, body);
        var ok = Results.Ok(envelope);
        return linkHeader is null ? ok : new PagedHttpResult(ok, linkHeader);
    }

    /// <summary>Async <see cref="Task"/> overload.</summary>
    public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResponseAsync<T, TBody>(
        this Task<Result<Page<T>>> resultTask,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TBody> body,
        Action<HttpResponseOptionsBuilder<Page<T>>>? configure = null)
        => (await resultTask.ConfigureAwait(false)).ToHttpResponse(nextUrlBuilder, body, configure);

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
        var statusCode = ResolveStatusCode(_error);
        return ResponseFailureWriter.WriteAsync(httpContext, _error, statusCode);
    }

    private int ResolveStatusCode(Error error)
    {
        if (_options.ErrorMapper is not null)
            return _options.ErrorMapper(error);

        if (_options.ErrorOverrides is { Count: > 0 })
        {
            var t = error.GetType();
            while (t is not null && t != typeof(object))
            {
                if (_options.ErrorOverrides.TryGetValue(t, out var sc))
                    return sc;

                t = t.BaseType;
            }
        }

        return TrellisAspOptions.Default.GetStatusCode(error);
    }
}

/// <summary>Internal IResult that writes 204 No Content for a successful non-generic Result.</summary>
internal sealed class TrellisEmptyResult : Microsoft.AspNetCore.Http.IResult
{
    private readonly HttpResponseOptions<object> _options;

    public TrellisEmptyResult(HttpResponseOptions<object> options) => _options = options;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        if (_options.HonorPrefer)
            TrellisHttpResult<object, object>.AppendVaryUnique(httpContext.Response, "Prefer");

        if (_options.Vary is { Count: > 0 })
        {
            foreach (var v in _options.Vary)
                TrellisHttpResult<object, object>.AppendVaryUnique(httpContext.Response, v);
        }

        return Microsoft.AspNetCore.Http.Results.NoContent().ExecuteAsync(httpContext);
    }
}
