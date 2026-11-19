namespace Trellis.Mediator;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal sealed class LocalInboxDeliveryRegistrationValidator(IServiceScopeFactory scopeFactory) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            if (scope.ServiceProvider.GetServices<IIntegrationEventPublisher>().ToArray()
                is not [LocalInboxIntegrationEventPublisher])
            {
                throw new InvalidOperationException(
                    "Local inbox delivery must be the only unkeyed integration-event publisher. " +
                    "Remove the broker or custom publisher registration, or disable local inbox delivery.");
            }

            if (scope.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(IInboxDispatcher)) == false)
                throw new InvalidOperationException("Local inbox delivery requires an unkeyed IInboxDispatcher registration.");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}