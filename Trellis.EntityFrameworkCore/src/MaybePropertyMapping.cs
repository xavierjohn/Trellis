namespace Trellis.EntityFrameworkCore;

/// <summary>
/// Describes how a <see cref="Maybe{T}"/> property resolved to an EF Core mapped backing field.
/// </summary>
/// <param name="EntityTypeName">The EF Core entity type name.</param>
/// <param name="EntityClrType">The entity CLR type.</param>
/// <param name="PropertyName">The original <see cref="Maybe{T}"/> CLR property name.</param>
/// <param name="MappedBackingFieldName">The source-generated private backing field name used by the EF model.</param>
/// <param name="InnerType">The inner <see cref="Maybe{T}"/> value type.</param>
/// <param name="StoreType">The EF Core property CLR type used for persistence.</param>
/// <param name="IsMapped">Whether the mapped backing field is present in the EF model.</param>
/// <param name="IsNullable">Whether the mapped backing field is nullable in the EF model.</param>
/// <param name="ColumnName">The relational column name, if available.</param>
/// <param name="ProviderClrType">The provider CLR type after value conversion, if any.</param>
public sealed record MaybePropertyMapping(
    string EntityTypeName,
    Type EntityClrType,
    string PropertyName,
    string MappedBackingFieldName,
    Type InnerType,
    Type StoreType,
    bool IsMapped,
    bool IsNullable,
    string? ColumnName,
    Type? ProviderClrType);