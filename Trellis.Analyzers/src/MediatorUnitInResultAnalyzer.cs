namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer (TRLS059) that flags <c>Result&lt;Mediator.Unit&gt;</c>. Trellis and the
/// martinothamar/Mediator package both define a <c>Unit</c> type; in a handler file with a
/// file-scoped <c>using Mediator;</c>, a bare <c>Unit</c> binds to <c>Mediator.Unit</c> (the nearer
/// using wins, with no ambiguity error), so <c>Result&lt;Unit&gt;</c> silently compiles as
/// <c>Result&lt;Mediator.Unit&gt;</c>. The Trellis.Asp response layer only maps
/// <c>Result&lt;Trellis.Unit&gt;</c> to <c>204 No Content</c>, so the endpoint returns 200 instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MediatorUnitInResultAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.MediatorUnitInResult];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // A single GenericName hook catches every occurrence — handler return types,
        // ICommand/IQuery declarations, handler interfaces, and local variables —
        // without double-reporting (only the inner Result<...> node matches).
        context.RegisterSyntaxNodeAction(AnalyzeGenericName, SyntaxKind.GenericName);
    }

    private static void AnalyzeGenericName(SyntaxNodeAnalysisContext context)
    {
        var genericName = (GenericNameSyntax)context.Node;

        // Fast syntactic filter before touching the semantic model.
        if (genericName.Identifier.ValueText != "Result")
            return;
        if (genericName.TypeArgumentList.Arguments.Count != 1)
            return;

        // Must bind to Trellis.Result<T> with a Mediator.Unit type argument.
        if (context.SemanticModel.GetSymbolInfo(genericName, context.CancellationToken).Symbol is not INamedTypeSymbol resultType)
            return;
        if (!resultType.IsResultType())
            return;
        if (!resultType.TypeArguments[0].IsMediatorUnit())
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.MediatorUnitInResult,
            genericName.GetLocation()));
    }
}
