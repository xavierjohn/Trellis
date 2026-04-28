namespace Trellis.Asp.Routing;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods that register Trellis value objects as ASP.NET Core route constraints.
/// </summary>
/// <remarks>
/// Without these registrations, a route template such as <c>"products/{id:ProductId}"</c> silently
/// returns 500 at request time because <c>RouteOptions.ConstraintMap</c> has no entry for the
/// value object type. This extension scans assemblies for Trellis value objects (types implementing
/// <see cref="IParsable{TSelf}"/> and <see cref="IScalarValue{TSelf, TPrimitive}"/>) and registers
/// each one under its simple type name (e.g., <c>ProductId</c>).
/// </remarks>
public static class RouteConstraintRegistrationExtensions
{
    /// <summary>
    /// Registers a route constraint for every Trellis value object discovered in the supplied
    /// assemblies. The constraint key is the value object's simple type name.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">
    /// Assemblies to scan. If empty, scans the calling assembly and the assembly defining
    /// <see cref="IScalarValue{TSelf, TPrimitive}"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddTrellisRouteConstraints(typeof(ProductId).Assembly);
    /// // Then in a controller / minimal endpoint:
    /// // app.MapGet("/products/{id:ProductId}", (ProductId id) => ...)
    /// </code>
    /// </example>
    [RequiresUnreferencedCode("Reflects over assembly types to discover Trellis value objects. Use AddTrellisRouteConstraint<T> for AOT scenarios.")]
    [RequiresDynamicCode("Constructs generic route-constraint types via reflection. Use AddTrellisRouteConstraint<T> for AOT scenarios.")]
    public static IServiceCollection AddTrellisRouteConstraints(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (assemblies is null || assemblies.Length == 0)
        {
            assemblies =
            [
                Assembly.GetCallingAssembly(),
                typeof(IScalarValue<,>).Assembly,
            ];
        }

        var voTypes = DiscoverValueObjectTypes(assemblies);

        services.Configure<RouteOptions>(options => RegisterAll(options, voTypes));

        return services;
    }

    [RequiresUnreferencedCode("Reads RouteOptions.ConstraintMap and assigns generic constraint types.")]
    [RequiresDynamicCode("Constructs generic route-constraint types via reflection.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Public callers carry RequiresUnreferencedCode.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Public callers carry RequiresDynamicCode.")]
    private static void RegisterAll(RouteOptions options, List<(string Name, Type Type)> voTypes)
    {
        foreach (var (name, type) in voTypes)
        {
            if (options.ConstraintMap.ContainsKey(name))
                continue;

            var constraintType = typeof(TrellisValueObjectRouteConstraint<>).MakeGenericType(type);
            options.ConstraintMap[name] = constraintType;
        }
    }

    /// <summary>
    /// Registers a single route constraint for the specified Trellis value object type. AOT-safe.
    /// </summary>
    /// <typeparam name="T">The value object type. Must implement <see cref="IParsable{TSelf}"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="constraintName">
    /// Optional constraint name. Defaults to the simple type name of <typeparamref name="T"/>.
    /// </param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTrellisRouteConstraint<T>(
        this IServiceCollection services,
        string? constraintName = null)
        where T : IParsable<T>
    {
        ArgumentNullException.ThrowIfNull(services);

        var name = constraintName ?? typeof(T).Name;

        services.Configure<RouteOptions>(options => ConfigureSingle(options, name, typeof(TrellisValueObjectRouteConstraint<T>)));

        return services;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Caller registers a known constraint type at compile time.")]
    private static void ConfigureSingle(
        RouteOptions options,
        string name,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type constraintType)
    {
        // Match the assembly-scanning variant: preserve any existing entry under the same name.
        if (!options.ConstraintMap.ContainsKey(name))
            options.ConstraintMap[name] = constraintType;
    }

    [RequiresUnreferencedCode("Reflects over assembly types.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Discovery is gated by RequiresUnreferencedCode on public entry point.")]
    private static List<(string Name, Type Type)> DiscoverValueObjectTypes(Assembly[] assemblies)
    {
        var results = new List<(string, Type)>();
        var scalarValueInterface = typeof(IScalarValue<,>);

        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type.IsAbstract || !type.IsClass || type.IsGenericTypeDefinition)
                    continue;

                if (!ImplementsParsable(type))
                    continue;

                if (!ImplementsScalarValue(type, scalarValueInterface))
                    continue;

                results.Add((type.Name, type));
            }
        }

        ThrowIfDuplicateConstraintNames(results);
        return results;
    }

    private static void ThrowIfDuplicateConstraintNames(List<(string Name, Type Type)> discoveredTypes)
    {
        var duplicates = discoveredTypes
            .GroupBy(x => x.Name, StringComparer.Ordinal)
            .Where(g => g.Select(x => x.Type).Distinct().Count() > 1)
            .Select(g => new
            {
                Name = g.Key,
                Types = g.Select(x => x.Type).Distinct()
                    .OrderBy(t => t.FullName, StringComparer.Ordinal)
                    .ToArray(),
            })
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        if (duplicates.Length == 0)
            return;

        var details = string.Join(
            "; ",
            duplicates.Select(x => $"{x.Name}: {string.Join(", ", x.Types.Select(t => $"{t.FullName} ({t.Assembly.GetName().Name})"))}"));

        throw new InvalidOperationException(
            "Multiple Trellis value object types were discovered with the same route constraint name. " +
            "Route constraints are registered by simple type name, so registration would be ambiguous. " +
            "Resolve the duplicate names or stop scanning conflicting assemblies. " +
            $"Conflicts: {details}");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Discovery is gated by RequiresUnreferencedCode on public entry point.")]
    private static bool ImplementsParsable(Type type)
    {
        var parsableDefinition = typeof(IParsable<>);
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType
                && iface.GetGenericTypeDefinition() == parsableDefinition
                && iface.GetGenericArguments()[0] == type)
            {
                return true;
            }
        }

        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Discovery is gated by RequiresUnreferencedCode on public entry point.")]
    private static bool ImplementsScalarValue(Type type, Type scalarValueInterface)
    {
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == scalarValueInterface)
                return true;
        }

        return false;
    }
}