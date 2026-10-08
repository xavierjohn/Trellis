namespace Trellis.Asp;

using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

/// <summary>Builds named-route pagination URLs without requiring an API-versioning package.</summary>
/// <remarks>
/// Endpoint discovery and compatible route groups are cached per endpoint data source until its
/// change token signals an update. Route values and destination policies remain request-specific.
/// Custom sources must signal endpoint and metadata changes. Unlike the former per-link endpoint
/// scan, cached discovery does not support unsignaled mutations.
/// </remarks>
public static class HttpContextPaginationExtensions
{
    private static readonly ConditionalWeakTable<EndpointDataSource, CandidateCache> Cache = new();

    /// <summary>Creates a request-scoped URL builder for the next-page callback.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="routeName">The named destination route.</param>
    /// <param name="routeValues">Maps the cursor and applied limit to route values, which are cloned.</param>
    /// <param name="routeResolver">An optional per-builder policy overriding the host's pagination policy.</param>
    /// <returns>A builder to invoke during this request.</returns>
    public static Func<Cursor, int, string> PageUrl(
        this HttpContext httpContext,
        string routeName,
        Func<Cursor, int, RouteValueDictionary> routeValues,
        Func<PageUrlRouteContext, Endpoint>? routeResolver = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(routeValues);
        var builder = httpContext.PageUrl(routeName, (cursor, _, limit) => routeValues(cursor, limit), routeResolver);
        return (cursor, limit) => builder(cursor, PageDirection.Next, limit);
    }

    /// <summary>Creates a request-scoped, direction-aware pagination URL builder.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <param name="routeName">The named destination route.</param>
    /// <param name="routeValues">Maps the cursor, direction, and applied limit to route values, which are cloned.</param>
    /// <param name="routeResolver">An optional per-builder policy overriding the host's pagination policy.</param>
    /// <returns>A builder to invoke during this request.</returns>
    public static Func<Cursor, PageDirection, int, string> PageUrl(
        this HttpContext httpContext,
        string routeName,
        Func<Cursor, PageDirection, int, RouteValueDictionary> routeValues,
        Func<PageUrlRouteContext, Endpoint>? routeResolver = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);
        ArgumentNullException.ThrowIfNull(routeValues);
        return (cursor, direction, appliedLimit) =>
        {
            var consumerValues = routeValues(cursor, direction, appliedLimit)
                ?? throw new InvalidOperationException(
                    "PageUrl: the routeValues callback returned null. Return a RouteValueDictionary (possibly empty) instead.");
            var values = new RouteValueDictionary(consumerValues);
            var context = new PageUrlRouteContext(httpContext, routeName, FindCandidates(httpContext, routeName), values);
            var resolver = routeResolver ?? httpContext.RequestServices.GetService<TrellisAspOptions>()?.PageUrlRouteResolver;
            var target = resolver is null
                ? ResolveDefaultTarget(context)
                : resolver(context) ?? throw new InvalidOperationException("PageUrl: the route resolver returned null.");

            if (!context.Candidates.Contains(target) && !IsActiveDestination(httpContext, routeName, target))
                throw new InvalidOperationException(
                    $"PageUrl: the route resolver selected an endpoint outside route '{routeName}'s link-enabled destinations.");

            var linkGenerator = httpContext.RequestServices.GetRequiredService<LinkGenerator>();
            return linkGenerator.GetUriByRouteValues(httpContext, routeName, values)
                ?? throw new InvalidOperationException(
                    $"PageUrl: LinkGenerator.GetUriByRouteValues returned null for route '{routeName}'. " +
                    "The supplied route values, combined with the current request's ambient route values, " +
                    "did not match the target route template.");
        };
    }

    private static ReadOnlyCollection<Endpoint> FindCandidates(HttpContext httpContext, string routeName)
    {
        var dataSource = httpContext.RequestServices.GetService<EndpointDataSource>();
        return dataSource is null
            ? ValidateCandidates([], routeName)
            : Cache.GetValue(dataSource, static _ => new()).FindCandidates(dataSource, routeName);
    }

    private static ReadOnlyCollection<Endpoint> ValidateCandidates(Endpoint[] candidates, string routeName)
    {
        if (candidates.Length == 0)
            throw new InvalidOperationException(
                $"PageUrl: no registered link-enabled endpoint has route name '{routeName}'. " +
                "Name and register the target, for example [HttpGet(Name = \"...\")] with MapControllers(), " +
                "or MapGet(...).WithName(\"...\").");

        if (candidates.Length > 1)
        {
            var pattern = (candidates[0] as RouteEndpoint)?.RoutePattern;
            if (pattern?.RawText is null || candidates.Any(endpoint =>
                endpoint is not RouteEndpoint routeEndpoint
                || !HaveMatchingPatterns(pattern, routeEndpoint.RoutePattern)))
                throw new InvalidOperationException(
                    $"PageUrl: route name '{routeName}' is ambiguous because its endpoints use different " +
                    "route templates, defaults, required values, or parameter policies. " +
                    "Give these destinations distinct route names; out-of-line policy objects must compare equal.");
        }

        return Array.AsReadOnly(candidates);
    }

    private sealed class CandidateCache
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<string, ReadOnlyCollection<Endpoint>> _validatedRoutes = new(StringComparer.Ordinal);
        private Dictionary<string, List<Endpoint>> _routes = new(StringComparer.Ordinal);
        private IChangeToken? _changeToken;

        public ReadOnlyCollection<Endpoint> FindCandidates(EndpointDataSource dataSource, string routeName)
        {
            lock (_lock)
            {
                if (_changeToken is null || _changeToken.HasChanged)
                {
                    IChangeToken changeToken;
                    Dictionary<string, List<Endpoint>> routes;
                    do
                    {
                        // Read the token first so a concurrent update cannot pair an old index with a new token.
                        changeToken = dataSource.GetChangeToken();
                        routes = new(StringComparer.Ordinal);
                        foreach (var endpoint in dataSource.Endpoints)
                        {
                            var name = endpoint.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName;
                            if (name is null
                                || endpoint.Metadata.GetMetadata<ISuppressLinkGenerationMetadata>()?.SuppressLinkGeneration == true)
                                continue;

                            if (!routes.TryGetValue(name, out var candidates))
                                routes.Add(name, candidates = []);
                            candidates.Add(endpoint);
                        }
                    } while (changeToken.HasChanged);

                    _routes = routes;
                    _changeToken = changeToken;
                    _validatedRoutes.Clear();
                }

                if (_validatedRoutes.TryGetValue(routeName, out var cached))
                    return cached;

                var result = ValidateCandidates(_routes.TryGetValue(routeName, out var endpoints) ? endpoints.ToArray() : [], routeName);
                _validatedRoutes.Add(routeName, result);
                return result;
            }
        }
    }

    private static bool HaveMatchingPatterns(RoutePattern expected, RoutePattern actual) =>
        string.Equals(expected.RawText, actual.RawText, StringComparison.OrdinalIgnoreCase)
        && HaveMatchingValues(expected, actual, expected.Defaults, actual.Defaults)
        && HaveMatchingValues(expected, actual, expected.RequiredValues, actual.RequiredValues)
        && expected.ParameterPolicies.Count == actual.ParameterPolicies.Count
        && expected.ParameterPolicies.All(pair =>
            actual.ParameterPolicies.TryGetValue(pair.Key, out var policies)
            && pair.Value.Count == policies.Count
            && pair.Value.Zip(policies).All(policyPair =>
                string.Equals(policyPair.First.Content, policyPair.Second.Content, StringComparison.Ordinal)
                && Equals(policyPair.First.ParameterPolicy, policyPair.Second.ParameterPolicy)));

    private static bool HaveMatchingValues(
        RoutePattern expectedPattern,
        RoutePattern actualPattern,
        IReadOnlyDictionary<string, object?> expected,
        IReadOnlyDictionary<string, object?> actual) =>
        expected.Count == actual.Count
        && expected.All(pair =>
            (IsNonUrlMvcSelector(expectedPattern, pair.Key) && IsNonUrlMvcSelector(actualPattern, pair.Key))
            || (actual.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value)));

    private static bool IsNonUrlMvcSelector(RoutePattern pattern, string key) =>
        (string.Equals(key, "controller", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "action", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key, "area", StringComparison.OrdinalIgnoreCase))
        && !pattern.Parameters.Any(parameter => string.Equals(parameter.Name, key, StringComparison.OrdinalIgnoreCase))
        && pattern.Defaults.TryGetValue(key, out var defaultValue)
        && pattern.RequiredValues.TryGetValue(key, out var requiredValue)
        && Equals(defaultValue, requiredValue);

    private static Endpoint ResolveDefaultTarget(PageUrlRouteContext context)
    {
        if (context.HttpContext.GetEndpoint() is { } current && HasRouteName(current, context.RouteName))
            return current;
        if (context.Candidates.Count == 1)
            return context.Candidates[0];

        throw new InvalidOperationException(
            $"PageUrl: route name '{context.RouteName}' is ambiguous. Give the destinations distinct route names " +
            "or configure TrellisAspOptions.PageUrlRouteResolver to select one destination.");
    }

    private static bool IsActiveDestination(HttpContext httpContext, string routeName, Endpoint endpoint) =>
        ReferenceEquals(httpContext.GetEndpoint(), endpoint) && HasRouteName(endpoint, routeName);

    private static bool HasRouteName(Endpoint endpoint, string routeName) =>
        string.Equals(endpoint.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName, routeName, StringComparison.Ordinal)
        && endpoint.Metadata.GetMetadata<ISuppressLinkGenerationMetadata>()?.SuppressLinkGeneration != true;
}
