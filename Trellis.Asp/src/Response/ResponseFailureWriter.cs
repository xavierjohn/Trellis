﻿namespace Trellis.Asp;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trellis;

/// <summary>
/// Internal helper that writes failure responses (ProblemDetails / ValidationProblem) and emits
/// RFC-required companion headers (<c>Allow</c>, <c>Content-Range</c>, <c>Retry-After</c>,
/// <c>WWW-Authenticate</c>) using the supplied per-call status code.
/// </summary>
internal static class ResponseFailureWriter
{
    private const string LoggerCategoryName = "Trellis.Asp.ResponseFailureWriter";

    private static readonly ConcurrentDictionary<Type, byte> _loggedExceptionTypes = new();

    private static readonly Action<ILogger, string, Exception?> _logInstanceSynthesisFailure =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(ResponseFailureWriter)),
            "ResponseFailureWriter could not synthesize ProblemDetails.Instance from " +
            "ResourceRef; falling back to request URL. Suppressing further logs for " +
            "exceptions of type {ExceptionType} for this application lifetime. Verify " +
            "ResourceCollectionNameRegistry registration and any AddResourceCollectionName " +
            "configurations.");

    /// <summary>
    /// Test-only hook for resetting the static diagnostic throttle.
    /// </summary>
    internal static void ResetDiagnosticThrottlesForTests() => _loggedExceptionTypes.Clear();

    /// <summary>
    /// Writes an <see cref="Error"/> to the response as an RFC 9457 problem document.
    /// </summary>
    /// <param name="httpContext">The current request context.</param>
    /// <param name="error">The failing error.</param>
    /// <param name="statusCode">The already-resolved status code for <paramref name="error"/>.</param>
    /// <param name="resolveChildStatus">
    /// Optional projection used for <see cref="Error.Aggregate"/> children rendered into the
    /// <c>errors[]</c> extension. Callers pass the same per-call → ambient precedence chain used
    /// for <paramref name="statusCode"/> so a child status cannot contradict the endpoint's own
    /// <c>WithErrorMapping</c> configuration. Falls back to ambient options when null.
    /// </param>
    public static async Task WriteAsync(
        HttpContext httpContext,
        Error error,
        int statusCode,
        Func<Error, int>? resolveChildStatus = null)
    {
        EmitCompanionHeaders(httpContext.Response, error);

        // Conflict + concurrent_modification + If-Match → 412 / precondition-failed.
        // Applied locally here (not in GetStatusCode) because the override depends on the
        // request's If-Match header, which TrellisAspOptions has no access to.
        var overriden = TryConcurrentModificationOverride(httpContext, error);
        var effectiveStatus = overriden?.Status ?? statusCode;
        var wireKindOverride = overriden?.WireKind;

        // The only async companion-header path: synthesize WWW-Authenticate when a 401 error
        // reaches the writer and the response doesn't already carry the header.
        if (error is Error.AuthenticationRequired authRequired
            && effectiveStatus == 401
            && !httpContext.Response.Headers.ContainsKey("WWW-Authenticate"))
        {
            await SynthesizeWwwAuthenticateAsync(httpContext, authRequired.Scheme).ConfigureAwait(false);
        }

        // RFC 9457 §3.1: "instance" identifies the specific occurrence of the problem.
        // Emitting the server-relative path+query (rather than the absolute URL) avoids host
        // disclosure while still letting clients correlate the response with the request that
        // produced it.
        var requestPath = httpContext.Request.GetEncodedPathAndQuery();
        var aspOptions = httpContext.RequestServices?.GetService<TrellisAspOptions>() ?? TrellisAspOptions.SystemDefault;

        // When the failing resource is identified by a ResourceRef whose Id does NOT appear in
        // the request URL (typical for POST-to-collection that references some other missing
        // resource), substitute Instance with a canonical resource URI and preserve the original
        // request URL under Extensions["request"]. Best-effort: any malformed ResourceRef or
        // resolver error keeps the original behaviour.
        var (instance, originalRequest) = TrySynthesizeInstance(httpContext, error, requestPath, aspOptions);

        Microsoft.AspNetCore.Http.IResult inner;

        if (error is Error.Aggregate aggregate)
        {
            var options = aspOptions;
            var detail = PublicDetailForStatus(error, effectiveStatus);
            var extensions = BuildExtensions(error, default, wireKindOverride);
            if (originalRequest is not null)
                extensions["request"] = originalRequest;
            extensions["errors"] = aggregate.Errors.Items
                .Select(child =>
                {
                    var (childCode, childKind) = GetCodeAndKind(child);
                    var childStatus = resolveChildStatus is not null
                        ? resolveChildStatus(child)
                        : options.GetStatusCode(child);
                    return (object?)new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = childKind,
                        ["status"] = childStatus,
                        ["code"] = childCode,
                        ["kind"] = childKind,
                        ["detail"] = PublicDetailForStatus(child, childStatus),
                    };
                })
                .ToArray();

            inner = Microsoft.AspNetCore.Http.Results.Problem(
                detail,
                instance,
                effectiveStatus,
                extensions: extensions);
        }
        else if (error is Error.InvalidInput unprocessable
            && (unprocessable.Fields.Items.Length > 0 || unprocessable.Rules.Items.Length > 0))
        {
            var errors = unprocessable.Fields.Items
                .GroupBy(fv => JsonPointerToMvc.Translate(fv.Field.Path))
                .ToDictionary(g => g.Key, g => g.Select(fv => fv.Detail ?? fv.ReasonCode).ToArray());

            var validationDetail = effectiveStatus >= 500 ? RedactedServerDetail : unprocessable.Detail;
            var extensions = BuildExtensions(error, unprocessable.Rules, wireKindOverride);
            if (originalRequest is not null)
                extensions["request"] = originalRequest;
            inner = Microsoft.AspNetCore.Http.Results.ValidationProblem(
                errors,
                validationDetail,
                instance,
                effectiveStatus,
                extensions: extensions);
        }
        else
        {
            var detail = PublicDetailForStatus(error, effectiveStatus);
            var rules = error is Error.InvalidInput uc ? uc.Rules : default;
            var extensions = BuildExtensions(error, rules, wireKindOverride);
            if (originalRequest is not null)
                extensions["request"] = originalRequest;
            inner = Microsoft.AspNetCore.Http.Results.Problem(
                detail,
                instance,
                effectiveStatus,
                extensions: extensions);
        }

        await inner.ExecuteAsync(httpContext).ConfigureAwait(false);
    }

    private static (string Instance, string? OriginalRequest) TrySynthesizeInstance(
        HttpContext httpContext,
        Error error,
        string requestPath,
        TrellisAspOptions aspOptions)
    {
        if (!aspOptions.SynthesizeProblemDetailsInstanceFromResourceRef)
            return (requestPath, null);

        var resourceRef = TryGetResourceRef(error);
        if (resourceRef is not { } @ref
            || string.IsNullOrWhiteSpace(@ref.Type)
            || string.IsNullOrWhiteSpace(@ref.Id))
        {
            return (requestPath, null);
        }

        if (UrlContainsId(requestPath, @ref.Id!))
            return (requestPath, null);

        ResourceCollectionNameRegistry registry;
        string collection;
        try
        {
            registry = httpContext.RequestServices?.GetService<ResourceCollectionNameRegistry>()
                ?? s_defaultRegistry;
            collection = registry.Resolve(@ref.Type);
        }
        catch (Exception ex)
        {
            // Best-effort: a DI activation failure (e.g., the registry ctor throws on
            // misconfigured overrides) or a resolver fault must never turn a legitimate
            // 404/409 into a 500. Fall back to the request URL.
            LogInstanceSynthesisFailure(httpContext.RequestServices, ex);
            return (requestPath, null);
        }

        if (string.IsNullOrWhiteSpace(collection)
            || !ResourceCollectionNameAttribute.IsSafePathSegment(collection))
        {
            return (requestPath, null);
        }

        var synthesized = "/" + collection + "/" + Uri.EscapeDataString(@ref.Id!);
        return (synthesized, requestPath);
    }

    private static ResourceRef? TryGetResourceRef(Error error) => error switch
    {
        Error.NotFound nf => nf.Resource,
        Error.Gone gn => gn.Resource,
        Error.Conflict { Resource: { } r } => r,
        Error.Forbidden { Resource: { } r } => r,
        Error.InvariantViolation { Resource: { } r } => r,
        Error.TransportFault { Fault: HttpError.PreconditionFailed pf } => pf.Resource,
        _ => null,
    };

    // Segment- and query-aware check: matches a path segment exactly OR a query value exactly
    // against the raw id, after percent-decoding the candidate. Percent-encoding is case-
    // insensitive (RFC 3986 §6.2.2.1) so we normalise by decoding the candidate rather than
    // comparing encoded forms. Query values are decoded with WebUtility.UrlDecode so that
    // form-encoded '+' is treated as space, matching ASP.NET Core's query parser. Avoids
    // false positives on short numeric ids that happen to appear inside path segments
    // like "v1" or inside query keys.
    private static bool UrlContainsId(string pathAndQuery, string id)
    {
        if (string.IsNullOrEmpty(pathAndQuery) || string.IsNullOrEmpty(id))
            return false;

        var queryStart = pathAndQuery.IndexOf('?');
        var path = queryStart < 0 ? pathAndQuery.AsSpan() : pathAndQuery.AsSpan(0, queryStart);
        var query = queryStart < 0 ? ReadOnlySpan<char>.Empty : pathAndQuery.AsSpan(queryStart + 1);

        foreach (var segmentRange in SplitSpan(path, '/'))
        {
            var segment = path[segmentRange];
            if (segment.Length == 0)
                continue;
            if (SegmentMatchesId(segment, id))
                return true;
        }

        foreach (var pairRange in SplitSpan(query, '&'))
        {
            var pair = query[pairRange];
            if (pair.Length == 0)
                continue;
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                // Key-only query parameter (no '='): RFC 3986 allows this, and a domain
                // resource id is never expressed as a bare query key. Skip the pair so the
                // id-in-URL check does not treat the key itself as a value match.
                continue;
            }

            var value = pair[(eq + 1)..];
            if (QueryValueMatchesId(value, id))
                return true;
        }

        return false;
    }

    private static bool SegmentMatchesId(ReadOnlySpan<char> segment, string id)
    {
        if (segment.SequenceEqual(id))
            return true;

        // Only attempt decode when the segment actually contains a percent escape; this
        // avoids allocating a string per non-encoded segment.
        if (segment.IndexOf('%') < 0)
            return false;

        // Instance synthesis is best-effort: a malformed percent-escape in the request
        // path must never turn a domain failure into a 500. .NET's current Uri.UnescapeDataString
        // is permissive (returns invalid triplets verbatim), but the contract does not
        // forbid throwing, so we treat a decode failure as "non-match" rather than letting
        // it propagate.
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(segment.ToString());
        }
        catch (UriFormatException)
        {
            return false;
        }

        return string.Equals(decoded, id, StringComparison.Ordinal);
    }

    private static bool QueryValueMatchesId(ReadOnlySpan<char> value, string id)
    {
        if (value.SequenceEqual(id))
            return true;

        // Query values may contain percent escapes OR '+' for space (form-encoded). Only
        // pay decoding cost when either is present.
        if (value.IndexOf('%') < 0 && value.IndexOf('+') < 0)
            return false;

        var decoded = System.Net.WebUtility.UrlDecode(value.ToString());
        return string.Equals(decoded, id, StringComparison.Ordinal);
    }

    private static List<Range> SplitSpan(ReadOnlySpan<char> source, char separator)
    {
        var ranges = new List<Range>();
        var start = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == separator)
            {
                ranges.Add(new Range(start, i));
                start = i + 1;
            }
        }

        ranges.Add(new Range(start, source.Length));
        return ranges;
    }

    private static readonly ResourceCollectionNameRegistry s_defaultRegistry = new();

    private static void LogInstanceSynthesisFailure(IServiceProvider? requestServices, Exception exception)
    {
        try
        {
            var logger = requestServices?.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategoryName);
            if (logger is null)
                return;

            var exceptionType = exception.GetType();
            if (!_loggedExceptionTypes.TryAdd(exceptionType, 0))
                return;

            _logInstanceSynthesisFailure(logger, exceptionType.FullName ?? exceptionType.Name, exception);
        }
        catch
        {
            // Diagnostic logging must remain best-effort and never change the HTTP outcome.
        }
    }

    private static void EmitCompanionHeaders(HttpResponse response, Error error)
    {
        switch (error)
        {
            case Error.TransportFault { Fault: HttpError.MethodNotAllowed mae }:
                response.Headers["Allow"] = string.Join(", ", mae.Allow.Items);
                break;

            case Error.TransportFault { Fault: HttpError.RangeNotSatisfiable rnse }:
                response.Headers["Content-Range"] = $"{rnse.Unit} */{rnse.CompleteLength}";
                break;

            case Error.RateLimited { Retry: { } rl }:
                EmitRetryAfter(response, rl);
                break;

            case Error.Unavailable { Retry: { } uv }:
                EmitRetryAfter(response, uv);
                break;
        }
    }

    private static void EmitRetryAfter(HttpResponse response, RetryAdvice retry)
    {
        // RFC 9110 §10.2.3: prefer delta-seconds when both are set — simpler for clients and
        // not subject to clock skew.
        if (retry.After is { } delta)
        {
            var seconds = Math.Max(0L, (long)Math.Ceiling(delta.TotalSeconds));
            response.Headers["Retry-After"] = seconds.ToString(CultureInfo.InvariantCulture);
        }
        else if (retry.At is { } at)
        {
            // RFC 7231 §7.1.1.1 IMF-fixdate form, e.g. "Sun, 06 Nov 1994 08:49:37 GMT".
            response.Headers["Retry-After"] = at.UtcDateTime.ToString("r", CultureInfo.InvariantCulture);
        }
    }

    private static (int Status, string WireKind)? TryConcurrentModificationOverride(HttpContext httpContext, Error error)
    {
        if (error is Error.Conflict { ReasonCode: "concurrent_modification" }
            && httpContext.Request.Headers.ContainsKey("If-Match"))
        {
            return (StatusCodes.Status412PreconditionFailed, "precondition-failed");
        }

        return null;
    }

    /// <summary>
    /// Synthesizes a <c>WWW-Authenticate</c> challenge. When the domain error carries an explicit
    /// scheme string, it is emitted verbatim (RFC 9110 §11.6.1 challenge syntax is the caller's
    /// responsibility). Otherwise the registered default-challenge scheme is resolved via
    /// <see cref="IAuthenticationSchemeProvider"/>. If no auth scheme is configured, emits
    /// nothing — synthesizing "Bearer" for a service that does not use Bearer would mislead clients.
    /// </summary>
    private static async Task SynthesizeWwwAuthenticateAsync(HttpContext httpContext, string? explicitScheme)
    {
        if (!string.IsNullOrWhiteSpace(explicitScheme))
        {
            httpContext.Response.Headers.Append("WWW-Authenticate", explicitScheme);
            return;
        }

        var schemeProvider = httpContext.RequestServices?.GetService<IAuthenticationSchemeProvider>();
        if (schemeProvider is null)
            return;

        var scheme = await schemeProvider.GetDefaultChallengeSchemeAsync().ConfigureAwait(false)
            ?? await schemeProvider.GetDefaultAuthenticateSchemeAsync().ConfigureAwait(false);
        if (scheme is null)
            return;

        httpContext.Response.Headers.Append("WWW-Authenticate", scheme.Name);
    }

    private const string RedactedServerDetail = "An internal error occurred.";

    /// <summary>
    /// Applies the server-failure redaction rule: a 5xx detail describes an internal condition the
    /// caller cannot act on, so it is replaced with a fixed message. Applied per error and per
    /// resolved status so an aggregate's <c>errors[]</c> children are redacted on their own status
    /// rather than inheriting the outer one.
    /// </summary>
    private static string? PublicDetailForStatus(Error error, int status) =>
        status >= 500 ? RedactedServerDetail : GetPublicDetail(error);

    private static string? GetPublicDetail(Error error) =>
        error.Detail
        ?? (error is Error.TransportFault { Fault: HttpError httpError } ? httpError.Detail : null);

    private static (string Code, string Kind) GetCodeAndKind(Error error) =>
        error is Error.TransportFault { Fault: HttpError httpError }
            ? (httpError.Code, httpError.Kind)
            : (error.Code, ToWireKind(error));

    private static Dictionary<string, object?> BuildExtensions(Error error, EquatableArray<RuleViolation> rules, string? wireKindOverride = null)
    {
        var (code, kind) = GetCodeAndKind(error);
        if (wireKindOverride is not null)
            kind = wireKindOverride;

        var ext = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["code"] = code,
            ["kind"] = kind,
        };

        if (error is Error.Unexpected ise && ise.FaultId is not null)
            ext["faultId"] = ise.FaultId;

        if (error is Error.TransportFault { Fault: HttpError fault })
        {
            ProjectHttpErrorPayload(ext, fault);
        }

        if (rules.Items.Length > 0)
        {
            ext["rules"] = rules.Items
                .Select(rv => new RuleViolationProblemDetail(
                    rv.ReasonCode,
                    rv.Detail,
                    rv.Fields.Items.Select(p => p.Path).ToArray()))
                .ToArray();
        }

        return ext;
    }

    private static void ProjectHttpErrorPayload(Dictionary<string, object?> ext, HttpError fault)
    {
        switch (fault)
        {
            case HttpError.MethodNotAllowed { Allow: var allow }:
                ext["allow"] = allow.Items.ToArray();
                break;
            case HttpError.NotAcceptable { Available: var avail }:
                ext["available"] = avail.Items.ToArray();
                break;
            case HttpError.UnsupportedMediaType { Supported: var sup }:
                ext["supported"] = sup.Items.ToArray();
                break;
            case HttpError.RangeNotSatisfiable rnse:
                ext["completeLength"] = rnse.CompleteLength;
                ext["unit"] = rnse.Unit;
                break;
            case HttpError.ContentTooLarge { MaxBytes: { } max }:
                ext["maxBytes"] = max;
                break;
            case HttpError.PreconditionFailed pf:
                ext["preconditionKind"] = pf.Condition.ToString();
                break;
            case HttpError.PreconditionRequired pr:
                ext["preconditionKind"] = pr.Condition.ToString();
                break;
        }
    }

    private static string ToWireKind(Error error) => error switch
    {
        Error.InvalidInput => "unprocessable-content",
        Error.InvariantViolation => "unprocessable-content",
        Error.AuthenticationRequired => "unauthorized",
        Error.RateLimited => "too-many-requests",
        Error.Unavailable => "service-unavailable",
        Error.Unexpected u when u.ReasonCode == "not_implemented" => "not-implemented",
        Error.Unexpected => "internal-server-error",
        Error.Aggregate => "multi",
        _ => error.Kind,
    };
}

/// <summary>JSON shape used for rule violations in ProblemDetails extensions.</summary>
public sealed record RuleViolationProblemDetail(string Code, string? Detail, string[] Fields);