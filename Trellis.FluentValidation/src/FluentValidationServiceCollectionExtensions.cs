namespace Trellis.FluentValidation;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using global::FluentValidation;
using global::Mediator;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering Trellis FluentValidation discovery into the Mediator pipeline.
/// </summary>
public static class FluentValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FluentValidationBehavior{TMessage, TResponse}"/> as an open-generic
    /// <see cref="IPipelineBehavior{TMessage, TResponse}"/> so that any <see cref="IValidator{T}"/>
    /// registered in the DI container will run automatically before its message reaches the handler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Validators are NOT scanned by this overload — register them explicitly
    /// (e.g., <c>services.AddScoped&lt;IValidator&lt;CreateOrderCommand&gt;, CreateOrderCommandValidator&gt;()</c>)
    /// or use the assembly-scanning overload
    /// <see cref="AddTrellisFluentValidation(IServiceCollection, Assembly[])"/>.
    /// </para>
    /// <para>
    /// Call this <b>after</b> <c>AddTrellisBehaviors()</c> so FluentValidation discovery runs after
    /// the compile-time <c>IValidate</c> behavior, and <b>before</b> <c>AddTrellisUnitOfWork()</c>
    /// so commits stay innermost.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTrellisBehaviors();
    /// services.AddTrellisFluentValidation();
    /// services.AddScoped&lt;IValidator&lt;CreateOrderCommand&gt;, CreateOrderCommandValidator&gt;();
    /// services.AddTrellisUnitOfWork&lt;AppDbContext&gt;();
    /// </code>
    /// </example>
    public static IServiceCollection AddTrellisFluentValidation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(FluentValidationBehavior<,>));
        return services;
    }

    /// <summary>
    /// Registers <see cref="FluentValidationBehavior{TMessage, TResponse}"/> and scans the supplied
    /// assemblies for concrete <see cref="IValidator{T}"/> implementations, registering each one
    /// as a scoped service so the behavior can discover and execute it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">The assemblies to scan for validator implementations.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Uses reflection over <see cref="Assembly.GetTypes"/>, so this overload is not AOT/trimming
    /// compatible. For AOT scenarios, use the parameterless overload and register validators
    /// explicitly.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTrellisFluentValidation(typeof(CreateOrderCommandValidator).Assembly);
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Assembly scanning requires unreferenced types. Use the parameterless overload with explicit registration for AOT/trimming scenarios.")]
    [RequiresDynamicCode("Constructs closed generic IValidator<T> service types at runtime. Use the parameterless overload for AOT scenarios.")]
    public static IServiceCollection AddTrellisFluentValidation(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0)
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));

        AddTrellisFluentValidation(services);

        var validatorDef = typeof(IValidator<>);
        foreach (var assembly in assemblies)
        {
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                foreach (var iface in type.GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == validatorDef)
                        services.AddScoped(iface, type);
                }
            }
        }

        return services;
    }

    [RequiresUnreferencedCode("Calls Assembly.GetTypes().")]
    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return [.. ex.Types.Where(t => t is not null)!];
        }
    }
}
