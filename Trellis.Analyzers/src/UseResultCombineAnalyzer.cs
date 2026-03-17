namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// Analyzer that detects manual combination of multiple Result.IsSuccess checks
/// and suggests using Result.Combine() or .Combine() chaining instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseResultCombineAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.UseResultCombine];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeConditional, OperationKind.Conditional);
    }

    private static void AnalyzeConditional(OperationAnalysisContext context)
    {
        var conditional = (IConditionalOperation)context.Operation;

        if (conditional.Condition is not IBinaryOperation binaryOp)
            return;

        string propertyName;
        switch (binaryOp.OperatorKind)
        {
            case BinaryOperatorKind.ConditionalAnd:
                propertyName = "IsSuccess";
                break;
            case BinaryOperatorKind.ConditionalOr:
                propertyName = "IsFailure";
                break;
            default:
                return;
        }

        var resultCheckCount = CountResultChecks(binaryOp, propertyName);

        if (resultCheckCount < 2)
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.UseResultCombine,
            conditional.Syntax.GetLocation());

        context.ReportDiagnostic(diagnostic);
    }

    private static int CountResultChecks(IOperation operation, string propertyName)
    {
        var count = 0;

        if (operation is IBinaryOperation binaryOp)
        {
            // Recursively count in left and right operands
            count += CountResultChecks(binaryOp.LeftOperand, propertyName);
            count += CountResultChecks(binaryOp.RightOperand, propertyName);
        }
        else if (operation is IPropertyReferenceOperation propertyRef)
        {
            if (propertyRef.Property.Name == propertyName &&
                propertyRef.Property.ContainingType.IsResultType())
            {
                count++;
            }
        }

        return count;
    }
}