namespace Trellis.Core.Tests;

using System.Reflection;

/// <summary>
/// <see cref="ResultFailAfterCommitPropagationTests"/> pins operators that <em>project</em> an upstream failure,
/// which carry its commit intent. These tests pin the operators that <em>replace</em> a result with another one:
/// the replacement's commit intent governs, so a persisted original recovered to a plain failure stops asking for
/// a commit, and an ordinary failure replaced by a persisted recovery starts asking for one.
/// </summary>
/// <remarks>
/// Each theory runs one scenario across every overload through a small adapter, so an overload that breaks the
/// rule fails by name. The completeness tests fail when an overload exists that has no adapter.
/// </remarks>
public class ResultFailAfterCommitReplacementTests
{
    private static readonly Error PersistError = new Error.Conflict(Resource: null, Code: "persist.intent") { Detail = "stage me" };
    private static readonly Error PlainError = new Error.Conflict(Resource: null, Code: "plain.intent") { Detail = "drop me" };
    private static readonly Error ReplacementError = new Error.Conflict(Resource: null, Code: "replacement") { Detail = "replacement" };

    private static bool PersistFlag<T>(Result<T> result) => ((IPersistOnFailure)result).PersistOnFailure;

    private static int ValueOf(Result<int> result) => result.TryGetValue(out var value) ? value : -1;

    private sealed record RecoverAdapter(bool HasPredicate, Func<Result<int>, Func<Result<int>>, bool, Task<Result<int>>> Run);

    private sealed record EnsureAdapter(Func<Result<int>, Func<Result<int>>, Task<Result<int>>> Run);

    #region RecoverOnFailure: the recovery result governs

    private static readonly Dictionary<string, RecoverAdapter> s_recover = new()
    {
        ["sync Func<Result>"] = new(false, (s, r, _) => Task.FromResult(s.RecoverOnFailure(() => r()))),
        ["sync Func<Error, Result>"] = new(false, (s, r, _) => Task.FromResult(s.RecoverOnFailure(_ => r()))),
        ["sync predicate, Func<Result>"] = new(true, (s, r, p) => Task.FromResult(s.RecoverOnFailure(_ => p, () => r()))),
        ["sync predicate, Func<Error, Result>"] = new(true, (s, r, p) => Task.FromResult(s.RecoverOnFailure(_ => p, _ => r()))),

        ["Task source, Func<Result>"] = new(false, (s, r, _) => Task.FromResult(s).RecoverOnFailureAsync(() => r())),
        ["Task source, Func<Error, Result>"] = new(false, (s, r, _) => Task.FromResult(s).RecoverOnFailureAsync(_ => r())),
        ["Task source, predicate, Func<Result>"] = new(true, (s, r, p) => Task.FromResult(s).RecoverOnFailureAsync(_ => p, () => r())),
        ["Task source, predicate, Func<Error, Result>"] = new(true, (s, r, p) => Task.FromResult(s).RecoverOnFailureAsync(_ => p, _ => r())),
        ["Task source, Func<Task<Result>>"] = new(false, (s, r, _) => Task.FromResult(s).RecoverOnFailureAsync(() => Task.FromResult(r()))),
        ["Task source, Func<Error, Task<Result>>"] = new(false, (s, r, _) => Task.FromResult(s).RecoverOnFailureAsync(_ => Task.FromResult(r()))),
        ["Task source, predicate, Func<Task<Result>>"] = new(true, (s, r, p) => Task.FromResult(s).RecoverOnFailureAsync(_ => p, () => Task.FromResult(r()))),
        ["Task source, predicate, Func<Error, Task<Result>>"] = new(true, (s, r, p) => Task.FromResult(s).RecoverOnFailureAsync(_ => p, _ => Task.FromResult(r()))),

        ["Result source, Func<Task<Result>>"] = new(false, (s, r, _) => s.RecoverOnFailureAsync(() => Task.FromResult(r()))),
        ["Result source, Func<Error, Task<Result>>"] = new(false, (s, r, _) => s.RecoverOnFailureAsync(_ => Task.FromResult(r()))),
        ["Result source, predicate, Func<Task<Result>>"] = new(true, (s, r, p) => s.RecoverOnFailureAsync(_ => p, () => Task.FromResult(r()))),
        ["Result source, predicate, Func<Error, Task<Result>>"] = new(true, (s, r, p) => s.RecoverOnFailureAsync(_ => p, _ => Task.FromResult(r()))),

        ["ValueTask source, Func<Result>"] = new(false, (s, r, _) => new ValueTask<Result<int>>(s).RecoverOnFailureAsync(() => r()).AsTask()),
        ["ValueTask source, Func<Error, Result>"] = new(false, (s, r, _) => new ValueTask<Result<int>>(s).RecoverOnFailureAsync(_ => r()).AsTask()),
        ["ValueTask source, predicate, Func<Result>"] = new(true, (s, r, p) => new ValueTask<Result<int>>(s).RecoverOnFailureAsync(_ => p, () => r()).AsTask()),
        ["ValueTask source, predicate, Func<Error, Result>"] = new(true, (s, r, p) => new ValueTask<Result<int>>(s).RecoverOnFailureAsync(_ => p, _ => r()).AsTask()),
        ["ValueTask source, Func<ValueTask<Result>>"] = new(false, (s, r, _) => new ValueTask<Result<int>>(s).RecoverOnFailureAsync(() => new ValueTask<Result<int>>(r())).AsTask()),
        ["ValueTask source, Func<Error, ValueTask<Result>>"] = new(false, (s, r, _) => new ValueTask<Result<int>>(s).RecoverOnFailureAsync(_ => new ValueTask<Result<int>>(r())).AsTask()),
        ["ValueTask source, predicate, Func<ValueTask<Result>>"] = new(true, (s, r, p) => new ValueTask<Result<int>>(s).RecoverOnFailureAsync(_ => p, () => new ValueTask<Result<int>>(r())).AsTask()),
        ["ValueTask source, predicate, Func<Error, ValueTask<Result>>"] = new(true, (s, r, p) => new ValueTask<Result<int>>(s).RecoverOnFailureAsync(_ => p, _ => new ValueTask<Result<int>>(r())).AsTask()),
    };

    public static TheoryData<string> RecoverOnFailureOverloads() => new(s_recover.Keys);

    public static TheoryData<string> RecoverOnFailurePredicateOverloads() =>
        new(s_recover.Where(pair => pair.Value.HasPredicate).Select(pair => pair.Key));

    private static Task<Result<int>> Recover(string overload, Result<int> source, Func<Result<int>> replacement, bool predicate = true) =>
        s_recover[overload].Run(source, replacement, predicate);

    [Theory]
    [MemberData(nameof(RecoverOnFailureOverloads))]
    public async Task RecoverOnFailure_persisted_original_recovered_to_success_is_a_success(string overload)
    {
        var recovered = await Recover(overload, Result.FailAfterCommit<int>(PersistError), () => Result.Ok(7));

        recovered.IsSuccess.Should().BeTrue();
        ValueOf(recovered).Should().Be(7);
    }

    [Theory]
    [MemberData(nameof(RecoverOnFailureOverloads))]
    public async Task RecoverOnFailure_persisted_original_recovered_to_a_plain_failure_stops_requesting_a_commit(string overload)
    {
        var recovered = await Recover(overload, Result.FailAfterCommit<int>(PersistError), () => Result.Fail<int>(ReplacementError));

        recovered.IsFailure.Should().BeTrue();
        recovered.Error.Should().BeSameAs(ReplacementError, "the recovery result replaces the original");
        PersistFlag(recovered).Should().BeFalse("replacement governs, unlike projection operators: the replacement is an ordinary failure, so the original's commit intent does not carry over");
    }

    [Theory]
    [MemberData(nameof(RecoverOnFailureOverloads))]
    public async Task RecoverOnFailure_plain_original_recovered_to_a_persisted_failure_starts_requesting_a_commit(string overload)
    {
        var recovered = await Recover(overload, Result.Fail<int>(PlainError), () => Result.FailAfterCommit<int>(ReplacementError));

        recovered.IsFailure.Should().BeTrue();
        recovered.Error.Should().BeSameAs(ReplacementError);
        PersistFlag(recovered).Should().BeTrue("the recovery result's commit intent governs");
    }

    [Theory]
    [MemberData(nameof(RecoverOnFailureOverloads))]
    public async Task RecoverOnFailure_persisted_original_recovered_to_another_persisted_failure_keeps_requesting_a_commit(string overload)
    {
        var recovered = await Recover(overload, Result.FailAfterCommit<int>(PersistError), () => Result.FailAfterCommit<int>(ReplacementError));

        recovered.Error.Should().BeSameAs(ReplacementError);
        PersistFlag(recovered).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(RecoverOnFailureOverloads))]
    public async Task RecoverOnFailure_success_is_returned_unchanged_without_calling_the_recovery(string overload)
    {
        var calls = 0;

        var recovered = await Recover(overload, Result.Ok(3), () =>
        {
            calls++;
            return Result.FailAfterCommit<int>(ReplacementError);
        });

        calls.Should().Be(0);
        recovered.IsSuccess.Should().BeTrue();
        ValueOf(recovered).Should().Be(3);
    }

    [Theory]
    [MemberData(nameof(RecoverOnFailurePredicateOverloads))]
    public async Task RecoverOnFailure_predicate_false_keeps_the_persisted_original_without_calling_the_recovery(string overload)
    {
        var calls = 0;

        var recovered = await Recover(overload, Result.FailAfterCommit<int>(PersistError), () =>
        {
            calls++;
            return Result.Ok(7);
        }, predicate: false);

        calls.Should().Be(0);
        recovered.Error.Should().BeSameAs(PersistError);
        PersistFlag(recovered).Should().BeTrue("a failure that was not recovered keeps its commit intent");
    }

    [Theory]
    [MemberData(nameof(RecoverOnFailurePredicateOverloads))]
    public async Task RecoverOnFailure_predicate_false_keeps_a_plain_original_plain(string overload)
    {
        var recovered = await Recover(overload, Result.Fail<int>(PlainError), () => Result.FailAfterCommit<int>(ReplacementError), predicate: false);

        recovered.Error.Should().BeSameAs(PlainError);
        PersistFlag(recovered).Should().BeFalse();
    }

    [Fact]
    public async Task RecoverOnFailureAsync_unit_persisted_original_recovered_to_a_plain_failure_stops_requesting_a_commit()
    {
        var source = Task.FromResult(Result.FailAfterCommit(PersistError));

        var recovered = await source.RecoverOnFailureAsync(_ => true, () => Task.FromResult(Result.Fail(ReplacementError)));

        recovered.Error.Should().BeSameAs(ReplacementError);
        PersistFlag(recovered).Should().BeFalse();
    }

    [Fact]
    public async Task RecoverOnFailureAsync_unit_plain_original_recovered_to_a_persisted_failure_starts_requesting_a_commit()
    {
        var source = Task.FromResult(Result.Fail(PlainError));

        var recovered = await source.RecoverOnFailureAsync(_ => true, () => Task.FromResult(Result.FailAfterCommit(ReplacementError)));

        recovered.Error.Should().BeSameAs(ReplacementError);
        PersistFlag(recovered).Should().BeTrue();
    }

    [Fact]
    public async Task RecoverOnFailureAsync_unit_success_is_returned_without_calling_the_recovery()
    {
        var calls = 0;

        var recovered = await Task.FromResult(Result.Ok()).RecoverOnFailureAsync(_ => true, () =>
        {
            calls++;
            return Task.FromResult(Result.FailAfterCommit(ReplacementError));
        });

        calls.Should().Be(0);
        recovered.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RecoverOnFailureAsync_unit_predicate_false_keeps_the_persisted_original()
    {
        var source = Task.FromResult(Result.FailAfterCommit(PersistError));

        var recovered = await source.RecoverOnFailureAsync(_ => false, () => Task.FromResult(Result.Ok()));

        recovered.Error.Should().BeSameAs(PersistError);
        PersistFlag(recovered).Should().BeTrue();
    }

    #endregion

    #region Ensure with a Result predicate: the predicate result's intent governs a new failure

    private static readonly Dictionary<string, EnsureAdapter> s_ensure = new()
    {
        ["sync Func<Result>"] = new((s, p) => Task.FromResult(s.Ensure(() => p()))),
        ["sync Func<TValue, Result>"] = new((s, p) => Task.FromResult(s.Ensure(_ => p()))),

        ["Task source, Func<Result>"] = new((s, p) => Task.FromResult(s).EnsureAsync(() => p())),
        ["Task source, Func<TValue, Result>"] = new((s, p) => Task.FromResult(s).EnsureAsync(_ => p())),
        ["Task source, Func<Task<Result>>"] = new((s, p) => Task.FromResult(s).EnsureAsync(() => Task.FromResult(p()))),
        ["Task source, Func<TValue, Task<Result>>"] = new((s, p) => Task.FromResult(s).EnsureAsync(_ => Task.FromResult(p()))),
        ["Result source, Func<Task<Result>>"] = new((s, p) => s.EnsureAsync(() => Task.FromResult(p()))),
        ["Result source, Func<TValue, Task<Result>>"] = new((s, p) => s.EnsureAsync(_ => Task.FromResult(p()))),

        ["ValueTask source, Func<Result>"] = new((s, p) => new ValueTask<Result<int>>(s).EnsureAsync(() => p()).AsTask()),
        ["ValueTask source, Func<TValue, Result>"] = new((s, p) => new ValueTask<Result<int>>(s).EnsureAsync(_ => p()).AsTask()),
        ["ValueTask source, Func<ValueTask<Result>>"] = new((s, p) => new ValueTask<Result<int>>(s).EnsureAsync(() => new ValueTask<Result<int>>(p())).AsTask()),
        ["ValueTask source, Func<TValue, ValueTask<Result>>"] = new((s, p) => new ValueTask<Result<int>>(s).EnsureAsync(_ => new ValueTask<Result<int>>(p())).AsTask()),
        ["Result source, Func<ValueTask<Result>>"] = new((s, p) => s.EnsureAsync(() => new ValueTask<Result<int>>(p())).AsTask()),
        ["Result source, Func<TValue, ValueTask<Result>>"] = new((s, p) => s.EnsureAsync(_ => new ValueTask<Result<int>>(p())).AsTask()),
    };

    public static TheoryData<string> EnsureOverloads() => new(s_ensure.Keys);

    #endregion

    #region Completeness: every overload has an adapter

    [Fact]
    public void Every_generic_RecoverOnFailure_overload_has_an_adapter()
    {
        var overloads = PublicOverloads([typeof(RecoverOnFailureExtensions), typeof(RecoverOnFailureExtensionsAsync)], "RecoverOnFailure", "RecoverOnFailureAsync");

        overloads.Count(method => method.IsGenericMethodDefinition).Should().Be(
            s_recover.Count, "a RecoverOnFailure overload was added or removed; update the adapters above");
        overloads.Count(method => !method.IsGenericMethodDefinition).Should().Be(
            1, "the single Result<Unit> overload is covered by the unit tests above; cover any other non-generic overload too");
    }

    [Fact]
    public void Every_Ensure_overload_taking_a_Result_returning_predicate_has_an_adapter()
    {
        var overloads = PublicOverloads([typeof(EnsureExtensions), typeof(EnsureExtensionsAsync)], "Ensure", "EnsureAsync")
            .Where(method => method.GetParameters().Skip(1).Any(parameter => IsResultReturningDelegate(parameter.ParameterType)));

        overloads.Should().HaveCount(s_ensure.Count, "an Ensure overload with a Result-returning predicate was added or removed; update the adapters above");
    }

    private static List<MethodInfo> PublicOverloads(Type[] types, params string[] names) =>
        types.SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => names.Contains(method.Name))
            .ToList();

    private static bool IsResultReturningDelegate(Type parameterType) =>
        typeof(Delegate).IsAssignableFrom(parameterType) && IsResultShape(parameterType.GetMethod("Invoke")!.ReturnType);

    private static bool IsResultShape(Type type)
    {
        if (!type.IsGenericType)
            return false;

        var definition = type.GetGenericTypeDefinition();
        if (definition == typeof(Result<>))
            return true;

        return (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
            && type.GetGenericArguments()[0] is { IsGenericType: true } inner
            && inner.GetGenericTypeDefinition() == typeof(Result<>);
    }

    #endregion

    #region Ensure with a Result predicate: behaviour

    private static Task<Result<int>> Ensure(string overload, Result<int> source, Func<Result<int>> predicate) =>
        s_ensure[overload].Run(source, predicate);

    [Theory]
    [MemberData(nameof(EnsureOverloads))]
    public async Task Ensure_predicate_returning_a_persisted_failure_makes_the_new_failure_request_a_commit(string overload)
    {
        var ensured = await Ensure(overload, Result.Ok(1), () => Result.FailAfterCommit<int>(PersistError));

        ensured.IsFailure.Should().BeTrue();
        ensured.Error.Should().BeSameAs(PersistError);
        PersistFlag(ensured).Should().BeTrue("the predicate result is the failure that surfaces, with its commit intent");
    }

    [Theory]
    [MemberData(nameof(EnsureOverloads))]
    public async Task Ensure_predicate_returning_a_plain_failure_makes_a_plain_failure(string overload)
    {
        var ensured = await Ensure(overload, Result.Ok(1), () => Result.Fail<int>(PlainError));

        ensured.Error.Should().BeSameAs(PlainError);
        PersistFlag(ensured).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(EnsureOverloads))]
    public async Task Ensure_predicate_returning_success_keeps_the_starting_result(string overload)
    {
        var ensured = await Ensure(overload, Result.Ok(1), () => Result.Ok(99));

        ensured.IsSuccess.Should().BeTrue();
        ValueOf(ensured).Should().Be(1, "only a failure from the predicate matters; its value is discarded");
    }

    [Theory]
    [MemberData(nameof(EnsureOverloads))]
    public async Task Ensure_persisted_upstream_failure_is_kept_without_calling_the_predicate(string overload)
    {
        var calls = 0;

        var ensured = await Ensure(overload, Result.FailAfterCommit<int>(PersistError), () =>
        {
            calls++;
            return Result.Fail<int>(PlainError);
        });

        calls.Should().Be(0);
        ensured.Error.Should().BeSameAs(PersistError);
        PersistFlag(ensured).Should().BeTrue();
    }

    #endregion
}
