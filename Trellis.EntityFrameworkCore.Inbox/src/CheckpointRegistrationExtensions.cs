namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Registers the pull-consumer checkpoint store for a <see cref="DbContext"/>.
/// </summary>
public static class CheckpointServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core <see cref="IConsumerCheckpointStore"/> backed by <typeparamref name="TContext"/>.
    /// Pair this with
    /// <see cref="CheckpointModelBuilderExtensions.AddTrellisConsumerCheckpoints(ModelBuilder)"/> in the
    /// context's <c>OnModelCreating</c>. The store is a durable resume cursor — a performance optimization;
    /// once-effective processing stays with the inbox anti-join
    /// (<see cref="IInboxStore.FilterUnprocessedAsync"/>) and dedup row, not the checkpoint.
    /// </summary>
    /// <typeparam name="TContext">The consumer's <see cref="DbContext"/> that owns the checkpoint table.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddTrellisConsumerCheckpointStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IConsumerCheckpointStore, EfConsumerCheckpointStore<TContext>>();

        return services;
    }
}

/// <summary>
/// EF Core model hook for the pull-consumer checkpoint table.
/// </summary>
public static class CheckpointModelBuilderExtensions
{
    /// <summary>
    /// Maps the <see cref="ConsumerCheckpoint"/> table (<c>TrellisConsumerCheckpoints</c>) onto the model.
    /// Call from <c>OnModelCreating</c>.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <returns>The same model builder for chaining.</returns>
    public static ModelBuilder AddTrellisConsumerCheckpoints(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new ConsumerCheckpointConfiguration());
        return modelBuilder;
    }
}