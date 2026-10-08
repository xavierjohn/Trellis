namespace Trellis.Asp.ApiVersioning;

using global::Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

internal static class VersionedPageUrlResolver
{
    internal static Endpoint Resolve(PageUrlRouteContext context, ApiVersion? explicitVersion = null, string? pinnedValue = null)
    {
        var target = FindTarget(context, explicitVersion);
        var values = context.RouteValues;
        if (explicitVersion is not null)
        {
            if (HttpResponseOptionsBuilderApiVersioningExtensions.RouteTemplateContainsVersionToken(target))
                throw new InvalidOperationException(
                    $"PageUrl: cannot pin api-version='{pinnedValue}' on route '{context.RouteName}' because it " +
                    "uses URL-segment versioning. Drop the explicit version and use the per-request overload " +
                    "PageUrl(routeName, routeValues), which uses ambient segment routing.");

            if (!HttpResponseOptionsBuilderApiVersioningExtensions.ShouldSkipInjection(target))
            {
                var metadata = target.Metadata.GetMetadata<ApiVersionMetadata>()!;
                if (!HttpResponseOptionsBuilderApiVersioningExtensions.TargetDeclaresVersion(metadata, explicitVersion))
                    throw new InvalidOperationException(
                        $"PageUrl: api-version='{pinnedValue}' is not declared by the target endpoint " +
                        $"for route '{context.RouteName}'. Pin a version the target declares, or use the per-request overload.");
                values[HttpResponseOptionsBuilderApiVersioningExtensions.DefaultRouteValueKey] =
                    pinnedValue ?? explicitVersion.ToString();
            }
        }
        else
        {
            if (!values.ContainsKey(HttpResponseOptionsBuilderApiVersioningExtensions.DefaultRouteValueKey))
            {
                var resolved = HttpResponseOptionsBuilderApiVersioningExtensions.ResolveApiVersion(
                    context.HttpContext, target, callerLabel: "the next-page URL",
                    explicitOverloadHint: "PageUrl(routeName, ApiVersion, ...)");
                if (resolved is not null)
                    values[HttpResponseOptionsBuilderApiVersioningExtensions.DefaultRouteValueKey] = resolved;
            }

            HttpContextPageUrlExtensions.ValidateUrlSegmentCrossRouteOrThrow(
                context.HttpContext, target, context.RouteName, values);
        }

        return target;
    }

    private static Endpoint FindTarget(PageUrlRouteContext context, ApiVersion? explicitVersion)
    {
        var current = context.HttpContext.GetEndpoint();
        if (explicitVersion is null && current is not null
            && string.Equals(current.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName, context.RouteName, StringComparison.Ordinal)
            && current.Metadata.GetMetadata<ISuppressLinkGenerationMetadata>()?.SuppressLinkGeneration != true)
            return current;
        if (context.Candidates.Count == 1)
            return context.Candidates[0];

        var requested = explicitVersion ?? ResolveTargetVersion(context);
        if (requested is not null
            && FindUniqueVersionTarget(context, requested) is { } requestedTarget)
            return requestedTarget;

        if (explicitVersion is null
            && context.HttpContext.RequestServices.GetService<IOptions<ApiVersioningOptions>>()?.Value.DefaultApiVersion is { } defaultVersion
            && FindUniqueVersionTarget(context, defaultVersion) is { } defaultTarget)
            return defaultTarget;

        throw new InvalidOperationException(
            $"PageUrl: route name '{context.RouteName}' is ambiguous: no requested, pinned, or configured default " +
            "API version selects a unique destination. Supply a version mapped to one destination, " +
            "or give the destinations distinct route names.");
    }

    private static Endpoint? FindUniqueVersionTarget(PageUrlRouteContext context, ApiVersion version)
    {
        Endpoint? match = null;
        foreach (var candidate in context.Candidates)
        {
            var metadata = candidate.Metadata.GetMetadata<ApiVersionMetadata>();
            if (metadata is not null && !metadata.IsApiVersionNeutral
                && !HttpResponseOptionsBuilderApiVersioningExtensions.TargetDeclaresVersion(metadata, version))
                continue;
            if (match is not null)
                throw new InvalidOperationException(
                    $"PageUrl: route name '{context.RouteName}' is ambiguous for api-version='{version}': " +
                    "more than one link-enabled endpoint accepts it. Give these destinations distinct route names.");
            match = candidate;
        }

        return match;
    }

    private static ApiVersion? ResolveTargetVersion(PageUrlRouteContext context)
    {
        var segmentKey = HttpResponseOptionsBuilderApiVersioningExtensions
            .TryGetUrlSegmentVersionParameterName(context.Candidates[0]);
        var key = segmentKey ?? HttpResponseOptionsBuilderApiVersioningExtensions.DefaultRouteValueKey;
        if (!context.RouteValues.TryGetValue(key, out var rawVersion) && segmentKey is not null)
            context.HttpContext.Request.RouteValues.TryGetValue(segmentKey, out rawVersion);

        var parser = context.HttpContext.RequestServices.GetService<IApiVersionParser>() ?? ApiVersionParser.Default;
        return parser.TryParse(rawVersion?.ToString(), out var parsed) ? parsed : context.HttpContext.RequestedApiVersion;
    }
}
