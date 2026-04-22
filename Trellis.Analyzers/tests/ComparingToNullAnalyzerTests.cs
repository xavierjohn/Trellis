namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

/// <summary>
/// Tests for ComparingToNullAnalyzer (TRLS012).
/// Verifies that comparing Result or Maybe to null is detected.
/// Note: These tests also expect CS0019 compiler error since Result/Maybe are structs.
/// </summary>
public class ComparingToNullAnalyzerTests
{
    [Fact]
    public async Task Result_EqualsNull_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result == null) { }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ComparingToNullAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ComparingToNull)
                .WithArguments("Result", "IsSuccess or IsFailure")
                .WithLocation(11, 13),
            DiagnosticResult.CompilerError("CS0019").WithSpan(11, 13, 11, 27));

        await test.RunAsync();
    }

    [Fact]
    public async Task Result_NotEqualsNull_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result != null) { }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ComparingToNullAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ComparingToNull)
                .WithArguments("Result", "IsSuccess or IsFailure")
                .WithLocation(11, 13),
            DiagnosticResult.CompilerError("CS0019").WithSpan(11, 13, 11, 27));

        await test.RunAsync();
    }

    [Fact]
    public async Task Result_IsNull_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result is null) { }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ComparingToNullAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ComparingToNull)
                .WithArguments("Result", "IsSuccess or IsFailure")
                .WithLocation(11, 13),
            DiagnosticResult.CompilerError("CS9135").WithSpan(11, 23, 11, 27));

        await test.RunAsync();
    }

    [Fact]
    public async Task Result_IsNotNull_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result is not null) { }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ComparingToNullAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ComparingToNull)
                .WithArguments("Result", "IsSuccess or IsFailure")
                .WithLocation(11, 13),
            DiagnosticResult.CompilerError("CS9135").WithSpan(11, 27, 11, 31));

        await test.RunAsync();
    }

    [Fact]
    public async Task Maybe_EqualsNull_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Maybe<int> maybe)
                {
                    if (maybe == null) { }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ComparingToNullAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ComparingToNull)
                .WithArguments("Maybe", "HasValue or HasNoValue")
                .WithLocation(11, 13),
            DiagnosticResult.CompilerError("CS0019").WithSpan(11, 13, 11, 26));

        await test.RunAsync();
    }

    [Fact]
    public async Task Maybe_IsNull_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Maybe<int> maybe)
                {
                    if (maybe is null) { }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ComparingToNullAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ComparingToNull)
                .WithArguments("Maybe", "HasValue or HasNoValue")
                .WithLocation(11, 13),
            DiagnosticResult.CompilerError("CS0037").WithSpan(11, 22, 11, 26));

        await test.RunAsync();
    }

    [Fact]
    public async Task Result_IsSuccess_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsSuccess) { }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ComparingToNullAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Maybe_HasValue_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Maybe<int> maybe)
                {
                    if (maybe.HasValue) { }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ComparingToNullAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task String_EqualsNull_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(string? str)
                {
                    if (str == null) { }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ComparingToNullAnalyzer>(source);
        await test.RunAsync();
    }
}