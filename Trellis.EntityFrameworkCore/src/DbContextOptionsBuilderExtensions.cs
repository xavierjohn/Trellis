namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// Extension methods for <see cref="DbContextOptionsBuilder"/> that register Trellis EF Core interceptors.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    private static readonly MaybeQueryInterceptor s_maybeQueryInterceptor = new();
    private static readonly ScalarValueQueryInterceptor s_scalarValueQueryInterceptor = new();
    private static readonly AggregateVersionInterceptor s_aggregateVersionInterceptor = new();

    /// <summary>
    /// Adds Trellis EF Core interceptors to the <see cref="DbContextOptionsBuilder"/>.
    /// Registers the <see cref="MaybeQueryInterceptor"/>, <see cref="ScalarValueQueryInterceptor"/>,
    /// and <see cref="AggregateVersionInterceptor"/> as singletons, enabling natural LINQ syntax
    /// with <see cref="Maybe{T}"/> properties, <c>.Value</c> access on scalar value objects,
    /// and automatic optimistic concurrency version increment on aggregate saves.
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
    /// services.AddDbContext&lt;MyDbContext&gt;(options =>
    ///     options.UseSqlite(connectionString).AddTrellisInterceptors());
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        optionsBuilder.AddInterceptors(s_maybeQueryInterceptor, s_scalarValueQueryInterceptor, s_aggregateVersionInterceptor);
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
        optionsBuilder.AddInterceptors(s_maybeQueryInterceptor, s_scalarValueQueryInterceptor, s_aggregateVersionInterceptor);
        return optionsBuilder;
    }
}