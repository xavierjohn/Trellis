namespace Trellis.Yarp;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Authorization;

/// <summary>
/// Validates at host start that an <see cref="IActorProvider"/> is registered. The
/// YARP actor-forwarding transform resolves <see cref="IActorProvider"/> per request;
/// without a registered provider, every inbound request would fail with the generic
/// "no service registered" message from <see cref="System.IServiceProvider"/>. Running
/// the check at startup turns a per-request runtime failure into a fail-fast host
/// startup error pointing the operator at the exact misconfiguration.
/// </summary>
/// <remarks>
/// Pattern mirrors <c>WorkerActorRegistrationValidator</c> in
/// <c>Trellis.Asp.Authorization</c> — both are hosted-lifecycle services that
/// reach into the root services to assert a composition invariant before the host
/// starts accepting traffic.
/// </remarks>
internal sealed class TrellisActorForwardingRegistrationValidator(IServiceProvider rootServices)
    : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        using var scope = rootServices.CreateScope();
        var provider = scope.ServiceProvider.GetService<IActorProvider>();

        if (provider is null)
        {
            throw new InvalidOperationException(
                "AddTrellisActorForwarding requires an IActorProvider to be registered in the same service collection. " +
                "The YARP per-request transform resolves IActorProvider on every request to hydrate the Actor that gets " +
                "minted into the forwarded JWT. The gateway typically uses AddClaimsActorProvider or AddEntraActorProvider " +
                "from Trellis.Asp to hydrate the actor from the upstream JWT (the JWT the gateway validated at its boundary). " +
                "Add one of those actor-provider registrations to services BEFORE app.MapReverseProxy() accepts traffic.");
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
