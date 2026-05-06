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
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueInLinq)
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
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueInLinq)
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
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueInLinq)
                .WithArguments("Maybe.Value", "HasValue")
                .WithLocation(14, 54));

        await test.RunAsync();
    }

    [Fact]
    public void UnsafeValueInLinq_DescriptorAlias_PointsToSameInstance()
    {
        // N-A-1 (GPT-5.5 meta-review): older versions of Trellis.Analyzers exposed the TRLS013
        // descriptor as `UnsafeValueInLinq`, drifting from the matching `TrellisDiagnosticIds`
        // constant `UnsafeMaybeValueInLinq`. The alias keeps existing custom analyzers and rule-set
        // tooling compiling. This test pins the alias still resolves to the same descriptor.
#pragma warning disable CS0618 // intentionally referencing the obsolete alias
        Assert.Same(DiagnosticDescriptors.UnsafeMaybeValueInLinq, DiagnosticDescriptors.UnsafeValueInLinq);
        Assert.Equal(TrellisDiagnosticIds.UnsafeMaybeValueInLinq, DiagnosticDescriptors.UnsafeValueInLinq.Id);
#pragma warning restore CS0618
    }
}