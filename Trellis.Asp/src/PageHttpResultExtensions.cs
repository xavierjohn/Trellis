namespace Trellis.Asp;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Maps a <see cref="Result{T}"/> of <see cref="Page{T}"/> to an HTTP response that follows
/// the Trellis pagination contract:
/// <list type="bullet">
///   <item><c>200 OK</c> with a <see cref="PagedResponse{TResponse}"/> JSON envelope (items, next/prev cursor + href, requestedLimit, appliedLimit, deliveredCount, wasCapped).</item>
///   <item>Co-emitted <c>Link</c> header per RFC 8288 with <c>rel="next"</c> and/or <c>rel="prev"</c>.</item>
///   <item>Failure results are delegated to the standard error mapper (<see cref="HttpResultExtensions.ToHttpResult{TValue}(Result{TValue}, TrellisAspOptions)"/>) — no Link header emitted.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an envelope AND a Link header?</b> The body envelope is AI/LLM-friendly (the cursor
/// sits in the JSON the consumer already parsed) and matches OData / Microsoft Graph idioms.
/// The Link header satisfies RFC-8288-aware tooling (HTTP middleware, crawlers, gateways) at
/// near-zero cost. The two carry the same information.
/// </para>
/// <para>
/// <b>Url builder responsibility:</b> the caller supplies a <c>nextUrlBuilder</c> that converts
/// a <see cref="Cursor"/> + the applied limit into a fully-formed URL. The extension does NOT
/// inspect the current request, build query strings, or URL-encode the cursor — the caller
/// owns route construction.
/// </para>
/// </remarks>
[Obsolete("Use Result<Page<T>>.ToHttpResponse(nextUrlBuilder, body, opts) instead. See migration guide at docs/articles/asp-tohttpresponse.md.")]
public static class PageHttpResultExtensions
{
    /// <summary>
    /// Maps <c>Result&lt;Page&lt;T&gt;&gt;</c> to <c>200 OK</c> + envelope + <c>Link</c> header,
    /// or to the standard error response on failure.
    /// </summary>
    /// <typeparam name="T">Domain item type carried by the page.</typeparam>
    /// <typeparam name="TResponse">Wire DTO type to project items into.</typeparam>
    /// <param name="result">The result containing a <see cref="Page{T}"/> on success.</param>
    /// <param name="nextUrlBuilder">Builds the absolute URL for a cursor link given the cursor and the applied limit.</param>
    /// <param name="map">Projection from domain item to wire DTO.</param>
    /// <param name="options">Optional error-to-status mapping (defaults applied otherwise).</param>
    public static IResult ToPagedHttpResult<T, TResponse>(
        this Result<Page<T>> result,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TResponse> map,
        TrellisAspOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(nextUrlBuilder);
        ArgumentNullException.ThrowIfNull(map);

        if (result.TryGetError(out var error))
            return error.ToHttpResult(options);

        result.TryGetValue(out var page);
        return BuildPagedResult(page, nextUrlBuilder, map);
    }

    /// <summary>Async <see cref="Task{TResult}"/> overload of <see cref="ToPagedHttpResult{T, TResponse}"/>.</summary>
    public static async Task<IResult> ToPagedHttpResultAsync<T, TResponse>(
        this Task<Result<Page<T>>> resultTask,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TResponse> map,
        TrellisAspOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        var result = await resultTask.ConfigureAwait(false);
        return result.ToPagedHttpResult(nextUrlBuilder, map, options);
    }

    /// <summary>Async <see cref="ValueTask{TResult}"/> overload of <see cref="ToPagedHttpResult{T, TResponse}"/>.</summary>
    public static async ValueTask<IResult> ToPagedHttpResultAsync<T, TResponse>(
        this ValueTask<Result<Page<T>>> resultTask,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TResponse> map,
        TrellisAspOptions? options = null)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToPagedHttpResult(nextUrlBuilder, map, options);
    }

    private static IResult BuildPagedResult<T, TResponse>(
        Page<T> page,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TResponse> map)
    {
        var (envelope, linkHeader) = PagedResponseBuilder.Build(page, nextUrlBuilder, map);
        var ok = Results.Ok(envelope);
        return linkHeader is null ? ok : new PagedHttpResult(ok, linkHeader);
    }
}

/// <summary>JSON envelope wrapping a single page of items and its cursor links.</summary>
public sealed record PagedResponse<TResponse>(
    IReadOnlyList<TResponse> Items,
    PageLink? Next,
    PageLink? Previous,
    int RequestedLimit,
    int AppliedLimit,
    int DeliveredCount,
    bool WasCapped);

/// <summary>A cursor + the absolute URL the client should follow to fetch the linked page.</summary>
public sealed record PageLink(string Cursor, string Href);

/// <summary>
/// <see cref="IResult"/> wrapper that delegates to an inner result and also emits an
/// RFC 8288 <c>Link</c> header containing pre-formatted <c>rel="next"</c> / <c>rel="prev"</c> entries.
/// </summary>
internal sealed class PagedHttpResult : IResult
{
    private readonly IResult _inner;
    private readonly string _linkHeader;

    public PagedHttpResult(IResult inner, string linkHeader)
    {
        _inner = inner;
        _linkHeader = linkHeader;
    }

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.Headers.Append("Link", _linkHeader);
        return _inner.ExecuteAsync(httpContext);
    }
}
