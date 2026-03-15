namespace Trellis.Analyzers;

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Code fix provider that replaces ternary operator with GetValueOrDefault() or Match().
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseFunctionalValueOrDefaultCodeFixProvider))]
[Shared]
public sealed class UseFunctionalValueOrDefaultCodeFixProvider : CodeFixProvider
{
    private const string TitleGetValueOrDefault = "Replace with GetValueOrDefault()";
    private const string TitleMatch = "Replace with Match()";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticDescriptors.UseFunctionalValueOrDefault.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the conditional expression (ternary)
        var ternary = root.FindNode(diagnosticSpan) as ConditionalExpressionSyntax;
        if (ternary == null)
            return;

        // Determine if the false branch is a simple default value
        var isSimpleDefault = IsSimpleDefaultValue(ternary.WhenFalse);

        if (isSimpleDefault)
        {
            // Offer GetValueOrDefault() code fix
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: TitleGetValueOrDefault,
                    createChangedDocument: c => ReplaceWithGetValueOrDefaultAsync(
                        context.Document,
                        ternary,
                        c),
                    equivalenceKey: TitleGetValueOrDefault),
                diagnostic);
        }

        // Always offer Match() as an alternative
        context.RegisterCodeFix(
            CodeAction.Create(
                title: TitleMatch,
                createChangedDocument: c => ReplaceWithMatchAsync(
                    context.Document,
                    ternary,
                    c),
                equivalenceKey: TitleMatch),
            diagnostic);
    }

    private static bool IsSimpleDefaultValue(ExpressionSyntax expression)
    {
        // Accept all literal values (null, default, numbers, strings, bools)
        // This enables GetValueOrDefault() for both simple defaults and custom values
        if (expression is LiteralExpressionSyntax)
            return true;

        if (expression is DefaultExpressionSyntax)
            return true;

        return false;
    }

    private static async Task<Document> ReplaceWithGetValueOrDefaultAsync(
        Document document,
        ConditionalExpressionSyntax ternary,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Extract the result expression from the condition
        // Pattern: result.IsSuccess ? result.Value : defaultValue
        if (ternary.Condition is not MemberAccessExpressionSyntax conditionMemberAccess)
            return document;

        var resultExpression = conditionMemberAccess.Expression;

        // Build GetValueOrDefault() call
        // If default is null/default, use parameterless GetValueOrDefault()
        // Otherwise, use GetValueOrDefault(defaultValue)
        InvocationExpressionSyntax newInvocation;

        if (IsSimpleNullOrDefault(ternary.WhenFalse))
        {
            // result.GetValueOrDefault()
            newInvocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    resultExpression,
                    SyntaxFactory.IdentifierName("GetValueOrDefault")));
        }
        else
        {
            // result.GetValueOrDefault(defaultValue)
            newInvocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    resultExpression,
                    SyntaxFactory.IdentifierName("GetValueOrDefault")),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(ternary.WhenFalse))));
        }

        var newExpression = newInvocation.WithTriviaFrom(ternary);
        var newRoot = root.ReplaceNode(ternary, newExpression);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> ReplaceWithMatchAsync(
        Document document,
        ConditionalExpressionSyntax ternary,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Extract the result expression from the condition
        // Pattern: result.IsSuccess ? result.Value : defaultValue
        if (ternary.Condition is not MemberAccessExpressionSyntax conditionMemberAccess)
            return document;

        var resultExpression = conditionMemberAccess.Expression;

        // Build Match() call: result.Match(value => value, _ => defaultValue)
        var successLambda = SyntaxFactory.SimpleLambdaExpression(
            SyntaxFactory.Parameter(SyntaxFactory.Identifier("value")),
            SyntaxFactory.IdentifierName("value"));

        var failureLambda = SyntaxFactory.SimpleLambdaExpression(
            SyntaxFactory.Parameter(SyntaxFactory.Identifier("_")),
            ternary.WhenFalse);

        var matchInvocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                resultExpression,
                SyntaxFactory.IdentifierName("Match")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.Argument(successLambda),
                    SyntaxFactory.Argument(failureLambda)
                })))
            .WithTriviaFrom(ternary);

        var newRoot = root.ReplaceNode(ternary, matchInvocation);
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool IsSimpleNullOrDefault(ExpressionSyntax expression)
    {
        // Check if it's a literal null, default literal, or default(T)
        if (expression is LiteralExpressionSyntax literal)
        {
            if (literal.IsKind(SyntaxKind.NullLiteralExpression) ||
                literal.IsKind(SyntaxKind.DefaultLiteralExpression))
                return true;

            // Check if it's a numeric zero or false
            var value = literal.Token.Value;
            return value is 0 or 0L or 0f or 0d or 0m or false;
        }

        return expression is DefaultExpressionSyntax;
    }
}