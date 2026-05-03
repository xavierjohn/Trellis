namespace Trellis.Asp.Authorization;

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trellis.Authorization;

/// <summary>
/// Extension methods for registering actor providers in ASP.NET Core DI.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ClaimsActorProvider"/> as the scoped <see cref="IActorProvider"/>
    /// with configurable claim mapping for any OIDC/JWT identity provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Optional delegate to customize <see cref="ClaimsActorOptions"/>.
    /// Override <see cref="ClaimsActorOptions.ActorIdClaim"/> and
    /// <see cref="ClaimsActorOptions.PermissionsClaim"/> to match your token format.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // Auth0
    /// builder.Services.AddClaimsActorProvider(opts =>
    /// {
    ///     opts.ActorIdClaim = "sub";
    ///     opts.PermissionsClaim = "permissions";
    /// });
    ///
    /// // Keycloak
    /// builder.Services.AddClaimsActorProvider(opts =>
    /// {
    ///     opts.ActorIdClaim = "sub";
    ///     opts.PermissionsClaim = "realm_access.roles";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddClaimsActorProvider(
        this IServiceCollection services,
        Action<ClaimsActorOptions>? configure = null)
    {
        services.AddHttpContextAccessor();

        if (configure is not null)
            services.Configure(configure);
        else
            services.Configure<ClaimsActorOptions>(_ => { });

        // Replace (not append): each AddXxxActorProvider helper claims the IActorProvider
        // slot. Without Replace, calling two helpers leaves two descriptors and
        // GetServices<IActorProvider>() returns both — order-dependent and surprising.
        services.Replace(ServiceDescriptor.Scoped<IActorProvider, ClaimsActorProvider>());

        return services;
    }

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

        services.Replace(ServiceDescriptor.Scoped<IActorProvider, EntraActorProvider>());

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
    /// <b>Security:</b> The provider throws <see cref="InvalidOperationException"/> unconditionally
    /// when resolved outside of the Development environment, regardless of whether an
    /// <c>X-Test-Actor</c> header is present on the request. Use <see cref="AddEntraActorProvider"/>
    /// for production deployments.
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

        services.Replace(ServiceDescriptor.Scoped<IActorProvider, DevelopmentActorProvider>());

        return services;
    }

    /// <summary>
    /// Registers a caching decorator around the specified <see cref="IActorProvider"/> implementation.
    /// The inner provider is resolved per-scope and wrapped with <see cref="CachingActorProvider"/>
    /// so that multiple calls within the same request return the same actor instance.
    /// </summary>
    /// <typeparam name="T">The concrete <see cref="IActorProvider"/> implementation to cache.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // Wrap a DB-backed provider that resolves permissions from a database
    /// services.AddCachingActorProvider&lt;DatabaseActorProvider&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddCachingActorProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services)
        where T : class, IActorProvider
    {
        services.AddHttpContextAccessor();
        services.AddScoped<T>();
        services.Replace(ServiceDescriptor.Scoped<IActorProvider>(sp =>
            new CachingActorProvider(
                sp.GetRequiredService<T>(),
                sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>())));

        return services;
    }
}