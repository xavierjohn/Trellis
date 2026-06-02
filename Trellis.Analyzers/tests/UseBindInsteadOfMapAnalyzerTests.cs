namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class UseBindInsteadOfMapAnalyzerTests
{
    [Fact]
    public async Task MapWithResultReturningLambda_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var nested = result.{|#0:Map|}(x => Validate(x));
                }

                private Result<int> Validate(int x) => x > 0 ? x : Error.Validation("Must be positive");
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseBindInsteadOfMapAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseBindInsteadOfMap)
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task MapWithNonResultLambda_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var doubled = result.Map(x => x * 2);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseBindInsteadOfMapAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task BindWithResultReturningLambda_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var validated = result.Bind(x => Validate(x));
                }

                private Result<int> Validate(int x) => x > 0 ? x : Error.Validation("Must be positive");
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UseBindInsteadOfMapAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task MapWithMethodGroup_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var nested = result.{|#0:Map|}(Validate);
                }

                private Result<int> Validate(int x) => x > 0 ? x : Error.Validation("Must be positive");
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseBindInsteadOfMapAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseBindInsteadOfMap)
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task MapAsyncWithTaskResultLambda_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public async Task TestMethod(Result<int> result)
                {
                    var nested = await result.{|#0:MapAsync|}(x => ValidateAsync(x));
                }

                private Task<Result<int>> ValidateAsync(int x) =>
                    Task.FromResult<Result<int>>(x > 0 ? x : Error.Validation("Must be positive"));
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UseBindInsteadOfMapAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UseBindInsteadOfMap)
                .WithLocation(0));

        await test.RunAsync();
    }
}