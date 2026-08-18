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
/// Code fix for TRLS023: chains <c>.WithVersionedRoute()</c> after the matched
/// <c>CreatedAtRoute(...)</c>, <c>CreatedAtAction(...)</c>, or <c>WithLocation(...)</c>
/// call so that the framework
/// injects the api-version into the generated Location header per-request.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CreatedAtRouteMissingApiVersionCodeFixProvider))]
[Shared]
public sealed class CreatedAtRouteMissingApiVersionCodeFixProvider : CodeFixProvider
{
    private const string Title = "Chain .WithVersionedRoute()";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticDescriptors.MissingApiVersionRouteValue.Id);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics.First();
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null) return;
        if (invocation.Expression is not MemberAccessExpressionSyntax mae) return;
        var name = mae.Name.Identifier.Text;
        if (name is not ("CreatedAtRoute" or "CreatedAtAction" or "WithLocation")) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => RewriteAsync(context.Document, invocation, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> RewriteAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        // Wrap the original invocation as the receiver of `.WithVersionedRoute()` — the chain
        // becomes `<original>.WithVersionedRoute()`. Trailing trivia from the original is moved
        // onto the new outer invocation so block-statement terminators (semicolons, closing
        // parens) stay attached to the chain's tail.
        var trailing = invocation.GetTrailingTrivia();
        var receiver = invocation.WithTrailingTrivia(SyntaxFactory.TriviaList());
        var chainedAccess = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            receiver,
            SyntaxFactory.IdentifierName("WithVersionedRoute"));
        var chainedInvocation = SyntaxFactory.InvocationExpression(chainedAccess)
            .WithTrailingTrivia(trailing);

        var newRoot = root.ReplaceNode(invocation, chainedInvocation);

        // Ensure `using Trellis.Asp.ApiVersioning;` is in scope. Without this the rewritten
        // extension call won't resolve and the code-fix produces uncompilable code. We don't
        // detect the package reference here (analyzers can't observe project metadata
        // reliably), so we always add the using if missing — if the package isn't referenced,
        // the user gets a clear "missing reference" build error pointing to the new namespace
        // rather than a confusing "method not found" error.
        if (newRoot is CompilationUnitSyntax cu && !HasUsing(cu, ApiVersioningNamespace))
        {
            cu = AddUsing(cu, ApiVersioningNamespace);
            return document.WithSyntaxRoot(cu);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private const string ApiVersioningNamespace = "Trellis.Asp.ApiVersioning";

    /// <summary>
    /// Adds <paramref name="namespaceName"/> as a <c>using</c> directive in the same scope where
    /// existing usings live. The repo convention is file-scoped namespaces with usings *inside*
    /// the namespace block, so adding to <see cref="CompilationUnitSyntax.Usings"/> would place
    /// the new using above the namespace declaration — out of order with the existing usings,
    /// and potentially flagged by IDE0065 (using directive placement).
    /// </summary>
    private static CompilationUnitSyntax AddUsing(CompilationUnitSyntax cu, string namespaceName)
    {
        var (fileScopedNs, blockScopedNs) = FindNamespaceWithUsings(cu);

        if (fileScopedNs is not null)
        {
            var trailing = fileScopedNs.Usings.Count > 0
                ? fileScopedNs.Usings[fileScopedNs.Usings.Count - 1].GetTrailingTrivia()
                : SyntaxFactory.TriviaList(SyntaxFactory.ElasticCarriageReturnLineFeed);

            var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
                .WithTrailingTrivia(trailing);
            var updated = fileScopedNs.AddUsings(directive);
            return cu.ReplaceNode(fileScopedNs, updated);
        }

        if (blockScopedNs is not null)
        {
            var trailing = blockScopedNs.Usings.Count > 0
                ? blockScopedNs.Usings[blockScopedNs.Usings.Count - 1].GetTrailingTrivia()
                : SyntaxFactory.TriviaList(SyntaxFactory.ElasticCarriageReturnLineFeed);

            var directive = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
                .WithTrailingTrivia(trailing);
            var updated = blockScopedNs.AddUsings(directive);
            return cu.ReplaceNode(blockScopedNs, updated);
        }

        var topTrailing = cu.Usings.Count > 0
            ? cu.Usings[cu.Usings.Count - 1].GetTrailingTrivia()
            : SyntaxFactory.TriviaList(SyntaxFactory.ElasticCarriageReturnLineFeed);

        var topDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(topTrailing);
        return cu.AddUsings(topDirective);
    }

    private static (FileScopedNamespaceDeclarationSyntax? FileScoped, NamespaceDeclarationSyntax? BlockScoped)
        FindNamespaceWithUsings(CompilationUnitSyntax cu)
    {
        FileScopedNamespaceDeclarationSyntax? firstFileScoped = null;
        NamespaceDeclarationSyntax? firstBlockScoped = null;

        foreach (var member in cu.Members)
        {
            switch (member)
            {
                case FileScopedNamespaceDeclarationSyntax fs:
                    firstFileScoped ??= fs;
                    if (fs.Usings.Count > 0)
                        return (fs, null);
                    break;
                case NamespaceDeclarationSyntax ns:
                    firstBlockScoped ??= ns;
                    if (ns.Usings.Count > 0)
                        return (null, ns);
                    break;
            }
        }

        if (cu.Usings.Count > 0)
            return (null, null);

        return (firstFileScoped, firstBlockScoped);
    }

    private static bool HasUsing(CompilationUnitSyntax cu, string namespaceName)
    {
        foreach (var u in cu.Usings)
        {
            if (u.Name?.ToString() == namespaceName)
                return true;
        }

        foreach (var member in cu.Members)
        {
            var nsUsings = member switch
            {
                NamespaceDeclarationSyntax ns => ns.Usings,
                FileScopedNamespaceDeclarationSyntax fs => fs.Usings,
                _ => default,
            };

            foreach (var u in nsUsings)
            {
                if (u.Name?.ToString() == namespaceName)
                    return true;
            }
        }

        return false;
    }
}