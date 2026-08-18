namespace Trellis.Asp;

using Microsoft.AspNetCore.Builder;

/// <summary>
/// <see cref="IApplicationBuilder"/> extensions for Trellis ASP.NET integration.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Wires the canonical Trellis ProblemDetails pipeline so unhandled exceptions and
    /// ASP.NET status-code short-circuits (404 / 405 / 415 / 5xx) become RFC 9457
    /// ProblemDetails responses with the enrichment registered by
    /// <see cref="ServiceCollectionExtensions.AddTrellisProblemDetails(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Equivalent to calling <c>app.UseExceptionHandler()</c> followed by
    /// <c>app.UseStatusCodePages()</c>. The first catches unhandled exceptions and
    /// rewrites them to 500 ProblemDetails; the second catches empty-body status-code
    /// responses (404 from a route miss, 405 from a method mismatch, 415 from a
    /// content-type mismatch) and rewrites them to ProblemDetails bodies.
    /// </para>
    /// <para>
    /// <b>Ordering.</b> Register this <em>early</em> in the pipeline, before
    /// <c>UseRouting</c> / endpoint middleware. <c>UseStatusCodePages</c> only
    /// rewrites status-code responses that come from downstream middleware, so it
    /// must be registered before any middleware that could emit one.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var app = builder.Build();
    /// app.UseTrellisProblemDetails();
    /// app.UseRouting();
    /// app.MapControllers();
    /// app.Run();
    /// </code>
    /// </example>
    public static IApplicationBuilder UseTrellisProblemDetails(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        return app;
    }
}