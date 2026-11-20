namespace Trellis.Mediator;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal sealed record LocalInboxDeliveryRegistrations(IServiceCollection Services);

internal sealed class LocalInboxDeliveryRegistrationValidator(LocalInboxDeliveryRegistrations registrations) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        var unkeyed = registrations.Services.Where(descriptor => !descriptor.IsKeyedService).ToArray();

        if (unkeyed.Where(descriptor => descriptor.ServiceType == typeof(IIntegrationEventPublisher)).ToArray()
            is not [{ ImplementationType: var publisher }] || publisher != typeof(LocalInboxIntegrationEventPublisher))
        {
            throw new InvalidOperationException(
                "Local inbox delivery must be the only unkeyed integration-event publisher. " +
                "Remove the broker or custom publisher registration, or disable local inbox delivery.");
        }

        if (!unkeyed.Any(descriptor => descriptor.ServiceType == typeof(IInboxDispatcher)))
            throw new InvalidOperationException("Local inbox delivery requires an unkeyed IInboxDispatcher registration.");

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
