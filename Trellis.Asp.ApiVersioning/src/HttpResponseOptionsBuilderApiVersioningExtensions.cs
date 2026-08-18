namespace Trellis.Asp.ApiVersioning;

using System;
using System.Collections.Generic;
using global::Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Trellis.Asp;

/// <summary>
/// API-versioning extensions on <see cref="HttpResponseOptionsBuilder{TDomain}"/> that auto-inject
/// the <c>api-version</c> route value into <c>Location</c> headers so responses round-trip the
/// requested version under query/header API versioning. Chain after any builder method that
/// generates a <c>Location</c> header — <c>CreatedAtRoute(...)</c>, <c>CreatedAtAction(...)</c>,
/// <c>WithLocation(...)</c>, etc.
/// </summary>
/// <remarks>
/// <para>
/// Resolution order (per request, inside the <c>LinkGenerator</c> callback):
/// </para>
/// <list type="number">
///   <item><description><c>HttpContext.RequestedApiVersion</c> — primary signal; reflects whatever the configured <c>IApiVersionReader</c> parsed (query, header, media-type, URL segment, composite).</description></item>
///   <item><description>Endpoint metadata <c>ApiVersionMetadata.Map(ApiVersionMapping.Implicit).DeclaredApiVersions</c> — fallback when (1) is null and exactly one declared version exists.</description></item>
///   <item><description><c>ApiVersioningOptions.DefaultApiVersion</c> — final fallback, configured via <c>services.AddApiVersioning(o =&gt; o.DefaultApiVersion = ...)</c>.</description></item>
/// </list>
/// <para>
/// The route-value key is fixed at <c>"api-version"</c>, matching the default for
/// <c>QueryStringApiVersionReader</c> and the conventional header name. Hosts using a
/// non-default reader parameter name should register a custom resolver via
/// <see cref="HttpResponseOptionsBuilder{TDomain}.WithRouteValueResolver"/> directly.
/// </para>
/// <para>
/// Cases that skip injection (resolver returns <c>null</c>, applies to both the per-request
/// and explicit-version overloads): version-neutral endpoints
/// (<c>ApiVersionMetadata.IsApiVersionNeutral</c> or <c>[ApiVersionNeutral]</c>),
/// URL-segment-versioned routes (route template contains <c>:apiVersion</c>; ambient
/// routing handles substitution), and endpoints with no <see cref="ApiVersionMetadata"/>
/// attached (hosts that never called <c>AddApiVersioning(...)</c>, or endpoints sitting
/// outside its surface — the helpers compose cleanly in unversioned and mixed-versioned
/// hosts by dropping injection rather than emitting a stale URL artefact). Multi-version
/// actions with no client-requested version AND no <c>DefaultApiVersion</c> throw
/// <see cref="InvalidOperationException"/> rather than silently picking — silent picking
/// would resurrect the original 404 bug.
/// </para>
/// </remarks>
public static class HttpResponseOptionsBuilderApiVersioningExtensions
{
    /// <summary>
    /// Injects the configured <c>api-version</c> route value into the <c>Location</c> header
    /// emitted by a preceding <see cref="HttpResponseOptionsBuilder{TDomain}.CreatedAtRoute(string, Func{TDomain, RouteValueDictionary})"/>,
    /// <see cref="HttpResponseOptionsBuilder{TDomain}.CreatedAtAction(string, Func{TDomain, RouteValueDictionary}, string?)"/>
    /// or <see cref="HttpResponseOptionsBuilder{TDomain}.WithLocation(string, Func{TDomain, RouteValueDictionary})"/>
    /// call. The version is resolved per-request from <see cref="HttpContext"/>.
    /// </summary>
    /// <typeparam name="TDomain">The domain value type from <c>Result&lt;TDomain&gt;</c>.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <remarks>
    /// See the type-level remarks on <see cref="HttpResponseOptionsBuilderApiVersioningExtensions"/>
    /// for the version-resolution order and skip rules. The route-value key is fixed at
    /// <c>"api-version"</c> — matches Asp.Versioning's defaults.
    /// </remarks>
    public static HttpResponseOptionsBuilder<TDomain> WithVersionedRoute<TDomain>(
        this HttpResponseOptionsBuilder<TDomain> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Resolution happens per-request from HttpContext (inside the resolver delegate);
        // only the route-value key is fixed at registration time. The key is the constant
        // `DefaultRouteValueKey` ("api-version") — no IOptions lookup is involved for the key.
        // `httpContext.GetEndpoint()` returns the endpoint currently being executed. For
        // same-route Location responses (the common case) that endpoint IS the target of the
        // Location header. For cross-route Location helpers (e.g. CreatedAtRoute pointing at
        // a sibling route) it is the best signal the resolver has at registration time —
        // declared-version inspection of the actual target route requires the route name and
        // happens in PageUrl(...) instead, which takes the route name as a parameter.
        return builder.WithRouteValueResolver(
            DefaultRouteValueKey,
            httpContext =>
            {
                var endpoint = httpContext.GetEndpoint();
                // Mid-migration safety net: warn once (or fail fast, opt-in) when this chain
                // runs against an endpoint with no ApiVersionMetadata — the canonical
                // "AddApiVersioning() was removed but WithVersionedRoute() chains remain"
                // regression. Scoped to the missing-metadata case; [ApiVersionNeutral] and
                // URL-segment skips stay silent.
                SilentVersionInjectionDiagnostic.EmitIfMetadataMissing(httpContext, endpoint);
                return ResolveApiVersion(httpContext, endpoint);
            });
    }

    /// <summary>
    /// Escape hatch: pin the <c>Location</c> header to a specific <see cref="ApiVersion"/>
    /// regardless of what the client requested. Use for cross-version <c>Location</c> redirects
    /// on deprecated endpoints.
    /// </summary>
    /// <typeparam name="TDomain">The domain value type from <c>Result&lt;TDomain&gt;</c>.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="explicitVersion">The version to inject, regardless of client request or endpoint metadata.</param>
    /// <remarks>
    /// Explicit pinning overrides the per-request resolution order (requested / declared /
    /// default), but the skip rules still apply: on a version-neutral endpoint
    /// (<c>ApiVersionMetadata.IsApiVersionNeutral</c> or <c>[ApiVersionNeutral]</c>), on
    /// URL-segment-versioned routes (route template contains <c>:apiVersion</c>), and on
    /// targets with no <see cref="ApiVersionMetadata"/> attached (the host did not call
    /// <c>AddApiVersioning(...)</c>) the resolver returns <c>null</c> and no
    /// <c>api-version</c> route value is injected. Injecting a pinned version into a
    /// Location that targets a neutral or unversioned endpoint would mislead clients or be
    /// a stale URL artefact; injecting it as a query parameter alongside a path segment
    /// would create a redundant / conflicting value. All three bugs are silent without
    /// this guard.
    /// </remarks>
    public static HttpResponseOptionsBuilder<TDomain> WithVersionedRoute<TDomain>(
        this HttpResponseOptionsBuilder<TDomain> builder,
        ApiVersion explicitVersion)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(explicitVersion);

        var pinnedValue = explicitVersion.ToString();
        return builder.WithRouteValueResolver(
            DefaultRouteValueKey,
            httpContext =>
            {
                var endpoint = httpContext.GetEndpoint();
                // Mid-migration safety net (mirrors the per-request overload): emit a single
                // warning (or fail fast when TrellisAspOptions.FailFastOnSilentVersionInjection
                // is set) when the explicit pin would be silently dropped because the endpoint
                // carries no ApiVersionMetadata.
                SilentVersionInjectionDiagnostic.EmitIfMetadataMissing(httpContext, endpoint);
                return ShouldSkipInjection(endpoint) ? null : pinnedValue;
            });
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
    /// (1) <c>httpContext.RequestedApiVersion</c> — primary signal, only echoed when the target
    ///     endpoint declares that version. For same-route helpers (<c>WithVersionedRoute</c>)
    ///     the target IS the current endpoint, so the requested version is always declared.
    ///     For cross-route helpers (<c>PageUrl</c>) the requested version may not be in the
    ///     target's declared set; echoing it would emit a URL the target immediately rejects.
    /// (2) Single declared version on <paramref name="targetEndpoint"/> — unambiguous fallback.
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

        // 1. Echo the version the client requested — primary signal. Only echo when the target
        //    actually declares the requested version. For same-route targets the requested
        //    version is guaranteed to be declared (the route was selected on that basis), so
        //    same-route behaviour is unchanged. For cross-route targets (PageUrl) this prevents
        //    leaking a v2 request into a v1-only target URL.
        var requested = httpContext.RequestedApiVersion;
        if (requested is not null && TargetDeclaresVersion(metadata, requested))
            return requested.ToString();

        // 2. Single declared version on the endpoint metadata — unambiguous fallback.
        //    Use the union of Implicit ∪ Explicit DeclaredApiVersions for the count, matching
        //    TargetDeclaresVersion. Otherwise a controller with `[ApiVersion(v1)]` + an action
        //    `[MapToApiVersion(v2)]` (1 implicit + 1 explicit) would look "single-declared" via
        //    Implicit alone and silently echo v1, even though the target declares two versions.
        var declared = DistinctDeclaredVersions(metadata);
        if (declared.Count == 1)
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
    /// Returns <c>true</c> when <paramref name="metadata"/>'s union of implicit + explicit declared
    /// versions contains <paramref name="requested"/>. The union is necessary because Asp.Versioning
    /// splits declarations by mapping kind: a multi-version controller's secondary versions appear
    /// under <see cref="ApiVersionMapping.Explicit"/> while the primary appears under
    /// <see cref="ApiVersionMapping.Implicit"/>.
    /// </summary>
    internal static bool TargetDeclaresVersion(ApiVersionMetadata metadata, ApiVersion requested)
    {
        var implicitVersions = metadata.Map(ApiVersionMapping.Implicit).DeclaredApiVersions;
        if (implicitVersions.Contains(requested))
            return true;

        var explicitVersions = metadata.Map(ApiVersionMapping.Explicit).DeclaredApiVersions;
        return explicitVersions.Contains(requested);
    }

    /// <summary>
    /// Returns the distinct union of implicit + explicit <c>DeclaredApiVersions</c> for the given
    /// metadata. Used by the single-declared fallback so a controller that splits its declarations
    /// across mapping kinds (e.g., <c>[ApiVersion(v1)]</c> with action-level <c>[MapToApiVersion(v2)]</c>)
    /// is correctly seen as multi-version instead of looking single-version through one mapping.
    /// </summary>
    private static IReadOnlyList<ApiVersion> DistinctDeclaredVersions(ApiVersionMetadata metadata)
    {
        var implicitVersions = metadata.Map(ApiVersionMapping.Implicit).DeclaredApiVersions;
        var explicitVersions = metadata.Map(ApiVersionMapping.Explicit).DeclaredApiVersions;

        if (explicitVersions.Count == 0)
            return implicitVersions;
        if (implicitVersions.Count == 0)
            return explicitVersions;

        var union = new List<ApiVersion>(implicitVersions.Count + explicitVersions.Count);
        union.AddRange(implicitVersions);
        foreach (var v in explicitVersions)
        {
            if (!union.Contains(v))
                union.Add(v);
        }

        return union;
    }

    /// <summary>
    /// Returns <c>true</c> when the api-version resolver must NOT inject a route value for the
    /// given endpoint, regardless of which resolution path would otherwise produce a candidate.
    /// Centralised so the per-request resolver, the explicit-pinning overload, and the
    /// <c>PageUrl</c> helper share identical skip semantics.
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