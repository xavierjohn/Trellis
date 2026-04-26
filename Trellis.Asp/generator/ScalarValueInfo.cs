namespace Trellis.AspSourceGenerator;

/// <summary>
/// Represents metadata about a scalar value type discovered during source generation.
/// Used to generate AOT-compatible JSON converters and serializer context entries.
/// </summary>
/// <remarks>
/// <para>
/// This class captures essential information needed to generate:
/// <list type="bullet">
/// <item>Strongly-typed JSON converters without runtime reflection</item>
/// <item><c>[JsonSerializable]</c> attributes for AOT compilation</item>
/// <item>Registration code for JSON serialization infrastructure</item>
/// </list>
/// </para>
/// </remarks>
internal class ScalarValueInfo
{
    /// <summary>
    /// Gets the namespace of the scalar value type.
    /// </summary>
    /// <value>
    /// The fully-qualified namespace (e.g., "MyApp.Domain.Values").
    /// </value>
    public readonly string Namespace;

    /// <summary>
    /// Gets the name of the scalar value type.
    /// </summary>
    /// <value>
    /// The simple class name without namespace (e.g., "CustomerId", "EmailAddress").
    /// </value>
    public readonly string TypeName;

    /// <summary>
    /// Gets the primitive type that the scalar value wraps.
    /// </summary>
    /// <value>
    /// The primitive type name (e.g., "string", "Guid", "int").
    /// </value>
    public readonly string PrimitiveType;

    /// <summary>
    /// Gets the fully qualified name of the value object type.
    /// </summary>
    public readonly string FullTypeName;

    /// <summary>
    /// Gets a unique identifier-safe name for generated converter types.
    /// </summary>
    public readonly string GeneratedTypeName;

    /// <summary>
    /// Gets the source location of the class declaration's identifier, used when
    /// reporting diagnostics so consumers can navigate to the offending type in the IDE.
    /// </summary>
    public readonly Microsoft.CodeAnalysis.Location? Location;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScalarValueInfo"/> class.
    /// </summary>
    /// <param name="namespace">The namespace of the value object type.</param>
    /// <param name="typeName">The name of the value object type.</param>
    /// <param name="primitiveType">The primitive type that the value object wraps.</param>
    /// <param name="fullTypeName">The fully qualified type name, including containing types when nested.</param>
    /// <param name="generatedTypeName">A unique identifier-safe type name used for generated converter types.</param>
    /// <param name="location">The source location of the class declaration's identifier (optional).</param>
    public ScalarValueInfo(string @namespace, string typeName, string primitiveType, string fullTypeName, string generatedTypeName, Microsoft.CodeAnalysis.Location? location = null)
    {
        Namespace = @namespace;
        TypeName = typeName;
        PrimitiveType = primitiveType;
        FullTypeName = fullTypeName;
        GeneratedTypeName = generatedTypeName;
        Location = location;
    }

    /// <summary>
    /// Returns a string representation for debugging purposes.
    /// </summary>
    public override string ToString() => $"{FullTypeName} : IScalarValue<{TypeName}, {PrimitiveType}>";
}