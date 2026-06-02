namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Extension methods for <see cref="DbContextOptionsBuilder"/> that register Trellis EF Core interceptors.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    private static readonly MaybeQueryInterceptor s_maybeQueryInterceptor = new();
    private static readonly ScalarValueQueryInterceptor s_scalarValueQueryInterceptor = new();
    private static readonly AggregateETagInterceptor s_aggregateETagInterceptor = new();
    private static readonly EntityTimestampInterceptor s_entityTimestampInterceptor = new();

    /// <summary>
    /// Adds Trellis EF Core interceptors to the <see cref="DbContextOptionsBuilder"/>.
    /// Registers the <see cref="MaybeQueryInterceptor"/>, <see cref="ScalarValueQueryInterceptor"/>,
    /// <see cref="AggregateETagInterceptor"/>, and <see cref="EntityTimestampInterceptor"/> as singletons,
    /// plus the <see cref="MaybeEvaluatableExpressionFilterPlugin"/> required for correct
    /// <c>c.Maybe == Maybe.From(value)</c> translation. Enables natural LINQ syntax with
    /// <see cref="Maybe{T}"/> properties, <c>.Value</c> access on scalar value objects, automatic
    /// optimistic concurrency ETag generation on aggregate saves, and automatic
    /// <see cref="IEntity.CreatedAt"/>/<see cref="IEntity.LastModified"/> timestamps.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// Uses a static singleton interceptor instance to avoid EF Core's
    /// <c>ManyServiceProvidersCreatedWarning</c> when multiple DbContext instances are created
    /// (common in integration tests). This is the canonical registration path for Trellis EF Core
    /// integration — registering only the interceptor via
    /// <c>optionsBuilder.AddInterceptors(new MaybeQueryInterceptor())</c> is insufficient for
    /// <c>Maybe.From(value)</c> equality translation; the
    /// <see cref="MaybeEvaluatableExpressionFilterPlugin"/> must also be installed in the per-context
    /// internal service provider. Repeated calls on the same options builder are idempotent: each
    /// Trellis interceptor is registered at most once, while consumer interceptors registered
    /// separately through <c>AddInterceptors</c> are preserved.
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
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        if (!TryAddTrellisInterceptorsMarkerExtension(optionsBuilder, timeProvider: null))
            return optionsBuilder;

        optionsBuilder.AddInterceptors(s_maybeQueryInterceptor, s_scalarValueQueryInterceptor, s_aggregateETagInterceptor, s_entityTimestampInterceptor);
        AddMaybeEvaluatableExpressionFilterExtension(optionsBuilder);
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
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        if (!TryAddTrellisInterceptorsMarkerExtension(optionsBuilder, timeProvider: null))
            return optionsBuilder;

        optionsBuilder.AddInterceptors(s_maybeQueryInterceptor, s_scalarValueQueryInterceptor, s_aggregateETagInterceptor, s_entityTimestampInterceptor);
        AddMaybeEvaluatableExpressionFilterExtension(optionsBuilder);
        return optionsBuilder;
    }

    /// <summary>
    /// Adds Trellis EF Core interceptors to the <see cref="DbContextOptionsBuilder"/> with a custom <see cref="TimeProvider"/>.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="timeProvider">
    /// The time provider to use for <see cref="EntityTimestampInterceptor"/> timestamps.
    /// Defaults to <see cref="TimeProvider.System"/> if <c>null</c>.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>AddTrellisInterceptors</c> has already been called on this builder with
    /// a different <see cref="TimeProvider"/>. Library + application composition must agree
    /// on the time-provider choice; a silent no-op would let a library's parameterless
    /// registration shadow the application's later custom-clock registration without
    /// diagnostic. Resolve by consolidating to a single composition-root call.
    /// </exception>
    public static DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder, TimeProvider? timeProvider)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        if (!TryAddTrellisInterceptorsMarkerExtension(optionsBuilder, timeProvider))
            return optionsBuilder;

        optionsBuilder.AddInterceptors(s_maybeQueryInterceptor, s_scalarValueQueryInterceptor, s_aggregateETagInterceptor, new EntityTimestampInterceptor(timeProvider));
        AddMaybeEvaluatableExpressionFilterExtension(optionsBuilder);
        return optionsBuilder;
    }

    /// <summary>
    /// Adds Trellis EF Core interceptors to the <see cref="DbContextOptionsBuilder"/> with a custom <see cref="TimeProvider"/>.
    /// Non-generic overload for use with <c>DbContextOptionsBuilder</c> directly.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <param name="timeProvider">
    /// The time provider to use for <see cref="EntityTimestampInterceptor"/> timestamps.
    /// Defaults to <see cref="TimeProvider.System"/> if <c>null</c>.
    /// </param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>AddTrellisInterceptors</c> has already been called on this builder with
    /// a different <see cref="TimeProvider"/>. Library + application composition must agree
    /// on the time-provider choice.
    /// </exception>
    public static DbContextOptionsBuilder AddTrellisInterceptors(
        this DbContextOptionsBuilder optionsBuilder, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        if (!TryAddTrellisInterceptorsMarkerExtension(optionsBuilder, timeProvider))
            return optionsBuilder;

        optionsBuilder.AddInterceptors(s_maybeQueryInterceptor, s_scalarValueQueryInterceptor, s_aggregateETagInterceptor, new EntityTimestampInterceptor(timeProvider));
        AddMaybeEvaluatableExpressionFilterExtension(optionsBuilder);
        return optionsBuilder;
    }

    /// <summary>
    /// Records the marker extension on the builder when this is the first
    /// <c>AddTrellisInterceptors</c> call. Skips (returns <c>false</c>) when the marker is
    /// already present AND the requested <c>TimeProvider</c> matches the recorded one.
    /// Throws when the requested <c>TimeProvider</c> conflicts with the recorded one, so
    /// the consumer's TimeProvider choice cannot be silently shadowed by a prior call.
    /// </summary>
    private static bool TryAddTrellisInterceptorsMarkerExtension(
        DbContextOptionsBuilder optionsBuilder,
        TimeProvider? timeProvider)
    {
        var extension = optionsBuilder.Options.FindExtension<TrellisInterceptorsMarkerExtension>();
        if (extension is not null)
        {
            // Both calls supplied no TimeProvider — same default; idempotent skip.
            if (extension.RecordedTimeProvider is null && timeProvider is null)
                return false;

            // Same TimeProvider reference — idempotent skip.
            if (ReferenceEquals(extension.RecordedTimeProvider, timeProvider))
                return false;

            // Conflict: prior call recorded one TimeProvider, this call supplies another.
            // Fail fast rather than silently dropping the consumer's choice.
            var recorded = extension.RecordedTimeProvider?.GetType().FullName ?? "<default (System)>";
            var requested = timeProvider?.GetType().FullName ?? "<default (System)>";
            throw new InvalidOperationException(
                $"AddTrellisInterceptors was already called on this DbContextOptionsBuilder with TimeProvider '{recorded}'. " +
                $"The current call requests TimeProvider '{requested}', which would be silently ignored. " +
                "Consolidate to a single AddTrellisInterceptors call at your composition root with the intended TimeProvider.");
        }

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new TrellisInterceptorsMarkerExtension(timeProvider));
        return true;
    }

    private static void AddMaybeEvaluatableExpressionFilterExtension(DbContextOptionsBuilder optionsBuilder)
    {
        var coreOptions = optionsBuilder.Options.FindExtension<MaybeEvaluatableExpressionFilterExtension>();
        if (coreOptions is not null)
            return;

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new MaybeEvaluatableExpressionFilterExtension());
    }
}
