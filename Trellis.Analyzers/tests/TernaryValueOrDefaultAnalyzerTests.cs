namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class TernaryValueOrDefaultAnalyzerTests
{
    [Fact]
    public async Task TernaryWithIsSuccessAndValue_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result = Result.Success(42);
                    var value = result.IsSuccess ? result.Value : 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<TernaryValueOrDefaultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault)
                .WithLocation(12, 21));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithStringDefault_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<string> result = Result.Success("test");
                    var value = result.IsSuccess ? result.Value : "default";
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<TernaryValueOrDefaultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault)
                .WithLocation(12, 21));

        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithNullDefault_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<User> result = Result.Success(new User());
                    var value = result.IsSuccess ? result.Value : null;
                }
            }

            public class User { }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<TernaryValueOrDefaultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault)
                .WithLocation(12, 21));

        await test.RunAsync();
    }

    [Fact]
    public async Task UsingMatch_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result = Result.Success(42);
                    var value = result.Match(v => v, _ => 0);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<TernaryValueOrDefaultAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithDifferentVariable_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result1 = Result.Success(1);
                    Result<int> result2 = Result.Success(2);
                    var value = result1.IsSuccess ? result2.Value : 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<TernaryValueOrDefaultAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithRepeatedMethodCall_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var value = GetResult().IsSuccess ? GetResult().Value : 0;
                }

                private Result<int> GetResult() => Result.Success(42);
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<TernaryValueOrDefaultAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithoutResultType_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    bool condition = true;
                    var value = condition ? 42 : 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<TernaryValueOrDefaultAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryWithIsFailure_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result = Result.Success(42);
                    var value = result.IsFailure ? 0 : result.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<TernaryValueOrDefaultAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task MultipleTernaries_ReportsMultipleDiagnostics()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result1 = Result.Success(1);
                    Result<string> result2 = Result.Success("test");
                    
                    var value1 = result1.IsSuccess ? result1.Value : 0;
                    var value2 = result2.IsSuccess ? result2.Value : "default";
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<TernaryValueOrDefaultAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault)
                .WithLocation(14, 22),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseFunctionalValueOrDefault)
                .WithLocation(15, 22));

        await test.RunAsync();
    }
}