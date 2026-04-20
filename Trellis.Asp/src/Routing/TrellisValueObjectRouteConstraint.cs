namespace Trellis.Asp.Routing;

using System;
using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// Route constraint that delegates parsing to the value object's <see cref="IParsable{TSelf}"/> implementation.
/// Used by <c>AddTrellisRouteConstraints</c> to make <c>{id:ProductId}</c>-style route templates work for any
/// Trellis value object.
/// </summary>
/// <typeparam name="T">The value object type. Must implement <see cref="IParsable{TSelf}"/>.</typeparam>
public sealed class TrellisValueObjectRouteConstraint<T> : IRouteConstraint
    where T : IParsable<T>
{
    /// <inheritdoc />
    public bool Match(
        HttpContext? httpContext,
        IRouter? route,
        string routeKey,
        RouteValueDictionary values,
        RouteDirection routeDirection)
    {
        ArgumentNullException.ThrowIfNull(routeKey);
        ArgumentNullException.ThrowIfNull(values);

        if (!values.TryGetValue(routeKey, out var raw) || raw is null)
            return false;

        var text = Convert.ToString(raw, CultureInfo.InvariantCulture);
        return T.TryParse(text, CultureInfo.InvariantCulture, out _);
    }
}
