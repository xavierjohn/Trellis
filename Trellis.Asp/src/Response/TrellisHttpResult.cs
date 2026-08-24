namespace Trellis.Asp;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Trellis;

/// <summary>
/// The unified Trellis HTTP response. Implements <see cref="Microsoft.AspNetCore.Http.IResult"/> and the
/// metadata interfaces consumed by OpenAPI/ApiExplorer in both Minimal API and MVC hosts.
/// </summary>
internal sealed class TrellisHttpResult<TDomain, TBody> :
    Microsoft.AspNetCore.Http.IResult,
    IStatusCodeHttpResult,
    IValueHttpResult,
    IValueHttpResult<TBody>,
    IContentTypeHttpResult,
    IEndpointMetadataProvider
{
    // True when TDomain is Unit. The Unit success path emits 204 No Content
    // unconditionally (see ExecuteSuccessAsync below); StatusCode / Value /
    // ContentType / PopulateMetadata must agree so OpenAPI / ApiExplorer
    // consumers see the right success contract.
    private static readonly bool s_isUnit = typeof(TDomain) == typeof(Unit);

    private readonly Result<TDomain> _result;
    private readonly Func<TDomain, TBody>? _bodyProjector;
    private readonly HttpResponseOptions<TDomain> _options;

    public TrellisHttpResult(
        Result<TDomain> result,
        Func<TDomain, TBody>? bodyProjector,
        HttpResponseOptions<TDomain> options)
    {
        _result = result;
        _bodyProjector = bodyProjector;
        _options = options;
    }

    /// <summary>Hint for OpenAPI: the success status code expected on the success path.</summary>
    public int? StatusCode => s_isUnit
        ? StatusCodes.Status204NoContent
        : _options.MarkAsCreated
            ? StatusCodes.Status201Created
            : StatusCodes.Status200OK;

    /// <summary>Hint for OpenAPI: the body value (null on the failure path, or for Result&lt;Unit&gt; success which has no body).</summary>
    public object? Value => s_isUnit
        ? null
        : _result.TryGetValue(out var v)
            ? (_bodyProjector is not null ? (object?)_bodyProjector(v) : v)
            : null;

    TBody? IValueHttpResult<TBody>.Value =>
        _result.TryGetValue(out var v)
            ? (_bodyProjector is not null ? _bodyProjector(v) : v is TBody t ? t : default)
            : default;

    public string? ContentType => s_isUnit ? null : "application/json";

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // Apply actor-vary headers BEFORE branching: cacheable failures (404 from a missing
        // resource the actor cannot see, 403 shaped by visibility, validation 422s, etc.)
        // must partition by actor too. Doing this here also runs the fail-closed validation
        // before any response bytes are written, so misconfiguration surfaces with a clear
        // error rather than a silent cache-poisoning vulnerability.
        if (_options.VaryForActor)
            AppendActorVaryHeaders(httpContext);

        // Static Cache-Control applies to both success and failure: a sensitive endpoint
        // declaring `WithCacheControl(CacheControl.NoStore())` should not leak its 404 / 403
        // through an intermediate cache any more than its 200. The selector overload (which
        // needs a domain value) only fires on the success path inside ApplyMetadata.
        if (_options.CacheControl is { } staticCc)
            httpContext.Response.Headers["Cache-Control"] = staticCc.ToString();

        return _result.IsSuccess
            ? ExecuteSuccessAsync(httpContext)
            : ResponseFailureWriter.WriteAsync(
                httpContext,
                _result.Error!,
                ResolveErrorStatusCode(httpContext, _result.Error!, _options),
                err => ResolveErrorStatusCode(httpContext, err, _options));
    }

    private Task ExecuteSuccessAsync(HttpContext httpContext)
    {
        _result.TryGetValue(out var domain);
        var response = httpContext.Response;

        ApplyMetadata(response, domain!);

        if (_options.HonorPrefer)
            AppendVaryUnique(response, "Prefer");

        // No-payload Result<Unit> success — emit 204 No Content.
        // ETag/LastModified/Vary/ContentLanguage/Prefer headers (above) still apply.
        if (s_isUnit)
        {
            ApplyCacheControlSelector(response, domain!);
            return Results.NoContent().ExecuteAsync(httpContext);
        }

        if (_options.EvaluatePreconditions)
        {
            var method = httpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            {
                var metadata = BuildMetadataForEvaluation(domain!);
                if (metadata is not null)
                {
                    var decision = ConditionalRequestEvaluator.Evaluate(httpContext.Request, metadata, out var failedKind);
                    if (decision == ConditionalDecision.NotModified)
                    {
                        // 304 is a success disposition (the cached representation is still valid) —
                        // apply the selector so the revalidation response carries the same Cache-Control
                        // policy a fresh 200 would.
                        ApplyCacheControlSelector(response, domain!);
                        return Results.StatusCode(StatusCodes.Status304NotModified).ExecuteAsync(httpContext);
                    }

                    if (decision == ConditionalDecision.PreconditionFailed)
                    {
                        var pf = new Error.TransportFault(new HttpError.PreconditionFailed(ResourceRef.For<TDomain>(), failedKind ?? PreconditionKind.IfMatch))
                        { Detail = "A conditional request header evaluated to false." };

                        return ResponseFailureWriter.WriteAsync(httpContext, pf, ResolveErrorStatusCode(httpContext, pf, _options));
                    }
                }
            }
        }

        if (_options.LocationKind != LocationKind.None)
        {
            var location = ResolveLocation(httpContext, domain!);
            if (location is null)
            {
                var error = new Error.Unexpected(Code: FaultCodes.ResponseLocationUnresolved, FaultId: Guid.NewGuid().ToString("N"))
                { Detail = "Could not generate Location URI for the response." };

                return ResponseFailureWriter.WriteAsync(httpContext, error, ResolveErrorStatusCode(httpContext, error, _options));
            }

            ApplyCacheControlSelector(response, domain!);
            var body = _bodyProjector is not null ? (object?)_bodyProjector(domain!) : domain;
            if (_options.MarkAsCreated)
                return Results.Created(location, body).ExecuteAsync(httpContext);

            response.Headers.Location = location;
            return Results.Ok(body).ExecuteAsync(httpContext);
        }

        ApplyCacheControlSelector(response, domain!);
        var payload = _bodyProjector is not null ? (object?)_bodyProjector(domain!) : domain;
        return Results.Ok(payload).ExecuteAsync(httpContext);
    }

    // Selector-derived Cache-Control is applied only at the success-emit points (200/201/204/304)
    // so a mid-flow failure (412 PreconditionFailed, 500 / location-missing) does
    // NOT inherit a `public, max-age=N` selector value. That would otherwise turn a transient client
    // error into a cached negative response. The static-value overload remains in place across both
    // success and failure paths because it's written in ExecuteAsync before the branch. Also gates
    // on `domain is null` because Result.Ok<TValue>(null!) is legal for reference-type TValue and
    // the selector would NPE on the first member access.
    private void ApplyCacheControlSelector(HttpResponse response, TDomain domain)
    {
        if (domain is null)
            return;
        if (_options.CacheControlSelector is { } ccSel)
        {
            var v = ccSel(domain);
            if (v is not null)
                response.Headers["Cache-Control"] = v.ToString();
        }
    }

    internal static int ResolveErrorStatusCode(HttpContext httpContext, Error error, HttpResponseOptions<TDomain> options) =>
        ErrorStatusCodeResolver.Resolve(httpContext, error, options.ErrorMapper, options.ErrorOverrides);

    private RepresentationMetadata? BuildMetadataForEvaluation(TDomain domain)
    {
        // Same domain-null guard as ApplyMetadata: the selectors would NPE on null TDomain.
        // Returning null here means EvaluatePreconditions has nothing to evaluate against —
        // 304/412 short-circuit logic is skipped, and the response continues as a normal 200
        // (with a null body for Result.Ok<TValue>(null!)).
        if (domain is null)
            return null;

        var etag = _options.ETagSelector?.Invoke(domain);
        var lastModRaw = _options.LastModifiedSelector?.Invoke(domain);
        // Truncate the metadata value to match what was emitted on the wire (see ApplyMetadata
        // comment above) so revalidation requests bearing the exact emitted Last-Modified
        // value hit the 304 path instead of being treated as "client sent stale timestamp".
        var lastMod = lastModRaw.HasValue
            ? lastModRaw.Value.AddTicks(-(lastModRaw.Value.Ticks % TimeSpan.TicksPerSecond))
            : (DateTimeOffset?)null;
        if (etag is null && lastMod is null)
            return null;

        var b = RepresentationMetadata.Create();
        if (etag is not null)
            b = b.SetETag(etag);

        if (lastMod.HasValue)
            b = b.SetLastModified(lastMod.Value);

        return b.Build();
    }

    private void ApplyMetadata(HttpResponse response, TDomain domain)
    {
        // Result.Ok<TValue>(...) carries no null guard on the value (Trellis.Core/src/Result.cs:29),
        // so reference-type TDomain on the success path may arrive here with domain = null.
        // Domain-dependent selectors (ETag / Last-Modified / Content-Location) must short-circuit
        // in that case — invoking them with a null domain would NPE on the first member access.
        // Non-domain headers (Vary / ContentLanguage) still apply.
        var hasDomain = domain is not null;

        if (hasDomain && _options.ETagSelector is { } et)
        {
            var v = et(domain);
            if (v is not null)
                response.Headers.ETag = v.ToHeaderValue();
        }

        if (hasDomain && _options.LastModifiedSelector is { } lm)
        {
            var d = lm(domain);
            if (d.HasValue)
            {
                // Truncate to second precision so the emitted RFC1123 header matches the
                // value the precondition evaluator sees on the next request — preserving
                // sub-second ticks would cause `If-Modified-Since: <emitted-value>` to miss
                // the 304 short-circuit because `selectorTicks > Parse(headerString).Ticks`.
                var truncated = d.Value.AddTicks(-(d.Value.Ticks % TimeSpan.TicksPerSecond));
                response.Headers["Last-Modified"] = truncated.ToString("R");
            }
        }

        if (_options.Vary is { Count: > 0 })
        {
            foreach (var v in _options.Vary)
                AppendVaryUnique(response, v);
        }

        if (_options.ContentLanguage is { Count: > 0 })
            response.Headers.ContentLanguage = string.Join(", ", _options.ContentLanguage);

        LinkHeader.Append(response, _options.Links);

        if (hasDomain && _options.ContentLocationSelector is { } cls)
        {
            var v = cls(domain);
            if (!string.IsNullOrEmpty(v))
                response.Headers["Content-Location"] = v;
        }
    }

    internal static void AppendVaryUnique(HttpResponse response, string headerName)
    {
        var existing = response.Headers.Vary;
        foreach (var entry in existing)
        {
            if (entry is null)
                continue;

            foreach (var part in entry.Split(',', StringSplitOptions.TrimEntries))
            {
                if (string.Equals(part, headerName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        response.Headers.Append("Vary", headerName);
    }

    /// <summary>
    /// Resolves the registered <c>IActorProvider</c> and appends its
    /// <c>VaryByHeaders</c> entries to the response <c>Vary</c> header. Throws
    /// <see cref="InvalidOperationException"/> when no provider is registered or no
    /// provider in the decorator chain implements <c>IProvideActorVaryHeaders</c> —
    /// fail-closed rather than silently emit an incorrect or incomplete <c>Vary</c>
    /// header that would let intermediate caches serve actor A's response to actor B.
    /// Decorating providers (such as <c>CachingActorProvider</c>) are unwrapped via
    /// <c>IDecoratingActorProvider</c>: the OUTERMOST <c>IProvideActorVaryHeaders</c>
    /// in the chain wins (so a wrapper that legitimately augments the headers is
    /// honored), and unwrapping continues past wrappers without the capability so the
    /// fail-closed diagnostic names the innermost provider that needs the
    /// implementation.
    /// </summary>
    internal static void AppendActorVaryHeaders(HttpContext httpContext)
    {
        var provider = httpContext.RequestServices?.GetService<Trellis.Authorization.IActorProvider>()
            ?? throw new InvalidOperationException(
                "VaryForActor() requires an IActorProvider in the request scope but none is registered. " +
                "Register a provider via AddClaimsActorProvider/AddEntraActorProvider/AddDevelopmentActorProvider, " +
                "or call .Vary(\"...\") explicitly with the headers that contribute to actor identity.");

        // Walk the decorator chain. The outermost IProvideActorVaryHeaders wins so a custom
        // wrapper that adds its own headers (e.g. a tenant-scoping decorator that appends
        // X-Tenant to the inner provider's headers) is honored rather than silently bypassed.
        // Keep unwrapping past wrappers that don't implement the capability so the fail-closed
        // diagnostic, when no capability is found anywhere in the chain, names the innermost
        // provider that the consumer must fix — not an intermediate wrapper. Bounded against
        // self-referencing or cyclic IDecoratingActorProvider.Inner chains.
        const int MaxDecoratorDepth = 16;
        Trellis.Asp.Authorization.IProvideActorVaryHeaders? vary = null;
        var underlying = provider;
        var depth = 0;
        while (true)
        {
            if (vary is null && underlying is Trellis.Asp.Authorization.IProvideActorVaryHeaders v)
                vary = v;

            if (underlying is Trellis.Asp.Authorization.IDecoratingActorProvider decorator)
            {
                if (++depth > MaxDecoratorDepth)
                    throw new InvalidOperationException(
                        $"IDecoratingActorProvider chain rooted at '{provider.GetType().FullName}' exceeded the maximum unwrap depth ({MaxDecoratorDepth}). " +
                        "This usually means a decorator's Inner property returns itself or forms a cycle. Fix the offending IDecoratingActorProvider implementation.");
                underlying = decorator.Inner;
                continue;
            }

            break;
        }

        if (vary is null)
            throw new InvalidOperationException(
                $"VaryForActor() requires the registered IActorProvider " +
                $"('{underlying.GetType().FullName}') to implement IProvideActorVaryHeaders, but neither it " +
                "nor any IDecoratingActorProvider in its chain does. Either implement IProvideActorVaryHeaders on the provider " +
                "(returning the HTTP request headers that contribute to actor identity, e.g. [\"Authorization\"] for " +
                "bearer auth), or call .Vary(\"...\") explicitly with the relevant headers.");

        var headers = vary.VaryByHeaders;
        if (headers.Count == 0)
            throw new InvalidOperationException(
                $"VaryForActor() called against IActorProvider '{underlying.GetType().FullName}' whose VaryByHeaders is empty. " +
                "An empty collection means the provider derives actor identity from request data that cannot be " +
                "cleanly named by a single HTTP header (e.g. mTLS); such endpoints should not be cacheable across actors. " +
                "Use Cache-Control: private, no-store instead of VaryForActor().");

        foreach (var h in headers)
            AppendVaryUnique(httpContext.Response, h);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "ResolveLocation routes to ResolveActionLocation only when the consumer opted into LocationKind.Action via CreatedAtAction. CreatedAtAction itself is annotated [RequiresUnreferencedCode] so the requirement is surfaced at the public API boundary; consumers who don't use it pay no AOT cost.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "ResolveLocation routes to ResolveActionLocation only when the consumer opted into LocationKind.Action via CreatedAtAction. CreatedAtAction itself is annotated [RequiresDynamicCode] so the requirement is surfaced at the public API boundary; consumers who don't use it pay no AOT cost.")]
    private string? ResolveLocation(HttpContext httpContext, TDomain domain)
    {
        switch (_options.LocationKind)
        {
            case LocationKind.Literal:
                return _options.LocationLiteral;

            case LocationKind.Selector:
                return _options.LocationSelector!(domain);

            case LocationKind.Route:
                {
                    var lg = httpContext.RequestServices.GetRequiredService<LinkGenerator>();
                    var rv = ApplyRouteValueResolvers(_options.RouteValuesSelector!(domain), httpContext);
                    return lg.GetPathByName(httpContext, _options.RouteName!, rv);
                }

            case LocationKind.Action:
                return ResolveActionLocation(httpContext, domain);

            default:
                return null;
        }
    }

    private Microsoft.AspNetCore.Routing.RouteValueDictionary ApplyRouteValueResolvers(
        Microsoft.AspNetCore.Routing.RouteValueDictionary routeValues,
        HttpContext httpContext)
    {
        if (_options.RouteValueResolvers is null || _options.RouteValueResolvers.Count == 0)
            return routeValues;

        // Defer cloning until we actually have a non-null resolver value to write. If every
        // resolver returns null (e.g., api-version resolver short-circuits for [ApiVersionNeutral]
        // or URL-segment versioning), we never allocate a clone — the original dictionary is
        // returned unchanged. The clone protects against cross-request leakage on user selectors
        // that return cached/shared dictionary instances: writes go to the per-request copy, not
        // the shared instance.
        Microsoft.AspNetCore.Routing.RouteValueDictionary? withResolved = null;
        foreach (var (key, resolver) in _options.RouteValueResolvers)
        {
            var value = resolver(httpContext);
            if (value is null)
                continue;

            withResolved ??= new Microsoft.AspNetCore.Routing.RouteValueDictionary(routeValues);
            withResolved[key] = value;
        }

        return withResolved ?? routeValues;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("LocationKind.Action calls into MVC's ControllerLinkGeneratorExtensions which is not trim-safe. Use CreatedAtRoute (named routes) instead for AOT/trim scenarios.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("LocationKind.Action calls into MVC's ControllerLinkGeneratorExtensions which is not AOT-safe. Use CreatedAtRoute (named routes) instead for AOT scenarios.")]
    private string? ResolveActionLocation(HttpContext httpContext, TDomain domain)
    {
        // RuntimeFeature.IsDynamicCodeSupported is a trimmer-substituted constant: under PublishAot
        // the trimmer rewrites it to `false` and removes the entire body of this branch, eliminating
        // the reachability of MVC's trim-unsafe ControllerLinkGeneratorExtensions.CreateAddress.
        if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new NotSupportedException(
                "LocationKind.Action is not supported in AOT/trimmed builds because " +
                "MVC's ControllerLinkGeneratorExtensions is not trim-safe. " +
                "Use CreatedAtRoute with a named route instead.");
        }

        var lg = httpContext.RequestServices.GetRequiredService<LinkGenerator>();
        var rv = ApplyRouteValueResolvers(_options.RouteValuesSelector!(domain), httpContext);
        return lg.GetPathByAction(httpContext, _options.ActionName!, _options.ControllerName, rv);
    }

    /// <summary>
    /// Provides OpenAPI/ApiExplorer metadata. Declares the success status code, body type, and
    /// the full set of error envelope responses the result writer can emit. Consumers can layer
    /// their own <c>[ProducesResponseType]</c>/<c>Produces&lt;T&gt;</c> on top.
    /// </summary>
    /// <remarks>
    /// Because this contract is invoked statically by the endpoint pipeline (it has no access to
    /// the per-instance <see cref="HttpResponseOptions{TDomain}"/>), we declare the union of
    /// statuses any configuration of this result type may produce:
    /// <list type="bullet">
    ///   <item><description>200 OK — default success path with body.</description></item>
    ///   <item><description>201 Created — when <c>Created</c>, <c>CreatedAtRoute</c>, or <c>CreatedAtAction</c> set the builder's MarkAsCreated flag. Bare <c>WithLocation</c> emits a 200 OK + Location instead.</description></item>
    ///   <item><description>304 Not Modified — when conditional-request evaluation matches an If-None-Match / If-Modified-Since precondition.</description></item>
    ///   <item><description>400, 404, 412, 500 — error envelopes (problem+json) for the most common failure mappings.</description></item>
    /// </list>
    /// <para>
    /// <b>Result&lt;Unit&gt; specialization.</b> When <typeparamref name="TDomain"/> is
    /// <see cref="Unit"/>, the success path short-circuits to 204 No Content (see
    /// <c>ExecuteSuccessAsync</c>) before any body / location / preconditions branch
    /// runs. The metadata declared here matches that contract: 204 with no body and no JSON
    /// content type, plus the same problem-envelope error responses (400 / 404 / 500). 412 is
    /// omitted because preconditions are skipped for the Unit path.
    /// </para>
    /// </remarks>
    public static void PopulateMetadata(System.Reflection.MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (s_isUnit)
        {
            // 204 No Content — Result<Unit> success short-circuits before any body / location /
            // preconditions branch can apply, so the metadata is much smaller than the
            // general case below. Error envelopes still apply because failures flow through the
            // same ProblemDetails writer regardless of TDomain.
            builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status204NoContent, typeof(void)));
            builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(ProblemDetails), ["application/problem+json"]));
            builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status404NotFound, typeof(ProblemDetails), ["application/problem+json"]));
            builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status500InternalServerError, typeof(ProblemDetails), ["application/problem+json"]));
            return;
        }

        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(TBody), ["application/json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status201Created, typeof(TBody), ["application/json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status304NotModified, typeof(void)));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(ProblemDetails), ["application/problem+json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status404NotFound, typeof(ProblemDetails), ["application/problem+json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status412PreconditionFailed, typeof(ProblemDetails), ["application/problem+json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status500InternalServerError, typeof(ProblemDetails), ["application/problem+json"]));
    }
}