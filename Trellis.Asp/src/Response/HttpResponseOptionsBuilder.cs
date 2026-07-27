namespace Trellis.Asp;

using System;
using System.Collections.Generic;
using Trellis;

/// <summary>
/// Fluent options builder for <see cref="HttpResponseExtensions.ToHttpResponse{T}(Result{T},Action{HttpResponseOptionsBuilder{T}}?)"/>.
/// Selectors run against the <typeparamref name="TDomain"/> value (not the projected response body).
/// </summary>
/// <typeparam name="TDomain">The domain value type from <c>Result&lt;TDomain&gt;</c>.</typeparam>
public sealed class HttpResponseOptionsBuilder<TDomain>
{
    private Func<TDomain, EntityTagValue?>? _eTagSelector;
    private Func<TDomain, DateTimeOffset?>? _lastModifiedSelector;
    private List<string>? _vary;
    private bool _varyForActor;
    private List<string>? _contentLanguage;
    private Func<TDomain, string?>? _contentLocationSelector;
    private System.Net.Http.Headers.CacheControlHeaderValue? _cacheControl;
    private Func<TDomain, System.Net.Http.Headers.CacheControlHeaderValue?>? _cacheControlSelector;

    private LocationKind _locationKind;
    private string? _locationLiteral;
    private Func<TDomain, string>? _locationSelector;
    private string? _routeName;
    private string? _actionName;
    private string? _controllerName;
    private Func<TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary>? _routeValuesSelector;
    private Dictionary<string, Func<Microsoft.AspNetCore.Http.HttpContext, string?>>? _routeValueResolvers;
    private bool _markAsCreated;

    private bool _evaluatePreconditions;
    private bool _honorPrefer;

    private Func<Error, int>? _errorMapper;
    private Dictionary<Type, int>? _errorOverrides;

    /// <summary>Sets a strong ETag derived from the domain value.</summary>
    public HttpResponseOptionsBuilder<TDomain> WithETag(Func<TDomain, string> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _eTagSelector = v => EntityTagValue.Strong(selector(v));
        return this;
    }

    /// <summary>Sets an ETag (strong or weak) derived from the domain value.</summary>
    public HttpResponseOptionsBuilder<TDomain> WithETag(Func<TDomain, EntityTagValue> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _eTagSelector = v => selector(v);
        return this;
    }

    /// <summary>Sets the <c>Last-Modified</c> header from the domain value.</summary>
    public HttpResponseOptionsBuilder<TDomain> WithLastModified(Func<TDomain, DateTimeOffset> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _lastModifiedSelector = v => selector(v);
        return this;
    }

    /// <summary>Appends the named headers to the response <c>Vary</c> header (existing values preserved).</summary>
    public HttpResponseOptionsBuilder<TDomain> Vary(params string[] headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        _vary ??= new();
        foreach (var h in headers)
            if (!string.IsNullOrWhiteSpace(h))
                _vary.Add(h);
        return this;
    }

    /// <summary>
    /// Appends the request header(s) that contribute to actor identity for the registered
    /// <c>IActorProvider</c> to the response <c>Vary</c> header. The provider must
    /// implement <c>IProvideActorVaryHeaders</c>; without that implementation the call
    /// throws at apply time (fail-closed) rather than emit an incorrect or incomplete
    /// <c>Vary</c> header that would allow intermediate caches to serve actor A's response
    /// to actor B.
    /// </summary>
    /// <remarks>
    /// For the framework-bundled providers: <c>ClaimsActorProvider</c> /
    /// <c>EntraActorProvider</c> emit <c>Authorization</c> (JWT-bearer assumption — override
    /// <c>VaryByHeaders</c> in a subclass if your service uses cookies, mTLS, or another
    /// non-bearer scheme); <c>DevelopmentActorProvider</c> emits the test header
    /// (<c>X-Test-Actor</c>); <c>CachingActorProvider</c> delegates to the wrapped provider.
    /// </remarks>
    public HttpResponseOptionsBuilder<TDomain> VaryForActor()
    {
        _varyForActor = true;
        return this;
    }

    /// <summary>Adds languages to the response <c>Content-Language</c> header.</summary>
    public HttpResponseOptionsBuilder<TDomain> WithContentLanguage(params string[] languages)
    {
        ArgumentNullException.ThrowIfNull(languages);
        _contentLanguage ??= new();
        foreach (var l in languages)
            if (!string.IsNullOrWhiteSpace(l))
                _contentLanguage.Add(l);
        return this;
    }

    /// <summary>Sets the <c>Content-Location</c> header from the domain value.</summary>
    public HttpResponseOptionsBuilder<TDomain> WithContentLocation(Func<TDomain, string> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _contentLocationSelector = v => selector(v);
        return this;
    }

    /// <summary>
    /// Sets the <c>Cache-Control</c> response header from the supplied directive. Applied to
    /// success responses (including 304 Not Modified) AND to failure responses, so a sensitive
    /// endpoint declaring <c>WithCacheControl(CacheControl.NoStore())</c> protects 404 / 403 /
    /// validation responses from intermediate-cache leakage, not just the success-path
    /// representation. Use the selector overload when the directive depends on the domain value
    /// (e.g. a TTL derived from the resource).
    /// </summary>
    /// <param name="value">The <see cref="System.Net.Http.Headers.CacheControlHeaderValue"/> whose <see cref="object.ToString"/> is written to <c>Cache-Control</c>.</param>
    /// <remarks>
    /// Use the framework-provided <see cref="CacheControl"/> presets for common shapes:
    /// <c>CacheControl.NoStore()</c>, <c>CacheControl.NoCache()</c>,
    /// <c>CacheControl.Public(TimeSpan)</c>, <c>CacheControl.Private(TimeSpan)</c>,
    /// <c>CacheControl.Immutable(TimeSpan)</c>. Each call returns a fresh instance so a consumer
    /// mutating one value does not corrupt a later call.
    /// </remarks>
    public HttpResponseOptionsBuilder<TDomain> WithCacheControl(System.Net.Http.Headers.CacheControlHeaderValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _cacheControl = value;
        return this;
    }

    /// <summary>
    /// Sets the <c>Cache-Control</c> response header from a selector run against the domain value.
    /// Applies to success responses only — the failure path has no domain value.
    /// </summary>
    /// <param name="selector">
    /// Selector invoked with the success-path domain value. Returning <see langword="null"/> from
    /// the selector skips the per-domain header on that response; when the static-value overload
    /// is also configured, that static value remains in place (the selector "refines, then falls
    /// back to static" rather than "overrides to nothing").
    /// </param>
    public HttpResponseOptionsBuilder<TDomain> WithCacheControl(Func<TDomain, System.Net.Http.Headers.CacheControlHeaderValue?> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _cacheControlSelector = selector;
        return this;
    }

    /// <summary>Returns 201 Created with a literal Location header (e.g. <c>/orders/123</c>).</summary>
    public HttpResponseOptionsBuilder<TDomain> Created(string locationLiteral)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationLiteral);
        _locationKind = LocationKind.Literal;
        _locationLiteral = locationLiteral;
        _markAsCreated = true;
        return this;
    }

    /// <summary>Returns 201 Created with a Location header derived from the domain value.</summary>
    public HttpResponseOptionsBuilder<TDomain> Created(Func<TDomain, string> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _locationKind = LocationKind.Selector;
        _locationSelector = selector;
        _markAsCreated = true;
        return this;
    }

    /// <summary>
    /// Returns 201 Created with a relative Location path generated via <c>LinkGenerator.GetPathByName</c>
    /// (resolved from <c>HttpContext.RequestServices</c> at execute time).
    /// </summary>
    /// <param name="routeName">The route name.</param>
    /// <param name="routeValues">A function that returns a <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> for the new resource. Pass a <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> rather than an anonymous type to remain AOT-compatible.</param>
    public HttpResponseOptionsBuilder<TDomain> CreatedAtRoute(string routeName, Func<TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(routeValues);
        _locationKind = LocationKind.Route;
        _routeName = routeName;
        _routeValuesSelector = routeValues;
        _markAsCreated = true;
        return this;
    }

    /// <summary>
    /// Convenience overload for the common single-id route shape (e.g. <c>{ ["id"] = order.Id.Value }</c>).
    /// Constructs the <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> from the supplied
    /// id selector and route key, then chains <see cref="CreatedAtRoute(string, Func{TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary})"/>.
    /// </summary>
    /// <param name="routeName">The route name.</param>
    /// <param name="idSelector">A function that returns the id value for the new resource.</param>
    /// <param name="idRouteKey">The route-value key for the id (defaults to <c>"id"</c>).</param>
    public HttpResponseOptionsBuilder<TDomain> CreatedAtRoute(string routeName, Func<TDomain, object> idSelector, string idRouteKey = "id")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(idSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(idRouteKey);
        return CreatedAtRoute(
            routeName,
            value => new Microsoft.AspNetCore.Routing.RouteValueDictionary { [idRouteKey] = idSelector(value) });
    }

    /// <summary>
    /// Adds a <c>Location</c> header to the response (status code unchanged — typically 200 OK),
    /// generated as a relative path via <c>LinkGenerator.GetPathByName</c> at execute time. RFC 9110 §10.2.2 permits
    /// <c>Location</c> on any 2xx response that identifies a related resource; use this on
    /// state-transition endpoints that mutate an existing resource and want to point clients at
    /// the canonical URL (e.g. <c>POST /orders/{id}/return</c> returning 200 OK).
    /// </summary>
    /// <param name="routeName">The route name.</param>
    /// <param name="routeValues">A function that returns a <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> for the resource being located.</param>
    /// <remarks>
    /// Unlike <see cref="CreatedAtRoute(string, Func{TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary})"/>,
    /// this does <b>not</b> set the status code to 201 Created — the response ships with its
    /// natural 2xx status code. This applies on the <c>Result&lt;T&gt;</c> execution path
    /// (<c>ToHttpResponse</c>). On <c>Result&lt;WriteOutcome&lt;T&gt;&gt;</c>, the builder still
    /// applies for other options, but <c>WithLocation</c> itself has no effect on the outcome's
    /// Location handling — that path reads <c>WriteOutcome.Created.Location</c>,
    /// <c>WriteOutcome.Accepted.MonitorUri</c>, and <c>WriteOutcome.AcceptedNoContent.MonitorUri</c>
    /// directly. On the <c>Result&lt;T&gt;</c> path, to round-trip the requested <c>api-version</c>
    /// through the generated <c>Location</c>, chain <c>WithVersionedRoute()</c> from
    /// <c>Trellis.Asp.ApiVersioning</c>; on the <c>Result&lt;WriteOutcome&lt;T&gt;&gt;</c> path,
    /// the version must instead be present in the URL the outcome itself carries.
    /// </remarks>
    public HttpResponseOptionsBuilder<TDomain> WithLocation(string routeName, Func<TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(routeValues);
        _locationKind = LocationKind.Route;
        _routeName = routeName;
        _routeValuesSelector = routeValues;
        // WithLocation takes ownership of the location configuration: it's the
        // state-transition primitive (2xx + Location), not a 201 Created. If a
        // prior Created/CreatedAtRoute/CreatedAtAction call had marked this builder
        // as Created, that intent is being replaced — clear the flag so the response
        // ships with its natural status code rather than a stale 201.
        _markAsCreated = false;
        return this;
    }

    /// <summary>
    /// Convenience overload for the common single-id route shape. Constructs the
    /// <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> from the supplied id selector
    /// and route key, then chains <see cref="WithLocation(string, Func{TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary})"/>.
    /// </summary>
    /// <param name="routeName">The route name.</param>
    /// <param name="idSelector">A function that returns the id value for the resource being located.</param>
    /// <param name="idRouteKey">The route-value key for the id (defaults to <c>"id"</c>).</param>
    public HttpResponseOptionsBuilder<TDomain> WithLocation(string routeName, Func<TDomain, object> idSelector, string idRouteKey = "id")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(idSelector);
        ArgumentException.ThrowIfNullOrWhiteSpace(idRouteKey);
        return WithLocation(
            routeName,
            value => new Microsoft.AspNetCore.Routing.RouteValueDictionary { [idRouteKey] = idSelector(value) });
    }

    /// <summary>
    /// Returns 201 Created with a relative Location path generated via <c>LinkGenerator.GetPathByAction</c>
    /// (resolved from <c>HttpContext.RequestServices</c> at execute time). Equivalent to MVC's <c>CreatedAtAction</c>.
    /// </summary>
    /// <param name="actionName">The action method name.</param>
    /// <param name="routeValues">A function that returns a <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> for the new resource. Pass a <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> rather than an anonymous type to remain AOT-compatible.</param>
    /// <param name="controllerName">Optional controller name (defaults to the current controller).</param>
    /// <remarks>
    /// This method is not trim-safe / AOT-safe because MVC's <c>ControllerLinkGeneratorExtensions</c>
    /// is not annotated. Use <see cref="CreatedAtRoute(string, Func{TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary})"/> with a named route instead for AOT scenarios.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("CreatedAtAction relies on MVC's ControllerLinkGeneratorExtensions which is not trim-safe. Use CreatedAtRoute with a named route for AOT/trim scenarios.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("CreatedAtAction relies on MVC's ControllerLinkGeneratorExtensions which is not AOT-safe. Use CreatedAtRoute with a named route for AOT scenarios.")]
    public HttpResponseOptionsBuilder<TDomain> CreatedAtAction(string actionName, Func<TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues, string? controllerName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        ArgumentNullException.ThrowIfNull(routeValues);
        _locationKind = LocationKind.Action;
        _actionName = actionName;
        _controllerName = controllerName;
        _routeValuesSelector = routeValues;
        _markAsCreated = true;
        return this;
    }

    /// <summary>
    /// Registers a per-request resolver that injects (or overrides) a single route-value entry
    /// at <c>Location</c>-header generation time. Invoked AFTER the
    /// <see cref="CreatedAtRoute(string, Func{TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary})"/> / <see cref="CreatedAtAction"/> route-values selector runs;
    /// if the resolver returns non-null, the value is set under <paramref name="key"/> in the
    /// resulting <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> (overriding any
    /// existing entry under the same key). If the resolver returns null, the entry is left
    /// untouched.
    /// </summary>
    /// <param name="key">The route-value key to inject.</param>
    /// <param name="resolver">The per-request resolver. Receives the active <see cref="Microsoft.AspNetCore.Http.HttpContext"/>; returns the value to inject, or null to skip.</param>
    /// <remarks>
    /// Designed for cross-cutting per-request concerns such as API versioning (a Location header
    /// that round-trips the requested version), tenant id, or culture. Multiple resolvers can be
    /// registered with distinct keys; calling this method again with the same key replaces the
    /// previous resolver. Useful with <see cref="CreatedAtRoute(string, Func{TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary})"/>,
    /// <see cref="CreatedAtAction"/> and <see cref="WithLocation(string, Func{TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary})"/>;
    /// ignored by literal/selector Location modes.
    /// </remarks>
    public HttpResponseOptionsBuilder<TDomain> WithRouteValueResolver(
        string key,
        Func<Microsoft.AspNetCore.Http.HttpContext, string?> resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(resolver);
        _routeValueResolvers ??= new Dictionary<string, Func<Microsoft.AspNetCore.Http.HttpContext, string?>>(StringComparer.OrdinalIgnoreCase);
        _routeValueResolvers[key] = resolver;
        return this;
    }

    /// <summary>
    /// Honors RFC 9110 conditional request headers (<c>If-Match</c>, <c>If-Unmodified-Since</c>,
    /// <c>If-None-Match</c>, <c>If-Modified-Since</c>) on the response side. Only meaningful for
    /// safe methods (GET/HEAD); on unsafe methods the precondition must be evaluated *before* the mutation.
    /// </summary>
    public HttpResponseOptionsBuilder<TDomain> EvaluatePreconditions()
    {
        _evaluatePreconditions = true;
        return this;
    }

    /// <summary>
    /// Honors RFC 7240 <c>Prefer: return=minimal/representation</c>. Always emits <c>Vary: Prefer</c>
    /// (appended; existing values preserved) and emits <c>Preference-Applied</c> only when honored.
    /// </summary>
    public HttpResponseOptionsBuilder<TDomain> HonorPrefer()
    {
        _honorPrefer = true;
        return this;
    }

    /// <summary>Per-call override mapper for failure responses. Highest precedence.</summary>
    public HttpResponseOptionsBuilder<TDomain> WithErrorMapping(Func<Error, int> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _errorMapper = mapper;
        return this;
    }

    /// <summary>Per-call override for a single error type. Higher precedence than the global <see cref="TrellisAspOptions"/>.</summary>
    public HttpResponseOptionsBuilder<TDomain> WithErrorMapping<TError>(int statusCode) where TError : Error
    {
        _errorOverrides ??= new();
        _errorOverrides[typeof(TError)] = statusCode;
        return this;
    }

    internal HttpResponseOptions<TDomain> Build() => new()
    {
        ETagSelector = _eTagSelector,
        LastModifiedSelector = _lastModifiedSelector,
        Vary = _vary,
        VaryForActor = _varyForActor,
        ContentLanguage = _contentLanguage,
        ContentLocationSelector = _contentLocationSelector,
        CacheControl = _cacheControl,
        CacheControlSelector = _cacheControlSelector,
        LocationKind = _locationKind,
        LocationLiteral = _locationLiteral,
        LocationSelector = _locationSelector,
        RouteName = _routeName,
        ActionName = _actionName,
        ControllerName = _controllerName,
        RouteValuesSelector = _routeValuesSelector,
        RouteValueResolvers = _routeValueResolvers,
        MarkAsCreated = _markAsCreated,
        EvaluatePreconditions = _evaluatePreconditions,
        HonorPrefer = _honorPrefer,
        ErrorMapper = _errorMapper,
        ErrorOverrides = _errorOverrides,
    };
}

internal enum LocationKind { None, Literal, Selector, Route, Action }

internal sealed class HttpResponseOptions<TDomain>
{
    public Func<TDomain, EntityTagValue?>? ETagSelector { get; init; }
    public Func<TDomain, DateTimeOffset?>? LastModifiedSelector { get; init; }
    public List<string>? Vary { get; init; }
    public bool VaryForActor { get; init; }
    public List<string>? ContentLanguage { get; init; }
    public Func<TDomain, string?>? ContentLocationSelector { get; init; }
    public System.Net.Http.Headers.CacheControlHeaderValue? CacheControl { get; init; }
    public Func<TDomain, System.Net.Http.Headers.CacheControlHeaderValue?>? CacheControlSelector { get; init; }

    public LocationKind LocationKind { get; init; }
    public string? LocationLiteral { get; init; }
    public Func<TDomain, string>? LocationSelector { get; init; }
    public string? RouteName { get; init; }
    public string? ActionName { get; init; }
    public string? ControllerName { get; init; }
    public Func<TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary>? RouteValuesSelector { get; init; }
    public IReadOnlyDictionary<string, Func<Microsoft.AspNetCore.Http.HttpContext, string?>>? RouteValueResolvers { get; init; }
    public bool MarkAsCreated { get; init; }

    public bool EvaluatePreconditions { get; init; }
    public bool HonorPrefer { get; init; }

    public Func<Error, int>? ErrorMapper { get; init; }
    public Dictionary<Type, int>? ErrorOverrides { get; init; }
}

/// <summary>Non-generic builder used for <see cref="Result"/> (no value).</summary>
public sealed class HttpResponseOptionsBuilder
{
    private List<string>? _vary;
    private bool _varyForActor;
    private bool _honorPrefer;
    private System.Net.Http.Headers.CacheControlHeaderValue? _cacheControl;
    private Func<Error, int>? _errorMapper;
    private Dictionary<Type, int>? _errorOverrides;

    /// <summary>Appends headers to the response <c>Vary</c> header.</summary>
    public HttpResponseOptionsBuilder Vary(params string[] headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        _vary ??= new();
        foreach (var h in headers)
            if (!string.IsNullOrWhiteSpace(h))
                _vary.Add(h);
        return this;
    }

    /// <summary>
    /// Appends the request header(s) that contribute to actor identity for the registered
    /// <c>IActorProvider</c> to the response <c>Vary</c> header. See
    /// <see cref="HttpResponseOptionsBuilder{TDomain}.VaryForActor"/> for the contract.
    /// </summary>
    public HttpResponseOptionsBuilder VaryForActor()
    {
        _varyForActor = true;
        return this;
    }

    /// <summary>Honors <c>Prefer</c>; always emits <c>Vary: Prefer</c>.</summary>
    public HttpResponseOptionsBuilder HonorPrefer()
    {
        _honorPrefer = true;
        return this;
    }

    /// <summary>
    /// Sets the <c>Cache-Control</c> response header from the supplied directive. The non-generic
    /// builder is consumed only by <see cref="HttpResponseExtensions.ToHttpResponse(Error, Action{HttpResponseOptionsBuilder}?)"/>,
    /// so this overload applies the directive to the standalone <c>Error</c> ProblemDetails
    /// response — useful for keeping deterministic-error responses out of intermediate caches
    /// via <c>Error.ToHttpResponse(o => o.WithCacheControl(CacheControl.NoStore()))</c>. See
    /// <see cref="HttpResponseOptionsBuilder{TDomain}.WithCacheControl(System.Net.Http.Headers.CacheControlHeaderValue)"/>
    /// for the generic builder's full success-and-failure semantics.
    /// </summary>
    public HttpResponseOptionsBuilder WithCacheControl(System.Net.Http.Headers.CacheControlHeaderValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _cacheControl = value;
        return this;
    }

    /// <summary>Per-call override mapper for failure responses.</summary>
    public HttpResponseOptionsBuilder WithErrorMapping(Func<Error, int> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        _errorMapper = mapper;
        return this;
    }

    /// <summary>Per-call override for a single error type.</summary>
    public HttpResponseOptionsBuilder WithErrorMapping<TError>(int statusCode) where TError : Error
    {
        _errorOverrides ??= new();
        _errorOverrides[typeof(TError)] = statusCode;
        return this;
    }

    internal HttpResponseOptions<object> Build() => new()
    {
        Vary = _vary,
        VaryForActor = _varyForActor,
        HonorPrefer = _honorPrefer,
        CacheControl = _cacheControl,
        ErrorMapper = _errorMapper,
        ErrorOverrides = _errorOverrides,
    };
}
