namespace Trellis.Analyzers;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects <c>Result&lt;T&gt;</c> deconstruction patterns where the value slot is
/// read without first gating on the success/error component. The deconstruct triplet returns
/// <c>default(T)</c> on failure, silently propagating a fake value for struct types.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeResultDeconstructionAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UnsafeResultDeconstruction];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Form: var (a, b, c) = expr;  or  (a, b, c) = expr;  -- both are SimpleAssignmentExpressions.
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        var slots = ExtractSlots(assignment.Left, context.SemanticModel);
        if (slots is null || slots.Count != 3)
            return;

        var rightType = context.SemanticModel.GetTypeInfo(assignment.Right).Type;
        if (!rightType.IsResultType())
            return;

        var (successSlot, valueSlot, errorSlot) = (slots[0], slots[1], slots[2]);

        // Only flag when the value slot binds a named local. Discard ('_') means the consumer
        // already opted out of reading the value.
        if (valueSlot.Local is not { } valueLocal)
            return;

        var enclosingBlock = assignment.FirstAncestorOrSelf<BlockSyntax>();
        if (enclosingBlock is null)
            return;

        var valueReads = enclosingBlock.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(id => id.SpanStart > assignment.Span.End && id.Identifier.Text == valueLocal.Name)
            .Where(id => SymbolEqualityComparer.Default.Equals(
                context.SemanticModel.GetSymbolInfo(id).Symbol, valueLocal))
            .Where(id => !IsInsideNameof(id))
            .Where(id => !IsWritePosition(id))
            .ToList();

        if (valueReads.Count == 0)
            return;

        foreach (var read in valueReads)
        {
            if (!IsGuardedRead(read, successSlot.Local, errorSlot.Local, context.SemanticModel))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnsafeResultDeconstruction,
                    valueSlot.Location ?? assignment.GetLocation(),
                    valueLocal.Name));
                return;
            }
        }
    }

    private static bool IsInsideNameof(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax invocation &&
                invocation.Expression is IdentifierNameSyntax id &&
                id.Identifier.Text == "nameof")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWritePosition(IdentifierNameSyntax id)
    {
        // Walk past member access / parenthesized so we find the immediate syntactic role.
        SyntaxNode candidate = id;
        while (candidate.Parent is ParenthesizedExpressionSyntax pe && pe.Expression == candidate)
            candidate = pe;

        var parent = candidate.Parent;

        // Left-hand side of an assignment.
        if (parent is AssignmentExpressionSyntax assign && assign.Left == candidate)
            return true;

        // out / ref / in argument position.
        if (parent is ArgumentSyntax arg && arg.Expression == candidate &&
            arg.RefKindKeyword.Kind() is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword)
        {
            return true;
        }

        return false;
    }

    private readonly struct Slot
    {
        public ILocalSymbol? Local { get; }
        public Location? Location { get; }
        public bool IsDiscard { get; }

        public Slot(ILocalSymbol? local, Location? location, bool isDiscard)
        {
            Local = local;
            Location = location;
            IsDiscard = isDiscard;
        }
    }

    private static List<Slot>? ExtractSlots(ExpressionSyntax left, SemanticModel model)
    {
        if (left is DeclarationExpressionSyntax decl &&
            decl.Designation is ParenthesizedVariableDesignationSyntax paren)
        {
            return paren.Variables.Select(v => SlotFromDesignation(v, model)).ToList();
        }

        if (left is TupleExpressionSyntax tuple)
        {
            return tuple.Arguments.Select(a => SlotFromExpression(a.Expression, model)).ToList();
        }

        return null;
    }

    private static Slot SlotFromDesignation(VariableDesignationSyntax designation, SemanticModel model) =>
        designation switch
        {
            DiscardDesignationSyntax discard => new Slot(null, discard.GetLocation(), true),
            SingleVariableDesignationSyntax single => new Slot(
                model.GetDeclaredSymbol(single) as ILocalSymbol,
                single.GetLocation(),
                false),
            _ => new Slot(null, designation.GetLocation(), false),
        };

    private static Slot SlotFromExpression(ExpressionSyntax expr, SemanticModel model) =>
        expr switch
        {
            DeclarationExpressionSyntax decl when decl.Designation is DiscardDesignationSyntax d
                => new Slot(null, d.GetLocation(), true),
            DeclarationExpressionSyntax decl when decl.Designation is SingleVariableDesignationSyntax s
                => new Slot(model.GetDeclaredSymbol(s) as ILocalSymbol, s.GetLocation(), false),
            IdentifierNameSyntax { Identifier.Text: "_" } id
                => new Slot(null, id.GetLocation(), true),
            IdentifierNameSyntax id
                => new Slot(model.GetSymbolInfo(id).Symbol as ILocalSymbol, id.GetLocation(), false),
            _ => new Slot(null, expr.GetLocation(), false),
        };

    private static bool IsGuardedRead(
        IdentifierNameSyntax read,
        ILocalSymbol? successLocal,
        ILocalSymbol? errorLocal,
        SemanticModel model)
    {
        SyntaxNode? current = read;
        while (current is not null)
        {
            switch (current)
            {
                case IfStatementSyntax ifStmt
                    when IsConditionGuaranteesSuccess(ifStmt.Condition, successLocal, errorLocal, model)
                      && SpanContains(ifStmt.Statement, read):
                    return true;
                case WhileStatementSyntax whileStmt
                    when IsConditionGuaranteesSuccess(whileStmt.Condition, successLocal, errorLocal, model)
                      && SpanContains(whileStmt.Statement, read):
                    return true;
                case ConditionalExpressionSyntax cond
                    when IsConditionGuaranteesSuccess(cond.Condition, successLocal, errorLocal, model)
                      && SpanContains(cond.WhenTrue, read):
                    return true;
            }

            current = current.Parent;
        }

        return HasEarlyReturnGuard(read, successLocal, errorLocal, model);
    }

    private static bool SpanContains(SyntaxNode container, SyntaxNode inner)
        => inner.SpanStart >= container.SpanStart && inner.Span.End <= container.Span.End;

    private static bool IsConditionGuaranteesSuccess(
        ExpressionSyntax condition,
        ILocalSymbol? successLocal,
        ILocalSymbol? errorLocal,
        SemanticModel model)
    {
        condition = Unparenthesize(condition);

        if (successLocal is not null && IsLocalReference(condition, successLocal, model))
            return true;

        if (errorLocal is not null && IsErrorIsNullCheck(condition, errorLocal, model))
            return true;

        if (condition is BinaryExpressionSyntax binAnd && binAnd.IsKind(SyntaxKind.LogicalAndExpression))
        {
            return IsConditionGuaranteesSuccess(binAnd.Left, successLocal, errorLocal, model)
                || IsConditionGuaranteesSuccess(binAnd.Right, successLocal, errorLocal, model);
        }

        return false;
    }

    private static bool IsErrorIsNullCheck(ExpressionSyntax condition, ILocalSymbol errorLocal, SemanticModel model)
    {
        condition = Unparenthesize(condition);

        if (condition is IsPatternExpressionSyntax isPattern &&
            isPattern.Pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax lit } &&
            lit.IsKind(SyntaxKind.NullLiteralExpression) &&
            IsLocalReference(isPattern.Expression, errorLocal, model))
            return true;

        if (condition is BinaryExpressionSyntax bin && bin.IsKind(SyntaxKind.EqualsExpression))
        {
            if (bin.Left.IsKind(SyntaxKind.NullLiteralExpression) && IsLocalReference(bin.Right, errorLocal, model))
                return true;
            if (bin.Right.IsKind(SyntaxKind.NullLiteralExpression) && IsLocalReference(bin.Left, errorLocal, model))
                return true;
        }

        return false;
    }

    private static bool HasEarlyReturnGuard(
        IdentifierNameSyntax read,
        ILocalSymbol? successLocal,
        ILocalSymbol? errorLocal,
        SemanticModel model)
    {
        var enclosingBlock = read.FirstAncestorOrSelf<BlockSyntax>();
        if (enclosingBlock is null)
            return false;

        foreach (var stmt in enclosingBlock.Statements)
        {
            if (stmt.SpanStart >= read.SpanStart)
                break;

            if (stmt is IfStatementSyntax ifStmt &&
                IsExitingStatement(ifStmt.Statement) &&
                IsConditionGuaranteesFailure(ifStmt.Condition, successLocal, errorLocal, model))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExitingStatement(StatementSyntax stmt) =>
        stmt switch
        {
            ReturnStatementSyntax => true,
            ThrowStatementSyntax => true,
            BlockSyntax block => block.Statements.Count > 0
                && block.Statements[block.Statements.Count - 1] is ReturnStatementSyntax or ThrowStatementSyntax,
            _ => false,
        };

    private static bool IsConditionGuaranteesFailure(
        ExpressionSyntax condition,
        ILocalSymbol? successLocal,
        ILocalSymbol? errorLocal,
        SemanticModel model)
    {
        condition = Unparenthesize(condition);

        if (successLocal is not null && condition is PrefixUnaryExpressionSyntax neg &&
            neg.IsKind(SyntaxKind.LogicalNotExpression) &&
            IsLocalReference(neg.Operand, successLocal, model))
            return true;

        if (successLocal is not null && condition is BinaryExpressionSyntax binEq &&
            binEq.IsKind(SyntaxKind.EqualsExpression))
        {
            if (binEq.Left.IsKind(SyntaxKind.FalseLiteralExpression) && IsLocalReference(binEq.Right, successLocal, model)) return true;
            if (binEq.Right.IsKind(SyntaxKind.FalseLiteralExpression) && IsLocalReference(binEq.Left, successLocal, model)) return true;
        }

        if (errorLocal is not null)
        {
            if (condition is BinaryExpressionSyntax binNe && binNe.IsKind(SyntaxKind.NotEqualsExpression))
            {
                if (binNe.Left.IsKind(SyntaxKind.NullLiteralExpression) && IsLocalReference(binNe.Right, errorLocal, model)) return true;
                if (binNe.Right.IsKind(SyntaxKind.NullLiteralExpression) && IsLocalReference(binNe.Left, errorLocal, model)) return true;
            }

            if (condition is IsPatternExpressionSyntax isPattern && IsLocalReference(isPattern.Expression, errorLocal, model))
            {
                if (isPattern.Pattern is RecursivePatternSyntax)
                    return true;

                if (isPattern.Pattern is UnaryPatternSyntax unary &&
                    unary.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                    unary.Pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax lit } &&
                    lit.IsKind(SyntaxKind.NullLiteralExpression))
                    return true;
            }
        }

        return false;
    }

    private static bool IsLocalReference(ExpressionSyntax expr, ILocalSymbol target, SemanticModel model)
    {
        expr = Unparenthesize(expr);
        var sym = model.GetSymbolInfo(expr).Symbol;
        return sym is ILocalSymbol local && SymbolEqualityComparer.Default.Equals(local, target);
    }

    private static ExpressionSyntax Unparenthesize(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax paren)
            expr = paren.Expression;
        return expr;
    }
}
