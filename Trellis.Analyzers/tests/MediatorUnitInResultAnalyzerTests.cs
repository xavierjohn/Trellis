namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class MediatorUnitInResultAnalyzerTests
{
    // Stub Trellis.Unit + Mediator.Unit (and a Mediator marker interface) for the collision tests.
    // TrellisStubSource intentionally omits Unit, so these are supplied per test.
    private const string UnitStubs = """
        namespace Trellis { public readonly struct Unit { } }
        namespace Mediator
        {
            public readonly struct Unit { }
            public interface ICommand<TResponse> { }
        }
        """;

    [Fact]
    public async Task MethodReturnType_ResultOfMediatorUnit_ReportsDiagnostic()
    {
        const string source = """
            public class Handler
            {
                public {|#0:Result<Mediator.Unit>|} Handle() => default;
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<MediatorUnitInResultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MediatorUnitInResult).WithLocation(0));
        test.TestState.Sources.Add(("UnitStubs.cs", UnitStubs));

        await test.RunAsync();
    }

    [Fact]
    public async Task BareUnit_WithUsingMediator_ReportsDiagnostic()
    {
        // The realistic trap: a file-scoped 'using Mediator;' makes bare 'Unit' bind to
        // Mediator.Unit (nearer than the file-level 'using Trellis;'), with no ambiguity error.
        const string source = """
            using Mediator;

            public class Handler
            {
                public {|#0:Result<Unit>|} Handle() => default;
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<MediatorUnitInResultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MediatorUnitInResult).WithLocation(0));
        test.TestState.Sources.Add(("UnitStubs.cs", UnitStubs));

        await test.RunAsync();
    }

    [Fact]
    public async Task CommandInterface_ResultOfMediatorUnit_ReportsDiagnostic()
    {
        const string source = """
            public sealed record DeleteThingCommand : Mediator.ICommand<{|#0:Result<Mediator.Unit>|}>;
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<MediatorUnitInResultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MediatorUnitInResult).WithLocation(0));
        test.TestState.Sources.Add(("UnitStubs.cs", UnitStubs));

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultOfTrellisUnit_NoDiagnostic()
    {
        // The correct form — qualified Trellis.Unit — must not be flagged.
        const string source = """
            public class Handler
            {
                public Result<Trellis.Unit> Handle() => default;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<MediatorUnitInResultAnalyzer>(source);
        test.TestState.Sources.Add(("UnitStubs.cs", UnitStubs));

        await test.RunAsync();
    }

    [Fact]
    public async Task MediatorUnitOutsideResult_NoDiagnostic()
    {
        // ICommand<Mediator.Unit> is the legitimate Mediator void-message marker — not a Result.
        const string source = """
            public sealed record PingCommand : Mediator.ICommand<Mediator.Unit>;
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<MediatorUnitInResultAnalyzer>(source);
        test.TestState.Sources.Add(("UnitStubs.cs", UnitStubs));

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultOfOtherType_NoDiagnostic()
    {
        const string source = """
            public class Handler
            {
                public Result<string> Handle() => default;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<MediatorUnitInResultAnalyzer>(source);
        test.TestState.Sources.Add(("UnitStubs.cs", UnitStubs));

        await test.RunAsync();
    }
}
