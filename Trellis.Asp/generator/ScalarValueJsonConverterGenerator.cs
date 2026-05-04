namespace Trellis.AspSourceGenerator;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// C# source generator that automatically creates AOT-compatible JSON converters
/// and serializer context entries for scalar values implementing <c>IScalarValue&lt;TSelf, TPrimitive&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// This incremental source generator analyzes the codebase for types implementing
/// <c>IScalarValue&lt;TSelf, TPrimitive&gt;</c> and generates:
/// <list type="bullet">
/// <item>Strongly-typed JSON converters that don't use runtime reflection</item>
/// <item>A partial class extending any user-defined <c>JsonSerializerContext</c> with <c>[JsonSerializable]</c> attributes</item>
/// <item>Registration code for automatic converter discovery</item>
/// </list>
/// </para>
/// <para>
/// Benefits of using the source generator:
/// <list type="bullet">
/// <item><strong>AOT Compatible</strong>: Generated code works with Native AOT compilation</item>
/// <item><strong>No Reflection</strong>: All type information is resolved at compile time</item>
/// <item><strong>Faster Startup</strong>: No runtime type scanning or assembly reflection</item>
/// <item><strong>Trimming Safe</strong>: Code won't be trimmed away since it's explicitly generated</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// To use the generator, reference it from your project and create a partial JsonSerializerContext:
/// <code>
/// // Mark your context with the [GenerateScalarValueConverters] attribute
/// [GenerateScalarValueConverters]
/// [JsonSerializable(typeof(MyDto))]
/// public partial class AppJsonSerializerContext : JsonSerializerContext
/// {
/// }
///
/// // The generator will automatically add [JsonSerializable] for all value objects:
/// // [JsonSerializable(typeof(CustomerId))]
/// // [JsonSerializable(typeof(FirstName))]
/// // etc.
/// </code>
/// </example>
[Generator(LanguageNames.CSharp)]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class ScalarValueJsonConverterGenerator : IIncrementalGenerator
{
    private const string GenerateAttributeName = "GenerateScalarValueConvertersAttribute";
    private const string GenerateAttributeFullName = "Trellis.Asp.GenerateScalarValueConvertersAttribute";
    private const string ScalarValueInterfaceName = "IScalarValue";

    /// <summary>
    /// Canonical IDs for diagnostics emitted by this generator. See
    /// <c>Trellis.TrellisDiagnosticIds</c> in <c>Trellis.Analyzers</c> for the
    /// consumer-facing equivalents.
    /// </summary>
    private static class Ids
    {
        public const string UnsupportedScalarValuePrimitiveForAotJson = "TRLS039";
    }

    /// <summary>
    /// Set of primitive type names for which the generator emits a fully AOT-safe converter
    /// using direct <c>Utf8JsonReader</c>/<c>Utf8JsonWriter</c> APIs (no reflection).
    /// </summary>
    /// <remarks>
    /// Must be kept in sync with the case branches in <see cref="GetTypedReaderCall"/>,
    /// <see cref="GetPrimitiveCsName"/>, <see cref="GetPrimitiveFriendlyName"/>, and
    /// <see cref="GenerateWritePrimitive"/>. When a value object wraps a primitive
    /// outside this set the generator emits TRLS039 and skips converter generation rather
    /// than falling back to <c>JsonSerializer.Deserialize</c>/<c>Serialize</c>, which are
    /// annotated <c>[RequiresUnreferencedCode]</c>/<c>[RequiresDynamicCode]</c> and would
    /// produce IL2026/IL3050 under <c>PublishAot=true</c>.
    /// </remarks>
    private static readonly HashSet<string> SupportedPrimitives = new(StringComparer.Ordinal)
    {
        "string",
        "int",
        "long",
        "bool",
        "double",
        "decimal",
        "float",
        "short",
        "byte",
        "Guid",
        "System.Guid",
        "DateTime",
        "System.DateTime",
        "DateTimeOffset",
        "System.DateTimeOffset",
    };

    /// <summary>
    /// Diagnostic descriptor for TRLS039 — emitted once per value object wrapping an
    /// unsupported primitive. Hoisted to a static field so the descriptor is allocated
    /// only once per process rather than per reported diagnostic.
    /// </summary>
    private static readonly DiagnosticDescriptor UnsupportedScalarValuePrimitiveDescriptor = new(
        id: Ids.UnsupportedScalarValuePrimitiveForAotJson,
        title: "Unsupported scalar value primitive for AOT-safe JSON converter",
        messageFormat: "Value object '{0}' wraps primitive '{1}', which is not supported by the AOT-safe Trellis JSON converter generator. Provide a custom JsonConverter<{0}> or use a supported primitive (string, int, long, short, byte, bool, float, double, decimal, Guid, DateTime, DateTimeOffset).",
        category: "Trellis",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Initializes the incremental generator pipeline.
    /// </summary>
    /// <param name="context">The initialization context provided by the compiler.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register the marker attribute
        context.RegisterPostInitializationOutput(static ctx => ctx.AddSource("GenerateScalarValueConvertersAttribute.g.cs", @"// <auto-generated/>
#nullable enable
namespace Trellis.Asp;

using System;

/// <summary>
/// Marks a JsonSerializerContext for automatic generation of [JsonSerializable] attributes
/// for all IScalarValue types in the assembly.
/// </summary>
/// <remarks>
/// Apply this attribute to a partial JsonSerializerContext class to have the source generator
/// automatically add [JsonSerializable] attributes for all value object types, enabling
/// AOT-compatible JSON serialization.
/// </remarks>
/// <example>
/// <code>
/// [GenerateScalarValueConverters]
/// [JsonSerializable(typeof(MyDto))]
/// public partial class AppJsonSerializerContext : JsonSerializerContext
/// {
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
internal sealed class GenerateScalarValueConvertersAttribute : Attribute
{
}
"));

        // Find all types implementing IScalarValue<TSelf, TPrimitive>
        IncrementalValuesProvider<ScalarValueInfo> valueObjectTypes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (n, _) => IsPotentialValueObject(n),
                transform: static (ctx, ct) => GetValueObjectInfo(ctx, ct))
            .Where(static info => info is not null)!;

        // Find classes with [GenerateScalarValueConverters] attribute
        IncrementalValuesProvider<ClassDeclarationSyntax> contextClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (n, _) => IsJsonSerializerContext(n),
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
            .Where(static c => c is not null);

        // Combine context classes with all value object types
        var combined = contextClasses.Collect().Combine(valueObjectTypes.Collect());

        // Also combine with compilation for namespace resolution
        var withCompilation = context.CompilationProvider.Combine(combined);

        // Generate the output
        context.RegisterSourceOutput(withCompilation,
            static (spc, source) => Execute(source.Left, source.Right.Left, source.Right.Right, spc));
    }

    /// <summary>
    /// Fast syntax-only filter to identify potential value object types.
    /// </summary>
    private static bool IsPotentialValueObject(SyntaxNode node)
    {
        // Look for class declarations with base types that might be value objects
        if (node is ClassDeclarationSyntax c && c.BaseList is not null)
        {
            // Check if any base type text matches known value object base types or interface.
            // The semantic transform (GetValueObjectInfo) walks the full type hierarchy,
            // but this syntax predicate must let candidates through first.
            foreach (var baseType in c.BaseList.Types)
            {
                var typeName = baseType.Type.ToString();
                if (typeName.Contains(ScalarValueInterfaceName)
                    || typeName.Contains("RequiredString")
                    || typeName.Contains("RequiredGuid")
                    || typeName.Contains("RequiredInt")
                    || typeName.Contains("RequiredDecimal")
                    || typeName.Contains("RequiredEnum")
                    || typeName.Contains("ScalarValueObject"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Fast syntax-only filter to identify JsonSerializerContext classes with our attribute.
    /// </summary>
    private static bool IsJsonSerializerContext(SyntaxNode node)
    {
        if (node is ClassDeclarationSyntax c)
        {
            // Check for the [GenerateScalarValueConverters] attribute
            foreach (var attributeList in c.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var name = attribute.Name.ToString();
                    if (name is "GenerateScalarValueConverters" or
                        "GenerateScalarValueConvertersAttribute" or
                        "Trellis.Asp.GenerateScalarValueConverters" or
                        "Trellis.Asp.GenerateScalarValueConvertersAttribute")
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Extracts value object metadata using semantic analysis.
    /// </summary>
    /// <remarks>
    /// This method looks for base classes (RequiredString, RequiredGuid, ScalarValueObject)
    /// rather than the IScalarValue interface because the interface is added by
    /// PrimitiveValueObjectGenerator, and source generators can't see each other's output.
    /// </remarks>
    private static ScalarValueInfo? GetValueObjectInfo(GeneratorSyntaxContext context, CancellationToken ct)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration, ct) is not INamedTypeSymbol classSymbol)
            return null;

        // Find the base class and extract primitive type
        var (baseTypeName, primitiveType) = FindValueObjectBaseType(classSymbol);
        if (baseTypeName is null)
            return null;

        // Get primitive type name - either from the ITypeSymbol or from the base type name
        var primitiveTypeName = primitiveType is not null
            ? GetPrimitiveTypeName(primitiveType)
            : GetPrimitiveTypeNameFromBase(baseTypeName);

        var fullTypeName = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);

        return new ScalarValueInfo(
            classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol.Name,
            primitiveTypeName,
            fullTypeName,
            CreateGeneratedTypeName(fullTypeName),
            classDeclaration.Identifier.GetLocation());
    }

    /// <summary>
    /// Finds the value object base type (RequiredString, RequiredGuid, or ScalarValueObject)
    /// and extracts the primitive type parameter.
    /// </summary>
    private static (string? BaseTypeName, ITypeSymbol? PrimitiveType) FindValueObjectBaseType(INamedTypeSymbol classSymbol)
    {
        var baseType = classSymbol.BaseType;

        while (baseType is not null)
        {
            var baseName = baseType.Name;

            // Check for Trellis primitive CRTP base types
            if (baseName == "RequiredString" && baseType.IsGenericType)
            {
                // The type argument should be the class itself (CRTP), primitive is string
                if (baseType.TypeArguments.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(baseType.TypeArguments[0], classSymbol))
                {
                    // Return a marker that we'll convert to "string" in GetPrimitiveTypeName
                    return ("RequiredString", null);
                }
            }

            if (baseName == "RequiredGuid" && baseType.IsGenericType)
            {
                // The type argument should be the class itself (CRTP), primitive is Guid
                if (baseType.TypeArguments.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(baseType.TypeArguments[0], classSymbol))
                {
                    // Return a marker that we'll convert to "Guid" in GetPrimitiveTypeName
                    return ("RequiredGuid", null);
                }
            }

            if (baseName == "RequiredInt" && baseType.IsGenericType)
            {
                if (baseType.TypeArguments.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(baseType.TypeArguments[0], classSymbol))
                {
                    return ("RequiredInt", null);
                }
            }

            if (baseName == "RequiredDecimal" && baseType.IsGenericType)
            {
                if (baseType.TypeArguments.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(baseType.TypeArguments[0], classSymbol))
                {
                    return ("RequiredDecimal", null);
                }
            }

            if (baseName == "RequiredEnum" && baseType.IsGenericType)
            {
                if (baseType.TypeArguments.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(baseType.TypeArguments[0], classSymbol))
                {
                    return ("RequiredEnum", null);
                }
            }

            // Check for ScalarValueObject<TSelf, TPrimitive>
            if (baseName == "ScalarValueObject" && baseType.IsGenericType && baseType.TypeArguments.Length == 2)
            {
                // Verify CRTP pattern: first type arg should be the class itself
                if (SymbolEqualityComparer.Default.Equals(baseType.TypeArguments[0], classSymbol))
                {
                    return ("ScalarValueObject", baseType.TypeArguments[1]);
                }
            }

            baseType = baseType.BaseType;
        }

        return (null, null);
    }

    /// <summary>
    /// Gets the primitive type name based on the base class name.
    /// </summary>
    private static string GetPrimitiveTypeNameFromBase(string baseTypeName) =>
        baseTypeName switch
        {
            "RequiredString" => "string",
            "RequiredGuid" => "System.Guid",
            "RequiredInt" => "int",
            "RequiredDecimal" => "decimal",
            "RequiredEnum" => "string",
            _ => "object"
        };

    /// <summary>
    /// Gets a friendly name for the primitive type.
    /// </summary>
    private static string GetPrimitiveTypeName(ITypeSymbol type) =>
        type.SpecialType switch
        {
            SpecialType.System_String => "string",
            SpecialType.System_Int32 => "int",
            SpecialType.System_Int64 => "long",
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Double => "double",
            SpecialType.System_Decimal => "decimal",
            SpecialType.System_Single => "float",
            SpecialType.System_Int16 => "short",
            SpecialType.System_Byte => "byte",
            _ => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", "")
        };

    /// <summary>
    /// Executes the source generation for collected types.
    /// </summary>
    private static void Execute(
        Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> contextClasses,
        ImmutableArray<ScalarValueInfo> scalarValues,
        SourceProductionContext context)
    {
        if (scalarValues.IsDefaultOrEmpty)
            return;

        // Deduplicate value objects by full type name
        var distinctValueObjects = scalarValues
            .GroupBy(vo => vo.FullTypeName)
            .Select(g => g.First())
            .ToList();

        // Filter out value objects whose wrapped primitive cannot be serialized AOT-safely.
        // Emitting JsonSerializer.Deserialize/Serialize for unknown primitives would produce
        // IL2026/IL3050 warnings under PublishAot=true (see issue #413).
        var supportedValueObjects = new List<ScalarValueInfo>(distinctValueObjects.Count);
        foreach (var vo in distinctValueObjects)
        {
            if (IsSupportedPrimitive(vo.PrimitiveType))
            {
                supportedValueObjects.Add(vo);
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedScalarValuePrimitiveDescriptor,
                location: vo.Location,
                vo.FullTypeName,
                vo.PrimitiveType));
        }

        if (supportedValueObjects.Count == 0)
            return;

        // Generate AOT-compatible JSON converters
        GenerateJsonConverters(supportedValueObjects, context);

        // Generate partial class extensions for each JsonSerializerContext with our attribute
        foreach (var contextClass in contextClasses)
        {
            var semanticModel = compilation.GetSemanticModel(contextClass.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(contextClass) is INamedTypeSymbol contextSymbol)
            {
                GenerateSerializerContextExtension(contextSymbol, supportedValueObjects, context);
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the primitive type can be read/written using direct
    /// <c>Utf8JsonReader</c>/<c>Utf8JsonWriter</c> APIs without reflection.
    /// </summary>
    private static bool IsSupportedPrimitive(string primitiveType) =>
        SupportedPrimitives.Contains(primitiveType);

    /// <summary>
    /// Generates AOT-compatible JSON converters for all value object types.
    /// </summary>
    private static void GenerateJsonConverters(
        List<ScalarValueInfo> valueObjects,
        SourceProductionContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Trellis.Generated;");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine("using Trellis;");
        sb.AppendLine();

        foreach (var vo in valueObjects)
        {
            GenerateSingleConverter(sb, vo);
            sb.AppendLine();
        }

        // Generate a factory that can create converters for all known types
        GenerateConverterFactory(sb, valueObjects);

        context.AddSource("ValueObjectJsonConverters.g.cs", sb.ToString());
    }

    /// <summary>
    /// Generates a single JSON converter for a value object type.
    /// </summary>
    /// <remarks>
    /// The generated <c>Read</c> method mirrors <c>ScalarValueJsonConverterBase&lt;TValue?, TValue, TPrimitive&gt;</c>
    /// from the reflection-mode runtime: it consults <c>ValidationErrorsContext.CurrentPropertyName</c>
    /// for the property name, falls back to the camel-cased type name when no scope is active,
    /// passes the resolved field name to <c>TryCreate</c>, and reports validation failures via
    /// <c>ValidationErrorsContext.AddError</c> instead of silently coercing them to <c>null</c>.
    /// Primitive-read failures (<c>FormatException</c>/<c>InvalidOperationException</c> from the
    /// typed <c>Utf8JsonReader</c> getters) are caught and recorded the same way reflection mode
    /// does via <c>PrimitiveJsonReader.TryRead</c>, so an invalid token like a non-Guid string for
    /// a Guid VO produces a 422 instead of bubbling up as a <c>JsonException</c>.
    /// This is the M-2 fix; without it, AOT consumers got <c>null</c> on validation failure
    /// while reflection-mode consumers got a 422 — a divergence that broke the framework's
    /// "one programming model" promise.
    /// </remarks>
    private static void GenerateSingleConverter(StringBuilder sb, ScalarValueInfo vo)
    {
        var converterName = $"{vo.GeneratedTypeName}JsonConverter";
        var fullTypeName = vo.FullTypeName;
        var primitiveType = vo.PrimitiveType;
        var defaultFieldName = ToCamelCase(vo.TypeName);
        var primitiveFriendlyName = GetPrimitiveFriendlyName(primitiveType);

        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// AOT-compatible JSON converter for <see cref=\"{fullTypeName}\"/>.");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"internal sealed class {converterName} : JsonConverter<{fullTypeName}>");
        sb.AppendLine("{");
        sb.AppendLine($"    private const string DefaultFieldName = \"{defaultFieldName}\";");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Tells System.Text.Json to call <see cref=\"Read\"/> even when the JSON token is <c>null</c>");
        sb.AppendLine("    /// so a missing/null value can be reported via ValidationErrorsContext rather than bypassing the converter.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public override bool HandleNull => true;");
        sb.AppendLine();

        // Read method
        sb.AppendLine($"    public override {fullTypeName}? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
        sb.AppendLine("    {");
        sb.AppendLine("        var fieldName = global::Trellis.Asp.ValidationErrorsContext.CurrentPropertyName ?? DefaultFieldName;");
        sb.AppendLine();
        sb.AppendLine("        if (reader.TokenType == JsonTokenType.Null)");
        sb.AppendLine("        {");
        sb.AppendLine($"            global::Trellis.Asp.ValidationErrorsContext.AddError(fieldName, \"{vo.TypeName} cannot be null.\");");
        sb.AppendLine("            return null;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Read the primitive via the local helper which mirrors
        // PrimitiveJsonReader.TryRead — invalid tokens for the wrapped primitive
        // (e.g. a non-Guid string for a Guid VO) are recorded as a validation error
        // instead of escaping as a JsonException.
        sb.AppendLine($"        if (!__TryReadPrimitive(ref reader, fieldName, out var primitiveValue))");
        sb.AppendLine("            return null;");

        if (PrimitiveCanBeNull(primitiveType))
        {
            sb.AppendLine();
            sb.AppendLine("        if (primitiveValue is null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            global::Trellis.Asp.ValidationErrorsContext.AddError(fieldName, \"Cannot deserialize null to {vo.TypeName}\");");
            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
        }

        sb.AppendLine();
        sb.AppendLine($"        var result = {fullTypeName}.TryCreate(primitiveValue, fieldName);");
        sb.AppendLine();
        sb.AppendLine($"        return result.Match<{fullTypeName}, {fullTypeName}?>(");
        sb.AppendLine("            onSuccess: v => v,");
        sb.AppendLine("            onFailure: createError =>");
        sb.AppendLine("            {");
        sb.AppendLine("                if (createError is global::Trellis.Error.UnprocessableContent unprocessable)");
        sb.AppendLine("                {");
        sb.AppendLine("                    global::Trellis.Asp.ValidationErrorsContext.AddError(unprocessable);");
        sb.AppendLine("                }");
        sb.AppendLine("                else");
        sb.AppendLine("                {");
        sb.AppendLine("                    global::Trellis.Asp.ValidationErrorsContext.AddError(");
        sb.AppendLine("                        fieldName,");
        sb.AppendLine("                        string.IsNullOrWhiteSpace(createError.Detail)");
        sb.AppendLine($"                            ? \"{vo.TypeName} is invalid.\"");
        sb.AppendLine("                            : createError.Detail);");
        sb.AppendLine("                }");
        sb.AppendLine();
        sb.AppendLine("                return null;");
        sb.AppendLine("            });");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Write method
        sb.AppendLine($"    public override void Write(Utf8JsonWriter writer, {fullTypeName} value, JsonSerializerOptions options)");
        sb.AppendLine("    {");
        sb.AppendLine("        if (value is null)");
        sb.AppendLine("        {");
        sb.AppendLine("            writer.WriteNullValue();");
        sb.AppendLine("            return;");
        sb.AppendLine("        }");
        sb.AppendLine();

        // Write the primitive value based on type
        GenerateWritePrimitive(sb, primitiveType);

        sb.AppendLine("    }");
        sb.AppendLine();

        // Local-typed primitive-read helper. Hoisted into the converter so each closed-generic
        // converter has its own non-generic reader (no boxing) and the try/catch lives outside
        // the public Read method to keep the hot path readable.
        sb.AppendLine($"    private static bool __TryReadPrimitive(ref Utf8JsonReader reader, string fieldName, out {GetPrimitiveCsName(primitiveType)} value)");
        sb.AppendLine("    {");
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine($"            value = {GetTypedReaderCall(primitiveType)};");
        sb.AppendLine("            return true;");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (global::System.Exception ex) when (ex is global::System.FormatException || ex is global::System.InvalidOperationException)");
        sb.AppendLine("        {");
        sb.AppendLine($"            global::Trellis.Asp.ValidationErrorsContext.AddError(fieldName, $\"'{{fieldName}}' is not a valid {primitiveFriendlyName}.\");");
        sb.AppendLine($"            value = default({GetPrimitiveCsName(primitiveType)});");
        sb.AppendLine("            return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    /// <summary>
    /// Returns true when the C# primitive type can hold <c>null</c> via the typed
    /// <c>Utf8JsonReader</c> getter. Only <c>string?</c> is null-bearing in the supported set;
    /// the value-type getters (<c>GetInt32</c>, <c>GetGuid</c>, etc.) cannot return <c>null</c>.
    /// </summary>
    private static bool PrimitiveCanBeNull(string primitiveType) =>
        string.Equals(primitiveType, "string", StringComparison.Ordinal);

    /// <summary>
    /// Returns the friendly C#-keyword type name that matches <c>typeof(TPrimitive).Name</c>
    /// from the reflection-mode error string, so AOT and reflection produce the same 422 detail.
    /// </summary>
    private static string GetPrimitiveFriendlyName(string primitiveType) => primitiveType switch
    {
        "string" => "String",
        "int" => "Int32",
        "long" => "Int64",
        "short" => "Int16",
        "byte" => "Byte",
        "bool" => "Boolean",
        "float" => "Single",
        "double" => "Double",
        "decimal" => "Decimal",
        "Guid" or "System.Guid" => "Guid",
        "DateTime" or "System.DateTime" => "DateTime",
        "DateTimeOffset" or "System.DateTimeOffset" => "DateTimeOffset",
        _ => primitiveType,
    };

    /// <summary>
    /// Returns the C# type name suitable for a local variable declaration; <c>string</c> is
    /// declared nullable because <c>reader.GetString()</c> can legitimately return <c>null</c>
    /// for a quoted JSON null literal.
    /// </summary>
    private static string GetPrimitiveCsName(string primitiveType) => primitiveType switch
    {
        "string" => "string?",
        "Guid" => "System.Guid",
        "DateTime" => "System.DateTime",
        "DateTimeOffset" => "System.DateTimeOffset",
        _ => primitiveType,
    };

    /// <summary>
    /// Returns the typed <c>Utf8JsonReader</c> getter call appropriate for the primitive.
    /// </summary>
    private static string GetTypedReaderCall(string primitiveType) => primitiveType switch
    {
        "string" => "reader.GetString()",
        "int" => "reader.GetInt32()",
        "long" => "reader.GetInt64()",
        "short" => "reader.GetInt16()",
        "byte" => "reader.GetByte()",
        "bool" => "reader.GetBoolean()",
        "float" => "reader.GetSingle()",
        "double" => "reader.GetDouble()",
        "decimal" => "reader.GetDecimal()",
        "Guid" or "System.Guid" => "reader.GetGuid()",
        "DateTime" or "System.DateTime" => "reader.GetDateTime()",
        "DateTimeOffset" or "System.DateTimeOffset" => "reader.GetDateTimeOffset()",
        _ => throw new InvalidOperationException(
            $"Unsupported primitive '{primitiveType}' reached GetTypedReaderCall; should have been filtered upstream."),
    };

    /// <summary>
    /// Camel-cases a simple type name to produce the default field reference used in
    /// validation errors when no <c>ValidationErrorsContext.CurrentPropertyName</c> is set.
    /// Mirrors <c>JsonNamingPolicy.CamelCase.ConvertName</c> bit-for-bit so the AOT and
    /// reflection paths agree on the field key for acronym-leading types like
    /// <c>SKU → "sku"</c>, <c>URLValue → "urlValue"</c>, <c>IPAddress → "ipAddress"</c>,
    /// and <c>XMLDocument → "xmlDocument"</c>. The naive
    /// "lowercase-first-character-only" approach the generator originally used produced
    /// <c>"sKU"</c>/<c>"uRLValue"</c>/<c>"iPAddress"</c> and silently diverged from
    /// reflection mode.
    /// </summary>
    private static string ToCamelCase(string typeName)
    {
        if (string.IsNullOrEmpty(typeName) || !char.IsUpper(typeName[0]))
            return typeName;

        // Find the run of consecutive uppercase characters at the start.
        var runLength = 1;
        while (runLength < typeName.Length && char.IsUpper(typeName[runLength]))
            runLength++;

        // All-uppercase string — lowercase the entire thing (e.g. "SKU" → "sku").
        if (runLength == typeName.Length)
            return typeName.ToLowerInvariant();

        // Single leading uppercase — lowercase that one character (e.g. "OrderId" → "orderId").
        if (runLength == 1)
            return char.ToLowerInvariant(typeName[0]) + typeName.Substring(1);

        // Acronym block followed by a non-uppercase character. STJ treats the LAST uppercase
        // letter in the run as the start of the next "word" and lowercases all earlier ones
        // (e.g. "URLValue" → "urlValue"; "XMLDocument" → "xmlDocument"; "IPAddress" → "ipAddress").
        return typeName.Substring(0, runLength - 1).ToLowerInvariant() + typeName.Substring(runLength - 1);
    }

    /// <summary>
    /// Generates code to write a primitive value to JSON.
    /// </summary>
    private static void GenerateWritePrimitive(StringBuilder sb, string primitiveType)
    {
        switch (primitiveType)
        {
            case "string":
                sb.AppendLine("        writer.WriteStringValue(value.Value);");
                break;
            case "int":
            case "long":
            case "short":
            case "byte":
            case "float":
            case "double":
            case "decimal":
                sb.AppendLine("        writer.WriteNumberValue(value.Value);");
                break;
            case "bool":
                sb.AppendLine("        writer.WriteBooleanValue(value.Value);");
                break;
            case "System.Guid":
            case "Guid":
                sb.AppendLine("        writer.WriteStringValue(value.Value);");
                break;
            case "System.DateTime":
            case "DateTime":
            case "System.DateTimeOffset":
            case "DateTimeOffset":
                sb.AppendLine("        writer.WriteStringValue(value.Value);");
                break;
            default:
                // Unsupported primitives are filtered out in Execute before reaching this
                // method (see TRLS039). This branch must remain unreachable; the throw
                // documents the contract and surfaces a clear runtime error if the filter
                // is ever bypassed.
                throw new InvalidOperationException(
                    $"Unsupported primitive '{primitiveType}' reached GenerateWritePrimitive; should have been filtered upstream.");
        }
    }

    /// <summary>
    /// Generates a converter factory that returns converters for all known value object types.
    /// </summary>
    private static void GenerateConverterFactory(StringBuilder sb, List<ScalarValueInfo> scalarValueInfo)
    {
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Factory for creating AOT-compatible JSON converters for all generated value object types.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("/// <remarks>");
        sb.AppendLine("/// This factory provides converters for value objects without requiring runtime reflection.");
        sb.AppendLine("/// Add this to your JsonSerializerOptions.Converters collection.");
        sb.AppendLine("/// </remarks>");
        sb.AppendLine("public sealed class GeneratedValueObjectConverterFactory : JsonConverterFactory");
        sb.AppendLine("{");
        sb.AppendLine("    private static readonly System.Collections.Generic.Dictionary<Type, JsonConverter> _converters = new()");
        sb.AppendLine("    {");

        foreach (var vo in scalarValueInfo)
        {
            sb.AppendLine($"        {{ typeof({vo.FullTypeName}), new {vo.GeneratedTypeName}JsonConverter() }},");
        }

        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc />");
        sb.AppendLine("    public override bool CanConvert(Type typeToConvert) => _converters.ContainsKey(typeToConvert);");
        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc />");
        sb.AppendLine("    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)");
        sb.AppendLine("    {");
        sb.AppendLine("        return _converters.TryGetValue(typeToConvert, out var converter) ? converter : null;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    /// <summary>
    /// Generates a partial class extending the user's JsonSerializerContext with [JsonSerializable] attributes.
    /// </summary>
    private static void GenerateSerializerContextExtension(
        INamedTypeSymbol contextSymbol,
        List<ScalarValueInfo> scalarValueInfo,
        SourceProductionContext context)
    {
        var contextNamespace = contextSymbol.ContainingNamespace.ToDisplayString();
        var contextName = contextSymbol.Name;
        var accessibility = contextSymbol.DeclaredAccessibility.ToString().ToLowerInvariant();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {contextNamespace};");
        sb.AppendLine();
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();

        // Add [JsonSerializable] attributes for all value object types
        foreach (var vo in scalarValueInfo)
        {
            sb.AppendLine($"[JsonSerializable(typeof({vo.FullTypeName}))]");
        }

        sb.AppendLine($"{accessibility} partial class {contextName}");
        sb.AppendLine("{");
        sb.AppendLine("}");

        context.AddSource($"{contextName}.ValueObjects.g.cs", sb.ToString());
    }

    private static string CreateGeneratedTypeName(string fullTypeName)
    {
        var builder = new StringBuilder(fullTypeName.Length);

        foreach (var character in fullTypeName)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        if (builder.Length == 0 || (!char.IsLetter(builder[0]) && builder[0] != '_'))
            builder.Insert(0, '_');

        return builder.ToString();
    }
}