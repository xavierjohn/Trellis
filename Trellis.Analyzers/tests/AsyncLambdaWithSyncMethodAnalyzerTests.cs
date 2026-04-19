namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for AsyncLambdaWithSyncMethodAnalyzer (TRLS014).
/// Verifies that async lambdas with sync methods are detected.
/// </summary>
public class AsyncLambdaWithSyncMethodAnalyzerTests
{
    [Fact]
    public async Task Map_WithAsyncLambda_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = Result.Ok(1).Map(async x => await ProcessAsync(x));
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<AsyncLambdaWithSyncMethodAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(11, 35));

        await test.RunAsync();
    }

    [Fact]
    public async Task Tap_WithAsyncLambda_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = Result.Ok(1).Tap(async x => await LogAsync(x));
                }

                private Task LogAsync(int x) => Task.CompletedTask;
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<AsyncLambdaWithSyncMethodAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("TapAsync", "Tap")
                .WithLocation(11, 35));

        await test.RunAsync();
    }

    [Fact]
    public async Task MapAsync_WithAsyncLambda_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public async Task TestMethod()
                {
                    var result = await Result.Ok(1).MapAsync(async x => await ProcessAsync(x));
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<AsyncLambdaWithSyncMethodAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Map_WithSyncLambda_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = Result.Ok(1).Map(x => x * 2);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<AsyncLambdaWithSyncMethodAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Unrelated_TrellisNamespace_MapExtension_NoDiagnostic()
    {
        const string source = """
            namespace Trellis
            {
                public sealed class CustomWrapper
                {
                }

                public static class CustomWrapperExtensions
                {
                    public static CustomWrapper Map(this CustomWrapper wrapper, Func<int, Task<int>> func) => wrapper;
                }
            }

            public class TestClass
            {
                public void TestMethod()
                {
                    var wrapper = new global::TestNamespace.Trellis.CustomWrapper();
                    _ = global::TestNamespace.Trellis.CustomWrapperExtensions.Map(wrapper, async x => await ProcessAsync(x));
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<AsyncLambdaWithSyncMethodAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Lambda_Returning_TaskCompletionSource_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = GetResult().Map(x => new TaskCompletionSource<int>());
                }

                private Result<int> GetResult() => 42;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<AsyncLambdaWithSyncMethodAnalyzer>(source);
        await test.RunAsync();
    }
}