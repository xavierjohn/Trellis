namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// Tests for <see cref="DefaultResultOrMaybeAnalyzer"/> (TRLS019) — flags explicit
/// <c>default(Result&lt;T&gt;)</c> and <c>default(Maybe&lt;T&gt;)</c>
/// per ADR-002 §3.5.1.
/// </summary>
public class DefaultResultOrMaybeAnalyzerTests
{
    [Fact]
    public async Task Default_of_GenericResult_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<int> Run()
                {
                    return default(Result<int>);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<DefaultResultOrMaybeAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.DefaultResultOrMaybe)
                .WithLocation(11, 16)
                .WithArguments("Result<int>", "Result.Ok(...) or Result.Fail<T>(...)"));

        await test.RunAsync();
    }

    [Fact]
    public async Task Default_of_Maybe_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Maybe<int> Run()
                {
                    return default(Maybe<int>);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<DefaultResultOrMaybeAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.DefaultResultOrMaybe)
                .WithLocation(11, 16)
                .WithArguments("Maybe<int>", "Maybe<T>.None or Maybe.From(...)"));

        await test.RunAsync();
    }

    [Fact]
    public async Task TargetTyped_Default_ResultReturn_ReportsDiagnostic()
    {
        // 'return default;' inferred to Result<int>.
        const string source = """
            public class TestClass
            {
                public Result<int> Run()
                {
                    return default;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<DefaultResultOrMaybeAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.DefaultResultOrMaybe)
                .WithLocation(11, 16)
                .WithArguments("Result<int>", "Result.Ok(...) or Result.Fail<T>(...)"));

        await test.RunAsync();
    }

    [Fact]
    public async Task DefaultBangSuppressed_StillReportsDiagnostic()
    {
        // 'default!' bypasses nullable warnings but the underlying default value remains.
        const string source = """
            public class TestClass
            {
                public Result<string> Run()
                {
                    return default!;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<DefaultResultOrMaybeAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.DefaultResultOrMaybe)
                .WithLocation(11, 16)
                .WithArguments("Result<string>", "Result.Ok(...) or Result.Fail<T>(...)"));

        await test.RunAsync();
    }

    [Fact]
    public async Task LocalDefaultAssignment_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int Run()
                {
                    Result<int> r = default;
                    return r.GetValueOrDefault(0);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<DefaultResultOrMaybeAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.DefaultResultOrMaybe)
                .WithLocation(11, 25)
                .WithArguments("Result<int>", "Result.Ok(...) or Result.Fail<T>(...)"));

        await test.RunAsync();
    }

    [Fact]
    public async Task DefaultOfUnrelatedStruct_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int Run()
                {
                    var x = default(int);
                    return x;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<DefaultResultOrMaybeAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ResultOk_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<int> Run() => Result.Ok(42);
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<DefaultResultOrMaybeAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task MaybeNone_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Maybe<int> Run() => Maybe<int>.None;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<DefaultResultOrMaybeAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task DefaultInsideNameof_NoDiagnostic()
    {
        // nameof(default(...)) is illegal; cover the broader "default token isn't an expression" case
        // via typeof(Result<int>) which is a different operation kind.
        const string source = """
            public class TestClass
            {
                public string Run() => typeof(Result<int>).Name;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<DefaultResultOrMaybeAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task SuppressMessageAttribute_SilencesDiagnostic()
    {
        const string source = """
            using System.Diagnostics.CodeAnalysis;

            public class TestClass
            {
                [SuppressMessage("Trellis", "TRLS019", Justification = "Sentinel for testing.")]
                public Result<int> Run()
                {
                    return default;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<DefaultResultOrMaybeAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task PragmaDisable_SilencesDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<int> Run()
                {
            #pragma warning disable TRLS019
                    return default;
            #pragma warning restore TRLS019
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<DefaultResultOrMaybeAnalyzer>(source);
        await test.RunAsync();
    }
}