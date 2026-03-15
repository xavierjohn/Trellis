namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects when Result return values are not handled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResultNotHandledAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.ResultNotHandled];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeExpressionStatement, SyntaxKind.ExpressionStatement);
    }

    private static void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context)
    {
        var expressionStatement = (ExpressionStatementSyntax)context.Node;
        var expression = expressionStatement.Expression;

        // Check for method invocations that return Result
        if (expression is InvocationExpressionSyntax invocation)
        {
            AnalyzeInvocation(context, invocation);
        }
        // Check for await expressions
        else if (expression is AwaitExpressionSyntax awaitExpression &&
                 awaitExpression.Expression is InvocationExpressionSyntax awaitedInvocation)
        {
            // Unwrap ConfigureAwait: await x.ConfigureAwait(false) → x
            var targetInvocation = awaitedInvocation;
            if (awaitedInvocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "ConfigureAwait" &&
                memberAccess.Expression is InvocationExpressionSyntax innerInvocation)
            {
                targetInvocation = innerInvocation;
            }

            AnalyzeInvocation(context, targetInvocation);
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        var returnType = methodSymbol.ReturnType;

        // Unwrap Task<T> or ValueTask<T>
        if (returnType.IsTaskType() && returnType is INamedTypeSymbol namedType && namedType.TypeArguments.Length == 1)
        {
            returnType = namedType.TypeArguments[0];
        }

        // Check if the return type is Result<T>
        if (!returnType.IsResultType())
            return;

        // Get the method name for the diagnostic message
        var methodName = GetMethodName(invocation, methodSymbol);

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ResultNotHandled,
            invocation.GetLocation(),
            methodName);

        context.ReportDiagnostic(diagnostic);
    }

    private static string GetMethodName(InvocationExpressionSyntax invocation, IMethodSymbol methodSymbol)
    {
        // For extension methods, try to get a more descriptive name
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text;
        }

        return methodSymbol.Name;
    }
}