namespace Trellis.Analyzers;

using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Shared helpers for analyzing lambda expressions across Trellis analyzers.
/// </summary>
internal static class LambdaSyntaxHelpers
{
    internal static string? GetLambdaParameter(LambdaExpressionSyntax lambda) =>
        lambda switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
            ParenthesizedLambdaExpressionSyntax paren when paren.ParameterList.Parameters.Count > 0 =>
                paren.ParameterList.Parameters[0].Identifier.Text,
            _ => null
        };

    internal static bool IsAccessOnParameter(MemberAccessExpressionSyntax memberAccess, string parameterName) =>
        DependsOnParameter(memberAccess.Expression, parameterName);

    internal static bool DependsOnParameter(ExpressionSyntax expression, string parameterName) =>
        expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text == parameterName,
            MemberAccessExpressionSyntax memberAccess => DependsOnParameter(memberAccess.Expression, parameterName),
            InvocationExpressionSyntax invocation =>
                DependsOnParameter(invocation.Expression, parameterName) ||
                invocation.ArgumentList.Arguments.Any(arg => DependsOnParameter(arg.Expression, parameterName)),
            ElementAccessExpressionSyntax elementAccess =>
                DependsOnParameter(elementAccess.Expression, parameterName) ||
                elementAccess.ArgumentList.Arguments.Any(arg => DependsOnParameter(arg.Expression, parameterName)),
            CastExpressionSyntax cast => DependsOnParameter(cast.Expression, parameterName),
            ParenthesizedExpressionSyntax parenthesized => DependsOnParameter(parenthesized.Expression, parameterName),
            ConditionalAccessExpressionSyntax conditionalAccess => DependsOnParameter(conditionalAccess.Expression, parameterName),
            _ => false
        };
}
