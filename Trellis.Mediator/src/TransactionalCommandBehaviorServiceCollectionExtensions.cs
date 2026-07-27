namespace Trellis.Mediator;

using global::Mediator;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration for the <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> pipeline behavior,
/// independent of any persistence adapter. Any adapter that registers an <see cref="IUnitOfWork"/> (the
/// shipped EF Core adapter does this in <c>AddTrellisUnitOfWork&lt;TContext&gt;()</c>) can call
/// <see cref="AddTransactionalCommandBehavior"/> to install the standard commit pipeline.
/// </summary>
public static class TransactionalCommandBehaviorServiceCollectionExtensions
{
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
    /// skip this method and keep the explicit closed registrations. Detection covers
    /// <see cref="ServiceDescriptor.ImplementationType"/> and
    /// <see cref="ServiceDescriptor.ImplementationInstance"/>; factory-registered closed
    /// transactional behaviors (via <see cref="ServiceDescriptor.ImplementationFactory"/>) are
    /// not detectable without invoking the factory and are not caught here — consumers using
    /// factory registrations should skip this method and own behavior wiring.</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTransactionalCommandBehavior(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

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
                $"closed registration so AddTransactionalCommandBehavior() can install the open " +
                $"generic that covers every command, or skip AddTransactionalCommandBehavior() to " +
                $"keep the explicit closed registrations.");
        }

        if (openTransactionalAlreadyRegistered)
            return services;

        var descriptor = ServiceDescriptor.Scoped(
            typeof(IPipelineBehavior<,>), typeof(TransactionalCommandBehavior<,>));

        if (lastBehaviorIndex >= 0)
            services.Insert(lastBehaviorIndex + 1, descriptor);
        else
            services.Add(descriptor);

        return services;
    }

    private static bool IsPipelineBehaviorRegistration(Type serviceType) =>
        serviceType == typeof(IPipelineBehavior<,>)
        || (serviceType.IsGenericType
            && serviceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

    internal static bool IsTransactionalCommandBehaviorRegistration(ServiceDescriptor descriptor) =>
        IsOpenTransactionalCommandBehaviorRegistration(descriptor)
        || IsClosedTransactionalCommandBehaviorRegistration(descriptor);

    internal static List<ServiceDescriptor> RemoveTransactionalCommandBehaviorRegistrations(IServiceCollection services)
    {
        var descriptors = new List<ServiceDescriptor>();
        for (var i = 0; i < services.Count; i++)
        {
            if (IsTransactionalCommandBehaviorRegistration(services[i]))
                descriptors.Add(services[i]);
        }

        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (IsTransactionalCommandBehaviorRegistration(services[i]))
                services.RemoveAt(i);
        }

        return descriptors;
    }

    private static bool IsOpenTransactionalCommandBehaviorRegistration(ServiceDescriptor descriptor) =>
        descriptor.ServiceType == typeof(IPipelineBehavior<,>)
        && descriptor.ImplementationType == typeof(TransactionalCommandBehavior<,>);

    internal static bool IsClosedTransactionalCommandBehaviorRegistration(ServiceDescriptor descriptor)
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

        // ImplementationFactory: not detectable without invoking the factory. A factory-registered
        // closed transactional behavior alongside the open-generic registration this helper installs
        // will still produce two commits per matching command. Consumers using factory registrations
        // should skip AddTransactionalCommandBehavior() and own behavior wiring.
        return false;
    }

    private static string FormatDescriptor(ServiceDescriptor descriptor)
    {
        var implementationDisplay = descriptor.ImplementationType?.FullName
            ?? descriptor.ImplementationInstance?.GetType().FullName
            ?? descriptor.ImplementationFactory?.GetType().FullName
            ?? "<unknown>";
        return $"{descriptor.ServiceType.FullName} -> {implementationDisplay}";
    }
}
