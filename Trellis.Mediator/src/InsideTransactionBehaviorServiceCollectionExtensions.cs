namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers application-owned pipeline behaviors inside the command's unit-of-work scope.
/// </summary>
public static class InsideTransactionBehaviorServiceCollectionExtensions
{
    /// <summary>
    /// Registers a scoped behavior between the transactional command behavior and the handler.
    /// </summary>
    /// <remarks>
    /// Repeated bindings deduplicate. Behaviors nest in registration order, so their post-handler
    /// work runs in reverse order, before the transaction behavior decides whether to commit.
    /// Installs the open transactional behavior, but not <see cref="IUnitOfWork"/>.
    /// Explicitly closed or generated pipelines must declare their own behavior order instead.
    /// </remarks>
    /// <typeparam name="TMessage">The command type.</typeparam>
    /// <typeparam name="TResponse">The command's result type.</typeparam>
    /// <typeparam name="TBehavior">The application-owned pipeline behavior.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when an existing closed transaction conflicts with open registration.</exception>
    public static IServiceCollection AddInsideTransactionBehavior<
        TMessage,
        TResponse,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior>(
        this IServiceCollection services)
        where TMessage : ICommand<TResponse>
        where TResponse : IResult, IFailureFactory<TResponse>
        where TBehavior : class, IPipelineBehavior<TMessage, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);
        return Register(services, typeof(IPipelineBehavior<TMessage, TResponse>), typeof(TBehavior));
    }

    /// <summary>
    /// Registers an open-generic scoped behavior for commands satisfying its constraints.
    /// </summary>
    /// <remarks>
    /// The concrete class must map its two parameters directly to
    /// <c>IPipelineBehavior&lt;TMessage,TResponse&gt;</c> and explicitly constrain
    /// <c>TMessage : ICommand&lt;TResponse&gt;</c> and
    /// <c>TResponse : IResult, IFailureFactory&lt;TResponse&gt;</c>.
    /// Additional constraints select matching commands; queries never enter this slot.
    /// Registration is order-independent with Trellis's mediator, event, and unit-of-work helpers.
    /// Register the application's <see cref="IUnitOfWork"/> separately.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="behaviorType">The concrete open behavior, with message and response parameters in that order.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the type is not a supported open command behavior.</exception>
    /// <exception cref="InvalidOperationException">Thrown when an existing closed transaction conflicts with open registration.</exception>
    public static IServiceCollection AddInsideTransactionBehavior(
        this IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
        Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(behaviorType);

        var parameters = behaviorType.GetGenericArguments();
        if (!behaviorType.IsClass || behaviorType.IsAbstract || !behaviorType.IsGenericTypeDefinition
            || parameters.Length != 2
            || !behaviorType.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)
                && contract.GetGenericArguments().SequenceEqual(parameters))
            || !HasConstraint(parameters[0], typeof(ICommand<>), parameters[1])
            || !HasConstraint(parameters[1], typeof(IResult))
            || !HasConstraint(parameters[1], typeof(IFailureFactory<>), parameters[1]))
            throw new ArgumentException(
                "The behavior must be a concrete open generic class implementing IPipelineBehavior<TMessage, TResponse> " +
                "in that parameter order, constrained by TMessage : ICommand<TResponse> and " +
                "TResponse : IResult, IFailureFactory<TResponse>.",
                nameof(behaviorType));

        return Register(services, typeof(IPipelineBehavior<,>), behaviorType);
    }

    internal static List<ServiceDescriptor> RemoveInsideTransactionBehaviorRegistrations(IServiceCollection services)
    {
        var descriptors = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(Registration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<Registration>()
            .Select(registration => registration.Descriptor)
            .Where(services.Contains)
            .ToList();

        foreach (var descriptor in descriptors)
            services.Remove(descriptor);

        return descriptors;
    }

    private static bool HasConstraint(Type parameter, Type contract, Type? argument = null) =>
        parameter.GetGenericParameterConstraints().Any(constraint => argument is null
            ? constraint == contract
            : constraint.IsGenericType
                && constraint.GetGenericTypeDefinition() == contract
                && constraint.GetGenericArguments()[0] == argument);

    private static IServiceCollection Register(
        IServiceCollection services,
        Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type behaviorType)
    {
        services.AddTransactionalCommandBehavior();

        var descriptor = services.FirstOrDefault(candidate => !candidate.IsKeyedService
            && candidate.ServiceType == serviceType && candidate.ImplementationType == behaviorType);
        if (descriptor is null)
        {
            descriptor = ServiceDescriptor.Scoped(serviceType, behaviorType);
            services.Add(descriptor);
        }

        if (!services.Any(candidate => !candidate.IsKeyedService
            && candidate.ImplementationInstance is Registration registration
            && ReferenceEquals(registration.Descriptor, descriptor)))
            services.AddSingleton(new Registration(descriptor));

        TransactionalCommandBehaviorServiceCollectionExtensions.RelocateTransactionalBehaviorsToEnd(services);
        return services;
    }

    private sealed record Registration(ServiceDescriptor Descriptor);
}
