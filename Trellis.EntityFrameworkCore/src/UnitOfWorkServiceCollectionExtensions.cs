namespace Trellis.EntityFrameworkCore;

using global::Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
        InsertTransactionalBehavior(services);
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
        services.AddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
        return services;
    }

    /// <summary>
    /// Inserts <see cref="TransactionalCommandBehavior{TMessage,TResponse}"/> after the last
    /// <see cref="IPipelineBehavior{TMessage,TResponse}"/> registration to ensure it runs
    /// innermost (closest to the handler). If no behaviors are registered yet, appends at the end.
    /// Detects both open-generic and closed-generic behavior registrations.
    /// </summary>
    private static void InsertTransactionalBehavior(IServiceCollection services)
    {
        var descriptor = ServiceDescriptor.Scoped(
            typeof(IPipelineBehavior<,>), typeof(TransactionalCommandBehavior<,>));

        var lastBehaviorIndex = -1;
        for (var i = 0; i < services.Count; i++)
        {
            var serviceType = services[i].ServiceType;
            if (serviceType == typeof(IPipelineBehavior<,>)
                || (serviceType.IsGenericType
                    && serviceType.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>)))
                lastBehaviorIndex = i;
        }

        if (lastBehaviorIndex >= 0)
            services.Insert(lastBehaviorIndex + 1, descriptor);
        else
            services.Add(descriptor);
    }
}
