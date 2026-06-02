namespace Trellis.Analyzers;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects accessing <c>.Value</c> on <c>Maybe&lt;T&gt;</c> inside LINQ
/// projections without first filtering by <c>HasValue</c>. The Result-side equivalent
/// was removed from the current API along with <c>Result&lt;T&gt;.Value</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeValueInLinqAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> LinqSelectMethods =
        ["Select", "SelectMany", "ToDictionary", "ToLookup", "GroupBy", "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [
            DiagnosticDescriptors.UnsafeMaybeValueInLinq,
            DiagnosticDescriptors.MaybeEqualsInQueryable,
            DiagnosticDescriptors.NonInlineHasValueWhereInQueryable
        ];

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

        // Only Maybe<T>.Value remains a runtime hazard.
        if (!type.IsMaybeType())
            return;

        const string typeName = "Maybe.Value";
        const string checkProperty = "HasValue";

        // Check if there's a Where clause before this that filters by HasValue
        if (HasPriorFilterClause(invocation, checkProperty))
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.UnsafeMaybeValueInLinq,
            memberAccess.Name.GetLocation(),
            typeName,
            checkProperty);

        context.ReportDiagnostic(diagnostic);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!IsInsideQueryableLinqContext(invocation, context.SemanticModel, context.CancellationToken))
            return;

        if (IsMaybeEqualsInvocation(invocation, context.SemanticModel, context.CancellationToken))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.MaybeEqualsInQueryable,
                GetInvocationNameLocation(invocation));

            context.ReportDiagnostic(diagnostic);
            return;
        }

        if (IsNonInlineHasValueWhereInvocation(invocation, context.SemanticModel, context.CancellationToken))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.NonInlineHasValueWhereInQueryable,
                GetInvocationNameLocation(invocation));

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsInsideQueryableLinqContext(
        SyntaxNode node,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        // Path 1: method-syntax (e.g. `q.Where(x => x.M.Equals(...))`). The call sits inside
        // a LambdaExpressionSyntax whose enclosing argument's owning invocation must bind to
        // a System.Linq.Queryable method.
        var lambda = node.FirstAncestorOrSelf<LambdaExpressionSyntax>();
        if (lambda is not null)
        {
            for (SyntaxNode? current = lambda; current is not null; current = current.Parent)
            {
                if (current is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax invocation }
                    && IsQueryableLinqInvocation(invocation, semanticModel, cancellationToken))
                    return true;
            }
        }

        // Path 2: query-syntax (e.g. `from x in q where x.M.Equals(...) select x`). The call has
        // no LambdaExpressionSyntax ancestor; instead, look for an enclosing QueryExpressionSyntax
        // whose source ('q' in the FROM clause) is IQueryable<T>. The compiler lowers each query
        // clause into the same System.Linq.Queryable method calls, so the EF-translation failure
        // mode is identical.
        var queryExpression = node.FirstAncestorOrSelf<QueryExpressionSyntax>();
        if (queryExpression?.FromClause.Expression is { } fromSource)
        {
            var fromType = semanticModel.GetTypeInfo(fromSource, cancellationToken).Type;
            if (IsIQueryable(fromType))
                return true;
        }

        return false;
    }

    private static bool IsIQueryable(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        if (type is INamedTypeSymbol named
            && named.ConstructedFrom?.ToDisplayString() == "System.Linq.IQueryable<T>")
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.ConstructedFrom?.ToDisplayString() == "System.Linq.IQueryable<T>")
                return true;
        }

        return false;
    }

    private static bool IsQueryableLinqInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        if (methodSymbol is null)
            return false;

        var originalMethod = methodSymbol.ReducedFrom ?? methodSymbol;
        return originalMethod.ContainingType?.ToDisplayString() == "System.Linq.Queryable";
    }

    private static bool IsMaybeEqualsInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name.Identifier.Text == nameof(object.Equals)
            && semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type.IsMaybeType())
            return true;

        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        var methodSymbol = symbolInfo.Symbol as IMethodSymbol
            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        return IsObjectEqualsMaybeInvocation(invocation, methodSymbol, semanticModel, cancellationToken);
    }

    private static bool IsObjectEqualsMaybeInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol? methodSymbol,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (methodSymbol is null || methodSymbol.Name != nameof(object.Equals) || !methodSymbol.IsStatic)
            return false;

        if (methodSymbol.ContainingType?.SpecialType != SpecialType.System_Object)
            return false;

        if (invocation.ArgumentList.Arguments.Count != 2)
            return false;

        return invocation.ArgumentList.Arguments
            .Any(argument => semanticModel.GetTypeInfo(argument.Expression, cancellationToken).Type.IsMaybeType());
    }

    private static bool IsNonInlineHasValueWhereInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess
            || memberAccess.Name.Identifier.Text != "HasValueWhere")
            return false;

        if (!semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type.IsMaybeType())
            return false;

        if (invocation.ArgumentList.Arguments.Count != 1)
            return false;

        return invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax;
    }

    private static Location GetInvocationNameLocation(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.GetLocation(),
            IdentifierNameSyntax identifier => identifier.GetLocation(),
            _ => invocation.GetLocation()
        };

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