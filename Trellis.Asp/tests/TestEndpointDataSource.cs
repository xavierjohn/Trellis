namespace Trellis.Asp.Tests;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;

internal sealed class TestEndpointDataSource : EndpointDataSource
{
    private readonly List<Endpoint> _endpoints = new();

    public void AddNamedRoute(string name, string pattern)
    {
        var eb = new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(pattern),
            order: 0);
        eb.Metadata.Add(new RouteNameMetadata(name));
        eb.Metadata.Add(new EndpointNameMetadata(name));
        _endpoints.Add(eb.Build());
    }

    public override IReadOnlyList<Endpoint> Endpoints => _endpoints;

    public override IChangeToken GetChangeToken()
        => new CancellationChangeToken(CancellationToken.None);
}