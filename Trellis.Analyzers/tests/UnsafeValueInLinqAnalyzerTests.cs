namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for <see cref="UnsafeValueInLinqAnalyzer"/> (TRLS013 — Maybe.Value in LINQ).
/// The Result-side path was removed in v2 along with <c>Result&lt;T&gt;.Value</c>.
/// </summary>
public class UnsafeValueInLinqAnalyzerTests
{
    [Fact]
    public void MessageFormat_names_MaybeQueryableExtensions_for_IQueryable_path()
    {
        var message = DiagnosticDescriptors.UnsafeMaybeValueInLinq.MessageFormat.ToString(System.Globalization.CultureInfo.InvariantCulture);

        message.Should().Contain(".Where(x => x.HasValue)");
        message.Should().Contain(".Match(...)");
        message.Should().Contain("MaybeQueryableExtensions");
        message.Should().Contain("IQueryable");
        message.Should().Contain("WhereHasValue");
        message.Should().Contain("WhereNone");
        message.Should().Contain("OrderByMaybe");
        message.Should().Contain("ThenByMaybe");
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
    public async Task Where_MaybeEqualsInQueryable_ReportsDiagnostic()
    {
        const string source = """
            using System.Linq;

            public class TestClass
            {
                public void TestMethod(IQueryable<TestEntity> query)
                {
                    var matches = query.Where(e => e.OptionalNumber.Equals(Maybe<int>.From(42)));
                }
            }

            public class TestEntity
            {
                public Maybe<int> OptionalNumber { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MaybeEqualsInQueryable)
                .WithLocation(13, 57));

        await test.RunAsync();
    }

    [Fact]
    public async Task Where_ObjectEqualsMaybeInQueryable_ReportsDiagnostic()
    {
        const string source = """
            using System.Linq;

            public class TestClass
            {
                public void TestMethod(IQueryable<TestEntity> query)
                {
                    var matches = query.Where(e => object.Equals(e.OptionalNumber, Maybe<int>.From(42)));
                }
            }

            public class TestEntity
            {
                public Maybe<int> OptionalNumber { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MaybeEqualsInQueryable)
                .WithLocation(13, 47));

        await test.RunAsync();
    }

    [Fact]
    public async Task QuerySyntax_WhereClauseMaybeEqualsRootedAtIQueryable_ReportsDiagnostic()
    {
        // Round-4 pre-PR review caught this gap: LINQ query syntax has no LambdaExpressionSyntax
        // ancestor, so the original Queryable-context detector missed it. Query expressions are
        // lowered to System.Linq.Queryable calls when rooted at IQueryable<T>, so the
        // EF-translation failure mode is identical to method syntax.
        const string source = """
            using System.Linq;

            public class TestClass
            {
                public void TestMethod(IQueryable<TestEntity> query)
                {
                    var matches = from e in query
                                  where e.OptionalNumber.Equals(Maybe<int>.From(42))
                                  select e;
                }
            }

            public class TestEntity
            {
                public Maybe<int> OptionalNumber { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.MaybeEqualsInQueryable)
                .WithLocation(14, 46));

        await test.RunAsync();
    }

    [Fact]
    public async Task QuerySyntax_WhereClauseMaybeEqualsRootedAtIEnumerable_NoDiagnostic()
    {
        // Negative case: query syntax over IEnumerable<T> is in-memory LINQ, not Queryable.
        // The diagnostic must remain silent — in-memory Maybe<T>.Equals works fine.
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public class TestClass
            {
                public void TestMethod(List<TestEntity> entities)
                {
                    var matches = from e in entities
                                  where e.OptionalNumber.Equals(Maybe<int>.From(42))
                                  select e;
                }
            }

            public class TestEntity
            {
                public Maybe<int> OptionalNumber { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Where_MaybeEqualsInEnumerable_NoDiagnostic()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public class TestClass
            {
                public void TestMethod(List<TestEntity> entities)
                {
                    var matches = entities.Where(e => e.OptionalNumber.Equals(Maybe<int>.From(42)));
                }
            }

            public class TestEntity
            {
                public Maybe<int> OptionalNumber { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueInLinqAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Where_MaybeEqualityOperatorInQueryable_NoDiagnostic()
    {
        const string source = """
            using System.Linq;

            public class TestClass
            {
                public void TestMethod(IQueryable<TestEntity> query)
                {
                    var matches = query.Where(e => e.OptionalNumber == Maybe<int>.From(42));
                }
            }

            public class TestEntity
            {
                public Maybe<int> OptionalNumber { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueInLinqAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Where_HasValueWhereCapturedDelegateInQueryable_ReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Linq;

            public class TestClass
            {
                public void TestMethod(IQueryable<TestEntity> query)
                {
                    Func<int, bool> predicate = value => value > 0;
                    var matches = query.Where(e => e.OptionalNumber.HasValueWhere(predicate));
                }
            }

            public class TestEntity
            {
                public Maybe<int> OptionalNumber { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueInLinqAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.NonInlineHasValueWhereInQueryable)
                .WithLocation(15, 57));

        await test.RunAsync();
    }

    [Fact]
    public async Task Where_HasValueWhereCapturedDelegateInEnumerable_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public class TestClass
            {
                public void TestMethod(List<TestEntity> entities)
                {
                    Func<int, bool> predicate = value => value > 0;
                    var matches = entities.Where(e => e.OptionalNumber.HasValueWhere(predicate));
                }
            }

            public class TestEntity
            {
                public Maybe<int> OptionalNumber { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueInLinqAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Where_HasValueWhereInlineLambdaInQueryable_NoDiagnostic()
    {
        const string source = """
            using System.Linq;

            public class TestClass
            {
                public void TestMethod(IQueryable<TestEntity> query)
                {
                    var matches = query.Where(e => e.OptionalNumber.HasValueWhere(value => value > 0));
                }
            }

            public class TestEntity
            {
                public Maybe<int> OptionalNumber { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueInLinqAnalyzer>(source);
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