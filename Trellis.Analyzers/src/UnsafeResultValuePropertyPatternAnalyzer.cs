namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects property patterns naming <c>Value</c> on a <c>Result&lt;T&gt;</c> instance
/// without first discriminating by <c>IsSuccess</c>/<c>Error</c>. Property patterns evaluate the
/// member, and <c>Result&lt;T&gt;.Value</c> throws on a failed result.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeResultValuePropertyPatternAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UnsafeResultValuePropertyPattern];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Property patterns appear inside RecursivePattern: { Value: ... } in `is`/`switch`/`switch expression`.
        context.RegisterSyntaxNodeAction(AnalyzeRecursivePattern, SyntaxKind.RecursivePattern);
    }

    private static void AnalyzeRecursivePattern(SyntaxNodeAnalysisContext context)
    {
        var pattern = (RecursivePatternSyntax)context.Node;

        if (pattern.PropertyPatternClause is not { } clause)
            return;

        // Determine the type the pattern matches against.
        var matchedType = GetMatchedType(pattern, context.SemanticModel);
        if (!matchedType.IsResultType())
            return;

        foreach (var sub in clause.Subpatterns)
        {
            // Only flag the top-level Value subpattern. Nested subpatterns inside discriminator-led
            // patterns will themselves be re-analyzed by the recursive visit.
            if (sub.NameColon?.Name.Identifier.Text == "Value" ||
                sub.ExpressionColon?.Expression is IdentifierNameSyntax { Identifier.Text: "Value" })
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnsafeResultValuePropertyPattern,
                    sub.GetLocation()));
                return;
            }
        }
    }

    /// <summary>
    /// Determines the type the pattern is matched against by inspecting the parent context.
    /// </summary>
    private static ITypeSymbol? GetMatchedType(RecursivePatternSyntax pattern, SemanticModel model)
    {
        // If the pattern explicitly types the match (e.g., `is Result<int> { Value: ... }`) honor that.
        if (pattern.Type is { } typeSyntax)
        {
            return model.GetTypeInfo(typeSyntax).Type;
        }

        // Walk parents to find the governing expression / pattern.
        SyntaxNode? current = pattern.Parent;
        while (current is not null)
        {
            switch (current)
            {
                case IsPatternExpressionSyntax isExpr:
                    return model.GetTypeInfo(isExpr.Expression).Type;
                case SwitchExpressionSyntax switchExpr:
                    return model.GetTypeInfo(switchExpr.GoverningExpression).Type;
                case SwitchStatementSyntax switchStmt:
                    return model.GetTypeInfo(switchStmt.Expression).Type;
                case CasePatternSwitchLabelSyntax label
                    when label.Parent is SwitchSectionSyntax { Parent: SwitchStatementSyntax outer }:
                    return model.GetTypeInfo(outer.Expression).Type;
                case SwitchExpressionArmSyntax arm
                    when arm.Parent is SwitchExpressionSyntax outerExpr:
                    return model.GetTypeInfo(outerExpr.GoverningExpression).Type;
            }

            current = current.Parent;
        }

        return null;
    }
}
