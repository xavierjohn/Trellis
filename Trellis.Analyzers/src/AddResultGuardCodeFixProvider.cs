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
/// Code fix provider that wraps unsafe <c>Maybe&lt;T&gt;.Value</c> access in an
/// <c>if (maybe.HasValue)</c> guard. The Result-side fixes for TRLS003/TRLS004 were
/// removed in v2 (Result.Value no longer exists; Result.Error is nullable and handled by NRT).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddResultGuardCodeFixProvider))]
[Shared]
public sealed class AddResultGuardCodeFixProvider : CodeFixProvider
{
    private const string TitleMaybe = "Add 'if (maybe.HasValue)' guard";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticDescriptors.UnsafeMaybeValueAccess.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the member access (.Value or .Error)
        // The diagnostic is on the identifier, so we need to get the parent MemberAccessExpression
        var node = root.FindNode(diagnosticSpan);
        var memberAccess = node.Parent as MemberAccessExpressionSyntax ?? node as MemberAccessExpressionSyntax;
        if (memberAccess == null)
            return;

        // FixableDiagnosticIds already restricts this provider to TRLS003 (UnsafeMaybeValueAccess);
        // no defensive ID re-check needed here.

        var title = TitleMaybe;
        var guardProperty = "HasValue";

        // Register the code fix
        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: c => AddGuardAsync(
                    context.Document,
                    memberAccess,
                    guardProperty,
                    c),
                equivalenceKey: title),
            diagnostic);
    }

    private static async Task<Document> AddGuardAsync(
        Document document,
        MemberAccessExpressionSyntax memberAccess,
        string guardProperty,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        // Find the statement containing the member access
        var statement = memberAccess.FirstAncestorOrSelf<StatementSyntax>();
        if (statement == null)
            return document;

        // Get the expression being accessed (e.g., "maybe" from "maybe.Value")
        var resultExpression = memberAccess.Expression;

        if (resultExpression is InvocationExpressionSyntax)
            return document;

        // Find the containing block to get subsequent statements
        var containingBlock = statement.Parent as BlockSyntax;
        if (containingBlock == null)
            return document;

        // Find all statements from the current one to the end of the block
        var currentIndex = containingBlock.Statements.IndexOf(statement);
        if (currentIndex == -1)
            return document;

        // Get the identifier being accessed (e.g., "result")
        var resultIdentifier = GetBaseIdentifier(resultExpression);
        if (resultIdentifier == null)
            return document;

        // Determine which property we're guarding (Value or Error)
        var unsafeProperty = memberAccess.Name.Identifier.Text;

        // Find all consecutive statements that access the same unsafe property
        var statementsToWrap = GetStatementsAccessingUnsafeProperty(
            containingBlock.Statements,
            currentIndex,
            resultIdentifier.Identifier.Text,
            unsafeProperty);

        // If no statements to wrap, bail out (shouldn't happen, but safety check)
        if (statementsToWrap.Count == 0)
            return document;

        // Create the guard condition: result.IsSuccess or result.IsFailure or maybe.HasValue
        var guardCondition = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            resultExpression,
            SyntaxFactory.IdentifierName(guardProperty));

        // Strip leading trivia from wrapped statements so the formatter can apply correct indentation
        var strippedStatements = statementsToWrap
            .Select(s => s.WithLeadingTrivia(SyntaxFactory.ElasticMarker))
            .ToList();

        // Create a block statement with all the statements to wrap
        var blockStatement = SyntaxFactory.Block(strippedStatements)
            .WithAdditionalAnnotations(Microsoft.CodeAnalysis.Formatting.Formatter.Annotation);

        // Create the if statement wrapping all statements
        var ifStatement = SyntaxFactory.IfStatement(
            guardCondition,
            blockStatement)
            .WithLeadingTrivia(statement.GetLeadingTrivia())
            .WithAdditionalAnnotations(Microsoft.CodeAnalysis.Formatting.Formatter.Annotation);

        // Create new block with statements before the guard, then the if statement, then remaining statements
        var statementsBeforeGuard = containingBlock.Statements.Take(currentIndex);
        var statementsAfterWrapped = containingBlock.Statements.Skip(currentIndex + statementsToWrap.Count);

        // Check if the wrapped statements contain a return statement and there are no statements after
        // If so, add a default return to ensure all code paths return a value
        var needsDefaultReturn = IsTopLevelFunctionBlock(containingBlock) &&
            !statementsAfterWrapped.Any() &&
            statementsToWrap.Any(s => s is ReturnStatementSyntax);

        IEnumerable<StatementSyntax> newStatements = statementsBeforeGuard
            .Append(ifStatement)
            .Concat(statementsAfterWrapped);

        if (needsDefaultReturn)
        {
            // Add a default return statement after the if block
            var defaultReturn = SyntaxFactory.ReturnStatement(
                SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression))
                .WithLeadingTrivia(SyntaxFactory.ElasticMarker)
                .WithAdditionalAnnotations(Microsoft.CodeAnalysis.Formatting.Formatter.Annotation);
            newStatements = newStatements.Append(defaultReturn);
        }

        var newBlock = containingBlock.WithStatements(
            SyntaxFactory.List(newStatements));

        // Replace the old block with the new one
        var newRoot = root.ReplaceNode(containingBlock, newBlock);

        // Nodes already have Formatter.Annotation — the host IDE handles formatting
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool IsTopLevelFunctionBlock(BlockSyntax block) =>
        block.Parent is MethodDeclarationSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax;

    // Get the base identifier from an expression (e.g., "result" from "result.Error")
    // Recursive, but limited by realistic code depth
    private static IdentifierNameSyntax? GetBaseIdentifier(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax identifier => identifier,
            MemberAccessExpressionSyntax memberAccess => GetBaseIdentifier(memberAccess.Expression),
            _ => null
        };

    // Get consecutive statements that access the unsafe property (Value or Error) on the result,
    // including statements that use variables derived from the result
    private static List<StatementSyntax> GetStatementsAccessingUnsafeProperty(
        SyntaxList<StatementSyntax> statements,
        int startIndex,
        string resultIdentifier,
        string unsafeProperty)
    {
        var statementsToWrap = new List<StatementSyntax>();
        var trackedIdentifiers = new HashSet<string> { resultIdentifier };

        for (int i = startIndex; i < statements.Count; i++)
        {
            var stmt = statements[i];

            // Get all descendant nodes once to avoid multiple tree walks
            var descendants = stmt.DescendantNodes().ToList();

            // Check if this statement accesses the unsafe property on any tracked identifier
            var accessesUnsafeProperty = descendants
                .OfType<MemberAccessExpressionSyntax>()
                .Any(ma =>
                {
                    var baseId = GetBaseIdentifier(ma.Expression);
                    return baseId != null
                        && trackedIdentifiers.Contains(baseId.Identifier.Text)
                        && ma.Name.Identifier.Text == unsafeProperty;
                });

            // Also check if statement uses any tracked identifiers (for derived variables)
            var usesTrackedIdentifier = !accessesUnsafeProperty && descendants
                .OfType<IdentifierNameSyntax>()
                .Any(id => trackedIdentifiers.Contains(id.Identifier.Text));

            if (!accessesUnsafeProperty && !usesTrackedIdentifier)
            {
                // This statement doesn't access the unsafe property or use tracked variables - stop here
                break;
            }

            statementsToWrap.Add(stmt);

            // Track any new variables declared in this statement that are derived from tracked variables
            var declaredVariables = descendants
                .OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Initializer != null &&
                           v.Initializer.DescendantNodes()
                               .OfType<IdentifierNameSyntax>()
                               .Any(id => trackedIdentifiers.Contains(id.Identifier.Text)))
                .Select(v => v.Identifier.Text);

            foreach (var declaredVar in declaredVariables)
            {
                trackedIdentifiers.Add(declaredVar);
            }
        }

        return statementsToWrap;
    }
}