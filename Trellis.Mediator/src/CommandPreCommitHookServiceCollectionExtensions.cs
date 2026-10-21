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
