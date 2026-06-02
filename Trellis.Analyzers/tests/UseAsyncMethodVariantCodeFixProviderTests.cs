namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for UseAsyncMethodVariantCodeFixProvider (TRLS009).
/// Verifies that sync methods are replaced with async variants only when the await rewrite is safe.
/// </summary>
public class UseAsyncMethodVariantCodeFixProviderTests
{
    [Fact]
    public async Task ReplaceWithAsyncVariantAsync_AsyncMethod_AddsAwait()
    {
        const string source = """
            public class TestClass
            {
                public async Task TestMethod()
                {
                    var result = Result.Ok(1).{|#0:Map|}(async x => await ProcessAsync(x));
                    await Task.CompletedTask;
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public async Task TestMethod()
                {
                    var result = await Result.Ok(1).MapAsync(async x => await ProcessAsync(x));
                    await Task.CompletedTask;
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task ReplaceWithAsyncVariantAsync_TaskMethod_AddsAsyncAndAwait()
    {
        const string source = """
            public class TestClass
            {
                public Task<Result<int>> TestMethod()
                {
                    var result = Result.Ok(1).{|#0:Map|}(async x => await ProcessAsync(x));
                    throw new NotImplementedException();
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public async Task<Result<int>> TestMethod()
                {
                    var result = await Result.Ok(1).MapAsync(async x => await ProcessAsync(x));
                    throw new NotImplementedException();
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisterCodeFixesAsync_VarResultUsedLater_DoesNotOfferFix()
    {
        const string source = """
            public class TestClass
            {
                public async Task<Result<Task<int>>> TestMethod()
                {
                    var result = Result.Ok(1).{|#0:Map|}(async x => await ProcessAsync(x));
                    await Task.CompletedTask;
                    return result;
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisterCodeFixesAsync_NonAsyncLambda_DoesNotOfferFix()
    {
        const string source = """
            public class TestClass
            {
                public async Task TestMethod()
                {
                    var result = Result.Ok(1).{|#0:Map|}(x => ProcessAsync(x));
                    await Task.CompletedTask;
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisterCodeFixesAsync_ChainedInvocation_DoesNotOfferFix()
    {
        const string source = """
            public class TestClass
            {
                public async Task TestMethod()
                {
                    var result = Result.Ok(1)
                        .{|#0:Map|}(async x => await ProcessAsync(x))
                        .Bind(x => Result.Ok(x));
                    await Task.CompletedTask;
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisterCodeFixesAsync_TaskMethodWithInParameter_DoesNotOfferFix()
    {
        const string source = """
            public class TestClass
            {
                public Task<Result<int>> TestMethod(in int value)
                {
                    var result = Result.Ok(1).{|#0:Map|}(async x => await ProcessAsync(x));
                    throw new NotImplementedException();
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisterCodeFixesAsync_AsyncMethodWithDirectReturn_DoesNotOfferFix()
    {
        const string source = """
            public class TestClass
            {
                public async Task<Result<Task<int>>> TestMethod()
                {
                    await Task.CompletedTask;
                    return Result.Ok(1).{|#0:Map|}(async x => await ProcessAsync(x));
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisterCodeFixesAsync_AsyncMethodWithWrappedReturn_DoesNotOfferFix()
    {
        const string source = """
            public class TestClass
            {
                public async Task<int> TestMethod()
                {
                    await Task.CompletedTask;
                    return Wrap(Result.Ok(1).{|#0:Map|}(async x => await ProcessAsync(x)));
                }

                private int Wrap(Result<Task<int>> result) => 1;
                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisterCodeFixesAsync_TaskMethodWithWrappedReturn_DoesNotOfferFix()
    {
        const string source = """
            public class TestClass
            {
                public Task<Result<int>> TestMethod()
                {
                    return Wrap(Result.Ok(1).{|#0:Map|}(async x => await ProcessAsync(x)));
                }

                private Task<Result<int>> Wrap(Result<Task<int>> result) => throw new NotImplementedException();
                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisterCodeFixesAsync_TaskMethodWithOtherTaskReturn_DoesNotOfferFix()
    {
        const string source = """
            public class TestClass
            {
                public Task<Result<int>> TestMethod(bool cached)
                {
                    if (cached)
                        return GetCachedAsync();

                    var result = Result.Ok(1).{|#0:Map|}(async x => await ProcessAsync(x));
                    throw new NotImplementedException();
                }

                private Task<Result<int>> GetCachedAsync() => Task.FromResult(Result.Ok(1));
                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisterCodeFixesAsync_VoidMethod_DoesNotOfferFix()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = Result.Ok(1).{|#0:Map|}(async x => await ProcessAsync(x));
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task RegisterCodeFixesAsync_ValueReturningMethod_DoesNotOfferFix()
    {
        const string source = """
            public class TestClass
            {
                public Result<Task<int>> TestMethod()
                {
                    return Result.Ok(1).{|#0:Map|}(async x => await ProcessAsync(x));
                }

                private Task<int> ProcessAsync(int x) => Task.FromResult(x * 2);
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<AsyncLambdaWithSyncMethodAnalyzer, UseAsyncMethodVariantCodeFixProvider>(
            source,
            source,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseAsyncMethodVariant)
                .WithArguments("MapAsync", "Map")
                .WithLocation(0));

        await test.RunAsync();
    }
}