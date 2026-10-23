namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Testing;

public class Net10ReferenceParityTests
{
    [Theory]
    [MemberData(nameof(ReasonCodeGuardTestCases.Net10Bindings), MemberType = typeof(ReasonCodeGuardTestCases))]
    public async Task Compilation_Net10References_GuardExpressions_PreserveSdkBindingsAndDiagnostics(
        string expression, bool fails, string? expectedMethod, ArgumentKind? expectedArgumentKind,
        OperationKind? expectedValueKind, bool? collectionIsImplicit, int spreadCount, int arrayCount) =>
        await VerifyGuardSourceAsync($$"""
            Error.Unexpected error = {{expression}};
            CustomerId id = new(Guid.NewGuid().ToString("N"));
            _ = default(Result<int>);
            """, fails, expectedMethod, expectedArgumentKind, expectedValueKind, collectionIsImplicit, spreadCount, arrayCount);

    [Fact]
    public async Task Compilation_Net10References_SingleWriteLocal_ReportsExactReasonDiagnostic() =>
        await VerifyGuardSourceAsync(ReasonCodeGuardTestCases.SingleWriteLocal, fails: true);

    [Fact]
    public async Task Compilation_Net10References_MutableCollectionSpread_RemainsUnknown() =>
        await VerifyGuardSourceAsync(ReasonCodeGuardTestCases.MutableCollectionSpread, fails: false,
            "string.Concat(params System.ReadOnlySpan<string?>)", ArgumentKind.Explicit,
            OperationKind.Conversion, collectionIsImplicit: false, spreadCount: 1, arrayCount: 1);

    [Fact]
    public async Task AnalyzerHelper_Net10References_FrameworkApis_CompileWithoutShims()
    {
        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<PerIncidentReasonCodeAnalyzer>("""
            public class FrameworkProbe
            {
                public static string Format() => string.Concat(
                    (ReadOnlySpan<string?>)["incident-", Guid.CreateVersion7().ToString("N")]);
            }
            """);

        test.ReferenceAssemblies.Should().BeSameAs(ReferenceAssemblies.Net.Net100);
        await test.RunAsync();
    }

    [Fact]
    public async Task CodeFixHelper_Net10References_FrameworkApis_CompileBeforeAndAfterFix()
    {
        const string source = """
            public class FrameworkProbe
            {
                public void Format(Result<int> result)
                {
                    var nested = result.{|#0:Map|}(static value => Result.Ok(string.Concat(
                        (ReadOnlySpan<string?>)["incident-", value.ToString(), Guid.CreateVersion7().ToString("N")])));
                }
            }
            """;
        const string fixedSource = """
            public class FrameworkProbe
            {
                public void Format(Result<int> result)
                {
                    var nested = result.Bind(static value => Result.Ok(string.Concat(
                        (ReadOnlySpan<string?>)["incident-", value.ToString(), Guid.CreateVersion7().ToString("N")])));
                }
            }
            """;
        var test = CodeFixTestHelper.CreateCodeFixTest<UseBindInsteadOfMapAnalyzer, UseBindInsteadOfMapCodeFixProvider>(
            source, fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseBindInsteadOfMap).WithLocation(0));

        test.ReferenceAssemblies.Should().BeSameAs(ReferenceAssemblies.Net.Net100);
        await test.RunAsync();
    }

    private static async Task VerifyGuardSourceAsync(
        string statements, bool fails, string? expectedMethod = null, ArgumentKind? expectedArgumentKind = null,
        OperationKind? expectedValueKind = null, bool? collectionIsImplicit = null, int spreadCount = 0, int arrayCount = 0)
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
        AssertPlatformBinding(operations, expectedMethod, expectedArgumentKind, expectedValueKind,
            collectionIsImplicit, spreadCount, arrayCount);

        var diagnostics = await compilation.WithAnalyzers(
            [new PerIncidentReasonCodeAnalyzer(), new DefaultResultOrMaybeAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancellationToken);
        if (!fails)
        {
            diagnostics.Should().BeEmpty();
            return;
        }

        var diagnostic = diagnostics.Should().ContainSingle().Which;
        diagnostic.Id.Should().Be("TRLS067");
        diagnostic.Severity.Should().Be(DiagnosticSeverity.Error);
        diagnostic.Location.SourceTree.Should().BeSameAs(tree);
        var error = operations.OfType<IObjectCreationOperation>()
            .Single(operation => operation.Constructor?.ContainingType.ToDisplayString() == "Trellis.Error.Unexpected");
        var reason = error.Arguments.Single(argument => argument.Parameter?.Name == "Code");
        diagnostic.Location.SourceSpan.Should().Be(reason.Value.Syntax.Span);
    }

    private static void AssertPlatformBinding(
        IOperation[] operations, string? expectedMethod, ArgumentKind? expectedArgumentKind,
        OperationKind? expectedValueKind, bool? collectionIsImplicit, int spreadCount, int arrayCount)
    {
        var stringification = operations.OfType<IInvocationOperation>()
            .Where(operation => operation.TargetMethod.Name == "Concat"
                || operation.TargetMethod.ContainingType.ToDisplayString() == "System.Convert")
            .ToArray();
        if (expectedMethod is null)
        {
            stringification.Should().BeEmpty();
        }
        else
        {
            var invocation = stringification.Should().ContainSingle().Which;
            invocation.TargetMethod.ToDisplayString().Should().Be(expectedMethod);
            invocation.TargetMethod.ContainingAssembly.Name.Should().Be("System.Runtime");
            invocation.TargetMethod.ContainingAssembly.Identity.Version.Major.Should().Be(10);
            var argument = invocation.Arguments.Should().ContainSingle().Which;
            expectedArgumentKind.Should().NotBeNull();
            expectedValueKind.Should().NotBeNull();
            argument.ArgumentKind.Should().Be(expectedArgumentKind!.Value);
            argument.Value.Kind.Should().Be(expectedValueKind!.Value);
            invocation.TargetMethod.Parameters.Single().IsParamsCollection.Should().Be(collectionIsImplicit.HasValue);
        }

        var collections = operations.OfType<ICollectionExpressionOperation>().ToArray();
        if (collectionIsImplicit is { } isImplicit)
            collections.Should().ContainSingle().Which.IsImplicit.Should().Be(isImplicit);
        else
            collections.Should().BeEmpty();

        var spreads = operations.OfType<ISpreadOperation>().ToArray();
        spreads.Should().HaveCount(spreadCount);
        spreads.Should().NotContain(spread => spread.ElementConversion.IsUserDefined);
        operations.OfType<IArrayCreationOperation>().Should().HaveCount(arrayCount);
    }

    private static IEnumerable<IOperation> Walk(IOperation operation)
    {
        yield return operation;
        foreach (var child in operation.ChildOperations)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }
}