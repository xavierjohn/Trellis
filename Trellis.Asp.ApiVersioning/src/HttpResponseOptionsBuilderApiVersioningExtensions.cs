namespace Trellis.Asp.ApiVersioning;

using System;
using global::Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis.Asp;

/// <summary>
/// API-versioning extensions on <see cref="HttpResponseOptionsBuilder{TDomain}"/> that auto-inject
/// a target-supported version into <c>Location</c> headers. Chain after any builder method that
/// generates a <c>Location</c> header — <c>CreatedAtRoute(...)</c>, <c>CreatedAtAction(...)</c>,
/// <c>WithLocation(...)</c>, etc.
/// </summary>
/// <remarks>
/// <para>
/// Resolution order (per request, inside the <c>LinkGenerator</c> callback):
/// </para>
/// <list type="number">
///   <item><description><c>HttpContext.RequestedApiVersion</c>, if mapped to the destination endpoint.</description></item>
///   <item><description>Exactly one version mapped to the destination, respecting action-level mappings.</description></item>
///   <item><description><c>ApiVersioningOptions.DefaultApiVersion</c>, only if mapped to the destination; otherwise throw <see cref="InvalidOperationException"/>.</description></item>
/// </list>
/// <para>
/// Query-style injection uses <c>"api-version"</c>, matching the default for
/// <c>QueryStringApiVersionReader</c> and the conventional header name. Hosts using a
/// non-default reader parameter name should register a custom resolver via
/// <see cref="HttpResponseOptionsBuilder{TDomain}.WithRouteValueResolver"/> directly.
/// </para>
/// <para>
/// Both overloads resolve the final named-route or MVC-action destination using endpoint routing.
/// Missing or ambiguous destinations throw. Explicit pins must be mapped to the destination.
/// URL-segment destinations receive the version in their actual <c>:apiVersion</c> parameter,
/// not a duplicate query parameter. Neutral targets and targets without
/// <see cref="ApiVersionMetadata"/> omit version injection and remove a supplied <c>api-version</c>.
/// The missing-metadata diagnostic identifies the destination endpoint.
/// </para>
/// </remarks>
public static class HttpResponseOptionsBuilderApiVersioningExtensions
{
    /// <summary>
    /// Injects a destination-supported version into the <c>Location</c> header
    /// emitted by a preceding <see cref="HttpResponseOptionsBuilder{TDomain}.CreatedAtRoute(string, Func{TDomain, RouteValueDictionary})"/>,
    /// <see cref="HttpResponseOptionsBuilder{TDomain}.CreatedAtAction(string, Func{TDomain, RouteValueDictionary}, string?)"/>
    /// or <see cref="HttpResponseOptionsBuilder{TDomain}.WithLocation(string, Func{TDomain, RouteValueDictionary})"/>
    /// call. The version is resolved per-request from <see cref="HttpContext"/>.
    /// </summary>
    /// <typeparam name="TDomain">The domain value type from <c>Result&lt;TDomain&gt;</c>.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <remarks>
    /// See the type-level remarks on <see cref="HttpResponseOptionsBuilderApiVersioningExtensions"/>
    /// for resolution and skip rules. Uses the target's URL-segment parameter when present,
    /// otherwise <c>"api-version"</c>. Repeated calls replace the previous Location-route resolver.
    /// </remarks>
    public static HttpResponseOptionsBuilder<TDomain> WithVersionedRoute<TDomain>(
        this HttpResponseOptionsBuilder<TDomain> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithLocationRouteResolver(context => VersionedLocationResolver.Apply(context, null));
    }

    /// <summary>
    /// Escape hatch: pin the <c>Location</c> header to a specific <see cref="ApiVersion"/>
    /// regardless of what the client requested. Use for cross-version <c>Location</c> redirects
    /// on deprecated endpoints.
    /// </summary>
    /// <typeparam name="TDomain">The domain value type from <c>Result&lt;TDomain&gt;</c>.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="explicitVersion">The destination-supported version to inject regardless of the requested version.</param>
    /// <remarks>
    /// Pins the query version or actual URL-segment parameter. Unsupported pins throw
    /// <see cref="InvalidOperationException"/>. Neutral and unversioned destinations omit the pin.
    /// Repeated calls replace the previous Location-route resolver.
    /// </remarks>
    public static HttpResponseOptionsBuilder<TDomain> WithVersionedRoute<TDomain>(
        this HttpResponseOptionsBuilder<TDomain> builder,
        ApiVersion explicitVersion)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(explicitVersion);

        return builder.WithLocationRouteResolver(context => VersionedLocationResolver.Apply(context, explicitVersion));
    }

    /// <summary>
    /// Default route-value key used to inject the resolved API version into the
    /// <see cref="RouteValueDictionary"/>. Matches <c>QueryStringApiVersionReader.DefaultParameterName</c>
    /// and the conventional <c>api-version</c> header name. Hosts using a non-default reader
    /// parameter name should bypass this helper and register a resolver via
    /// <see cref="HttpResponseOptionsBuilder{TDomain}.WithRouteValueResolver"/> directly.
    /// </summary>
    internal const string DefaultRouteValueKey = "api-version";

    /// <summary>
    /// Resolves the api-version to inject into a route-value dictionary. Inspects the
    /// <paramref name="targetEndpoint"/> for skip/declared-version checks, but reads the
    /// per-request signals (<c>httpContext.GetRequestedApiVersion()</c>,
    /// <c>ApiVersioningOptions.DefaultApiVersion</c>) from <paramref name="httpContext"/>.
    /// </summary>
    /// <remarks>
    /// Resolution order (when not skipped):
    /// (1) <c>httpContext.RequestedApiVersion</c>, only when mapped to the destination.
    /// (2) Single mapped version, respecting explicit action-level mappings.
    /// (3) <c>ApiVersioningOptions.DefaultApiVersion</c> — host-level fallback.
    /// (4) Throws <see cref="InvalidOperationException"/> — silent picking would resurrect the original 404 bug.
    /// Returns <c>null</c> when <see cref="ShouldSkipInjection"/> reports the target is version-neutral,
    /// URL-segment-versioned, or has no <see cref="ApiVersionMetadata"/> attached (the host did not call
    /// <c>AddApiVersioning(...)</c> for this endpoint) — callers must NOT inject a route value in any of those cases.
    /// </remarks>
    /// <param name="httpContext">The current request context — supplies per-request signals.</param>
    /// <param name="targetEndpoint">The endpoint whose URL is being built — supplies declared-version metadata and skip signals.</param>
    /// <param name="callerLabel">
    /// Caller-facing label inserted into the multi-version unresolvable error message
    /// (e.g., <c>"the Location header"</c>, <c>"the next-page URL"</c>). Default matches
    /// <see cref="WithVersionedRoute{TDomain}(HttpResponseOptionsBuilder{TDomain})"/>'s context.
    /// </param>
    /// <param name="explicitOverloadHint">
    /// Caller-facing name of the explicit-version overload mentioned in the unresolvable error
    /// message (e.g., <c>"WithVersionedRoute(explicitVersion)"</c>, <c>"PageUrl(routeName, ApiVersion, ...)"</c>).
    /// Default matches <see cref="WithVersionedRoute{TDomain}(HttpResponseOptionsBuilder{TDomain})"/>'s context.
    /// </param>
    internal static string? ResolveApiVersion(
        HttpContext httpContext,
        Endpoint? targetEndpoint,
        string callerLabel = "the Location header",
        string explicitOverloadHint = "WithVersionedRoute(explicitVersion)")
    {
        // Skip injection when ShouldSkipInjection reports a skip case (version-neutral
        // endpoint, URL-segment-versioned route, or — most relevant for unversioned hosts —
        // an endpoint with no ApiVersionMetadata at all). In all three cases injecting an
        // api-version route value would emit a misleading or stale URL artefact.
        if (ShouldSkipInjection(targetEndpoint))
            return null;

        // Past the skip check the metadata is guaranteed non-null; ShouldSkipInjection
        // returns true for the null case. The null-forgiving operator is safe here.
        var metadata = targetEndpoint!.Metadata.GetMetadata<ApiVersionMetadata>()!;

        return ResolveDeclaredApiVersion(httpContext, metadata, callerLabel, explicitOverloadHint);
    }

    internal static string ResolveDeclaredApiVersion(
        HttpContext httpContext,
        ApiVersionMetadata metadata,
        string callerLabel,
        string explicitOverloadHint)
    {
        // 1. Echo the version the client requested — primary signal. Only echo when the target
        //    actually declares the requested version. For same-route targets the requested
        //    version is guaranteed to be declared (the route was selected on that basis), so
        //    same-route behaviour is unchanged. For cross-route targets (PageUrl) this prevents
        //    leaking a v2 request into a v1-only target URL.
        var requested = httpContext.RequestedApiVersion;
        if (requested is not null && TargetDeclaresVersion(metadata, requested))
            return requested.ToString();

        var declared = MappedVersions(metadata);
        if (declared.Length == 1)
            return declared[0].ToString();

        // 3. ApiVersioningOptions.DefaultApiVersion — host-level fallback. Validate that the
        //    configured default is declared by the TARGET endpoint before using it. Asp.Versioning
        //    sets DefaultApiVersion to 1.0 even on date-versioned hosts that never explicitly
        //    configured one, so an unvalidated return here would silently emit `?api-version=1.0`
        //    on a date-versioned target that never declares 1.0 — a URL the target rejects when
        //    the client follows it. Step 1 already validates RequestedApiVersion against the
        //    target; step 3 must do the same for the host-level fallback.
        var apiVersioningOptions = httpContext.RequestServices.GetService<IOptions<ApiVersioningOptions>>();
        if (apiVersioningOptions?.Value.DefaultApiVersion is { } defaultVersion
            && TargetDeclaresVersion(metadata, defaultVersion))
            return defaultVersion.ToString();

        // 4. No way to resolve — fail loudly. Silent picking would resurrect the original 404 bug.
        throw new InvalidOperationException(
            $"Cannot determine the api-version for {callerLabel}: the request did not specify a " +
            "version this endpoint declares, the endpoint declares more than one (or none), and " +
            "no ApiVersioningOptions.DefaultApiVersion this endpoint declares is configured. " +
            "Either configure ApiVersioningOptions.DefaultApiVersion to a version this endpoint " +
            $"declares, or use the explicit-version {explicitOverloadHint} overload.");
    }

    /// <summary>
    /// Returns whether the target is mapped to the version, including explicit action mappings.
    /// </summary>
    internal static bool TargetDeclaresVersion(ApiVersionMetadata metadata, ApiVersion requested) =>
        metadata.IsMappedTo(requested);

    /// <summary>
    /// Uses Asp.Versioning's combined mapping so explicit action declarations take precedence.
    /// </summary>
    private static ApiVersion[] MappedVersions(ApiVersionMetadata metadata) =>
        metadata.Map(ApiVersionMapping.Implicit | ApiVersionMapping.Explicit).DeclaredApiVersions
            .Where(metadata.IsMappedTo).ToArray();

    /// <summary>
    /// Returns <c>true</c> when the api-version resolver must NOT inject a route value for the
    /// given endpoint, regardless of which resolution path would otherwise produce a candidate.
    /// Used by <c>PageUrl</c>; Location generation instead fills URL-segment parameters explicitly.
    /// </summary>
    internal static bool ShouldSkipInjection(Endpoint? endpoint)
    {
        var metadata = endpoint?.Metadata.GetMetadata<ApiVersionMetadata>();

        // No metadata to drive injection — either the endpoint argument is null (unrouted
        // request: caller invoked the resolver before UseRouting matched, or under test) or
        // the target endpoint carries no ApiVersionMetadata (the host never called
        // AddApiVersioning(), or this endpoint sits outside its surface). In both cases skip
        // silently rather than echoing a RequestedApiVersion value the target can't
        // interpret, falling through to the multi-version unresolvable branch with advice
        // (configure DefaultApiVersion / use the explicit overload) that does not apply
        // when versioning is not in use, or emitting a pinned version as a stale query
        // parameter the (absent or unrelated) versioning middleware would never consume.
        // Lets the helpers compose cleanly in unversioned and mixed-versioned hosts.
        if (metadata is null)
            return true;

        // Version-neutral endpoints reject any api-version parameter; emitting one would
        // mislead clients into resending the same Location with an unsupported value.
        if (metadata.IsApiVersionNeutral)
            return true;

        // URL-segment versioning embeds the version in the path; ambient routing fills the
        // segment from RouteData, and a query-string copy would create a redundant /
        // conflicting parameter on the URI.
        if (RouteTemplateContainsVersionToken(endpoint))
            return true;

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the endpoint's route template embeds the api-version as a path
    /// segment via the <c>:apiVersion</c> route constraint (URL-segment versioning). Exposed
    /// internally so cross-route helpers like <c>PageUrl</c> can distinguish "skip is correct"
    /// (version-neutral) from "skip would silently drop a pin" (URL-segment), and react
    /// accordingly.
    /// </summary>
    internal static bool RouteTemplateContainsVersionToken(Microsoft.AspNetCore.Http.Endpoint? endpoint)
    {
        if (endpoint is not Microsoft.AspNetCore.Routing.RouteEndpoint routeEndpoint)
            return false;

        var template = routeEndpoint.RoutePattern.RawText;
        if (string.IsNullOrEmpty(template))
            return false;

        // Recognises the standard URL-segment versioning shape: api/v{version:apiVersion}/...
        // The `:apiVersion` route constraint is the canonical signal — query/header readers don't
        // produce a route token. Match either the parameter name "version" with the apiVersion
        // constraint, or the constraint anywhere in the template.
        return template.Contains(":apiVersion", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the name of the route parameter carrying the <c>:apiVersion</c> constraint on the
    /// endpoint's route pattern, or <c>null</c> when the endpoint is not URL-segment-versioned.
    /// Distinct from <see cref="RouteTemplateContainsVersionToken"/> in that callers needing the
    /// route-value KEY (e.g., to detect a consumer override) get the actual parameter name from
    /// the route pattern rather than hard-coding <c>"version"</c>. The conventional name is
    /// <c>version</c> but the template may use any name (e.g., <c>v{apiVer:apiVersion}</c>).
    /// </summary>
    internal static string? TryGetUrlSegmentVersionParameterName(Microsoft.AspNetCore.Http.Endpoint? endpoint)
    {
        if (endpoint is not Microsoft.AspNetCore.Routing.RouteEndpoint routeEndpoint)
            return null;

        foreach (var parameter in routeEndpoint.RoutePattern.Parameters)
        {
            foreach (var policy in parameter.ParameterPolicies)
            {
                if (string.Equals(policy.Content, "apiVersion", StringComparison.OrdinalIgnoreCase))
                    return parameter.Name;
            }
        }

        return null;
    }
}