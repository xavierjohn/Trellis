namespace Trellis.Yarp;

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;

/// <summary>
/// <see cref="IEndpointConventionBuilder"/> that fans every chained convention out to
/// a fixed list of inner builders. Used by
/// <see cref="TrellisDiscoveryEndpointRouteBuilderExtensions.MapTrellisDiscoveryEndpoint"/>
/// so that calls like
/// <c>app.MapTrellisDiscoveryEndpoint().WithTags("discovery").RequireHost("gateway.internal")</c>
/// apply to BOTH the OIDC discovery endpoint AND the JWKS endpoint, not just the
/// first one. Returning a single inner builder would silently misconfigure the second
/// endpoint — a footgun for any consumer chaining endpoint metadata.
/// </summary>
internal sealed class CompositeEndpointConventionBuilder(IReadOnlyList<IEndpointConventionBuilder> inner)
    : IEndpointConventionBuilder
{
    private readonly IReadOnlyList<IEndpointConventionBuilder> _inner = inner
        ?? throw new ArgumentNullException(nameof(inner));

    public void Add(Action<EndpointBuilder> convention)
    {
        ArgumentNullException.ThrowIfNull(convention);
        foreach (var builder in _inner)
            builder.Add(convention);
    }

    public void Finally(Action<EndpointBuilder> finallyConvention) // stale-doc-ok: IEndpointConventionBuilder.Finally (ASP.NET), not removed Trellis Result.Finally
    {
        ArgumentNullException.ThrowIfNull(finallyConvention);
        foreach (var builder in _inner)
            builder.Finally(finallyConvention); // stale-doc-ok: IEndpointConventionBuilder.Finally (ASP.NET), not removed Trellis Result.Finally
    }
}
