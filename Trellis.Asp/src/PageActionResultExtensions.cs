namespace Trellis.Asp;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// MVC counterpart of <see cref="PageHttpResultExtensions"/>. Maps
/// <see cref="Result{T}"/> of <see cref="Page{T}"/> to an <see cref="ActionResult{TValue}"/>
/// of <see cref="PagedResponse{TResponse}"/>, co-emitting an RFC 8288 <c>Link</c>
/// header on the response.
/// </summary>
/// <remarks>
/// Intentionally mirrors the Minimal-API overload surface so that MVC controllers and
/// Minimal-API handlers produce byte-identical HTTP responses for the same domain result.
/// Failure results delegate to <see cref="ActionResultExtensions.ToActionResult{TValue}(Error, ControllerBase)"/>;
/// no <c>Link</c> header is emitted on the failure path.
/// </remarks>
public static class PageActionResultExtensions
{
    /// <summary>
    /// Maps <c>Result&lt;Page&lt;T&gt;&gt;</c> to <c>200 OK</c> + paged envelope + <c>Link</c> header,
    /// or to the standard error response on failure.
    /// </summary>
    /// <typeparam name="T">Domain item type carried by the page.</typeparam>
    /// <typeparam name="TResponse">Wire DTO type to project items into.</typeparam>
    /// <param name="result">The result containing a <see cref="Page{T}"/> on success.</param>
    /// <param name="controller">The MVC controller — supplies HttpContext for header emission and error mapping.</param>
    /// <param name="nextUrlBuilder">Builds the absolute URL for a cursor link given the cursor and the applied limit.</param>
    /// <param name="map">Projection from domain item to wire DTO.</param>
    public static ActionResult<PagedResponse<TResponse>> ToPagedActionResult<T, TResponse>(
        this Result<Page<T>> result,
        ControllerBase controller,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(nextUrlBuilder);
        ArgumentNullException.ThrowIfNull(map);

        if (result.TryGetError(out var error))
            return error.ToActionResult<PagedResponse<TResponse>>(controller);

        result.TryGetValue(out var page);

        string? nextHref = page.Next is { } next ? nextUrlBuilder(next, page.AppliedLimit) : null;
        string? prevHref = page.Previous is { } prev ? nextUrlBuilder(prev, page.AppliedLimit) : null;

        var envelope = new PagedResponse<TResponse>(
            Items: page.Items.Select(map).ToList(),
            Next: page.Next is { } n && nextHref is not null ? new PageLink(n.Token, nextHref) : null,
            Previous: page.Previous is { } p && prevHref is not null ? new PageLink(p.Token, prevHref) : null,
            RequestedLimit: page.RequestedLimit,
            AppliedLimit: page.AppliedLimit,
            DeliveredCount: page.DeliveredCount,
            WasCapped: page.WasCapped);

        var links = new List<string>(2);
        if (nextHref is not null) links.Add($"<{nextHref}>; rel=\"next\"");
        if (prevHref is not null) links.Add($"<{prevHref}>; rel=\"prev\"");
        if (links.Count > 0)
            controller.Response.Headers.Append("Link", string.Join(", ", links));

        return controller.Ok(envelope);
    }

    /// <summary>Async <see cref="Task{TResult}"/> overload of <see cref="ToPagedActionResult{T, TResponse}"/>.</summary>
    public static async Task<ActionResult<PagedResponse<TResponse>>> ToPagedActionResultAsync<T, TResponse>(
        this Task<Result<Page<T>>> resultTask,
        ControllerBase controller,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(resultTask);
        var result = await resultTask.ConfigureAwait(false);
        return result.ToPagedActionResult(controller, nextUrlBuilder, map);
    }

    /// <summary>Async <see cref="ValueTask{TResult}"/> overload of <see cref="ToPagedActionResult{T, TResponse}"/>.</summary>
    public static async ValueTask<ActionResult<PagedResponse<TResponse>>> ToPagedActionResultAsync<T, TResponse>(
        this ValueTask<Result<Page<T>>> resultTask,
        ControllerBase controller,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TResponse> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.ToPagedActionResult(controller, nextUrlBuilder, map);
    }
}
