namespace Trellis.Asp;

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Reads what an endpoint binds, and from where, out of the API description the framework already
/// builds for it.
/// </summary>
/// <remarks>
/// <para>
/// This is evidence, not inference. ApiExplorer reports the same binding map for a controller
/// action and for a minimal API endpoint, so a violation can be located without either host
/// guessing, and without the two hosts disagreeing about the same request.
/// </para>
/// <para>
/// Two things are read. The query and header parameter <b>names</b>, which locate a violation that
/// names one of them; and whether the endpoint binds a request <b>body</b> at all, which decides
/// whether anything the URL does not account for can be attributed to one. Body member names are
/// not read back, because a body parameter names a type rather than a set of members and recovering
/// the members would mean reflecting over the DTO graph — which the AOT-friendly projection path
/// avoids. That limits the <c>pointer</c>, not the <c>in</c>: a domain producer may raise a name
/// that matches no member, but on an endpoint that binds a body, <c>body</c> is still where the
/// value came from.
/// </para>
/// </remarks>
internal static class EndpointBinding
{
    private static readonly ConditionalWeakTable<Endpoint, Binding> Cache = new();

    /// <summary>
    /// What an endpoint is known to bind from the request.
    /// </summary>
    /// <param name="QueryParameters">The query parameter names it binds.</param>
    /// <param name="HeaderParameters">The header parameter names it binds.</param>
    /// <param name="BindsBody">Whether it binds a request body.</param>
    internal sealed record Binding(
        IReadOnlyList<string> QueryParameters,
        IReadOnlyList<string> HeaderParameters,
        bool BindsBody)
    {
        /// <summary>Nothing is known — ApiExplorer is absent, or described no matching endpoint.</summary>
        public static Binding Unknown { get; } = new([], [], false);
    }

    /// <summary>
    /// Returns what <paramref name="endpoint"/> binds, or <see cref="Binding.Unknown"/> when
    /// ApiExplorer is not registered or describes no matching endpoint.
    /// </summary>
    /// <remarks>
    /// Enumerating the description groups walks every endpoint in the application, so the answer
    /// is cached against the endpoint instance, which lives as long as the routing table does.
    /// An application that never registered ApiExplorer derives nothing and falls back to whatever
    /// the endpoint declared.
    /// </remarks>
    public static Binding For(HttpContext httpContext, Endpoint endpoint)
    {
        if (Cache.TryGetValue(endpoint, out var cached)) return cached;

        var discovered = Discover(httpContext.RequestServices?.GetService<IApiDescriptionGroupCollectionProvider>(), endpoint);
        Cache.AddOrUpdate(endpoint, discovered);
        return discovered;
    }

    private static Binding Discover(IApiDescriptionGroupCollectionProvider? provider, Endpoint endpoint)
    {
        if (provider is null) return Binding.Unknown;

        var action = endpoint.Metadata.GetMetadata<ActionDescriptor>();
        var handler = endpoint.Metadata.GetMetadata<MethodInfo>();
        if (action is null && handler is null) return Binding.Unknown;

        ApiDescription? candidate = null;
        List<ApiDescription>? shared = null;

        foreach (var group in provider.ApiDescriptionGroups.Items)
        {
            foreach (var description in group.Items)
            {
                if (action is not null && ReferenceEquals(description.ActionDescriptor, action))
                    return Read(description);

                if (handler is null || !SharesHandler(description, handler)) continue;

                if (candidate is null)
                    candidate = description;
                else
                    (shared ??= [candidate]).Add(description);
            }
        }

        if (shared is not null) return Read(Disambiguate(shared, endpoint));

        return Read(candidate);
    }

    /// <summary>
    /// Picks the description for <paramref name="endpoint"/> from candidates that share a handler.
    /// </summary>
    /// <remarks>
    /// One method can be mapped to several routes, and every route's description then carries the
    /// same handler. The routes still differ, and so do their binding maps — a name that is a
    /// route parameter on one is a query parameter on another — so the handler alone cannot say
    /// which description belongs to the endpoint being served. The route template and HTTP method
    /// settle it. When they cannot, this returns nothing rather than a guess, and the caller falls
    /// back to the declared residual.
    /// </remarks>
    private static ApiDescription? Disambiguate(List<ApiDescription> candidates, Endpoint endpoint)
    {
        if (endpoint is not RouteEndpoint route) return null;

        var template = Normalize(route.RoutePattern.RawText);
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        ApiDescription? found = null;

        foreach (var candidate in candidates)
        {
            if (!string.Equals(Normalize(candidate.RelativePath), template, StringComparison.OrdinalIgnoreCase))
                continue;

            if (methods is not null
                && candidate.HttpMethod is not null
                && !methods.Contains(candidate.HttpMethod, StringComparer.OrdinalIgnoreCase))
                continue;

            if (found is not null) return null;

            found = candidate;
        }

        return found;
    }

    private static string Normalize(string? template) => template?.Trim('/') ?? string.Empty;

    /// <summary>
    /// Reports whether a description was built for the same handler method as the endpoint.
    /// </summary>
    /// <remarks>
    /// A controller action and its description share the <see cref="ActionDescriptor"/> instance,
    /// which identifies it uniquely. A minimal API endpoint has no action descriptor of its own,
    /// but ApiExplorer copies the endpoint's metadata onto the descriptor it synthesises, so the
    /// handler's <see cref="MethodInfo"/> is shared instead. That is a weaker signal — one method
    /// can serve several routes — so a handler match narrows the field rather than settling it.
    /// </remarks>
    private static bool SharesHandler(ApiDescription description, MethodInfo handler)
    {
        var metadata = description.ActionDescriptor?.EndpointMetadata;
        if (metadata is null) return false;

        for (var i = 0; i < metadata.Count; i++)
        {
            if (ReferenceEquals(metadata[i], handler)) return true;
        }

        return false;
    }

    private static Binding Read(ApiDescription? description)
    {
        if (description is null) return Binding.Unknown;

        List<string>? queryParameters = null;
        List<string>? headerParameters = null;
        var bindsBody = false;

        foreach (var parameter in description.ParameterDescriptions)
        {
            var source = parameter.Source?.Id;

            if (string.Equals(source, BindingSource.Query.Id, StringComparison.Ordinal))
                (queryParameters ??= []).Add(parameter.Name);
            else if (string.Equals(source, BindingSource.Header.Id, StringComparison.Ordinal))
                (headerParameters ??= []).Add(parameter.Name);
            else if (string.Equals(source, BindingSource.Body.Id, StringComparison.Ordinal)
                || string.Equals(source, BindingSource.Form.Id, StringComparison.Ordinal))
                bindsBody = true;
        }

        return queryParameters is null && headerParameters is null && !bindsBody
            ? Binding.Unknown
            : new Binding(
                queryParameters is null ? [] : [.. queryParameters],
                headerParameters is null ? [] : [.. headerParameters],
                bindsBody);
    }
}
