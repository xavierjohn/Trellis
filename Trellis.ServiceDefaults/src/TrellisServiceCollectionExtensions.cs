namespace Trellis.ServiceDefaults;

using System;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for applying opinionated Trellis service composition defaults.
/// </summary>
public static class TrellisServiceCollectionExtensions
{
    /// <summary>
    /// Applies Trellis integration modules in canonical order.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the Trellis service modules to apply.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    /// <remarks>
    /// This method only wires Trellis integration services. Application-owned services such as
    /// <c>AddDbContext&lt;TContext&gt;(...)</c> and <c>AddMediator(...)</c> stay explicit.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTrellis(options => options
    ///     .UseAsp()
    ///     .UseMediator()
    ///     .UseFluentValidation(typeof(Program).Assembly)
    ///     .UseEntityFrameworkUnitOfWork&lt;AppDbContext&gt;());
    /// </code>
    /// </example>
    public static IServiceCollection AddTrellis(
        this IServiceCollection services,
        Action<TrellisServiceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new TrellisServiceBuilder(services);
        configure(builder);
        builder.Apply();
        return services;
    }
}