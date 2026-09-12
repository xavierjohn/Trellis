namespace Trellis;

using System.Collections.Immutable;

/// <summary>Storage-neutral assembly of forward pages from an ordered, sought, over-fetched batch.</summary>
public static class PageBuilder
{
    /// <summary>
    /// Keeps at most the applied limit and obtains the next cursor from the last retained row,
    /// exactly once and only when more rows exist. The caller owns filtering, seeking and ordering.
    /// Use the <see cref="Page{T}"/> constructor instead for provider-supplied continuation tokens.
    /// </summary>
    public static Page<T> FromOverFetch<T>(
        IReadOnlyList<T> overFetched,
        PageSize pageSize,
        Func<T, Cursor> cursorSelector)
    {
        ArgumentNullException.ThrowIfNull(overFetched);
        ArgumentNullException.ThrowIfNull(pageSize);
        ArgumentNullException.ThrowIfNull(cursorSelector);

        var count = Math.Min(overFetched.Count, pageSize.Applied);
        var builder = ImmutableArray.CreateBuilder<T>(count);
        for (var i = 0; i < count; i++)
            builder.Add(overFetched[i]);
        var kept = builder.MoveToImmutable();
        Cursor? next = null;
        if (overFetched.Count > count)
            next = cursorSelector(kept[count - 1])
                ?? throw new InvalidOperationException("The cursor selector returned null for a continuation boundary.");
        return new Page<T>(kept, next, null, pageSize.Requested, pageSize.Applied);
    }
}
