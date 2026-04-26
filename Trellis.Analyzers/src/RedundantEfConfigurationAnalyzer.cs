namespace Trellis.Analyzers;

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
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
    // The two canonical types that own the ApplyTrellisConventions / ApplyTrellisConventionsFor
    // extension methods.  Any call that resolves to a *different* containing type is user code
    // and must NOT enable the analyzer.
    private static readonly ImmutableHashSet<string> TrellisConventionContainingTypes =
        ImmutableHashSet.Create(
            "Trellis.EntityFrameworkCore.ModelConfigurationBuilderExtensions",
            "Trellis.EntityFrameworkCore.GeneratedTrellisConventions");

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

            // Per-compilation state:
            //   wired  — set to 1 when a Trellis-conventions call is confirmed via semantic model
            //   pending — candidate diagnostics collected before the wiring tree is analyzed
            var wired = new StrongBox<int>(0);
            var pending = new ConcurrentBag<Diagnostic>();

            // Semantic-model pass: resolve each candidate invocation and set wired flag.
            compilationContext.RegisterSemanticModelAction(semanticModelContext =>
            {
                var root = semanticModelContext.SemanticModel.SyntaxTree
                    .GetRoot(semanticModelContext.CancellationToken);

                foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (!IsCandidateConventionsName(invocation))
                        continue;

                    if (semanticModelContext.SemanticModel
                            .GetSymbolInfo(invocation, semanticModelContext.CancellationToken)
                            .Symbol is not IMethodSymbol method)
                        continue;

                    var containingType = method.ContainingType?.ToDisplayString();
                    if (containingType is not null &&
                        TrellisConventionContainingTypes.Contains(containingType))
                    {
                        Interlocked.Exchange(ref wired.Value, 1);
                        return;
                    }
                }
            });

            // Syntax-node pass: collect candidate diagnostics (deferred — the wiring call
            // may live in a tree that has not yet been semantically analyzed).
            compilationContext.RegisterSyntaxNodeAction(
                nodeContext => CollectCandidates(nodeContext, pending),
                SyntaxKind.InvocationExpression);

            // Drain pending diagnostics only when all trees have been walked and wired == 1.
            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                if (Volatile.Read(ref wired.Value) == 0)
                    return;

                foreach (var diagnostic in pending)
                    endContext.ReportDiagnostic(diagnostic);
            });
        });
    }

    // Returns true when the invocation's method name looks like a Trellis-conventions call.
    // Actual symbol resolution happens in RegisterSemanticModelAction.
    private static bool IsCandidateConventionsName(InvocationExpressionSyntax invocation)
    {
        var methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax id } => id.Identifier.Text,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax gen } => gen.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax gen => gen.Identifier.Text,
            _ => null
        };

        return methodName is "ApplyTrellisConventions" or "ApplyTrellisConventionsFor";
    }

    private static void CollectCandidates(SyntaxNodeAnalysisContext context, ConcurrentBag<Diagnostic> pending)
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
                CollectHasConversionCandidate(context, invocation, memberAccess, pending);
                break;
            case "OwnsOne":
            case "Ignore":
                CollectEntityTypeBuilderCandidate(context, invocation, memberAccess, methodName, pending);
                break;
        }
    }

    private static void CollectHasConversionCandidate(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        ConcurrentBag<Diagnostic> pending)
    {
        if (!IsPropertyBuilderMethod(context, invocation))
            return;

        var propertyInvocation = FindPropertyInvocation(context, memberAccess.Expression);
        if (propertyInvocation is null)
            return;

        if (!TryGetConfiguredProperty(context, propertyInvocation, out var property))
            return;

        EnqueueIfTrellisConventionProperty(memberAccess.Name.GetLocation(), "HasConversion", property, pending);
    }

    private static void CollectEntityTypeBuilderCandidate(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        MemberAccessExpressionSyntax memberAccess,
        string methodName,
        ConcurrentBag<Diagnostic> pending)
    {
        if (!IsEntityTypeBuilderMethod(context, invocation, methodName))
            return;

        if (!TryGetConfiguredProperty(context, invocation, out var property))
            return;

        EnqueueIfTrellisConventionProperty(memberAccess.Name.GetLocation(), methodName, property, pending);
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

    private static void EnqueueIfTrellisConventionProperty(
        Location location,
        string methodName,
        IPropertySymbol property,
        ConcurrentBag<Diagnostic> pending)
    {
        if (!property.Type.IsMaybeType() && !HasOwnedEntityAttribute(property.Type))
            return;

        pending.Add(Diagnostic.Create(
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
}
