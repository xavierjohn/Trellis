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
/// published by the transactional outbox relay. These helpers register the default in-process publisher,
/// the scoped collector, and any in-process consumers.
/// </remarks>
public static class IntegrationEventDispatchServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default <see cref="IIntegrationEventPublisher"/> (in-process fan-out) and the scoped
    /// <see cref="IIntegrationEventCollector"/>. This is the AOT/trim-friendly entry point; pair it with
    /// <see cref="AddIntegrationEventHandler{TEvent, THandler}(IServiceCollection)"/> for each consumer.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Idempotent: calling this more than once registers the publisher and collector exactly once. To
    /// deliver integration events to other services, replace the <see cref="IIntegrationEventPublisher"/>
    /// registration with a message-broker adapter after calling this method.
    /// </remarks>
    public static IServiceCollection AddIntegrationEventDispatch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IIntegrationEventPublisher, MediatorIntegrationEventPublisher>();
        services.TryAddScoped<IIntegrationEventCollector, IntegrationEventCollector>();

        return services;
    }

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
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0)
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));
        for (var i = 0; i < assemblies.Length; i++)
        {
            if (assemblies[i] is null)
                throw new ArgumentException($"Assembly at index [{i}] is null.", nameof(assemblies));
        }

        services.AddIntegrationEventDispatch();

        var handlerInterfaceDef = typeof(IIntegrationEventHandler<>);

        foreach (var assembly in assemblies)
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                foreach (var iface in type.GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == handlerInterfaceDef)
                        services.TryAddEnumerable(ServiceDescriptor.Scoped(iface, type));
                }
            }

        return services;
    }

    [RequiresUnreferencedCode("Calls Assembly.GetTypes().")]
    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().ToArray();
        }
    }
}