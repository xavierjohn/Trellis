namespace Trellis.Asp;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Internal helper that builds the <see cref="PagedResponse{TResponse}"/> envelope and the
/// matching RFC 8288 <c>Link</c> header value from a <see cref="Page{T}"/>. Used by both
/// <see cref="PageHttpResultExtensions"/> (Minimal API) and <see cref="PageActionResultExtensions"/>
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
        var linkHeader = links.Count > 0 ? string.Join(", ", links) : null;

        return (envelope, linkHeader);
    }
}
