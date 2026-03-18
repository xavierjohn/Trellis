namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects when Map is used with a lambda that returns a Result,
/// suggesting to use Bind instead to avoid Result{Result{T}}.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseBindInsteadOfMapAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> MapMethodNames = ["Map", "MapAsync"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UseBindInsteadOfMap];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a Map or MapAsync call
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (!MapMethodNames.Contains(methodName))
            return;

        // Get the method symbol to verify it's from Trellis
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        // Check if it's an extension method from Trellis
        if (!methodSymbol.IsTrellisExtensionMethod())
            return;

        // Check if the lambda argument returns a Result type
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0)
            return;

        var firstArgument = arguments[0].Expression;

        // Get the type of the lambda's return value
        var lambdaReturnType = GetLambdaReturnType(firstArgument, context.SemanticModel);
        if (lambdaReturnType == null)
            return;

        // Check if the return type is Result<T>
        if (lambdaReturnType.IsResultType())
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.UseBindInsteadOfMap,
                memberAccess.Name.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }

        // Also check for Task<Result<T>> or ValueTask<Result<T>>
        if (lambdaReturnType.IsTaskWrappingResult())
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.UseBindInsteadOfMap,
                memberAccess.Name.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static ITypeSymbol?GetLambdaReturnType(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        // Handle lambda expressions
        if (expression is LambdaExpressionSyntax lambda)
        {
            var typeInfo = semanticModel.GetTypeInfo(lambda);
            if (typeInfo.ConvertedType is INamedTypeSymbol delegateType &&
                delegateType.DelegateInvokeMethod != null)
            {
                return delegateType.DelegateInvokeMethod.ReturnType;
            }
        }

        // Handle method group conversions
        if (expression is IdentifierNameSyntax or MemberAccessExpressionSyntax)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(expression);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
            {
                return methodSymbol.ReturnType;
            }
        }

        return null;
    }
}