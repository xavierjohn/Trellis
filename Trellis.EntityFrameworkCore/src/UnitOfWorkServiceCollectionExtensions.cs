namespace Trellis.EntityFrameworkCore;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for registering <see cref="IUnitOfWork"/> and the
/// <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> pipeline behavior.
/// </summary>
public static class UnitOfWorkServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EfUnitOfWork{TContext}"/> as the <see cref="IUnitOfWork"/>
    /// implementation and adds the <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/>
    /// pipeline behavior so that command handlers automatically commit on success.
    /// <para>
    /// The behavior is inserted after the last existing <see cref="IPipelineBehavior{TMessage,TResponse}"/>
    /// registration (innermost position, closest to the handler). For correct ordering, call this
    /// method <b>after</b> <c>AddTrellisBehaviors()</c> and any other behavior registrations so that
    /// commit failures are visible to outer behaviors (logging, tracing, exception handling).
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">The concrete <see cref="DbContext"/> type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;(...);
    /// services.AddTrellisBehaviors();           // register other behaviors first
    /// services.AddTrellisUnitOfWork&lt;AppDbContext&gt;(); // commit behavior goes innermost
    /// </code>
    /// </example>
    public static IServiceCollection AddTrellisUnitOfWork<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
        AddTrackedAggregateSourceForwarder(services);
        InsertTransactionalBehavior(services);
        return services;
    }

    /// <summary>
    /// Registers <see cref="EfUnitOfWork{TContext}"/> as the <see cref="IUnitOfWork"/>
    /// implementation without registering the pipeline behavior.
    /// Use this when you want manual commit control (e.g., background jobs)
    /// or when the Mediator pipeline is not in use.
    /// </summary>
    /// <typeparam name="TContext">The concrete <see cref="DbContext"/> type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTrellisUnitOfWorkWithoutBehavior<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
        AddTrackedAggregateSourceForwarder(services);
        return services;
    }

    /// <summary>
    /// Forwards <see cref="ITrackedAggregateSource"/> through the registered <see cref="IUnitOfWork"/>
    /// so the same scoped instance backs both contracts. If a consumer pre-registered a custom
    /// <see cref="IUnitOfWork"/> that does not implement <see cref="ITrackedAggregateSource"/>, the
    /// forwarder throws at resolution time with an actionable message rather than silently handing
    /// out a different EF instance whose snapshot is never populated.
    /// </summary>
    private static void AddTrackedAggregateSourceForwarder(IServiceCollection services) =>
        services.TryAddScoped<ITrackedAggregateSource>(static sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            if (unitOfWork is ITrackedAggregateSource source)
                return source;

            throw new InvalidOperationException(
                $"The registered IUnitOfWork implementation '{unitOfWork.GetType().FullName}' does not implement " +
                $"ITrackedAggregateSource. Replace it with one that does (e.g. EfUnitOfWork<TContext>) or register " +
                $"ITrackedAggregateSource explicitly to use TrackedAggregateDomainEventDispatchBehavior.");
        });

    /// <summary>
    /// Inserts the open-generic <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/>
    /// after the last <see cref="IPipelineBehavior{TMessage,TResponse}"/> registration so it runs
    /// innermost (closest to the handler). If no behaviors are registered yet, appends at the end.
    /// </summary>
    /// <remarks>
    /// <para>Idempotent for the open-generic case: a second call is a no-op when the open-generic
    /// <c>TransactionalCommandBehavior&lt;,&gt;</c> is already registered.</para>
    /// <para>Throws <see cref="InvalidOperationException"/> when a closed-generic
    /// <c>TransactionalCommandBehavior&lt;TMessage,TResponse&gt;</c> is already registered. The
    /// open generic added here would resolve alongside the closed registration on matching
    /// commands and cause <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> to run
    /// twice (two commits per command). The actionable resolution is to remove the closed
    /// registration (so this method installs the open generic that covers every command), or to
    /// use <c>AddTrellisUnitOfWorkWithoutBehavior&lt;TContext&gt;()</c> and keep the explicit
    /// closed registrations. Detection covers <see cref="ServiceDescriptor.ImplementationType"/>
    /// and <see cref="ServiceDescriptor.ImplementationInstance"/>; factory-registered closed
    /// transactional behaviors (via <see cref="ServiceDescriptor.ImplementationFactory"/>) are
    /// not detectable without invoking the factory and are not caught here — consumers using
    /// factory registrations should call
    /// <c>AddTrellisUnitOfWorkWithoutBehavior&lt;TContext&gt;()</c> and own behavior wiring.</para>
    /// </remarks>
    private static void InsertTransactionalBehavior(IServiceCollection services)
    {
        // Single-pass scan: detect an existing open-generic TransactionalCommandBehavior
        // registration (idempotency), detect any closed-generic TransactionalCommandBehavior
        // registrations (conflict — must throw), and track the index of the last open-or-closed
        // IPipelineBehavior<,> registration so the new behavior is inserted innermost.
        var lastBehaviorIndex = -1;
        var openTransactionalAlreadyRegistered = false;
        string? closedConflictDisplay = null;
        for (var i = 0; i < services.Count; i++)
        {
            var existingDescriptor = services[i];
            if (!IsPipelineBehaviorRegistration(existingDescriptor.ServiceType))
                continue;

            if (IsOpenTransactionalCommandBehaviorRegistration(existingDescriptor))
                openTransactionalAlreadyRegistered = true;
            else if (IsClosedTransactionalCommandBehaviorRegistration(existingDescriptor))
                closedConflictDisplay ??= FormatDescriptor(existingDescriptor);

            lastBehaviorIndex = i;
        }

        if (closedConflictDisplay is not null)
        {
            throw new InvalidOperationException(
                $"Cannot register the open-generic TransactionalCommandBehavior<,> alongside the " +
                $"pre-existing closed-generic registration '{closedConflictDisplay}'. Both would " +
                $"run on matching commands, producing two commits per command. Either remove the " +
                $"closed registration so AddTrellisUnitOfWork<TContext>() can install the open " +
                $"generic that covers every command, or call " +
                $"AddTrellisUnitOfWorkWithoutBehavior<TContext>() to keep the explicit closed " +
                $"registrations and skip open-generic installation.");
        }

        if (openTransactionalAlreadyRegistered)
            return;

        var descriptor = ServiceDescriptor.Scoped(
            typeof(IPipelineBehavior<,>), typeof(TransactionalCommandBehavior<,>));

        if (lastBehaviorIndex >= 0)
            services.Insert(lastBehaviorIndex + 1, descriptor);
        else
            services.Add(descriptor);
    }

    private static bool IsPipelineBehaviorRegistration(Type serviceType) =>
        serviceType == typeof(IPipelineBehavior<,>)
        || (serviceType.IsGenericType
            && serviceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

    private static bool IsOpenTransactionalCommandBehaviorRegistration(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IPipelineBehavior<,>)
        && descriptor.ImplementationType == typeof(TransactionalCommandBehavior<,>);

    private static bool IsClosedTransactionalCommandBehaviorRegistration(ServiceDescriptor descriptor)
    {
        if (descriptor.ServiceType is not { IsGenericType: true } serviceType
            || serviceType.GetGenericTypeDefinition() != typeof(IPipelineBehavior<,>))
            return false;

        // ImplementationType: covered explicitly.
        if (descriptor.ImplementationType is { IsGenericType: true } impl
            && impl.GetGenericTypeDefinition() == typeof(TransactionalCommandBehavior<,>))
            return true;

        // ImplementationInstance: inspect the concrete instance's runtime type. This catches
        // singleton-style closed registrations like
        //   services.AddSingleton<IPipelineBehavior<C, R>>(new TransactionalCommandBehavior<C, R>(...))
        if (descriptor.ImplementationInstance is { } instance
            && instance.GetType() is { IsGenericType: true } instanceType
            && instanceType.GetGenericTypeDefinition() == typeof(TransactionalCommandBehavior<,>))
            return true;

        // ImplementationFactory: not detectable without invoking the factory. Documented in the
        // remarks on InsertTransactionalBehavior — a factory-registered closed transactional
        // behavior alongside the open-generic registration this helper installs will still
        // produce two commits per matching command. Consumers using factory registrations should
        // call AddTrellisUnitOfWorkWithoutBehavior<TContext>() and own behavior wiring.
        return false;
    }

    private static string FormatDescriptor(ServiceDescriptor descriptor) =>
        $"{descriptor.ServiceType.FullName} -> {descriptor.ImplementationType?.FullName}";
}