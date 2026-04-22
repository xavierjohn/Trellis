namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for <see cref="UnsafeValueAccessAnalyzer"/> (TRLS006 — Maybe.Value).
/// The Result-side rules (TRLS003, TRLS004) were removed in v2: <c>Result&lt;T&gt;.Value</c>
/// no longer exists, and <c>Result&lt;T&gt;.Error</c> is nullable so NRT handles unsafe access.
/// </summary>
public class UnsafeValueAccessAnalyzerTests
{
    [Fact]
    public async Task UnguardedMaybeValueAccess_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Maybe<int> maybe)
                {
                    var value = maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(11, 27));

        await test.RunAsync();
    }

    [Fact]
    public async Task GuardedMaybeValueAccess_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Maybe<int> maybe)
                {
                    if (maybe.HasValue)
                    {
                        var value = maybe.Value;
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryGuardedMaybeValueAccess_HasValue_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    return maybe.HasValue ? maybe.Value : 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryGuardedMaybeValueAccess_NegatedHasNoValue_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    return !maybe.HasNoValue ? maybe.Value : 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryGuardedMaybeValueAccess_HasNoValueFalseBranch_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    return maybe.HasNoValue ? 0 : maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryGuardedMaybeValueAccess_HasValueEqualityTrue_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    return maybe.HasValue == true ? maybe.Value : 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryUnguardedMaybeValueAccess_WrongBranch_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    return maybe.HasValue ? 0 : maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(11, 43));

        await test.RunAsync();
    }

    #region Assignment guard — TRLS006

    [Fact]
    public async Task AssignmentGuard_MaybeFromThenValue_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Maybe<DateTime> Timestamp { get; set; }

                public void TestMethod()
                {
                    Timestamp = Maybe<DateTime>.From(DateTime.UtcNow);
                    var value = Timestamp.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task AssignmentGuard_NoAssignment_StillReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Maybe<DateTime> Timestamp { get; set; }

                public void TestMethod()
                {
                    var value = Timestamp.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(13, 31));

        await test.RunAsync();
    }

    [Fact]
    public async Task AssignmentGuard_ReferenceType_StillReportsDiagnostic()
    {
        // Maybe<string>.From(null) returns None, so .Value is unsafe for reference types
        const string source = """
            public class TestClass
            {
                public Maybe<string> Name { get; set; }

                public void TestMethod(string? input)
                {
                    Name = Maybe<string>.From(input);
                    var value = Name.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(14, 26));

        await test.RunAsync();
    }

    [Fact]
    public async Task AssignmentGuard_UnrelatedFromMethod_StillReportsDiagnostic()
    {
        // A From() method on a different type should not suppress the diagnostic
        const string source = """
            public static class SomeFactory
            {
                public static Maybe<DateTime> From(DateTime value) => default;
            }

            public class TestClass
            {
                public Maybe<DateTime> Timestamp { get; set; }

                public void TestMethod()
                {
                    Timestamp = SomeFactory.From(DateTime.UtcNow);
                    var value = Timestamp.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(19, 31));

        await test.RunAsync();
    }

    #endregion

    #region Expression tree short-circuit — TRLS006

    [Fact]
    public async Task ExpressionTreeShortCircuit_HasValueAndValue_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Linq.Expressions;

            public class TestClass
            {
                public Expression<Func<TestEntity, bool>> GetFilter(DateTime cutoff)
                {
                    return e => e.SubmittedAt.HasValue && e.SubmittedAt.Value < cutoff;
                }
            }

            public class TestEntity
            {
                public Maybe<DateTime> SubmittedAt { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ExpressionTreeShortCircuit_ValueWithoutHasValueGuard_StillReportsDiagnostic()
    {
        const string source = """
            using System;
            using System.Linq.Expressions;

            public class TestClass
            {
                public Expression<Func<TestEntity, bool>> GetFilter(DateTime cutoff)
                {
                    return e => e.SubmittedAt.Value < cutoff;
                }
            }

            public class TestEntity
            {
                public Maybe<DateTime> SubmittedAt { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(14, 35));

        await test.RunAsync();
    }

    [Fact]
    public async Task ExpressionTreeShortCircuit_DifferentVariableInAnd_StillReportsDiagnostic()
    {
        // a.HasValue && b.Value — different receivers, should still warn
        const string source = """
            using System;
            using System.Linq.Expressions;

            public class TestClass
            {
                public Expression<Func<TestEntity, bool>> GetFilter(DateTime cutoff)
                {
                    return e => e.SubmittedAt.HasValue && e.ShippedAt.Value < cutoff;
                }
            }

            public class TestEntity
            {
                public Maybe<DateTime> SubmittedAt { get; set; }
                public Maybe<DateTime> ShippedAt { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(14, 59));

        await test.RunAsync();
    }

    #endregion

    #region Reassignment invalidates guards

    [Fact]
    public async Task AssignmentGuard_ReassignmentAfterFrom_StillReportsDiagnostic()
    {
        // Guard is invalidated by reassignment between From() and .Value access
        const string source = """
            public class TestClass
            {
                public Maybe<DateTime> Timestamp { get; set; }

                public void TestMethod(Maybe<DateTime> other)
                {
                    Timestamp = Maybe<DateTime>.From(DateTime.UtcNow);
                    Timestamp = other;
                    var value = Timestamp.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(15, 31));

        await test.RunAsync();
    }

    #endregion
}
