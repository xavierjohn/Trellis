namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trellis.Mediator;

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

        // Configure and validate a copy so a failed Validate() cannot leave the container holding a
        // half-applied options instance; the new state is committed only after it is known good.
        var existingIndex = FindLastOptionsIndex(services);
        if (existingIndex >= 0 && services[existingIndex].ImplementationInstance is not InboxOptions)
        {
            // A consumer owns the InboxOptions registration via a factory or implementation type, so
            // this helper cannot layer onto it. Applying configure to a fresh instance would register a
            // second descriptor the container never resolves, silently dropping the caller's ConsumerId.
            throw new InvalidOperationException(
                "InboxOptions is already registered by a factory or implementation type, so AddTrellisInbox " +
                "cannot apply its configure callback — the configured instance would not be the one resolved " +
                "for the dispatcher. Remove the custom InboxOptions registration and configure it here.");
        }

        var options = existingIndex < 0
            ? new InboxOptions()
            : ((InboxOptions)services[existingIndex].ImplementationInstance!).Clone();

        configure(options);
        options.Validate();

        if (existingIndex < 0)
            services.AddSingleton(options);
        else
            services[existingIndex] = ServiceDescriptor.Singleton(options);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IInboxStore, EfInboxStore<TContext>>();
        services.TryAddSingleton<IInboxDispatcher, InboxDispatcher<TContext>>();

        return services;
    }

    /// <summary>
    /// Returns the index of the <b>last</b> unkeyed <see cref="InboxOptions"/> descriptor, or <c>-1</c>
    /// when none is registered. The last one is what <c>GetRequiredService&lt;InboxOptions&gt;()</c>
    /// resolves, so layering a repeated <c>configure</c> callback onto any earlier descriptor would
    /// configure an instance the dispatcher never sees. Keyed descriptors are skipped: they do not take
    /// part in unkeyed resolution, and reading <c>ImplementationInstance</c> on one throws.
    /// </summary>
    private static int FindLastOptionsIndex(IServiceCollection services)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(InboxOptions) && !services[i].IsKeyedService)
                return i;
        }

        return -1;
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
