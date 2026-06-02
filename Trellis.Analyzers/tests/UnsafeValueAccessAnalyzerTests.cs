namespace Trellis.Analyzers.Tests;

using Xunit;

/// <summary>
/// Tests for <see cref="UnsafeValueAccessAnalyzer"/> (TRLS003 — Maybe.Value).
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
                    var value = maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

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
                    return maybe.HasValue ? 0 : maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

        await test.RunAsync();
    }

    #region Assignment guard — TRLS003

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
                    var value = Timestamp.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

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
                    var value = Name.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

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
                    var value = Timestamp.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

        await test.RunAsync();
    }

    #endregion

    #region Expression tree short-circuit — TRLS003

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
                    return e => e.SubmittedAt.{|#0:Value|} < cutoff;
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
                .WithLocation(0));

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
                    return e => e.SubmittedAt.HasValue && e.ShippedAt.{|#0:Value|} < cutoff;
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
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task ExpressionTreeShortCircuit_HasValueAndValue_WithLeadingClause_NoDiagnostic()
    {
        // The natural multi-clause specification shape:
        //     status == X && maybe.HasValue && maybe.Value < cutoff
        // C# left-associates this as ((status == X) && maybe.HasValue) && (maybe.Value < cutoff).
        // The .Value access is short-circuit-guarded by HasValue regardless — the analyzer must
        // recognize this, not just the two-clause shape.
        const string source = """
            using System;
            using System.Linq.Expressions;

            public class TestClass
            {
                public Expression<Func<TestEntity, bool>> GetFilter(string status, DateTime cutoff)
                {
                    return e => e.Status == status && e.SubmittedAt.HasValue && e.SubmittedAt.Value < cutoff;
                }
            }

            public class TestEntity
            {
                public string Status { get; set; } = "";
                public Maybe<DateTime> SubmittedAt { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ExpressionTreeShortCircuit_HasValueAndValue_WithMiddleClause_NoDiagnostic()
    {
        // HasValue first, an unrelated middle clause, Value last:
        //     maybe.HasValue && other && maybe.Value < cutoff
        // C# left-associates as ((maybe.HasValue && other) && (maybe.Value < cutoff));
        // because && short-circuits left-to-right, .Value is still guarded by HasValue.
        const string source = """
            using System;
            using System.Linq.Expressions;

            public class TestClass
            {
                public Expression<Func<TestEntity, bool>> GetFilter(string status, DateTime cutoff)
                {
                    return e => e.SubmittedAt.HasValue && e.Status == status && e.SubmittedAt.Value < cutoff;
                }
            }

            public class TestEntity
            {
                public string Status { get; set; } = "";
                public Maybe<DateTime> SubmittedAt { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ExpressionTreeShortCircuit_HasValueAndValue_WithFourClauses_NoDiagnostic()
    {
        // Four-clause chain — verifies recursion through nested && operators.
        //     a && b && maybe.HasValue && maybe.Value < cutoff
        // C# left-associates as (((a && b) && maybe.HasValue) && (maybe.Value < cutoff)).
        const string source = """
            using System;
            using System.Linq.Expressions;

            public class TestClass
            {
                public Expression<Func<TestEntity, bool>> GetFilter(string status, int min, DateTime cutoff)
                {
                    return e => e.Status == status && e.Count > min && e.SubmittedAt.HasValue && e.SubmittedAt.Value < cutoff;
                }
            }

            public class TestEntity
            {
                public string Status { get; set; } = "";
                public int Count { get; set; }
                public Maybe<DateTime> SubmittedAt { get; set; }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ExpressionTreeShortCircuit_ValueBeforeHasValueGuard_StillReportsDiagnostic()
    {
        // Negative case: when .Value appears LEFT of .HasValue in the && chain, the guard is
        // useless (.Value evaluates first). The analyzer must still report.
        //     maybe.Value < cutoff && maybe.HasValue
        const string source = """
            using System;
            using System.Linq.Expressions;

            public class TestClass
            {
                public Expression<Func<TestEntity, bool>> GetFilter(DateTime cutoff)
                {
                    return e => e.SubmittedAt.{|#0:Value|} < cutoff && e.SubmittedAt.HasValue;
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
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task ExpressionTreeShortCircuit_HasValueOrValue_StillReportsDiagnostic()
    {
        // Negative case: `||` short-circuits when its left side is true, but a true `HasValue`
        // does not prevent the right side from being evaluated when `HasValue` is false —
        // exactly the case where `.Value` would throw. So `||` cannot be a guard.
        //     maybe.HasValue || maybe.Value < cutoff
        const string source = """
            using System;
            using System.Linq.Expressions;

            public class TestClass
            {
                public Expression<Func<TestEntity, bool>> GetFilter(DateTime cutoff)
                {
                    return e => e.SubmittedAt.HasValue || e.SubmittedAt.{|#0:Value|} < cutoff;
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
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task ExpressionTreeShortCircuit_DifferentReceiverChainsWithSameMember_StillReportsDiagnostic()
    {
        // Negative case: two same-typed properties on the same parent (Primary, Secondary —
        // both Address). e.Primary.Phone.HasValue does NOT guard e.Secondary.Phone.Value,
        // even though both terminal members resolve to the same `Phone` symbol. The analyzer
        // must compare the full receiver chain structurally.
        const string source = """
            using System;
            using System.Linq.Expressions;

            public class TestClass
            {
                public Expression<Func<TestEntity, bool>> GetFilter()
                {
                    return e => e.Primary.Phone.HasValue && e.Secondary.Phone.{|#0:Value|}.Length > 0;
                }
            }

            public class Address
            {
                public Maybe<string> Phone { get; set; }
            }

            public class TestEntity
            {
                public Address Primary { get; set; } = new();
                public Address Secondary { get; set; } = new();
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

        await test.RunAsync();
    }

    [Fact]
    public async Task InstanceMember_MixedImplicitAndExplicitThis_NoDiagnostic()
    {
        // Mixing `this.X.HasValue && X.Value` (or any combination of implicit/explicit `this`)
        // refers to the same instance member, so the guard must be recognized. The analyzer
        // must not falsely reject equivalent receivers when one side qualifies the member with
        // `this.` and the other does not.
        const string source = """
            using System;

            public class TestClass
            {
                public Maybe<int> Timestamp { get; set; }

                public bool IsPositive()
                {
                    return this.Timestamp.HasValue && Timestamp.Value > 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ExpressionTreeShortCircuit_DifferentInvocationReceivers_StillReportsDiagnostic()
    {
        // Negative case: invocation receivers (`primary.GetPhone()` vs `secondary.GetPhone()`)
        // resolve to the same `GetPhone` method symbol but address different objects. The
        // analyzer cannot structurally compare invocation receivers — it must reject this
        // shape rather than treat them as the same variable.
        const string source = """
            using System;
            using System.Linq.Expressions;

            public class TestClass
            {
                public Expression<Func<TestEntity, bool>> GetFilter()
                {
                    return e => e.Primary.GetPhone().HasValue && e.Secondary.GetPhone().{|#0:Value|}.Length > 0;
                }
            }

            public class Address
            {
                public Maybe<string> GetPhone() => Maybe<string>.None;
            }

            public class TestEntity
            {
                public Address Primary { get; set; } = new();
                public Address Secondary { get; set; } = new();
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

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
                    var value = Timestamp.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess)
                .WithLocation(0));

        await test.RunAsync();
    }

    #endregion
}