namespace Trellis.Asp;

using Trellis;

/// <summary>
/// A single resource-type-to-collection-name override registered via
/// <c>AddResourceCollectionName</c> or discovered by
/// <c>AddResourceCollectionNames(Assembly)</c>. Registered as a DI singleton; the
/// <see cref="ResourceCollectionNameRegistry"/> consumes the full enumeration at
/// activation time.
/// </summary>
/// <param name="ResourceType">
/// The resource type name as it appears on <see cref="ResourceRef.Type"/>. Matching is
/// case-insensitive (ordinal).
/// </param>
/// <param name="CollectionName">
/// The collection segment to substitute in the synthesised <c>ProblemDetails.Instance</c>
/// URI (for example <c>"people"</c>).
/// </param>
public sealed record ResourceCollectionNameOverride(string ResourceType, string CollectionName);