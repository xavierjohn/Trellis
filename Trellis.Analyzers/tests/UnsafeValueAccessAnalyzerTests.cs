namespace Trellis.Analyzers.Tests;

using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class UnsafeValueAccessAnalyzerTests
{
    [Fact]
    public async Task UnguardedResultValueAccess_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var value = result.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 28));

        await test.RunAsync();
    }

    [Fact]
    public async Task GuardedResultValueAccess_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsSuccess)
                    {
                        var value = result.Value;
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task GuardedByIsFailureFalse_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (!result.IsFailure)
                    {
                        var value = result.Value;
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task UnguardedResultErrorAccess_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    var error = result.Error;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultErrorAccess)
                .WithLocation(11, 28));

        await test.RunAsync();
    }

    [Fact]
    public async Task GuardedResultErrorAccess_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.IsFailure)
                    {
                        var error = result.Error;
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

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

    [Fact]
    public async Task TernaryGuardedResultValueAccess_IsSuccess_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Result<int> result)
                {
                    return result.IsSuccess ? result.Value : 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryGuardedResultValueAccess_NegatedIsFailure_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Result<int> result)
                {
                    return !result.IsFailure ? result.Value : 0;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TernaryGuardedResultErrorAccess_IsFailure_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public string TestMethod(Result<int> result)
                {
                    return result.IsFailure ? result.Error.Detail : "ok";
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ValueAccessInBindLambda_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    result.Bind(x => Result.Success(x * 2));
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ValueAccessToDifferentResultInBindLambda_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result, Result<int> other)
                {
                    result.Bind(x => Result.Success(other.Value + x));
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(11, 47));

        await test.RunAsync();
    }

    [Fact]
    public async Task ErrorAccessInBindLambda_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<string> TestMethod(Result<int> result)
                {
                    return result.Bind(x => Result.Success(result.Error.Message));
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultErrorAccess)
                .WithLocation(11, 55));

        await test.RunAsync();
    }

    [Fact]
    public async Task ValueAccessInMatchCallback_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Result<int> result)
                {
                    return result.Match(
                        onSuccess: value => value * 2,
                        onFailure: error => 0);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ValueAccessInMatchFailureCallback_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Result<int> result)
                {
                    return result.Match(
                        onSuccess: value => value * 2,
                        onFailure: error => result.Value);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(13, 40));

        await test.RunAsync();
    }

    [Fact]
    public async Task ErrorAccessInMatchSuccessCallback_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public string TestMethod(Result<int> result)
                {
                    return result.Match(
                        onSuccess: value => result.Error.Message,
                        onFailure: error => error.Message);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultErrorAccess)
                .WithLocation(12, 40));

        await test.RunAsync();
    }

    [Fact]
    public async Task ValueAccessInMatchErrorSuccessCallback_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Result<int> result)
                {
                    return result.MatchError(
                        onSuccess: value => result.Value,
                        onValidation: error => 0,
                        onOther: error => 0);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ValueAccessInMatchErrorFailureCallback_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int TestMethod(Result<int> result)
                {
                    return result.MatchError(
                        onSuccess: value => value,
                        onValidation: error => result.Value,
                        onOther: error => 0);
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(13, 43));

        await test.RunAsync();
    }

    [Fact]
    public async Task ErrorAccessInTapOnFailureCallback_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<int> TestMethod(Result<int> result)
                {
                    return result.TapOnFailure(error => Console.WriteLine(result.Error.Message));
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task TryGetValuePattern_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (result.TryGetValue(out var value))
                    {
                        Console.WriteLine(value);
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task NegatedTryGetValue_ErrorAccessInThenBranch_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<string> TestMethod(Result<int> result)
                {
                    if (!result.TryGetValue(out var value))
                    {
                        return Result.Failure<string>(result.Error);
                    }
                    return Result.Success(value.ToString());
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task NegatedTryGetValue_ValueAccessInElseBranch_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (!result.TryGetValue(out var value))
                    {
                        return;
                    }
                    else
                    {
                        Console.WriteLine(result.Value);
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task NegatedTryGetValue_ValueAccessInThenBranch_ReportsDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (!result.TryGetValue(out var value))
                    {
                        Console.WriteLine(result.Value);
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateDiagnosticTest<UnsafeValueAccessAnalyzer>(
            source,
            AnalyzerTestHelper.Diagnostic(DiagnosticDescriptors.UnsafeResultValueAccess)
                .WithLocation(13, 38));

        await test.RunAsync();
    }

    [Fact]
    public async Task ConditionalAccessOnResultItself_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public int? TestMethod(Result<int>? result)
                {
                    return result?.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ConditionalAccessOnWrapper_ValueAccess_NoDiagnostic()
    {
        const string source = """
            public class Wrapper
            {
                public Result<int> Result { get; set; }
            }

            public class TestClass
            {
                public void TestMethod(Wrapper? wrapper)
                {
                    var value = wrapper?.Result.Value;
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task ReversedEquality_TrueEqualsIsSuccess_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public void TestMethod(Result<int> result)
                {
                    if (true == result.IsSuccess)
                    {
                        var value = result.Value;
                    }
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    #region Early-return guard — TRLS003

    [Fact]
    public async Task EarlyReturnGuard_IsFailureReturn_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<string> TestMethod(Result<int> result)
                {
                    if (result.IsFailure) return result.Error;
                    var value = result.Value;
                    return value.ToString();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task EarlyReturnGuard_NegatedIsSuccessReturn_NoDiagnostic()
    {
        const string source = """
            public class TestClass
            {
                public Result<string> TestMethod(Result<int> result)
                {
                    if (!result.IsSuccess) return result.Error;
                    var value = result.Value;
                    return value.ToString();
                }
            }
            """;

        var test = AnalyzerTestHelper.CreateNoDiagnosticTest<UnsafeValueAccessAnalyzer>(source);
        await test.RunAsync();
    }

    #endregion

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

    #endregion
}