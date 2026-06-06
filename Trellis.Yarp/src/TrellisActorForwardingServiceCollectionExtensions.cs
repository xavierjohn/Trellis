namespace Trellis.Yarp;

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using global::Yarp.ReverseProxy.Transforms.Builder;

/// <summary>
/// <see cref="IReverseProxyBuilder"/> extensions that wire the Trellis actor-forwarding
/// transform pipeline. Pair with the consumer-side
/// <c>TrellisInternalJwtActorProvider</c> in <c>Trellis.Asp.Authorization</c> and the
/// strict <c>AddJwtBearer</c> profile in cookbook Recipe 33.
/// </summary>
public static class TrellisActorForwardingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Trellis actor-forwarding YARP transform pipeline. Every request
    /// flowing through YARP's reverse proxy gets a per-cluster transform that hydrates
    /// the current <see cref="Authorization.Actor"/> from the registered
    /// <see cref="Authorization.IActorProvider"/> and mints a fresh internal JWT carrying
    /// the actor surface for the destination cluster. The upstream <c>Authorization</c>
    /// header is overwritten with the gateway-minted JWT.
    /// </summary>
    /// <param name="builder">The YARP reverse-proxy builder.</param>
    /// <param name="configure">Configures the forwarding options. The configured action runs at
    /// startup; <see cref="TrellisActorForwardingOptionsValidator"/> then validates the
    /// resulting options via <c>ValidateOnStart()</c>, so misconfiguration fails the host
    /// at startup rather than per request.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Caller responsibility — register IActorProvider.</b> This extension does NOT
    /// register an <see cref="Authorization.IActorProvider"/>. The gateway typically
    /// uses <c>AddClaimsActorProvider</c> or <c>AddEntraActorProvider</c> from
    /// <c>Trellis.Asp</c> to hydrate the actor from the upstream JWT (the JWT the
    /// gateway validated at its boundary). Calling this extension WITHOUT having
    /// registered an actor provider <b>fails at host startup</b>:
    /// <c>AddTrellisActorForwarding</c> registers
    /// <see cref="TrellisActorForwardingRegistrationValidator"/> as an
    /// <see cref="Microsoft.Extensions.Hosting.IHostedLifecycleService"/> that throws
    /// <see cref="InvalidOperationException"/> in <c>StartingAsync</c> if no
    /// <see cref="Authorization.IActorProvider"/> is registered. That turns what would
    /// otherwise be a per-request "no service registered" error into a clear startup
    /// failure pointing at the exact misconfiguration.
    /// </para>
    /// <para>
    /// <b>YARP composition.</b> Place this call immediately after
    /// <c>services.AddReverseProxy().LoadFromConfig(...)</c> and before
    /// <c>app.MapReverseProxy()</c>. The transform pipeline is built once per cluster at
    /// startup; per-request work resolves a scoped <see cref="Authorization.IActorProvider"/>
    /// and the singleton <see cref="TrellisActorJwtMinter"/>.
    /// </para>
    /// </remarks>
    public static IReverseProxyBuilder AddTrellisActorForwarding(
        this IReverseProxyBuilder builder,
        Action<TrellisActorForwardingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services
            .AddOptions<TrellisActorForwardingOptions>()
            .Configure(configure)
            .ValidateOnStart();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<TrellisActorForwardingOptions>,
            TrellisActorForwardingOptionsValidator>());

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<TrellisActorJwtMinter>();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IHostedService,
            TrellisActorForwardingRegistrationValidator>());

        builder.AddTransforms<TrellisActorForwardingTransformProvider>();

        return builder;
    }
}
