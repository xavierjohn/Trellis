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
                    Result<Result<string>> doubleWrapped;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(11, 9)
                .WithArguments("string"));

        await test.RunAsync();
    }

    [Fact]
    public async Task PropertyDeclaration_DoubleWrapped_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<Result<int>> DoubleWrapped { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(9, 12)
                .WithArguments("int"));

        await test.RunAsync();
    }

    [Fact]
    public async Task MethodReturnType_DoubleWrapped_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<Result<User>> GetUser()
                {
                    return default;
                }
            }

            public class User { }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(9, 12)
                .WithArguments("User"));

        await test.RunAsync();
    }

    [Fact]
    public async Task MethodParameter_DoubleWrapped_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void ProcessResult(Result<Result<string>> result)
                {
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(9, 31)
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
                    ProcessResult(Result.Ok(existingResult));
                }
                
                private void ProcessResult(Result<Result<int>> result) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(12, 33)
                .WithArguments("int"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(15, 32)
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
                    ProcessResult(Result.Ok(result));
                }
                
                private void ProcessResult(Result<Result<string>> doubleWrapped) { }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(16, 33)
                .WithArguments("string"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(19, 32)
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
                public Result<Result<User>> GetUser() => default;
            }

            public class User
            {
                public string Name { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(9, 12)
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
                    var result = GetDoubleWrapped();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(12, 9)
                .WithArguments("int"));

        await test.RunAsync();
    }

    [Fact]
    public async Task MultipleDoubleWrappings_ReportsMultipleDiagnostics()
    {
        const string source = """
            public class TestClass
            {
                public Result<Result<string>> Property { get; set; }
                
                public Result<Result<int>> GetValue() => default;
                
                public void TestMethod(Result<Result<bool>> parameter)
                {
                    Result<Result<double>> local;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<ResultDoubleWrappingAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(9, 12)
                .WithArguments("string"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(11, 12)
                .WithArguments("int"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(13, 28)
                .WithArguments("bool"),
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.ResultDoubleWrapping)
                .WithLocation(15, 9)
                .WithArguments("double"));

        await test.RunAsync();
    }
}