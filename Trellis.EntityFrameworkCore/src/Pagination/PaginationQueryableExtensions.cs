namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Forward-only seek pagination. Ordering must end in a stable unique key.
/// Provider translation failures and cancellation propagate; invalid client cursors return InvalidInput.
/// Value-object projections require the Trellis query interceptors.
/// </summary>
public static class PaginationQueryableExtensions
{
    /// <summary>
    /// Executes a single-key ascending seek using the built-in scalar codec.
    /// Codec support is checked before querying. Use an explicit SeekDefinition for custom codecs or composite keys.
    /// </summary>
    public static Task<Result<Page<T>>> ToPageAsync<T, TKey>(
        this IQueryable<T> source,
        PageSize pageSize,
        Cursor? cursor,
        Expression<Func<T, TKey>> keySelector,
        string? cursorFieldName = null,
        CancellationToken cancellationToken = default)
        where T : class
        where TKey : notnull, IComparable<TKey>, IParsable<TKey> =>
        source.ToPageAsync(pageSize, cursor, SeekDefinition.Ascending(keySelector), cursorFieldName, cancellationToken);

    /// <summary>
    /// Decodes a boundary, applies its matching composite ordering and seek, over-fetches one row, and assembles a page.
    /// This does not create a snapshot or guarantee stability of mutable sort values between requests.
    /// </summary>
    public static async Task<Result<Page<T>>> ToPageAsync<T, TState>(
        this IQueryable<T> source,
        PageSize pageSize,
        Cursor? cursor,
        SeekDefinition<T, TState> seek,
        string? cursorFieldName = null,
        CancellationToken cancellationToken = default)
        where T : class
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(pageSize);
        ArgumentNullException.ThrowIfNull(seek);
        var decoded = CursorCodec.TryDecodeOptional(cursor, seek.Codec, cursorFieldName);
        if (!decoded.TryGetValue(out var boundary, out var error))
            return Result.Fail<Page<T>>(error);
        var rows = await seek.Project(seek.Apply(source, boundary).Take(pageSize.Applied + 1))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return Result.Ok(PageBuilder.FromOverFetch(rows, pageSize, row => seek.Codec.Encode(row.State)).Map(row => row.Item));
    }

    /// <summary>Executes a seek from validated request controls.</summary>
    public static Task<Result<Page<T>>> ToPageAsync<T, TState>(
        this IQueryable<T> source,
        PageRequest request,
        SeekDefinition<T, TState> seek,
        string? cursorFieldName = null,
        CancellationToken cancellationToken = default)
        where T : class
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        return source.ToPageAsync(request.Size, request.Cursor, seek, cursorFieldName, cancellationToken);
    }
}
