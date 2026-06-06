namespace Trellis.Yarp;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Authorization;

/// <summary>
/// Validates at host start that EXACTLY ONE <see cref="IActorProvider"/> is registered.
/// The YARP actor-forwarding transform resolves <see cref="IActorProvider"/> per request;
/// without a registered provider, every inbound request would fail with the generic
/// "no service registered" message from <see cref="System.IServiceProvider"/>. Running
/// the check at startup turns a per-request runtime failure into a fail-fast host
/// startup error pointing the operator at the exact misconfiguration.
/// </summary>
/// <remarks>
/// Pattern mirrors <c>WorkerActorRegistrationValidator</c> in
/// <c>Trellis.Asp.Authorization</c> — both are hosted-lifecycle services that
/// reach into the root services to assert a composition invariant before the host
/// starts accepting traffic. The single-slot enforcement (count == 1, not just
/// count >= 1) is critical: with multiple <see cref="IActorProvider"/> descriptors
/// registered, <see cref="ServiceProviderServiceExtensions.GetRequiredService"/>
/// returns the LAST one, which silently picks an unintended provider and changes the
/// minted Actor / JWT contract — a hard-to-debug authorization regression. Consumers
/// using <c>AddTrellis(o =&gt; o.UseClaimsActorProvider(...))</c> get this protection
/// from <c>TrellisServiceBuilder</c>'s single-slot policy, but consumers calling
/// <see cref="TrellisActorForwardingServiceCollectionExtensions.AddTrellisActorForwarding"/>
/// directly bypass that gate. This validator restores the invariant for that path.
/// </remarks>
internal sealed class TrellisActorForwardingRegistrationValidator(IServiceProvider rootServices)
    : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        using var scope = rootServices.CreateScope();
        var providers = scope.ServiceProvider.GetServices<IActorProvider>().ToList();

        if (providers.Count == 0)
        {
            throw new InvalidOperationException(
                "AddTrellisActorForwarding requires an IActorProvider to be registered in the same service collection. " +
                "The YARP per-request transform resolves IActorProvider on every request to hydrate the Actor that gets " +
                "minted into the forwarded JWT. The gateway typically uses AddClaimsActorProvider or AddEntraActorProvider " +
                "from Trellis.Asp to hydrate the actor from the upstream JWT (the JWT the gateway validated at its boundary). " +
                "Add one of those actor-provider registrations to services BEFORE app.MapReverseProxy() accepts traffic.");
        }

        if (providers.Count > 1)
        {
            // GetRequiredService<IActorProvider>() resolves the LAST registered descriptor;
            // surface that one in the error so operators know exactly which provider would
            // have been used silently.
            var resolvedType = providers[^1].GetType().Name;
            var allTypes = string.Join(", ", providers.Select(p => p.GetType().Name));
            throw new InvalidOperationException(
                $"AddTrellisActorForwarding requires EXACTLY ONE IActorProvider to be registered. " +
                $"Found {providers.Count} registrations [{allTypes}]; the YARP transform would silently use '{resolvedType}' " +
                "(the LAST registered descriptor — DI's GetRequiredService<T> semantics). With multiple actor providers " +
                "registered, the minted Actor surface (id, permissions, forbidden permissions, attributes) depends on " +
                "registration order — a fragile and hard-to-debug authorization regression. Remove the extra registrations " +
                "or use TrellisServiceBuilder (via AddTrellis(o => o.UseClaimsActorProvider(...))) which enforces single-slot " +
                "actor-provider selection at composition time.");
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
