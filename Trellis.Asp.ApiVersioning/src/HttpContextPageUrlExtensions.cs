namespace Trellis.Asp.ApiVersioning;

using System;
using global::Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// API-versioning extensions on <see cref="HttpContext"/> that build paginated-list URLs in the
/// shape expected by <c>Trellis.Asp</c>'s paged <c>ToHttpResponse</c> overloads
/// (the <c>nextUrlBuilder</c> parameter, a <see cref="Func{T1, T2, TResult}"/> taking
/// (<see cref="Cursor"/>, <see cref="int"/>) and returning a URL string).
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the resolution and skip rules of
/// <see cref="HttpResponseOptionsBuilderApiVersioningExtensions.WithVersionedRoute{TDomain}(HttpResponseOptionsBuilder{TDomain})"/>
/// for paginated <c>Link</c> / <c>next</c> URLs: query/header versioning round-trips
/// <c>api-version</c> automatically; version-neutral endpoints never receive an
/// <c>api-version</c> parameter; endpoints with no <see cref="ApiVersionMetadata"/> attached
/// (hosts that never called <c>AddApiVersioning(...)</c>) likewise skip injection so
/// <c>PageUrl</c> composes cleanly in unversioned and mixed-versioned hosts. For URL-segment
/// versioning, the per-request overload skips query injection and lets ambient route data
/// fill the path segment; the explicit-version overload throws instead, because silently
/// dropping the pin would let <see cref="Microsoft.AspNetCore.Routing.LinkGenerator"/>
/// emit a URL with the wrong path-segment version.
/// </para>
/// <para>
/// Builds absolute URLs via <c>LinkGenerator.GetUriByRouteValues(HttpContext, ...)</c> so the
/// emitted next-page URL preserves <see cref="HttpRequest.Scheme"/>, <see cref="HttpRequest.Host"/>,
/// and <see cref="HttpRequest.PathBase"/> from the current request.
/// </para>
/// <para>
/// The returned <see cref="Func{T1, T2, TResult}"/> captures the current <see cref="HttpContext"/>
/// and is therefore request-scoped: invoke it during the same request that produced it, before
/// the response completes. Don't hand it to a background task.
/// </para>
/// </remarks>
public static class HttpContextPageUrlExtensions
{
    /// <summary>
    /// Returns a <c>(Cursor, int) -&gt; string</c> builder suitable for the <c>nextUrlBuilder</c>
    /// parameter of <c>Trellis.Asp</c>'s paged <c>ToHttpResponse</c> overloads. Resolves the
    /// <c>api-version</c> route value from the request (per-request mode).
    /// </summary>
    /// <param name="httpContext">The current request context.</param>
    /// <param name="routeName">
    /// The name of the target route (typically the current paginated endpoint's route name).
    /// </param>
    /// <param name="routeValues">
    /// A callback that maps <c>(cursor, appliedLimit)</c> to the route-value dictionary for the
    /// next-page URL. The helper clones this dictionary before injecting <c>api-version</c>, so
    /// you may return a shared instance from the callback without it being mutated.
    /// </param>
    /// <returns>A request-scoped URL builder. Must be invoked during the same request.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="httpContext"/> or <paramref name="routeValues"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="routeName"/> is <c>null</c>, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the returned builder when (a) the target route name resolves to no registered
    /// endpoint, (b) a multi-version endpoint has no client-requested version AND no
    /// <c>DefaultApiVersion</c> is configured, or (c) <c>LinkGenerator</c> returns <c>null</c>
    /// (route + ambient values produced no match).
    /// </exception>
    /// <remarks>
    /// Cross-route caveat: when <paramref name="routeName"/> differs from the current endpoint's
    /// route name, the skip rules and declared-version fallback are evaluated against the
    /// TARGET endpoint, not the current one. The per-request <c>RequestedApiVersion</c>
    /// (when the client supplied one) is echoed only when the target endpoint declares it;
    /// otherwise the resolver falls through to the target's single declared version or to
    /// <c>DefaultApiVersion</c>. This prevents emitting a URL the target would reject when the
    /// client follows it.
    /// <para>
    /// Unversioned-host behaviour: when the target endpoint has no <see cref="ApiVersionMetadata"/>
    /// — the host never called <c>AddApiVersioning()</c>, or the endpoint sits outside its
    /// surface — the helper skips <c>api-version</c> injection silently and emits a clean URL.
    /// This lets <c>PageUrl</c> compose in unversioned hosts and in mixed-versioned hosts where
    /// individual paginated endpoints are not part of the versioning surface.
    /// </para>
    /// </remarks>
    public static Func<Cursor, int, string> PageUrl(
        this HttpContext httpContext,
        string routeName,
        Func<Cursor, int, RouteValueDictionary> routeValues)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ThrowIfRouteNameInvalid(routeName);
        ArgumentNullException.ThrowIfNull(routeValues);

        return (cursor, appliedLimit) =>
        {
            var targetEndpoint = FindEndpointByRouteName(httpContext, routeName)
                ?? throw new InvalidOperationException(
                    $"PageUrl: no registered endpoint has route name '{routeName}'. " +
                    "Ensure the target endpoint is named — for example [HttpGet(Name = \"...\")] on " +
                    "an MVC controller action or .WithName(\"...\") on a minimal-API endpoint — and " +
                    "registered with the routing pipeline (MapControllers(), MapGet(...), " +
                    "MapGroup(...), etc.).");

            var consumerValues = routeValues(cursor, appliedLimit)
                ?? throw new InvalidOperationException(
                    "PageUrl: the routeValues callback returned null. Return a RouteValueDictionary " +
                    "(possibly empty) instead.");

            // Clone before injecting api-version so the consumer's dict — which may be a shared
            // instance returned from a closure — is never mutated. Required by the contract
            // documented on the routeValues parameter.
            var values = new RouteValueDictionary(consumerValues);

            // Consumer-supplied api-version wins over framework injection — gives callers
            // an escape hatch for cross-version Location-like URLs without a separate overload.
            // This escape hatch only applies to query/header-versioned targets where the route
            // value with the "api-version" KEY carries the version; URL-segment validation
            // below runs independently because for URL-segment targets the segment route value
            // (not the query key) is what fills the URL.
            if (!values.ContainsKey(HttpResponseOptionsBuilderApiVersioningExtensions.DefaultRouteValueKey))
            {
                var resolved = HttpResponseOptionsBuilderApiVersioningExtensions.ResolveApiVersion(
                    httpContext,
                    targetEndpoint,
                    callerLabel: "the next-page URL",
                    explicitOverloadHint: "PageUrl(routeName, ApiVersion, ...)");
                if (resolved is not null)
                {
                    values[HttpResponseOptionsBuilderApiVersioningExtensions.DefaultRouteValueKey] = resolved;
                }
            }

            // URL-segment cross-route validation runs independently of the api-version
            // escape hatch above: the query-string key is irrelevant to URL-segment
            // targets, so the escape hatch for those is supplying the segment route value
            // in the callback. LinkGenerator otherwise fills the segment from ambient
            // route data and may emit a version the target does not declare.
            ValidateUrlSegmentCrossRouteOrThrow(httpContext, targetEndpoint, routeName, values);

            var linkGenerator = httpContext.RequestServices.GetRequiredService<LinkGenerator>();
            var url = linkGenerator.GetUriByRouteValues(httpContext, routeName, values);
            if (url is null)
            {
                throw new InvalidOperationException(
                    $"PageUrl: LinkGenerator.GetUriByRouteValues returned null for route '{routeName}'. " +
                    "The supplied route values, combined with the current request's ambient route values, " +
                    "did not match the target route template.");
            }

            return url;
        };
    }

    /// <summary>
    /// Returns a <c>(Cursor, int) -&gt; string</c> builder pinned to a specific
    /// <see cref="ApiVersion"/> regardless of what the client requested. Use for cross-version
    /// list-page URLs (e.g. a deprecated endpoint redirecting paginated traffic to its successor).
    /// </summary>
    /// <param name="httpContext">The current request context.</param>
    /// <param name="routeName">The name of the target route.</param>
    /// <param name="version">The version to inject, regardless of client request or endpoint metadata.</param>
    /// <param name="routeValues">
    /// A callback that maps <c>(cursor, appliedLimit)</c> to the route-value dictionary for the
    /// next-page URL. The helper clones this dictionary before injecting <c>api-version</c>.
    /// </param>
    /// <returns>A request-scoped URL builder. Must be invoked during the same request.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="httpContext"/>, <paramref name="version"/>, or <paramref name="routeValues"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="routeName"/> is <c>null</c>, empty, or whitespace.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown by the returned builder when (a) the target route name resolves to no registered
    /// endpoint, (b) the target route is URL-segment-versioned (route template contains
    /// <c>:apiVersion</c>) — the pin cannot be honoured as a query parameter because the version
    /// belongs in the path segment, and silently dropping the pin would let <c>LinkGenerator</c>
    /// fill the segment from ambient route data and produce a URL with the wrong version; switch
    /// to the per-request overload <see cref="PageUrl(HttpContext, string, Func{Cursor, int, RouteValueDictionary})"/>,
    /// (c) the target endpoint has <see cref="ApiVersionMetadata"/> but does not declare
    /// <paramref name="version"/> among its implicit or explicit declared versions — emitting
    /// the URL would produce a link the target rejects (e.g. <c>400</c> from the versioning
    /// middleware) when the client follows it, or (d) <c>LinkGenerator</c> returns <c>null</c>.
    /// </exception>
    /// <remarks>
    /// The explicit version is still silently suppressed on version-neutral endpoints — emitting
    /// a query <c>api-version</c> on a neutral endpoint would mislead clients into resending the
    /// URL with a parameter the target rejects.
    /// <para>
    /// Unversioned-host behaviour: when the target endpoint has no <see cref="ApiVersionMetadata"/>
    /// — the host never called <c>AddApiVersioning()</c>, or the endpoint sits outside its
    /// surface — the pin is silently suppressed and a clean URL is emitted (the pinned version
    /// has no target declaration set to validate against, and emitting it as a query parameter
    /// would be a stale URL artefact). This lets <c>PageUrl</c> compose in unversioned hosts.
    /// </para>
    /// </remarks>
    public static Func<Cursor, int, string> PageUrl(
        this HttpContext httpContext,
        string routeName,
        ApiVersion version,
        Func<Cursor, int, RouteValueDictionary> routeValues)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ThrowIfRouteNameInvalid(routeName);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(routeValues);

        // Cache the stringified version outside the closure — matches the
        // WithVersionedRoute(ApiVersion) precedent and avoids re-allocating on every call.
        var pinnedValue = version.ToString();

        return (cursor, appliedLimit) =>
        {
            var targetEndpoint = FindEndpointByRouteName(httpContext, routeName)
                ?? throw new InvalidOperationException(
                    $"PageUrl: no registered endpoint has route name '{routeName}'. " +
                    "Ensure the target endpoint is named — for example [HttpGet(Name = \"...\")] on " +
                    "an MVC controller action or .WithName(\"...\") on a minimal-API endpoint — and " +
                    "registered with the routing pipeline (MapControllers(), MapGet(...), " +
                    "MapGroup(...), etc.).");

            // URL-segment-versioned targets carry the version in the path. Silently skipping
            // the pin (the WithVersionedRoute precedent for same-route Location headers) would
            // let LinkGenerator fall back to ambient route data — emitting `/v1/...` even when
            // the caller explicitly pinned v2. That is a wrong URL, not just a missing query
            // parameter. Surface it so the caller switches to the implicit overload (which
            // resolves the segment from ambient route data correctly for same-version targets).
            if (HttpResponseOptionsBuilderApiVersioningExtensions.RouteTemplateContainsVersionToken(targetEndpoint))
            {
                throw new InvalidOperationException(
                    $"PageUrl: cannot pin api-version='{pinnedValue}' on route '{routeName}' because it " +
                    "uses URL-segment versioning (the route template contains a ':apiVersion' constraint). " +
                    "Silently skipping the pin would let LinkGenerator fill the version segment from the " +
                    "current request's ambient route values, producing a URL with the wrong version. " +
                    "Drop the explicit version and use the per-request overload " +
                    "PageUrl(routeName, routeValues) — it resolves the segment from ambient route data " +
                    "correctly for same-version targets and validates cross-route target-version support.");
            }

            var consumerValues = routeValues(cursor, appliedLimit)
                ?? throw new InvalidOperationException(
                    "PageUrl: the routeValues callback returned null. Return a RouteValueDictionary " +
                    "(possibly empty) instead.");

            var values = new RouteValueDictionary(consumerValues);

            // Skip injection silently when ShouldSkipInjection reports a skip case:
            //   - Version-neutral targets (the same precedent as WithVersionedRoute(ApiVersion))
            //     reject api-version parameters entirely; emitting one would mislead clients.
            //   - Targets with no ApiVersionMetadata — the host did not call AddApiVersioning()
            //     or this endpoint sits outside its surface. There is no declared-version set
            //     to validate against, and emitting an api-version parameter would be a stale
            //     URL artefact (the middleware, if installed at all, would never act on it).
            //     Lets PageUrl compose cleanly in unversioned and mixed-versioned hosts.
            // The URL-segment case is rejected earlier with a loud exception — segment routes
            // need a different overload, not silent suppression.
            //
            // When NOT skipped, the pin always wins over any api-version the consumer placed
            // in the callback dictionary. This matches the WithVersionedRoute(explicitVersion)
            // precedent and keeps "pinned" semantics honest: callers who need per-call version
            // selection should use the per-request overload (PageUrl(routeName, routeValues)),
            // which deliberately honors consumer-supplied api-version as an escape hatch.
            if (!HttpResponseOptionsBuilderApiVersioningExtensions.ShouldSkipInjection(targetEndpoint))
            {
                // Past the skip check the metadata is guaranteed non-null (ShouldSkipInjection
                // returns true for the null case). Validate the pinned version is declared so
                // the helper never emits a URL the target rejects (e.g. 400 from the versioning
                // middleware) when the client follows it.
                var targetMetadata = targetEndpoint.Metadata.GetMetadata<ApiVersionMetadata>()!;

                if (!HttpResponseOptionsBuilderApiVersioningExtensions.TargetDeclaresVersion(targetMetadata, version))
                {
                    throw new InvalidOperationException(
                        $"PageUrl: api-version='{pinnedValue}' is not declared by the target endpoint " +
                        $"for route '{routeName}'. The generated URL would be rejected by the target " +
                        "when the client follows it. Pin a version the target declares, or use the " +
                        "per-request overload PageUrl(routeName, routeValues) — it resolves the version " +
                        "from ambient route data and validates target support automatically.");
                }

                values[HttpResponseOptionsBuilderApiVersioningExtensions.DefaultRouteValueKey] = pinnedValue;
            }

            var linkGenerator = httpContext.RequestServices.GetRequiredService<LinkGenerator>();
            var url = linkGenerator.GetUriByRouteValues(httpContext, routeName, values);
            if (url is null)
            {
                throw new InvalidOperationException(
                    $"PageUrl: LinkGenerator.GetUriByRouteValues returned null for route '{routeName}'. " +
                    "The supplied route values, combined with the current request's ambient route values, " +
                    "did not match the target route template.");
            }

            return url;
        };
    }

    private static void ThrowIfRouteNameInvalid(string routeName)
    {
        ArgumentNullException.ThrowIfNull(routeName);
        if (string.IsNullOrWhiteSpace(routeName))
            throw new ArgumentException("Route name must be a non-empty, non-whitespace string.", nameof(routeName));
    }

    private static Endpoint? FindEndpointByRouteName(HttpContext httpContext, string routeName)
    {
        // EndpointDataSource is the supported public API for enumerating registered endpoints
        // (MVC registers a CompositeEndpointDataSource; the framework merges all sources).
        // Walking it is O(endpoints) per call. Paginated controllers are not the hot path of
        // a high-throughput API — typical pages serve tens of items at the cost of a database
        // round-trip, so this lookup is in the noise. Caching would require invalidation
        // semantics for dynamically-modified endpoint sources (development hot-reload, plug-ins),
        // and the corresponding bug class is harder to detect than this lookup is expensive.
        var dataSource = httpContext.RequestServices.GetService<EndpointDataSource>();
        if (dataSource is null)
            return null;

        foreach (var endpoint in dataSource.Endpoints)
        {
            var routeNameMetadata = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.RouteNameMetadata>();
            if (routeNameMetadata is not null
                && string.Equals(routeNameMetadata.RouteName, routeName, StringComparison.Ordinal))
            {
                return endpoint;
            }
        }

        return null;
    }

    internal static void ValidateUrlSegmentCrossRouteOrThrow(
        HttpContext httpContext,
        Endpoint targetEndpoint,
        string routeName,
        RouteValueDictionary values)
    {
        // The cross-route URL-segment trap: ResolveApiVersion correctly skips api-version
        // injection (the segment IS the version), but LinkGenerator then fills the segment
        // from ambient route data. For SAME-route requests that ambient value is by
        // construction a version the route declares (the request matched the route).
        // For CROSS-route targets the ambient value may be a version the target does not
        // declare, producing a URL the target rejects when followed.
        var segmentKey = HttpResponseOptionsBuilderApiVersioningExtensions
            .TryGetUrlSegmentVersionParameterName(targetEndpoint);
        if (segmentKey is null)
            return;

        // Escape hatch: consumer supplied the segment value explicitly in the routeValues
        // callback. Honour it without validation — the consumer has taken ownership.
        if (values.ContainsKey(segmentKey))
            return;

        var targetMetadata = targetEndpoint.Metadata.GetMetadata<ApiVersionMetadata>();
        if (targetMetadata is null || targetMetadata.IsApiVersionNeutral)
            return;

        // Determine what LinkGenerator will fill the segment with. It uses ambient
        // Request.RouteValues[segmentKey] when present — RequestedApiVersion is a
        // higher-level concept (parsed by IApiVersionReader) and does not drive
        // segment substitution. So validate ambient first when present; only fall
        // back to RequestedApiVersion when ambient is absent (e.g., a query/header-
        // versioned source request whose route has no parameter named segmentKey).
        // Unparseable ambient is itself a leak: it means the source route has an
        // unrelated parameter colliding with the target's segment name, and
        // LinkGenerator will substitute the literal string into the URL — which the
        // target's apiVersion route constraint will reject when followed. Throw with
        // the same guidance shape so the consumer can disambiguate via the segment
        // escape hatch.
        ApiVersion? versionToValidate;
        if (httpContext.Request.RouteValues.TryGetValue(segmentKey, out var ambientRaw)
            && ambientRaw is string ambientStr
            && !string.IsNullOrEmpty(ambientStr))
        {
            var parser = httpContext.RequestServices.GetService<IApiVersionParser>()
                ?? ApiVersionParser.Default;
            if (!parser.TryParse(ambientStr, out var ambientParsed))
            {
                throw new InvalidOperationException(
                    $"PageUrl: ambient route value '{segmentKey}'='{ambientStr}' is not a valid " +
                    $"ApiVersion, but the URL-segment-versioned target route '{routeName}' has a " +
                    $"'{{{segmentKey}:apiVersion}}' segment. LinkGenerator would substitute the " +
                    "literal ambient value into the URL, producing a URL the target's apiVersion " +
                    "route constraint rejects when the client follows it. Either link to a route " +
                    "whose segment name does not collide with the current request's route " +
                    "parameters, or supply the segment value explicitly in the routeValues " +
                    $"callback (for example `[\"{segmentKey}\"] = \"<a-version-the-target-declares>\"`).");
            }

            versionToValidate = ambientParsed;
        }
        else
        {
            versionToValidate = httpContext.RequestedApiVersion;
        }

        if (versionToValidate is null)
            return;

        if (HttpResponseOptionsBuilderApiVersioningExtensions.TargetDeclaresVersion(targetMetadata, versionToValidate))
            return;

        throw new InvalidOperationException(
            $"PageUrl: the current request's api-version='{versionToValidate}' is not declared by the " +
            $"URL-segment-versioned target route '{routeName}'. LinkGenerator would fill the " +
            $"'{{{segmentKey}:apiVersion}}' route segment from ambient route data, emitting a URL " +
            "the target rejects when the client follows it. Either link to a route that declares " +
            "the current request's version, or supply the segment value explicitly in the " +
            $"routeValues callback (for example `[\"{segmentKey}\"] = \"<a-version-the-target-declares>\"`).");
    }
}