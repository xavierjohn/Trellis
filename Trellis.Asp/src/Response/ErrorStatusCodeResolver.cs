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
/// Centralised so both <c>TrellisHttpResult&lt;TDomain,TBody&gt;</c> and <c>TrellisErrorOnlyResult</c>
/// (which carry differently-typed <c>HttpResponseOptions</c>) cannot drift apart over time.
/// </remarks>
internal static class ErrorStatusCodeResolver
{
    public static int Resolve(
        HttpContext httpContext,
        Error error,
        Func<Error, int>? errorMapper,
        Dictionary<Type, int>? errorOverrides)
    {
        if (errorMapper is not null)
            return errorMapper(error);

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
}
