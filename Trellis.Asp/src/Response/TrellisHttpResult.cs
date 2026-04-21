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
    public int? StatusCode => _options.LocationKind != LocationKind.None
        ? StatusCodes.Status201Created
        : StatusCodes.Status200OK;

    /// <summary>Hint for OpenAPI: the body value (null on the failure path).</summary>
    public object? Value =>
        _result.IsSuccess && _bodyProjector is not null
            ? _bodyProjector(_result.Value)
            : _result.IsSuccess
                ? _result.Value
                : null;

    TBody? IValueHttpResult<TBody>.Value =>
        _result.IsSuccess && _bodyProjector is not null
            ? _bodyProjector(_result.Value)
            : default;

    public string? ContentType => "application/json";

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return _result.IsSuccess
            ? ExecuteSuccessAsync(httpContext)
            : ResponseFailureWriter.WriteAsync(httpContext, _result.Error!, ResolveErrorStatusCode(_result.Error!, _options));
    }

    private Task ExecuteSuccessAsync(HttpContext httpContext)
    {
        var domain = _result.Value;
        var response = httpContext.Response;

        ApplyMetadata(response, domain);

        if (_options.HonorPrefer)
            AppendVaryUnique(response, "Prefer");

        if (_options.EvaluatePreconditions)
        {
            var method = httpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            {
                var metadata = BuildMetadataForEvaluation(domain);
                if (metadata is not null)
                {
                    var decision = ConditionalRequestEvaluator.Evaluate(httpContext.Request, metadata);
                    if (decision == ConditionalDecision.NotModified)
                        return Results.StatusCode(StatusCodes.Status304NotModified).ExecuteAsync(httpContext);

                    if (decision == ConditionalDecision.PreconditionFailed)
                    {
                        var pf = new Error.PreconditionFailed(new ResourceRef(typeof(TDomain).Name, null), PreconditionKind.IfMatch)
                        { Detail = "A conditional request header evaluated to false." };

                        return ResponseFailureWriter.WriteAsync(httpContext, pf, ResolveErrorStatusCode(pf, _options));
                    }
                }
            }
        }

        var rangeOutcome = TryEvaluateRange(domain);
        if (rangeOutcome is not null)
        {
            var (from, to, total, error) = rangeOutcome.Value;
            if (error is not null)
                return ResponseFailureWriter.WriteAsync(httpContext, error, ResolveErrorStatusCode(error, _options));

            var bodyValue = _bodyProjector is not null ? (object?)_bodyProjector(domain) : domain;
            return new PartialContentHttpResult(from, to, total, Results.Ok(bodyValue)).ExecuteAsync(httpContext);
        }

        if (_options.LocationKind != LocationKind.None)
        {
            var location = ResolveLocation(httpContext, domain);
            if (location is null)
            {
                var error = new Error.InternalServerError(Guid.NewGuid().ToString("N"))
                { Detail = "Could not generate Location URI for created resource." };

                return ResponseFailureWriter.WriteAsync(httpContext, error, ResolveErrorStatusCode(error, _options));
            }

            var body = _bodyProjector is not null ? (object?)_bodyProjector(domain) : domain;
            return Results.Created(location, body).ExecuteAsync(httpContext);
        }

        var payload = _bodyProjector is not null ? (object?)_bodyProjector(domain) : domain;
        return Results.Ok(payload).ExecuteAsync(httpContext);
    }

    internal static int ResolveErrorStatusCode(Error error, HttpResponseOptions<TDomain> options)
    {
        if (options.ErrorMapper is not null)
            return options.ErrorMapper(error);

        if (options.ErrorOverrides is { Count: > 0 })
        {
            var t = error.GetType();
            while (t is not null && t != typeof(object))
            {
                if (options.ErrorOverrides.TryGetValue(t, out var sc))
                    return sc;

                t = t.BaseType;
            }
        }

        return TrellisAspOptions.Default.GetStatusCode(error);
    }

    private RepresentationMetadata? BuildMetadataForEvaluation(TDomain domain)
    {
        var etag = _options.ETagSelector?.Invoke(domain);
        var lastMod = _options.LastModifiedSelector?.Invoke(domain);
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
        if (_options.ETagSelector is { } et)
        {
            var v = et(domain);
            if (v is not null)
                response.Headers.ETag = v.ToHeaderValue();
        }

        if (_options.LastModifiedSelector is { } lm)
        {
            var d = lm(domain);
            if (d.HasValue)
                response.Headers["Last-Modified"] = d.Value.ToString("R");
        }

        if (_options.Vary is { Count: > 0 })
        {
            foreach (var v in _options.Vary)
                AppendVaryUnique(response, v);
        }

        if (_options.ContentLanguage is { Count: > 0 })
            response.Headers.ContentLanguage = string.Join(", ", _options.ContentLanguage);

        if (_options.ContentLocationSelector is { } cls)
        {
            var v = cls(domain);
            if (!string.IsNullOrEmpty(v))
                response.Headers["Content-Location"] = v;
        }

        if (!string.IsNullOrEmpty(_options.AcceptRanges))
            response.Headers["Accept-Ranges"] = _options.AcceptRanges;
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

    private (long From, long To, long Total, Error? Error)? TryEvaluateRange(TDomain domain)
    {
        if (_options.RangeSelector is { } rs)
        {
            var cr = rs(domain);
            if (cr.From is null || cr.To is null || cr.Length is null)
                return null;

            var from = cr.From.Value;
            var to = cr.To.Value;
            var total = cr.Length.Value;
            if (from == 0 && to == total - 1)
                return null;

            return (from, to, total, null);
        }

        if (_options.StaticRange is { } sr)
        {
            if (sr.From < 0 || sr.To < sr.From || sr.Total <= 0 || sr.From >= sr.Total)
                return null;

            var clampedTo = Math.Min(sr.To, sr.Total - 1);
            if (sr.From == 0 && clampedTo == sr.Total - 1)
                return null;

            return (sr.From, clampedTo, sr.Total, null);
        }

        return null;
    }

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
                var rv = _options.RouteValuesSelector!(domain);
                return lg.GetUriByName(httpContext, _options.RouteName!, rv)
                    ?? lg.GetPathByName(httpContext, _options.RouteName!, rv);
            }

            case LocationKind.Action:
            {
                var lg = httpContext.RequestServices.GetRequiredService<LinkGenerator>();
                var rv = _options.RouteValuesSelector!(domain);
                return lg.GetUriByAction(httpContext, _options.ActionName!, _options.ControllerName, rv)
                    ?? lg.GetPathByAction(httpContext, _options.ActionName!, _options.ControllerName, rv);
            }

            default:
                return null;
        }
    }

    /// <summary>
    /// Provides OpenAPI/ApiExplorer metadata. Declares the success status code, body type, and
    /// common error envelope responses. Consumers can layer their own
    /// <c>[ProducesResponseType]</c>/<c>Produces&lt;T&gt;</c> on top.
    /// </summary>
    public static void PopulateMetadata(System.Reflection.MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(TBody), ["application/json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(ProblemDetails), ["application/problem+json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status404NotFound, typeof(ProblemDetails), ["application/problem+json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status500InternalServerError, typeof(ProblemDetails), ["application/problem+json"]));
    }
}
