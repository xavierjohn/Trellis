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
    private List<string>? _contentLanguage;
    private Func<TDomain, string?>? _contentLocationSelector;
    private string? _acceptRanges;

    private LocationKind _locationKind;
    private string? _locationLiteral;
    private Func<TDomain, string>? _locationSelector;
    private string? _routeName;
    private string? _actionName;
    private string? _controllerName;
    private Func<TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary>? _routeValuesSelector;

    private bool _evaluatePreconditions;
    private bool _honorPrefer;

    private Func<TDomain, System.Net.Http.Headers.ContentRangeHeaderValue>? _rangeSelector;
    private (long From, long To, long Total)? _staticRange;

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

    /// <summary>Sets the <c>Accept-Ranges</c> response header (e.g. "bytes" or "none").</summary>
    public HttpResponseOptionsBuilder<TDomain> WithAcceptRanges(string acceptRanges)
    {
        _acceptRanges = acceptRanges;
        return this;
    }

    /// <summary>Returns 201 Created with a literal Location header (e.g. <c>/orders/123</c>).</summary>
    public HttpResponseOptionsBuilder<TDomain> Created(string locationLiteral)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationLiteral);
        _locationKind = LocationKind.Literal;
        _locationLiteral = locationLiteral;
        return this;
    }

    /// <summary>Returns 201 Created with a Location header derived from the domain value.</summary>
    public HttpResponseOptionsBuilder<TDomain> Created(Func<TDomain, string> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _locationKind = LocationKind.Selector;
        _locationSelector = selector;
        return this;
    }

    /// <summary>
    /// Returns 201 Created with a Location header generated via <c>LinkGenerator.GetUriByName</c>
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
        return this;
    }

    /// <summary>
    /// Returns 201 Created with a Location header generated via <c>LinkGenerator.GetUriByAction</c>
    /// (resolved from <c>HttpContext.RequestServices</c> at execute time). Equivalent to MVC's <c>CreatedAtAction</c>.
    /// </summary>
    /// <param name="actionName">The action method name.</param>
    /// <param name="routeValues">A function that returns a <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> for the new resource. Pass a <see cref="Microsoft.AspNetCore.Routing.RouteValueDictionary"/> rather than an anonymous type to remain AOT-compatible.</param>
    /// <param name="controllerName">Optional controller name (defaults to the current controller).</param>
    /// <remarks>
    /// This method is not trim-safe / AOT-safe because MVC's <c>ControllerLinkGeneratorExtensions</c>
    /// is not annotated. Use <see cref="CreatedAtRoute"/> with a named route instead for AOT scenarios.
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

    /// <summary>Returns 206 Partial Content with <c>Content-Range</c> header for partial ranges.</summary>
    public HttpResponseOptionsBuilder<TDomain> WithRange(Func<TDomain, System.Net.Http.Headers.ContentRangeHeaderValue> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _rangeSelector = selector;
        _staticRange = null;
        return this;
    }

    /// <summary>Returns 206 Partial Content for the given byte range, or 200 OK if it covers the whole resource.</summary>
    public HttpResponseOptionsBuilder<TDomain> WithRange(long from, long to, long totalLength)
    {
        _staticRange = (from, to, totalLength);
        _rangeSelector = null;
        return this;
    }

    /// <summary>Per-call override mapper for failure responses. Highest precedence.</summary>
    public HttpResponseOptionsBuilder<TDomain> WithErrorMapping(Func<Error, int> mapper)
    {
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
        ContentLanguage = _contentLanguage,
        ContentLocationSelector = _contentLocationSelector,
        AcceptRanges = _acceptRanges,
        LocationKind = _locationKind,
        LocationLiteral = _locationLiteral,
        LocationSelector = _locationSelector,
        RouteName = _routeName,
        ActionName = _actionName,
        ControllerName = _controllerName,
        RouteValuesSelector = _routeValuesSelector,
        EvaluatePreconditions = _evaluatePreconditions,
        HonorPrefer = _honorPrefer,
        RangeSelector = _rangeSelector,
        StaticRange = _staticRange,
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
    public List<string>? ContentLanguage { get; init; }
    public Func<TDomain, string?>? ContentLocationSelector { get; init; }
    public string? AcceptRanges { get; init; }

    public LocationKind LocationKind { get; init; }
    public string? LocationLiteral { get; init; }
    public Func<TDomain, string>? LocationSelector { get; init; }
    public string? RouteName { get; init; }
    public string? ActionName { get; init; }
    public string? ControllerName { get; init; }
    public Func<TDomain, Microsoft.AspNetCore.Routing.RouteValueDictionary>? RouteValuesSelector { get; init; }

    public bool EvaluatePreconditions { get; init; }
    public bool HonorPrefer { get; init; }

    public Func<TDomain, System.Net.Http.Headers.ContentRangeHeaderValue>? RangeSelector { get; init; }
    public (long From, long To, long Total)? StaticRange { get; init; }

    public Func<Error, int>? ErrorMapper { get; init; }
    public Dictionary<Type, int>? ErrorOverrides { get; init; }
}

/// <summary>Non-generic builder used for <see cref="Result"/> (no value).</summary>
public sealed class HttpResponseOptionsBuilder
{
    private List<string>? _vary;
    private bool _honorPrefer;
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

    /// <summary>Honors <c>Prefer</c>; always emits <c>Vary: Prefer</c>.</summary>
    public HttpResponseOptionsBuilder HonorPrefer()
    {
        _honorPrefer = true;
        return this;
    }

    /// <summary>Per-call override mapper for failure responses.</summary>
    public HttpResponseOptionsBuilder WithErrorMapping(Func<Error, int> mapper)
    {
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
        HonorPrefer = _honorPrefer,
        ErrorMapper = _errorMapper,
        ErrorOverrides = _errorOverrides,
    };
}