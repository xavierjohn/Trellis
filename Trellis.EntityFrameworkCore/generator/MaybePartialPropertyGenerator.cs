namespace Trellis.EntityFrameworkCore.Generator;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Source generator that implements <c>partial Maybe&lt;T&gt;</c> properties by emitting a private
/// nullable backing field and the property getter/setter. This eliminates the boilerplate required
/// when using <see cref="Maybe{T}"/> in EF Core entity types.
/// </summary>
/// <remarks>
/// <para>User writes:</para>
/// <code>
/// public partial class Customer
/// {
///     public partial Maybe&lt;PhoneNumber&gt; Phone { get; set; }
/// }
/// </code>
/// <para>Generator emits:</para>
/// <code>
/// partial class Customer
/// {
///     private PhoneNumber? _phone;
///     public partial Maybe&lt;PhoneNumber&gt; Phone
///     {
///         get =&gt; _phone is not null ? Maybe.From(_phone) : Maybe&lt;PhoneNumber&gt;.None;
///         set =&gt; _phone = value.HasValue ? value.Value : null;
///     }
/// }
/// </code>
/// <para>
/// The generated backing field is discovered by <c>MaybeConvention</c> in Trellis.EntityFrameworkCore,
/// which ignores the <c>Maybe&lt;T&gt;</c> CLR property and maps the private nullable field instead.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class MaybePartialPropertyGenerator : IIncrementalGenerator
{
    /// <summary>
    /// Diagnostic reported when a non-partial property of type <c>Maybe&lt;T&gt;</c> is found.
    /// </summary>
    private static readonly DiagnosticDescriptor s_shouldBePartial = new(
        id: "TRLSGEN100",
        title: "Maybe<T> property should be partial",
        messageFormat: "Property '{0}' of type Maybe<{1}> should be declared 'partial' so the source generator can emit the backing field and implementation",
        category: "Trellis.EntityFrameworkCore.Generator",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Find all partial properties whose type is Maybe<T>
        var partialProperties = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsPartialMaybeProperty(node),
                transform: static (ctx, ct) => GetMaybePropertyInfo(ctx, ct))
            .Where(static info => info is not null);

        // Find non-partial Maybe<T> properties for the analyzer diagnostic
        var nonPartialProperties = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsNonPartialMaybeProperty(node),
                transform: static (ctx, ct) => GetDiagnosticInfo(ctx, ct))
            .Where(static info => info is not null);

        // Group partial properties by containing type to emit one file per type
        var grouped = partialProperties.Collect();

        context.RegisterSourceOutput(grouped,
            static (spc, properties) => GenerateSource(spc, properties));

        context.RegisterSourceOutput(nonPartialProperties,
            static (spc, diagnostic) => ReportDiagnostic(spc, diagnostic));
    }

    /// <summary>
    /// Fast syntax filter: is this a partial property declaration with a generic type name
    /// that could be Maybe&lt;T&gt;?
    /// </summary>
    private static bool IsPartialMaybeProperty(SyntaxNode node)
    {
        if (node is not PropertyDeclarationSyntax prop)
            return false;

        if (!prop.Modifiers.Any(SyntaxKind.PartialKeyword))
            return false;

        // Check the type looks like Maybe<Something>
        return IsMaybeGenericName(prop.Type);
    }

    /// <summary>
    /// Fast syntax filter: is this a non-partial auto-property with a generic type that
    /// could be Maybe&lt;T&gt;? Used for the TRLSGEN100 diagnostic.
    /// </summary>
    private static bool IsNonPartialMaybeProperty(SyntaxNode node)
    {
        if (node is not PropertyDeclarationSyntax prop)
            return false;

        if (prop.Modifiers.Any(SyntaxKind.PartialKeyword))
            return false;

        // Must be an auto-property (has accessor list with no bodies)
        if (prop.AccessorList is null)
            return false;

        // The containing type must be partial for us to care
        if (prop.Parent is not TypeDeclarationSyntax typeDecl)
            return false;

        if (!typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            return false;

        return IsMaybeGenericName(prop.Type);
    }

    private static bool IsMaybeGenericName(TypeSyntax type)
    {
        // Handle "Maybe<T>" or "Trellis.Maybe<T>" etc.
        var name = type switch
        {
            GenericNameSyntax g => g,
            QualifiedNameSyntax { Right: GenericNameSyntax g } => g,
            _ => null
        };

        return name is not null
               && name.Identifier.Text == "Maybe"
               && name.TypeArgumentList.Arguments.Count == 1;
    }

    /// <summary>
    /// Semantic analysis: extract metadata for a partial Maybe&lt;T&gt; property.
    /// </summary>
    private static MaybePropertyInfo GetMaybePropertyInfo(
        GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var prop = (PropertyDeclarationSyntax)ctx.Node;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(prop, ct);
        if (symbol is null)
#pragma warning disable CS8603 // Possible null reference return — pipeline filters nulls via .Where()
            return null;
#pragma warning restore CS8603

        // Verify the type is actually Trellis.Maybe<T>
        if (symbol.Type is not INamedTypeSymbol { IsGenericType: true } namedType)
#pragma warning disable CS8603
            return null;
#pragma warning restore CS8603

        var originalDef = namedType.ConstructedFrom;
        if (originalDef.ContainingNamespace?.ToString() != "Trellis" || originalDef.Name != "Maybe")
#pragma warning disable CS8603
            return null;
#pragma warning restore CS8603

        var innerType = namedType.TypeArguments[0];
        var containingType = symbol.ContainingType;

        // Get setter accessibility
        var setterAccessibility = "";
        var isInitOnly = false;
        if (prop.AccessorList != null)
        {
            foreach (var accessor in prop.AccessorList.Accessors)
            {
                if (accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
                    accessor.IsKind(SyntaxKind.InitAccessorDeclaration))
                {
                    isInitOnly = accessor.IsKind(SyntaxKind.InitAccessorDeclaration);
                    if (accessor.Modifiers.Count > 0)
                        setterAccessibility = accessor.Modifiers.ToString();
                    break;
                }
            }
        }

        // Build nesting chain
        var nestingParents = new List<string>();
        INamedTypeSymbol? parent = containingType.ContainingType;
        while (parent is not null)
        {
            nestingParents.Insert(0, $"{AccessibilityToString(parent.DeclaredAccessibility)} partial {TypeKindKeyword(parent)} {FormatTypeName(parent)}");
            parent = parent.ContainingType;
        }

        var isRecord = prop.Parent is RecordDeclarationSyntax;
        var typePath = BuildTypePath(containingType);

        return new MaybePropertyInfo(
            @namespace: containingType.ContainingNamespace?.IsGlobalNamespace == true
                ? ""
                : containingType.ContainingNamespace?.ToString() ?? "",
            typeName: FormatTypeName(containingType),
            typeAccessibility: AccessibilityToString(containingType.DeclaredAccessibility),
            isRecord: isRecord,
            propertyName: symbol.Name,
            propertyAccessibility: AccessibilityToString(symbol.DeclaredAccessibility),
            setterAccessibility: setterAccessibility,
            isInitOnly: isInitOnly,
            innerTypeName: innerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            innerTypeShortName: innerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            innerTypeIsValueType: innerType.IsValueType,
            nestingParents: nestingParents.ToArray(),
            typePath: typePath);
    }

    /// <summary>
    /// Extract info for the TRLSGEN100 diagnostic on non-partial Maybe properties.
    /// </summary>
    private static (Location Location, string PropertyName, string InnerTypeName)? GetDiagnosticInfo(
        GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        var prop = (PropertyDeclarationSyntax)ctx.Node;
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(prop, ct);
#pragma warning restore CS8600
        if (symbol is null)
            return null;

        if (symbol.Type is not INamedTypeSymbol { IsGenericType: true } namedType)
            return null;

        var originalDef = namedType.ConstructedFrom;
        if (originalDef.ContainingNamespace?.ToString() != "Trellis" || originalDef.Name != "Maybe")
            return null;

        var innerType = namedType.TypeArguments[0];
        return (prop.GetLocation(), symbol.Name, innerType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    private static void ReportDiagnostic(
        SourceProductionContext spc,
        (Location Location, string PropertyName, string InnerTypeName)? info)
    {
        if (info is null) return;
        var (location, propertyName, innerTypeName) = info.Value;
        spc.ReportDiagnostic(Diagnostic.Create(s_shouldBePartial, location, propertyName, innerTypeName));
    }

    /// <summary>
    /// Generates the source for all collected Maybe&lt;T&gt; partial properties,
    /// grouped by containing type.
    /// </summary>
    private static void GenerateSource(
        SourceProductionContext spc,
        ImmutableArray<MaybePropertyInfo> properties)
    {
        if (properties.IsDefaultOrEmpty)
            return;

        // Group by fully-qualified type name
        var grouped = new Dictionary<string, List<MaybePropertyInfo>>();
        foreach (var prop in properties)
        {
            if (prop is null) continue;
            var key = prop.TypePath;
            if (!grouped.TryGetValue(key, out var list))
            {
                list = new List<MaybePropertyInfo>();
                grouped[key] = list;
            }

            list.Add(prop);
        }

        foreach (var kvp in grouped)
        {
            var props = kvp.Value;
            var first = props[0];
            var sb = new StringBuilder();

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(first.Namespace))
            {
                sb.Append("namespace ");
                sb.Append(first.Namespace);
                sb.AppendLine(";");
                sb.AppendLine();
            }

            sb.AppendLine("using Trellis;");
            sb.AppendLine();

            // Open nesting parents
            var indent = "";
            foreach (var parent in first.NestingParents)
            {
                sb.Append(indent);
                sb.AppendLine(parent);
                sb.Append(indent);
                sb.AppendLine("{");
                indent += "    ";
            }

            var typeKeyword = first.IsRecord ? "record class" : "class";
            sb.Append(indent);
            sb.Append(first.TypeAccessibility);
            sb.Append(" partial ");
            sb.Append(typeKeyword);
            sb.Append(' ');
            sb.AppendLine(first.TypeName);
            sb.Append(indent);
            sb.AppendLine("{");

            var memberIndent = indent + "    ";

            for (var i = 0; i < props.Count; i++)
            {
                var prop = props[i];
                var backingFieldName = ToCamelCaseField(prop.PropertyName);

                // The nullable type for the backing field
                var nullableInnerType = prop.InnerTypeName + "?";

                // Backing field
                sb.Append(memberIndent);
                sb.Append("private ");
                sb.Append(nullableInnerType);
                sb.Append(' ');
                sb.Append(backingFieldName);
                sb.AppendLine(";");

                // Property implementation
                sb.Append(memberIndent);
                sb.Append(prop.PropertyAccessibility);
                sb.Append(" partial Maybe<");
                sb.Append(prop.InnerTypeName);
                sb.Append("> ");
                sb.AppendLine(prop.PropertyName);
                sb.Append(memberIndent);
                sb.AppendLine("{");

                // Getter — different for value types vs reference types
                var getterIndent = memberIndent + "    ";
                sb.Append(getterIndent);
                if (prop.InnerTypeIsValueType)
                {
                    sb.Append("get => ");
                    sb.Append(backingFieldName);
                    sb.Append(".HasValue ? Maybe.From(");
                    sb.Append(backingFieldName);
                    sb.Append(".Value) : Maybe<");
                    sb.Append(prop.InnerTypeName);
                    sb.AppendLine(">.None;");
                }
                else
                {
                    sb.Append("get => ");
                    sb.Append(backingFieldName);
                    sb.Append(" is not null ? Maybe.From(");
                    sb.Append(backingFieldName);
                    sb.Append(") : Maybe<");
                    sb.Append(prop.InnerTypeName);
                    sb.AppendLine(">.None;");
                }

                // Setter
                sb.Append(getterIndent);
                if (!string.IsNullOrEmpty(prop.SetterAccessibility))
                {
                    sb.Append(prop.SetterAccessibility);
                    sb.Append(' ');
                }

                sb.Append(prop.IsInitOnly ? "init => " : "set => ");
                sb.Append(backingFieldName);
                sb.Append(" = value.HasValue ? value.Value : null;");
                sb.AppendLine();

                sb.Append(memberIndent);
                sb.AppendLine("}");

                if (i < props.Count - 1)
                    sb.AppendLine();
            }

            sb.Append(indent);
            sb.AppendLine("}");

            // Close nesting parents
            for (var i = first.NestingParents.Length - 1; i >= 0; i--)
            {
                indent = new string(' ', i * 4);
                sb.Append(indent);
                sb.AppendLine("}");
            }

            var fileName = $"{first.TypePath}.Maybe.g.cs";

            spc.AddSource(fileName, sb.ToString());
        }
    }

    private static string BuildTypePath(INamedTypeSymbol type)
    {
        var typeNames = new Stack<string>();
        var current = type;

        while (current is not null)
        {
            typeNames.Push(current.Name);
            current = current.ContainingType;
        }

        var typePath = string.Join(".", typeNames);
        var @namespace = type.ContainingNamespace?.IsGlobalNamespace == true
            ? string.Empty
            : type.ContainingNamespace?.ToString() ?? string.Empty;

        return string.IsNullOrEmpty(@namespace) ? typePath : $"{@namespace}.{typePath}";
    }

    private static string ToCamelCaseField(string propertyName)
    {
        if (propertyName.Length == 0) return "_";
        if (propertyName.Length == 1)
            return $"_{char.ToLowerInvariant(propertyName[0])}";
        return $"_{char.ToLowerInvariant(propertyName[0])}{propertyName.Substring(1)}";
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

    private static string FormatTypeName(INamedTypeSymbol type) =>
        type.TypeParameters.Length > 0
            ? $"{type.Name}<{string.Join(", ", type.TypeParameters.Select(tp => tp.Name))}>"
            : type.Name;
}