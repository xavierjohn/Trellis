namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Registers the transactional outbox relay for a <see cref="DbContext"/>.
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox relay hosted service for <typeparamref name="TContext"/>. Pair this with
    /// <see cref="OutboxModelBuilderExtensions.AddTrellisOutbox(ModelBuilder)"/> in the context's
    /// <c>OnModelCreating</c> and
    /// <see cref="OutboxModelBuilderExtensions.AddTrellisOutboxInterceptor(DbContextOptionsBuilder)"/>
    /// on the options builder. Domain-event handlers and <c>IDomainEventPublisher</c> must also be
    /// registered (for example via <c>AddDomainEventDispatch(...)</c>).
    /// </summary>
    /// <typeparam name="TContext">The application's <see cref="DbContext"/> that owns the outbox table.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional relay tuning.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddTrellisOutbox<TContext>(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OutboxOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<OutboxRelay<TContext>>();

        return services;
    }
}

/// <summary>
/// EF Core model and interceptor hooks for the transactional outbox.
/// </summary>
public static class OutboxModelBuilderExtensions
{
    private static readonly OutboxCaptureInterceptor s_captureInterceptor = new();

    /// <summary>
    /// Maps the <see cref="OutboxMessage"/> table onto the model. Call from <c>OnModelCreating</c>.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <returns>The same model builder for chaining.</returns>
    public static ModelBuilder AddTrellisOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        return modelBuilder;
    }

    /// <summary>
    /// Adds the outbox capture interceptor so uncommitted domain events are written to the outbox
    /// table in the same transaction as the aggregate change. Call on the <c>DbContextOptionsBuilder</c>.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    /// <returns>The same options builder for chaining.</returns>
    public static DbContextOptionsBuilder AddTrellisOutboxInterceptor(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        if (TryMarkOutboxInterceptor(optionsBuilder))
            optionsBuilder.AddInterceptors(s_captureInterceptor);
        return optionsBuilder;
    }

    /// <inheritdoc cref="AddTrellisOutboxInterceptor(DbContextOptionsBuilder)"/>
    /// <typeparam name="TContext">The <see cref="DbContext"/> type.</typeparam>
    public static DbContextOptionsBuilder<TContext> AddTrellisOutboxInterceptor<TContext>(
        this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        if (TryMarkOutboxInterceptor(optionsBuilder))
            optionsBuilder.AddInterceptors(s_captureInterceptor);
        return optionsBuilder;
    }

    // Records a marker on the builder the first time the interceptor is added so repeat calls are
    // idempotent. EF does not de-duplicate the same interceptor instance, and the interceptor only
    // clears aggregate events in SavedChanges, so a double registration would capture each event twice.
    private static bool TryMarkOutboxInterceptor(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.Options.FindExtension<OutboxInterceptorMarkerExtension>() is not null)
            return false;

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new OutboxInterceptorMarkerExtension());
        return true;
    }
}
