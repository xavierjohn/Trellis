namespace Trellis.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzer that detects Result&lt;Result&lt;T&gt;&gt; double wrapping in type declarations,
/// variable assignments, and factory method calls.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResultDoubleWrappingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [DiagnosticDescriptors.ResultDoubleWrapping];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeTypeDeclaration,
            SyntaxKind.VariableDeclaration,
            SyntaxKind.PropertyDeclaration,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.Parameter);

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        ITypeSymbol? typeSymbol = context.Node switch
        {
            VariableDeclarationSyntax variable => context.SemanticModel.GetTypeInfo(variable.Type).Type,
            PropertyDeclarationSyntax property => context.SemanticModel.GetTypeInfo(property.Type).Type,
            MethodDeclarationSyntax method => context.SemanticModel.GetTypeInfo(method.ReturnType).Type,
            ParameterSyntax parameter when parameter.Type != null => context.SemanticModel.GetTypeInfo(parameter.Type).Type,
            _ => null
        };

        if (typeSymbol == null)
            return;

        if (typeSymbol.IsDoubleWrappedResult(out var innerType))
        {
            var location = context.Node switch
            {
                VariableDeclarationSyntax v => v.Type.GetLocation(),
                PropertyDeclarationSyntax p => p.Type.GetLocation(),
                MethodDeclarationSyntax m => m.ReturnType.GetLocation(),
                ParameterSyntax param when param.Type != null => param.Type.GetLocation(),
                _ => context.Node.GetLocation()
            };

            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.ResultDoubleWrapping,
                location,
                innerType);

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check for Result.Ok(result) or Result.Fail(result)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodSymbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol == null)
            return;

        // Check if it's Result.Ok or Result.Fail
        if (!IsResultFactoryMethod(methodSymbol))
            return;

        // Check if the argument is already a Result
        if (invocation.ArgumentList.Arguments.Count != 1)
            return;

        var argument = invocation.ArgumentList.Arguments[0];
        var argumentType = context.SemanticModel.GetTypeInfo(argument.Expression).Type;

        if (argumentType is INamedTypeSymbol { TypeArguments.Length: 1 } resultType &&
            argumentType.IsResultType())
        {
            var innerType = resultType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.ResultDoubleWrapping,
                argument.GetLocation(),
                innerType);

            context.ReportDiagnostic(diagnostic);
        }
    }

    // Check if method is Result.Ok or Result.Fail
    private static bool IsResultFactoryMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ContainingType?.Name != "Result")
            return false;

        if (methodSymbol.ContainingType.ContainingNamespace?.ToDisplayString() != "Trellis")
            return false;

        return methodSymbol.Name is "Ok" or "Fail";
    }
}