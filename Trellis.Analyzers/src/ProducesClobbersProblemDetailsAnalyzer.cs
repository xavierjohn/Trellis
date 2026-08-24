namespace Trellis.Analyzers;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// TRLS065: Warns when a <c>[Produces(...)]</c> media-type list contains a JSON-family media type
/// in a project that uses Trellis.Asp.
/// <para>
/// <c>ProducesAttribute</c> is a result filter that overwrites <c>ObjectResult.ContentTypes</c>
/// wholesale. Because the JSON output formatter advertises <c>application/json</c>,
/// <c>text/json</c> and <c>application/*+json</c>, it can write a <c>ProblemDetails</c> as any of
/// them — so an RFC 9457 failure response silently loses its <c>application/problem+json</c>
/// media type. Adding <c>application/problem+json</c> to the list does not fix it, in any
/// position: MVC's <c>SelectFormatterUsingAnyAcceptableContentType</c> loops over formatters in
/// the outer loop and acceptable media types in the inner one, so the JSON formatter claims
/// whichever of its media types the list names — a trailing entry sits inert behind an earlier
/// <c>application/json</c>, and one the JSON formatter reaches first rewrites successful
/// responses to <c>problem+json</c>. The remedy is to trim the formatters registered in
/// <c>MvcOptions</c>.
/// </para>
/// <para>
/// Lists containing no JSON-family type are deliberately not reported. A <c>text/csv</c> or
/// <c>application/pdf</c> formatter declines <c>ProblemDetails</c> in <c>CanWriteType</c>, so MVC
/// falls back and the problem response keeps <c>application/problem+json</c>. Mixing a non-JSON
/// type with a JSON-family one does not earn the same exemption: MVC loops over formatters in the
/// outer loop, so the JSON formatter can claim its entry ahead of a later-registered one.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProducesClobbersProblemDetailsAnalyzer : DiagnosticAnalyzer
{
    private const string ProblemJson = "application/problem+json";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.ProducesClobbersProblemDetails);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            // [Produces] is a stock MVC attribute, so this rule only has standing in a project
            // that opted into Trellis problem-details responses.
            if (start.Compilation.GetTypeByMetadataName("Trellis.Asp.TrellisAspOptions") is null) return;

            var producesAttribute = start.Compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.ProducesAttribute");
            if (producesAttribute is null) return;

            start.RegisterSyntaxNodeAction(ctx => AnalyzeAttribute(ctx, producesAttribute), SyntaxKind.Attribute);
        });
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context, INamedTypeSymbol producesAttribute)
    {
        var attribute = (AttributeSyntax)context.Node;

        // Quick syntactic filter before paying for symbol resolution.
        var simpleName = attribute.Name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => null
        };
        if (simpleName is not ("Produces" or "ProducesAttribute")) return;

        var arguments = attribute.ArgumentList?.Arguments;
        if (arguments is not { Count: > 0 }) return;

        if (context.SemanticModel.GetSymbolInfo(attribute).Symbol is not IMethodSymbol constructor) return;
        if (!SymbolEqualityComparer.Default.Equals(constructor.ContainingType, producesAttribute)) return;

        // ProducesAttribute(Type) declares a response type and leaves ContentTypes empty, so it
        // cannot rewrite anything.
        if (constructor.Parameters.Length == 0 ||
            constructor.Parameters[0].Type.SpecialType != SpecialType.System_String) return;

        if (!TryReadMediaTypes(context.SemanticModel, constructor, arguments.Value, out var mediaTypes)) return;

        var firstJsonIndex = mediaTypes.FindIndex(IsJsonFamily);
        if (firstJsonIndex < 0) return;

        var offender = mediaTypes[firstJsonIndex];

        // Reporting the FIRST JSON-family entry is what makes a trailing "application/problem+json"
        // non-exculpatory. Note that this is *not* the same as "first entry in the list": MVC's
        // SelectFormatterUsingAnyAcceptableContentType loops over formatters in the outer loop and
        // acceptable media types in the inner one, so a JSON-family entry anywhere in the list can
        // be claimed by the JSON formatter ahead of an earlier non-JSON entry. Which one wins
        // depends on formatter registration order, which is invisible from here — so any
        // JSON-family entry is reported rather than modelled.
        var consequence = IsProblemJson(offender)
            ? "MVC will write successful ObjectResult responses as application/problem+json"
            : "MVC will write RFC 9457 problem responses as that media type instead of application/problem+json";

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.ProducesClobbersProblemDetails,
            attribute.GetLocation(),
            offender,
            consequence));
    }

    /// <summary>
    /// Reads the media types in the order <c>ProducesAttribute</c> will see them, which is by
    /// constructor parameter rather than by source order — named arguments may be written in any
    /// order. Returns <see langword="false"/> when any entry cannot be read as a compile-time
    /// constant, because position is what the rule reasons about and a partially-known list cannot
    /// support that reasoning.
    /// </summary>
    private static bool TryReadMediaTypes(
        SemanticModel model,
        IMethodSymbol constructor,
        SeparatedSyntaxList<AttributeArgumentSyntax> arguments,
        out List<string> mediaTypes)
    {
        mediaTypes = [];
        var head = new List<string>();
        var tail = new List<string>();
        var positional = 0;

        foreach (var argument in arguments)
        {
            // NameEquals is a property initialiser (`Type = typeof(T)`), not a content type.
            // NameColon is a *named constructor argument* and must still be read.
            if (argument.NameEquals is not null) continue;

            var parameterIndex = argument.NameColon is null
                ? Math.Min(positional++, constructor.Parameters.Length - 1)
                : IndexOfParameter(constructor, argument.NameColon.Name.Identifier.Text);
            if (parameterIndex < 0) return false;

            if (!TryReadStrings(model, argument.Expression, parameterIndex == 0 ? head : tail)) return false;
        }

        if (head.Count == 0) return false;

        mediaTypes.AddRange(head);
        mediaTypes.AddRange(tail);
        return true;
    }

    private static int IndexOfParameter(IMethodSymbol constructor, string name)
    {
        for (var i = 0; i < constructor.Parameters.Length; i++)
            if (constructor.Parameters[i].Name == name) return i;
        return -1;
    }

    /// <summary>
    /// Reads a constant string, or the elements of an explicit array passed for the
    /// <c>params</c> parameter (<c>[Produces("a", new[] { "b" })]</c>).
    /// </summary>
    private static bool TryReadStrings(SemanticModel model, ExpressionSyntax expression, List<string> into)
    {
        var initializer = expression switch
        {
            ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer,
            ArrayCreationExpressionSyntax array => array.Initializer,
            _ => null
        };

        if (initializer is not null)
        {
            foreach (var element in initializer.Expressions)
                if (!TryReadStrings(model, element, into)) return false;
            return true;
        }

        var constant = model.GetConstantValue(expression);
        if (!constant.HasValue || constant.Value is not string value) return false;

        into.Add(value);
        return true;
    }

    private static bool IsProblemJson(string mediaType) =>
        string.Equals(WithoutParameters(mediaType), ProblemJson, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A media type is JSON-family when <c>SystemTextJsonOutputFormatter</c> advertises it, which
    /// is <c>application/json</c>, <c>text/json</c>, and <c>application/*+json</c> — and no wider.
    /// The top-level type matters: <c>text/vnd.foo+json</c> is not a subset of
    /// <c>application/*+json</c>, so the JSON formatter declines it and the clobbering mechanism
    /// this rule describes does not apply.
    /// </summary>
    private static bool IsJsonFamily(string mediaType)
    {
        var withoutParameters = WithoutParameters(mediaType);
        var slash = withoutParameters.IndexOf('/');
        if (slash < 0) return false;

        var type = withoutParameters.Substring(0, slash).Trim();
        var subtype = withoutParameters.Substring(slash + 1).Trim();

        if (type.Equals("text", StringComparison.OrdinalIgnoreCase))
            return subtype.Equals("json", StringComparison.OrdinalIgnoreCase);

        if (!type.Equals("application", StringComparison.OrdinalIgnoreCase)) return false;

        return subtype.Equals("json", StringComparison.OrdinalIgnoreCase)
            || subtype.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static string WithoutParameters(string mediaType)
    {
        var semicolon = mediaType.IndexOf(';');
        return (semicolon < 0 ? mediaType : mediaType.Substring(0, semicolon)).Trim();
    }
}
