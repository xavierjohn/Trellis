namespace Trellis.Asp.Idempotency;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

/// <summary>
/// Service-registration extensions for the Trellis Idempotency-Key middleware.
/// </summary>
public static class IdempotencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the idempotency middleware, its startup-validated options, and the default
    /// <see cref="DefaultIdempotencyScopeResolver"/> (per-actor scope, falling back to
    /// anonymous when no actor provider is registered). A store registration is required
    /// — call <see cref="AddInMemoryIdempotencyStore"/> for in-process scenarios, or supply
    /// your own <see cref="IIdempotencyStore"/> implementation.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback to mutate <see cref="IdempotencyOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTrellisIdempotency(
        this IServiceCollection services,
        Action<IdempotencyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddOptions<IdempotencyOptions>()
            .Configure(configure ?? (_ => { }))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsValidator>());

        services.TryAddSingleton<IIdempotencyScopeResolver, DefaultIdempotencyScopeResolver>();
        services.TryAddSingleton<IdempotencyMarker>();

        return services;
    }

    /// <summary>
    /// Registers an in-memory <see cref="IIdempotencyStore"/> suitable for single-instance
    /// deployments, tests, and demos. Not safe for multi-replica services where the same
    /// Idempotency-Key may land on a different replica.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryIdempotencyStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IIdempotencyStore>(sp => new InMemoryIdempotencyStore(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdempotencyOptions>>().Value,
            sp.GetService<TimeProvider>()));
        return services;
    }

    internal sealed class IdempotencyMarker
    {
    }
}
