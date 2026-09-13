namespace Trellis.Asp;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Describes the final named-route or MVC-action destination of a generated Location header.
/// Supplied to <c>HttpResponseOptionsBuilder&lt;TDomain&gt;.WithLocationRouteResolver</c> at execution time.
/// </summary>
public sealed class LocationRouteContext
{
    internal LocationRouteContext(
        HttpContext httpContext,
        string? routeName,
        string? actionName,
        string? controllerName,
        RouteValueDictionary routeValues)
    {
        HttpContext = httpContext;
        RouteName = routeName;
        ActionName = actionName;
        ControllerName = controllerName;
        RouteValues = routeValues;
    }

    /// <summary>The request generating the response.</summary>
    public HttpContext HttpContext { get; }

    /// <summary>The named destination, or null for an MVC-action destination.</summary>
    public string? RouteName { get; }

    /// <summary>The destination action name, or null for a named-route destination.</summary>
    public string? ActionName { get; }

    /// <summary>
    /// The explicitly configured destination controller, or null to use route values and then
    /// the current controller. Always null for named-route destinations.
    /// </summary>
    public string? ControllerName { get; }

    /// <summary>
    /// The per-execution copy of the selected route values, after single-key resolvers have run.
    /// Mutations affect link generation without changing the selector's original dictionary.
    /// </summary>
    public RouteValueDictionary RouteValues { get; }
}
