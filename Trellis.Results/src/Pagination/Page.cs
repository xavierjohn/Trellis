namespace Trellis;

using System;
using System.Collections.Generic;

/// <summary>
/// A single page of items from a paginated collection together with the cursors needed to
/// fetch adjacent pages. The canonical Trellis primitive for server-driven pagination.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// <para>
/// <b>Wire shape:</b> Trellis projects <see cref="Page{T}"/> to <c>200 OK</c> with a JSON
/// body envelope and a co-emitted <c>Link</c> header (RFC 8288). See
/// <c>Trellis.Asp.PageHttpResultExtensions.ToPagedHttpResult</c>.
/// </para>
/// <para>
/// <b>Why not 206 Partial Content?</b> RFC 9110 §14 was designed for byte-range transfer
/// of a single octet stream; collection pagination has no IANA-registered range unit and
/// no proxy/CDN ecosystem support. Use <see cref="Page{T}"/> for collections; reserve
/// <c>206</c> for actual byte-range GETs.
/// </para>
/// <para>
/// <b>Cap visibility:</b> <see cref="RequestedLimit"/> records what the client asked for and
/// <see cref="AppliedLimit"/> records what the server actually used. <see cref="WasCapped"/>
/// makes server-side clamping observable without the client having to compare counts.
/// </para>
/// </remarks>
public readonly record struct Page<T>(
    IReadOnlyList<T> Items,
    Cursor? Next,
    Cursor? Previous,
    int RequestedLimit,
    int AppliedLimit)
{
    /// <summary>The number of items actually returned in this page (always equal to <c>Items.Count</c>).</summary>
    public int DeliveredCount => Items.Count;

    /// <summary>True when the server applied a smaller limit than the client requested.</summary>
    public bool WasCapped => AppliedLimit < RequestedLimit;
}

/// <summary>
/// Non-generic factory companion for <see cref="Page{T}"/>. Mirrors the <c>Result</c> /
/// <c>Result&lt;T&gt;</c> split: factory methods live on the non-generic type to keep
/// generic-type surface minimal (CA1000) and to allow type inference at the call site.
/// </summary>
public static class Page
{
    /// <summary>An empty page (no items, no cursors) for the supplied limits.</summary>
    public static Page<T> Empty<T>(int requestedLimit, int appliedLimit) =>
        new(Array.Empty<T>(), null, null, requestedLimit, appliedLimit);
}
