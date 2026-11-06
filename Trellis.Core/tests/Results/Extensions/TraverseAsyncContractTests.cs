namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using System.Reflection;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Pins one contract across the non-indexed async <c>TraverseAsync</c> (first failure wins) and
/// <c>TraverseAllAsync</c> (every failure accumulates) overloads: selectors run one at a time in source order, the
/// source is enumerated lazily and always disposed, a selector's exception escapes unwrapped, the cancellation token is
/// the caller's and is observed between items by the library itself (not by a selector that throws), and the span
/// reports the outcome.
/// </summary>
/// <remarks>
/// Each theory runs one scenario through a small adapter per overload, so an overload that breaks the rule fails by
/// name. The completeness test fails when an overload exists that has no adapter.
/// </remarks>
public class TraverseAsyncContractTests
{
    private static readonly Error First = new Error.Conflict(Resource: null, Code: "first") { Detail = "first" };
    private static readonly Error Second = new Error.Conflict(Resource: null, Code: "second") { Detail = "second" };

    private delegate Task<Result<int>> Step(int item, CancellationToken token);

    private sealed record Outcome(bool IsSuccess, Error? Error, bool Persist, IReadOnlyList<int>? Values);

    private sealed record Form(
        string Method,
        bool TakesToken,
        bool CollectsValues,
        Func<IEnumerable<int>, Step?, CancellationToken, Task<Outcome>> Run)
    {
        public bool AccumulatesFailures => Method == "TraverseAllAsync";
    }

    private sealed class TrackedSource(int count)
    {
        public List<int> Yielded { get; } = [];

        public int Enumerations { get; private set; }

        public int Disposals { get; private set; }

        public IEnumerable<int> Items() => Enumerate();

        private IEnumerable<int> Enumerate()
        {
            Enumerations++;
            try
            {
                for (var item = 1; item <= count; item++)
                {
                    Yielded.Add(item);
                    yield return item;
                }
            }
            finally
            {
                Disposals++;
            }
        }
    }

    private static bool PersistFlag<T>(Result<T> result) => ((IPersistOnFailure)result).PersistOnFailure;

    private static Outcome Of(Result<IReadOnlyList<int>> result) =>
        new(result.IsSuccess, result.IsFailure ? result.Error : null, PersistFlag(result), result.TryGetValue(out var values) ? values : null);

    private static Outcome Of(Result<Unit> result) =>
        new(result.IsSuccess, result.IsFailure ? result.Error : null, PersistFlag(result), null);

    private static Func<int, Task<Result<int>>>? TaskSelector(Step? step) =>
        step is null ? null : item => step(item, default);

    private static Func<int, CancellationToken, Task<Result<int>>>? TaskTokenSelector(Step? step) =>
        step is null ? null : (item, token) => step(item, token);

    private static Func<int, ValueTask<Result<int>>>? ValueTaskSelector(Step? step) =>
        step is null ? null : item => new ValueTask<Result<int>>(step(item, default));

    private static Func<int, CancellationToken, ValueTask<Result<int>>>? ValueTaskTokenSelector(Step? step) =>
        step is null ? null : (item, token) => new ValueTask<Result<int>>(step(item, token));

    private static Func<int, CancellationToken, Task<Result<Unit>>>? UnitSelector(Step? step) =>
        step is null ? null : async (item, token) => (await step(item, token)).Map(_ => default(Unit));

    #region Adapters

    private static readonly Dictionary<string, Form> s_forms = new()
    {
        ["TraverseAsync Task"] = new("TraverseAsync", false, true,
            async (source, step, _) => Of(await source.TraverseAsync(TaskSelector(step)!))),
        ["TraverseAsync Task+token"] = new("TraverseAsync", true, true,
            async (source, step, token) => Of(await source.TraverseAsync(TaskTokenSelector(step)!, token))),
        ["TraverseAsync ValueTask"] = new("TraverseAsync", false, true,
            async (source, step, _) => Of(await source.TraverseAsync(ValueTaskSelector(step)!))),
        ["TraverseAsync ValueTask+token"] = new("TraverseAsync", true, true,
            async (source, step, token) => Of(await source.TraverseAsync(ValueTaskTokenSelector(step)!, token))),
        ["TraverseAsync Unit+token"] = new("TraverseAsync", true, false,
            async (source, step, token) => Of(await source.TraverseAsync<int>(UnitSelector(step)!, token))),

        ["TraverseAllAsync Task"] = new("TraverseAllAsync", false, true,
            async (source, step, _) => Of(await source.TraverseAllAsync(TaskSelector(step)!))),
        ["TraverseAllAsync Task+token"] = new("TraverseAllAsync", true, true,
            async (source, step, token) => Of(await source.TraverseAllAsync(TaskTokenSelector(step)!, token))),
        ["TraverseAllAsync ValueTask"] = new("TraverseAllAsync", false, true,
            async (source, step, _) => Of(await source.TraverseAllAsync(ValueTaskSelector(step)!))),
        ["TraverseAllAsync ValueTask+token"] = new("TraverseAllAsync", true, true,
            async (source, step, token) => Of(await source.TraverseAllAsync(ValueTaskTokenSelector(step)!, token))),
        ["TraverseAllAsync Unit+token"] = new("TraverseAllAsync", true, false,
            async (source, step, token) => Of(await source.TraverseAllAsync<int>(UnitSelector(step)!, token))),
    };

    public static TheoryData<string> AllForms() => new(Keys(_ => true));

    public static TheoryData<string> ValueForms() => new(Keys(form => form.CollectsValues));

    public static TheoryData<string> FirstFailureForms() => new(Keys(form => !form.AccumulatesFailures));

    public static TheoryData<string> AccumulatingForms() => new(Keys(form => form.AccumulatesFailures));

    public static TheoryData<string> TokenForms() => new(Keys(form => form.TakesToken));

    public static TheoryData<string> AccumulatingTokenForms() => new(Keys(form => form.TakesToken && form.AccumulatesFailures));

    public static TheoryData<string, bool> FirstFailureFormsWithPersist() => Cross(Keys(form => !form.AccumulatesFailures));

    public static TheoryData<string, bool, bool> AccumulatingFormsWithIntents()
    {
        var data = new TheoryData<string, bool, bool>();
        foreach (var key in Keys(form => form.AccumulatesFailures))
            foreach (var persistFirst in new[] { true, false })
                foreach (var persistLast in new[] { true, false })
                    data.Add(key, persistFirst, persistLast);

        return data;
    }

    private static List<string> Keys(Func<Form, bool> filter)
    {
        var keys = new List<string>();
        foreach (var pair in s_forms)
            if (filter(pair.Value))
                keys.Add(pair.Key);

        return keys;
    }

    private static TheoryData<string, bool> Cross(List<string> keys)
    {
        var data = new TheoryData<string, bool>();
        foreach (var key in keys)
        {
            data.Add(key, true);
            data.Add(key, false);
        }

        return data;
    }

    private static Task<Outcome> RunWithToken(string form, IEnumerable<int> source, Step? step, CancellationToken token) =>
        s_forms[form].Run(source, step, token);

    private static Task<Outcome> Run(string form, IEnumerable<int> source, Step? step) =>
        RunWithToken(form, source, step, TestContext.Current.CancellationToken);

    private static Task<Result<int>> Ok(int item) => Task.FromResult(Result.Ok(item));

    private static TaskCompletionSource<Result<int>> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    #endregion

    #region Results and enumeration

    [Theory]
    [MemberData(nameof(ValueForms))]
    public async Task Successful_items_are_collected_in_source_order(string form)
    {
        var outcome = await Run(form, new TrackedSource(4).Items(), (item, _) => Ok(item * 10));

        outcome.IsSuccess.Should().BeTrue();
        outcome.Values.Should().Equal([10, 20, 30, 40]);
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task An_empty_source_succeeds_without_calling_the_selector(string form)
    {
        var calls = new List<int>();
        var source = new TrackedSource(0);

        var outcome = await Run(form, source.Items(), (item, _) =>
        {
            calls.Add(item);
            return Ok(item);
        });

        outcome.IsSuccess.Should().BeTrue();
        calls.Should().BeEmpty();
        source.Disposals.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task The_source_is_enumerated_once_and_disposed_on_success(string form)
    {
        var source = new TrackedSource(3);

        await Run(form, source.Items(), (item, _) => Ok(item));

        source.Enumerations.Should().Be(1);
        source.Disposals.Should().Be(1);
        source.Yielded.Should().Equal([1, 2, 3]);
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task The_next_selector_does_not_start_until_the_pending_one_completes(string form)
    {
        var gate = Gate();
        var calls = new List<int>();
        var source = new TrackedSource(3);

        var all = Run(form, source.Items(), (item, _) =>
        {
            calls.Add(item);
            return item == 1 ? gate.Task : Ok(item);
        });

        all.IsCompleted.Should().BeFalse("the first selector has not completed yet");
        calls.Should().Equal([1]);
        source.Yielded.Should().Equal([1]);
        gate.SetResult(Result.Ok(1));
        var outcome = await all;
        outcome.IsSuccess.Should().BeTrue();
        calls.Should().Equal([1, 2, 3]);
    }

    #endregion

    #region Failures

    [Theory]
    [MemberData(nameof(FirstFailureForms))]
    public async Task The_first_failure_stops_the_traversal_before_the_next_item_is_enumerated(string form)
    {
        var calls = new List<int>();
        var source = new TrackedSource(5);

        var outcome = await Run(form, source.Items(), (item, _) =>
        {
            calls.Add(item);
            return Task.FromResult(item == 2 ? Result.Fail<int>(First) : Result.Ok(item));
        });

        outcome.IsSuccess.Should().BeFalse();
        outcome.Error.Should().BeSameAs(First);
        calls.Should().Equal([1, 2]);
        source.Yielded.Should().Equal([1, 2]);
        source.Disposals.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(FirstFailureFormsWithPersist))]
    public async Task The_first_failure_keeps_its_own_commit_intent(string form, bool persisted)
    {
        var outcome = await Run(form, new TrackedSource(3).Items(), (item, _) =>
            Task.FromResult(item == 2
                ? persisted ? Result.FailAfterCommit<int>(First) : Result.Fail<int>(First)
                : Result.Ok(item)));

        outcome.Error.Should().BeSameAs(First);
        outcome.Persist.Should().Be(persisted);
    }

    [Theory]
    [MemberData(nameof(AccumulatingForms))]
    public async Task Every_item_is_visited_and_the_failures_are_combined_in_source_order(string form)
    {
        var calls = new List<int>();
        var source = new TrackedSource(4);

        var outcome = await Run(form, source.Items(), (item, _) =>
        {
            calls.Add(item);
            return Task.FromResult(item switch
            {
                2 => Result.Fail<int>(First),
                4 => Result.Fail<int>(Second),
                _ => Result.Ok(item),
            });
        });

        outcome.IsSuccess.Should().BeFalse();
        outcome.Error.Should().BeOfType<Error.Aggregate>().Which.Errors.Items.Should().Equal([First, Second]);
        calls.Should().Equal([1, 2, 3, 4]);
        source.Disposals.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(AccumulatingForms))]
    public async Task A_single_failure_is_returned_without_an_aggregate_wrapper(string form)
    {
        var outcome = await Run(form, new TrackedSource(3).Items(), (item, _) =>
            Task.FromResult(item == 2 ? Result.Fail<int>(First) : Result.Ok(item)));

        outcome.Error.Should().BeSameAs(First);
    }

    [Theory]
    [MemberData(nameof(AccumulatingFormsWithIntents))]
    public async Task The_combined_failure_asks_for_a_commit_when_any_item_does(string form, bool persistFirst, bool persistLast)
    {
        var outcome = await Run(form, new TrackedSource(3).Items(), (item, _) =>
            Task.FromResult(item switch
            {
                1 => persistFirst ? Result.FailAfterCommit<int>(First) : Result.Fail<int>(First),
                3 => persistLast ? Result.FailAfterCommit<int>(Second) : Result.Fail<int>(Second),
                _ => Result.Ok(item),
            }));

        outcome.IsSuccess.Should().BeFalse();
        outcome.Persist.Should().Be(persistFirst || persistLast);
    }

    #endregion

    #region Exceptions

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_faulted_selector_escapes_unwrapped_and_the_source_is_disposed(string form)
    {
        var calls = new List<int>();
        var source = new TrackedSource(5);

        Func<Task> act = () => Run(form, source.Items(), (item, _) =>
        {
            calls.Add(item);
            return item == 2 ? Task.FromException<Result<int>>(new InvalidOperationException("selector")) : Ok(item);
        });

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("selector");
        calls.Should().Equal([1, 2]);
        source.Disposals.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_selector_that_throws_escapes_unwrapped_and_the_source_is_disposed(string form)
    {
        var calls = new List<int>();
        var source = new TrackedSource(5);

        Func<Task> act = () => Run(form, source.Items(), (item, _) =>
        {
            calls.Add(item);
            return item == 2 ? throw new InvalidOperationException("selector") : Ok(item);
        });

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("selector");
        calls.Should().Equal([1, 2]);
        source.Disposals.Should().Be(1);
    }

    #endregion

    #region Cancellation

    [Theory]
    [MemberData(nameof(TokenForms))]
    public async Task A_token_canceled_before_the_start_cancels_without_calling_the_selector(string form)
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var calls = new List<int>();
        var source = new TrackedSource(3);

        Func<Task> act = () => RunWithToken(form, source.Items(), (item, _) =>
        {
            calls.Add(item);
            return Ok(item);
        }, cts.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.CancellationToken.Should().Be(cts.Token);
        calls.Should().BeEmpty();
        source.Disposals.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(TokenForms))]
    public async Task A_selector_that_cancels_the_token_and_returns_Ok_stops_the_traversal_before_the_next_item(string form)
    {
        using var cts = new CancellationTokenSource();
        var calls = new List<int>();
        var source = new TrackedSource(5);

        Func<Task> act = () => RunWithToken(form, source.Items(), (item, _) =>
        {
            calls.Add(item);
            if (item == 2)
                cts.Cancel();

            return Ok(item);
        }, cts.Token);

        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.CancellationToken.Should().Be(cts.Token);
        calls.Should().Equal([1, 2]);
        source.Disposals.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(TokenForms))]
    public async Task The_selector_receives_the_callers_token(string form)
    {
        using var cts = new CancellationTokenSource();
        var seen = new List<CancellationToken>();

        await RunWithToken(form, new TrackedSource(3).Items(), (item, token) =>
        {
            seen.Add(token);
            return Ok(item);
        }, cts.Token);

        seen.Should().HaveCount(3).And.OnlyContain(token => token == cts.Token);
    }

    [Theory]
    [MemberData(nameof(TokenForms))]
    public async Task Cancellation_is_observed_between_items_so_the_last_selector_canceling_does_not_discard_the_result(string form)
    {
        using var cts = new CancellationTokenSource();

        var outcome = await RunWithToken(form, new TrackedSource(3).Items(), (item, _) =>
        {
            if (item == 3)
                cts.Cancel();

            return Ok(item);
        }, cts.Token);

        outcome.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AccumulatingTokenForms))]
    public async Task An_accumulated_failure_is_discarded_when_the_token_is_canceled_before_the_next_item(string form)
    {
        using var cts = new CancellationTokenSource();
        var calls = new List<int>();

        Func<Task> act = () => RunWithToken(form, new TrackedSource(3).Items(), (item, _) =>
        {
            calls.Add(item);
            cts.Cancel();
            return Task.FromResult(Result.Fail<int>(First));
        }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Equal([1]);
    }

    #endregion

    #region Guards

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_null_source_is_rejected(string form)
    {
        Func<Task> act = () => Run(form, null!, (item, _) => Ok(item));

        await act.ShouldRejectNullAsync("source");
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_null_selector_is_rejected_before_the_source_is_enumerated(string form)
    {
        var source = new TrackedSource(3);

        Func<Task> act = () => Run(form, source.Items(), null);

        await act.ShouldRejectNullAsync("selector");
        source.Enumerations.Should().Be(0);
    }

    #endregion

    #region Tracing

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_successful_traversal_records_one_span_named_after_the_operator(string form)
    {
        using var activityTest = new ActivityTestHelper();

        await Run(form, new TrackedSource(3).Items(), (item, _) => Ok(item));

        var activity = activityTest.CapturedActivities.Should().ContainSingle(span => span.DisplayName == s_forms[form].Method).Which;
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_failed_traversal_records_one_error_span(string form)
    {
        using var activityTest = new ActivityTestHelper();
        var failed = Result.Fail<int>(First);

        await Run(form, new TrackedSource(3).Items(), (item, _) => item == 2 ? Task.FromResult(failed) : Ok(item));

        var activity = activityTest.CapturedActivities.Should().ContainSingle(span => span.DisplayName == s_forms[form].Method).Which;
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }

    #endregion

    #region Completeness

    [Fact]
    public void Every_non_indexed_async_Traverse_overload_has_an_adapter()
    {
        var published = new Dictionary<string, int>();
        CountPublished(typeof(TraverseExtensions), "TraverseAsync", published);
        CountPublished(typeof(TraverseAllExtensions), "TraverseAllAsync", published);

        var adapted = new Dictionary<string, int>();
        foreach (var form in s_forms.Values)
        {
            adapted.TryGetValue(form.Method, out var count);
            adapted[form.Method] = count + 1;
        }

        adapted.Should().BeEquivalentTo(published);
    }

    private static void CountPublished(Type type, string name, Dictionary<string, int> counts)
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (method.Name != name || TakesIndexedSelector(method))
                continue;

            counts.TryGetValue(name, out var count);
            counts[name] = count + 1;
        }
    }

    private static bool TakesIndexedSelector(MethodInfo method) =>
        method.GetParameters()[1].ParameterType.GetGenericArguments().Contains(typeof(int));

    #endregion
}
