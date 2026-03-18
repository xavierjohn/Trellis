namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects blocking or incorrect access to Task&lt;Result&lt;T&gt;&gt; or ValueTask&lt;Result&lt;T&gt;&gt;.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncResultMisuseAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.AsyncResultMisuse];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Check if accessing .Result or .Wait() on a Task/ValueTask
        var memberName = memberAccess.Name.Identifier.Text;
        if (memberName is not ("Result" or "Wait"))
            return;

        var expressionType = context.SemanticModel.GetTypeInfo(memberAccess.Expression).Type;
        if (expressionType == null)
            return;

        // Check if it's Task<Result<T>> or ValueTask<Result<T>>
        if (expressionType.IsAsyncResultType(out var resultInnerType))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.AsyncResultMisuse,
                memberAccess.Name.GetLocation(),
                resultInnerType);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Look for .GetAwaiter().GetResult() pattern
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "GetResult")
            return;

        // Check if the receiver is a .GetAwaiter() call
        if (memberAccess.Expression is not InvocationExpressionSyntax getAwaiterInvocation)
            return;

        if (getAwaiterInvocation.Expression is not MemberAccessExpressionSyntax getAwaiterMemberAccess)
            return;

        if (getAwaiterMemberAccess.Name.Identifier.Text != "GetAwaiter")
            return;

        // Get the type of the expression before .GetAwaiter()
        var expressionType = context.SemanticModel.GetTypeInfo(getAwaiterMemberAccess.Expression).Type;
        if (expressionType == null)
            return;

        if (expressionType.IsAsyncResultType(out var resultInnerType))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.AsyncResultMisuse,
                invocation.GetLocation(),
                resultInnerType);

            context.ReportDiagnostic(diagnostic);
        }
    }
}