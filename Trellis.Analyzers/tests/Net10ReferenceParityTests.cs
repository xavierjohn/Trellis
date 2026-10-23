namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis;
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
        var operations = await ReasonCodeGuardTestHelper.VerifyAsync(statements, fails);
        AssertPlatformBinding(operations, expectedMethod, expectedArgumentKind, expectedValueKind,
            collectionIsImplicit, spreadCount, arrayCount);
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
}