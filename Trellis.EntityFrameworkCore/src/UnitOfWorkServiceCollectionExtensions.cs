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
    /// </summary>
    /// <typeparam name="TContext">The concrete <see cref="DbContext"/> type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddDbContext&lt;AppDbContext&gt;(...);
    /// services.AddTrellisUnitOfWork&lt;AppDbContext&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddTrellisUnitOfWork<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddScoped<IUnitOfWork, EfUnitOfWork<TContext>>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionalCommandBehavior<,>));
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
}
