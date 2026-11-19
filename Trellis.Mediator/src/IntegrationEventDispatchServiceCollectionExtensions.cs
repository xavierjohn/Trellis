namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for registering Trellis.Mediator integration-event publishing.
/// </summary>
/// <remarks>
/// Unlike domain-event dispatch, integration events are not dispatched by a command-pipeline behavior;
/// they are produced via the <see cref="IIntegrationEventCollector"/> during domain-event handling and
/// published by the transactional outbox relay. These helpers register the default best-effort
/// in-process publisher, the scoped collector, and any consumers. The same handler registrations are
/// used by <see cref="IInboxDispatcher"/>. Select <see cref="AddLocalInboxDelivery"/> explicitly to
/// route messages through an inbox.
/// </remarks>
public static class IntegrationEventDispatchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default <see cref="IIntegrationEventPublisher"/> (best-effort in-process fan-out) and the scoped
    /// <see cref="IIntegrationEventCollector"/>. This is the AOT/trim-friendly entry point; pair it with
    /// <see cref="AddIntegrationEventHandler{TEvent, THandler}(IServiceCollection)"/> for each consumer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Idempotent: adds the publisher and collector only if they are not already registered.
    /// The default publisher logs and swallows ordinary consumer failures; the relay processes the row
    /// without retrying those consumers. To deliver integration events to other services, replace the
    /// <see cref="IIntegrationEventPublisher"/> registration with a message-broker adapter that awaits
    /// broker acceptance and propagates publication failures so the relay can retry.
    /// </remarks>
    public static IServiceCollection AddIntegrationEventDispatch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IIntegrationEventPublisher, MediatorIntegrationEventPublisher>();
        services.TryAddScoped<IIntegrationEventCollector, IntegrationEventCollector>();

        return services;
    }

    /// <summary>
    /// Selects the registered <see cref="IInboxDispatcher"/> as the destination for local integration-event delivery.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Replaces the default best-effort publisher and registers the scoped <see cref="IIntegrationEventCollector"/>
    /// as <see cref="AddIntegrationEventDispatch(IServiceCollection)"/> does. Register the inbox, its handlers, and the outbox separately.
    /// Every event published to the built-in inbox needs a local handler. A new message with no handler
    /// fails delivery instead of being skipped and is retried or parked by the relay.
    /// Publication completes after the inbox processes the message or skips a duplicate; failures propagate to the relay.
    /// This registration is idempotent and can run before or after <see cref="AddIntegrationEventDispatch(IServiceCollection)"/>.
    /// Do not combine it with a broker or another unkeyed publisher. The host rejects a later addition
    /// or replacement at startup.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A custom unkeyed integration-event publisher is already registered.</exception>
    public static IServiceCollection AddLocalInboxDelivery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (UnkeyedPublishers(services).Any(descriptor => descriptor.ImplementationType != typeof(MediatorIntegrationEventPublisher)
            && descriptor.ImplementationType != typeof(LocalInboxIntegrationEventPublisher)))
        {
            throw new InvalidOperationException(
                "Local inbox delivery cannot be combined with a custom integration-event publisher. Select one publisher.");
        }

        services.AddIntegrationEventDispatch();
        foreach (var publisher in UnkeyedPublishers(services))
            services.Remove(publisher);

        services.TryAddSingleton(new LocalInboxDeliveryRegistrations(services));
        services.AddHostedService<LocalInboxDeliveryRegistrationValidator>();
        return services.AddScoped<IIntegrationEventPublisher, LocalInboxIntegrationEventPublisher>();
    }

    private static ServiceDescriptor[] UnkeyedPublishers(IServiceCollection services) =>
        [.. services.Where(descriptor =>
            descriptor.ServiceType == typeof(IIntegrationEventPublisher) && !descriptor.IsKeyedService)];

    /// <summary>
    /// Registers a single <see cref="IIntegrationEventHandler{TEvent}"/> and ensures the publisher and
    /// collector are wired up. Use this for AOT/trim scenarios where assembly scanning is not available.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type the handler responds to.</typeparam>
    /// <typeparam name="THandler">The handler implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIntegrationEventHandler<
        TEvent,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services)
        where TEvent : IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddIntegrationEventDispatch();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntegrationEventHandler<TEvent>, THandler>());

        return services;
    }

    /// <summary>
    /// Scans the specified assemblies for concrete <see cref="IIntegrationEventHandler{TEvent}"/>
    /// implementations and registers each as a scoped service, along with the default publisher and
    /// collector.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Assemblies to scan for handler implementations.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="assemblies"/> is empty or contains a null element.</exception>
    [RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use AddIntegrationEventHandler<TEvent, THandler> for AOT/trim scenarios.")]
    [RequiresDynamicCode("Constructs closed generic IIntegrationEventHandler<TEvent> at runtime.")]
    public static IServiceCollection AddIntegrationEventDispatch(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        AssemblyScanner.ValidateAssemblies(assemblies);

        services.AddIntegrationEventDispatch();
        AssemblyScanner.RegisterHandlers(services, assemblies, typeof(IIntegrationEventHandler<>));

        return services;
    }
}