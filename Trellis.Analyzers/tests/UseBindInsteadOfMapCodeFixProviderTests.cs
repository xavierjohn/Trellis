namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for UseBindInsteadOfMapCodeFixProvider (TRLS002).
/// Verifies that Map is correctly replaced with Bind when the lambda returns a Result.
/// </summary>
public class UseBindInsteadOfMapCodeFixProviderTests
{
    [Fact]
    public async Task Map_WithResultReturningLambda_ReplacedWithBind()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var nested = result.{|#0:Map|}(x => Validate(x));
                }

                private Result<int> Validate(int x) =>
                    x > 0 ? Result.Ok(x) : Result.Fail<int>(Error.Validation("Must be positive"));
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var nested = result.Bind(x => Validate(x));
                }

                private Result<int> Validate(int x) =>
                    x > 0 ? Result.Ok(x) : Result.Fail<int>(Error.Validation("Must be positive"));
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseBindInsteadOfMapAnalyzer, UseBindInsteadOfMapCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseBindInsteadOfMap).WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task MapAsync_WithTaskResultReturningLambda_ReplacedWithBindAsync()
    {
        const string source = """
            public class TestClass
            {
                public async Task TestMethod(Result<int> result)
                {
                    var nested = await result.{|#0:MapAsync|}(x => ValidateAsync(x));
                }

                private Task<Result<int>> ValidateAsync(int x) =>
                    Task.FromResult(x > 0 ? Result.Ok(x) : Result.Fail<int>(Error.Validation("Must be positive")));
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public async Task TestMethod(Result<int> result)
                {
                    var nested = await result.BindAsync(x => ValidateAsync(x));
                }

                private Task<Result<int>> ValidateAsync(int x) =>
                    Task.FromResult(x > 0 ? Result.Ok(x) : Result.Fail<int>(Error.Validation("Must be positive")));
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseBindInsteadOfMapAnalyzer, UseBindInsteadOfMapCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseBindInsteadOfMap).WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task Map_WithMethodGroup_ReplacedWithBind()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var nested = result.{|#0:Map|}(Validate);
                }

                private Result<int> Validate(int x) =>
                    x > 0 ? Result.Ok(x) : Result.Fail<int>(Error.Validation("Must be positive"));
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var nested = result.Bind(Validate);
                }

                private Result<int> Validate(int x) =>
                    x > 0 ? Result.Ok(x) : Result.Fail<int>(Error.Validation("Must be positive"));
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseBindInsteadOfMapAnalyzer, UseBindInsteadOfMapCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseBindInsteadOfMap).WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task Map_WithComments_PreservesTrivia()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    // Validate the result
                    var nested = result.{|#0:Map|}(x => Validate(x)); // Should use Bind
                }

                private Result<int> Validate(int x) =>
                    x > 0 ? Result.Ok(x) : Result.Fail<int>(Error.Validation("Must be positive"));
            }
            """;

        const string fixedSource = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    // Validate the result
                    var nested = result.Bind(x => Validate(x)); // Should use Bind
                }

                private Result<int> Validate(int x) =>
                    x > 0 ? Result.Ok(x) : Result.Fail<int>(Error.Validation("Must be positive"));
            }
            """;

        var test = CodeFixTestHelper.CreateCodeFixTest<UseBindInsteadOfMapAnalyzer, UseBindInsteadOfMapCodeFixProvider>(
            source,
            fixedSource,
            CodeFixTestHelper.Diagnostic(DiagnosticDescriptors.UseBindInsteadOfMap).WithLocation(0));

        await test.RunAsync();
    }
}