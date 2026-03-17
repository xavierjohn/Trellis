namespace Trellis.Analyzers;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects manual error type discrimination (switch/if on error types)
/// and suggests using MatchError instead for exhaustive type-safe handling.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseMatchErrorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UseMatchErrorForDiscrimination];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeSwitchStatement, SyntaxKind.SwitchStatement);
        context.RegisterSyntaxNodeAction(AnalyzeSwitchExpression, SyntaxKind.SwitchExpression);
        context.RegisterSyntaxNodeAction(AnalyzeIfStatement, SyntaxKind.IsPatternExpression);
        context.RegisterSyntaxNodeAction(AnalyzeIsExpression, SyntaxKind.IsExpression);
    }

    private static void AnalyzeSwitchStatement(SyntaxNodeAnalysisContext context)
    {
        var switchStatement = (SwitchStatementSyntax)context.Node;

        // Check if switching on result.Error or similar
        if (!IsErrorExpression(switchStatement.Expression, context.SemanticModel))
            return;

        // Check if any case uses type patterns for error types
        var hasErrorTypePatterns = switchStatement.Sections.Any(section =>
            section.Labels.Any(label =>
                label is CasePatternSwitchLabelSyntax patternLabel &&
                IsErrorTypePattern(patternLabel.Pattern, context.SemanticModel)));

        if (hasErrorTypePatterns)
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.UseMatchErrorForDiscrimination,
                switchStatement.GetLocation());

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeSwitchExpression(SyntaxNodeAnalysisContext context)
    {
        var switchExpression = (SwitchExpressionSyntax)context.Node;

        // Check if switching on result.Error or similar
        if (!IsErrorExpression(switchExpression.GoverningExpression, context.SemanticModel))
            return;

        // Check if any arm uses type patterns for error types
        var hasErrorTypePatterns = switchExpression.Arms.Any(arm =>
            IsErrorTypePattern(arm.Pattern, context.SemanticModel));

        if (hasErrorTypePatterns)
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.UseMatchErrorForDiscrimination,
                switchExpression.GetLocation());

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeIfStatement(SyntaxNodeAnalysisContext context)
    {
        var isPattern = (IsPatternExpressionSyntax)context.Node;

        // Check if pattern matching on an error
        if (!IsErrorExpression(isPattern.Expression, context.SemanticModel))
            return;

        // Check if the pattern is checking for a specific error type
        if (IsErrorTypePattern(isPattern.Pattern, context.SemanticModel))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.UseMatchErrorForDiscrimination,
                isPattern.GetLocation());

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeIsExpression(SyntaxNodeAnalysisContext context)
    {
        var isExpression = (BinaryExpressionSyntax)context.Node;

        if (!IsErrorExpression(isExpression.Left, context.SemanticModel))
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(isExpression.Right);
        if (!typeInfo.Type.IsErrorOrDerivedType())
        {
            var typeSymbol = context.SemanticModel.GetSymbolInfo(isExpression.Right).Symbol as ITypeSymbol;
            if (!typeSymbol.IsErrorOrDerivedType())
                return;
        }

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.UseMatchErrorForDiscrimination,
            isExpression.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    // Check if expression accesses an Error (e.g., result.Error)
    private static bool IsErrorExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression);
        return typeInfo.Type.IsErrorOrDerivedType();
    }

    // Check if pattern matches a specific error type
    private static bool IsErrorTypePattern(PatternSyntax pattern, SemanticModel semanticModel)
    {
        TypeSyntax? typeSyntax = pattern switch
        {
            DeclarationPatternSyntax declarationPattern => declarationPattern.Type,
            TypePatternSyntax typePattern => typePattern.Type,
            _ => null
        };

        if (typeSyntax is null)
        {
            if (pattern is ConstantPatternSyntax constantPattern)
            {
                var constantType = semanticModel.GetTypeInfo(constantPattern.Expression).Type;
                if (constantType.IsErrorOrDerivedType())
                    return true;

                var symbol = semanticModel.GetSymbolInfo(constantPattern.Expression).Symbol as ITypeSymbol;
                return symbol.IsErrorOrDerivedType();
            }

            return false;
        }

        var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
        if (typeInfo.Type.IsErrorOrDerivedType())
            return true;

        var typeSymbol = semanticModel.GetSymbolInfo(typeSyntax).Symbol as ITypeSymbol;
        return typeSymbol.IsErrorOrDerivedType();
    }
}