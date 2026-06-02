namespace Trellis.PrimitiveValueObjectGenerator;

using System;
using System.Linq;
using Microsoft.CodeAnalysis;

internal sealed class GeneratedMemberDeclaration : IEquatable<GeneratedMemberDeclaration>
{
    private readonly string _filePath;
    private readonly int _spanStart;
    private readonly int _spanLength;

    public readonly string Name;
    public readonly string Signature;
    public readonly bool MatchesByNameOnly;
    public readonly Location? Location;

    public GeneratedMemberDeclaration(string name, string signature, bool matchesByNameOnly, Location? location)
    {
        Name = name;
        Signature = signature;
        MatchesByNameOnly = matchesByNameOnly;
        Location = location;

        if (location is { IsInSource: true })
        {
            _filePath = location.GetLineSpan().Path;
            _spanStart = location.SourceSpan.Start;
            _spanLength = location.SourceSpan.Length;
        }
        else
        {
            _filePath = string.Empty;
            _spanStart = 0;
            _spanLength = 0;
        }
    }

    public string ReportKey => MatchesByNameOnly ? Name : Signature;

    public bool Matches(string name, string signature, bool generatedNameOnly) =>
        Name == name && (generatedNameOnly || MatchesByNameOnly || Signature == signature);

    public bool Equals(GeneratedMemberDeclaration? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Name == other.Name
            && Signature == other.Signature
            && MatchesByNameOnly == other.MatchesByNameOnly
            && _filePath == other._filePath
            && _spanStart == other._spanStart
            && _spanLength == other._spanLength;
    }

    public override bool Equals(object? obj) => Equals(obj as GeneratedMemberDeclaration);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Name);
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Signature);
            hash = (hash * 31) + MatchesByNameOnly.GetHashCode();
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_filePath);
            hash = (hash * 31) + _spanStart.GetHashCode();
            hash = (hash * 31) + _spanLength.GetHashCode();
            return hash;
        }
    }
}
/// <summary>
/// Represents metadata about a partial class that requires source generation for value object functionality.
/// Used by the source generator to create factory methods, validation, and parsing logic.
/// </summary>
/// <remarks>
/// <para>
/// This class captures the essential information needed to generate the complementary partial class
/// that provides the public API for value objects inheriting from <see cref="RequiredGuid"/>, <see cref="RequiredString"/>,
/// <see cref="RequiredInt"/>, <see cref="RequiredLong"/>, <see cref="RequiredDecimal"/>,
/// <see cref="RequiredBool"/>, <see cref="RequiredDateTime"/>, or <see cref="RequiredEnum"/>.
/// </para>
/// <para>
/// The generator uses this information to create:
/// <list type="bullet">
/// <item>Static factory methods (<c>NewUniqueV4()</c>/<c>NewUniqueV7()</c>/<c>NewUniqueV7(TimeProvider)</c> for GUIDs, <c>TryCreate</c> for all types)</item>
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
    /// <item><c>RequiredGuid</c>: Generates NewUniqueV4(), NewUniqueV7(), NewUniqueV7(TimeProvider), TryCreate(Guid?), TryParse(string?)</item>
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
    public readonly int? MinLength;

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

    /// <summary>
    /// Gets whether the target class is annotated with <c>[NotDefault]</c>.
    /// When true, the generator emits a per-type "zero value" rejection check
    /// (rejects <see cref="string.Empty"/> for strings, <c>0</c> for numerics,
    /// <see cref="System.Guid.Empty"/> for GUIDs, <see cref="System.DateTime.MinValue"/> for
    /// date-times). Invalid on <c>RequiredBool</c> and <c>RequiredEnum</c>.
    /// </summary>
    public readonly bool HasNotDefault;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[Trim]</c>.
    /// When true, the generator emits a trim of the input string before any other check.
    /// Only valid on <c>RequiredString</c>-derived types.
    /// </summary>
    public readonly bool HasTrim;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[AllowEmpty]</c>.
    /// Reserved for future use by the generator to opt-out of empty-string rejection on
    /// <c>RequiredString</c>-derived types when the strict default lands.
    /// </summary>
    public readonly bool HasAllowEmpty;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[AllowWhitespace]</c>.
    /// Reserved for future use by the generator to opt-out of whitespace-only rejection on
    /// <c>RequiredString</c>-derived types when the strict default lands.
    /// </summary>
    public readonly bool HasAllowWhitespace;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[NoTrim]</c>.
    /// Reserved for future use by the generator to opt-out of automatic trim on
    /// <c>RequiredString</c>-derived types when the trim-by-default behavior lands.
    /// </summary>
    public readonly bool HasNoTrim;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[AllowZero]</c>.
    /// When true, numeric Required bases accept zero under the strict-by-default model.
    /// </summary>
    public readonly bool HasAllowZero;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[AllowMinValue]</c>.
    /// When true, date/time Required bases accept their CLR minimum value under the strict-by-default model.
    /// </summary>
    public readonly bool HasAllowMinValue;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[AllowDefault]</c>.
    /// Reserved for future use by the generator to opt-out of CLR-default rejection on
    /// <c>RequiredGuid</c> / <c>RequiredDateTime</c> / <c>RequiredDateTimeOffset</c>-derived
    /// types when the strict default lands.
    /// </summary>
    public readonly bool HasAllowDefault;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[Positive]</c>.
    /// When true on a numeric Required base, the generator synthesizes a <c>[Range]</c>-equivalent
    /// constraint that rejects values <c>&lt;= 0</c>.
    /// </summary>
    public readonly bool HasPositive;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[NonNegative]</c>.
    /// When true on a numeric Required base, the generator synthesizes a <c>[Range]</c>-equivalent
    /// constraint that rejects values <c>&lt; 0</c>.
    /// </summary>
    public readonly bool HasNonNegative;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[Negative]</c>.
    /// When true on a numeric Required base, the generator synthesizes a <c>[Range]</c>-equivalent
    /// constraint that rejects values <c>&gt;= 0</c>.
    /// </summary>
    public readonly bool HasNegative;

    /// <summary>
    /// Gets whether the target class is annotated with <c>[NonPositive]</c>.
    /// When true on a numeric Required base, the generator synthesizes a <c>[Range]</c>-equivalent
    /// constraint that rejects values <c>&gt; 0</c>.
    /// </summary>
    public readonly bool HasNonPositive;

    /// <summary>
    /// Gets whether the target class had an explicit <c>[Range]</c> attribute before any
    /// convenience-attribute synthesis. Used by <c>ValidateAttributeUsage</c> to detect the
    /// conflict between an explicit <c>[Range]</c> and a numeric convenience attribute
    /// (<c>[Positive]</c> etc.) on the same class — the combination would otherwise silently
    /// disable the convenience sign check.
    /// </summary>
    public readonly bool HasExplicitRange;

    public readonly GeneratedMemberDeclaration[] UserDeclaredMembers;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequiredPartialClassInfo"/> class.
    /// </summary>
    /// <param name="nameSpace">The namespace of the partial class.</param>
    /// <param name="className">The name of the partial class.</param>
    /// <param name="classBase">The supported Required* base class.</param>
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
    /// <param name="hasNotDefault">True when the target carries <c>[NotDefault]</c>.</param>
    /// <param name="hasTrim">True when the target carries <c>[Trim]</c>.</param>
    /// <param name="hasAllowEmpty">True when the target carries <c>[AllowEmpty]</c>.</param>
    /// <param name="hasAllowWhitespace">True when the target carries <c>[AllowWhitespace]</c>.</param>
    /// <param name="hasNoTrim">True when the target carries <c>[NoTrim]</c>.</param>
    /// <param name="hasAllowZero">True when the target carries <c>[AllowZero]</c>.</param>
    /// <param name="hasAllowMinValue">True when the target carries <c>[AllowMinValue]</c>.</param>
    /// <param name="hasAllowDefault">True when the target carries <c>[AllowDefault]</c>.</param>
    /// <param name="hasPositive">True when the target carries <c>[Positive]</c>.</param>
    /// <param name="hasNonNegative">True when the target carries <c>[NonNegative]</c>.</param>
    /// <param name="hasNegative">True when the target carries <c>[Negative]</c>.</param>
    /// <param name="hasNonPositive">True when the target carries <c>[NonPositive]</c>.</param>
    /// <param name="hasExplicitRange">True when the target had an explicit <c>[Range]</c> before convenience-attribute synthesis.</param>
    /// <param name="userDeclaredMembers">Members declared by the user that may collide with generated members.</param>
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
        string? typePath = null,
        bool hasNotDefault = false,
        bool hasTrim = false,
        bool hasAllowEmpty = false,
        bool hasAllowWhitespace = false,
        bool hasNoTrim = false,
        bool hasAllowZero = false,
        bool hasAllowMinValue = false,
        bool hasAllowDefault = false,
        bool hasPositive = false,
        bool hasNonNegative = false,
        bool hasNegative = false,
        bool hasNonPositive = false,
        bool hasExplicitRange = false,
        GeneratedMemberDeclaration[]? userDeclaredMembers = null)
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
        HasNotDefault = hasNotDefault;
        HasTrim = hasTrim;
        HasAllowEmpty = hasAllowEmpty;
        HasAllowWhitespace = hasAllowWhitespace;
        HasNoTrim = hasNoTrim;
        HasAllowZero = hasAllowZero;
        HasAllowMinValue = hasAllowMinValue;
        HasAllowDefault = hasAllowDefault;
        HasPositive = hasPositive;
        HasNonNegative = hasNonNegative;
        HasNegative = hasNegative;
        HasNonPositive = hasNonPositive;
        HasExplicitRange = hasExplicitRange;
        UserDeclaredMembers = userDeclaredMembers ?? [];
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
            && HasNotDefault == other.HasNotDefault
            && HasTrim == other.HasTrim
            && HasAllowEmpty == other.HasAllowEmpty
            && HasAllowWhitespace == other.HasAllowWhitespace
            && HasNoTrim == other.HasNoTrim
            && HasAllowZero == other.HasAllowZero
            && HasAllowMinValue == other.HasAllowMinValue
            && HasAllowDefault == other.HasAllowDefault
            && HasPositive == other.HasPositive
            && HasNonNegative == other.HasNonNegative
            && HasNegative == other.HasNegative
            && HasNonPositive == other.HasNonPositive
            && HasExplicitRange == other.HasExplicitRange
            && TypePath == other.TypePath
            && NestingParents.SequenceEqual(other.NestingParents)
            && UserDeclaredMembers.SequenceEqual(other.UserDeclaredMembers);
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
            hash = (hash * 31) + HasNotDefault.GetHashCode();
            hash = (hash * 31) + HasTrim.GetHashCode();
            hash = (hash * 31) + HasAllowEmpty.GetHashCode();
            hash = (hash * 31) + HasAllowWhitespace.GetHashCode();
            hash = (hash * 31) + HasNoTrim.GetHashCode();
            hash = (hash * 31) + HasAllowZero.GetHashCode();
            hash = (hash * 31) + HasAllowMinValue.GetHashCode();
            hash = (hash * 31) + HasAllowDefault.GetHashCode();
            hash = (hash * 31) + HasPositive.GetHashCode();
            hash = (hash * 31) + HasNonNegative.GetHashCode();
            hash = (hash * 31) + HasNegative.GetHashCode();
            hash = (hash * 31) + HasNonPositive.GetHashCode();
            hash = (hash * 31) + HasExplicitRange.GetHashCode();
            hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TypePath);
            foreach (var member in UserDeclaredMembers)
                hash = (hash * 31) + member.GetHashCode();
            return hash;
        }
    }
}
