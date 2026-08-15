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

    #region Early-return / guard-clause — TRLS003

    [Fact]
    public async Task EarlyReturnGuard_NegatedHasValue_Return_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (!maybe.HasValue)
                        return 0;

                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_HasNoValue_Return_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe.HasNoValue)
                        return 0;

                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_NegatedHasValue_Throw_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (!maybe.HasValue)
                        throw new System.InvalidOperationException();

                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_NegatedHasValue_BlockBodyReturn_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (!maybe.HasValue)
                    {
                        return 0;
                    }

                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_HasValueEqualsFalse_Return_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe.HasValue == false)
                        return 0;

                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_Loop_NegatedHasValue_Continue_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    int total = 0;
                    foreach (var i in new[] { 1, 2 })
                    {
                        if (!maybe.HasValue)
                            continue;

                        total += maybe.Value;
                    }
                    return total;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_AccessInNestedBlock_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (!maybe.HasValue)
                        return 0;

                    {
                        return maybe.Value;
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_UnrelatedCondition_StillReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, bool other)
                {
                    if (other)
                        return 0;

                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_NonExitingGuardBody_StillReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (!maybe.HasValue)
                    {
                    }

                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_ReassignedAfterGuard_StillReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, Maybe<int> other)
                {
                    if (!maybe.HasValue)
                        return 0;

                    maybe = other;
                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_LocalFunctionBody_NotGuardedByEnclosingGuard_StillReportsDiagnostic()
    {
        // The enclosing guard does NOT protect the local function body: Read() is invoked before the
        // guard runs (callFirst branch), so maybe may be empty inside Read.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, bool callFirst)
                {
                    if (callFirst)
                        return Read();

                    if (!maybe.HasValue)
                        return 0;

                    return 0;

                    int Read() => maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_ReassignedInNestedBlock_StillReportsDiagnostic()
    {
        // Reassignment hidden in a nested block between the guard and the access must invalidate the
        // guard: 'other' may be empty.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, Maybe<int> other)
                {
                    if (!maybe.HasValue)
                        return 0;

                    {
                        maybe = other;
                        return maybe.{|#0:Value|};
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_ReassignedViaEmbeddedAssignment_StillReportsDiagnostic()
    {
        // The reassignment is embedded in an expression (not a plain `maybe = other;` statement);
        // it must still invalidate the guard.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, Maybe<int> other)
                {
                    if (!maybe.HasValue)
                        return 0;

                    _ = maybe = other;
                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_GuardBeforeLoop_NoReassignment_NoDiagnostic()
    {
        // Common safe pattern: guard before a loop, value never reassigned inside the loop.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (!maybe.HasValue)
                        return 0;

                    int total = 0;
                    foreach (var i in new[] { 1, 2 })
                    {
                        total += maybe.Value;
                    }
                    return total;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_GuardBeforeLoop_ReassignedInLoop_StillReportsDiagnostic()
    {
        // The value is reassigned later in the loop body, so the loop back-edge makes the access
        // unsafe on the second iteration even though the reassignment is textually after it.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, Maybe<int> other)
                {
                    if (!maybe.HasValue)
                        return 0;

                    int total = 0;
                    foreach (var i in new[] { 1, 2 })
                    {
                        total += maybe.{|#0:Value|};
                        maybe = other;
                    }
                    return total;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_ReassignedReceiverChainPrefix_StillReportsDiagnostic()
    {
        // Reassigning a prefix of the receiver chain (the holder) defeats a guard on holder.Opt.
        const string source = """
            public class TestClass
            {
                public sealed class Holder { public Maybe<int> Opt; }

                public int TestMethod(Holder holder, Holder other)
                {
                    if (!holder.Opt.HasValue)
                        return 0;

                    holder = other;
                    return holder.Opt.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_ReassignedParenthesizedReceiverPrefix_StillReportsDiagnostic()
    {
        // Parentheses around the receiver chain must not hide the prefix write.
        const string source = """
            public class TestClass
            {
                public sealed class Holder { public Maybe<int> Opt; }

                public int TestMethod(Holder holder, Holder other)
                {
                    if (!holder.Opt.HasValue)
                        return 0;

                    holder = other;
                    return (holder.Opt).{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_ReassignedViaOutArgument_StillReportsDiagnostic()
    {
        // An out argument rewrites the receiver, defeating the guard.
        const string source = """
            public class TestClass
            {
                private static void Reset(out Maybe<int> m) => m = default;

                public int TestMethod(Maybe<int> maybe)
                {
                    if (!maybe.HasValue)
                        return 0;

                    Reset(out maybe);
                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_ReassignedViaTupleDeconstruction_StillReportsDiagnostic()
    {
        // Tuple deconstruction rewrites the receiver, defeating the guard.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, Maybe<int> other)
                {
                    if (!maybe.HasValue)
                        return 0;

                    int x;
                    (maybe, x) = (other, 0);
                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_ParenthesizedCondition_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if ((!(maybe.HasValue)))
                        return 0;

                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_LiteralOnLeftEquality_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (false == maybe.HasValue)
                        return 0;

                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_HasValueNotEqualTrue_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe.HasValue != true)
                        return 0;

                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_Loop_NegatedHasValue_Break_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    int total = 0;
                    foreach (var i in new[] { 1, 2 })
                    {
                        if (!maybe.HasValue)
                            break;

                        total += maybe.Value;
                    }
                    return total;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_TupleWriteToOtherVariables_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (!maybe.HasValue)
                        return 0;

                    int a, b;
                    (a, b) = (1, 2);
                    return maybe.Value + a + b;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_UnrelatedBooleanEquality_StillReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, bool flag)
                {
                    if (flag == true)
                        return 0;

                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_DifferentReceiverHasValueEquality_StillReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, Maybe<int> other)
                {
                    if (other.HasValue == false)
                        return 0;

                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_HasValueComparedToDefaultLiteral_StillReportsDiagnostic()
    {
        // `== default` is not recognized as a boolean-literal comparison, so the guard is conservatively
        // not honored and the access is still reported.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe.HasValue == default)
                        return 0;

                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_NegatedNonMaybeCondition_StillReportsDiagnostic()
    {
        // Negating a non-Maybe condition is not a presence guard; the access is still reported.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, bool flag)
                {
                    if (!flag)
                        return 0;

                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_GuardInsideLoop_ReassignedLaterInLoop_NoDiagnostic()
    {
        // The guard is INSIDE the loop, so it re-runs every iteration before the access; a
        // reassignment later in the same loop body is re-checked on the next iteration and must NOT
        // invalidate the guard. (The loop-carried scan only covers loops that enclose the access but
        // not the guard; here the guard's block is the loop body, so the loop is not scanned.)
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, Maybe<int> other)
                {
                    int total = 0;
                    foreach (var i in new[] { 1, 2 })
                    {
                        if (!maybe.HasValue)
                            continue;

                        total += maybe.Value;
                        maybe = other;
                    }
                    return total;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_ReassignedViaRefLocalAlias_StillReportsDiagnostic()
    {
        // A writable ref-local alias of the receiver can rewrite it, defeating the guard.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, Maybe<int> other)
                {
                    if (!maybe.HasValue)
                        return 0;

                    ref var alias = ref maybe;
                    alias = other;
                    return maybe.{|#0:Value|};
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeMaybeValueAccess).WithLocation(0));
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_RefReadonlyAlias_NoDiagnostic()
    {
        // A `ref readonly` alias cannot rewrite the receiver, so the guard stays valid.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (!maybe.HasValue)
                        return 0;

                    ref readonly var alias = ref maybe;
                    _ = alias;
                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_RefAliasOfUnrelatedVariable_NoDiagnostic()
    {
        // A writable ref-local that aliases a different variable does not touch the receiver.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (!maybe.HasValue)
                        return 0;

                    int x = 0;
                    ref var r = ref x;
                    r = 5;
                    return maybe.Value + x;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    #endregion

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

    #region Property-pattern guard — TRLS003

    [Fact]
    public async Task PropertyPatternGuard_ThenBranch_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe is { HasValue: true })
                        return maybe.Value;

                    return 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task PropertyPatternGuard_HasNoValueFalse_ThenBranch_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe is { HasNoValue: false })
                        return maybe.Value;

                    return 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task NegatedPropertyPatternGuard_ElseBranch_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe is not { HasValue: true })
                        return 0;
                    else
                        return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task NegatedPropertyPatternGuard_EarlyReturn_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe is not { HasValue: true })
                        return 0;

                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task PropertyPatternGuard_HasValueFalse_EarlyReturn_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe is { HasValue: false })
                        return 0;

                    return maybe.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task PropertyPatternGuard_Ternary_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                    => maybe is { HasValue: true } ? maybe.Value : 0;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task PropertyPatternGuard_ShortCircuitAnd_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public bool TestMethod(Maybe<int> maybe)
                    => maybe is { HasValue: true } && maybe.Value > 0;
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task PropertyPatternGuard_WrongBranch_ReportsDiagnostic()
    {
        // The pattern asserts a value in the then-branch, so the else-branch access is unguarded.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe is { HasValue: true })
                        return 0;

                    return maybe.{|#0:Value|};
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
    public async Task PropertyPatternGuard_OnDifferentReceiver_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe, Maybe<int> other)
                {
                    if (other is { HasValue: true })
                        return maybe.{|#0:Value|};

                    return 0;
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
    public async Task PropertyPatternGuard_WithAdditionalSubpattern_ReportsDiagnostic()
    {
        // A multi-subpattern clause is not decidable in both directions, so the analyzer stays
        // conservative and keeps reporting rather than silently trusting a partial match.
        const string source = """
            public class TestClass
            {
                public int TestMethod(Maybe<int> maybe)
                {
                    if (maybe is not { HasValue: true, Value: > 0 })
                        return 0;

                    return maybe.{|#0:Value|};
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