namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class ResultDoubleWrappingAnalyzerTests
{
    [Fact]
    public async Task VariableDeclaration_DoubleWrapped_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    {|#0:Result<Result<string>>|} doubleWrapped;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(0)
                .WithArguments("string"));

        await test.RunAsync();
    }

    [Fact]
    public async Task PropertyDeclaration_DoubleWrapped_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public {|#0:Result<Result<int>>|} DoubleWrapped { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(0)
                .WithArguments("int"));

        await test.RunAsync();
    }

    [Fact]
    public async Task MethodReturnType_DoubleWrapped_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public {|#0:Result<Result<User>>|} GetUser()
                {
                    return default;
                }
            }

            public class User { }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(0)
                .WithArguments("User"));

        await test.RunAsync();
    }

    [Fact]
    public async Task MethodParameter_DoubleWrapped_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void ProcessResult({|#0:Result<Result<string>>|} result)
                {
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(0)
                .WithArguments("string"));

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultSuccess_WithResultArgument_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<int> existingResult = Result.Ok(42);
                    ProcessResult(Result.Ok({|#0:existingResult|}));
                }
                
                private void ProcessResult({|#1:Result<Result<int>>|} result) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(0)
                .WithArguments("int"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(1)
                .WithArguments("int"));

        await test.RunAsync();
    }

    [Fact]
    public async Task ResultFactoryMethod_WithDoubleWrappedType_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    // This creates a Result<Result<string>> when inferred from the generic parameter
                    var error = Error.Validation("error");
                    var result = Result.Fail<string>(error);
                    
                    // Now wrapping it creates Result<Result<Result<string>>> which contains Result<Result<string>>
                    ProcessResult(Result.Ok({|#0:result|}));
                }
                
                private void ProcessResult({|#1:Result<Result<string>>|} doubleWrapped) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(0)
                .WithArguments("string"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(1)
                .WithArguments("string"));

        await test.RunAsync();
    }

    [Fact]
    public async Task SingleWrappedResult_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<string> GetValue() => Result.Ok("test");
                
                public void TestMethod()
                {
                    Result<int> singleWrapped = Result.Ok(42);
                    var value = GetValue();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ResultDoubleWrappingAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ResultSuccess_WithNonResultArgument_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    var result = Result.Ok(42);
                    var result2 = Result.Ok("test");
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ResultDoubleWrappingAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task NestedGenericType_NotResultDoubleWrapping_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public List<Result<string>> GetResults() => new();
                
                public void TestMethod()
                {
                    var results = new List<Result<int>>();
                }
            }

            public class List<T> { }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<ResultDoubleWrappingAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ComplexType_DoubleWrapped_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public {|#0:Result<Result<User>>|} GetUser() => default;
            }

            public class User
            {
                public string Name { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(0)
                .WithArguments("User"));

        await test.RunAsync();
    }

    [Fact]
    public async Task LocalFunction_DoubleWrapped_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod()
                {
                    Result<Result<int>> GetDoubleWrapped() => default;
                    {|#0:var|} result = GetDoubleWrapped();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(0)
                .WithArguments("int"));

        await test.RunAsync();
    }

    [Fact]
    public async Task MultipleDoubleWrappings_ReportsMultipleDiagnostics()
    {
        const string source = """
            public class TestClass
            {
                public {|#0:Result<Result<string>>|} Property { get; set; }
                
                public {|#1:Result<Result<int>>|} GetValue() => default;
                
                public void TestMethod({|#2:Result<Result<bool>>|} parameter)
                {
                    {|#3:Result<Result<double>>|} local;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(0)
                .WithArguments("string"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(1)
                .WithArguments("int"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(2)
                .WithArguments("bool"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(3)
                .WithArguments("double"));

        await test.RunAsync();
    }
}