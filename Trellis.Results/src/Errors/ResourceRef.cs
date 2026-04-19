namespace Trellis;

/// <summary>
/// Identifies a resource by its type and optional identifier. Used as the typed payload
/// for resource-oriented errors such as <see cref="Error.NotFound"/>, <see cref="Error.Conflict"/>,
/// <see cref="Error.Gone"/>, and <see cref="Error.PreconditionFailed"/>.
/// </summary>
/// <param name="Type">
/// The resource type name (e.g. <c>"User"</c>, <c>"Order"</c>). Should be the domain
/// concept, not a CLR type name. Required.
/// </param>
/// <param name="Id">
/// Optional identifier of the specific resource instance. May be null when the error
/// applies to the resource collection rather than a specific instance.
/// </param>
public readonly record struct ResourceRef(string Type, string? Id = null);
