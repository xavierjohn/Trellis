namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects the pattern "result.IsSuccess ? result.Value : defaultValue"
/// and suggests using GetValueOrDefault() or Match() instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TernaryValueOrDefaultAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UseFunctionalValueOrDefault];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeConditionalExpression, SyntaxKind.ConditionalExpression);
    }

    private static void AnalyzeConditionalExpression(SyntaxNodeAnalysisContext context)
    {
        var conditional = (ConditionalExpressionSyntax)context.Node;

        // Check if the condition is accessing IsSuccess on a Result
        if (conditional.Condition is not MemberAccessExpressionSyntax conditionMemberAccess)
            return;

        if (conditionMemberAccess.Name.Identifier.Text != "IsSuccess")
            return;

        // Get the type being accessed
        var conditionType = context.SemanticModel.GetTypeInfo(conditionMemberAccess.Expression).Type;
        if (!conditionType.IsResultType())
            return;

        // Check if whenTrue is accessing .Value on the same result
        if (conditional.WhenTrue is MemberAccessExpressionSyntax whenTrueMemberAccess)
        {
            if (whenTrueMemberAccess.Name.Identifier.Text == "Value")
            {
                // Check if it's the same expression using symbol comparison
                if (AreEquivalentExpressions(conditionMemberAccess.Expression, whenTrueMemberAccess.Expression, context.SemanticModel))
                {
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.UseFunctionalValueOrDefault,
                        conditional.GetLocation());

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    /// <summary>
    /// Check if two expressions refer to the same variable using symbol comparison.
    /// Falls back to syntax equivalence if symbols are not available.
    /// </summary>
    private static bool AreEquivalentExpressions(ExpressionSyntax expr1, ExpressionSyntax expr2, SemanticModel semanticModel)
    {
        // Repeated invocations may have side effects or return different results.
        // The analyzer should only suggest GetValueOrDefault()/Match() for stable expressions.
        if (expr1 is InvocationExpressionSyntax || expr2 is InvocationExpressionSyntax)
            return false;

        var symbol1 = semanticModel.GetSymbolInfo(expr1).Symbol;
        var symbol2 = semanticModel.GetSymbolInfo(expr2).Symbol;

        if (symbol1 != null && symbol2 != null)
            return SymbolEqualityComparer.Default.Equals(symbol1, symbol2);

        // Fallback to syntax equivalence
        return SyntaxFactory.AreEquivalent(expr1, expr2);
    }
}