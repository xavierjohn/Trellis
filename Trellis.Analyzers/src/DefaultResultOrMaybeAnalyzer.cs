namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
/// TRLS029 — flags explicit <c>default(Result)</c>, <c>default(Result&lt;T&gt;)</c>, and
/// <c>default(Maybe&lt;T&gt;)</c> at use sites. Per ADR-002 §3.5.1, default-initialized
/// <see cref="Trellis.Result"/> and <see cref="Trellis.Result{TValue}"/> represent a typed
/// failure (<see cref="Trellis.Error.Unexpected"/> sentinel), and <c>default(Maybe&lt;T&gt;)</c>
/// is semantically <c>Maybe&lt;T&gt;.None</c>; in both cases the explicit literal obscures
/// intent and is a known footgun.
/// </summary>
/// <remarks>
/// <para>
/// Detection uses <see cref="OperationKind.DefaultValue"/> to cover all surface forms:
/// <list type="bullet">
///   <item><c>default(Result)</c>, <c>default(Result&lt;T&gt;)</c>, <c>default(Maybe&lt;T&gt;)</c> (typeof-style)</item>
///   <item>Target-typed <c>default</c> in <c>return default;</c>, parameter defaults, etc.</item>
///   <item><c>default!</c> with the null-suppressing operator</item>
/// </list>
/// </para>
/// <para>
/// To suppress: use <c>[SuppressMessage("Trellis", "TRLS029", Justification = "...")]</c>
/// on the enclosing member, or <c>#pragma warning disable TRLS029</c> on the offending span.
/// Both are sanctioned by the ADR for legitimate sentinel/test-helper sites.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DefaultResultOrMaybeAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.DefaultResultOrMaybe];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeDefaultValue, OperationKind.DefaultValue);
    }

    private static void AnalyzeDefaultValue(OperationAnalysisContext context)
    {
        var op = (IDefaultValueOperation)context.Operation;
        if (op.Type is not INamedTypeSymbol type)
            return;

        string typeDisplay;
        string suggestion;
        if (type.IsNonGenericResultType())
        {
            typeDisplay = "Result";
            suggestion = "Result.Ok() or Result.Fail(...)";
        }
        else if (type.IsResultType())
        {
            typeDisplay = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            suggestion = "Result.Ok(...) or Result.Fail<T>(...)";
        }
        else if (type.IsMaybeType())
        {
            typeDisplay = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            suggestion = "Maybe<T>.None or Maybe.From(...)";
        }
        else
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.DefaultResultOrMaybe,
            op.Syntax.GetLocation(),
            typeDisplay,
            suggestion));
    }
}
