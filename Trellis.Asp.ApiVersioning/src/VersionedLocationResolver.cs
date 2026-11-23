namespace Trellis.Asp.ApiVersioning;

using global::Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp;
using static HttpResponseOptionsBuilderApiVersioningExtensions;

internal static class VersionedLocationResolver
{
    internal static void Apply(LocationRouteContext context, ApiVersion? explicitVersion)
    {
        var target = FindTarget(context);
        SilentVersionInjectionDiagnostic.EmitIfMetadataMissing(context.HttpContext, target);
        var metadata = target.Metadata.GetMetadata<ApiVersionMetadata>();

        context.RouteValues.Remove(DefaultRouteValueKey);
        if (metadata is null || metadata.IsApiVersionNeutral)
            return;

        if (explicitVersion is not null && !TargetDeclaresVersion(metadata, explicitVersion))
            throw new InvalidOperationException(
                $"WithVersionedRoute: api-version='{explicitVersion}' is not declared by the Location target '{target.DisplayName}'.");

        var version = explicitVersion?.ToString()
            ?? ResolveDeclaredApiVersion(context.HttpContext, metadata, "the Location target", "WithVersionedRoute(explicitVersion)");
        var key = TryGetUrlSegmentVersionParameterName(target) ?? DefaultRouteValueKey;
        context.RouteValues[key] = version;
    }

    private static Endpoint FindTarget(LocationRouteContext context)
    {
        IEnumerable<Endpoint> candidates;
        var routeName = context.RouteName;
        object? controller = null;
        string? customActionDescription = null;
        if (routeName is not null)
        {
            candidates = context.HttpContext.RequestServices
                .GetRequiredService<IEndpointAddressScheme<string>>()
                .FindEndpoints(routeName);
        }
        else
        {
            var values = new RouteValueDictionary(context.RouteValues)
            {
                ["action"] = context.ActionName
            };
            if (context.ControllerName is { } controllerName)
                values["controller"] = controllerName;
            else if (!values.ContainsKey("controller")
                && context.HttpContext.Request.RouteValues.TryGetValue("controller", out var currentController))
                values["controller"] = currentController;

            candidates = context.HttpContext.RequestServices
                .GetRequiredService<IEndpointAddressScheme<RouteValuesAddress>>()
                .FindEndpoints(new RouteValuesAddress
                {
                    ExplicitValues = values,
                    AmbientValues = context.HttpContext.Request.RouteValues
                });
            controller = values["controller"];
            // Keep custom route-value formatting before enumeration, even on success.
            if (controller is not null and not string)
                customActionDescription = DescribeAction(context.ActionName, controller);
        }

        Endpoint? match = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Metadata.GetMetadata<ISuppressLinkGenerationMetadata>()?.SuppressLinkGeneration == true)
                continue;
            if (match is not null)
                throw new InvalidOperationException(
                    $"WithVersionedRoute: the Location destination {DescribeDestination()} is ambiguous. Use a uniquely named destination route.");
            match = candidate;
        }

        return match ?? throw new InvalidOperationException(
            $"WithVersionedRoute: no registered endpoint matches the Location destination {DescribeDestination()}. Register the destination with endpoint routing.");

        string DescribeDestination() => customActionDescription ?? (routeName is not null
            ? $"named route '{routeName}'"
            : DescribeAction(context.ActionName, controller));

        static string DescribeAction(string? action, object? controller) =>
            $"action '{action}' on controller '{controller}'";
    }
}