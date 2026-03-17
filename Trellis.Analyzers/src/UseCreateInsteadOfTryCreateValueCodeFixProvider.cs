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
/// Code fix provider that replaces TryCreate().Value with Create().
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseCreateInsteadOfTryCreateValueCodeFixProvider))]
[Shared]
public sealed class UseCreateInsteadOfTryCreateValueCodeFixProvider : CodeFixProvider
{
    private const string Title = "Replace TryCreate().Value with Create()";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticDescriptors.UseCreateInsteadOfTryCreateValue.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the .Value member access
        var node = root.FindNode(diagnosticSpan);
        var memberAccess = node.Parent as MemberAccessExpressionSyntax ?? node as MemberAccessExpressionSyntax;
        if (memberAccess == null || memberAccess.Name.Identifier.Text != "Value")
            return;

        // The expression should be TryCreate() call
        if (memberAccess.Expression is not InvocationExpressionSyntax tryCreateInvocation)
            return;

        // Verify it's a TryCreate call
        if (tryCreateInvocation.Expression is not MemberAccessExpressionSyntax tryCreateMemberAccess ||
            tryCreateMemberAccess.Name.Identifier.Text != "TryCreate")
            return;

        if (!CanRewriteToCreate(semanticModel, tryCreateInvocation, tryCreateMemberAccess))
            return;

        // Register the code fix
        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => ReplaceTryCreateValueWithCreateAsync(
                    context.Document,
                    memberAccess,
                    tryCreateInvocation,
                    tryCreateMemberAccess,
                    c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static bool CanRewriteToCreate(
        SemanticModel semanticModel,
        InvocationExpressionSyntax tryCreateInvocation,
        MemberAccessExpressionSyntax tryCreateMemberAccess)
    {
        var createInvocation = SyntaxFactory.InvocationExpression(
            tryCreateMemberAccess.WithName(SyntaxFactory.IdentifierName("Create")),
            tryCreateInvocation.ArgumentList);

        var symbolInfo = semanticModel.GetSpeculativeSymbolInfo(
            tryCreateInvocation.SpanStart,
            createInvocation,
            SpeculativeBindingOption.BindAsExpression);

        return symbolInfo.Symbol is IMethodSymbol { Name: "Create" };
    }

    private static async Task<Document> ReplaceTryCreateValueWithCreateAsync(
        Document document,
        MemberAccessExpressionSyntax valueAccess,
        InvocationExpressionSyntax tryCreateInvocation,
        MemberAccessExpressionSyntax tryCreateMemberAccess,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Create the new Create() invocation
        // Replace TryCreate with Create
        var createMemberAccess = tryCreateMemberAccess.WithName(
            SyntaxFactory.IdentifierName("Create"));

        // Create new invocation with the same arguments
        var createInvocation = SyntaxFactory.InvocationExpression(
            createMemberAccess,
            tryCreateInvocation.ArgumentList)
            .WithTriviaFrom(valueAccess);

        // Replace the entire .Value access with the Create() call
        var newRoot = root.ReplaceNode(valueAccess, createInvocation);
        return document.WithSyntaxRoot(newRoot);
    }
}