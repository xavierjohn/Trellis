namespace Trellis.EntityFrameworkCore;

/// <summary>The resolved storage shape of a <see cref="Maybe{T}"/> property.</summary>
public enum MaybeStorageKind
{
    /// <summary>No mapped scalar property or owned navigation was found.</summary>
    Unmapped,

    /// <summary>A mapped scalar backing field.</summary>
    Scalar,

    /// <summary>An owned navigation without a relational table mapping.</summary>
    Owned,

    /// <summary>An owned value sharing its immediate owner's table and schema.</summary>
    TableSplit,

    /// <summary>An owned value mapped to a different table or schema from its immediate owner.</summary>
    SeparateTable
}
