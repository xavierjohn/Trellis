namespace Trellis.Asp;

using Microsoft.AspNetCore.Builder;

/// <summary>Declares pagination metadata on Minimal API endpoints and route groups.</summary>
public static class PaginationEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Declares cursor and limit query metadata without binding or parsing request values.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="cursorParameter">The query parameter containing the opaque cursor.</param>
    /// <param name="limitParameter">The query parameter containing the requested page size.</param>
    /// <param name="max">The maximum applied page size.</param>
    /// <param name="defaultSize">The requested page size when the limit is absent.</param>
    /// <param name="policy">How explicitly requested page sizes above the maximum are handled.</param>
    /// <returns>The original builder for chaining.</returns>
    /// <remarks>
    /// Native OpenAPI documentation requires the optional <c>Trellis.Asp.OpenApi</c>
    /// adapter and its transformer registration in the host's <c>AddOpenApi</c> callback.
    /// Endpoint declarations override route-group declarations. Keep the settings aligned with
    /// <see cref="HttpRequestPaginationExtensions.TryCreatePageRequest"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">The query parameter names are invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size settings or policy are invalid.</exception>
    public static TBuilder WithPagination<TBuilder>(
        this TBuilder builder,
        string cursorParameter = "cursor",
        string limitParameter = "limit",
        int max = PageSize.Max,
        int defaultSize = PageSize.Default,
        PageSizeLimitPolicy policy = PageSizeLimitPolicy.Clamp)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var declaration = new PaginationAttribute(cursorParameter, limitParameter, max, defaultSize, policy);
        builder.Add(endpoint => endpoint.Metadata.Add(declaration));
        return builder;
    }
}
