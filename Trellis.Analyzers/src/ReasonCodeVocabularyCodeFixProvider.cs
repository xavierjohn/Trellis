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
using Microsoft.CodeAnalysis.Simplification;

/// <summary>
/// Replaces a reason-code literal that restates a frozen framework code with the constant that holds
/// it — <c>"value.not-null"</c> becomes <c>ValidationCodes.ValueNotNull</c>.
/// </summary>
/// <remarks>
/// Only the restated-code case is fixable, and the analyzer says so by attaching the constant to the
/// diagnostic. A literal that squats a framework namespace has no mechanical replacement, because only
/// the author knows which namespace the code belongs in; those diagnostics carry no property and are
/// left for a human. Fix-all is supported because the motivating case is a codebase that spelled the
/// same literal at dozens of call sites.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReasonCodeVocabularyCodeFixProvider))]
[Shared]
public sealed class ReasonCodeVocabularyCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticDescriptors.ReasonCodeVocabulary.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(
                    ReasonCodeVocabularyAnalyzer.ConstantPropertyKey, out var constant)
                || string.IsNullOrEmpty(constant))
            {
                continue;
            }

            // The argument and its expression have identical spans, and on a tie FindNode returns the
            // outer node — so without getInnermostNodeForTie this yields an ArgumentSyntax and the fix
            // silently never registers.
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                is not LiteralExpressionSyntax literal)
                continue;

            var title = $"Use {constant}";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: c => ReplaceWithConstantAsync(context.Document, literal, constant!, c),
                    equivalenceKey: title),
                diagnostic);
        }
    }

    private static async Task<Document> ReplaceWithConstantAsync(
        Document document,
        LiteralExpressionSyntax literal,
        string constant,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        // Fully qualified and then annotated for simplification, so the result reads as
        // `ValidationCodes.ValueNotNull` where `Trellis` is imported and stays correct where it is not.
        var replacement = SyntaxFactory
            .ParseExpression($"global::Trellis.{constant}")
            .WithAdditionalAnnotations(Simplifier.Annotation)
            .WithTriviaFrom(literal);

        return document.WithSyntaxRoot(root.ReplaceNode(literal, replacement));
    }
}
