namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

/// <summary>Registers explicitly selected, read-only outbox readiness.</summary>
public static class OutboxReadinessServiceCollectionExtensions
{
    /// <summary>
    /// Adds the named <c>trellis-outbox</c> health check for the registered outbox context.
    /// The host owns endpoint authorization, selection, and readiness versus alert-only exposure.
    /// </summary>
    /// <remarks>
    /// Register <c>AddTrellisOutbox&lt;TContext&gt;</c> separately. Repeating this call with the same
    /// context and equal profile is idempotent; conflicting contexts or profiles throw.
    /// The context must resolve the same fixed store from background scopes, not request-local tenancy.
    /// No capture-age column, endpoint, liveness policy, or consumer-lag provider is added.
    /// </remarks>
    /// <typeparam name="TContext">The application's context that owns the outbox table.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="profile">An explicitly selected and optionally tuned readiness profile.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddTrellisOutboxReadiness<TContext>(
        this IServiceCollection services, OutboxReadinessOptions profile)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();
        OutboxReadinessRegistration.ValidateRelayContext<TContext>(services);

        var existing = services.FirstOrDefault(d =>
            d.ServiceType == typeof(OutboxReadinessRegistration) && !d.IsKeyedService)?.ImplementationInstance;
        if (existing is OutboxReadinessRegistration registration)
        {
            if (registration.ContextType != typeof(TContext) || registration.Profile != profile)
                throw new InvalidOperationException(
                    "Only one outbox readiness context and profile can be configured per composition.");
            return services;
        }

        services.AddSingleton(new OutboxReadinessRegistration(typeof(TContext), profile));
        services.AddSingleton(profile);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OutboxRelayObservations<TContext>>();
        services.TryAddSingleton<OutboxReadinessHealthCheck<TContext>>();
        services.AddHealthChecks().Add(new HealthCheckRegistration(
            "trellis-outbox",
            provider => provider.GetRequiredService<OutboxReadinessHealthCheck<TContext>>(),
            HealthStatus.Unhealthy,
            ["trellis", "outbox"]));
        return services;
    }
}

internal sealed record OutboxReadinessRegistration(Type ContextType, OutboxReadinessOptions Profile)
{
    public static void ValidateRelayContext<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        if (services.Any(d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType is { IsGenericType: true } type
            && type.GetGenericTypeDefinition() == typeof(OutboxRelay<>)
            && type.GetGenericArguments()[0] != typeof(TContext)))
            throw new InvalidOperationException("Outbox readiness must observe the registered relay's context.");

        ValidateReadinessContext<TContext>(services);
    }

    public static void ValidateReadinessContext<TContext>(IServiceCollection services)
        where TContext : DbContext
    {
        var registration = services.FirstOrDefault(d =>
            d.ServiceType == typeof(OutboxReadinessRegistration) && !d.IsKeyedService)?.ImplementationInstance;
        if (registration is OutboxReadinessRegistration readiness && readiness.ContextType != typeof(TContext))
            throw new InvalidOperationException("Outbox readiness must observe the registered relay's context.");
    }
}