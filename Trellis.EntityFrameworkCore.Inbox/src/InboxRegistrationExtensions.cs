namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Registers the transactional inbox for a <see cref="DbContext"/>.
/// </summary>
public static class InboxServiceCollectionExtensions
{
    /// <summary>
    /// Registers the inbox dispatcher (<see cref="IInboxDispatcher"/>) and EF store
    /// (<see cref="IInboxStore"/>) for <typeparamref name="TContext"/>. Pair this with
    /// <see cref="InboxModelBuilderExtensions.AddTrellisInbox(ModelBuilder)"/> in the context's
    /// <c>OnModelCreating</c>, and register the integration-event handlers that should consume the messages
    /// (for example via <c>AddIntegrationEventHandler&lt;TEvent, THandler&gt;()</c>).
    /// </summary>
    /// <typeparam name="TContext">The consumer's <see cref="DbContext"/> that owns the inbox table.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Inbox configuration; <see cref="InboxOptions.ConsumerId"/> is required.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddTrellisInbox<TContext>(
        this IServiceCollection services,
        Action<InboxOptions> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new InboxOptions();
        configure(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IInboxStore, EfInboxStore<TContext>>();
        services.TryAddSingleton<IInboxDispatcher, InboxDispatcher<TContext>>();

        return services;
    }
}

/// <summary>
/// EF Core model hook for the transactional inbox.
/// </summary>
public static class InboxModelBuilderExtensions
{
    /// <summary>
    /// Maps the <see cref="InboxMessage"/> table onto the model. Call from <c>OnModelCreating</c>.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <returns>The same model builder for chaining.</returns>
    public static ModelBuilder AddTrellisInbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
        return modelBuilder;
    }
}
