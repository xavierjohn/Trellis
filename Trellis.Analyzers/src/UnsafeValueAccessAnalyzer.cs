namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects unsafe access to <c>Maybe&lt;T&gt;.Value</c> without proper
/// presence checks. The corresponding rules for <c>Result&lt;T&gt;.Value</c>
/// and <c>Result&lt;T&gt;.Error</c> were removed from the current API: <c>Value</c> no longer
/// exists on <c>Result&lt;T&gt;</c>, and <c>Error</c> is now nullable so NRT handles
/// unsafe access at the language level.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeValueAccessAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UnsafeMaybeValueAccess];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        if (memberAccess.Name.Identifier.Text != "Value")
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
        var type = typeInfo.Type;

        if (type is null || !type.IsMaybeType())
            return;

        if (IsGuardedByHasValueCheck(memberAccess, context.SemanticModel))
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.UnsafeMaybeValueAccess,
            memberAccess.Name.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsGuardedByHasValueCheck(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel) =>
        IsGuardedByCheck(memberAccess, semanticModel, "HasValue", true) ||
        IsGuardedByCheck(memberAccess, semanticModel, "HasNoValue", false) ||
        IsGuardedByShortCircuitAnd(memberAccess, semanticModel) ||
        IsGuardedByPriorAssignment(memberAccess, semanticModel) ||
        IsGuardedByEarlyReturn(memberAccess, semanticModel) ||
        IsInsideTryGetValueBlock(memberAccess, semanticModel, "TryGetValue") ||
        IsInsideTrackSafeLambda(memberAccess, semanticModel);

    private static bool IsGuardedByCheck(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        string checkPropertyName,
        bool expectedValue)
    {
        var current = memberAccess.Parent;
        while (current != null)
        {
            if (current is IfStatementSyntax ifStatement)
            {
                if (ifStatement.Condition is PrefixUnaryExpressionSyntax { Operand: MemberAccessExpressionSyntax negatedMemberAccess } prefixUnary &&
                    prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                    negatedMemberAccess.Name.Identifier.Text == checkPropertyName &&
                    AreSameVariable(negatedMemberAccess.Expression, memberAccess.Expression, semanticModel))
                {
                    if (!expectedValue && IsInThenBranch(memberAccess, ifStatement))
                        return true;
                    if (expectedValue && IsInElseBranch(memberAccess, ifStatement))
                        return true;
                }

                if (ifStatement.Condition is MemberAccessExpressionSyntax conditionMemberAccess &&
                    conditionMemberAccess.Name.Identifier.Text == checkPropertyName &&
                    AreSameVariable(conditionMemberAccess.Expression, memberAccess.Expression, semanticModel))
                {
                    if (expectedValue && IsInThenBranch(memberAccess, ifStatement))
                        return true;
                    if (!expectedValue && IsInElseBranch(memberAccess, ifStatement))
                        return true;
                }

                if (IsEqualityCheckingProperty(ifStatement.Condition, memberAccess.Expression, checkPropertyName, expectedValue, semanticModel, out var matchesThenBranch))
                {
                    if (matchesThenBranch && IsInThenBranch(memberAccess, ifStatement))
                        return true;
                    if (!matchesThenBranch && IsInElseBranch(memberAccess, ifStatement))
                        return true;
                }
            }

            if (current is ConditionalExpressionSyntax conditionalExpression)
            {
                if (conditionalExpression.Condition is PrefixUnaryExpressionSyntax { Operand: MemberAccessExpressionSyntax negatedTernaryMemberAccess } ternaryPrefixUnary &&
                    ternaryPrefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                    negatedTernaryMemberAccess.Name.Identifier.Text == checkPropertyName &&
                    AreSameVariable(negatedTernaryMemberAccess.Expression, memberAccess.Expression, semanticModel))
                {
                    if (!expectedValue && IsInWhenTrueBranch(memberAccess, conditionalExpression))
                        return true;
                    if (expectedValue && IsInWhenFalseBranch(memberAccess, conditionalExpression))
                        return true;
                }

                if (conditionalExpression.Condition is MemberAccessExpressionSyntax ternaryConditionMemberAccess &&
                    ternaryConditionMemberAccess.Name.Identifier.Text == checkPropertyName &&
                    AreSameVariable(ternaryConditionMemberAccess.Expression, memberAccess.Expression, semanticModel))
                {
                    if (expectedValue && IsInWhenTrueBranch(memberAccess, conditionalExpression))
                        return true;
                    if (!expectedValue && IsInWhenFalseBranch(memberAccess, conditionalExpression))
                        return true;
                }

                if (IsEqualityCheckingProperty(conditionalExpression.Condition, memberAccess.Expression, checkPropertyName, expectedValue, semanticModel, out var ternaryMatchesTrueBranch))
                {
                    if (ternaryMatchesTrueBranch && IsInWhenTrueBranch(memberAccess, conditionalExpression))
                        return true;
                    if (!ternaryMatchesTrueBranch && IsInWhenFalseBranch(memberAccess, conditionalExpression))
                        return true;
                }
            }

            // Conditional access ?.Value pattern is safe.
            if (current is ConditionalAccessExpressionSyntax)
                return true;

            current = current.Parent;
        }

        return false;
    }

    private static bool IsEqualityCheckingProperty(
        ExpressionSyntax condition,
        ExpressionSyntax targetExpression,
        string propertyName,
        bool expectedValue,
        SemanticModel semanticModel,
        out bool matchesThenBranch)
    {
        matchesThenBranch = false;

        if (condition is not BinaryExpressionSyntax binaryExpression)
            return false;

        if (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
            !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression))
            return false;

        var left = binaryExpression.Left;
        var right = binaryExpression.Right;

        if (left is MemberAccessExpressionSyntax leftMemberAccess &&
            leftMemberAccess.Name.Identifier.Text == propertyName &&
            right is LiteralExpressionSyntax literal &&
            AreSameVariable(leftMemberAccess.Expression, targetExpression, semanticModel))
        {
            var literalValue = literal.IsKind(SyntaxKind.TrueLiteralExpression);
            var isEquals = binaryExpression.IsKind(SyntaxKind.EqualsExpression);
            var propertyValueInThenBranch = isEquals ? literalValue : !literalValue;

            matchesThenBranch = propertyValueInThenBranch == expectedValue;
            return true;
        }

        if (right is MemberAccessExpressionSyntax rightMemberAccess &&
            rightMemberAccess.Name.Identifier.Text == propertyName &&
            left is LiteralExpressionSyntax literalLeft &&
            AreSameVariable(rightMemberAccess.Expression, targetExpression, semanticModel))
        {
            var literalValue = literalLeft.IsKind(SyntaxKind.TrueLiteralExpression);
            var isEquals = binaryExpression.IsKind(SyntaxKind.EqualsExpression);
            var propertyValueInThenBranch = isEquals ? literalValue : !literalValue;

            matchesThenBranch = propertyValueInThenBranch == expectedValue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if two syntax expressions refer to the same logical
    /// receiver. Only stable receiver forms are considered comparable: identifiers,
    /// member-access chains, <c>this</c>, and <c>base</c>. For instance members accessed
    /// through a chain of receivers, comparison is structural — each chain segment must match
    /// in both the terminal symbol and the recursive receiver. Implicit and explicit
    /// <c>this</c> are treated as equivalent (an unqualified instance-member access and the
    /// same member explicitly qualified with <c>this.</c> name the same thing). Static
    /// members and locals/parameters compare by symbol identity alone. Any other receiver
    /// shape (invocation, element access, conditional access, cast, etc.) is conservatively
    /// rejected because it cannot be structurally compared without evaluating runtime state.
    /// </summary>
    private static bool AreSameVariable(ExpressionSyntax expr1, ExpressionSyntax expr2, SemanticModel semanticModel)
    {
        while (expr1 is ParenthesizedExpressionSyntax p1)
            expr1 = p1.Expression;
        while (expr2 is ParenthesizedExpressionSyntax p2)
            expr2 = p2.Expression;

        // Reject any receiver shape we cannot structurally compare.
        if (!IsStableReceiverShape(expr1) || !IsStableReceiverShape(expr2))
            return false;

        var symbol1 = semanticModel.GetSymbolInfo(expr1).Symbol;
        var symbol2 = semanticModel.GetSymbolInfo(expr2).Symbol;

        if (symbol1 == null || symbol2 == null)
            return false;

        if (!SymbolEqualityComparer.Default.Equals(symbol1, symbol2))
            return false;

        // Static members, locals, parameters, type names — symbol identity is sufficient.
        // The receivers (if any) cannot disambiguate them further.
        if (symbol1.IsStatic ||
            symbol1 is ILocalSymbol or IParameterSymbol or ITypeSymbol or INamespaceSymbol)
        {
            return true;
        }

        // Instance member: the same symbol on different receivers refers to different state.
        // Walk the receiver chains, treating implicit `this` (no receiver) and explicit
        // `this`/`base` as equivalent.
        var receiver1 = expr1 is MemberAccessExpressionSyntax m1 ? m1.Expression : null;
        var receiver2 = expr2 is MemberAccessExpressionSyntax m2 ? m2.Expression : null;

        var isThis1 = receiver1 is null or ThisExpressionSyntax or BaseExpressionSyntax;
        var isThis2 = receiver2 is null or ThisExpressionSyntax or BaseExpressionSyntax;

        if (isThis1 && isThis2)
            return true;

        if (isThis1 != isThis2)
            return false;

        return AreSameVariable(receiver1!, receiver2!, semanticModel);
    }

    private static bool IsStableReceiverShape(ExpressionSyntax expr) =>
        expr is IdentifierNameSyntax
            or MemberAccessExpressionSyntax
            or ThisExpressionSyntax
            or BaseExpressionSyntax;

    private static bool IsInThenBranch(SyntaxNode node, IfStatementSyntax ifStatement) =>
        ifStatement.Statement.Contains(node);

    private static bool IsInElseBranch(SyntaxNode node, IfStatementSyntax ifStatement) =>
        ifStatement.Else?.Statement.Contains(node) ?? false;

    private static bool IsInWhenTrueBranch(SyntaxNode node, ConditionalExpressionSyntax conditionalExpression) =>
        conditionalExpression.WhenTrue.Contains(node);

    private static bool IsInWhenFalseBranch(SyntaxNode node, ConditionalExpressionSyntax conditionalExpression) =>
        conditionalExpression.WhenFalse.Contains(node);

    private static bool IsInsideTryGetValueBlock(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel, string tryMethodName)
    {
        var current = memberAccess.Parent;
        while (current != null)
        {
            if (current is IfStatementSyntax ifStatement)
            {
                if (ifStatement.Condition is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax methodAccess } &&
                    methodAccess.Name.Identifier.Text == tryMethodName &&
                    AreSameVariable(methodAccess.Expression, memberAccess.Expression, semanticModel))
                {
                    return IsInThenBranch(memberAccess, ifStatement);
                }

                if (ifStatement.Condition is PrefixUnaryExpressionSyntax { Operand: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax negatedMethodAccess } } prefixUnary &&
                    prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                    negatedMethodAccess.Name.Identifier.Text == tryMethodName &&
                    AreSameVariable(negatedMethodAccess.Expression, memberAccess.Expression, semanticModel))
                {
                    return IsInElseBranch(memberAccess, ifStatement);
                }
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool IsInsideTrackSafeLambda(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel)
    {
        if (memberAccess.FirstAncestorOrSelf<LambdaExpressionSyntax>() is not { Parent: ArgumentSyntax argument } ||
            argument.Parent?.Parent is not InvocationExpressionSyntax invocation ||
            invocation.Expression is not MemberAccessExpressionSyntax methodAccess ||
            !AreSameVariable(methodAccess.Expression, memberAccess.Expression, semanticModel))
            return false;

        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol ||
            !methodSymbol.IsTrellisExtensionMethod())
            return false;

        var parameter = GetArgumentParameter(methodSymbol, argument);
        if (parameter == null)
            return false;

        return IsSafeLambdaParameter(methodSymbol.Name, parameter.Name);
    }

    private static IParameterSymbol? GetArgumentParameter(IMethodSymbol methodSymbol, ArgumentSyntax argument)
    {
        if (argument.NameColon is { } nameColon)
            return methodSymbol.Parameters.FirstOrDefault(parameter => parameter.Name == nameColon.Name.Identifier.Text);

        if (argument.Parent is not BaseArgumentListSyntax argumentList)
            return null;

        var argumentIndex = argumentList.Arguments.IndexOf(argument);
        return argumentIndex >= 0 && argumentIndex < methodSymbol.Parameters.Length
            ? methodSymbol.Parameters[argumentIndex]
            : null;
    }

    /// <summary>
    /// Lambda parameters whose body is only invoked on the present-value branch of a
    /// <c>Maybe&lt;T&gt;</c> chain. Inside such bodies, accessing <c>.Value</c> on the
    /// receiver is safe because the API itself has already discriminated.
    /// </summary>
    private static bool IsSafeLambdaParameter(string methodName, string parameterName) =>
        methodName switch
        {
            "Bind" or "BindAsync" or "Map" or "MapAsync" or "Tap" or "TapAsync" or "Ensure" or "EnsureAsync" => true,
            "Match" or "MatchAsync" or "Switch" or "SwitchAsync" => parameterName is "onSome",
            _ => false,
        };

    /// <summary>
    /// Recognizes: x = Maybe&lt;T&gt;.From(...); followed by x.Value in the same block.
    /// Only suppresses when T is a non-nullable value type (where From() can never return None).
    /// </summary>
    private static bool IsGuardedByPriorAssignment(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel)
    {
        var containingStatement = memberAccess.FirstAncestorOrSelf<StatementSyntax>();
        if (containingStatement?.Parent is not BlockSyntax block)
            return false;

        var memberAccessIndex = block.Statements.IndexOf(containingStatement);
        if (memberAccessIndex < 0)
            return false;

        for (var i = memberAccessIndex - 1; i >= 0; i--)
        {
            if (block.Statements[i] is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
                continue;

            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
                continue;

            if (!AreSameVariable(assignment.Left, memberAccess.Expression, semanticModel))
                continue;

            if (assignment.Right is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax methodAccess } ||
                methodAccess.Name.Identifier.Text != "From")
                return false;

            if (semanticModel.GetSymbolInfo(assignment.Right).Symbol is not IMethodSymbol methodSymbol)
                return false;

            var containingType = methodSymbol.ContainingType;
            if (containingType?.Name is not "Maybe" ||
                containingType.ContainingNamespace?.ToDisplayString() is not "Trellis")
                return false;

            var maybeType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
            if (maybeType is not INamedTypeSymbol { TypeArguments.Length: 1 } namedType)
                return false;

            var innerType = namedType.TypeArguments[0];
            if (innerType.IsValueType && innerType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
                && !HasReassignmentBetween(block, i + 1, memberAccessIndex, memberAccess.Expression, semanticModel))
                return true;
        }

        return false;
    }

    private static bool HasReassignmentBetween(
        BlockSyntax block,
        int startExclusive,
        int endExclusive,
        ExpressionSyntax targetExpression,
        SemanticModel semanticModel)
    {
        for (var j = startExclusive; j < endExclusive; j++)
        {
            if (block.Statements[j] is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                AreSameVariable(assignment.Left, targetExpression, semanticModel))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Recognizes <c>x.HasValue &amp;&amp; ... &amp;&amp; x.Value</c> in a left-to-right
    /// short-circuit chain. Common in expression trees (specifications) and any multi-clause
    /// boolean filter.
    /// </summary>
    /// <remarks>
    /// C# left-associates <c>a &amp;&amp; b &amp;&amp; c</c> as <c>(a &amp;&amp; b) &amp;&amp; c</c>,
    /// so the immediate left operand of the outermost <c>&amp;&amp;</c> is itself a binary
    /// expression for any 3+ clause shape. To recognize the guard, recurse through nested
    /// <c>&amp;&amp;</c> operators on the left side looking for a matching <c>HasValue</c>
    /// access on the same receiver. <c>||</c>, <c>!</c>, ternary, and other operators stop the
    /// recursion because they break the short-circuit guarantee.
    /// </remarks>
    private static bool IsGuardedByShortCircuitAnd(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel)
    {
        var current = memberAccess.Parent;
        while (current != null)
        {
            if (current is BinaryExpressionSyntax binaryExpression &&
                binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                binaryExpression.Right.Contains(memberAccess) &&
                ContainsHasValueGuard(binaryExpression.Left, memberAccess.Expression, semanticModel))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="expr"/> is, or contains within a
    /// connected <c>&amp;&amp;</c> subtree (with parentheses transparent), a <c>HasValue</c>
    /// member access on the same receiver as <paramref name="targetReceiver"/>. Recursion stops
    /// at non-<c>&amp;&amp;</c> boundaries so <c>||</c>, <c>!</c>, ternary, and other operators
    /// do not falsely satisfy the guard.
    /// </summary>
    private static bool ContainsHasValueGuard(
        ExpressionSyntax expr,
        ExpressionSyntax targetReceiver,
        SemanticModel semanticModel)
    {
        // Parentheses are transparent for short-circuit semantics.
        while (expr is ParenthesizedExpressionSyntax paren)
            expr = paren.Expression;

        if (expr is MemberAccessExpressionSyntax member &&
            member.Name.Identifier.Text == "HasValue" &&
            AreSameVariable(member.Expression, targetReceiver, semanticModel))
        {
            return true;
        }

        if (expr is BinaryExpressionSyntax binExpr &&
            binExpr.IsKind(SyntaxKind.LogicalAndExpression))
        {
            return ContainsHasValueGuard(binExpr.Left, targetReceiver, semanticModel)
                || ContainsHasValueGuard(binExpr.Right, targetReceiver, semanticModel);
        }

        return false;
    }

    /// <summary>
    /// Recognizes the guard-clause / early-exit pattern where a preceding sibling statement
    /// asserts the <c>Maybe&lt;T&gt;</c> is empty and unconditionally exits the flow that would
    /// otherwise reach <paramref name="memberAccess"/>:
    /// <code>
    /// if (!m.HasValue) return ...;   // or m.HasNoValue / m.HasValue == false; return / throw / break / continue
    /// ... m.Value ...                // safe in any subsequent sibling statement
    /// </code>
    /// The guard must have no <c>else</c> branch, its then-branch must unconditionally exit, and
    /// the same receiver must not be reassigned anywhere on the path between the guard and the
    /// access. The scan walks each enclosing block so the access may sit in a nested block or loop
    /// body, but it stops at the boundary of the executable body that contains the access: a guard
    /// in an enclosing method/function does not protect a nested local function or lambda body,
    /// which may be invoked before the guard runs.
    /// </summary>
    private static bool IsGuardedByEarlyReturn(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel)
    {
        var receiver = memberAccess.Expression;
        SyntaxNode? node = memberAccess;

        while (node != null)
        {
            // Do not let a guard from an enclosing body reach into a nested local function or
            // lambda: once the walk reaches the access's own function boundary, stop. Guards inside
            // that body have already been scanned in earlier iterations.
            if (IsFunctionBoundary(node))
                break;

            if (node is StatementSyntax statement && statement.Parent is BlockSyntax block)
            {
                var statementIndex = block.Statements.IndexOf(statement);
                for (var i = statementIndex - 1; i >= 0; i--)
                {
                    if (block.Statements[i] is IfStatementSyntax { Else: null } guard &&
                        ConditionAssertsEmpty(guard.Condition, receiver, semanticModel) &&
                        StatementUnconditionallyExits(guard.Statement) &&
                        !IsReceiverReassignedBeforeAccess(block.Statements[i], memberAccess, receiver, semanticModel))
                    {
                        return true;
                    }
                }
            }

            node = node.Parent;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="receiver"/> may be reassigned between
    /// <paramref name="guardStatement"/> and <paramref name="memberAccess"/>, which would defeat the
    /// guard. This is intentionally a conservative over-approximation (firing on an Error-severity
    /// safety rule is preferable to missing a real throw):
    /// <list type="bullet">
    /// <item>any write to the receiver (or a prefix of its member-access chain) evaluated after the
    /// guard and before the access — found by scanning descendant writes in the guard's block whose
    /// span sits between the two, so reassignments nested in blocks or embedded in expressions
    /// (<c>Consume(m = other)</c>) are caught;</item>
    /// <item>any write to the receiver anywhere inside a loop that encloses the access but not the
    /// guard — a loop back-edge re-enters the body, so a later reassignment is visible on the next
    /// iteration's access.</item>
    /// </list>
    /// A "write" is a simple/compound assignment, a tuple-deconstruction target, or a
    /// <c>ref</c>/<c>out</c> argument. Writes inside nested local functions / lambdas are ignored
    /// because they are not evaluated inline. Known accepted limitations (consistent with the
    /// analyzer's other syntactic guards): a mutation performed by an invoked local function / lambda
    /// and control flow via <c>goto</c> are not tracked.
    /// </summary>
    private static bool IsReceiverReassignedBeforeAccess(
        StatementSyntax guardStatement,
        MemberAccessExpressionSyntax memberAccess,
        ExpressionSyntax receiver,
        SemanticModel semanticModel)
    {
        if (guardStatement.Parent is not BlockSyntax guardBlock)
            return false;

        var guardEnd = guardStatement.Span.End;
        var accessStart = memberAccess.SpanStart;

        foreach (var descendant in guardBlock.DescendantNodes(descendIntoChildren: n => !IsFunctionBoundary(n)))
        {
            if (descendant.SpanStart >= guardEnd &&
                descendant.SpanStart < accessStart &&
                WritesReceiver(descendant, receiver, semanticModel))
                return true;
        }

        for (SyntaxNode? node = memberAccess; node is not null && node != guardBlock; node = node.Parent)
        {
            if (node is ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax
                or WhileStatementSyntax or DoStatementSyntax)
            {
                foreach (var descendant in node.DescendantNodes(descendIntoChildren: n => !IsFunctionBoundary(n)))
                {
                    if (WritesReceiver(descendant, receiver, semanticModel))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="node"/> writes to
    /// <paramref name="receiver"/> (or a prefix of its member-access chain): a simple/compound
    /// assignment, a tuple-deconstruction element, or a <c>ref</c>/<c>out</c> argument.
    /// </summary>
    private static bool WritesReceiver(SyntaxNode node, ExpressionSyntax receiver, SemanticModel semanticModel) =>
        node switch
        {
            AssignmentExpressionSyntax assignment => WriteTargetMatches(assignment.Left, receiver, semanticModel),
            ArgumentSyntax argument when argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                                         argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) =>
                WriteTargetMatches(argument.Expression, receiver, semanticModel),
            _ => false,
        };

    /// <summary>
    /// Returns <see langword="true"/> when assigning <paramref name="target"/> changes the value seen
    /// through <paramref name="receiver"/>: an exact match, a match against a prefix of the receiver's
    /// member-access chain (assigning <c>holder</c> defeats a guard on <c>holder.Maybe</c>), or any
    /// element of a tuple-deconstruction target.
    /// </summary>
    private static bool WriteTargetMatches(ExpressionSyntax target, ExpressionSyntax receiver, SemanticModel semanticModel)
    {
        if (target is TupleExpressionSyntax tuple)
        {
            foreach (var element in tuple.Arguments)
            {
                if (WriteTargetMatches(element.Expression, receiver, semanticModel))
                    return true;
            }

            return false;
        }

        for (ExpressionSyntax? prefix = receiver; prefix is not null;)
        {
            while (prefix is ParenthesizedExpressionSyntax parenthesized)
                prefix = parenthesized.Expression;

            if (AreSameVariable(target, prefix, semanticModel))
                return true;

            prefix = (prefix as MemberAccessExpressionSyntax)?.Expression;
        }

        return false;
    }

    private static bool IsFunctionBoundary(SyntaxNode node) =>
        node is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax;

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="condition"/> is true exactly when the
    /// <paramref name="receiver"/>'s <c>Maybe&lt;T&gt;</c> is empty: <c>!receiver.HasValue</c>,
    /// <c>receiver.HasNoValue</c>, <c>receiver.HasValue == false</c>, or <c>receiver.HasValue != true</c>
    /// (operands order-independent, parentheses transparent).
    /// </summary>
    private static bool ConditionAssertsEmpty(
        ExpressionSyntax condition,
        ExpressionSyntax receiver,
        SemanticModel semanticModel)
    {
        while (condition is ParenthesizedExpressionSyntax parenthesized)
            condition = parenthesized.Expression;

        if (condition is PrefixUnaryExpressionSyntax prefixUnary &&
            prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            var operand = prefixUnary.Operand;
            while (operand is ParenthesizedExpressionSyntax innerParenthesized)
                operand = innerParenthesized.Expression;

            if (operand is MemberAccessExpressionSyntax negatedAccess &&
                negatedAccess.Name.Identifier.Text == "HasValue" &&
                AreSameVariable(negatedAccess.Expression, receiver, semanticModel))
                return true;
        }

        if (condition is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "HasNoValue" &&
            AreSameVariable(memberAccess.Expression, receiver, semanticModel))
            return true;

        if (condition is BinaryExpressionSyntax binaryExpression &&
            (binaryExpression.IsKind(SyntaxKind.EqualsExpression) || binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)) &&
            TryGetHasValueLiteralComparison(binaryExpression, receiver, semanticModel, out var assertsEmpty))
            return assertsEmpty;

        return false;
    }

    /// <summary>
    /// Matches a <c>receiver.HasValue</c> vs boolean-literal equality/inequality and reports via
    /// <paramref name="assertsEmpty"/> whether the comparison is true exactly when the value is absent.
    /// </summary>
    private static bool TryGetHasValueLiteralComparison(
        BinaryExpressionSyntax binaryExpression,
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        out bool assertsEmpty)
    {
        assertsEmpty = false;

        MemberAccessExpressionSyntax? hasValueAccess = null;
        LiteralExpressionSyntax? literal = null;

        if (binaryExpression.Left is MemberAccessExpressionSyntax leftAccess &&
            leftAccess.Name.Identifier.Text == "HasValue" &&
            binaryExpression.Right is LiteralExpressionSyntax rightLiteral)
        {
            hasValueAccess = leftAccess;
            literal = rightLiteral;
        }
        else if (binaryExpression.Right is MemberAccessExpressionSyntax rightAccess &&
            rightAccess.Name.Identifier.Text == "HasValue" &&
            binaryExpression.Left is LiteralExpressionSyntax leftLiteral)
        {
            hasValueAccess = rightAccess;
            literal = leftLiteral;
        }

        if (hasValueAccess is null || literal is null)
            return false;

        if (!literal.IsKind(SyntaxKind.TrueLiteralExpression) && !literal.IsKind(SyntaxKind.FalseLiteralExpression))
            return false;

        if (!AreSameVariable(hasValueAccess.Expression, receiver, semanticModel))
            return false;

        var literalIsTrue = literal.IsKind(SyntaxKind.TrueLiteralExpression);
        var isEquals = binaryExpression.IsKind(SyntaxKind.EqualsExpression);

        // `HasValue == false` and `HasValue != true` are both true exactly when the value is absent.
        assertsEmpty = isEquals ? !literalIsTrue : literalIsTrue;
        return true;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="statement"/> cannot complete normally —
    /// it is a <c>return</c>, <c>throw</c>, <c>break</c>, or <c>continue</c> (or a block whose final
    /// statement is one of these). Such a guard body guarantees that any following sibling statement
    /// is reached only when the guard condition was false.
    /// </summary>
    private static bool StatementUnconditionallyExits(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
            return block.Statements.Count > 0 && StatementUnconditionallyExits(block.Statements[block.Statements.Count - 1]);

        return statement is ReturnStatementSyntax
            or ThrowStatementSyntax
            or BreakStatementSyntax
            or ContinueStatementSyntax;
    }
}