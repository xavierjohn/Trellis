namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for ThrowInResultChainAnalyzer (TRLS015).
/// Verifies that throw statements inside Result chain lambdas are detected.
/// </summary>
public class ThrowInResultChainAnalyzerTests
{
    [Fact]
    public async Task ThrowStatement_InsideBindLambda_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = Result.Success(1).Bind(x =>
                    {
                        if (x < 0) throw new ArgumentException("Must be positive");
                        return Result.Success(x);
                    });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ThrowInResultChainAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ThrowInResultChain)
                .WithArguments("Bind")
                .WithLocation(13, 24));

        await test.RunAsync();
    }

    [Fact]
    public async Task ThrowStatement_InsideMapLambda_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = Result.Success(1).Map(x =>
                    {
                        if (x < 0) throw new InvalidOperationException();
                        return x * 2;
                    });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ThrowInResultChainAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ThrowInResultChain)
                .WithArguments("Map")
                .WithLocation(13, 24));

        await test.RunAsync();
    }

    [Fact]
    public async Task ThrowStatement_InsideTapLambda_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = Result.Success(1).Tap(x =>
                    {
                        if (x < 0) throw new Exception("Error");
                    });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ThrowInResultChainAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ThrowInResultChain)
                .WithArguments("Tap")
                .WithLocation(13, 24));

        await test.RunAsync();
    }

    [Fact]
    public async Task ThrowExpression_InsideBindLambda_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = Result.Success(1).Bind(x =>
                        x < 0 ? throw new ArgumentException() : Result.Success(x));
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ThrowInResultChainAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ThrowInResultChain)
                .WithArguments("Bind")
                .WithLocation(12, 21));

        await test.RunAsync();
    }

    [Fact]
    public async Task ThrowStatement_OutsideResultChain_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var x = 1;
                    if (x < 0) throw new ArgumentException();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ThrowInResultChainAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ThrowStatement_InsideNonResultMethod_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var list = new System.Collections.Generic.List<int> { 1, 2, 3 };
                    list.ForEach(x =>
                    {
                        if (x < 0) throw new ArgumentException();
                    });
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ThrowInResultChainAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ThrowStatement_InsideTapOnFailureLambda_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var error = Error.Validation("Invalid");
                    var result = Result.Failure<int>(error)
                        .TapOnFailure(err => throw new ApplicationException(err.Detail));
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ThrowInResultChainAnalyzer>(source);
        await test.RunAsync();
    }
}