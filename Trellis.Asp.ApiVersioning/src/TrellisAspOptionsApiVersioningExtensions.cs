namespace Trellis.Asp.ApiVersioning;

/// <summary>Configures optional API-version-aware pagination through the existing ASP options slot.</summary>
public static class TrellisAspOptionsApiVersioningExtensions
{
    /// <summary>Enables destination-aware API-version handling for ordinary pagination builders.</summary>
    /// <param name="options">The options configured through AddTrellisAsp or UseAsp.</param>
    /// <returns>The options instance for chaining.</returns>
    /// <remarks>
    /// Register Asp.Versioning normally as well. Repeated calls replace the pagination policy;
    /// unrelated options are preserved. This does not change Location handling.
    /// </remarks>
    public static TrellisAspOptions UseVersionedPageUrls(this TrellisAspOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.PageUrlRouteResolver = static context => VersionedPageUrlResolver.Resolve(context);
        return options;
    }
}
