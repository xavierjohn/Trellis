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
            AnalyzeResultExpression(context, invocation);
        }
        // Check for await expressions
        else if (expression is AwaitExpressionSyntax awaitExpression)
        {
            var awaitedExpression = awaitExpression.Expression;

            // Unwrap ConfigureAwait: await x.ConfigureAwait(false) → x
            if (awaitedExpression is InvocationExpressionSyntax awaitedInvocation &&
                awaitedInvocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "ConfigureAwait")
            {
                awaitedExpression = memberAccess.Expression;
            }

            AnalyzeResultExpression(context, awaitedExpression);
        }
    }

    private static void AnalyzeResultExpression(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        var returnType = context.SemanticModel.GetTypeInfo(expression).Type;
        if (returnType == null)
            return;

        // Unwrap Task<T> or ValueTask<T>
        if (returnType.IsTaskType() && returnType is INamedTypeSymbol namedType && namedType.TypeArguments.Length == 1)
        {
            returnType = namedType.TypeArguments[0];
        }

        // Check if the return type is Result<T>
        if (!returnType.IsResultType())
            return;

        // Get the method name for the diagnostic message
        var methodName = GetExpressionName(expression, context.SemanticModel);

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ResultNotHandled,
            expression.GetLocation(),
            methodName);

        context.ReportDiagnostic(diagnostic);
    }

    private static string GetExpressionName(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                return memberAccess.Name.Identifier.Text;

            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
                return methodSymbol.Name;
        }

        if (expression is IdentifierNameSyntax identifierName)
            return identifierName.Identifier.Text;

        if (expression is MemberAccessExpressionSyntax memberAccessExpression)
        {
            if (memberAccessExpression.Name.Identifier.Text == "ConfigureAwait")
                return GetExpressionName(memberAccessExpression.Expression, semanticModel);

            var memberSymbolInfo = semanticModel.GetSymbolInfo(memberAccessExpression);
            if (memberSymbolInfo.Symbol is IPropertySymbol propertySymbol)
                return propertySymbol.Name;

            if (memberSymbolInfo.Symbol is IMethodSymbol methodSymbol)
                return methodSymbol.Name;

            return memberAccessExpression.Name.Identifier.Text;
        }

        var fallbackSymbolInfo = semanticModel.GetSymbolInfo(expression);
        if (fallbackSymbolInfo.Symbol is IMethodSymbol fallbackMethod)
            return fallbackMethod.Name;

        if (fallbackSymbolInfo.Symbol is IPropertySymbol fallbackProperty)
            return fallbackProperty.Name;

        return expression.ToString();
    }
}