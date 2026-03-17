namespace Trellis.EntityFrameworkCore.Generator;

using System;
using System.Linq;

/// <summary>
/// Metadata about a partial property of type <c>Maybe&lt;T&gt;</c> that requires source generation.
/// Implements <see cref="IEquatable{T}"/> so the incremental generator pipeline can skip
/// re-generation when the semantic model hasn't changed.
/// </summary>
internal sealed class MaybePropertyInfo : IEquatable<MaybePropertyInfo>
{
    /// <summary>The namespace of the containing type.</summary>
    public readonly string Namespace;

    /// <summary>The name of the containing type (e.g., "Customer").</summary>
    public readonly string TypeName;

    /// <summary>The accessibility of the containing type (e.g., "public").</summary>
    public readonly string TypeAccessibility;

    /// <summary>Whether the containing type is a record.</summary>
    public readonly bool IsRecord;

    /// <summary>The property name (e.g., "Phone").</summary>
    public readonly string PropertyName;

    /// <summary>The accessibility of the property (e.g., "public").</summary>
    public readonly string PropertyAccessibility;

    /// <summary>The accessibility of the setter, if different from the property (e.g., "private").</summary>
    public readonly string SetterAccessibility;

    /// <summary>Whether the property uses an init-only accessor instead of a set accessor.</summary>
    public readonly bool IsInitOnly;

    /// <summary>The fully-qualified inner type name (e.g., "PhoneNumber" or "System.DateTime").</summary>
    public readonly string InnerTypeName;

    /// <summary>The minimal inner type name for display (e.g., "PhoneNumber" or "DateTime").</summary>
    public readonly string InnerTypeShortName;

    /// <summary>Whether the inner type is a value type (struct).</summary>
    public readonly bool InnerTypeIsValueType;

    /// <summary>The nesting chain of parent types if the type is nested (e.g., ["OuterClass"]).</summary>
    public readonly string[] NestingParents;

    /// <summary>The namespace- and nesting-qualified type path used for grouping and hint names.</summary>
    public readonly string TypePath;

    public MaybePropertyInfo(
        string @namespace,
        string typeName,
        string typeAccessibility,
        bool isRecord,
        string propertyName,
        string propertyAccessibility,
        string setterAccessibility,
        bool isInitOnly,
        string innerTypeName,
        string innerTypeShortName,
        bool innerTypeIsValueType,
        string[] nestingParents,
        string typePath)
    {
        Namespace = @namespace;
        TypeName = typeName;
        TypeAccessibility = typeAccessibility;
        IsRecord = isRecord;
        PropertyName = propertyName;
        PropertyAccessibility = propertyAccessibility;
        SetterAccessibility = setterAccessibility;
        IsInitOnly = isInitOnly;
        InnerTypeName = innerTypeName;
        InnerTypeShortName = innerTypeShortName;
        InnerTypeIsValueType = innerTypeIsValueType;
        NestingParents = nestingParents;
        TypePath = typePath;
    }

    public bool Equals(MaybePropertyInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Namespace == other.Namespace
            && TypeName == other.TypeName
            && TypeAccessibility == other.TypeAccessibility
            && IsRecord == other.IsRecord
            && PropertyName == other.PropertyName
            && PropertyAccessibility == other.PropertyAccessibility
            && SetterAccessibility == other.SetterAccessibility
            && IsInitOnly == other.IsInitOnly
            && InnerTypeName == other.InnerTypeName
            && InnerTypeShortName == other.InnerTypeShortName
            && InnerTypeIsValueType == other.InnerTypeIsValueType
            && TypePath == other.TypePath
            && NestingParents.SequenceEqual(other.NestingParents);
    }

    public override bool Equals(object? obj) => Equals(obj as MaybePropertyInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Namespace);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TypeName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(PropertyName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(InnerTypeName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TypePath);
            hash = (hash * 31) + InnerTypeIsValueType.GetHashCode();
            return hash;
        }
    }
}