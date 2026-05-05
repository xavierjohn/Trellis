namespace Trellis.Asp;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Trellis;

/// <summary>
/// Internal helper that writes failure responses (ProblemDetails / ValidationProblem) and emits
/// RFC-required companion headers (<c>Allow</c>, <c>Retry-After</c>, <c>Content-Range</c>,
/// <c>WWW-Authenticate</c>) using the supplied per-call status code.
/// </summary>
internal static class ResponseFailureWriter
{
    public static Task WriteAsync(HttpContext httpContext, Error error, int statusCode)
    {
        EmitCompanionHeaders(error, httpContext.Response, statusCode);

        Microsoft.AspNetCore.Http.IResult inner;
        if (error is Error.UnprocessableContent unprocessable
            && (unprocessable.Fields.Items.Length > 0 || unprocessable.Rules.Items.Length > 0))
        {
            var errors = unprocessable.Fields.Items
                .GroupBy(fv => JsonPointerToMvc.Translate(fv.Field.Path))
                .ToDictionary(g => g.Key, g => g.Select(fv => fv.Detail ?? fv.ReasonCode).ToArray());

            var validationDetail = statusCode >= 500 ? "An internal error occurred." : unprocessable.Detail;
            inner = Microsoft.AspNetCore.Http.Results.ValidationProblem(
                errors,
                validationDetail,
                instance: null,
                statusCode,
                extensions: BuildExtensions(error, unprocessable.Rules));
        }
        else
        {
            var detail = statusCode >= 500 ? "An internal error occurred." : error.Detail;
            var rules = error is Error.UnprocessableContent uc ? uc.Rules : default;
            inner = Microsoft.AspNetCore.Http.Results.Problem(
                detail,
                instance: null,
                statusCode,
                extensions: BuildExtensions(error, rules));
        }

        return inner.ExecuteAsync(httpContext);
    }

    private static void EmitCompanionHeaders(Error error, HttpResponse response, int statusCode)
    {
        switch (error)
        {
            // Skip the Allow header when the typed payload is empty: an empty Allow array is
            // unusual to construct intentionally (any 405 response should list at least one
            // method) and emitting `Allow:` (empty) on round-trip turns a missing/unknown
            // upstream method list into a concrete-but-empty wire header. Mirrors the
            // status-aware skip applied to WWW-Authenticate below.
            case Error.MethodNotAllowed mae when mae.Allow.Items.Length > 0:
                response.Headers["Allow"] = string.Join(", ", mae.Allow.Items);
                break;

            case Error.TooManyRequests { RetryAfter: not null } tmr:
                response.Headers["Retry-After"] = tmr.RetryAfter.ToHeaderValue();
                break;

            case Error.ServiceUnavailable { RetryAfter: not null } sue:
                response.Headers["Retry-After"] = sue.RetryAfter.ToHeaderValue();
                break;

            // Skip the Content-Range header when CompleteLength == 0: zero is the default value
            // for the long field, so a `RangeNotSatisfiable(0)` instance — typically synthesized
            // when an upstream 416 omits the Content-Range header — would otherwise be rendered
            // as `Content-Range: bytes */0`, fabricating a zero-length-resource signal that the
            // upstream did not send. Callers who genuinely need to communicate a 0-length
            // resource can construct the header explicitly outside the typed-error path.
            case Error.RangeNotSatisfiable rnse when rnse.CompleteLength > 0:
                response.Headers["Content-Range"] = $"{rnse.Unit} */{rnse.CompleteLength}";
                break;

            // Gated on the resolved status code: WWW-Authenticate is RFC 9110 §11.6.1
            // tied specifically to 401. If WithErrorMapping promotes Error.Unauthorized
            // to a non-401 status, suppress the header rather than mislead clients into
            // attempting re-authentication. Mirrors the m-13 status-aware design used
            // by ValidationProblem detail scrubbing.
            case Error.Unauthorized unauth when statusCode == 401 && unauth.Challenges.Items.Length > 0:
                foreach (var challenge in unauth.Challenges.Items)
                    response.Headers.Append("WWW-Authenticate", FormatChallenge(challenge));
                break;
        }
    }

    /// <summary>
    /// Formats a single <see cref="AuthChallenge"/> as a <c>WWW-Authenticate</c> header value
    /// per RFC 9110 §11.6.1. Parameter values are always emitted as quoted-strings (RFC 9110
    /// §5.6.4); embedded <c>"</c> and <c>\</c> are backslash-escaped.
    /// </summary>
    private static string FormatChallenge(AuthChallenge challenge)
    {
        if (challenge.Params is null || challenge.Params.Count == 0)
            return challenge.Scheme;

        var sb = new StringBuilder(challenge.Scheme);
        sb.Append(' ');
        var first = true;
        foreach (var kv in challenge.Params)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(kv.Key).Append('=').Append('"');
            AppendQuotedString(sb, kv.Value);
            sb.Append('"');
        }

        return sb.ToString();
    }

    private static void AppendQuotedString(StringBuilder sb, string value)
    {
        foreach (var ch in value)
        {
            if (ch is '"' or '\\') sb.Append('\\');
            sb.Append(ch);
        }
    }

    private static Dictionary<string, object?> BuildExtensions(Error error, EquatableArray<RuleViolation> rules)
    {
        var ext = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["code"] = error.Code,
            ["kind"] = error.Kind,
        };

        if (error is Error.InternalServerError ise)
            ext["faultId"] = ise.FaultId;

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
}

/// <summary>JSON shape used for rule violations in ProblemDetails extensions.</summary>
public sealed record RuleViolationProblemDetail(string Code, string? Detail, string[] Fields);