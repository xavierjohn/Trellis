namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects throw statements inside Result chain lambdas (Bind, Map, Tap, Ensure).
/// Throwing defeats the purpose of Railway Oriented Programming.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ThrowInResultChainAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> ResultChainMethods =
        [
            "Bind", "BindAsync", "Map", "MapAsync", "Tap", "TapAsync", "Ensure", "EnsureAsync",
            "TapOnFailure", "TapOnFailureAsync", "MapOnFailure", "MapOnFailureAsync",
            "RecoverOnFailure", "RecoverOnFailureAsync", "DebugOnFailure", "DebugOnFailureAsync"
        ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.ThrowInResultChain];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeThrowStatement, SyntaxKind.ThrowStatement);
        context.RegisterSyntaxNodeAction(AnalyzeThrowExpression, SyntaxKind.ThrowExpression);
    }

    private static void AnalyzeThrowStatement(SyntaxNodeAnalysisContext context)
    {
        var throwStatement = (ThrowStatementSyntax)context.Node;
        AnalyzeThrow(context, throwStatement);
    }

    private static void AnalyzeThrowExpression(SyntaxNodeAnalysisContext context)
    {
        var throwExpression = (ThrowExpressionSyntax)context.Node;
        AnalyzeThrow(context, throwExpression);
    }

    private static void AnalyzeThrow(SyntaxNodeAnalysisContext context, SyntaxNode throwNode)
    {
        // Walk up to find if we're inside a lambda
        var lambda = throwNode.FirstAncestorOrSelf<LambdaExpressionSyntax>();
        if (lambda == null)
            return;

        // Check if the lambda is an argument to a Result chain method
        var argument = lambda.FirstAncestorOrSelf<ArgumentSyntax>();
        if (argument == null)
            return;

        var invocation = argument.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null)
            return;

        // Get the method name
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (!ResultChainMethods.Contains(methodName))
            return;

        // Verify it's from Trellis
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        if (!methodSymbol.IsTrellisExtensionMethod())
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.ThrowInResultChain,
            throwNode.GetLocation(),
            methodName);

        context.ReportDiagnostic(diagnostic);
    }
}