namespace Trellis.Testing.AspNetCore;

using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> that simplify
/// replacing EF Core database provider registrations in integration tests.
/// </summary>
public static class ServiceCollectionDbProviderExtensions
{
    /// <summary>
    /// Removes all existing EF Core provider registrations for <typeparamref name="TContext"/>
    /// and re-registers with a new provider configuration via <c>AddDbContext</c>. Use this in
    /// <c>WebApplicationFactory</c> tests to swap a production database provider
    /// (e.g., SQL Server) for a lightweight test provider (e.g., SQLite in-memory).
    /// </summary>
    /// <remarks>
    /// This method always re-registers using <c>AddDbContext&lt;TContext&gt;</c>.
    /// If the application registers the context via <c>AddDbContextFactory</c> or
    /// <c>AddPooledDbContextFactory</c>, use those APIs directly instead of this helper.
    /// </remarks>
    /// <typeparam name="TContext">The <see cref="DbContext"/> type to replace.</typeparam>
    /// <param name="services">The service collection to modify.</param>
    /// <param name="configureOptions">An action to configure the new <see cref="DbContextOptionsBuilder"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.ConfigureServices(services =&gt;
    ///     services.ReplaceDbProvider&lt;AppDbContext&gt;(options =&gt;
    ///         options.UseSqlite(connection).AddTrellisInterceptors()));
    /// </code>
    /// </example>
    public static IServiceCollection ReplaceDbProvider<[DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.NonPublicConstructors
        | DynamicallyAccessedMemberTypes.PublicProperties)] TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureOptions)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        services.RemoveAll<TContext>();
        services.RemoveAll<DbContextOptions<TContext>>();

        // EF Core registers additional provider-scoped services (e.g., IDbContextOptionsConfiguration<TContext>)
        // that carry the original provider configuration. Remove all EF Core services generic over TContext
        // to avoid dual-provider conflicts.
        var efCoreContextDescriptors = services
            .Where(d => d.ServiceType.IsConstructedGenericType
                && d.ServiceType.GenericTypeArguments.Contains(typeof(TContext))
                && (d.ServiceType.FullName?.Contains("EntityFrameworkCore", StringComparison.Ordinal) ?? false))
            .ToList();

        foreach (var descriptor in efCoreContextDescriptors)
            services.Remove(descriptor);

        services.AddDbContext<TContext>(configureOptions);
        return services;
    }
}
