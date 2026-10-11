namespace Trellis.Analyzers;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// Reports fresh GUID-derived values emitted as reason codes rather than incident identifiers.
/// </summary>
/// <remarks>
/// Reason-code arguments are identified by their bound parameters on error and violation types,
/// or FluentValidation's <c>WithErrorCode</c>. Other dynamic application codes remain legal.
/// String and GUID local provenance is followed only with a single write and no reference escape;
/// opaque calls and field, property, or parameter values are not inferred.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PerIncidentReasonCodeAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.PerIncidentReasonCode];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(Analyze,
            OperationKind.Invocation, OperationKind.ObjectCreation, OperationKind.SimpleAssignment);
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        switch (context.Operation)
        {
            case IInvocationOperation invocation when IsReasonCodeMethod(invocation.TargetMethod):
                AnalyzeArguments(context, invocation.Arguments);
                break;
            case IObjectCreationOperation { Constructor: { } constructor } creation
                when IsErrorOrViolation(constructor.ContainingType):
                AnalyzeArguments(context, creation.Arguments);
                break;
            case ISimpleAssignmentOperation { Target: IPropertyReferenceOperation target } assignment
                when IsReasonCodeProperty(target.Property):
                ReportIfPerIncident(context, assignment.Value);
                break;
        }
    }

    private static bool IsReasonCodeMethod(IMethodSymbol method) =>
        IsErrorOrViolation(method.ContainingType)
        || (method.Name == "WithErrorCode"
            && IsInNamespace(method.ContainingType, "FluentValidation"));

    private static bool IsErrorOrViolation(ITypeSymbol type) =>
        type.IsErrorOrDerivedType() || IsViolation(type);

    private static bool IsViolation(ITypeSymbol type) =>
        type.Name is "FieldViolation" or "RuleViolation"
        && type.ContainingNamespace.ToDisplayString() == "Trellis";

    private static bool IsInNamespace(ITypeSymbol type, string root)
    {
        var name = type.ContainingNamespace.ToDisplayString();
        return name == root || name.StartsWith(root + ".", StringComparison.Ordinal);
    }

    private static bool IsReasonCodeProperty(IPropertySymbol property) =>
        property.Type.SpecialType == SpecialType.System_String
        && ((property.Name == "Code" && property.ContainingType.IsErrorOrDerivedType())
            || (property.Name == "ReasonCode" && IsViolation(property.ContainingType)));

    private static void AnalyzeArguments(
        OperationAnalysisContext context, ImmutableArray<IArgumentOperation> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Parameter is not { Type.SpecialType: SpecialType.System_String } parameter)
                continue;

            var name = parameter.Name;
            if (string.Equals(name, "code", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "reasonCode", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(name, "errorCode", StringComparison.OrdinalIgnoreCase)
                    && parameter.ContainingSymbol is IMethodSymbol { Name: "WithErrorCode" }))
            {
                ReportIfPerIncident(context, argument.Value);
            }
        }
    }

    private static void ReportIfPerIncident(OperationAnalysisContext context, IOperation value)
    {
        if (ContainsFreshGuid(value, new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                context.CancellationToken))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.PerIncidentReasonCode, value.Syntax.GetLocation()));
        }
    }

    private static bool ContainsFreshGuid(
        IOperation value, HashSet<ILocalSymbol> visitedLocals, CancellationToken cancellationToken,
        bool enumerateElements = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (value)
        {
            case IConversionOperation { OperatorMethod: null } conversion:
                return ContainsFreshGuid(conversion.Operand, visitedLocals, cancellationToken, enumerateElements);
            case IParenthesizedOperation parentheses:
                return ContainsFreshGuid(parentheses.Operand, visitedLocals, cancellationToken, enumerateElements);
            case IInvocationOperation invocation when IsFreshGuidFactory(invocation.TargetMethod):
                return true;
            case IInvocationOperation invocation when IsStringification(invocation.TargetMethod):
                if (invocation.Instance is { } instance)
                    return ContainsFreshGuid(instance, visitedLocals, cancellationToken);

                foreach (var argument in invocation.Arguments)
                {
                    var enumerateArgumentElements = invocation.TargetMethod.Name == "Concat"
                        && argument.Parameter is { } parameter && IsConcatenatedCollection(parameter.Type);

                    if (ContainsFreshGuid(argument.Value, visitedLocals, cancellationToken, enumerateArgumentElements))
                        return true;
                }

                return false;
            case IInterpolatedStringOperation interpolation:
                foreach (var part in interpolation.Parts)
                {
                    if (part is IInterpolationOperation hole
                        && ContainsFreshGuid(hole.Expression, visitedLocals, cancellationToken))
                        return true;
                }

                return false;
            case IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Add, OperatorMethod: null,
                Type.SpecialType: SpecialType.System_String
            } addition:
                return ContainsFreshGuid(addition.LeftOperand, visitedLocals, cancellationToken)
                    || ContainsFreshGuid(addition.RightOperand, visitedLocals, cancellationToken);
            case IConditionalOperation conditional:
                if (conditional.Condition.ConstantValue is { HasValue: true, Value: bool condition })
                {
                    var selected = condition ? conditional.WhenTrue : conditional.WhenFalse;
                    return selected is not null
                        && ContainsFreshGuid(selected, visitedLocals, cancellationToken, enumerateElements);
                }

                return ContainsFreshGuid(conditional.WhenTrue, visitedLocals, cancellationToken, enumerateElements)
                    || (conditional.WhenFalse is { } otherwise
                        && ContainsFreshGuid(otherwise, visitedLocals, cancellationToken, enumerateElements));
            case ICoalesceOperation coalesce:
                if (coalesce.Value.ConstantValue.HasValue)
                {
                    return ContainsFreshGuid(coalesce.Value.ConstantValue.Value is null
                        ? coalesce.WhenNull : coalesce.Value, visitedLocals, cancellationToken, enumerateElements);
                }

                return ContainsFreshGuid(coalesce.Value, visitedLocals, cancellationToken, enumerateElements)
                    || ContainsFreshGuid(coalesce.WhenNull, visitedLocals, cancellationToken, enumerateElements);
            case IArrayCreationOperation { Initializer: { } initializer } when enumerateElements:
                foreach (var element in initializer.ElementValues)
                {
                    if (ContainsFreshGuid(element, visitedLocals, cancellationToken))
                        return true;
                }

                return false;
            case ICollectionExpressionOperation collection
                when enumerateElements && collection.Type is { } type && IsConcatenatedCollection(type):
                foreach (var element in collection.Elements)
                {
                    var hasFreshElement = element is ISpreadOperation spread
                        ? !spread.ElementConversion.IsUserDefined
                            && ContainsFreshGuid(spread.Operand, visitedLocals, cancellationToken, enumerateElements: true)
                        : ContainsFreshGuid(element, visitedLocals, cancellationToken);

                    if (hasFreshElement)
                        return true;
                }

                return false;
            case ILocalReferenceOperation local
                when (local.Local.Type.SpecialType == SpecialType.System_String || IsGuid(local.Local.Type))
                    && visitedLocals.Add(local.Local):
                if (FindSingleWrite(local, cancellationToken) is { } initializerValue)
                    return ContainsFreshGuid(initializerValue, visitedLocals, cancellationToken, enumerateElements);

                return false;
            default:
                return false;
        }
    }

    private static bool IsFreshGuidFactory(IMethodSymbol method) =>
        method.IsStatic && IsGuid(method.ContainingType) && IsGuid(method.ReturnType)
        && method.Name is "NewGuid" or "CreateVersion7";

    private static bool IsGuid(ITypeSymbol type) =>
        type.Name == "Guid" && type.ContainingNamespace.ToDisplayString() == "System";

    private static bool IsConcatenatedCollection(ITypeSymbol type) =>
        type is IArrayTypeSymbol
        || type.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T
        || (type is INamedTypeSymbol { Name: "ReadOnlySpan", Arity: 1 }
            && type.ContainingNamespace.ToDisplayString() == "System");

    private static bool IsStringification(IMethodSymbol method) =>
        (method.Name == "ToString"
            && (IsGuid(method.ContainingType)
                || method.ContainingType.SpecialType is SpecialType.System_Object or SpecialType.System_String))
        || (method.IsStatic && method.Name == "ToString"
            && method.ContainingType.ToDisplayString() == "System.Convert")
        || (method.IsStatic && method.Name == "Concat"
            && method.ContainingType.SpecialType == SpecialType.System_String);

    private static IOperation? FindSingleWrite(
        ILocalReferenceOperation reference, CancellationToken cancellationToken)
    {
        IOperation root = reference;
        while (root.Parent is { } parent)
            root = parent;

        var writes = new LocalWrites(reference.Local, cancellationToken);
        writes.Visit(root);
        return writes.SingleValue;
    }

    private sealed class LocalWrites(ILocalSymbol local, CancellationToken cancellationToken) : OperationWalker
    {
        private int _writes;
        private bool _escaped;
        private IOperation? _value;

        public IOperation? SingleValue => _writes == 1 && !_escaped ? _value : null;

        public override void Visit(IOperation? operation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            base.Visit(operation);
        }

        public override void VisitVariableDeclarator(IVariableDeclaratorOperation operation)
        {
            if (operation.Initializer is { } initializer)
            {
                if (SymbolEqualityComparer.Default.Equals(operation.Symbol, local))
                    RecordWrite(initializer.Value);
                else if (operation.Symbol.RefKind != RefKind.None && ReferencesLocal(initializer.Value))
                    _escaped = true;
            }

            base.VisitVariableDeclarator(operation);
        }

        public override void VisitSimpleAssignment(ISimpleAssignmentOperation operation)
        {
            if (operation.IsRef && ReferencesLocal(operation.Value))
                _escaped = true;

            if (operation.Target is ILocalReferenceOperation target
                && SymbolEqualityComparer.Default.Equals(target.Local, local))
                RecordWrite(operation.Value);

            base.VisitSimpleAssignment(operation);
        }

        public override void VisitAddressOf(IAddressOfOperation operation)
        {
            if (ReferencesLocal(operation.Reference))
                _escaped = true;

            base.VisitAddressOf(operation);
        }

        public override void VisitCompoundAssignment(ICompoundAssignmentOperation operation)
        {
            if (ReferencesLocal(operation.Target))
                _escaped = true;

            base.VisitCompoundAssignment(operation);
        }

        public override void VisitDeconstructionAssignment(IDeconstructionAssignmentOperation operation)
        {
            if (ReferencesLocal(operation.Target))
                _escaped = true;

            base.VisitDeconstructionAssignment(operation);
        }

        public override void VisitIncrementOrDecrement(IIncrementOrDecrementOperation operation)
        {
            if (ReferencesLocal(operation.Target))
                _escaped = true;

            base.VisitIncrementOrDecrement(operation);
        }

        public override void VisitArgument(IArgumentOperation operation)
        {
            if (operation.Parameter is { RefKind: not RefKind.None } && ReferencesLocal(operation.Value))
                _escaped = true;

            base.VisitArgument(operation);
        }

        private void RecordWrite(IOperation value)
        {
            _writes++;
            _value = value;
        }

        private bool ReferencesLocal(IOperation operation)
        {
            if (operation is ILocalReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Local, local))
                return true;

            foreach (var child in operation.ChildOperations)
            {
                if (ReferencesLocal(child))
                    return true;
            }

            return false;
        }
    }
}