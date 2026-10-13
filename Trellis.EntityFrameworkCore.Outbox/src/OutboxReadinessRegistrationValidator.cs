namespace Trellis.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal sealed class OutboxReadinessRegistrationValidator<TContext>(
    IServiceProvider services, OutboxReadinessOptions profile) : IHostedLifecycleService
    where TContext : DbContext
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        var registrations = services.GetService<IServiceProviderIsService>();
        if (registrations is not null && !registrations.IsService(typeof(TContext)))
            throw new InvalidOperationException(
                $"Outbox readiness requires a registered {typeof(TContext).Name}. Register the context before host startup.");

        if (profile.RelayMonitoring == OutboxRelayMonitoring.LocalRelay)
        {
            if (!services.GetServices<IHostedService>().Any(service => service is OutboxRelay<TContext>))
                throw new InvalidOperationException(
                    $"Local outbox readiness expects an actual OutboxRelay<{typeof(TContext).Name}> hosted service. " +
                    $"Call AddTrellisOutbox<{typeof(TContext).Name}> or UseOutbox<{typeof(TContext).Name}> separately. " +
                    "A wrapper that hides the relay instance does not satisfy local monitoring; " +
                    "select CreateMetadataOnlyProfile explicitly for a separate worker.");

            _ = services.GetRequiredService<OutboxOptions>();
            _ = services.GetRequiredService<OutboxRelayObservations<TContext>>();
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}