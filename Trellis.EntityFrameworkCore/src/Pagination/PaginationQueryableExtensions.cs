namespace Trellis.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF-Core-specific seek-pagination helpers that compose with the storage-agnostic
/// <see cref="PageBuilder"/> + <see cref="CursorCodec"/> primitives in <c>Trellis.Core</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Forward-only single-key seek.</b> <see cref="ToPageAsync{T, TKey}"/> owns the
/// <c>OrderBy</c>, cursor decoding, seek predicate (<c>WHERE key &gt; @after</c>), the
/// <c>Take(Applied + 1)</c> over-fetch, and the slice via <see cref="PageBuilder"/>.
/// Callers supply a pre-filtered <see cref="IQueryable{T}"/> and a <c>keySelector</c>
/// expression returning the sort key. A composite <c>(CreatedAt, Id)</c> overload is
/// deferred to a follow-up release.
/// </para>
/// <para>
/// <b>Key uniqueness.</b> Single-key seek requires a stable, unique ascending key
/// (a primary key or a unique surrogate). With a non-unique key, rows that share the
/// boundary value with the last item on the previous page are silently skipped on the
/// next page. Use a primary-key surrogate (or the upcoming composite overload) when
/// the natural sort key is not unique.
/// </para>
/// <para>
/// <b>Value-object projections.</b> When the entity uses a Trellis scalar value object
/// for its identity (e.g. <c>CustomerId : RequiredGuid&lt;CustomerId&gt;</c>), supply
/// the underlying primitive via projection (<c>c =&gt; c.Id.Value</c>) and ensure the
/// <see cref="DbContext"/> registers <see cref="DbContextOptionsBuilderExtensions.AddTrellisInterceptors(DbContextOptionsBuilder)"/>
/// so the <c>ScalarValueQueryInterceptor</c> rewrites the projection for EF translation.
/// </para>
/// <para>
/// <b>Provider notes.</b> EF Core translates <c>WHERE key &gt; @after</c> directly for
/// numeric and <see cref="DateTime"/>/<see cref="DateTimeOffset"/> keys. For
/// <see cref="Guid"/> and <see cref="string"/> keys the helper emits the
/// <see cref="IComparable{T}.CompareTo(T)"/> form, which translates on SQL Server but
/// requires provider support — verify against your provider before relying on it.
/// SQLite has a known trap for <see cref="DateTimeOffset"/> stored as <c>TEXT</c>; see
/// the framework's "Provider-specific column mapping" section.
/// </para>
/// </remarks>
public static class PaginationQueryableExtensions
{
    /// <summary>
    /// Materializes a page of <typeparamref name="T"/> from a pre-filtered
    /// <see cref="IQueryable{T}"/> using forward-only seek pagination on a single key.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TKey">
    /// The sort-key type. Must implement <see cref="IComparable{T}"/> for the seek
    /// predicate and <see cref="IParsable{TSelf}"/> for cursor decoding.
    /// </typeparam>
    /// <param name="source">The pre-filtered queryable. The method applies <see cref="Queryable.OrderBy{TSource, TKey}(IQueryable{TSource}, Expression{Func{TSource, TKey}})"/> internally.</param>
    /// <param name="pageSize">The validated page-size pair. <c>default(PageSize)</c> is rejected up-front.</param>
    /// <param name="cursor">An opaque cursor from a previous page's <c>Next</c>; <c>null</c> requests the first page.</param>
    /// <param name="keySelector">The sort-key projection. Used both as the <c>OrderBy</c> key and as the cursor-key extractor.</param>
    /// <param name="cursorFieldName">Optional field name used on malformed-cursor errors. Defaults to <c>"cursor"</c>.</param>
    /// <param name="cancellationToken">Propagated to the underlying <see cref="EntityFrameworkQueryableExtensions.ToListAsync{TSource}(IQueryable{TSource}, CancellationToken)"/> call.</param>
    /// <returns>
    /// <see cref="Result{T}"/> wrapping a <see cref="Page{T}"/> on success, or
    /// <see cref="Error.InvalidInput"/> with <c>reasonCode == "cursor.malformed"</c>
    /// when the supplied cursor cannot be decoded as <typeparamref name="TKey"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="keySelector"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pageSize"/> is not a validated instance (e.g. <c>default(PageSize)</c>).</exception>
    public static async Task<Result<Page<T>>> ToPageAsync<T, TKey>(
        this IQueryable<T> source,
        PageSize pageSize,
        Cursor? cursor,
        Expression<Func<T, TKey>> keySelector,
        string? cursorFieldName = null,
        CancellationToken cancellationToken = default)
        where T : class
        where TKey : notnull, IComparable<TKey>, IParsable<TKey>
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);
        EnsureValidPageSize(pageSize);

        IQueryable<T> ordered = source.OrderBy(keySelector);

        if (cursor is { } c)
        {
            var decoded = CursorCodec.TryDecode<TKey>(c, cursorFieldName);
            if (!decoded.TryGetValue(out var afterKey, out var error))
                return Result.Fail<Page<T>>(error);

            ordered = ordered.Where(BuildSeekPredicate(keySelector, afterKey));
        }

        var rows = await ordered
            .Take(pageSize.Applied + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Ok(PageBuilder.FromOverFetch(rows, pageSize, keySelector.Compile()));
    }

    private static void EnsureValidPageSize(PageSize pageSize)
    {
        if (pageSize.Requested <= 0 || pageSize.Applied <= 0 || pageSize.Applied > pageSize.Requested)
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                "PageSize must be a validated instance with Requested > 0, Applied > 0, and Applied <= Requested.");
    }

    private static Expression<Func<T, bool>> BuildSeekPredicate<T, TKey>(
        Expression<Func<T, TKey>> keySelector, TKey afterKey)
        where TKey : notnull, IComparable<TKey>
    {
        var parameter = keySelector.Parameters[0];

        // Wrap the captured key in a holder + member access so EF parameterizes the value
        // (mimics the C# compiler-generated DisplayClass shape).
        var holder = new CursorKeyHolder<TKey> { Value = afterKey };
        Expression keyExpr = Expression.Property(
            Expression.Constant(holder),
            nameof(CursorKeyHolder<TKey>.Value));

        Expression body = HasGreaterThanOperator(typeof(TKey))
            ? Expression.GreaterThan(keySelector.Body, keyExpr)
            : BuildCompareToPredicate(keySelector.Body, keyExpr);

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private static BinaryExpression BuildCompareToPredicate(Expression keyBody, Expression afterKeyExpr)
    {
        // Types like Guid and string don't have a C# `>` operator, so we route through
        // IComparable<TKey>.CompareTo, which EF translates to provider-native ordering.
        var keyType = keyBody.Type;
        var compareTo = keyType.GetMethod(
            nameof(IComparable<int>.CompareTo),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { keyType },
            modifiers: null)
            ?? throw new InvalidOperationException(
                $"Type {keyType.FullName} declared IComparable<{keyType.Name}> but does not expose CompareTo({keyType.Name}).");

        var compareCall = Expression.Call(keyBody, compareTo, afterKeyExpr);
        return Expression.GreaterThan(compareCall, Expression.Constant(0));
    }

    private static bool HasGreaterThanOperator(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double)
        || t == typeof(short) || t == typeof(float) || t == typeof(byte)
        || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(sbyte)
        || t == typeof(DateTime) || t == typeof(DateTimeOffset)
        || t == typeof(TimeSpan) || t == typeof(DateOnly) || t == typeof(TimeOnly);

    // Boxed holder so the cursor key is parameterized by EF rather than baked into the SQL.
    private sealed class CursorKeyHolder<TKey>
    {
        public TKey Value { get; init; } = default!;
    }
}