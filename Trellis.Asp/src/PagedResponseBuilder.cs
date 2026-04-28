namespace Trellis.Asp;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Internal helper that builds the <see cref="PagedResponse{TResponse}"/> envelope and the
/// matching RFC 8288 <c>Link</c> header value from a <see cref="Page{T}"/>. Used by the
/// <c>ToHttpResponse&lt;T,TBody&gt;</c> paginated overload to guarantee consistent wire output
/// (MVC) to guarantee byte-identical wire output across hosting styles.
/// </summary>
internal static class PagedResponseBuilder
{
    /// <summary>
    /// Projects <paramref name="page"/> into a JSON envelope and the <c>Link</c> header value.
    /// </summary>
    /// <param name="page">The source page.</param>
    /// <param name="nextUrlBuilder">Builds an absolute URL from a cursor and the applied limit.</param>
    /// <param name="map">Projection from domain item to wire DTO.</param>
    /// <returns>The envelope and the <c>Link</c> header value (null when neither cursor is present).</returns>
    public static (PagedResponse<TResponse> Envelope, string? LinkHeader) Build<T, TResponse>(
        Page<T> page,
        Func<Cursor, int, string> nextUrlBuilder,
        Func<T, TResponse> map)
    {
        string? nextHref = page.Next is { } next ? nextUrlBuilder(next, page.AppliedLimit) : null;
        string? prevHref = page.Previous is { } prev ? nextUrlBuilder(prev, page.AppliedLimit) : null;

        var items = page.Items ?? (IReadOnlyList<T>)Array.Empty<T>();

        var envelope = new PagedResponse<TResponse>(
            Items: items.Select(map).ToList(),
            Next: page.Next is { } n && nextHref is not null ? new PageLink(n.Token, nextHref) : null,
            Previous: page.Previous is { } p && prevHref is not null ? new PageLink(p.Token, prevHref) : null,
            RequestedLimit: page.RequestedLimit,
            AppliedLimit: page.AppliedLimit,
            DeliveredCount: page.DeliveredCount,
            WasCapped: page.WasCapped);

        var links = new List<string>(2);
        if (nextHref is not null) links.Add($"<{nextHref}>; rel=\"next\"");
        if (prevHref is not null) links.Add($"<{prevHref}>; rel=\"prev\"");
        var linkHeader = links.Count > 0 ? string.Join(", ", links) : null;

        return (envelope, linkHeader);
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
/// <see cref="Microsoft.AspNetCore.Http.IResult"/> wrapper that delegates to an inner result and also emits an
/// RFC 8288 <c>Link</c> header containing pre-formatted <c>rel="next"</c> / <c>rel="prev"</c> entries.
/// </summary>
internal sealed class PagedHttpResult : Microsoft.AspNetCore.Http.IResult
{
    private readonly Microsoft.AspNetCore.Http.IResult _inner;
    private readonly string _linkHeader;

    public PagedHttpResult(Microsoft.AspNetCore.Http.IResult inner, string linkHeader)
    {
        _inner = inner;
        _linkHeader = linkHeader;
    }

    public System.Threading.Tasks.Task ExecuteAsync(Microsoft.AspNetCore.Http.HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.Headers.Append("Link", _linkHeader);
        return _inner.ExecuteAsync(httpContext);
    }
}