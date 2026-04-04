namespace Trellis.EntityFrameworkCore.Generator;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Source generator that emits a private parameterless constructor for composite
/// <see cref="ValueObject"/> types decorated with <c>[OwnedEntity]</c>.
/// The generated constructor initializes all reference-type properties with <c>null!</c>
/// for EF Core materialization.
/// </summary>
[Generator(LanguageNames.CSharp)]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class OwnedEntityGenerator : IIncrementalGenerator
{
    private const string OwnedEntityAttributeName = "Trellis.EntityFrameworkCore.OwnedEntityAttribute";

    /// <summary>
    /// Diagnostic reported when [OwnedEntity] is applied to a non-partial type.
    /// </summary>
    private static readonly DiagnosticDescriptor s_shouldBePartial = new(
        id: "TRLSGEN101",
        title: "[OwnedEntity] type should be partial",
        messageFormat: "Type '{0}' is decorated with [OwnedEntity] but is not declared 'partial'. The source generator cannot emit the private parameterless constructor.",
        category: "Trellis.EntityFrameworkCore.Generator",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Diagnostic reported when [OwnedEntity] is applied to a type that already has
    /// a parameterless constructor.
    /// </summary>
    private static readonly DiagnosticDescriptor s_alreadyHasParameterlessCtor = new(
        id: "TRLSGEN102",
        title: "[OwnedEntity] type already has a parameterless constructor",
        messageFormat: "Type '{0}' already has a parameterless constructor. Remove it to let the [OwnedEntity] source generator emit one, or remove the [OwnedEntity] attribute.",
        category: "Trellis.EntityFrameworkCore.Generator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                OwnedEntityAttributeName,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, ct) => GetOwnedEntityInfo(ctx, ct))
            .Where(static info => info is not null);

        context.RegisterSourceOutput(candidates,
            static (spc, info) => Execute(spc, info));
    }

    private static OwnedEntityInfo? GetOwnedEntityInfo(
        GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        var typeDecl = (TypeDeclarationSyntax)ctx.TargetNode;
        var symbol = ctx.TargetSymbol as INamedTypeSymbol;
        if (symbol is null)
            return null;

        var isRecord = typeDecl is RecordDeclarationSyntax;
        var isPartial = typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword);

        // Check if the type already has a parameterless constructor
        var hasParameterlessCtor = symbol.Constructors
            .Any(c => c.Parameters.Length == 0 && !c.IsImplicitlyDeclared);

        // Collect properties that need null! initialization (reference-type with setter)
        var referenceProps = new List<string>();
        var compilation = ctx.SemanticModel.Compilation;
        var valueObjectSymbol = compilation.GetTypeByMetadataName("Trellis.ValueObject");
        var objectSymbol = compilation.ObjectType;
        var currentType = symbol;
        while (currentType is not null)
        {
            foreach (var member in currentType.GetMembers())
            {
                if (member is IPropertySymbol prop
                    && prop.SetMethod is not null
                    && !prop.IsStatic
                    && !prop.IsIndexer
                    && prop.Type.IsReferenceType
                    && prop.DeclaredAccessibility != Accessibility.Private
                    // Skip properties from ValueObject/object base
                    && !SymbolEqualityComparer.Default.Equals(prop.ContainingType, valueObjectSymbol)
                    && !SymbolEqualityComparer.Default.Equals(prop.ContainingType, objectSymbol)
                    // For inherited properties, setter must be accessible from derived type
                    && (SymbolEqualityComparer.Default.Equals(prop.ContainingType, symbol)
                        || compilation.IsSymbolAccessibleWithin(prop.SetMethod, symbol)))
                {
                    referenceProps.Add(prop.Name);
                }
            }

            // Walk up to base type but stop at ValueObject
            var baseType = currentType.BaseType;
            if (baseType is null
                || SymbolEqualityComparer.Default.Equals(baseType, valueObjectSymbol)
                || SymbolEqualityComparer.Default.Equals(baseType, objectSymbol))
                break;
            currentType = baseType;
        }

        // Build nesting chain
        var nestingParents = new List<string>();
        INamedTypeSymbol? parent = symbol.ContainingType;
        while (parent is not null)
        {
            nestingParents.Insert(0, $"{AccessibilityToString(parent.DeclaredAccessibility)} partial {TypeKindKeyword(parent)} {FormatTypeName(parent)}");
            parent = parent.ContainingType;
        }

        var @namespace = symbol.ContainingNamespace?.IsGlobalNamespace == true
            ? ""
            : symbol.ContainingNamespace?.ToString() ?? "";

        var typePath = BuildTypePath(symbol);

        return new OwnedEntityInfo(
            location: typeDecl.Identifier.GetLocation(),
            @namespace: @namespace,
            typeName: FormatTypeName(symbol),
            typeAccessibility: AccessibilityToString(symbol.DeclaredAccessibility),
            isRecord: isRecord,
            isPartial: isPartial,
            hasParameterlessCtor: hasParameterlessCtor,
            referencePropertyNames: referenceProps.Distinct().ToArray(),
            nestingParents: nestingParents.ToArray(),
            typePath: typePath);
    }

    private static void Execute(SourceProductionContext spc, OwnedEntityInfo? info)
    {
        if (info is null) return;

        // Report diagnostic if not partial
        if (!info.IsPartial)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                s_shouldBePartial, info.Location, info.TypeName));
            return;
        }

        // Report diagnostic if already has parameterless ctor
        if (info.HasParameterlessCtor)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                s_alreadyHasParameterlessCtor, info.Location, info.TypeName));
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(info.Namespace))
        {
            sb.Append("namespace ");
            sb.Append(info.Namespace);
            sb.AppendLine(";");
            sb.AppendLine();
        }

        // Open nesting parents
        var indent = "";
        foreach (var parent in info.NestingParents)
        {
            sb.Append(indent);
            sb.AppendLine(parent);
            sb.Append(indent);
            sb.AppendLine("{");
            indent += "    ";
        }

        sb.Append(indent);
        sb.Append(info.TypeAccessibility);
        sb.Append(info.IsRecord ? " partial record class " : " partial class ");
        sb.AppendLine(info.TypeName);
        sb.Append(indent);
        sb.AppendLine("{");

        var memberIndent = indent + "    ";
        var bodyIndent = memberIndent + "    ";

        // Private parameterless constructor
        sb.Append(memberIndent);
        sb.Append("private ");
        sb.Append(info.TypeName);
        sb.AppendLine("()");
        sb.Append(memberIndent);
        sb.AppendLine("{");

        foreach (var propName in info.ReferencePropertyNames)
        {
            sb.Append(bodyIndent);
            sb.Append(EscapeIdentifier(propName));
            sb.AppendLine(" = null!;");
        }

        sb.Append(memberIndent);
        sb.AppendLine("}");

        sb.Append(indent);
        sb.AppendLine("}");

        // Close nesting parents
        for (var i = info.NestingParents.Length - 1; i >= 0; i--)
        {
            indent = new string(' ', i * 4);
            sb.Append(indent);
            sb.AppendLine("}");
        }

        var fileName = $"{info.TypePath}.OwnedEntity.g.cs";

        spc.AddSource(fileName, sb.ToString());
    }

    private static string AccessibilityToString(Accessibility accessibility) =>
        accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal"
        };

    private static string TypeKindKeyword(INamedTypeSymbol type) =>
        type.IsRecord ? "record class" : type.TypeKind == TypeKind.Struct ? "struct" : "class";

    private sealed class OwnedEntityInfo : IEquatable<OwnedEntityInfo>
    {
        public OwnedEntityInfo(
            Location location,
            string @namespace,
            string typeName,
            string typeAccessibility,
            bool isRecord,
            bool isPartial,
            bool hasParameterlessCtor,
            string[] referencePropertyNames,
            string[] nestingParents,
            string typePath)
        {
            Location = location;
            Namespace = @namespace;
            TypeName = typeName;
            TypeAccessibility = typeAccessibility;
            IsRecord = isRecord;
            IsPartial = isPartial;
            HasParameterlessCtor = hasParameterlessCtor;
            ReferencePropertyNames = referencePropertyNames;
            NestingParents = nestingParents;
            TypePath = typePath;
        }

        public Location Location { get; }
        public string Namespace { get; }
        public string TypeName { get; }
        public string TypeAccessibility { get; }
        public bool IsRecord { get; }
        public bool IsPartial { get; }
        public bool HasParameterlessCtor { get; }
        public string[] ReferencePropertyNames { get; }
        public string[] NestingParents { get; }
        public string TypePath { get; }

        public bool Equals(OwnedEntityInfo? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return Namespace == other.Namespace
                && TypeName == other.TypeName
                && TypeAccessibility == other.TypeAccessibility
                && IsRecord == other.IsRecord
                && IsPartial == other.IsPartial
                && HasParameterlessCtor == other.HasParameterlessCtor
                && TypePath == other.TypePath
                && ReferencePropertyNames.SequenceEqual(other.ReferencePropertyNames)
                && NestingParents.SequenceEqual(other.NestingParents);
        }

        public override bool Equals(object? obj) => Equals(obj as OwnedEntityInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(Namespace);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TypeName);
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TypeAccessibility);
                hash = (hash * 31) + IsRecord.GetHashCode();
                hash = (hash * 31) + IsPartial.GetHashCode();
                hash = (hash * 31) + HasParameterlessCtor.GetHashCode();
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(TypePath);
                return hash;
            }
        }
    }

    private static string BuildTypePath(INamedTypeSymbol type)
    {
        var typeNames = new Stack<string>();
        var current = type;

        while (current is not null)
        {
            typeNames.Push(current.MetadataName);
            current = current.ContainingType;
        }

        var typePath = string.Join(".", typeNames);
        var @namespace = type.ContainingNamespace?.IsGlobalNamespace == true
            ? string.Empty
            : type.ContainingNamespace?.ToString() ?? string.Empty;

        return string.IsNullOrEmpty(@namespace) ? typePath : $"{@namespace}.{typePath}";
    }

    private static string FormatTypeName(INamedTypeSymbol type) =>
        type.TypeParameters.Length > 0
            ? $"{type.Name}<{string.Join(", ", type.TypeParameters.Select(tp => tp.Name))}>"
            : type.Name;

    private static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
