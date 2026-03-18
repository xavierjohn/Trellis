namespace Trellis.Analyzers;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects HasIndex lambda expressions referencing Maybe&lt;T&gt; properties.
/// MaybeConvention maps Maybe&lt;T&gt; via backing fields, so the CLR property is invisible
/// to EF Core's index builder and the index silently fails to be created.
/// Only activates when the compilation references the Trellis.EntityFrameworkCore assembly.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HasIndexMaybePropertyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.HasIndexMaybeProperty];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            // Only activate when the compilation references Trellis.EntityFrameworkCore
            // by checking for the MaybeConvention type that maps Maybe<T> via backing fields
            var maybeConventionType = compilationContext.Compilation.GetTypeByMetadataName(
                "Trellis.EntityFrameworkCore.MaybeConvention");
            if (maybeConventionType is null)
                return;

            compilationContext.RegisterSyntaxNodeAction(
                AnalyzeInvocation,
                SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "HasIndex")
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        if (!IsEntityTypeBuilder(methodSymbol.ContainingType))
            return;

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.Expression is not LambdaExpressionSyntax lambda)
                continue;

            var lambdaParameter = LambdaSyntaxHelpers.GetLambdaParameter(lambda);
            if (lambdaParameter is null)
                continue;

            var memberAccesses = lambda.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
            foreach (var ma in memberAccesses)
            {
                if (!LambdaSyntaxHelpers.IsAccessOnParameter(ma, lambdaParameter))
                    continue;

                var propSymbol = context.SemanticModel.GetSymbolInfo(ma).Symbol;
                if (propSymbol is not IPropertySymbol propertySymbol)
                    continue;

                if (!propertySymbol.Type.IsMaybeType())
                    continue;

                var propertyName = propertySymbol.Name;
                var backingFieldName = "_" + ToCamelCase(propertyName);

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.HasIndexMaybeProperty,
                    ma.Name.GetLocation(),
                    propertyName,
                    backingFieldName));
            }
        }
    }

    private static bool IsEntityTypeBuilder(INamedTypeSymbol? type)
    {
        while (type is not null)
        {
            if (type.Name == "EntityTypeBuilder" &&
                type.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore.Metadata.Builders")
                return true;
            type = type.BaseType;
        }

        return false;
    }

    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return propertyName;

        var chars = propertyName.ToCharArray();
        chars[0] = char.ToLowerInvariant(chars[0]);

        for (var i = 1; i < chars.Length; i++)
        {
            if (!char.IsUpper(chars[i]))
                break;

            var hasNext = i + 1 < chars.Length;
            if (hasNext && !char.IsUpper(chars[i + 1]))
                break;

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }
}