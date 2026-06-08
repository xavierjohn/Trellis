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
        context.RegisterSyntaxNodeAction(AnalyzeArrowExpressionClause, SyntaxKind.ArrowExpressionClause);
    }

    private static void AnalyzeExpressionStatement(SyntaxNodeAnalysisContext context)
    {
        var expressionStatement = (ExpressionStatementSyntax)context.Node;
        AnalyzeDiscardedExpression(context, expressionStatement.Expression);
    }

    private static void AnalyzeArrowExpressionClause(SyntaxNodeAnalysisContext context)
    {
        var arrow = (ArrowExpressionClauseSyntax)context.Node;
        if (!ExpressionBodyDiscardsValue(arrow.Parent, context.SemanticModel))
            return;

        AnalyzeDiscardedExpression(context, arrow.Expression);
    }

    private static bool ExpressionBodyDiscardsValue(SyntaxNode? member, SemanticModel semanticModel)
    {
        if (member is null || semanticModel.GetDeclaredSymbol(member) is not IMethodSymbol method)
            return false;

        return method.ReturnsVoid || ReturnsNonGenericAwaitable(method);
    }

    private static bool ReturnsNonGenericAwaitable(IMethodSymbol method) =>
        method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 0 } returnType &&
        returnType.IsAnyTaskType();

    private static void AnalyzeDiscardedExpression(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            AnalyzeResultExpression(context, invocation);
        }
        else if (expression is AwaitExpressionSyntax awaitExpression)
        {
            AnalyzeResultExpression(context, UnwrapConfigureAwait(awaitExpression.Expression));
        }
    }

    private static ExpressionSyntax UnwrapConfigureAwait(ExpressionSyntax expression) =>
        expression is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "ConfigureAwait" } configureAwait,
        }
            ? configureAwait.Expression
            : expression;

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