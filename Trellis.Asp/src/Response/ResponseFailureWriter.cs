namespace Trellis.Asp;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Trellis;

/// <summary>
/// Internal helper that writes failure responses (ProblemDetails / ValidationProblem) and emits
/// RFC-required companion headers (<c>Allow</c>, <c>Retry-After</c>, <c>Content-Range</c>) using
/// the supplied per-call status code.
/// </summary>
internal static class ResponseFailureWriter
{
    public static Task WriteAsync(HttpContext httpContext, Error error, int statusCode)
    {
        EmitCompanionHeaders(error, httpContext.Response);

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

    private static void EmitCompanionHeaders(Error error, HttpResponse response)
    {
        switch (error)
        {
            case Error.MethodNotAllowed mae:
                response.Headers["Allow"] = string.Join(", ", mae.Allow.Items);
                break;

            case Error.TooManyRequests { RetryAfter: not null } tmr:
                response.Headers["Retry-After"] = tmr.RetryAfter.ToHeaderValue();
                break;

            case Error.ServiceUnavailable { RetryAfter: not null } sue:
                response.Headers["Retry-After"] = sue.RetryAfter.ToHeaderValue();
                break;

            case Error.RangeNotSatisfiable rnse:
                response.Headers["Content-Range"] = $"{rnse.Unit} */{rnse.CompleteLength}";
                break;
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