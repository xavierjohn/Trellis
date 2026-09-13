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
        string description;
        if (context.RouteName is { } routeName)
        {
            candidates = context.HttpContext.RequestServices
                .GetRequiredService<IEndpointAddressScheme<string>>()
                .FindEndpoints(routeName);
            description = $"named route '{routeName}'";
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
            description = $"action '{context.ActionName}' on controller '{values["controller"]}'";
        }

        Endpoint? match = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Metadata.GetMetadata<ISuppressLinkGenerationMetadata>()?.SuppressLinkGeneration == true)
                continue;
            if (match is not null)
                throw new InvalidOperationException(
                    $"WithVersionedRoute: the Location destination {description} is ambiguous. Use a uniquely named destination route.");
            match = candidate;
        }

        return match ?? throw new InvalidOperationException(
            $"WithVersionedRoute: no registered endpoint matches the Location destination {description}. Register the destination with endpoint routing.");
    }
}
