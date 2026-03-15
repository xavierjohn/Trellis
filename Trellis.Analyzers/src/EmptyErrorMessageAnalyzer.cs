namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects empty or missing error messages in Error factory methods.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EmptyErrorMessageAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> ErrorFactoryMethods =
        ["Validation", "NotFound", "Unauthorized", "Forbidden", "Conflict", "Unexpected"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.EmptyErrorMessage];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        string? methodName;

        // Check for Error.Validation(...), Error.NotFound(...), etc.
        // Also handles type aliases like: using E = Trellis.Error; E.Validation(...)
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            methodName = memberAccess.Name.Identifier.Text;
        }
        // Check for using static Trellis.Error; Validation(...)
        else if (invocation.Expression is IdentifierNameSyntax identifierName)
        {
            methodName = identifierName.Identifier.Text;
        }
        else
        {
            return;
        }

        if (!ErrorFactoryMethods.Contains(methodName))
            return;

        // Verify it's from Trellis via semantic model
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        if (methodSymbol.ContainingType?.ContainingNamespace?.ToDisplayString() != "Trellis")
            return;

        // Check the first argument (message)
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0)
            return; // All Error factory methods require at least one argument

        // Check if first positional argument is empty
        // Note: We only check positional arguments - named arguments in different order is rare
        var firstArg = arguments[0];
        if (firstArg.NameColon != null)
            return; // Skip named argument analysis - complex and rare

        // Check if the message argument is empty
        if (IsEmptyOrWhitespaceString(firstArg.Expression, context.SemanticModel))
        {
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.EmptyErrorMessage,
                firstArg.GetLocation());
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsEmptyOrWhitespaceString(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        // Check for empty string literal ""
        if (expression is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal)
        {
            var value = literal.Token.ValueText;
            return string.IsNullOrWhiteSpace(value);
        }

        // Check for interpolated strings with only literal text segments, e.g. $"" or $"   ".
        if (expression is InterpolatedStringExpressionSyntax interpolatedString)
        {
            if (interpolatedString.Contents.Any(static content => content is InterpolationSyntax))
                return false;

            var value = string.Concat(interpolatedString.Contents
                .OfType<InterpolatedStringTextSyntax>()
                .Select(static content => content.TextToken.ValueText));

            return string.IsNullOrWhiteSpace(value);
        }

        // Check for string.Empty
        if (expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Empty" } memberAccess)
        {
            var typeInfo = semanticModel.GetTypeInfo(memberAccess.Expression);
            return typeInfo.Type?.SpecialType == SpecialType.System_String;
        }

        return false;
    }
}