namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Shared naming convention for generated <see cref="Maybe{T}"/> storage members.
/// Used by <see cref="MaybeConvention"/> and <see cref="MaybeQueryableExtensions"/>
/// to convert a property name to its <c>_camelCase</c> storage member name.
/// </summary>
internal static class MaybeFieldNaming
{
    /// <summary>
    /// Converts a property name (e.g., <c>Phone</c>) to the generated storage member name
    /// (e.g., <c>_phone</c>) emitted by the <c>MaybePartialPropertyGenerator</c>.
    /// </summary>
    internal static string ToStorageMemberName(string propertyName) =>
        propertyName.Length == 1
            ? $"_{char.ToLowerInvariant(propertyName[0])}"
            : $"_{char.ToLowerInvariant(propertyName[0])}{propertyName[1..]}";
}