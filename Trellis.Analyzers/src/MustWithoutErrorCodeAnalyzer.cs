namespace Trellis.Analyzers;

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects a FluentValidation <c>Must</c>/<c>MustAsync</c> rule component with no
/// <c>WithErrorCode</c>, which reaches the client as <c>error.unspecified</c>.
/// Only activates when the compilation references FluentValidation.
/// </summary>
/// <remarks>
/// <para>
/// Every built-in validator carries a name that <c>Trellis.FluentValidation</c> projects to a real
/// reason code. <c>Must</c> and <c>MustAsync</c> are the ones that do not: they report as
/// <c>PredicateValidator</c> and <c>AsyncPredicateValidator</c>, both of which project to the
/// sentinel. They are also the validators applications reach for most, so without this rule the
/// largest producer of application-authored failures is the one that says nothing.
/// </para>
/// <para>
/// The analyzer reports only when it can prove the component carries no code. A rule whose value
/// escapes the statement, is refined by <c>Configure</c>, or is passed through an extension the
/// analyzer does not recognise could be named somewhere the syntax does not reach, so those stay
/// silent. A false positive here accuses an author who did the right thing, which costs more trust
/// than a missed occurrence of a shape this rule will flag a hundred other times.
/// </para>
/// <para>
/// Only fluent (reduced extension) syntax is analyzed. Calling the rule builder extensions in static
/// form — <c>DefaultValidatorExtensions.Must(builder, predicate)</c> — is not recognised. That shape
/// does not occur in validator code, and missing it costs a diagnostic rather than producing a wrong
/// one.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustWithoutErrorCodeAnalyzer : DiagnosticAnalyzer
{
    private const string RuleBuilderMetadataName = "FluentValidation.IRuleBuilder`2";
    private const string FluentValidationRootNamespace = "FluentValidation";
    private const string WithErrorCodeName = "WithErrorCode";

    /// <summary>
    /// The type holding FluentValidation's built-in validators. A chained call from here is a real
    /// validator, so it starts a new rule component and the preceding one is provably unnamed.
    /// </summary>
    /// <remarks>
    /// Matching the container rather than the namespace matters: applications commonly declare their
    /// own rule extensions in <c>namespace FluentValidation</c> so callers need no extra
    /// <c>using</c>, and such a helper may well wrap <c>WithErrorCode</c>.
    /// </remarks>
    private const string BuiltInValidatorContainer = "DefaultValidatorExtensions";

    /// <summary>
    /// The escape hatch onto the raw rule object, which can set <c>ErrorCode</c> directly. Its
    /// argument is a lambda the analyzer does not read, so a chain that calls it proves nothing.
    /// </summary>
    private const string ConfigureName = "Configure";

    /// <summary>
    /// The asynchronous predicate validator, which FluentValidation reports under its own name.
    /// </summary>
    private const string MustAsyncName = "MustAsync";

    private static readonly ImmutableHashSet<string> PredicateValidatorNames =
        ["Must", MustAsyncName];

    /// <summary>
    /// FluentValidation calls that refine the rule component already on the chain without being able
    /// to name its failure, so a <c>WithErrorCode</c> beyond them still applies to the same component.
    /// </summary>
    private static readonly ImmutableHashSet<string> ComponentModifierNames =
    [
        "WithMessage",
        WithErrorCodeName,
        "WithSeverity",
        "WithName",
        "WithState",
        "When",
        "Unless",
        "WhenAsync",
        "UnlessAsync",
        "OverridePropertyName",
        "DependentRules"
    ];

    /// <summary>
    /// What the calls chained after a <c>Must</c> prove about whether its failure is named.
    /// </summary>
    private enum ComponentCoding
    {
        /// <summary>The chain ends, or a new component starts, with no code attached to this one.</summary>
        ProvablyUncoded,

        /// <summary>A <c>WithErrorCode</c> applies to this component.</summary>
        Coded,

        /// <summary>The chain leaves what the syntax can see, so nothing is proven either way.</summary>
        Unknown
    }

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.MustWithoutErrorCode];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var ruleBuilderType = compilationContext.Compilation.GetTypeByMetadataName(RuleBuilderMetadataName);
            if (ruleBuilderType is null)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                ctx => AnalyzeInvocation(ctx, ruleBuilderType),
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, INamedTypeSymbol ruleBuilderType)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (!PredicateValidatorNames.Contains(memberAccess.Name.Identifier.Text))
            return;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method)
            return;

        if (!IsRuleBuilderExtension(method, ruleBuilderType))
            return;

        // An application may declare its own Must overload in namespace FluentValidation, which passes
        // both the namespace and receiver checks; only the built-in one is known to leave the failure
        // unnamed. This mirrors the same test the chain walk applies to later components.
        if (method.ContainingType.Name != BuiltInValidatorContainer)
            return;

        if (ClassifyChainAfter(invocation, context, ruleBuilderType) != ComponentCoding.ProvablyUncoded)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.MustWithoutErrorCode,
            memberAccess.Name.GetLocation(),
            memberAccess.Name.Identifier.Text,
            ReportedValidatorName(memberAccess.Name.Identifier.Text)));
    }

    /// <summary>
    /// The validator name FluentValidation puts on the failure, which <c>Trellis.FluentValidation</c>
    /// then projects to <c>error.unspecified</c>.
    /// </summary>
    private static string ReportedValidatorName(string methodName) =>
        methodName == MustAsyncName ? "AsyncPredicateValidator" : "PredicateValidator";

    /// <summary>
    /// Confirms the call is FluentValidation's own rule-building extension rather than an unrelated
    /// method that happens to be named <c>Must</c>.
    /// </summary>
    /// <remarks>
    /// The receiver check is what does the work: a namespace test alone would also match a
    /// third-party <c>FluentValidationExtras</c>, and the method name alone matches anything.
    /// </remarks>
    private static bool IsRuleBuilderExtension(IMethodSymbol method, INamedTypeSymbol ruleBuilderType) =>
        IsInFluentValidationNamespace(method.ContainingType)
        && ImplementsRuleBuilder(method.ReceiverType, ruleBuilderType);

    private static bool IsInFluentValidationNamespace(INamedTypeSymbol? containingType)
    {
        if (containingType?.ContainingNamespace is not { } containingNamespace)
            return false;

        var name = containingNamespace.ToDisplayString();
        return name == FluentValidationRootNamespace
            || name.StartsWith(FluentValidationRootNamespace + ".", StringComparison.Ordinal);
    }

    private static bool ImplementsRuleBuilder(ITypeSymbol? type, INamedTypeSymbol ruleBuilderType)
    {
        if (type is null)
            return false;

        if (SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, ruleBuilderType))
            return true;

        foreach (var implemented in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, ruleBuilderType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Walks the calls chained after <paramref name="mustInvocation"/> to decide what they prove
    /// about whether that component's failure is named.
    /// </summary>
    /// <remarks>
    /// The walk stops at the first call that is not a known component modifier. One of
    /// FluentValidation's own built-in validators there starts a new component, so any code beyond it
    /// names that component's failure and this one is provably unnamed. Anything else — a helper the
    /// application declared, <c>Configure</c>, or a chain whose value escapes the statement — could
    /// still name this component out of sight.
    /// </remarks>
    private static ComponentCoding ClassifyChainAfter(
        InvocationExpressionSyntax mustInvocation,
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol ruleBuilderType)
    {
        var current = (ExpressionSyntax)mustInvocation;

        while (true)
        {
            var outermost = ClimbParentheses(current);

            if (outermost.Parent is not MemberAccessExpressionSyntax parentAccess
                || parentAccess.Expression != outermost
                || parentAccess.Parent is not InvocationExpressionSyntax nextInvocation)
            {
                // Nothing further is chained on. Only a value that goes nowhere is provably final:
                // one that is assigned, returned, or passed on can still be coded elsewhere.
                return outermost.Parent is ExpressionStatementSyntax
                    ? ComponentCoding.ProvablyUncoded
                    : ComponentCoding.Unknown;
            }

            var nextName = parentAccess.Name.Identifier.Text;

            if (context.SemanticModel.GetSymbolInfo(nextInvocation, context.CancellationToken).Symbol
                is not IMethodSymbol nextMethod || !IsRuleBuilderExtension(nextMethod, ruleBuilderType))
                return ComponentCoding.Unknown;

            if (nextName == WithErrorCodeName)
                return ComponentCoding.Coded;

            if (nextName == ConfigureName)
                return ComponentCoding.Unknown;

            if (ComponentModifierNames.Contains(nextName))
            {
                current = nextInvocation;
                continue;
            }

            return nextMethod.ContainingType.Name == BuiltInValidatorContainer
                ? ComponentCoding.ProvablyUncoded
                : ComponentCoding.Unknown;
        }
    }

    /// <summary>
    /// Returns the outermost expression that wraps <paramref name="expression"/> in parentheses only,
    /// so <c>(RuleFor(x).Must(p)).WithErrorCode("c")</c> reads as one chain.
    /// </summary>
    private static ExpressionSyntax ClimbParentheses(ExpressionSyntax expression)
    {
        while (expression.Parent is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized;

        return expression;
    }
}
