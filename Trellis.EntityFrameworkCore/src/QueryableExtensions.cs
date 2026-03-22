namespace Trellis.EntityFrameworkCore;

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods on <see cref="IQueryable{T}"/> that wrap EF Core query results
/// in <see cref="Maybe{T}"/> or <see cref="Result{T}"/>.
/// </summary>
public static class QueryableExtensions
{
    // ──────────────────────────────────────────────
    // Maybe<T> — for queries where absence is neutral
    // ──────────────────────────────────────────────

    /// <summary>
    /// Executes <see cref="EntityFrameworkQueryableExtensions.FirstOrDefaultAsync{TSource}(IQueryable{TSource}, CancellationToken)"/>
    /// and wraps the result in <see cref="Maybe{T}"/>.
    /// Returns <see cref="Maybe{T}.None"/> if no entity matches.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The queryable to execute.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Maybe{T}"/> containing the entity, or None if not found.</returns>
    public static async Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(
        this IQueryable<T> query,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var entity = await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return Maybe.From(entity);
    }

    /// <summary>
    /// Executes <see cref="EntityFrameworkQueryableExtensions.FirstOrDefaultAsync{TSource}(IQueryable{TSource}, Expression{Func{TSource, bool}}, CancellationToken)"/>
    /// with a predicate and wraps the result in <see cref="Maybe{T}"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The queryable to execute.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Maybe{T}"/> containing the entity, or None if not found.</returns>
    public static async Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(
        this IQueryable<T> query,
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var entity = await query.FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);
        return Maybe.From(entity);
    }

    /// <summary>
    /// Executes <see cref="EntityFrameworkQueryableExtensions.SingleOrDefaultAsync{TSource}(IQueryable{TSource}, CancellationToken)"/>
    /// and wraps the result in <see cref="Maybe{T}"/>.
    /// Throws <see cref="InvalidOperationException"/> if more than one element matches
    /// (this is a programming error, not an expected failure).
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The queryable to execute.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Maybe{T}"/> containing the entity, or None if not found.</returns>
    public static async Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(
        this IQueryable<T> query,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var entity = await query.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return Maybe.From(entity);
    }

    /// <summary>
    /// Executes <see cref="EntityFrameworkQueryableExtensions.SingleOrDefaultAsync{TSource}(IQueryable{TSource}, Expression{Func{TSource, bool}}, CancellationToken)"/>
    /// with a predicate and wraps in <see cref="Maybe{T}"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The queryable to execute.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Maybe{T}"/> containing the entity, or None if not found.</returns>
    public static async Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(
        this IQueryable<T> query,
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var entity = await query.SingleOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);
        return Maybe.From(entity);
    }

    // ──────────────────────────────────────────────
    // Result<T> — for queries where "not found" IS the error
    // ──────────────────────────────────────────────

    /// <summary>
    /// Executes <see cref="EntityFrameworkQueryableExtensions.FirstOrDefaultAsync{TSource}(IQueryable{TSource}, CancellationToken)"/>.
    /// Returns <see cref="Result.Success{TValue}(TValue)"/> if found,
    /// or the provided error if no entity matches.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The queryable to execute.</param>
    /// <param name="notFoundError">The error to return if no entity is found.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{T}"/> containing the entity on success, or the error on failure.</returns>
    public static async Task<Result<T>> FirstOrDefaultResultAsync<T>(
        this IQueryable<T> query,
        Error notFoundError,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var entity = await query.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return entity is not null
            ? Result.Success(entity)
            : Result.Failure<T>(notFoundError);
    }

    /// <summary>
    /// Executes <see cref="EntityFrameworkQueryableExtensions.FirstOrDefaultAsync{TSource}(IQueryable{TSource}, Expression{Func{TSource, bool}}, CancellationToken)"/>
    /// with a predicate. Returns <see cref="Result.Success{TValue}(TValue)"/> if found,
    /// or the provided error if no entity matches.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The queryable to execute.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <param name="notFoundError">The error to return if no entity is found.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A <see cref="Result{T}"/> containing the entity on success, or the error on failure.</returns>
    public static async Task<Result<T>> FirstOrDefaultResultAsync<T>(
        this IQueryable<T> query,
        Expression<Func<T, bool>> predicate,
        Error notFoundError,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var entity = await query.FirstOrDefaultAsync(predicate, cancellationToken).ConfigureAwait(false);
        return entity is not null
            ? Result.Success(entity)
            : Result.Failure<T>(notFoundError);
    }

    // ──────────────────────────────────────────────
    // Specification<T> integration
    // ──────────────────────────────────────────────

    /// <summary>
    /// Applies a <see cref="Specification{T}"/> as a Where clause on the queryable.
    /// The specification's expression tree is passed directly to the LINQ provider
    /// (EF Core, Cosmos DB, etc.) for server-side evaluation.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The queryable to filter.</param>
    /// <param name="specification">The specification to apply.</param>
    /// <returns>A filtered <see cref="IQueryable{T}"/>.</returns>
    public static IQueryable<T> Where<T>(
        this IQueryable<T> query,
        Specification<T> specification)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(specification);
        return query.Where(specification.ToExpression());
    }
}