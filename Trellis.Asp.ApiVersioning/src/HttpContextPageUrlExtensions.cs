namespace Trellis.Asp.ApiVersioning;

using global::Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Adds explicitly API-version-pinned pagination builders to the ordinary
/// <see cref="HttpContextPaginationExtensions"/> builders.
/// </summary>
public static class HttpContextPageUrlExtensions
{
    /// <summary>Creates a request-scoped pagination URL builder pinned to a destination-supported version.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="routeName">The named destination route.</param>
    /// <param name="version">The version to pin on versioned destinations.</param>
    /// <param name="routeValues">Maps the cursor and applied limit to route values, which are cloned.</param>
    /// <returns>A builder to invoke during this request.</returns>
    /// <remarks>
    /// The pin overrides consumer query versions on versioned targets. Neutral and unversioned
    /// targets retain consumer values without injection. Explicit URL-segment pins throw.
    /// No host pagination policy registration is required for an explicit pin.
    /// </remarks>
    public static Func<Cursor, int, string> PageUrl(
        this HttpContext httpContext,
        string routeName,
        ApiVersion version,
        Func<Cursor, int, RouteValueDictionary> routeValues)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(routeValues);
        var builder = httpContext.PageUrl(routeName, version, (cursor, _, limit) => routeValues(cursor, limit));
        return (cursor, limit) => builder(cursor, PageDirection.Next, limit);
    }

    /// <summary>Creates a request-scoped, direction-aware pagination builder pinned to an API version.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="routeName">The named destination route.</param>
    /// <param name="version">The version to pin on versioned destinations.</param>
    /// <param name="routeValues">Maps the cursor, direction, and applied limit to route values, which are cloned.</param>
    /// <returns>A builder to invoke during this request.</returns>
    /// <remarks>
    /// Destination selection, consumer-value precedence, neutral/unversioned skip rules,
    /// and rejection of explicit segment pins match the two-argument callback overload.
    /// </remarks>
    public static Func<Cursor, PageDirection, int, string> PageUrl(
        this HttpContext httpContext,
        string routeName,
        ApiVersion version,
        Func<Cursor, PageDirection, int, RouteValueDictionary> routeValues)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(routeValues);
        var pinnedValue = version.ToString();
        return HttpContextPaginationExtensions.PageUrl(
            httpContext, routeName, routeValues,
            context => VersionedPageUrlResolver.Resolve(context, version, pinnedValue));
    }

    internal static void ValidateUrlSegmentCrossRouteOrThrow(
        HttpContext httpContext,
        Endpoint targetEndpoint,
        string routeName,
        RouteValueDictionary values)
    {
        var segmentKey = HttpResponseOptionsBuilderApiVersioningExtensions
            .TryGetUrlSegmentVersionParameterName(targetEndpoint);
        if (segmentKey is null || values.ContainsKey(segmentKey))
            return;

        var targetMetadata = targetEndpoint.Metadata.GetMetadata<ApiVersionMetadata>();
        if (targetMetadata is null || targetMetadata.IsApiVersionNeutral)
            return;

        // LinkGenerator substitutes ambient segment values, not RequestedApiVersion.
        ApiVersion? versionToValidate;
        if (httpContext.Request.RouteValues.TryGetValue(segmentKey, out var ambientRaw)
            && ambientRaw is string ambientStr && !string.IsNullOrEmpty(ambientStr))
        {
            var parser = httpContext.RequestServices.GetService<IApiVersionParser>() ?? ApiVersionParser.Default;
            if (!parser.TryParse(ambientStr, out var ambientParsed))
                throw new InvalidOperationException(
                    $"PageUrl: ambient route value '{segmentKey}'='{ambientStr}' is not a valid " +
                    $"ApiVersion, but the URL-segment-versioned target route '{routeName}' has a " +
                    $"'{{{segmentKey}:apiVersion}}' segment. Supply the segment value explicitly in " +
                    $"the routeValues callback (for example `[\"{segmentKey}\"] = \"<a-version-the-target-declares>\"`).");
            versionToValidate = ambientParsed;
        }
        else
            versionToValidate = httpContext.RequestedApiVersion;

        if (versionToValidate is null
            || HttpResponseOptionsBuilderApiVersioningExtensions.TargetDeclaresVersion(targetMetadata, versionToValidate))
            return;

        throw new InvalidOperationException(
            $"PageUrl: the current request's api-version='{versionToValidate}' is not declared by the " +
            $"URL-segment-versioned target route '{routeName}'. LinkGenerator would fill the " +
            $"'{{{segmentKey}:apiVersion}}' route segment from ambient route data, emitting a URL " +
            "the target rejects when followed. Link to a route that declares the current version, " +
            $"or supply `[\"{segmentKey}\"]` explicitly in the routeValues callback.");
    }
}
