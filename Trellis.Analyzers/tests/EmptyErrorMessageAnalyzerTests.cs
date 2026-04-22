namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for EmptyErrorMessageAnalyzer (TRLS011).
/// Verifies that empty or missing error messages are detected.
/// </summary>
public class EmptyErrorMessageAnalyzerTests
{
    [Fact]
    public async Task ErrorValidation_EmptyString_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var error = Error.Validation("");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<EmptyErrorMessageAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.EmptyErrorMessage)
                .WithLocation(11, 38));

        await test.RunAsync();
    }

    [Fact]
    public async Task ErrorValidation_WhitespaceString_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var error = Error.Validation("   ");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<EmptyErrorMessageAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.EmptyErrorMessage)
                .WithLocation(11, 38));

        await test.RunAsync();
    }

    [Fact]
    public async Task ErrorValidation_ValidMessage_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var error = Error.Validation("Email is required");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<EmptyErrorMessageAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ErrorNotFound_EmptyString_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var error = Error.NotFound("");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<EmptyErrorMessageAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.EmptyErrorMessage)
                .WithLocation(11, 36));

        await test.RunAsync();
    }

    [Fact]
    public async Task ErrorNotFound_ValidMessage_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var error = Error.NotFound("Customer not found");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<EmptyErrorMessageAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ErrorValidation_StringEmpty_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var error = Error.Validation(string.Empty);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<EmptyErrorMessageAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.EmptyErrorMessage)
                .WithLocation(11, 38));

        await test.RunAsync();
    }

    [Fact]
    public async Task ErrorValidation_EmptyInterpolatedString_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var error = Error.Validation($"");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<EmptyErrorMessageAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.EmptyErrorMessage)
                .WithLocation(11, 38));

        await test.RunAsync();
    }
}