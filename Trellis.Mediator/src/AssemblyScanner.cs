namespace Trellis.Mediator;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

internal static class AssemblyScanner
{
    internal static void ValidateAssemblies(Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Length == 0)
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));
        for (var i = 0; i < assemblies.Length; i++)
        {
            if (assemblies[i] is null)
                throw new ArgumentException($"Assembly at index [{i}] is null.", nameof(assemblies));
        }
    }

    [RequiresUnreferencedCode("Scans assemblies for handler implementations.")]
    [RequiresDynamicCode("Registers closed generic handler service types discovered at runtime.")]
    internal static void RegisterHandlers(
        IServiceCollection services,
        Assembly[] assemblies,
        Type handlerInterfaceDefinition)
    {
        foreach (var assembly in assemblies)
            foreach (var type in GetLoadableTypes(assembly))
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                foreach (var iface in type.GetInterfaces())
                {
                    if (iface.IsGenericType && iface.GetGenericTypeDefinition() == handlerInterfaceDefinition)
                        services.TryAddEnumerable(ServiceDescriptor.Scoped(iface, type));
                }
            }
    }

    [RequiresUnreferencedCode("Calls Assembly.GetTypes().")]
    internal static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().ToArray();
        }
    }
}
