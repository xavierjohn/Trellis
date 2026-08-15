namespace Trellis.Asp;

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Trellis;

/// <summary>
/// Internal helper that resolves the HTTP status code for an <see cref="Error"/> using the standard
/// precedence chain: per-call <c>ErrorMapper</c> &#x2192; per-call <c>ErrorOverrides</c> (walking the
/// error&#x2019;s base-type chain) &#x2192; ambient <see cref="TrellisAspOptions"/> &#x2192;
/// <see cref="TrellisAspOptions.SystemDefault"/>.
/// </summary>
/// <remarks>
/// <para>
/// Centralised so both <c>TrellisHttpResult&lt;TDomain,TBody&gt;</c> and <c>TrellisErrorOnlyResult</c>
/// (which carry differently-typed <c>HttpResponseOptions</c>) cannot drift apart over time.
/// </para>
/// <para>
/// A per-call <c>ErrorMapper</c> that returns a value outside the legal HTTP status range
/// (100&#x2013;599) is treated as <em>no match</em> and resolution continues down the chain. This
/// makes the documented <c>err =&gt; err is SomeError ? 410 : default</c> idiom safe: the
/// <c>default</c> arm yields 0, which would otherwise be written to the response verbatim.
/// </para>
/// </remarks>
internal static class ErrorStatusCodeResolver
{
    /// <summary>
    /// Lowest status code the resolver will accept from a caller-supplied mapper.
    /// </summary>
    private const int MinHttpStatusCode = 100;

    /// <summary>
    /// Highest status code the resolver will accept from a caller-supplied mapper.
    /// </summary>
    private const int MaxHttpStatusCode = 599;

    public static int Resolve(
        HttpContext httpContext,
        Error error,
        Func<Error, int>? errorMapper,
        Dictionary<Type, int>? errorOverrides)
    {
        // A mapper that returns something outside the legal HTTP range is signalling
        // "this error is not mine" rather than requesting that status. The documented idiom
        // (`err => err is OutOfStockError ? 410 : default`) relies on `default` — that is, 0 —
        // as the non-match arm, so writing the mapper's return value verbatim would put an
        // unwritable status on the response for every error the mapper does not claim.
        if (errorMapper is not null && IsValidHttpStatusCode(errorMapper(error), out var mapped))
            return mapped;

        if (errorOverrides is { Count: > 0 })
        {
            var t = error.GetType();
            while (t is not null && t != typeof(object))
            {
                if (errorOverrides.TryGetValue(t, out var sc))
                    return sc;

                t = t.BaseType;
            }
        }

        var ambient = httpContext.RequestServices?.GetService<TrellisAspOptions>() ?? TrellisAspOptions.SystemDefault;
        return ambient.GetStatusCode(error);
    }

    /// <summary>
    /// Throws when <paramref name="statusCode"/> falls outside the writable HTTP range. Used by the
    /// typed registration helpers, where every call is a deliberate request for a specific status —
    /// unlike a delegate mapper, whose out-of-range return is the documented "not mine" signal and
    /// is therefore skipped at resolution time rather than rejected.
    /// </summary>
    internal static void ValidateStatusCode(int statusCode, string paramName)
    {
        if (statusCode is < MinHttpStatusCode or > MaxHttpStatusCode)
            throw new ArgumentOutOfRangeException(
                paramName,
                statusCode,
                $"HTTP status code must be between {MinHttpStatusCode} and {MaxHttpStatusCode}.");
    }

    private static bool IsValidHttpStatusCode(int candidate, out int statusCode)
    {
        statusCode = candidate;
        return candidate is >= MinHttpStatusCode and <= MaxHttpStatusCode;
    }
}
