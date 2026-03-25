namespace Trellis.PrimitiveValueObjectGenerator;

using System;
using System.Linq;

/// <summary>
/// Represents metadata about a partial class that requires source generation for value object functionality.
/// Used by the source generator to create factory methods, validation, and parsing logic.
/// </summary>
/// <remarks>
/// <para>
/// This class captures the essential information needed to generate the complementary partial class
/// that provides the public API for value objects inheriting from <see cref="RequiredGuid"/>, <see cref="RequiredString"/>,
/// <see cref="RequiredInt"/>, <see cref="RequiredDecimal"/>, or <see cref="RequiredEnum"/>.
/// </para>
/// <para>
/// The generator uses this information to create:
/// <list type="bullet">
/// <item>Static factory methods (NewUnique for GUIDs, TryCreate for all types)</item>
/// <item>Validation logic ensuring non-empty values</item>
/// <item>IParsable implementation for parsing support</item>
/// <item>JSON serialization attributes</item>
/// <item>Private constructors that call the base class (except for RequiredEnum)</item>
/// </list>
/// </para>
/// </remarks>
internal class RequiredPartialClassInfo : IEquatable<RequiredPartialClassInfo>
{
    /// <summary>
    /// Gets the namespace of the partial class.
    /// </summary>
    /// <value>
    /// The fully-qualified namespace (e.g., "MyApp.Domain.ValueObjects").
    /// </value>
    public readonly string NameSpace;

    /// <summary>
    /// Gets the name of the partial class.
    /// </summary>
    /// <value>
    /// The simple class name without namespace (e.g., "CustomerId", "EmailAddress").
    /// </value>
    public readonly string ClassName;

    /// <summary>
    /// Gets the base class that the partial class inherits from.
    /// </summary>
    /// <value>
    /// One of "RequiredGuid", "RequiredString", "RequiredInt", "RequiredDecimal", or "RequiredEnum",
    /// determining which factory methods are generated.
    /// </value>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>RequiredGuid</c>: Generates NewUniqueV4(), NewUniqueV7(), TryCreate(Guid?), TryParse(string?)</item>
    /// <item><c>RequiredString</c>: Generates TryCreate(string?)</item>
    /// <item><c>RequiredInt</c>: Generates TryCreate(int?), TryParse(string?)</item>
    /// <item><c>RequiredDecimal</c>: Generates TryCreate(decimal?), TryParse(string?)</item>
    /// <item><c>RequiredEnum</c>: Generates TryCreate(string?) delegating to TryFromName()</item>
    /// </list>
    /// </remarks>
    public readonly string ClassBase;

    /// <summary>
    /// Gets the accessibility level of the partial class.
    /// </summary>
    /// <value>
    /// The access modifier (e.g., "public", "internal", "private").
    /// </value>
    /// <remarks>
    /// The generated partial class will match this accessibility to ensure consistency.
    /// </remarks>
    public readonly string Accessibility;

    /// <summary>
    /// Gets the maximum string length constraint, if specified via <c>[StringLength]</c>.
    /// </summary>
    /// <value>
    /// The maximum length (inclusive), or <c>null</c> if no constraint was specified.
    /// Only applicable when <see cref="ClassBase"/> is <c>"RequiredString"</c>.
    /// </value>
    public readonly int? MaxLength;

    /// <summary>
    /// Gets the minimum string length constraint, if specified via <c>[StringLength(max, MinimumLength = min)]</c>.
    /// </summary>
    /// <value>
    /// The minimum length (inclusive), or <c>null</c> if no constraint was specified.
    /// Only applicable when <see cref="ClassBase"/> is <c>"RequiredString"</c>.
    /// </value>

    /// <summary>
    /// Gets the minimum range constraint, if specified via <c>[Range(min, max)]</c>.
    /// </summary>
    /// <value>
    /// The minimum value (inclusive), or <c>null</c> if no constraint was specified.
    /// Only applicable when <see cref="ClassBase"/> is <c>"RequiredInt"</c>.
    /// </value>
    public readonly int? RangeMin;

    /// <summary>
    /// Gets the maximum range constraint, if specified via <c>[Range(min, max)]</c>.
    /// </summary>
    /// <value>
    /// The maximum value (inclusive), or <c>null</c> if no constraint was specified.
    /// Only applicable when <see cref="ClassBase"/> is <c>"RequiredInt"</c>.
    /// </value>
    public readonly int? RangeMax;

    /// <summary>
    /// Gets the minimum range constraint for RequiredLong types, if specified via <c>[Range(min, max)]</c>.
    /// </summary>
    public readonly long? RangeLongMin;

    /// <summary>
    /// Gets the maximum range constraint for RequiredLong types, if specified via <c>[Range(min, max)]</c>.
    /// </summary>
    public readonly long? RangeLongMax;

    /// <summary>
    /// Gets the minimum range constraint for RequiredDecimal types with fractional bounds.
    /// </summary>
    public readonly double? RangeDoubleMin;

    /// <summary>
    /// Gets the maximum range constraint for RequiredDecimal types with fractional bounds.
    /// </summary>
    public readonly double? RangeDoubleMax;

    /// <summary>
    /// Gets the declarations for any containing types when the target class is nested.
    /// </summary>
    public readonly string[] NestingParents;

    /// <summary>
    /// Gets a unique type path including namespace and nesting used for hint names.
    /// </summary>
    public readonly string TypePath;
    public readonly int? MinLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredPartialClassInfo"/> class.
    /// </summary>
    /// <param name="nameSpace">The namespace of the partial class.</param>
    /// <param name="className">The name of the partial class.</param>
    /// <param name="classBase">The base class (RequiredGuid or RequiredString).</param>
    /// <param name="accessibility">The accessibility level (public, internal, etc.).</param>
    /// <param name="maxLength">Optional maximum string length from <c>[StringLength]</c> attribute.</param>
    /// <param name="minLength">Optional minimum string length from <c>[StringLength]</c> attribute.</param>
    /// <param name="rangeMin">Optional minimum value from <c>[Range]</c> attribute.</param>
    /// <param name="rangeMax">Optional maximum value from <c>[Range]</c> attribute.</param>
    /// <param name="rangeLongMin">Optional minimum value from <c>[Range]</c> attribute for RequiredLong types.</param>
    /// <param name="rangeLongMax">Optional maximum value from <c>[Range]</c> attribute for RequiredLong types.</param>
    /// <param name="rangeDoubleMin">Optional minimum value from <c>[Range]</c> attribute for RequiredDecimal with fractional bounds.</param>
    /// <param name="rangeDoubleMax">Optional maximum value from <c>[Range]</c> attribute for RequiredDecimal with fractional bounds.</param>
    /// <param name="nestingParents">Containing type declarations needed to emit nested generated types.</param>
    /// <param name="typePath">A unique namespace-qualified type path used for generated hint names.</param>
    public RequiredPartialClassInfo(
        string nameSpace,
        string className,
        string classBase,
        string accessibility,
        int? maxLength = null,
        int? minLength = null,
        int? rangeMin = null,
        int? rangeMax = null,
        long? rangeLongMin = null,
        long? rangeLongMax = null,
        double? rangeDoubleMin = null,
        double? rangeDoubleMax = null,
        string[]? nestingParents = null,
        string? typePath = null)
    {
        NameSpace = nameSpace;
        ClassName = className;
        ClassBase = classBase;
        Accessibility = accessibility;
        MaxLength = maxLength;
        MinLength = minLength;
        RangeMin = rangeMin;
        RangeMax = rangeMax;
        RangeLongMin = rangeLongMin;
        RangeLongMax = rangeLongMax;
        RangeDoubleMin = rangeDoubleMin;
        RangeDoubleMax = rangeDoubleMax;
        NestingParents = nestingParents ?? [];
        TypePath = typePath ?? (string.IsNullOrEmpty(nameSpace) ? className : $"{nameSpace}.{className}");
    }

    public bool Equals(RequiredPartialClassInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return NameSpace == other.NameSpace
            && ClassName == other.ClassName
            && ClassBase == other.ClassBase
            && Accessibility == other.Accessibility
            && MaxLength == other.MaxLength
            && MinLength == other.MinLength
            && RangeMin == other.RangeMin
            && RangeMax == other.RangeMax
            && RangeLongMin == other.RangeLongMin
            && RangeLongMax == other.RangeLongMax
            && RangeDoubleMin == other.RangeDoubleMin
            && RangeDoubleMax == other.RangeDoubleMax
            && TypePath == other.TypePath
            && NestingParents.SequenceEqual(other.NestingParents);
    }

    public override bool Equals(object? obj) => Equals(obj as RequiredPartialClassInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(NameSpace);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ClassName);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ClassBase);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Accessibility);
            hash = (hash * 31) + MaxLength.GetHashCode();
            hash = (hash * 31) + MinLength.GetHashCode();
            hash = (hash * 31) + RangeMin.GetHashCode();
            hash = (hash * 31) + RangeMax.GetHashCode();
            hash = (hash * 31) + RangeLongMin.GetHashCode();
            hash = (hash * 31) + RangeLongMax.GetHashCode();
            hash = (hash * 31) + RangeDoubleMin.GetHashCode();
            hash = (hash * 31) + RangeDoubleMax.GetHashCode();
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TypePath);
            return hash;
        }
    }
}