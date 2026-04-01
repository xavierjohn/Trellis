namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for <see cref="DbContextOptionsBuilder"/> that register Trellis EF Core interceptors.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    private static readonly MaybeQueryInterceptor s_maybeQueryInterceptor = new();
    private static readonly ScalarValueQueryInterceptor s_scalarValueQueryInterceptor = new();
    private static readonly AggregateETagInterceptor s_aggregateETagInterceptor = new();
    private static readonly LastModifiedInterceptor s_lastModifiedInterceptor = new();

    /// <summary>
    /// Adds Trellis EF Core interceptors to the <see cref="DbContextOptionsBuilder"/>.
    /// Registers the <see cref="MaybeQueryInterceptor"/>, <see cref="ScalarValueQueryInterceptor"/>,
    /// <see cref="AggregateETagInterceptor"/>, and <see cref="LastModifiedInterceptor"/> as singletons,
    /// enabling natural LINQ syntax with <see cref="Maybe{T}"/> properties, <c>.Value</c> access on
    /// scalar value objects, automatic optimistic concurrency ETag generation on aggregate saves,
    /// and automatic <see cref="ITrackLastModified.LastModified"/> timestamps.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// Uses a static singleton interceptor instance to avoid EF Core's
    /// <c>ManyServiceProvidersCreatedWarning</c> when multiple DbContext instances are created
    /// (common in integration tests).
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;MyDbContext&gt;(options =&gt;
    ///     options.UseSqlite(connectionString).AddTrellisInterceptors());
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        optionsBuilder.AddInterceptors(s_maybeQueryInterceptor, s_scalarValueQueryInterceptor, s_aggregateETagInterceptor, s_lastModifiedInterceptor);
        return optionsBuilder;
    }

    /// <summary>
    /// Adds Trellis EF Core interceptors to the <see cref="DbContextOptionsBuilder"/>.
    /// Non-generic overload for use with <c>DbContextOptionsBuilder</c> directly.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static DbContextOptionsBuilder AddTrellisInterceptors(
        this DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(s_maybeQueryInterceptor, s_scalarValueQueryInterceptor, s_aggregateETagInterceptor, s_lastModifiedInterceptor);
        return optionsBuilder;
    }

    /// <summary>
    /// Adds Trellis EF Core interceptors to the <see cref="DbContextOptionsBuilder"/> with a custom <see cref="TimeProvider"/>.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="timeProvider">
    /// The time provider to use for <see cref="LastModifiedInterceptor"/> timestamps.
    /// Defaults to <see cref="TimeProvider.System"/> if <c>null</c>.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public static DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder, TimeProvider? timeProvider)
        where TContext : DbContext
    {
        optionsBuilder.AddInterceptors(s_maybeQueryInterceptor, s_scalarValueQueryInterceptor, s_aggregateETagInterceptor, new LastModifiedInterceptor(timeProvider));
        return optionsBuilder;
    }

    /// <summary>
    /// Adds Trellis EF Core interceptors to the <see cref="DbContextOptionsBuilder"/> with a custom <see cref="TimeProvider"/>.
    /// Non-generic overload for use with <c>DbContextOptionsBuilder</c> directly.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="timeProvider">
    /// The time provider to use for <see cref="LastModifiedInterceptor"/> timestamps.
    /// Defaults to <see cref="TimeProvider.System"/> if <c>null</c>.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    public static DbContextOptionsBuilder AddTrellisInterceptors(
        this DbContextOptionsBuilder optionsBuilder, TimeProvider? timeProvider)
    {
        optionsBuilder.AddInterceptors(s_maybeQueryInterceptor, s_scalarValueQueryInterceptor, s_aggregateETagInterceptor, new LastModifiedInterceptor(timeProvider));
        return optionsBuilder;
    }
}