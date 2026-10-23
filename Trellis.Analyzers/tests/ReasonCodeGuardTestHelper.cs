namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

internal static class ReasonCodeGuardTestHelper
{
    internal static async Task<IOperation[]> VerifyAsync(string statements, bool fails)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var parseOptions = AnalyzerTestHelper.Net10ParseOptions;
        var tree = CSharpSyntaxTree.ParseText(ReasonCodeGuardBuildTests.CreateProbeSource(statements),
            parseOptions, path: "Probe.cs", cancellationToken: cancellationToken);
        var globalUsings = CSharpSyntaxTree.ParseText("""
            global using global::System;
            global using global::System.Collections.Generic;
            global using global::System.IO;
            global using global::System.Linq;
            global using global::System.Net.Http;
            global using global::System.Threading;
            global using global::System.Threading.Tasks;
            """, parseOptions, path: "GlobalUsings.g.cs", cancellationToken: cancellationToken);
        var references = await AnalyzerTestHelper.Net10References.ResolveAsync(LanguageNames.CSharp, cancellationToken);
        var compilation = CSharpCompilation.Create("GuardProbe", [tree, globalUsings], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
                {
                    ["TRLS067"] = ReportDiagnostic.Error,
                    ["TRLS019"] = ReportDiagnostic.Suppress
                }));

        compilation.GetDiagnostics(cancellationToken).Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var guid = compilation.GetTypeByMetadataName("System.Guid");
        guid.Should().NotBeNull();
        guid!.ContainingAssembly.Name.Should().Be("System.Runtime");
        guid.ContainingAssembly.Identity.Version.Major.Should().Be(10);
        compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ParamCollectionAttribute").Should().NotBeNull();
        tree.Options.Should().BeOfType<CSharpParseOptions>().Which.LanguageVersion.Should().Be(LanguageVersion.CSharp14);

        var model = compilation.GetSemanticModel(tree);
        var emit = tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Emit");
        var block = model.GetOperation(emit.Body!, cancellationToken);
        block.Should().BeAssignableTo<IBlockOperation>();
        var operations = Walk(block!).ToArray();
        operations.Should().NotContain(operation => operation.Kind == OperationKind.Invalid);

        var diagnostics = await compilation.WithAnalyzers(
            [new PerIncidentReasonCodeAnalyzer(), new DefaultResultOrMaybeAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);
        if (fails)
        {
            var diagnostic = diagnostics.Should().ContainSingle().Which;
            diagnostic.Id.Should().Be("TRLS067");
            diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
            diagnostic.Location.SourceTree.Should().BeSameAs(tree);
            var error = operations.OfType<IObjectCreationOperation>()
                .Single(operation => operation.Constructor?.ContainingType.ToDisplayString() == "Trellis.Error.Unexpected");
            var reason = error.Arguments.Single(argument => argument.Parameter?.Name == "Code");
            diagnostic.Location.SourceSpan.Should().Be(reason.Value.Syntax.Span);
        }
        else
        {
            diagnostics.Should().BeEmpty();
        }

        return operations;
    }

    private static IEnumerable<IOperation> Walk(IOperation operation)
    {
        yield return operation;
        foreach (var child in operation.ChildOperations)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }
}