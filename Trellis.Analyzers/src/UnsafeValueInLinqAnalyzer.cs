namespace Trellis.Analyzers;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects accessing .Value on Result/Maybe inside LINQ expressions
/// without first filtering by IsSuccess/HasValue.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeValueInLinqAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> LinqSelectMethods =
        ["Select", "SelectMany", "ToDictionary", "ToLookup", "GroupBy", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UnsafeValueInLinq];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Check if accessing .Value
        if (memberAccess.Name.Identifier.Text != "Value")
            return;

        // Check if inside a lambda
        var lambda = memberAccess.FirstAncestorOrSelf<LambdaExpressionSyntax>();
        if (lambda == null)
            return;

        // Check if the lambda is inside a LINQ method
        var argument = lambda.FirstAncestorOrSelf<ArgumentSyntax>();
        if (argument == null)
            return;

        var invocation = argument.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation == null)
            return;

        // Get the method name
        string? methodName = null;
        if (invocation.Expression is MemberAccessExpressionSyntax methodAccess)
            methodName = methodAccess.Name.Identifier.Text;
        else if (invocation.Expression is IdentifierNameSyntax identifier)
            methodName = identifier.Identifier.Text;

        if (methodName == null || !LinqSelectMethods.Contains(methodName))
            return;

        // Get the lambda parameter
        var lambdaParameter = LambdaSyntaxHelpers.GetLambdaParameter(lambda);
        if (lambdaParameter == null)
            return;

        // Check if the .Value access is on the lambda parameter
        if (!LambdaSyntaxHelpers.IsAccessOnParameter(memberAccess, lambdaParameter))
            return;

        // Check if the type of the expression is Result or Maybe
        var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
        var type = typeInfo.Type;

        if (type == null)
            return;

        string? typeName = null;
        string? checkProperty = null;

        if (type.IsResultType())
        {
            typeName = "Result.Value";
            checkProperty = "IsSuccess";
        }
        else if (type.IsMaybeType())
        {
            typeName = "Maybe.Value";
            checkProperty = "HasValue";
        }

        if (typeName == null || checkProperty == null)
            return;

        // Check if there's a Where clause before this that filters by IsSuccess/HasValue
        if (HasPriorFilterClause(invocation, checkProperty))
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.UnsafeValueInLinq,
            memberAccess.Name.GetLocation(),
            typeName,
            checkProperty);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool HasPriorFilterClause(
        InvocationExpressionSyntax currentInvocation,
        string checkProperty)
    {
        // Look for a .Where() clause before this Select/etc.
        // Pattern: collection.Where(x => x.IsSuccess).Select(x => x.Value)
        if (currentInvocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is InvocationExpressionSyntax priorInvocation)
        {
            // Check if prior invocation is Where
            if (priorInvocation.Expression is MemberAccessExpressionSyntax priorMemberAccess &&
                priorMemberAccess.Name.Identifier.Text == "Where")
            {
                // Check if the Where lambda checks the property
                var whereArgs = priorInvocation.ArgumentList.Arguments;
                if (whereArgs.Count > 0 && whereArgs[0].Expression is LambdaExpressionSyntax whereLambda)
                {
                    var whereBody = GetLambdaBody(whereLambda);
                    if (whereBody != null && ContainsPropertyCheck(whereBody, checkProperty))
                        return true;
                }
            }

            // Recurse to check further back in the chain
            return HasPriorFilterClause(priorInvocation, checkProperty);
        }

        return false;
    }

    private static CSharpSyntaxNode? GetLambdaBody(LambdaExpressionSyntax lambda) =>
        lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Body,
            ParenthesizedLambdaExpressionSyntax paren => paren.Body,
            _ => null
        };

    private static bool ContainsPropertyCheck(SyntaxNode body, string propertyName) =>
        // Check if the body contains a member access to the property
        body.DescendantNodesAndSelf()
            .OfType<MemberAccessExpressionSyntax>()
            .Any(m => m.Name.Identifier.Text == propertyName);
}