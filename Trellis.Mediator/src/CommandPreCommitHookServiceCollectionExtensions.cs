namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Registers command-specific hooks inside the provider-neutral transactional command behavior.
/// </summary>
public static class CommandPreCommitHookServiceCollectionExtensions
{
    /// <summary>
    /// Registers an open-generic scoped hook for commands satisfying its generic constraints.
    /// </summary>
    /// <remarks>
    /// The implementation must map its first and second generic parameters directly to the message
    /// and response parameters of <see cref="ICommandPreCommitHook{TMessage,TResponse}"/>.
    /// Microsoft DI filters nonmatching constraints when resolving hook enumerables.
    /// Identical open bindings deduplicate; distinct bindings run in registration order.
    /// Installs the transaction behavior and state accessor, but not <see cref="IUnitOfWork"/>.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="hookType">A concrete open class mapping its message and response parameters to the hook contract.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the type is not a supported open hook.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a known closed transactional behavior conflicts with open-generic installation.
    /// </exception>
    public static IServiceCollection AddCommandPreCommitHook(
        this IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)]
        Type hookType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(hookType);

        if (!hookType.IsClass || hookType.IsAbstract || !hookType.IsGenericTypeDefinition
            || hookType.GetGenericArguments().Length != 2
            || !hookType.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(ICommandPreCommitHook<,>)
                && contract.GetGenericArguments().SequenceEqual(hookType.GetGenericArguments())))
            throw new ArgumentException(
                "The hook must be a concrete open generic class with two type parameters implementing " +
                "ICommandPreCommitHook<TMessage, TResponse> in that order.",
                nameof(hookType));

        services.AddTransactionalCommandBehavior();
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(ICommandPreCommitHook<,>), hookType));
        return services;
    }

    /// <summary>
    /// Registers a scoped pre-commit hook and ensures the transactional command behavior is installed.
    /// </summary>
    /// <remarks>
    /// Hooks execute sequentially in registration order, after the handler and before its commit.
    /// Repeated registrations of the same command, response, and hook are idempotent. This helper
    /// adds no separate pipeline stage and is order-independent with Trellis behavior and
    /// unit-of-work registrations. Register the application's <see cref="IUnitOfWork"/> separately.
    /// Hosts with explicit closed transactional behaviors register the hook directly in DI instead.
    /// </remarks>
    /// <typeparam name="TMessage">The command type.</typeparam>
    /// <typeparam name="TResponse">The command's result type.</typeparam>
    /// <typeparam name="THook">The hook implementation type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a known closed transactional behavior conflicts with open-generic installation.
    /// </exception>
    public static IServiceCollection AddCommandPreCommitHook<
        TMessage,
        TResponse,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THook>(
        this IServiceCollection services)
        where TMessage : ICommand<TResponse>
        where TResponse : IResult, IFailureFactory<TResponse>
        where THook : class, ICommandPreCommitHook<TMessage, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransactionalCommandBehavior();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ICommandPreCommitHook<TMessage, TResponse>, THook>());

        return services;
    }
}
