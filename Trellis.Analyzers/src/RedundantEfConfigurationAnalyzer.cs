namespace Trellis.Analyzers;

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects manual EF configuration that duplicates Trellis EF conventions.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RedundantEfConfigurationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.RedundantEfConfiguration];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (compilationContext.Compilation.GetTypeByMetadataName("Trellis.EntityFrameworkCore.MaybeConvention") is null)
                return;

            var conventionsAreWired = 0;
            var pendingDiagnostics = new ConcurrentQueue<Diagnostic>();

            compilationContext.RegisterSyntaxNodeAction(
                context =>
                {
                    var invocation = (InvocationExpressionSyntax)context.Node;
                    if (IsTrellisConventionsInvocation(context.SemanticModel, invocation))
                        Interlocked.Exchange(ref conventionsAreWired, 1);

                    AnalyzeInvocation(context, pendingDiagnostics.Enqueue);
                },
                SyntaxKind.InvocationExpression);

            compilationContext.RegisterCompilationEndAction(context =>
            {
                if (Volatile.Read(ref conventionsAreWired) == 0)
                    return;

                while (pendingDiagnostics.TryDequeue(out var diagnostic))
                    context.ReportDiagnostic(diagnostic);
            });
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, Action<Diagnostic> reportDiagnostic)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            _ => null
        };

        switch (methodName)
        {
            case "HasConversion":
                AnalyzeHasConversion(context, invocation, memberAccess, reportDiagnostic);
                break;
            case "OwnsOne":
            case "Ignore":
                AnalyzeEntityTypeBuilderConfiguration(context, invocation, memberAccess, methodName, reportDiagnostic);
                break;
        }
    }

    private static void AnalyzeHasConversion(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        Action<Diagnostic> reportDiagnostic)
    {
        if (!IsPropertyBuilderMethod(context, invocation))
            return;

        var propertyInvocation = FindPropertyInvocation(context, memberAccess.Expression);
        if (propertyInvocation is null)
            return;

        if (!TryGetConfiguredProperty(context, propertyInvocation, out var property))
            return;

        ReportIfTrellisConventionProperty(reportDiagnostic, memberAccess.Name.GetLocation(), "HasConversion", property);
    }

    private static void AnalyzeEntityTypeBuilderConfiguration(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        string methodName,
        Action<Diagnostic> reportDiagnostic)
    {
        if (!IsEntityTypeBuilderMethod(context, invocation, methodName))
            return;

        if (!TryGetConfiguredProperty(context, invocation, out var property))
            return;

        ReportIfTrellisConventionProperty(reportDiagnostic, memberAccess.Name.GetLocation(), methodName, property);
    }

    private static bool TryGetConfiguredProperty(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        out IPropertySymbol property)
    {
        property = null!;

        if (invocation.ArgumentList.Arguments.Count == 0)
            return false;

        if (invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
            return false;

        var lambdaParameter = LambdaSyntaxHelpers.GetLambdaParameter(lambda);
        if (lambdaParameter is null)
            return false;

        foreach (var memberAccess in lambda.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (!LambdaSyntaxHelpers.IsAccessOnParameter(memberAccess, lambdaParameter))
                continue;

            if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IPropertySymbol propertySymbol)
                continue;

            property = propertySymbol;
            return true;
        }

        return false;
    }

    private static void ReportIfTrellisConventionProperty(
        Action<Diagnostic> reportDiagnostic,
        Location location,
        string methodName,
        IPropertySymbol property)
    {
        if (!property.Type.IsMaybeType() && !HasOwnedEntityAttribute(property.Type))
            return;

        reportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.RedundantEfConfiguration,
            location,
            methodName,
            $"{property.ContainingType.Name}.{property.Name}"));
    }

    private static bool IsEntityTypeBuilderMethod(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        string expectedMethodName)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
            return false;

        if (methodSymbol.Name != expectedMethodName)
            return false;

        return IsEntityTypeBuilder(methodSymbol.ContainingType);
    }

    private static bool IsPropertyBuilderMethod(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol methodSymbol)
            return false;

        var containingType = methodSymbol.ContainingType;
        return containingType?.Name.IndexOf("PropertyBuilder", StringComparison.Ordinal) >= 0 &&
               containingType.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore.Metadata.Builders";
    }

    private static InvocationExpressionSyntax? FindPropertyInvocation(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax invocation &&
            IsEntityTypeBuilderMethod(context, invocation, "Property"))
            return invocation;

        foreach (var descendantInvocation in expression.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (IsEntityTypeBuilderMethod(context, descendantInvocation, "Property"))
                return descendantInvocation;
        }

        return null;
    }

    private static bool IsEntityTypeBuilder(INamedTypeSymbol? type)
    {
        while (type is not null)
        {
            if (type.Name == "EntityTypeBuilder" &&
                type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore.Metadata.Builders")
                return true;

            type = type.BaseType;
        }

        return false;
    }

    private static bool HasOwnedEntityAttribute(ITypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is
                {
                    Name: "OwnedEntityAttribute",
                    ContainingNamespace: var ns
                } && ns?.ToDisplayString() == "Trellis.EntityFrameworkCore")
                return true;
        }

        return false;
    }

    private static bool IsTrellisConventionsInvocation(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return false;

        var originalMethod = method.ReducedFrom ?? method;
        var containingType = originalMethod.ContainingType;
        if (containingType?.ContainingNamespace?.ToDisplayString() != "Trellis.EntityFrameworkCore")
            return false;

        return originalMethod.Name switch
        {
            "ApplyTrellisConventions" =>
                containingType.Name == "ModelConfigurationBuilderExtensions",
            "ApplyTrellisConventionsFor" =>
                containingType.Name is "ModelConfigurationBuilderExtensions" or "GeneratedTrellisConventions",
            _ => false
        };
    }
}
