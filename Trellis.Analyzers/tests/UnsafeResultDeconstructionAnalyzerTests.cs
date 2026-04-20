namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class UnsafeResultDeconstructionAnalyzerTests
{
    [Fact]
    public async Task DiscardSuccessAndError_ReadsValue_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod()
                {
                    Result<int> result = Result.Ok(42);
                    var (_, value, _) = result;
                    return value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeResultDeconstructionAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultDeconstruction)
                .WithLocation(12, 17)
                .WithArguments("value"));

        await test.RunAsync();
    }

    [Fact]
    public async Task DiscardValue_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public bool TestMethod()
                {
                    Result<int> result = Result.Ok(42);
                    var (success, _, _) = result;
                    return success;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeResultDeconstructionAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ValueReadInsideIfSuccess_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod()
                {
                    Result<int> result = Result.Ok(42);
                    var (success, value, _) = result;
                    if (success)
                    {
                        return value;
                    }
                    return -1;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeResultDeconstructionAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ValueReadAfterEarlyReturnOnNotSuccess_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod()
                {
                    Result<int> result = Result.Ok(42);
                    var (success, value, _) = result;
                    if (!success) return -1;
                    return value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeResultDeconstructionAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ValueReadAfterEarlyReturnOnErrorNotNull_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod()
                {
                    Result<int> result = Result.Ok(42);
                    var (_, value, error) = result;
                    if (error is not null) return -1;
                    return value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeResultDeconstructionAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ValueReadInsideIfErrorIsNull_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod()
                {
                    Result<int> result = Result.Ok(42);
                    var (_, value, error) = result;
                    if (error is null)
                    {
                        return value;
                    }
                    return -1;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeResultDeconstructionAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ValueReadUnguarded_AfterCheckOfDifferentVariable_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(bool other)
                {
                    Result<int> result = Result.Ok(42);
                    var (success, value, _) = result;
                    if (other)
                    {
                        return value;
                    }
                    return -1;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeResultDeconstructionAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultDeconstruction)
                .WithLocation(12, 23)
                .WithArguments("value"));

        await test.RunAsync();
    }

    [Fact]
    public async Task ValueNeverRead_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public bool TestMethod()
                {
                    Result<int> result = Result.Ok(42);
                    var (success, value, _) = result;
                    return success;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeResultDeconstructionAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task NonResultDeconstruction_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod()
                {
                    var tuple = (1, 2, 3);
                    var (a, b, c) = tuple;
                    return b;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeResultDeconstructionAnalyzer>(source);
        await test.RunAsync();
    }
}
