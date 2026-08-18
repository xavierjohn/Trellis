namespace Trellis.Asp;

using System;
using System.Collections.Generic;
using System.Linq;
using Trellis;

/// <summary>
/// Resolves the <c>collection</c> segment used when the framework synthesises a canonical
/// resource URI for <c>ProblemDetails.Instance</c> from a <see cref="ResourceRef"/>.
/// Override entries take precedence; otherwise the registry falls back to a naive plural
/// (<c>type.ToLowerInvariant() + "s"</c>).
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton via <c>TryAddSingleton</c> by <c>AddTrellisAsp</c> (which
/// leaves the registry empty by default), and pre-populated with the supplied overrides by
/// <c>AddResourceCollectionName</c> and <c>AddResourceCollectionNames</c>. Resource types
/// without an override fall back to the naive plural; calling <c>AddTrellisAsp</c> alone
/// means every type uses the naive plural. When none of those helpers are called, the
/// registry is absent from DI and <see cref="ResponseFailureWriter"/> falls back to an
/// internal empty registry.
/// </para>
/// <para>
/// Override lookups are case-insensitive (<see cref="StringComparer.OrdinalIgnoreCase"/>)
/// so a domain that emits both <c>ResourceRef.For&lt;Person&gt;()</c> and
/// <c>ResourceRef.For("person", id)</c> sees the same plural.
/// </para>
/// </remarks>
public sealed class ResourceCollectionNameRegistry
{
    private readonly Dictionary<string, string> _overrides;

    /// <summary>Creates an empty registry. Every lookup falls back to the naive plural.</summary>
    public ResourceCollectionNameRegistry()
        : this(Enumerable.Empty<ResourceCollectionNameOverride>())
    {
    }

    /// <summary>
    /// Creates a registry pre-populated from the supplied overrides. Used by DI when one or
    /// more <see cref="ResourceCollectionNameOverride"/> singletons have been registered.
    /// </summary>
    /// <param name="overrides">The override entries; may be empty.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="overrides"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when two overrides assign different collection names to the same resource type
    /// (matched case-insensitively). Identical overrides are coalesced silently so the same
    /// attribute discovered through overlapping assembly scans does not throw.
    /// </exception>
    public ResourceCollectionNameRegistry(IEnumerable<ResourceCollectionNameOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in overrides)
        {
            if (entry is null)
                continue;

            if (string.IsNullOrWhiteSpace(entry.ResourceType) || string.IsNullOrWhiteSpace(entry.CollectionName))
            {
                throw new InvalidOperationException(
                    "ResourceCollectionNameOverride entries must have non-empty ResourceType and CollectionName.");
            }

            if (!ResourceCollectionNameAttribute.IsSafePathSegment(entry.CollectionName))
            {
                throw new InvalidOperationException(
                    $"Collection name '{entry.CollectionName}' for resource type '{entry.ResourceType}' must be a single URL-safe path segment of RFC 3986 unreserved characters only (ASCII letters and digits, '-', '.', '_', '~').");
            }

            if (_overrides.TryGetValue(entry.ResourceType, out var existing))
            {
                if (!string.Equals(existing, entry.CollectionName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Duplicate ResourceCollectionName override for resource type '{entry.ResourceType}': '{existing}' vs '{entry.CollectionName}'.");
                }

                continue;
            }

            _overrides[entry.ResourceType] = entry.CollectionName;
        }
    }

    /// <summary>
    /// Resolves the collection segment for a given <see cref="ResourceRef.Type"/> value.
    /// Returns the registered override <b>verbatim</b> when one is present (case-insensitive
    /// lookup, case-preserving result — an override registered as <c>"People"</c> emits
    /// <c>/People/{id}</c>); otherwise returns the naive plural
    /// <c>resourceType.ToLowerInvariant() + "s"</c>. Callers wanting a lowercase guarantee
    /// must lowercase the override at registration time.
    /// </summary>
    /// <param name="resourceType">The resource type name as carried on <see cref="ResourceRef"/>.</param>
    /// <returns>The collection segment to substitute into the synthesised URI.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="resourceType"/> is null or whitespace.</exception>
    public string Resolve(string resourceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        return _overrides.TryGetValue(resourceType, out var name)
            ? name
            : resourceType.ToLowerInvariant() + "s";
    }
}