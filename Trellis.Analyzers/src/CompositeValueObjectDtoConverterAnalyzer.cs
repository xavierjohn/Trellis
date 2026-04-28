namespace Trellis.Analyzers;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// Analyzer that detects composite value objects exposed by request/response DTOs without
/// <c>CompositeValueObjectJsonConverter&lt;T&gt;</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompositeValueObjectDtoConverterAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.CompositeValueObjectDtoMissingJsonConverter];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var reportedProperties = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            compilationContext.RegisterSymbolAction(
                symbolContext => AnalyzeMethod(symbolContext, reportedProperties),
                SymbolKind.Method);

            compilationContext.RegisterSymbolAction(
                symbolContext => AnalyzeMessageType(symbolContext, reportedProperties),
                SymbolKind.NamedType);

            compilationContext.RegisterOperationAction(
                operationContext => AnalyzeEndpointMappingInvocation(operationContext, reportedProperties),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context, ISet<ISymbol> reportedProperties)
    {
        var method = (IMethodSymbol)context.Symbol;
        var isControllerMethod = IsControllerMethod(method);

        foreach (var parameter in method.Parameters)
        {
            if (!isControllerMethod && !HasAttribute(parameter, "FromBodyAttribute", "Microsoft.AspNetCore.Mvc"))
                continue;

            if (parameter.Type is not INamedTypeSymbol dtoType)
                continue;

            AnalyzeDtoType(dtoType, context.ReportDiagnostic, reportedProperties);
        }

        if (isControllerMethod)
        {
            foreach (var dtoType in GetCandidateDtoTypes(method.ReturnType))
                AnalyzeDtoType(dtoType, context.ReportDiagnostic, reportedProperties);
        }
    }

    private static void AnalyzeMessageType(SymbolAnalysisContext context, ISet<ISymbol> reportedProperties)
    {
        if (context.Symbol is not INamedTypeSymbol type || !IsMediatorMessageType(type))
            return;

        AnalyzeDtoType(type, context.ReportDiagnostic, reportedProperties);
    }

    private static void AnalyzeEndpointMappingInvocation(OperationAnalysisContext context, ISet<ISymbol> reportedProperties)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsEndpointMappingMethod(invocation.TargetMethod))
            return;

        foreach (var argument in invocation.Arguments)
        {
            var handlerMethod = GetEndpointHandlerMethod(argument.Value);
            if (handlerMethod is null)
                continue;

            foreach (var parameter in handlerMethod.Parameters)
            {
                if (parameter.Type is INamedTypeSymbol dtoType)
                    AnalyzeDtoType(dtoType, context.ReportDiagnostic, reportedProperties);
            }
        }
    }

    private static IMethodSymbol? GetEndpointHandlerMethod(IOperation operation) =>
        operation switch
        {
            IAnonymousFunctionOperation anonymousFunction => anonymousFunction.Symbol,
            IMethodReferenceOperation methodReference => methodReference.Method,
            IDelegateCreationOperation { Target: var target } => GetEndpointHandlerMethod(target),
            IConversionOperation { Operand: var operand } => GetEndpointHandlerMethod(operand),
            _ => null,
        };

    private static void AnalyzeDtoType(
        INamedTypeSymbol dtoType,
        Action<Diagnostic> reportDiagnostic,
        ISet<ISymbol> reportedProperties)
    {
        foreach (var member in dtoType.GetMembers())
        {
            if (member is not IPropertySymbol property)
                continue;

            if (property.IsStatic)
                continue;

            if (property.Type is not INamedTypeSymbol propertyType)
                continue;

            if (!IsOwnedCompositeValueObject(propertyType))
                continue;

            if (HasCompositeValueObjectJsonConverter(propertyType))
                continue;

            if (!TryMarkReported(property, reportedProperties))
                continue;

            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.CompositeValueObjectDtoMissingJsonConverter,
                GetDiagnosticLocation(property, dtoType),
                propertyType.Name,
                $"{dtoType.Name}.{property.Name}"));
        }
    }

    private static Location GetDiagnosticLocation(IPropertySymbol property, INamedTypeSymbol dtoType)
    {
        if (property.Locations.Length > 0)
            return property.Locations[0];

        return dtoType.Locations.Length > 0 ? dtoType.Locations[0] : Location.None;
    }

    private static bool TryMarkReported(IPropertySymbol property, ISet<ISymbol> reportedProperties)
    {
        lock (reportedProperties)
        {
            return reportedProperties.Add(property);
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetCandidateDtoTypes(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
            yield break;

        if (namedType.Name is "Task" or "ValueTask" && namedType.TypeArguments.Length == 1)
        {
            foreach (var candidate in GetCandidateDtoTypes(namedType.TypeArguments[0]))
                yield return candidate;

            yield break;
        }

        if (namedType.Name is "ActionResult" or "Result" && namedType.TypeArguments.Length == 1)
        {
            foreach (var candidate in GetCandidateDtoTypes(namedType.TypeArguments[0]))
                yield return candidate;
        }

        yield return namedType;

        foreach (var typeArgument in namedType.TypeArguments)
        {
            foreach (var candidate in GetCandidateDtoTypes(typeArgument))
                yield return candidate;
        }
    }

    private static bool IsControllerMethod(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary &&
        method.ContainingType is not null &&
        (InheritsFrom(method.ContainingType, "ControllerBase", "Microsoft.AspNetCore.Mvc") ||
         HasAttribute(method.ContainingType, "ApiControllerAttribute", "Microsoft.AspNetCore.Mvc"));

    private static bool IsMediatorMessageType(INamedTypeSymbol type)
    {
        foreach (var interfaceType in type.AllInterfaces)
        {
            if (interfaceType.Name is "IRequest" or "ICommand" or "IQuery" &&
                IsMediatorNamespace(interfaceType.ContainingNamespace))
                return true;
        }

        return false;
    }

    private static bool IsMediatorNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToDisplayString();
        return namespaceName == "Mediator" || namespaceName?.EndsWith(".Mediator", StringComparison.Ordinal) == true;
    }

    private static bool IsEndpointMappingMethod(IMethodSymbol method)
    {
        var endpointMethod = method.ReducedFrom ?? method;
        if (method.Name is not ("MapPost" or "MapPut" or "MapPatch" or "MapDelete") &&
            endpointMethod.Name is not ("MapPost" or "MapPut" or "MapPatch" or "MapDelete"))
            return false;

        return IsAspNetCoreEndpointMappingContainer(method.ContainingType) ||
               IsAspNetCoreEndpointMappingContainer(endpointMethod.ContainingType) ||
               IsAspNetCoreBuilderNamespace(method.ContainingNamespace) ||
               IsAspNetCoreBuilderNamespace(endpointMethod.ContainingNamespace);
    }

    private static bool IsAspNetCoreEndpointMappingContainer(INamedTypeSymbol? containingType) =>
        containingType is not null &&
        containingType.Name is "EndpointRouteBuilderExtensions" or "RouteHandlerBuilderExtensions" &&
        IsAspNetCoreBuilderNamespace(containingType.ContainingNamespace);

    private static bool IsAspNetCoreBuilderNamespace(INamespaceSymbol? namespaceSymbol)
    {
        var namespaceName = namespaceSymbol?.ToDisplayString();
        return namespaceName == "Microsoft.AspNetCore.Builder" ||
               namespaceName?.EndsWith(".Microsoft.AspNetCore.Builder", StringComparison.Ordinal) == true;
    }

    private static bool IsOwnedCompositeValueObject(INamedTypeSymbol type) =>
        HasAttribute(type, "OwnedEntityAttribute", "Trellis.EntityFrameworkCore") &&
        InheritsFrom(type, "ValueObject", "Trellis");

    private static bool HasCompositeValueObjectJsonConverter(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (!IsAttribute(attribute.AttributeClass, "JsonConverterAttribute", "System.Text.Json.Serialization"))
                continue;

            if (attribute.ConstructorArguments.Length != 1)
                continue;

            if (attribute.ConstructorArguments[0].Value is not INamedTypeSymbol converterType)
                continue;

            if (converterType.Name != "CompositeValueObjectJsonConverter" ||
                converterType.ContainingNamespace?.ToDisplayString() != "Trellis.Primitives" ||
                converterType.TypeArguments.Length != 1)
                continue;

            if (SymbolEqualityComparer.Default.Equals(converterType.TypeArguments[0], type))
                return true;
        }

        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string baseTypeName, string baseTypeNamespace)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == baseTypeName &&
                current.ContainingNamespace?.ToDisplayString() == baseTypeNamespace)
                return true;
        }

        return false;
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName, string attributeNamespace)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (IsAttribute(attribute.AttributeClass, attributeName, attributeNamespace))
                return true;
        }

        return false;
    }

    private static bool IsAttribute(INamedTypeSymbol? attributeType, string attributeName, string attributeNamespace) =>
        attributeType is not null &&
        attributeType.Name == attributeName &&
        attributeType.ContainingNamespace?.ToDisplayString() == attributeNamespace;
}