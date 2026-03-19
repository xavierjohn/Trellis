namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects when an async lambda is used with a synchronous method variant
/// (e.g., Map instead of MapAsync) which causes the Task to not be properly awaited.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncLambdaWithSyncMethodAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableDictionary<string, string> SyncToAsyncMethods =
        ImmutableDictionary<string, string>.Empty
            .Add("Map", "MapAsync")
            .Add("Bind", "BindAsync")
            .Add("Tap", "TapAsync")
            .Add("Ensure", "EnsureAsync")
            .Add("TapOnFailure", "TapOnFailureAsync");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UseAsyncMethodVariant];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a known sync method (Map, Bind, Tap, Ensure)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (!SyncToAsyncMethods.TryGetValue(methodName, out var asyncVariant))
            return;

        // Verify it's from Trellis
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        if (!IsTrellisMethod(methodSymbol))
            return;

        // Check if any argument is an async lambda
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (IsAsyncLambda(argument.Expression, context.SemanticModel))
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.UseAsyncMethodVariant,
                    memberAccess.Name.GetLocation(),
                    asyncVariant,
                    methodName);

                context.ReportDiagnostic(diagnostic);
                return;
            }
        }
    }

    private static bool IsTrellisMethod(IMethodSymbol methodSymbol)
    {
        if (!methodSymbol.IsTrellisExtensionMethod())
            return false;

        var receiverType = methodSymbol.ReducedFrom?.Parameters.FirstOrDefault()?.Type
            ?? methodSymbol.Parameters.FirstOrDefault()?.Type;

        return receiverType.IsResultType() ||
               receiverType.IsMaybeType() ||
               receiverType.IsTaskWrappingResult();
    }

    private static bool IsAsyncLambda(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        // Check for async lambda: async x => ...
        if (expression is LambdaExpressionSyntax lambda)
        {
            // Check if lambda has async modifier
            if (lambda.Modifiers.Any(SyntaxKind.AsyncKeyword))
                return true;

            // Also check if the lambda body returns a Task (even without async keyword)
            // This handles cases like: x => SomeAsyncMethod(x)
            var typeInfo = semanticModel.GetTypeInfo(lambda);
            if (typeInfo.ConvertedType is INamedTypeSymbol delegateType &&
                delegateType.DelegateInvokeMethod != null)
            {
                var returnType = delegateType.DelegateInvokeMethod.ReturnType;
                if (returnType.IsAnyTaskType())
                    return true;
            }
        }

        // Check for method group that returns Task
        if (expression is IdentifierNameSyntax or MemberAccessExpressionSyntax)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(expression);
            if (symbolInfo.Symbol is IMethodSymbol method && method.ReturnType.IsAnyTaskType())
                return true;
        }

        return false;
    }
}