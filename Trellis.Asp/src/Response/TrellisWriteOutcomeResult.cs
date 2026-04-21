namespace Trellis.Asp;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Trellis;

/// <summary>
/// IResult implementation for <see cref="Result{T}"/> of <see cref="WriteOutcome{T}"/>.
/// Handles RFC 9110 status mapping (Created/Updated/UpdatedNoContent/Accepted/AcceptedNoContent)
/// and RFC 7240 Prefer semantics by delegating to the shared
/// <see cref="WriteOutcomeExtensions.ToHttpResult{T,TOut}(WriteOutcome{T},HttpContext,Func{T,TOut}?)"/>
/// helper after applying builder-supplied metadata overrides.
/// </summary>
internal sealed class TrellisWriteOutcomeResult<TDomain, TBody> :
    Microsoft.AspNetCore.Http.IResult,
    IStatusCodeHttpResult,
    IEndpointMetadataProvider
{
    private readonly Result<WriteOutcome<TDomain>> _result;
    private readonly Func<TDomain, TBody>? _body;
    private readonly HttpResponseOptions<TDomain> _options;

    public TrellisWriteOutcomeResult(Result<WriteOutcome<TDomain>> result, Func<TDomain, TBody>? body, HttpResponseOptions<TDomain> options)
    {
        _result = result;
        _body = body;
        _options = options;
    }

    /// <summary>Default success status hint for OpenAPI; runtime overrides per outcome variant.</summary>
    public int? StatusCode => StatusCodes.Status200OK;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (_result.IsFailure)
        {
            var sc = TrellisHttpResult<TDomain, TBody>.ResolveErrorStatusCode(_result.Error!, _options);
            return ResponseFailureWriter.WriteAsync(httpContext, _result.Error!, sc);
        }

        var outcome = _result.Value;
        var response = httpContext.Response;

        // Always emit Vary: Prefer when honoring Prefer (invariant per ADR-002 section 6).
        if (_options.HonorPrefer)
            TrellisHttpResult<TDomain, TBody>.AppendVaryUnique(response, "Prefer");

        ApplyBuilderMetadata(response, outcome);

#pragma warning disable CS0618 // Internal delegation to existing tested helper.
        return WriteOutcomeExtensions.ToHttpResult<TDomain, TBody>(outcome, httpContext, _body).ExecuteAsync(httpContext);
#pragma warning restore CS0618
    }

    private void ApplyBuilderMetadata(HttpResponse response, WriteOutcome<TDomain> outcome)
    {
        var domain = outcome switch
        {
            WriteOutcome<TDomain>.Created c => c.Value,
            WriteOutcome<TDomain>.Updated u => u.Value,
            _ => default,
        };

        if (_options.Vary is { Count: > 0 })
        {
            foreach (var v in _options.Vary)
                TrellisHttpResult<TDomain, TBody>.AppendVaryUnique(response, v);
        }

        if (domain is null)
            return;

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

    public static void PopulateMetadata(System.Reflection.MethodInfo method, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(TBody), ["application/json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status201Created, typeof(TBody), ["application/json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status204NoContent));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status202Accepted));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status400BadRequest, typeof(ProblemDetails), ["application/problem+json"]));
        builder.Metadata.Add(new ProducesResponseTypeMetadata(StatusCodes.Status412PreconditionFailed, typeof(ProblemDetails), ["application/problem+json"]));
    }
}
