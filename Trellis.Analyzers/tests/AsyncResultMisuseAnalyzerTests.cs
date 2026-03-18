namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class AsyncResultMisuseAnalyzerTests
{
    [Fact]
    public async Task TaskResult_AccessingResult_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;

            public class TestClass
            {
                public async Task TestMethod()
                {
                    Task<Result<int>> task = GetValueAsync();
                    var result = task.Result;
                }

                private Task<Result<int>> GetValueAsync() => Task.FromResult(Result.Success(42));
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<AsyncResultMisuseAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.AsyncResultMisuse)
                .WithLocation(14, 27)
                .WithArguments("int"));

        await test.RunAsync();
    }

    [Fact]
    public async Task TaskResult_CallingWait_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;

            public class TestClass
            {
                public void TestMethod()
                {
                    Task<Result<string>> task = GetValueAsync();
                    task.Wait();
                }

                private Task<Result<string>> GetValueAsync() => Task.FromResult(Result.Success("test"));
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<AsyncResultMisuseAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.AsyncResultMisuse)
                .WithLocation(14, 14)
                .WithArguments("string"));

        await test.RunAsync();
    }

    [Fact]
    public async Task ValueTaskResult_AccessingResult_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;

            public class TestClass
            {
                public void TestMethod()
                {
                    ValueTask<Result<User>> task = GetUserAsync();
                    var result = task.Result;
                }

                private ValueTask<Result<User>> GetUserAsync() => new(Result.Success(new User()));
            }

            public class User { }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<AsyncResultMisuseAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.AsyncResultMisuse)
                .WithLocation(14, 27)
                .WithArguments("User"));

        await test.RunAsync();
    }

    [Fact]
    public async Task AwaitingTaskResult_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;

            public class TestClass
            {
                public async Task TestMethod()
                {
                    Task<Result<int>> task = GetValueAsync();
                    var result = await task;
                }

                private Task<Result<int>> GetValueAsync() => Task.FromResult(Result.Success(42));
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<AsyncResultMisuseAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task AccessingResultOnNonAsyncResult_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;

            public class TestClass
            {
                public void TestMethod()
                {
                    Task<int> task = Task.FromResult(42);
                    var result = task.Result;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<AsyncResultMisuseAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task AccessingResultOnSyncResult_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result = Result.Success(42);
                    var value = result.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<AsyncResultMisuseAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ComplexAsyncScenario_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;

            public class TestClass
            {
                public void ProcessUser()
                {
                    var userResult = GetUserAsync().Result;
                }

                private Task<Result<User>> GetUserAsync() => Task.FromResult(Result.Success(new User()));
            }

            public class User { public string Name { get; set; } }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<AsyncResultMisuseAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.AsyncResultMisuse)
                .WithLocation(13, 41)
                .WithArguments("User"));

        await test.RunAsync();
    }

    [Fact]
    public async Task MultipleAsyncMisuses_ReportsMultipleDiagnostics()
    {
        const string source = """
            using System.Threading.Tasks;

            public class TestClass
            {
                public void TestMethod()
                {
                    Task<Result<int>> task1 = GetIntAsync();
                    Task<Result<string>> task2 = GetStringAsync();
                    
                    var result1 = task1.Result;
                    task2.Wait();
                }

                private Task<Result<int>> GetIntAsync() => Task.FromResult(Result.Success(42));
                private Task<Result<string>> GetStringAsync() => Task.FromResult(Result.Success("test"));
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<AsyncResultMisuseAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.AsyncResultMisuse)
                .WithLocation(16, 29)
                .WithArguments("int"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.AsyncResultMisuse)
                .WithLocation(17, 15)
                .WithArguments("string"));

        await test.RunAsync();
    }

    [Fact]
    public async Task GetAwaiterGetResult_OnTaskResultT_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;

            public class TestClass
            {
                public void TestMethod()
                {
                    Task<Result<int>> task = GetValueAsync();
                    var result = task.GetAwaiter().GetResult();
                }

                private Task<Result<int>> GetValueAsync() => Task.FromResult(Result.Success(42));
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<AsyncResultMisuseAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.AsyncResultMisuse)
                .WithLocation(14, 22)
                .WithArguments("int"));

        await test.RunAsync();
    }

    [Fact]
    public async Task GetAwaiterGetResult_OnNonResultTask_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;

            public class TestClass
            {
                public void TestMethod()
                {
                    Task<int> task = Task.FromResult(42);
                    var result = task.GetAwaiter().GetResult();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<AsyncResultMisuseAnalyzer>(source);
        await test.RunAsync();
    }
}