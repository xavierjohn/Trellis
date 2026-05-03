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
/// <c>Trellis.Asp.HttpResponseExtensions.ToHttpResponse</c> for the <c>Result&lt;Page&lt;T&gt;&gt;</c> overload.
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
public readonly record struct Page<T>
{
    private readonly EquatableArray<T> _items;

    /// <summary>Constructs a validated page. Use <see cref="Page.Empty{T}(int, int)"/> for empty pages.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="Items"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit is non-positive, or <paramref name="AppliedLimit"/> exceeds <paramref name="RequestedLimit"/>.</exception>
    public Page(
        IReadOnlyList<T> Items,
        Cursor? Next,
        Cursor? Previous,
        int RequestedLimit,
        int AppliedLimit)
    {
        ArgumentNullException.ThrowIfNull(Items);
        if (RequestedLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(RequestedLimit), "Limit must be positive.");
        if (AppliedLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(AppliedLimit), "Limit must be positive.");
        if (AppliedLimit > RequestedLimit)
            throw new ArgumentOutOfRangeException(nameof(AppliedLimit), "AppliedLimit cannot exceed RequestedLimit.");

        _items = EquatableArray.From(Items);
        this.Next = Next;
        this.Previous = Previous;
        this.RequestedLimit = RequestedLimit;
        this.AppliedLimit = AppliedLimit;
    }

    /// <summary>The items returned for this page. Defensive against <c>default(Page&lt;T&gt;)</c>: returns an empty list when the backing storage is uninitialized.</summary>
    /// <remarks>
    /// Mirrors the <see cref="EquatableArray{T}.Items"/> pattern: a <c>default</c>-constructed
    /// <see cref="Page{T}"/> is observably empty rather than throwing on enumeration. Always
    /// construct via the public constructor or <see cref="Page.Empty{T}(int, int)"/>; defaults
    /// are observable but not part of the supported design.
    /// </remarks>
    public IReadOnlyList<T> Items => _items.Items;

    /// <summary>Cursor for the next page, or null when this is the last page.</summary>
    public Cursor? Next { get; }

    /// <summary>Cursor for the previous page, or null when this is the first page (or the source doesn't support reverse).</summary>
    public Cursor? Previous { get; }

    /// <summary>The limit the client requested.</summary>
    public int RequestedLimit { get; }

    /// <summary>The limit the server actually applied (after server-side cap).</summary>
    public int AppliedLimit { get; }

    /// <summary>The number of items actually returned in this page.</summary>
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
