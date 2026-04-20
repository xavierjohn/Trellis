namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class UnsafeResultValuePropertyPatternAnalyzerTests
{
    [Fact]
    public async Task SwitchExpression_ValuePattern_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public string TestMethod()
                {
                    Result<int> result = Result.Ok(42);
                    return result switch
                    {
                        { IsFailure: true } => "fail",
                        { Value: var v } => v.ToString()
                    };
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeResultValuePropertyPatternAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValuePropertyPattern)
                .WithLocation(15, 15));

        await test.RunAsync();
    }

    [Fact]
    public async Task IsPattern_ValueProperty_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod()
                {
                    Result<int> result = Result.Ok(42);
                    if (result is { Value: var v })
                    {
                        return v;
                    }
                    return -1;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeResultValuePropertyPatternAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValuePropertyPattern)
                .WithLocation(12, 25));

        await test.RunAsync();
    }

    [Fact]
    public async Task SwitchExpression_IsSuccessPattern_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public string TestMethod()
                {
                    Result<int> result = Result.Ok(42);
                    return result switch
                    {
                        { IsSuccess: true } => "ok",
                        _ => "fail"
                    };
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeResultValuePropertyPatternAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task SwitchExpression_NonResult_NoDiagnostic()
    {
        const string source = """
            public class Wrapper
            {
                public int Value { get; set; }
            }

            public class TestClass
            {
                public int TestMethod()
                {
                    var w = new Wrapper { Value = 1 };
                    return w switch
                    {
                        { Value: var v } => v,
                        _ => -1
                    };
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeResultValuePropertyPatternAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task IsPattern_NonResult_NoDiagnostic()
    {
        const string source = """
            public class Wrapper
            {
                public int Value { get; set; }
            }

            public class TestClass
            {
                public int TestMethod(Wrapper w)
                {
                    if (w is { Value: var v })
                    {
                        return v;
                    }
                    return -1;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeResultValuePropertyPatternAnalyzer>(source);
        await test.RunAsync();
    }
}
