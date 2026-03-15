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
}