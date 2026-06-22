namespace Trellis.EntityFrameworkCore;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trellis.Mediator;

/// <summary>
/// Extension methods for registering <see cref="IUnitOfWork"/> and the
/// <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> pipeline behavior.
/// </summary>
public static class UnitOfWorkServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EfUnitOfWork{TContext}"/> as the <see cref="IUnitOfWork"/>
    /// implementation and adds the <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/>
    /// pipeline behavior so that command handlers automatically commit on success.
    /// <para>
    /// The behavior is inserted after the last existing <see cref="IPipelineBehavior{TMessage,TResponse}"/>
    /// registration (innermost position, closest to the handler). For correct ordering, call this
    /// method <b>after</b> <c>AddTrellisBehaviors()</c> and any other behavior registrations so that
    /// commit failures are visible to outer behaviors (logging, tracing, exception handling).
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">The concrete <see cref="DbContext"/> type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;(...);
    /// services.AddTrellisBehaviors();           // register other behaviors first
    /// services.AddTrellisUnitOfWork&lt;AppDbContext&gt;(); // commit behavior goes innermost
    /// </code>
    /// </example>
    public static IServiceCollection AddTrellisUnitOfWork<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
        AddTrackedAggregateSourceForwarder(services);
        services.AddTransactionalCommandBehavior();
        return services;
    }

    /// <summary>
    /// Registers <see cref="EfUnitOfWork{TContext}"/> as the <see cref="IUnitOfWork"/>
    /// implementation without registering the pipeline behavior.
    /// Use this when you want manual commit control (e.g., background jobs)
    /// or when the Mediator pipeline is not in use.
    /// </summary>
    /// <typeparam name="TContext">The concrete <see cref="DbContext"/> type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTrellisUnitOfWorkWithoutBehavior<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
        AddTrackedAggregateSourceForwarder(services);
        return services;
    }

    /// <summary>
    /// Forwards <see cref="ITrackedAggregateSource"/> through the registered <see cref="IUnitOfWork"/>
    /// so the same scoped instance backs both contracts. If a consumer pre-registered a custom
    /// <see cref="IUnitOfWork"/> that does not implement <see cref="ITrackedAggregateSource"/>, the
    /// forwarder throws at resolution time with an actionable message rather than silently handing
    /// out a different EF instance whose snapshot is never populated.
    /// </summary>
    private static void AddTrackedAggregateSourceForwarder(IServiceCollection services) =>
        services.TryAddScoped<ITrackedAggregateSource>(static sp =>
        {
            var unitOfWork = sp.GetRequiredService<IUnitOfWork>();
            if (unitOfWork is ITrackedAggregateSource source)
                return source;

            throw new InvalidOperationException(
                $"The registered IUnitOfWork implementation '{unitOfWork.GetType().FullName}' does not implement " +
                $"ITrackedAggregateSource. Replace it with one that does (e.g. EfUnitOfWork<TContext>) or register " +
                $"ITrackedAggregateSource explicitly to use TrackedAggregateDomainEventDispatchBehavior.");
        });
}
