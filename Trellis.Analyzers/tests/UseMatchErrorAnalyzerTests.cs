namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class UseMatchErrorAnalyzerTests
{
    [Fact]
    public async Task SwitchOnErrorType_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    switch (result.Error)
                    {
                        case ValidationError ve:
                            HandleValidation(ve);
                            break;
                        case NotFoundError nfe:
                            HandleNotFound(nfe);
                            break;
                    }
                }
                
                private void HandleValidation(ValidationError error) { }
                private void HandleNotFound(NotFoundError error) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseMatchErrorAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseMatchErrorForDiscrimination)
                .WithLocation(11, 9));

        await test.RunAsync();
    }

    [Fact]
    public async Task SwitchExpressionOnErrorType_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public string GetMessage(Result<int> result) =>
                    result.Error switch
                    {
                        ValidationError ve => ve.Message,
                        NotFoundError nfe => nfe.Message,
                        _ => "Unknown error"
                    };
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseMatchErrorAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseMatchErrorForDiscrimination)
                .WithLocation(10, 9));

        await test.RunAsync();
    }

    [Fact]
    public async Task IsPatternOnErrorType_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.Error is ValidationError ve)
                    {
                        HandleValidation(ve);
                    }
                }
                
                private void HandleValidation(ValidationError error) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseMatchErrorAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseMatchErrorForDiscrimination)
                .WithLocation(11, 13));

        await test.RunAsync();
    }

    [Fact]
    public async Task IsPatternOnErrorTypeWithoutCapture_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public bool TestMethod(Result<int> result)
                {
                    return result.Error is ValidationError;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseMatchErrorAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseMatchErrorForDiscrimination)
                .WithLocation(11, 16));

        await test.RunAsync();
    }

    [Fact]
    public async Task UsingMatchError_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    result.MatchError(
                        onSuccess: value => HandleSuccess(value),
                        onValidation: ve => HandleValidation(ve),
                        onNotFound: nfe => HandleNotFound(nfe),
                        onOther: e => HandleOther(e));
                }
                
                private void HandleSuccess(int value) { }
                private void HandleValidation(ValidationError error) { }
                private void HandleNotFound(NotFoundError error) { }
                private void HandleOther(Error error) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseMatchErrorAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task SwitchOnNonErrorType_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(object obj)
                {
                    switch (obj)
                    {
                        case string s:
                            HandleString(s);
                            break;
                        case int i:
                            HandleInt(i);
                            break;
                    }
                }
                
                private void HandleString(string s) { }
                private void HandleInt(int i) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseMatchErrorAnalyzer>(source);
        await test.RunAsync();
    }
}