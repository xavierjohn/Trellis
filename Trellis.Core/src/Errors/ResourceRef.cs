namespace Trellis;

using System.Globalization;

/// <summary>
/// Identifies a resource by its type and optional identifier. Used as the typed payload
/// for resource-oriented errors such as <see cref="Error.NotFound"/>, <see cref="Error.Conflict"/>,
/// <see cref="Error.Gone"/>, and <see cref="Error.PreconditionFailed"/>.
/// </summary>
/// <param name="Type">
/// The resource type name (e.g. <c>"User"</c>, <c>"Order"</c>). Use
/// <see cref="For{TResource}(object?)"/> when the CLR type name is the desired resource name,
/// or <see cref="For(string, object?)"/> when a custom domain name is needed. Required.
/// </param>
/// <param name="Id">
/// Optional identifier of the specific resource instance. May be null when the error
/// applies to the resource collection rather than a specific instance.
/// </param>
public readonly record struct ResourceRef(string Type, string? Id = null)
{
    /// <summary>
    /// Creates a resource reference from an explicit resource type name and optional identifier.
    /// </summary>
    /// <param name="type">The resource type name.</param>
    /// <param name="id">Optional resource identifier.</param>
    /// <returns>A resource reference.</returns>
    public static ResourceRef For(string type, object? id = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return new(type, FormatId(id));
    }

    /// <summary>
    /// Creates a resource reference whose resource type is <c>typeof(TResource).Name</c>.
    /// </summary>
    /// <typeparam name="TResource">The resource type.</typeparam>
    /// <param name="id">Optional resource identifier.</param>
    /// <returns>A resource reference.</returns>
    public static ResourceRef For<TResource>(object? id = null) =>
        new(typeof(TResource).Name, FormatId(id));

    private static string? FormatId(object? id) =>
        id switch
        {
            null => null,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => id.ToString(),
        };
}