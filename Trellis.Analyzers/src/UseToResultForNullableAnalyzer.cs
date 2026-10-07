namespace Trellis.Analyzers;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// TRLS066: Suggests <c>Result.EnsureNotNull(value, error)</c> where <c>Result.Ensure(value is not null, error)</c>
/// guards a nullable reference or <see cref="System.Nullable{T}"/> value.
/// <para>
/// The two are not interchangeable in type: <c>Result.Ensure</c> yields <c>Result&lt;Unit&gt;</c> and
/// <c>Result.EnsureNotNull</c> yields <c>Result&lt;T&gt;</c> carrying the non-null value. Reporting is therefore separate from
/// fixing: the rule reports every pure null test over a nullable value, and
/// <see cref="UseToResultForNullableCodeFixProvider"/> offers a rewrite only where it can prove the payload is discarded.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseToResultForNullableAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UseToResultForNullable];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (!IsResultEnsureWithFlag(invocation))
            return;

        if (invocation.Syntax is not InvocationExpressionSyntax syntax)
            return;

        var flag = invocation.Arguments.FirstOrDefault(static a => a.Parameter?.Ordinal == 0)?.Value;
        if (flag is null || NullCheckedOperand(flag) is not { } checkedOperand)
            return;

        if (!IsNullableOrReference(checkedOperand))
            return;

        var error = invocation.Arguments.FirstOrDefault(static a => a.Parameter?.Ordinal == 1)?.Value;
        if (error is not null && IsConditionalError(error))
            return;

        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UseToResultForNullable, syntax.GetLocation()));
    }

    internal static bool IsResultEnsureWithFlag(IInvocationOperation invocation) =>
        invocation.TargetMethod is
        {
            Name: "Ensure",
            IsExtensionMethod: false,
            Parameters.Length: 2,
            ContainingType: { Name: "Result", TypeArguments.Length: 0 } containingType,
        }
        && containingType.ContainingNamespace?.ToDisplayString() == "Trellis"
        && invocation.TargetMethod.Parameters[0].Type.SpecialType == SpecialType.System_Boolean;

    /// <summary>
    /// Returns the operand of a pure null test (<c>x is not null</c>, <c>x != null</c>, <c>x is { }</c>),
    /// or <see langword="null"/> when the condition tests anything else.
    /// </summary>
    internal static IOperation? NullCheckedOperand(IOperation condition)
    {
        condition = Unwrap(condition);

        switch (condition)
        {
            case IIsPatternOperation { Pattern: var pattern } isPattern when IsNotNullPattern(pattern):
                return isPattern.Value;

            case IBinaryOperation { OperatorKind: BinaryOperatorKind.NotEquals } binary when IsIntrinsicNullComparison(binary):
                if (IsNullConstant(binary.RightOperand))
                    return NullComparisonOperand(binary.LeftOperand);
                if (IsNullConstant(binary.LeftOperand))
                    return NullComparisonOperand(binary.RightOperand);
                return null;

            default:
                return null;
        }
    }

    // A user-defined != on a reference type can carry its own sentinel semantics or side effects, which the reference-null check in
    // EnsureNotNull would drop. string's operator is null-safe, and a lifted operator on Nullable<T> compares against null by HasValue.
    private static bool IsIntrinsicNullComparison(IBinaryOperation binary) =>
        binary.OperatorMethod is null
        || binary.IsLifted
        || binary.OperatorMethod.ContainingType.SpecialType == SpecialType.System_String;

    private static bool IsNotNullPattern(IPatternOperation pattern) => pattern switch
    {
        INegatedPatternOperation { Pattern: IConstantPatternOperation constant } => IsNullConstant(constant.Value),
        IRecursivePatternOperation
        {
            Syntax: RecursivePatternSyntax { Type: null, PositionalPatternClause: null },
            DeclaredSymbol: null,
            DeconstructionSubpatterns.IsEmpty: true,
            PropertySubpatterns.IsEmpty: true,
        } => true,
        _ => false,
    };

    private static bool IsNullConstant(IOperation operation) =>
        Unwrap(operation) is { ConstantValue: { HasValue: true, Value: null } };

    private static IOperation? NullComparisonOperand(IOperation operation)
    {
        operation = Unwrap(operation);
        return operation is IConversionOperation { IsImplicit: true } ? null : operation;
    }

    private static bool IsConditionalError(IOperation operation) =>
        Unwrap(operation, unwrapAllConversions: true) is IConditionalOperation
            or ISwitchExpressionOperation
            or ICoalesceOperation;

    private static IOperation Unwrap(IOperation operation, bool unwrapAllConversions = false)
    {
        while (true)
        {
            switch (operation)
            {
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                case IConversionOperation conversion when unwrapAllConversions
                    || (conversion.IsImplicit && !conversion.Conversion.IsUserDefined):
                    operation = conversion.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    // Reference types are judged by their declared annotation: IOperation.Type carries none, so it is read from the semantic model.
    // Flow state is no help here, because the compiler treats the operand of a null test as maybe-null by definition.
    private static bool IsNullableOrReference(IOperation operand)
    {
        var type = operand.Type;
        if (type is null)
            return false;

        if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            return true;

        return type.IsReferenceType
            && operand.SemanticModel?.GetTypeInfo(operand.Syntax).Nullability.Annotation == NullableAnnotation.Annotated;
    }
}
