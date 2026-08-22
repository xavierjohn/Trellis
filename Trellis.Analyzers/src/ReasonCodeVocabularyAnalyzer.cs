namespace Trellis.Analyzers;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// Analyzer that inspects string literals passed as a reason code and reports the three ways such a
/// literal interacts badly with the frozen Trellis vocabulary: it restates a framework code that has
/// a constant, it squats the reserved <c>error.*</c> namespace, or it squats one of the framework's
/// own namespaces.
/// </summary>
/// <remarks>
/// <para>
/// Three surfaces carry a reason code to the wire, and all three are analyzed: a <c>reasonCode</c>
/// parameter on any Trellis method or constructor, FluentValidation's <c>WithErrorCode</c>, and the
/// <c>Code</c> property on the Trellis primitive attributes. They are matched by parameter or property
/// name rather than by a list of members, so a surface added later is covered without a change here.
/// </para>
/// <para>
/// The rule deliberately does <b>not</b> check vocabulary membership. <c>trellis-api-core.md</c> scopes
/// the freeze to "the reason codes the framework itself emits" and states that it "constrains Trellis,
/// not the application"; <c>trellis-api-primitives.md</c> adds that "no analyzer pressures either
/// choice". An application code such as <c>order.cancel-after-ship</c> is legitimate and stays silent.
/// What is reported is narrower: a literal that is already a framework code (so the constant exists and
/// the literal is a silent wire break waiting on a typo), or one that claims a namespace the framework
/// has given a published meaning.
/// </para>
/// <para>
/// The vocabulary is read from the compilation rather than copied into the analyzer. A hard-coded table
/// would be a second source of truth that goes stale the first time a code is added, and this analyzer
/// exists precisely because duplicated reason codes drift. When <c>Trellis.Core</c> is not referenced
/// there is no vocabulary to compare against and the analyzer does nothing.
/// </para>
/// <para>
/// Only literal syntax is reported. A constant reference has a constant <em>value</em> too, so testing
/// <c>ConstantValue</c> alone would flag <c>ForField(f, ValidationCodes.ValueNotNull)</c> — the exact
/// shape this rule tells authors to write. A code reached through an application's own
/// <c>const string</c> indirection is therefore invisible here, which is the accepted trade.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReasonCodeVocabularyAnalyzer : DiagnosticAnalyzer
{
    private const string ValidationCodesMetadataName = "Trellis.ValidationCodes";
    private const string FaultCodesMetadataName = "Trellis.FaultCodes";
    private const string TrellisRootNamespace = "Trellis";
    private const string ReasonCodeParameterName = "reasonCode";
    private const string ErrorNamespacePrefix = "error.";

    /// <summary>
    /// FluentValidation's own naming surface. A code written here reaches the wire through
    /// <c>Trellis.FluentValidation</c>'s projection exactly as a <c>reasonCode</c> does, so the same
    /// three findings apply.
    /// </summary>
    private const string FluentValidationRootNamespace = "FluentValidation";
    private const string WithErrorCodeName = "WithErrorCode";
    private const string ErrorCodeParameterName = "errorCode";

    /// <summary>
    /// The settable property through which the Trellis primitive attributes
    /// (<c>[StringLength]</c>, <c>[Range]</c>, <c>[NotDefault]</c> and the sign-convenience
    /// attributes) override a generated value object's reason code.
    /// </summary>
    private const string CodePropertyName = "Code";

    /// <summary>
    /// The pre-vocabulary placeholder. It is a frozen value with a constant, but the documented
    /// guidance is not "use the constant" — it is "do not emit this at all", so it gets its own
    /// message and no code fix.
    /// </summary>
    private const string LegacyPlaceholder = "validation.error";

    /// <summary>
    /// Key under which the code fix reads the constant to substitute, e.g. <c>ValidationCodes.ValueNotNull</c>.
    /// </summary>
    internal const string ConstantPropertyKey = "TrellisReasonCodeConstant";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.ReasonCodeVocabulary];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var vocabulary = Vocabulary.Load(compilationContext.Compilation);
            if (vocabulary.IsEmpty)
                return;

            compilationContext.RegisterOperationAction(
                ctx => AnalyzeInvocation(ctx, vocabulary),
                OperationKind.Invocation);

            compilationContext.RegisterOperationAction(
                ctx => AnalyzeObjectCreation(ctx, vocabulary),
                OperationKind.ObjectCreation);

            compilationContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeAttribute(ctx, vocabulary),
                SyntaxKind.Attribute);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, Vocabulary vocabulary)
    {
        var operation = (IInvocationOperation)context.Operation;
        var method = operation.TargetMethod;

        var parameterName =
            IsTrellisDeclared(method.ContainingType) ? ReasonCodeParameterName
            : IsFluentValidationErrorCode(method) ? ErrorCodeParameterName
            : null;

        if (parameterName is null)
            return;

        AnalyzeArguments(context, vocabulary, operation.Arguments, parameterName);
    }

    /// <summary>
    /// Matches FluentValidation's <c>WithErrorCode</c>, the one naming surface outside Trellis whose
    /// argument becomes a Trellis reason code verbatim.
    /// </summary>
    /// <remarks>
    /// The declaring namespace is required so an unrelated API that happens to expose a method of the
    /// same name is not analyzed. An application's own <c>WithErrorCode</c> declared in
    /// <c>namespace FluentValidation</c> — a common convention — is deliberately still matched: whatever
    /// wraps it, the argument is an error code, so restating a frozen one there is the same defect.
    /// </remarks>
    private static bool IsFluentValidationErrorCode(IMethodSymbol method) =>
        method.Name == WithErrorCodeName
        && IsInNamespace(method.ContainingType, FluentValidationRootNamespace);

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, Vocabulary vocabulary)
    {
        var operation = (IObjectCreationOperation)context.Operation;
        if (operation.Constructor is not { } constructor || !IsTrellisDeclared(constructor.ContainingType))
            return;

        AnalyzeArguments(context, vocabulary, operation.Arguments, ReasonCodeParameterName);
    }

    /// <summary>
    /// Reports a frozen or squatting literal assigned to <c>Code</c> on a Trellis primitive attribute.
    /// </summary>
    /// <remarks>
    /// Attribute arguments must be compile-time constants, so the constant the code fix substitutes is
    /// legal here for the same reason the literal is.
    /// </remarks>
    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context, Vocabulary vocabulary)
    {
        var attribute = (AttributeSyntax)context.Node;
        if (attribute.ArgumentList is not { } argumentList)
            return;

        if (context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol
            is not IMethodSymbol constructor
            || !IsTrellisDeclared(constructor.ContainingType))
        {
            return;
        }

        foreach (var argument in argumentList.Arguments)
        {
            if (argument.NameEquals?.Name.Identifier.ValueText != CodePropertyName)
                continue;

            if (argument.Expression is not LiteralExpressionSyntax literal
                || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                continue;
            }

            Report(
                context.ReportDiagnostic,
                vocabulary,
                literal.Token.ValueText,
                literal.GetLocation());
        }
    }

    private static void AnalyzeArguments(
        OperationAnalysisContext context,
        Vocabulary vocabulary,
        ImmutableArray<IArgumentOperation> arguments,
        string parameterName)
    {
        foreach (var argument in arguments)
        {
            if (!IsReasonCodeParameter(argument.Parameter, parameterName))
                continue;

            if (UnwrapLiteral(argument.Value) is not { } literal)
                continue;

            if (literal.ConstantValue.Value is not string code)
                continue;

            Report(context.ReportDiagnostic, vocabulary, code, literal.Syntax.GetLocation());
        }
    }

    private static void Report(
        Action<Diagnostic> report,
        Vocabulary vocabulary,
        string code,
        Location location)
    {
        if (code.Length == 0 || Classify(code, vocabulary) is not { } finding)
            return;

        report(Diagnostic.Create(
            DiagnosticDescriptors.ReasonCodeVocabulary,
            location,
            finding.Properties,
            code,
            finding.Explanation));
    }

    /// <summary>
    /// Matches the parameter every Trellis reason-code surface uses, whether written as a method
    /// parameter (<c>ForField</c>, <c>ForRule</c>, <c>ForReason</c>, <c>For</c>) or as a positional
    /// record parameter (<c>FieldViolation.ReasonCode</c>, <c>RuleViolation.ReasonCode</c>).
    /// </summary>
    /// <remarks>
    /// Keying on the parameter name rather than a list of method names and argument positions is what
    /// keeps this rule from going stale: the eleven current factory overloads differ in arity and in
    /// where the code sits, and a twelfth is covered the day it is added. Comparison is
    /// case-insensitive because positional records declare the parameter as <c>ReasonCode</c>.
    /// </remarks>
    private static bool IsReasonCodeParameter(IParameterSymbol? parameter, string parameterName) =>
        parameter is not null
        && string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the literal underneath an argument, seeing through the implicit conversions the
    /// operation tree inserts, or <see langword="null"/> when the argument is anything else.
    /// </summary>
    private static IOperation? UnwrapLiteral(IOperation value)
    {
        var current = value;

        while (current is IConversionOperation conversion)
            current = conversion.Operand;

        return current is ILiteralOperation ? current : null;
    }

    private static bool IsTrellisDeclared(INamedTypeSymbol? containingType) =>
        IsInNamespace(containingType, TrellisRootNamespace);

    private static bool IsInNamespace(INamedTypeSymbol? containingType, string rootNamespace)
    {
        if (containingType?.ContainingNamespace is not { } containingNamespace)
            return false;

        var name = containingNamespace.ToDisplayString();
        return name == rootNamespace
            || name.StartsWith(rootNamespace + ".", StringComparison.Ordinal);
    }

    private static Finding? Classify(string code, Vocabulary vocabulary)
    {
        if (string.Equals(code, LegacyPlaceholder, StringComparison.Ordinal))
        {
            return new Finding(
                "is the pre-vocabulary placeholder, which the boundary normalizes away; emit a real "
                + "reason code instead",
                ImmutableDictionary<string, string?>.Empty);
        }

        if (vocabulary.ConstantsByValue.TryGetValue(code, out var constant))
        {
            return new Finding(
                $"is the framework reason code {constant}; emit it by constant, because a typo in a "
                + "literal is a silent wire break while a typo in a constant name does not compile",
                ImmutableDictionary<string, string?>.Empty.Add(ConstantPropertyKey, constant));
        }

        if (code.StartsWith(ErrorNamespacePrefix, StringComparison.Ordinal))
        {
            return new Finding(
                "claims the reserved 'error.*' namespace, which carries only the 'error.unspecified' "
                + "sentinel; a second member there makes the \"no reason available\" fallback lossy",
                ImmutableDictionary<string, string?>.Empty);
        }

        var separator = code.IndexOf('.');
        if (separator > 0)
        {
            var prefix = code.Substring(0, separator);
            if (vocabulary.FrameworkNamespaces.Contains(prefix))
            {
                return new Finding(
                    $"claims the framework namespace '{prefix}.*', whose meaning Trellis publishes, so a "
                    + "client falling back on the prefix will read this application code as a framework one "
                    + "— application codes are free-form, so pick a namespace the framework does not own",
                    ImmutableDictionary<string, string?>.Empty);
            }
        }

        return null;
    }

    private readonly struct Finding
    {
        public Finding(string explanation, ImmutableDictionary<string, string?> properties)
        {
            Explanation = explanation;
            Properties = properties;
        }

        public string Explanation { get; }

        public ImmutableDictionary<string, string?> Properties { get; }
    }

    /// <summary>
    /// The frozen vocabulary as read from the compilation being analyzed.
    /// </summary>
    private sealed class Vocabulary
    {
        private Vocabulary(
            ImmutableDictionary<string, string> constantsByValue,
            ImmutableHashSet<string> frameworkNamespaces)
        {
            ConstantsByValue = constantsByValue;
            FrameworkNamespaces = frameworkNamespaces;
        }

        /// <summary>Wire value to the display form of its constant, e.g. <c>value.not-null</c> to <c>ValidationCodes.ValueNotNull</c>.</summary>
        public ImmutableDictionary<string, string> ConstantsByValue { get; }

        /// <summary>First segment of every dotted frozen code — the namespaces the framework has given a published meaning.</summary>
        public ImmutableHashSet<string> FrameworkNamespaces { get; }

        public bool IsEmpty => ConstantsByValue.IsEmpty;

        public static Vocabulary Load(Compilation compilation)
        {
            var constants = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            var namespaces = ImmutableHashSet.CreateBuilder(StringComparer.Ordinal);

            foreach (var metadataName in new[] { ValidationCodesMetadataName, FaultCodesMetadataName })
            {
                if (compilation.GetTypeByMetadataName(metadataName) is not { } type)
                    continue;

                foreach (var value in StringConstants(type))
                {
                    // A duplicate value would already have failed ValidationCodesTests; keep the first
                    // so the analyzer stays deterministic rather than throwing during a build. Types are
                    // walked in a fixed order, so "first" means the ValidationCodes spelling wins.
                    if (!constants.ContainsKey(value.Value))
                        constants[value.Value] = $"{type.Name}.{value.Name}";

                    // The legacy placeholder's namespace is an artifact rather than a published one:
                    // reserving `validation.*` against applications on its account would flag codes the
                    // framework never claimed.
                    if (string.Equals(value.Value, LegacyPlaceholder, StringComparison.Ordinal))
                        continue;

                    var separator = value.Value.IndexOf('.');
                    if (separator > 0)
                        namespaces.Add(value.Value.Substring(0, separator));
                }
            }

            return new Vocabulary(constants.ToImmutable(), namespaces.ToImmutable());
        }

        private static IEnumerable<(string Name, string Value)> StringConstants(INamedTypeSymbol type) =>
            type.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f is
                {
                    IsConst: true,
                    HasConstantValue: true,
                    DeclaredAccessibility: Accessibility.Public
                })
                .Select(f => (f.Name, Value: f.ConstantValue as string))
                .Where(f => !string.IsNullOrEmpty(f.Value))
                .Select(f => (f.Name, f.Value!));
    }
}
