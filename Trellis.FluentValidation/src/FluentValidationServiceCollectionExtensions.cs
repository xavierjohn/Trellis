namespace Trellis.FluentValidation;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using global::FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Mediator;

/// <summary>
/// Extension methods for plugging FluentValidation into the Trellis Mediator validation stage.
/// </summary>
public static class FluentValidationServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FluentValidationMessageValidatorAdapter{TMessage}"/> as the
    /// open-generic <see cref="IMessageValidator{TMessage}"/> implementation. Every
    /// <see cref="IValidator{T}"/> registered for the message in DI will then run inside the
    /// existing <see cref="ValidationBehavior{TMessage, TResponse}"/> and contribute its
    /// failures to the aggregated <see cref="Error.UnprocessableContent"/> response.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This overload is AOT-friendly: it relies on .NET's open-generic DI registration to
    /// construct closed adapter types at runtime, with no reflection over assemblies. Validators
    /// must be registered explicitly (e.g.,
    /// <c>services.AddScoped&lt;IValidator&lt;CreateOrderCommand&gt;, CreateOrderCommandValidator&gt;()</c>).
    /// </para>
    /// <para>
    /// FluentValidation does not introduce an additional pipeline behavior; it extends the
    /// existing <see cref="ValidationBehavior{TMessage, TResponse}"/> via the
    /// <see cref="IMessageValidator{TMessage}"/> abstraction.
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
        services.AddScoped(typeof(IMessageValidator<>), typeof(FluentValidationMessageValidatorAdapter<>));
        return services;
    }

    /// <summary>
    /// Registers the FluentValidation adapter and scans the supplied assemblies for concrete
    /// <see cref="IValidator{T}"/> implementations, registering each one as a scoped service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">The assemblies to scan for validator implementations.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Uses reflection over <see cref="Assembly.GetTypes"/> and constructs closed
    /// <see cref="IValidator{T}"/> service types at runtime, so this overload is not AOT or
    /// trimming compatible. For AOT scenarios, call the parameterless overload and register
    /// validators explicitly.
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

