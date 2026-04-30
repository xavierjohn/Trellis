namespace Trellis.Analyzers;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects init-only properties (<c>{ get; init; }</c>) on classes or records
/// decorated with <c>[Trellis.EntityFrameworkCore.OwnedEntityAttribute]</c>. The supported shape
/// for <c>[OwnedEntity]</c> properties is <c>{ get; private set; }</c> so EF Core can populate
/// them during materialization through the generator-emitted parameterless constructor.
/// Only activates when the compilation references <c>Trellis.EntityFrameworkCore</c> (detected by
/// the presence of the well-known <c>OwnedEntityAttribute</c> type). Inherited init-only
/// properties from non-owned base types are also reported, mirroring the owned-entity generator's
/// base-type walk.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OwnedEntityInitOnlyPropertyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.OwnedEntityInitOnlyProperty];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var ownedEntityAttributeType = compilationContext.Compilation.GetTypeByMetadataName(
                "Trellis.EntityFrameworkCore.OwnedEntityAttribute");
            if (ownedEntityAttributeType is null)
                return;

            compilationContext.RegisterSymbolAction(
                ctx => AnalyzeNamedType(ctx, ownedEntityAttributeType),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context, INamedTypeSymbol ownedEntityAttributeType)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;
        if (typeSymbol.TypeKind != TypeKind.Class)
            return;

        if (!HasOwnedEntityAttribute(typeSymbol, ownedEntityAttributeType))
            return;

        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        for (var current = typeSymbol; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol property)
                    continue;
                if (property.IsStatic || property.IsIndexer)
                    continue;
                if (property.DeclaredAccessibility == Accessibility.Private)
                    continue;
                if (!property.ExplicitInterfaceImplementations.IsDefaultOrEmpty)
                    continue;

                // Track all instance property names (regardless of init-only status) so a
                // compliant derived property correctly hides a base init-only property under
                // the C# `new` modifier.
                if (!seen.Add(property.Name))
                    continue;

                if (property.SetMethod is null || !property.SetMethod.IsInitOnly)
                    continue;

                var location = GetInitKeywordLocation(property, context.CancellationToken)
                    ?? property.Locations.FirstOrDefault()
                    ?? typeSymbol.Locations.FirstOrDefault()
                    ?? Location.None;

                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.OwnedEntityInitOnlyProperty,
                    location,
                    property.Name,
                    typeSymbol.Name);
                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static Location? GetInitKeywordLocation(IPropertySymbol property, System.Threading.CancellationToken cancellationToken)
    {
        foreach (var declRef in property.DeclaringSyntaxReferences)
        {
            var node = declRef.GetSyntax(cancellationToken);
            if (node is PropertyDeclarationSyntax propertyDecl && propertyDecl.AccessorList is { } accessors)
            {
                var initAccessor = accessors.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.InitAccessorDeclaration));
                if (initAccessor is not null)
                    return initAccessor.Keyword.GetLocation();
            }
        }

        return null;
    }

    private static bool HasOwnedEntityAttribute(INamedTypeSymbol classSymbol, INamedTypeSymbol ownedEntityAttributeType)
    {
        foreach (var attr in classSymbol.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, ownedEntityAttributeType))
                return true;
        }

        return false;
    }
}
