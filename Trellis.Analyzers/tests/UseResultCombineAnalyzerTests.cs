namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class UseResultCombineAnalyzerTests
{
    [Fact]
    public async Task MultipleIsSuccessChecks_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result1 = Result.Ok(1);
                    Result<int> result2 = Result.Ok(2);
                    
                    if (result1.IsSuccess && result2.IsSuccess)
                    {
                        var combined = (result1.GetValueOrDefault(0), result2.GetValueOrDefault(0));
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseResultCombineAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseResultCombine)
                .WithLocation(14, 9));

        await test.RunAsync();
    }

    [Fact]
    public async Task ThreeIsSuccessChecks_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result1 = Result.Ok(1);
                    Result<int> result2 = Result.Ok(2);
                    Result<int> result3 = Result.Ok(3);
                    
                    if (result1.IsSuccess && result2.IsSuccess && result3.IsSuccess)
                    {
                        var combined = (result1.GetValueOrDefault(0), result2.GetValueOrDefault(0), result3.GetValueOrDefault(0));
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseResultCombineAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseResultCombine)
                .WithLocation(15, 9));

        await test.RunAsync();
    }

    [Fact]
    public async Task MultipleIsFailureChecks_WithOr_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result1 = Result.Ok(1);
                    Result<int> result2 = Result.Ok(2);

                    if (result1.IsFailure || result2.IsFailure)
                    {
                        var error = result1.IsFailure ? result1.Error : result2.Error;
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseResultCombineAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseResultCombine)
                .WithLocation(14, 9));

        await test.RunAsync();
    }

    [Fact]
    public async Task UsingCombineChaining_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result1 = Result.Ok(1);
                    Result<int> result2 = Result.Ok(2);
                    
                    var combined = result1.Combine(result2);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseResultCombineAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task UsingResultCombine_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result1 = Result.Ok(1);
                    Result<int> result2 = Result.Ok(2);
                    
                    var combined = Result.Combine(result1, result2);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseResultCombineAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task SingleIsSuccessCheck_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result = Result.Ok(1);
                    
                    if (result.IsSuccess)
                    {
                        result.TryGetValue(out var value);
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseResultCombineAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task NonResultBooleanChecks_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    bool condition1 = true;
                    bool condition2 = false;
                    
                    if (condition1 && condition2)
                    {
                        // Do something
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseResultCombineAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task MixedResultAndNonResultChecks_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> result = Result.Ok(1);
                    bool condition = true;
                    
                    if (result.IsSuccess && condition)
                    {
                        result.TryGetValue(out var value);
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseResultCombineAnalyzer>(source);
        await test.RunAsync();
    }
}