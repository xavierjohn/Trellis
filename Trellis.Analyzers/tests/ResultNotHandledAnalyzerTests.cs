namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class ResultNotHandledAnalyzerTests
{
    [Fact]
    public async Task UnhandledResultMethod_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    GetResult();
                }

                private Result<int> GetResult() => 42;
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultNotHandledAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultNotHandled)
                .WithLocation(11, 9)
                .WithArguments("GetResult"));

        await test.RunAsync();
    }

    [Fact]
    public async Task AssignedResult_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = GetResult();
                }

                private Result<int> GetResult() => 42;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ResultNotHandledAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ChainedResult_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var final = GetResult().Map(x => x * 2);
                }

                private Result<int> GetResult() => 42;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ResultNotHandledAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ReturnedResult_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<int> TestMethod()
                {
                    return GetResult();
                }

                private Result<int> GetResult() => 42;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ResultNotHandledAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task UnhandledAsyncResult_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public async Task TestMethod()
                {
                    await GetResultAsync();
                }

                private Task<Result<int>> GetResultAsync() => Task.FromResult<Result<int>>(42);
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultNotHandledAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultNotHandled)
                .WithLocation(11, 15)
                .WithArguments("GetResultAsync"));

        await test.RunAsync();
    }

    [Fact]
    public async Task AssignedAsyncResult_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public async Task TestMethod()
                {
                    var result = await GetResultAsync();
                }

                private Task<Result<int>> GetResultAsync() => Task.FromResult<Result<int>>(42);
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ResultNotHandledAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task UnhandledAsyncResult_WithConfigureAwait_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public async Task TestMethod()
                {
                    await GetResultAsync().ConfigureAwait(false);
                }

                private Task<Result<int>> GetResultAsync() => Task.FromResult<Result<int>>(42);
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultNotHandledAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultNotHandled)
                .WithLocation(11, 15)
                .WithArguments("GetResultAsync"));

        await test.RunAsync();
    }

    [Fact]
    public async Task UnhandledAsyncResultVariable_WithConfigureAwait_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;

            public class TestClass
            {
                public async Task TestMethod()
                {
                    var resultTask = GetResultAsync();
                    await resultTask.ConfigureAwait(false);
                }

                private Task<Result<int>> GetResultAsync() => Task.FromResult<Result<int>>(42);
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultNotHandledAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultNotHandled)
                .WithLocation(14, 15)
                .WithArguments("resultTask"));

        await test.RunAsync();
    }

    [Fact]
    public async Task ChainedThroughFluentAssertions_SingleLine_NoDiagnostic()
    {
        // Repro for Sonnet 4.6 lab run4 FP-2: claim was that
        //     order.Cancel(...).Should().BeSuccess();
        // triggers TRLS001 but the same call split across lines does not.
        // The analyzer fires on ExpressionStatement whose tail invocation returns Result<T>.
        // .Should() returns FluentAssertions wrapper, .BeSuccess() returns AndConstraint —
        // neither is Result<T>, so no diagnostic should be produced regardless of whitespace.
        const string source = """
            public class FakeAndConstraint { }
            public class FakeResultAssertions
            {
                public FakeAndConstraint BeSuccess() => new();
                public FakeAndConstraint BeFailure() => new();
            }
            public static class FakeShouldExtensions
            {
                public static FakeResultAssertions Should<T>(this Result<T> r) => new();
            }
            public class TestClass
            {
                public void TestMethod()
                {
                    GetResult().Should().BeSuccess();
                }

                private Result<int> GetResult() => 42;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ResultNotHandledAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ChainedThroughFluentAssertions_MultiLine_NoDiagnostic()
    {
        // Companion to the single-line variant above. Whitespace must not affect the analyzer.
        const string source = """
            public class FakeAndConstraint { }
            public class FakeResultAssertions
            {
                public FakeAndConstraint BeSuccess() => new();
                public FakeAndConstraint BeFailure() => new();
            }
            public static class FakeShouldExtensions
            {
                public static FakeResultAssertions Should<T>(this Result<T> r) => new();
            }
            public class TestClass
            {
                public void TestMethod()
                {
                    GetResult()
                        .Should()
                        .BeSuccess();
                }

                private Result<int> GetResult() => 42;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ResultNotHandledAnalyzer>(source);
        await test.RunAsync();
    }
}