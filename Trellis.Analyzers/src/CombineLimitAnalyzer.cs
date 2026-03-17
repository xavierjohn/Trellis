namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects Combine chains exceeding the maximum supported tuple size (9 elements).
/// When a Combine call would produce a 10+ element tuple, reports TRLS019 with guidance
/// to refactor into logical sub-groups.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CombineLimitAnalyzer : DiagnosticAnalyzer
{
    private const int MaxTupleElements = 9;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.CombineChainTooLong];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a .Combine() or .CombineAsync() call
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (methodName is not ("Combine" or "CombineAsync"))
            return;

        // Only analyze the outermost Combine in a chain to avoid duplicate reports.
        // Inner Combine calls are receivers of outer ones — skip them.
        if (IsPartOfOuterCombineChain(invocation))
            return;

        // Count chain depth syntactically to determine the would-be tuple size
        var elementCount = CountCombineChainElements(invocation, context.SemanticModel);
        if (elementCount <= MaxTupleElements)
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.CombineChainTooLong,
            memberAccess.Name.GetLocation(),
            elementCount);

        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Checks if this invocation is the receiver of another .Combine() call,
    /// meaning it's an inner link in a larger chain and shouldn't be analyzed independently.
    /// </summary>
    private static bool IsPartOfOuterCombineChain(InvocationExpressionSyntax invocation) =>
        invocation.Parent is MemberAccessExpressionSyntax parentMemberAccess &&
        parentMemberAccess.Name.Identifier.Text is "Combine" or "CombineAsync" &&
        parentMemberAccess.Parent is InvocationExpressionSyntax;

    /// <summary>
    /// Counts the total number of elements that would exist after the current Combine call.
    /// This uses the semantic type of the receiver, so it also works when the chain continues
    /// from an intermediate variable like <c>temp.Combine(r6)</c> where <c>temp</c> is already a combined tuple result.
    /// </summary>
    private static int CountCombineChainElements(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return 0;

        return CountReceiverElements(memberAccess.Expression, semanticModel) + 1;
    }

    private static int CountReceiverElements(ExpressionSyntax receiverExpression, SemanticModel semanticModel)
    {
        var receiverType = semanticModel.GetTypeInfo(receiverExpression).Type;
        if (receiverType is not INamedTypeSymbol namedType)
            return 1;

        if (namedType.IsTaskType() && namedType.TypeArguments.Length == 1)
            return CountResultElements(namedType.TypeArguments[0]);

        return CountResultElements(namedType);
    }

    private static int CountResultElements(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType || !namedType.IsResultType())
            return 1;

        var resultValueType = namedType.TypeArguments[0];
        if (resultValueType is INamedTypeSymbol { IsTupleType: true } tupleType)
            return tupleType.TupleElements.Length;

        return 1;
    }
}