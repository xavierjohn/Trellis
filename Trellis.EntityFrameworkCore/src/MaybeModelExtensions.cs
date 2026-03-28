namespace Trellis.EntityFrameworkCore;

using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

/// <summary>
/// Diagnostics helpers for inspecting resolved EF Core mappings for <see cref="Maybe{T}"/> properties.
/// </summary>
public static class MaybeModelExtensions
{
    /// <summary>
    /// Returns the resolved <see cref="Maybe{T}"/> mappings discovered in the EF Core model.
    /// </summary>
    /// <param name="model">The EF Core model.</param>
    /// <returns>A list describing each resolved <see cref="Maybe{T}"/> mapping.</returns>
    public static IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var mappings = new List<MaybePropertyMapping>();

        foreach (var entityType in model.GetEntityTypes().OrderBy(entity => entity.Name, StringComparer.Ordinal))
        {
            if (entityType.ClrType is null)
                continue;

            foreach (var maybeProperty in MaybePropertyResolver.GetMaybeProperties(entityType.ClrType))
            {
                var mappedProperty = entityType.FindProperty(maybeProperty.StorageMemberName);

                if (mappedProperty is not null)
                {
                    // Scalar Maybe<T> — mapped as a nullable backing field column
                    var providerClrType = mappedProperty.GetTypeMapping().Converter?.ProviderClrType;
                    mappings.Add(new MaybePropertyMapping(
                        entityType.Name,
                        entityType.ClrType,
                        maybeProperty.PropertyName,
                        maybeProperty.StorageMemberName,
                        maybeProperty.InnerType,
                        maybeProperty.StoreType,
                        IsMapped: true,
                        mappedProperty.IsNullable,
                        mappedProperty.GetColumnName(),
                        providerClrType));
                }
                else
                {
                    // Owned Maybe<T> (e.g., Maybe<Money>) — mapped as an optional owned navigation.
                    // Read actual metadata from the owned entity type's properties.
                    var navigation = entityType.FindNavigation(maybeProperty.StorageMemberName);
                    var isOwnedMapping = navigation?.TargetEntityType.IsOwned() == true;

                    string? columnName = null;
                    if (isOwnedMapping)
                    {
                        // Use the first owned property's column name as the representative column
                        var ownedProps = navigation!.TargetEntityType.GetDeclaredProperties()
                            .Where(p => !p.IsShadowProperty())
                            .ToList();
                        if (ownedProps.Count > 0)
                            columnName = ownedProps[0].GetColumnName();
                    }

                    // Maybe<T> is always optional by definition — the owned instance can be absent
                    mappings.Add(new MaybePropertyMapping(
                        entityType.Name,
                        entityType.ClrType,
                        maybeProperty.PropertyName,
                        maybeProperty.StorageMemberName,
                        maybeProperty.InnerType,
                        maybeProperty.StoreType,
                        IsMapped: isOwnedMapping,
                        IsNullable: isOwnedMapping,
                        columnName,
                        ProviderClrType: null));
                }
            }
        }

        return mappings;
    }

    /// <summary>
    /// Returns the resolved <see cref="Maybe{T}"/> mappings discovered in the EF Core model.
    /// </summary>
    /// <param name="dbContext">The DbContext whose model should be inspected.</param>
    /// <returns>A list describing each resolved <see cref="Maybe{T}"/> mapping.</returns>
    public static IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return dbContext.Model.GetMaybePropertyMappings();
    }

    /// <summary>
    /// Produces a readable debug string showing how each <see cref="Maybe{T}"/> property resolved.
    /// </summary>
    /// <param name="model">The EF Core model.</param>
    /// <returns>A debug string summarizing all resolved <see cref="Maybe{T}"/> mappings.</returns>
    public static string ToMaybeMappingDebugString(this IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var mappings = model.GetMaybePropertyMappings();
        if (mappings.Count == 0)
            return "No Maybe<T> mappings were discovered.";

        var builder = new StringBuilder();

        foreach (var mapping in mappings)
        {
            builder.Append(mapping.EntityTypeName)
                .Append('.')
                .Append(mapping.PropertyName)
                .Append(" => mappedBackingField=")
                .Append(mapping.MappedBackingFieldName)
                .Append(", column=")
                .Append(mapping.ColumnName ?? "<none>")
                .Append(", storeType=")
                .Append(mapping.StoreType.Name)
                .Append(", providerType=")
                .Append(mapping.ProviderClrType?.Name ?? "<none>")
                .Append(", mapped=")
                .Append(mapping.IsMapped)
                .Append(", nullable=")
                .Append(mapping.IsNullable)
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Produces a readable debug string showing how each <see cref="Maybe{T}"/> property resolved.
    /// </summary>
    /// <param name="dbContext">The DbContext whose model should be inspected.</param>
    /// <returns>A debug string summarizing all resolved <see cref="Maybe{T}"/> mappings.</returns>
    public static string ToMaybeMappingDebugString(this DbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        return dbContext.Model.ToMaybeMappingDebugString();
    }
}