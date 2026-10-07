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
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// Rewrites <c>Result.Ensure(x is not null, error)</c> to <c>Result.EnsureNotNull(x, error)</c> (TRLS066).
/// </summary>
/// <remarks>
/// <c>Result.Ensure</c> returns <c>Result&lt;Unit&gt;</c> and <c>Result.EnsureNotNull</c> returns <c>Result&lt;T&gt;</c>, so the
/// rewrite compiles and behaves the same only where the payload is provably unused. The fix is therefore offered
/// only when the call is an operand of a Trellis <c>Combine</c> chain whose result goes straight into Trellis's
/// <c>Map</c> or <c>Bind</c> with a lambda that ignores that tuple slot (a discard, or an unread <c>_</c>).
/// <para>
/// It is withheld when the rewritten call would not bind, when
/// arguments are named or the invocation contains preprocessor directives, and in every other position.
/// The enclosing pipeline must keep the same method definitions and consumer return type. Fix All rechecks this
/// after each replacement rather than merging independently validated edits.
/// </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseToResultForNullableCodeFixProvider))]
[Shared]
public sealed class UseToResultForNullableCodeFixProvider : CodeFixProvider
{
    private const string Title = "Use Result.EnsureNotNull(value, error)";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        [DiagnosticDescriptors.UseToResultForNullable.Id];

    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(static (context, document, diagnostics) =>
            ApplyFixAllAsync(document, diagnostics, context.CancellationToken));

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
            return;

        var diagnostic = context.Diagnostics.First();
        if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not InvocationExpressionSyntax invocation)
            return;

        if (BuildReplacement(invocation, model, context.CancellationToken) is null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: c => ApplyFixAsync(context.Document, invocation, c),
                equivalenceKey: Title),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null || BuildReplacement(invocation, model, cancellationToken) is not { } replacement)
            return document;

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, replacement));
    }

    private static async Task<Document?> ApplyFixAllAsync(
        Document document, ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var invocations = diagnostics
            .Select(diagnostic => root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true))
            .OfType<InvocationExpressionSyntax>()
            .OrderBy(static invocation => invocation.SpanStart)
            .ToArray();
        document = document.WithSyntaxRoot(root.TrackNodes(invocations));

        foreach (var original in invocations)
        {
            root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root?.GetCurrentNode(original) is { } invocation)
                document = await ApplyFixAsync(document, invocation, cancellationToken).ConfigureAwait(false);
        }

        return document;
    }

    private static InvocationExpressionSyntax? BuildReplacement(
        InvocationExpressionSyntax ensure, SemanticModel model, CancellationToken cancellationToken)
    {
        if (ensure.ContainsDirectives
            || ensure.ArgumentList.Arguments.Any(static a => a.NameColon is not null)
            || ensure.ArgumentList.Arguments.Count != 2)
            return null;

        if (model.GetOperation(ensure, cancellationToken) is not IInvocationOperation operation
            || !UseToResultForNullableAnalyzer.IsResultEnsureWithFlag(operation))
            return null;

        var flag = operation.Arguments.FirstOrDefault(static a => a.Parameter?.Ordinal == 0)?.Value;
        if (flag is null || UseToResultForNullableAnalyzer.NullCheckedOperand(flag)?.Syntax is not ExpressionSyntax receiver)
            return null;

        if (FindDiscardingConsumer(ensure, model, cancellationToken) is not { } consumer)
            return null;

        var arguments = ensure.ArgumentList.Arguments;
        var receiverLeading = receiver.FullSpan.Start == arguments[0].FullSpan.Start
            ? ensure.ArgumentList.OpenParenToken.TrailingTrivia.AddRange(receiver.GetLeadingTrivia())
            : receiver.GetLeadingTrivia();
        var errorLeading = arguments.GetSeparator(0).TrailingTrivia.AddRange(arguments[1].Expression.GetLeadingTrivia());
        var receiverIndentation = LeadingIndentation(receiver.GetLastToken().GetNextToken(includeZeroWidth: true));
        var receiverTrailing = TrimTrailingTrivia(receiver.GetTrailingTrivia(), receiverIndentation)
            .AddRange(PreservedConditionComments(arguments[0].Expression, receiver, receiverIndentation));
        var errorTrailing = TrimTrailingTrivia(
            arguments[1].Expression.GetTrailingTrivia(),
            LeadingIndentation(ensure.ArgumentList.CloseParenToken));
        var separator = arguments.GetSeparator(0)
            .WithLeadingTrivia(default(SyntaxTriviaList))
            .WithTrailingTrivia(SyntaxFactory.Space);
        if (receiverTrailing.Any(IsSingleLineComment))
        {
            separator = separator.WithTrailingTrivia(receiverTrailing);
            receiverTrailing = default;
        }

        var guardExpression = ensure.Expression is MemberAccessExpressionSyntax member
            ? member.WithName(SyntaxFactory.IdentifierName("EnsureNotNull"))
            : SyntaxFactory.ParseExpression("global::Trellis.Result.EnsureNotNull");
        var guard = SyntaxFactory.InvocationExpression(
                guardExpression,
                SyntaxFactory.ArgumentList(
                    ensure.ArgumentList.OpenParenToken.WithTrailingTrivia(default(SyntaxTriviaList)),
                    SyntaxFactory.SeparatedList(
                        [
                            SyntaxFactory.Argument(WithTrivia(receiver, receiverLeading, receiverTrailing)),
                            SyntaxFactory.Argument(WithTrivia(arguments[1].Expression, errorLeading, errorTrailing)),
                        ],
                        [separator]),
                    ensure.ArgumentList.CloseParenToken.WithLeadingTrivia(default(SyntaxTriviaList))));

        // The synthesized tokens carry elastic trivia that the code-action formatter would re-flow, so the outer
        // trivia is set explicitly from the call being replaced.
        guard = guard
            .WithLeadingTrivia(ensure.GetLeadingTrivia())
            .WithTrailingTrivia(ensure.GetTrailingTrivia());

        return BindsToTrellisGuard(guard, ensure, model)
            && PreservesPipelineBindings(ensure, guard, consumer, model, cancellationToken)
                ? guard
                : null;
    }

    private static ExpressionSyntax WithTrivia(
        ExpressionSyntax expression,
        SyntaxTriviaList leading,
        SyntaxTriviaList trailing) =>
        expression
            .WithLeadingTrivia(leading.SkipWhile(IsWhitespace))
            .WithTrailingTrivia(trailing);

    private static SyntaxTriviaList TrimTrailingTrivia(
        SyntaxTriviaList trivia,
        SyntaxTriviaList continuationIndentation)
    {
        var lastContent = -1;
        for (var i = 0; i < trivia.Count; i++)
        {
            if (!IsWhitespace(trivia[i]))
                lastContent = i;
        }

        if (lastContent < 0)
            return default;

        var result = SyntaxFactory.TriviaList(trivia.Take(lastContent + 1));
        if (!IsSingleLineComment(trivia[lastContent]) && !trivia[lastContent].IsDirective)
            return result;

        var endOfLine = -1;
        for (var i = lastContent + 1; i < trivia.Count; i++)
        {
            if (trivia[i].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                endOfLine = i;
                break;
            }
        }

        if (endOfLine < 0)
            return result.Add(SyntaxFactory.ElasticLineFeed).AddRange(continuationIndentation);

        result = result.AddRange(trivia.Skip(lastContent + 1).Take(endOfLine - lastContent));
        var originalIndentation = trivia
            .Skip(endOfLine + 1)
            .TakeWhile(static t => t.IsKind(SyntaxKind.WhitespaceTrivia));

        var indentation = SyntaxFactory.TriviaList(originalIndentation);
        return result.AddRange(indentation.Count > 0 ? indentation : continuationIndentation);
    }

    private static SyntaxTriviaList PreservedConditionComments(
        ExpressionSyntax condition,
        ExpressionSyntax receiver,
        SyntaxTriviaList continuationIndentation)
    {
        var allTrivia = condition.DescendantTrivia(descendIntoTrivia: true).ToList();
        var result = default(SyntaxTriviaList);

        for (var i = 0; i < allTrivia.Count; i++)
        {
            var trivia = allTrivia[i];
            if (!IsComment(trivia) || receiver.FullSpan.Contains(trivia.SpanStart))
                continue;

            if (result.Count == 0 || !IsWhitespace(result[result.Count - 1]))
                result = result.Add(SyntaxFactory.Space);

            result = result.Add(trivia);
            if (!IsSingleLineComment(trivia))
                continue;

            var endOfLine = allTrivia
                .Skip(i + 1)
                .FirstOrDefault(static t => t.IsKind(SyntaxKind.EndOfLineTrivia));
            result = result
                .Add(endOfLine.RawKind == 0 ? SyntaxFactory.ElasticLineFeed : endOfLine)
                .AddRange(continuationIndentation);
        }

        return result;
    }

    private static SyntaxTriviaList LeadingIndentation(SyntaxToken token) =>
        SyntaxFactory.TriviaList(token.LeadingTrivia
            .Reverse()
            .TakeWhile(static t => t.IsKind(SyntaxKind.WhitespaceTrivia))
            .Reverse());

    private static bool IsComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
        || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    private static bool IsSingleLineComment(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
        || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia);

    private static bool IsWhitespace(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia);

    private static bool BindsToTrellisGuard(InvocationExpressionSyntax candidate, InvocationExpressionSyntax original, SemanticModel model)
    {
        var symbol = model.GetSpeculativeSymbolInfo(original.SpanStart, candidate, SpeculativeBindingOption.BindAsExpression).Symbol;
        return symbol is IMethodSymbol { Name: "EnsureNotNull", IsExtensionMethod: false, Parameters.Length: 2 } method
            && IsTrellisMember(method, "Trellis.Result");
    }

    private static bool PreservesPipelineBindings(
        InvocationExpressionSyntax ensure,
        InvocationExpressionSyntax replacement,
        InvocationExpressionSyntax consumer,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        foreach (var invocation in ensure.Ancestors().OfType<InvocationExpressionSyntax>())
        {
            var candidate = invocation.ReplaceNode(ensure, replacement);
            if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol originalMethod
                || model.GetSpeculativeSymbolInfo(invocation.SpanStart, candidate, SpeculativeBindingOption.BindAsExpression).Symbol is not IMethodSymbol rewrittenMethod
                || !SymbolEqualityComparer.Default.Equals(
                    (originalMethod.ReducedFrom ?? originalMethod).OriginalDefinition,
                    (rewrittenMethod.ReducedFrom ?? rewrittenMethod).OriginalDefinition))
                return false;

            if (invocation == consumer)
                return SymbolEqualityComparer.Default.Equals(originalMethod.ReturnType, rewrittenMethod.ReturnType);
        }

        return false;
    }

    // Checking the namespace alone would trust an application type that merely sits in namespace Trellis.
    private static bool IsTrellisMember(IMethodSymbol method, params string[] containingTypes) =>
        method.OriginalDefinition.ContainingType?.ToDisplayString() is { } name && containingTypes.Contains(name);

    /// <summary>
    /// Finds the type-erasing consumer of a Trellis <c>Combine</c> chain whose lambda ignores the final slot containing
    /// the payload of <paramref name="ensure"/>.
    /// </summary>
    private static InvocationExpressionSyntax? FindDiscardingConsumer(InvocationExpressionSyntax ensure, SemanticModel model, CancellationToken cancellationToken)
    {
        if (!TryGetCombineAndSlot(ensure, model, cancellationToken, out var combine, out var slot))
            return null;

        while (combine.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax next } member
               && member.Expression == combine
               && AsTrellisCombine(next, model, cancellationToken) is { } nextMethod)
        {
            // A two-element result nests the entire previous tuple in its first slot.
            if (TupleArity(nextMethod.ReturnType) == 2)
                slot = 0;

            combine = next;
        }

        if (AsTrellisCombine(combine, model, cancellationToken) is not { } outermost
            || TupleArity(outermost.ReturnType) is not { } arity)
            return null;

        if (combine.Parent is not MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax consumer } consumerAccess
            || consumerAccess.Expression != combine
            || ErasingConsumerCallback(consumer, model, cancellationToken) is not { } callback)
            return null;

        var discardsSlot = callback switch
        {
            SimpleLambdaExpressionSyntax simple => IsUnusedUnderscore(simple.Parameter, simple, model),
            ParenthesizedLambdaExpressionSyntax paren =>
                paren.ParameterList.Parameters.Count == arity && IsUnusedUnderscore(paren.ParameterList.Parameters[slot], paren, model),
            _ => false,
        };
        return discardsSlot ? consumer : null;
    }

    /// <summary>
    /// Finds the callback of a Trellis <c>Map</c> or <c>Bind</c> consumer, by the delegate parameter it binds to rather than by
    /// source position. Those two return a result built from the callback, so they erase the tuple's element types. Anything
    /// else (<c>Tap</c> hands the tuple back; an application extension is unknown) is not proven safe.
    /// </summary>
    private static ExpressionSyntax? ErasingConsumerCallback(InvocationExpressionSyntax consumer, SemanticModel model, CancellationToken cancellationToken)
    {
        if (model.GetOperation(consumer, cancellationToken) is not IInvocationOperation operation
            || operation.TargetMethod is not { Name: "Map" or "Bind" } method
            || !IsTrellisMember(method, "Trellis.MapExtensions", "Trellis.BindExtensions"))
            return null;

        var callbacks = operation.Arguments.Where(static a => a.Parameter?.Type.TypeKind == TypeKind.Delegate).ToList();
        return callbacks.Count == 1 && callbacks[0].Syntax is ArgumentSyntax argument ? argument.Expression : null;
    }

    private static bool TryGetCombineAndSlot(
        InvocationExpressionSyntax ensure, SemanticModel model, CancellationToken cancellationToken,
        out InvocationExpressionSyntax combine, out int slot)
    {
        combine = ensure;
        slot = 0;

        var outer = ensure.Parent switch
        {
            ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax call } } => call,
            MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax call } member when member.Expression == ensure => call,
            _ => null,
        };

        if (outer is null
            || AsTrellisCombine(outer, model, cancellationToken) is not { } method
            || TupleArity(method.ReturnType) is not { } arity
            || model.GetOperation(outer, cancellationToken) is not IInvocationOperation operation
            || operation.Arguments.FirstOrDefault(argument => ReferencesOperand(argument, ensure))?.Parameter is not { } parameter)
            return false;

        // The slot comes from the parameter the operand binds to, not its position in the source, so named and
        // reordered arguments and the receiver of an extension call are all placed correctly.
        if (!method.IsExtensionMethod)
            slot = parameter.Ordinal;
        else if (parameter.Ordinal == method.Parameters.Length - 1)
            slot = arity - 1;
        else if (parameter.Ordinal == 0 && arity == 2)
            slot = 0;
        else
            return false;

        combine = outer;
        return true;
    }

    private static bool ReferencesOperand(IArgumentOperation argument, InvocationExpressionSyntax ensure) =>
        argument.Value.Syntax == ensure
        || argument.Syntax == ensure
        || (argument.Syntax is ArgumentSyntax syntax && syntax.Expression == ensure);

    /// <summary>
    /// Returns the Trellis <c>Combine</c> method an invocation binds to, as the unreduced method so that an extension call's
    /// receiver counts as its first parameter.
    /// </summary>
    private static IMethodSymbol? AsTrellisCombine(InvocationExpressionSyntax invocation, SemanticModel model, CancellationToken cancellationToken) =>
        model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol { Name: "Combine" } method && IsTrellisMember(method, "Trellis.CombineExtensions", "Trellis.Result")
            ? method.ReducedFrom ?? method
            : null;

    private static int? TupleArity(ITypeSymbol returnType) =>
        returnType is INamedTypeSymbol { Name: "Result", TypeArguments.Length: 1 } result
        && result.TypeArguments[0] is INamedTypeSymbol { IsTupleType: true } tuple
            ? tuple.TupleElements.Length
            : null;

    /// <summary>
    /// True for a real lambda discard, or for a parameter that merely happens to be named <c>_</c> but is never read; a lone
    /// <c>_</c> is an ordinary parameter in C# and may be referenced.
    /// </summary>
    private static bool IsUnusedUnderscore(ParameterSyntax parameter, LambdaExpressionSyntax lambda, SemanticModel model)
    {
        if (parameter.Type is not null || parameter.Identifier.ValueText != "_")
            return false;

        if (model.GetDeclaredSymbol(parameter) is not { } symbol)
            return true;

        return !lambda.Body.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(name => name.Identifier.ValueText == "_"
                         && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, symbol));
    }
}
