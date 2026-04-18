namespace Trellis.Analyzers;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// TRLS023: Detects commands and queries that carry a typed aggregate ID property
/// (a property whose type implements <c>IScalarValue&lt;,&gt;</c> and whose name ends with "Id")
/// but do not implement <c>IAuthorizeResource&lt;T&gt;</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingResourceAuthorizationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.MissingResourceAuthorization];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var commandType = compilationContext.Compilation.GetTypeByMetadataName("Mediator.ICommand`1");
            var queryType = compilationContext.Compilation.GetTypeByMetadataName("Mediator.IQuery`1");
            if (commandType is null && queryType is null)
                return;

            var authorizeResourceType = compilationContext.Compilation.GetTypeByMetadataName(
                "Trellis.Authorization.IAuthorizeResource`1");
            if (authorizeResourceType is null)
                return;

            var scalarValueType = compilationContext.Compilation.GetTypeByMetadataName("Trellis.IScalarValue`2");

            compilationContext.RegisterSymbolAction(
                ctx => AnalyzeNamedType(ctx, commandType, queryType, authorizeResourceType, scalarValueType),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        INamedTypeSymbol? commandType,
        INamedTypeSymbol? queryType,
        INamedTypeSymbol authorizeResourceType,
        INamedTypeSymbol? scalarValueType)
    {
        var typeSymbol = (INamedTypeSymbol)context.Symbol;

        if (!ImplementsCommandOrQuery(typeSymbol, commandType, queryType))
            return;

        if (ImplementsAuthorizeResource(typeSymbol, authorizeResourceType))
            return;

        var idProperties = FindTypedIdProperties(typeSymbol, scalarValueType);
        if (idProperties.Count == 0)
            return;

        var propertyList = string.Join(", ", idProperties.Select(p => $"'{p}'"));

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.MissingResourceAuthorization,
            typeSymbol.Locations[0],
            additionalLocations: typeSymbol.Locations.Skip(1),
            typeSymbol.Name,
            propertyList);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool ImplementsCommandOrQuery(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol? commandType,
        INamedTypeSymbol? queryType)
    {
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            if (iface.IsGenericType)
            {
                var original = iface.OriginalDefinition;
                if ((commandType is not null && SymbolEqualityComparer.Default.Equals(original, commandType)) ||
                    (queryType is not null && SymbolEqualityComparer.Default.Equals(original, queryType)))
                    return true;
            }
        }

        return false;
    }

    private static bool ImplementsAuthorizeResource(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol authorizeResourceType)
    {
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            if (iface.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, authorizeResourceType))
                return true;
        }

        return false;
    }

    private static List<string> FindTypedIdProperties(
        INamedTypeSymbol typeSymbol,
        INamedTypeSymbol? scalarValueType)
    {
        var result = new List<string>();
        if (scalarValueType is null)
            return result;

        // Walk the type hierarchy to include inherited properties
        var current = typeSymbol;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol property)
                    continue;

                if (!property.Name.EndsWith("Id", StringComparison.Ordinal))
                    continue;

                if (ImplementsScalarValue(property.Type, scalarValueType))
                    result.Add(property.Name);
            }

            current = current.BaseType;
        }

        return result;
    }

    private static bool ImplementsScalarValue(ITypeSymbol propertyType, INamedTypeSymbol scalarValueType)
    {
        if (propertyType is INamedTypeSymbol namedType)
        {
            foreach (var iface in namedType.AllInterfaces)
            {
                if (iface.IsGenericType &&
                    SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, scalarValueType))
                    return true;
            }
        }

        return false;
    }
}
