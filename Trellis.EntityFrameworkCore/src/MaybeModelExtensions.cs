namespace Trellis.EntityFrameworkCore;

using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

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
                    var providerClrType = mappedProperty.GetTypeMapping().Converter?.ProviderClrType;
                    var columns = GetColumns(entityType, mappedProperty);
                    var firstColumn = columns.Items.FirstOrDefault();
                    mappings.Add(new MaybePropertyMapping(
                        entityType.Name,
                        entityType.ClrType,
                        maybeProperty.PropertyName,
                        maybeProperty.StorageMemberName,
                        maybeProperty.InnerType,
                        maybeProperty.StoreType,
                        IsMapped: true,
                        mappedProperty.IsNullable,
                        firstColumn?.ColumnName ?? mappedProperty.GetColumnName(),
                        providerClrType)
                    {
                        StorageKind = MaybeStorageKind.Scalar,
                        TableName = firstColumn?.TableName,
                        Schema = firstColumn?.Schema,
                        Columns = columns
                    });
                }
                else
                {
                    var navigation = entityType.FindNavigation(maybeProperty.StorageMemberName);
                    var ownedType = navigation?.TargetEntityType;
                    var isOwnedMapping = ownedType?.IsOwned() == true;

                    string? columnName = null;
                    string? tableName = null;
                    string? schema = null;
                    string? reason = null;
                    var kind = MaybeStorageKind.Unmapped;
                    var columns = EquatableArray<MaybeColumnMapping>.Empty;
                    if (isOwnedMapping)
                    {
                        columns = GetColumns(ownedType!);
                        var firstProperty = ownedType!.GetDeclaredProperties().FirstOrDefault(p => !p.IsShadowProperty());
                        columnName = columns.Items.FirstOrDefault(c => c.PropertyPath == firstProperty?.Name)?.ColumnName;
                        kind = MaybeStorageKind.Owned;
                        if (ownedType.GetProperties().Any(p => p.GetTypeMapping() is RelationalTypeMapping)
                            && ownedType.GetTableMappings().Any())
                        {
                            tableName = ownedType.GetTableName();
                            schema = ownedType.GetSchema();
                            kind = tableName == entityType.GetTableName() && schema == entityType.GetSchema()
                                ? MaybeStorageKind.TableSplit
                                : MaybeStorageKind.SeparateTable;
                            if (kind == MaybeStorageKind.SeparateTable)
                                reason = ownedType.FindAnnotation(CompositeValueObjectConvention.MaybeStorageReasonAnnotation)?.Value as string;
                        }
                    }

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
                        ProviderClrType: null)
                    {
                        StorageKind = kind,
                        TableName = tableName,
                        Schema = schema,
                        Columns = columns,
                        StorageReason = reason
                    });
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
                .Append(", storage=")
                .Append(mapping.StorageKind)
                .Append(", table=")
                .Append(QualifiedTable(mapping.TableName, mapping.Schema));
            if (mapping.StorageReason is not null)
                builder.Append(", reason=").Append(mapping.StorageReason);
            builder.AppendLine();

            foreach (var column in mapping.Columns)
                builder.Append("  ").Append(column.PropertyPath)
                    .Append(" => ").Append(QualifiedTable(column.TableName, column.Schema))
                    .Append('.').Append(column.ColumnName)
                    .Append(", type=").Append(column.ColumnType)
                    .Append(", nullable=").Append(column.IsNullable)
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

    private static string QualifiedTable(string? table, string? schema) =>
        table is null ? "<none>" : schema is null ? table : schema + "." + table;

    private static EquatableArray<MaybeColumnMapping> GetColumns(IEntityType entityType, IProperty? scalarProperty = null)
    {
        var columns = new List<MaybeColumnMapping>();
        AddColumns(entityType, "", scalarProperty, columns);
        return EquatableArray.From(columns
            .OrderBy(column => column.PropertyPath, StringComparer.Ordinal)
            .ThenBy(column => column.Schema, StringComparer.Ordinal)
            .ThenBy(column => column.TableName, StringComparer.Ordinal)
            .ThenBy(column => column.ColumnName, StringComparer.Ordinal));
    }

    private static void AddColumns(
        IEntityType entityType, string prefix, IProperty? scalarProperty, List<MaybeColumnMapping> columns)
    {
        if (entityType.GetProperties().Any(property => property.GetTypeMapping() is RelationalTypeMapping))
            foreach (var table in entityType.GetTableMappings())
                foreach (var mapping in table.ColumnMappings)
                    if (scalarProperty is null || mapping.Property == scalarProperty)
                        columns.Add(new MaybeColumnMapping(
                            prefix + mapping.Property.Name,
                            table.Table.Name,
                            table.Table.Schema,
                            mapping.Column.Name,
                            mapping.Column.StoreType,
                            mapping.Column.IsNullable));

        if (scalarProperty is not null)
            return;

        foreach (var navigation in entityType.GetNavigations()
            .Where(navigation => navigation.ForeignKey.IsOwnership && !navigation.IsOnDependent)
            .OrderBy(navigation => navigation.Name, StringComparer.Ordinal))
            AddColumns(navigation.TargetEntityType, prefix + navigation.Name + ".", null, columns);
    }
}