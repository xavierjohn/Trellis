namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>Describes the named destination of a pagination URL.</summary>
public sealed class PageUrlRouteContext
{
    internal PageUrlRouteContext(
        HttpContext httpContext,
        string routeName,
        IReadOnlyList<Endpoint> candidates,
        RouteValueDictionary routeValues)
    {
        HttpContext = httpContext;
        RouteName = routeName;
        Candidates = candidates;
        RouteValues = routeValues;
    }

    /// <summary>The request generating the pagination link.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The name of the destination route.</summary>
    public string RouteName { get; }

    /// <summary>Link-enabled destinations with equivalent URL generation, allowing non-URL MVC selector metadata to differ.</summary>
    public IReadOnlyList<Endpoint> Candidates { get; }

    /// <summary>
    /// The per-link copy of the callback's route values. Mutations affect URL generation
    /// without changing the callback's original dictionary.
    /// </summary>
    public RouteValueDictionary RouteValues { get; }
}
