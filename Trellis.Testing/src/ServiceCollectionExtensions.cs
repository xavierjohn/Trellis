namespace Trellis.Testing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trellis.Authorization;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> that simplify
/// replacing service registrations in integration tests.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Replaces any existing <see cref="IResourceLoader{TMessage, TResource}"/> registration
    /// with a scoped factory, matching the production lifetime of resource loaders.
    /// For stateless fakes, capture a pre-created instance: <c>_ => fakeLoader</c>.
    /// </summary>
    /// <typeparam name="TMessage">The command or query type that identifies the resource.</typeparam>
    /// <typeparam name="TResource">The resource type returned by the loader.</typeparam>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="factory">A factory that creates a loader instance per scope.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// // Stateless fake — capture a pre-created instance
    /// var fakeLoader = new FakeOrderResourceLoader(fakeRepo);
    /// services.ReplaceResourceLoader&lt;CancelOrderCommand, Order&gt;(_ => fakeLoader);
    ///
    /// // Scoped dependency — resolve from the container
    /// services.ReplaceResourceLoader&lt;CancelOrderCommand, Order&gt;(
    ///     sp => new FakeOrderResourceLoader(sp.GetRequiredService&lt;AppDbContext&gt;()));
    /// </code>
    /// </example>
    public static IServiceCollection ReplaceResourceLoader<TMessage, TResource>(
        this IServiceCollection services,
        Func<IServiceProvider, IResourceLoader<TMessage, TResource>> factory)
    {
        services.RemoveAll<IResourceLoader<TMessage, TResource>>();
        services.AddScoped(factory);
        return services;
    }

    /// <summary>
    /// Replaces all registrations of <typeparamref name="TService"/> with a singleton instance.
    /// Common use: replacing <see cref="TimeProvider"/> with
    /// <c>Microsoft.Extensions.TimeProvider.Testing.FakeTimeProvider</c> in integration tests.
    /// </summary>
    /// <typeparam name="TService">The service type to replace.</typeparam>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="instance">The singleton instance to register.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
    /// services.ReplaceSingleton&lt;TimeProvider&gt;(fakeTime);
    /// </code>
    /// </example>
    public static IServiceCollection ReplaceSingleton<TService>(
        this IServiceCollection services,
        TService instance)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        services.RemoveAll<TService>();
        services.AddSingleton(instance);
        return services;
    }
}