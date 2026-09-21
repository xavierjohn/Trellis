namespace Trellis.EntityFrameworkCore;

/// <summary>A resolved relational column belonging to a scalar or owned <see cref="Maybe{T}"/> value.</summary>
/// <param name="PropertyPath">EF property name, prefixed by nested ownership navigation names.</param>
/// <param name="TableName">The actual mapped table name.</param>
/// <param name="Schema">The mapped schema, or null for the provider's default.</param>
/// <param name="ColumnName">The actual store column name, including convention prefixes or explicit overrides.</param>
/// <param name="ColumnType">The provider's relational column type.</param>
/// <param name="IsNullable">Whether the store column permits null, rather than the CLR property's nullability.</param>
public sealed record MaybeColumnMapping(
    string PropertyPath,
    string TableName,
    string? Schema,
    string ColumnName,
    string ColumnType,
    bool IsNullable);
