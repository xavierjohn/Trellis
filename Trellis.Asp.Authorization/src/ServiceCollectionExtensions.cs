namespace Trellis.Asp.Authorization;

using Microsoft.Extensions.DependencyInjection;
using Trellis.Authorization;

/// <summary>
/// Extension methods for registering actor providers in ASP.NET Core DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EntraActorProvider"/> as the scoped <see cref="IActorProvider"/>
    /// and configures <see cref="EntraActorOptions"/> with default Entra v2.0 claim mappings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional delegate to customize <see cref="EntraActorOptions"/>.
    /// Override <see cref="EntraActorOptions.MapPermissions"/> to flatten roles into granular permissions,
    /// <see cref="EntraActorOptions.MapForbiddenPermissions"/> to populate deny lists, or
    /// <see cref="EntraActorOptions.MapAttributes"/> to add custom ABAC attributes.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddEntraActorProvider(options =>
    /// {
    ///     options.MapPermissions = claims => claims
    ///         .Where(c => c.Type == "roles")
    ///         .SelectMany(role => RolePermissionMap[role.Value])
    ///         .ToHashSet();
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddEntraActorProvider(
        this IServiceCollection services,
        Action<EntraActorOptions>? configure = null)
    {
        services.AddHttpContextAccessor();

        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<EntraActorOptions>(_ => { });

        services.AddScoped<IActorProvider, EntraActorProvider>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="DevelopmentActorProvider"/> as the scoped <see cref="IActorProvider"/>
    /// for development and testing environments. Reads actor identity from the
    /// <c>X-Test-Actor</c> HTTP header and falls back to a configurable default actor.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional delegate to customize <see cref="DevelopmentActorOptions"/>.
    /// Set <see cref="DevelopmentActorOptions.DefaultPermissions"/> to grant permissions
    /// when no header is present, or <see cref="DevelopmentActorOptions.ThrowOnMalformedHeader"/>
    /// to reject invalid headers instead of falling back.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Security:</b> The provider throws <see cref="InvalidOperationException"/> if the
    /// <c>X-Test-Actor</c> header is present in a Production environment.
    /// Use <see cref="AddEntraActorProvider"/> for production deployments.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// if (builder.Environment.IsDevelopment())
    /// {
    ///     builder.Services.AddDevelopmentActorProvider(options =>
    ///     {
    ///         options.DefaultPermissions = new HashSet&lt;string&gt;
    ///         {
    ///             "orders:create", "orders:read"
    ///         };
    ///     });
    /// }
    /// else
    /// {
    ///     builder.Services.AddEntraActorProvider();
    /// }
    /// </code>
    /// </example>
    public static IServiceCollection AddDevelopmentActorProvider(
        this IServiceCollection services,
        Action<DevelopmentActorOptions>? configure = null)
    {
        services.AddHttpContextAccessor();
        services.AddLogging();

        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<DevelopmentActorOptions>(_ => { });

        services.AddScoped<IActorProvider, DevelopmentActorProvider>();

        return services;
    }
}