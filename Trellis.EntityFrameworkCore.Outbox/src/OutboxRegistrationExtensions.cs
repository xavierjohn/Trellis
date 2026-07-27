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

        // Configure and validate a copy so a failed Validate() cannot leave the container holding a
        // half-applied options instance; the new state is committed only after it is known good.
        var existingIndex = FindLastOptionsIndex(services);
        if (existingIndex >= 0 && services[existingIndex].ImplementationInstance is not OutboxOptions)
        {
            // A consumer owns the OutboxOptions registration via a factory or implementation type, so
            // this helper cannot layer onto it. Applying configure to a fresh instance would register a
            // second descriptor the container never resolves, silently dropping the caller's tuning.
            if (configure is not null)
                throw new InvalidOperationException(
                    "OutboxOptions is already registered by a factory or implementation type, so " +
                    "AddTrellisOutbox cannot apply its configure callback — the configured instance would " +
                    "not be the one resolved for the relay. Either remove the custom OutboxOptions " +
                    "registration and configure it here, or call AddTrellisOutbox<TContext>() without a " +
                    "configure callback and keep owning the options registration.");
        }
        else
        {
            var options = existingIndex < 0
                ? new OutboxOptions()
                : ((OutboxOptions)services[existingIndex].ImplementationInstance!).Clone();

            configure?.Invoke(options);
            options.Validate();

            if (existingIndex < 0)
                services.AddSingleton(options);
            else
                services[existingIndex] = ServiceDescriptor.Singleton(options);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<OutboxRelay<TContext>>();
        services.TryAddScoped<IOutboxMaintenance, OutboxMaintenance<TContext>>();

        return services;
    }

    /// <summary>
    /// Returns the index of the <b>last</b> unkeyed <see cref="OutboxOptions"/> descriptor, or <c>-1</c>
    /// when none is registered. The last one is what <c>GetRequiredService&lt;OutboxOptions&gt;()</c>
    /// resolves, so layering a repeated <c>configure</c> callback onto any earlier descriptor would
    /// configure an instance the relay never sees. Keyed descriptors are skipped: they do not take part
    /// in unkeyed resolution, and reading <c>ImplementationInstance</c> on one throws.
    /// </summary>
    private static int FindLastOptionsIndex(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(OutboxOptions) && !services[i].IsKeyedService)
                return i;
        }

        return -1;
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
