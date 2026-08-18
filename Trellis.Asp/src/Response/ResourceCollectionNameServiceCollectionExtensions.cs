namespace Trellis.Asp;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trellis;

/// <summary>
/// Extension methods for registering <see cref="ResourceCollectionNameOverride"/> entries
/// and the consuming <see cref="ResourceCollectionNameRegistry"/>.
/// </summary>
public static class ResourceCollectionNameServiceCollectionExtensions
{
    /// <summary>
    /// Registers a single resource-type-to-collection-name override. The <typeparamref name="TResource"/>
    /// CLR name (with generic backtick suffix stripped via <see cref="ResourceRef.FormatTypeName"/>)
    /// is used as the lookup key — match the name that <see cref="ResourceRef.For{TResource}(object?)"/>
    /// emits. Safe under trimming/AOT — does not perform assembly scanning.
    /// </summary>
    /// <typeparam name="TResource">The CLR resource type whose <see cref="ResourceRef.Type"/> name should map to <paramref name="collectionName"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="collectionName">The collection segment to substitute (e.g. <c>"people"</c>).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddResourceCollectionName<TResource>(
        this IServiceCollection services,
        string collectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddResourceCollectionName(ResourceRef.FormatTypeName(typeof(TResource)), collectionName);
    }

    /// <summary>
    /// Registers a single resource-type-to-collection-name override using an explicit
    /// resource-type string. Use when <see cref="ResourceRef.For(string, object?)"/> is
    /// emitted with a custom name that does not match any CLR type.
    /// Safe under trimming/AOT — does not perform assembly scanning.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="resourceType">The <see cref="ResourceRef.Type"/> value (case-insensitive).</param>
    /// <param name="collectionName">The collection segment to substitute (e.g. <c>"people"</c>).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddResourceCollectionName(
        this IServiceCollection services,
        string resourceType,
        string collectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        if (!ResourceCollectionNameAttribute.IsSafePathSegment(collectionName))
        {
            throw new ArgumentException(
                $"Collection name '{collectionName}' must be a single URL-safe path segment of RFC 3986 unreserved characters only (ASCII letters and digits, '-', '.', '_', '~').",
                nameof(collectionName));
        }

        services.AddSingleton(new ResourceCollectionNameOverride(resourceType, collectionName));
        services.TryAddSingleton<ResourceCollectionNameRegistry>();
        return services;
    }

    /// <summary>
    /// Scans the supplied assembly for types annotated with
    /// <see cref="ResourceCollectionNameAttribute"/> and registers one
    /// <see cref="ResourceCollectionNameOverride"/> per match. The resource-type lookup key
    /// is <see cref="ResourceRef.FormatTypeName"/> of the annotated type, matching the name
    /// emitted by <see cref="ResourceRef.For{TResource}(object?)"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Conflicting overrides for the same resource type (different collection names) are
    /// detected when the registry is activated, not here, so a misconfiguration surfaces
    /// on first resolve rather than tearing down composition root.
    /// </remarks>
    [RequiresUnreferencedCode("Assembly scanning may load types that have been trimmed. For AOT/trimming use AddResourceCollectionName<T>(name) for each override.")]
    public static IServiceCollection AddResourceCollectionNames(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsInterface)
                continue;

            var attribute = type.GetCustomAttribute<ResourceCollectionNameAttribute>(inherit: false);
            if (attribute is null)
                continue;

            services.AddSingleton(new ResourceCollectionNameOverride(ResourceRef.FormatTypeName(type), attribute.Name));
        }

        services.TryAddSingleton<ResourceCollectionNameRegistry>();
        return services;
    }

    /// <summary>
    /// Scans the supplied assemblies for types annotated with
    /// <see cref="ResourceCollectionNameAttribute"/>. Convenience overload for hosts that
    /// register overrides from more than one assembly in a single call. Each assembly is
    /// scanned in order; identical overrides across assemblies coalesce silently when the
    /// registry is activated, conflicting overrides throw.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The service collection for chaining.</returns>
    [RequiresUnreferencedCode("Assembly scanning may load types that have been trimmed. For AOT/trimming use AddResourceCollectionName<T>(name) for each override.")]
    public static IServiceCollection AddResourceCollectionNames(this IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            services.AddResourceCollectionNames(assembly);
        }

        return services;
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return GetTypesUnreferenced(assembly);
        }
        catch (ReflectionTypeLoadException ex)
        {
            return Array.FindAll(ex.Types, t => t is not null)!;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
            Justification = "Caller is marked [RequiresUnreferencedCode]; this scoped helper exists only to centralize the suppression.")]
        static Type[] GetTypesUnreferenced(Assembly assembly) => assembly.GetTypes();
    }
}