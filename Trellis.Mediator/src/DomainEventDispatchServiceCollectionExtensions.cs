namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for registering Trellis.Mediator domain-event dispatch.
/// </summary>
public static class DomainEventDispatchServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DomainEventDispatchBehavior{TMessage, TResponse}"/> as an open-generic
    /// pipeline behavior together with the default <see cref="IDomainEventPublisher"/> implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the AOT/trim-friendly registration entry point; pair it with
    /// <see cref="AddDomainEventHandler{TEvent, THandler}(IServiceCollection)"/> calls for each
    /// concrete handler. For assembly scanning, use
    /// <see cref="AddDomainEventDispatch(IServiceCollection, Assembly[])"/>.
    /// </para>
    /// <para>
    /// The behavior is inserted as the innermost of the always-on Trellis behaviors,
    /// running after <c>ValidationBehavior</c>
    /// (Exception → Tracing → Logging → Authorization → Validation → DomainEventDispatch).
    /// If <c>TransactionalCommandBehavior</c> (from <c>Trellis.EntityFrameworkCore</c>) is
    /// already registered, this method temporarily yanks it, ensures the always-on Trellis
    /// behaviors are present, appends dispatch, and re-appends the transactional behavior as
    /// innermost. The result is order-independent: events fire after the transaction commits
    /// regardless of whether <c>AddTrellisUnitOfWork&lt;TContext&gt;()</c> was called before
    /// or after this method.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once registers the behavior and publisher exactly once.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDomainEventDispatch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IDomainEventPublisher, MediatorDomainEventPublisher>();

        // Temporarily yank an already-registered transactional behavior so subsequent
        // appends (always-on behaviors + dispatch) land before it; re-append it as
        // innermost at the end. This makes the result order-independent vs the consumer's
        // call to AddTrellisUnitOfWork<TContext>().
        var transactionalDescriptor = TryRemoveTransactionalBehavior(services);

        services.AddTrellisBehaviors();
        AppendDispatchBehavior(services);

        if (transactionalDescriptor is not null)
            services.Add(transactionalDescriptor);

        return services;
    }

    /// <summary>
    /// Registers a single <see cref="IDomainEventHandler{TEvent}"/> and ensures the dispatch
    /// behavior is wired up. Use this for AOT/trim scenarios where assembly scanning is not available.
    /// </summary>
    /// <typeparam name="TEvent">The domain event type the handler responds to.</typeparam>
    /// <typeparam name="THandler">The handler implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDomainEventHandler<
        TEvent,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services)
        where TEvent : IDomainEvent
        where THandler : class, IDomainEventHandler<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDomainEventDispatch();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IDomainEventHandler<TEvent>, THandler>());

        return services;
    }

    /// <summary>
    /// Scans the specified assemblies for concrete <see cref="IDomainEventHandler{TEvent}"/>
    /// implementations and registers each as a scoped service. Also registers the dispatch
    /// behavior and default publisher.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Assemblies to scan for handler implementations.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="assemblies"/> is empty or contains a null element.</exception>
    /// <remarks>
    /// <para>
    /// A single concrete type may implement <see cref="IDomainEventHandler{TEvent}"/> for
    /// multiple event types; each interface implementation is registered separately so the
    /// type is invoked for each event it declares.
    /// </para>
    /// <para>
    /// Registration uses <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)"/>
    /// so calling this method repeatedly does not produce duplicate handler registrations.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use AddDomainEventHandler<TEvent, THandler> for AOT/trim scenarios.")]
    [RequiresDynamicCode("Constructs closed generic IDomainEventHandler<TEvent> at runtime.")]
    public static IServiceCollection AddDomainEventDispatch(
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

        services.AddDomainEventDispatch();

        var handlerInterfaceDef = typeof(IDomainEventHandler<>);

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

    // Trellis.Mediator does not reference Trellis.EntityFrameworkCore, so detect the
    // transactional behavior by full type name to keep the package boundary clean.
    private const string TransactionalBehaviorTypeName = "Trellis.EntityFrameworkCore.TransactionalCommandBehavior`2";

    private static ServiceDescriptor? TryRemoveTransactionalBehavior(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(IPipelineBehavior<,>)
                && services[i].ImplementationType?.FullName == TransactionalBehaviorTypeName)
            {
                var descriptor = services[i];
                services.RemoveAt(i);
                return descriptor;
            }
        }

        return null;
    }

    private static void AppendDispatchBehavior(IServiceCollection services)
    {
        // Idempotent: skip if dispatch behavior is already registered.
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(IPipelineBehavior<,>)
                && services[i].ImplementationType == typeof(DomainEventDispatchBehavior<,>))
            {
                return;
            }
        }

        services.Add(ServiceDescriptor.Scoped(
            typeof(IPipelineBehavior<,>),
            typeof(DomainEventDispatchBehavior<,>)));
    }
}
