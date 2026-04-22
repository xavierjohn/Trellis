namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for <see cref="UnsafeValueInLinqAnalyzer"/> (TRLS013 — Maybe.Value in LINQ).
/// The Result-side path was removed in v2 along with <c>Result&lt;T&gt;.Value</c>.
/// </summary>
public class UnsafeValueInLinqAnalyzerTests
{
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
    public async Task Select_NestedMaybeValue_WithoutWhere_ReportsDiagnostic()
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
                public Maybe<string> Address { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeValueInLinq)
                .WithArguments("Maybe.Value", "HasValue")
                .WithLocation(14, 57));

        await test.RunAsync();
    }

    [Fact]
    public async Task Select_MaybeValueOnInvocation_WithoutWhere_ReportsDiagnostic()
    {
        const string source = """
            using System.Linq;
            using System.Collections.Generic;

            public class TestClass
            {
                public void TestMethod(List<string> values)
                {
                    var lengths = values.Select(v => GetMaybe(v).Value);
                }

                private Maybe<int> GetMaybe(string value) => Maybe<int>.None;
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeValueInLinq)
                .WithArguments("Maybe.Value", "HasValue")
                .WithLocation(14, 54));

        await test.RunAsync();
    }
}
