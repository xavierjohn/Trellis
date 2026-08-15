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
/// Code fix provider that replaces SaveChangesAsync/SaveChanges with SaveChangesResultUnitAsync
/// or SaveChangesResultAsync depending on whether the return value is used.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseSaveChangesResultCodeFixProvider))]
[Shared]
public sealed class UseSaveChangesResultCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticDescriptors.UseSaveChangesResult.Id);

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the method name identifier node
        var node = root.FindNode(diagnosticSpan);
        if (node is not IdentifierNameSyntax identifierNode)
            return;

        var invocation = identifierNode.FirstAncestorOrSelf<InvocationExpressionSyntax>();

        // For sync SaveChanges(), only offer the fix when it's a standalone expression statement.
        // The code fix converts the method to async Task.
        // Skip the fix when:
        //  - The call is not a standalone expression statement (assignments, returns, etc.)
        //  - The containing method has a non-void return type (async int is not valid C#)
        if (identifierNode.Identifier.Text == "SaveChanges")
        {
            if (invocation?.FirstAncestorOrSelf<ExpressionStatementSyntax>() is null)
                return;

            var containingFunction = FindContainingFunction(identifierNode);
            if (containingFunction is null)
                return;

            var returnType = containingFunction switch
            {
                MethodDeclarationSyntax m => m.ReturnType,
                LocalFunctionStatementSyntax l => l.ReturnType,
                _ => null
            };
            var isVoid = returnType is PredefinedTypeSyntax predefined
                         && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword);
            if (!isVoid)
                return;
        }

        // Determine replacement: SaveChangesResultUnitAsync (standalone) vs SaveChangesResultAsync (value used)
        var replacementName = GetReplacementMethodName(identifierNode);

        // SaveChangesResultAsync returns Result<int>, not int. A mechanical rename only compiles
        // where the consuming context can rebind to the new type, which in practice means an
        // implicitly-typed local. Anywhere else (explicit int locals, assignments, conditions,
        // arguments, return statements) the caller must restructure into the railway by hand.
        if (replacementName == "SaveChangesResultAsync" && !IsRebindableTarget(identifierNode))
            return;

        var title = $"Use {replacementName}";

        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: c => ApplyFixAsync(context.Document, identifierNode, replacementName, c),
                equivalenceKey: title),
            diagnostic);
    }

    /// <summary>
    /// Determines whether to use SaveChangesResultUnitAsync or SaveChangesResultAsync.
    /// If the return value of SaveChangesAsync is used (assignment, return, condition, etc.),
    /// SaveChangesResultAsync (returning Result&lt;int&gt;) is the correct replacement.
    /// </summary>
    private static string GetReplacementMethodName(IdentifierNameSyntax identifierNode)
    {
        if (identifierNode.Identifier.Text != "SaveChangesAsync")
            return "SaveChangesResultUnitAsync";

        var effectiveNode = FindEffectiveExpression(identifierNode);
        if (effectiveNode is null)
            return "SaveChangesResultUnitAsync";

        // If the effective expression is a direct child of an ExpressionStatement, the value is discarded
        return effectiveNode.Parent is ExpressionStatementSyntax
            ? "SaveChangesResultUnitAsync"
            : "SaveChangesResultAsync";
    }

    /// <summary>
    /// Determines whether the consuming context can absorb a change of the expression's type from
    /// <c>int</c> to <c>Result&lt;int&gt;</c>. Only an implicitly-typed local declaration rebinds,
    /// so that is the sole shape where the mechanical rename is guaranteed to compile.
    /// </summary>
    private static bool IsRebindableTarget(IdentifierNameSyntax identifierNode) =>
        FindEffectiveExpression(identifierNode)?.Parent is EqualsValueClauseSyntax equalsValue
        && equalsValue.Parent is VariableDeclaratorSyntax declarator
        && declarator.Parent is VariableDeclarationSyntax { Type.IsVar: true };

    /// <summary>
    /// Walks up from the method-name identifier through chained calls (e.g. <c>.ConfigureAwait(false)</c>)
    /// and any enclosing <c>await</c> to the outermost expression node whose type the caller observes.
    /// </summary>
    private static SyntaxNode? FindEffectiveExpression(IdentifierNameSyntax identifierNode)
    {
        var invocation = identifierNode.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
            return null;

        // Pattern: InvocationExpression is the expression of a MemberAccessExpression,
        //          which is the expression of another InvocationExpression
        SyntaxNode current = invocation;
        while (current.Parent is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Parent is InvocationExpressionSyntax outerInvocation &&
               memberAccess.Expression == current)
        {
            current = outerInvocation;
        }

        return current.Parent is AwaitExpressionSyntax awaitExpr ? awaitExpr : current;
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        IdentifierNameSyntax identifierNode,
        string replacementName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newIdentifier = SyntaxFactory.IdentifierName(replacementName);

        // Unqualified calls (e.g. SaveChangesAsync(ct) inside a DbContext subclass) need
        // an explicit 'this.' receiver because the replacement is an extension method.
        bool isUnqualified = identifierNode.Parent is InvocationExpressionSyntax inv
                             && inv.Expression == identifierNode;
        SyntaxNode replacement = isUnqualified
            ? SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.ThisExpression(),
                    newIdentifier)
                .WithTriviaFrom(identifierNode)
            : newIdentifier.WithTriviaFrom(identifierNode);

        var newRoot = root.ReplaceNode(identifierNode, replacement);

        // If the original call was synchronous SaveChanges(), we need to add await and make the method async
        if (identifierNode.Identifier.Text == "SaveChanges")
            newRoot = AddAwaitAndMakeAsync(newRoot, identifierNode.SpanStart);

        // Ensure 'using Trellis.EntityFrameworkCore;' is present (replacement methods are extension methods)
        newRoot = AddUsingIfMissing(newRoot, "Trellis.EntityFrameworkCore");

        return document.WithSyntaxRoot(newRoot);
    }

    private static SyntaxNode AddUsingIfMissing(SyntaxNode root, string namespaceName)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        // Check top-level usings
        if (compilationUnit.Usings.Any(u => u.Name?.ToString() == namespaceName))
            return root;

        // Check usings inside namespace declarations
        foreach (var member in compilationUnit.Members)
        {
            var namespaceUsings = member switch
            {
                NamespaceDeclarationSyntax ns => ns.Usings,
                FileScopedNamespaceDeclarationSyntax fs => fs.Usings,
                _ => default
            };

            if (namespaceUsings.Any(u => u.Name?.ToString() == namespaceName))
                return root;
        }

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
            .WithTrailingTrivia(DetectEndOfLine(compilationUnit));

        return compilationUnit.AddUsings(usingDirective);
    }

    /// <summary>
    /// Detects the document's line ending style by finding the first newline in the source text.
    /// </summary>
    private static SyntaxTrivia DetectEndOfLine(SyntaxNode root)
    {
        var sourceText = root.SyntaxTree.GetText();
        for (var i = 0; i < sourceText.Length; i++)
        {
            if (sourceText[i] == '\n')
            {
                if (i > 0 && sourceText[i - 1] == '\r')
                    return SyntaxFactory.CarriageReturnLineFeed;
                return SyntaxFactory.LineFeed;
            }
        }

        return SyntaxFactory.CarriageReturnLineFeed;
    }

    private static SyntaxNode AddAwaitAndMakeAsync(SyntaxNode root, int originalSpanStart)
    {
        // Find the invocation expression that contains our replaced identifier
        var newIdentifier = root.FindToken(originalSpanStart).Parent;
        var invocation = newIdentifier?.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
            return root;

        // Find the containing expression statement
        var expressionStatement = invocation.FirstAncestorOrSelf<ExpressionStatementSyntax>();
        if (expressionStatement is null)
            return root;

        // Wrap the invocation in an await expression
        var awaitExpression = SyntaxFactory.AwaitExpression(invocation.WithoutLeadingTrivia())
            .WithLeadingTrivia(invocation.GetLeadingTrivia());
        var newStatement = expressionStatement.WithExpression(awaitExpression);
        root = root.ReplaceNode(expressionStatement, newStatement);

        // Find the containing method or local function and make it async with Task return type
        var containingFunction = FindContainingFunction(root.FindToken(originalSpanStart).Parent);
        if (containingFunction is null)
            return root;

        root = MakeAsyncWithTaskReturnType(root, containingFunction);

        // Add 'using System.Threading.Tasks;' if not already present
        root = AddUsingIfMissing(root, "System.Threading.Tasks");

        return root;
    }

    /// <summary>
    /// Finds the nearest containing function scope — either a method or a local function.
    /// </summary>
    private static SyntaxNode? FindContainingFunction(SyntaxNode? node) =>
        node?.Ancestors().FirstOrDefault(a => a is MethodDeclarationSyntax or LocalFunctionStatementSyntax);

    /// <summary>
    /// Adds the <c>async</c> modifier and changes <c>void</c> return type to <c>Task</c>
    /// on the given method or local function declaration.
    /// </summary>
    private static SyntaxNode MakeAsyncWithTaskReturnType(SyntaxNode root, SyntaxNode functionDecl)
    {
        var (modifiers, returnType) = functionDecl switch
        {
            MethodDeclarationSyntax m => (m.Modifiers, m.ReturnType),
            LocalFunctionStatementSyntax l => (l.Modifiers, l.ReturnType),
            _ => (default, null)
        };

        if (returnType is null || modifiers.Any(SyntaxKind.AsyncKeyword))
            return root;

        var asyncToken = SyntaxFactory.Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(SyntaxFactory.Space);

        SyntaxNode newDecl = functionDecl switch
        {
            MethodDeclarationSyntax m => m.AddModifiers(asyncToken)
                .WithReturnType(MakeTaskReturnType(m.ReturnType)),
            LocalFunctionStatementSyntax l => l.AddModifiers(asyncToken)
                .WithReturnType(MakeTaskReturnType(l.ReturnType)),
            _ => functionDecl
        };

        return root.ReplaceNode(functionDecl, newDecl);

        static TypeSyntax MakeTaskReturnType(TypeSyntax returnType)
        {
            if (returnType is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
                return SyntaxFactory.ParseTypeName("Task").WithTriviaFrom(returnType);
            return returnType;
        }
    }
}