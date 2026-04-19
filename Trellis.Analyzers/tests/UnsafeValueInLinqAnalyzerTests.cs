namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for UnsafeValueInLinqAnalyzer (TRLS018).
/// Verifies that .Value access in LINQ without prior filter is detected.
/// </summary>
public class UnsafeValueInLinqAnalyzerTests
{
    [Fact]
    public async Task Select_ResultValue_WithoutWhere_ReportsDiagnostic()
    {
        const string source = """
            using System.Linq;
            using System.Collections.Generic;

            public class TestClass
            {
                public void TestMethod(List<Result<int>> results)
                {
                    var values = results.Select(r => r.Value);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeValueInLinq)
                .WithArguments("Result.Value", "IsSuccess")
                .WithLocation(14, 44));

        await test.RunAsync();
    }

    [Fact]
    public async Task Select_MaybeValue_WithoutWhere_ReportsDiagnostic()
    {
        const string source = """
            using System.Linq;
            using System.Collections.Generic;

            public class TestClass
            {
                public void TestMethod(List<Maybe<int>> maybes)
                {
                    var values = maybes.Select(m => m.Value);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeValueInLinq)
                .WithArguments("Maybe.Value", "HasValue")
                .WithLocation(14, 43));

        await test.RunAsync();
    }

    [Fact]
    public async Task Select_ResultValue_WithWhereIsSuccess_NoDiagnostic()
    {
        const string source = """
            using System.Linq;
            using System.Collections.Generic;

            public class TestClass
            {
                public void TestMethod(List<Result<int>> results)
                {
                    var values = results.Where(r => r.IsSuccess).Select(r => r.Value);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueInLinqAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Select_MaybeValue_WithWhereHasValue_NoDiagnostic()
    {
        const string source = """
            using System.Linq;
            using System.Collections.Generic;

            public class TestClass
            {
                public void TestMethod(List<Maybe<int>> maybes)
                {
                    var values = maybes.Where(m => m.HasValue).Select(m => m.Value);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueInLinqAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Select_RegularProperty_NoDiagnostic()
    {
        const string source = """
            using System.Linq;
            using System.Collections.Generic;

            public class TestClass
            {
                public void TestMethod(List<Customer> customers)
                {
                    var names = customers.Select(c => c.Name);
                }
            }

            public class Customer
            {
                public string Name { get; set; } = "";
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueInLinqAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Select_NestedResultValue_WithoutWhere_ReportsDiagnostic()
    {
        const string source = """
            using System.Linq;
            using System.Collections.Generic;

            public class TestClass
            {
                public void TestMethod(List<Customer> customers)
                {
                    var addresses = customers.Select(c => c.Address.Value);
                }
            }

            public class Customer
            {
                public Result<string> Address { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeValueInLinq)
                .WithArguments("Result.Value", "IsSuccess")
                .WithLocation(14, 57));

        await test.RunAsync();
    }

    [Fact]
    public async Task Select_ResultValueOnInvocation_WithoutWhere_ReportsDiagnostic()
    {
        const string source = """
            using System.Linq;
            using System.Collections.Generic;

            public class TestClass
            {
                public void TestMethod(List<string> values)
                {
                    var lengths = values.Select(v => GetResult(v).Value);
                }

                private Result<int> GetResult(string value) => Result.Ok(value.Length);
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeValueInLinq)
                .WithArguments("Result.Value", "IsSuccess")
                .WithLocation(14, 55));

        await test.RunAsync();
    }
}