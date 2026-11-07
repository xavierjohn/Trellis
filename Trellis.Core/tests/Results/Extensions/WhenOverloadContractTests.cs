namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using System.Reflection;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Pins one contract across every <c>When</c>/<c>Unless</c> overload (sync, <see cref="Result{TValue}"/>-receiver,
/// <see cref="Task{TResult}"/>-receiver and <see cref="ValueTask{TResult}"/>-receiver; predicate and boolean forms): a failure short-circuits before
/// anything is evaluated, the selected branch runs exactly once with the value, the operation's result replaces the
/// source (so its commit intent governs), guards fire before anything is awaited, and the span is named after the
/// synchronous operator.
/// </summary>
/// <remarks>
/// Each theory runs one scenario through a small adapter per overload, so an overload that breaks the rule fails by
/// name. The completeness test fails when an overload exists that has no adapter.
/// </remarks>
public class WhenOverloadContractTests
{
    private static readonly Error SourceError = new Error.Conflict(Resource: null, Code: "source") { Detail = "source" };
    private static readonly Error OperationError = new Error.Conflict(Resource: null, Code: "operation") { Detail = "operation" };

    private enum Shape { Sync, ResultReceiver, TaskReceiver, ValueTaskReceiver }

    private sealed record Op(Func<int, Result<int>>? Sync, Func<int, Task<Result<int>>>? Async)
    {
        public static readonly Op Null = new(null, null);

        public static Op Of(Func<int, Result<int>> operation) => new(operation, value => Task.FromResult(operation(value)));
    }

    private sealed record Overload(
        string Method,
        Shape Shape,
        bool UsesPredicate,
        Func<Task<Result<int>>, Func<int, bool>?, bool, Op, Task<Result<int>>> Run)
    {

        public bool PendingSource => Shape is Shape.TaskReceiver or Shape.ValueTaskReceiver;

        public bool IsWhen => Method.StartsWith("When", StringComparison.Ordinal);

        public string SpanName => IsWhen ? "When" : "Unless";

        public bool Runs(bool flag) => IsWhen ? flag : !flag;
    }

    private static Func<int, ValueTask<Result<int>>>? ToValueTask(Func<int, Task<Result<int>>>? operation) =>
        operation is null ? null : value => new ValueTask<Result<int>>(operation(value));

    #region Adapters

    private static readonly Dictionary<string, Overload> s_overloads = new()
    {
        ["When(predicate)"] = new("When", Shape.Sync, true, async (s, p, _, o) => (await s).When(p!, o.Sync!)),
        ["When(condition)"] = new("When", Shape.Sync, false, async (s, _, c, o) => (await s).When(c, o.Sync!)),
        ["Unless(predicate)"] = new("Unless", Shape.Sync, true, async (s, p, _, o) => (await s).Unless(p!, o.Sync!)),
        ["Unless(condition)"] = new("Unless", Shape.Sync, false, async (s, _, c, o) => (await s).Unless(c, o.Sync!)),

        ["Result.WhenAsync(predicate)"] = new("WhenAsync", Shape.ResultReceiver, true, async (s, p, _, o) => await (await s).WhenAsync(p!, o.Async!)),
        ["Result.WhenAsync(condition)"] = new("WhenAsync", Shape.ResultReceiver, false, async (s, _, c, o) => await (await s).WhenAsync(c, o.Async!)),
        ["Result.UnlessAsync(predicate)"] = new("UnlessAsync", Shape.ResultReceiver, true, async (s, p, _, o) => await (await s).UnlessAsync(p!, o.Async!)),
        ["Result.UnlessAsync(condition)"] = new("UnlessAsync", Shape.ResultReceiver, false, async (s, _, c, o) => await (await s).UnlessAsync(c, o.Async!)),

        ["Task.WhenAsync(predicate)"] = new("WhenAsync", Shape.TaskReceiver, true, (s, p, _, o) => s.WhenAsync(p!, o.Async!)),
        ["Task.WhenAsync(condition)"] = new("WhenAsync", Shape.TaskReceiver, false, (s, _, c, o) => s.WhenAsync(c, o.Async!)),
        ["Task.UnlessAsync(predicate)"] = new("UnlessAsync", Shape.TaskReceiver, true, (s, p, _, o) => s.UnlessAsync(p!, o.Async!)),
        ["Task.UnlessAsync(condition)"] = new("UnlessAsync", Shape.TaskReceiver, false, (s, _, c, o) => s.UnlessAsync(c, o.Async!)),

        ["ValueTask.WhenAsync(predicate)"] = new("WhenAsync", Shape.ValueTaskReceiver, true, (s, p, _, o) => new ValueTask<Result<int>>(s).WhenAsync(p!, ToValueTask(o.Async)!).AsTask()),
        ["ValueTask.WhenAsync(condition)"] = new("WhenAsync", Shape.ValueTaskReceiver, false, (s, _, c, o) => new ValueTask<Result<int>>(s).WhenAsync(c, ToValueTask(o.Async)!).AsTask()),
        ["ValueTask.UnlessAsync(predicate)"] = new("UnlessAsync", Shape.ValueTaskReceiver, true, (s, p, _, o) => new ValueTask<Result<int>>(s).UnlessAsync(p!, ToValueTask(o.Async)!).AsTask()),
        ["ValueTask.UnlessAsync(condition)"] = new("UnlessAsync", Shape.ValueTaskReceiver, false, (s, _, c, o) => new ValueTask<Result<int>>(s).UnlessAsync(c, ToValueTask(o.Async)!).AsTask()),
    };

    public static TheoryData<string> AllOverloads() => new(s_overloads.Keys);

    public static TheoryData<string> PredicateOverloads() => new(Keys(o => o.UsesPredicate));

    public static TheoryData<string> PendingSourceOverloads() => new(Keys(o => o.PendingSource));

    public static TheoryData<string> PendingSourcePredicateOverloads() => new(Keys(o => o.PendingSource && o.UsesPredicate));

    public static TheoryData<string> TaskReceiverOverloads() => new(Keys(o => o.Shape == Shape.TaskReceiver));

    public static TheoryData<string, bool> PredicateOverloadsWithFlag() => Cross(Keys(o => o.UsesPredicate), [true, false]);

    public static TheoryData<string, bool> AllOverloadsWithPersist() => Cross(s_overloads.Keys, [true, false]);

    public static TheoryData<string, bool, bool> AllOverloadsWithFlagAndPersist()
    {
        var data = new TheoryData<string, bool, bool>();
        foreach (var key in s_overloads.Keys)
            foreach (var flag in new[] { true, false })
                foreach (var persisted in new[] { true, false })
                    data.Add(key, flag, persisted);

        return data;
    }

    private static List<string> Keys(Func<Overload, bool> filter)
    {
        var keys = new List<string>();
        foreach (var pair in s_overloads)
            if (filter(pair.Value))
                keys.Add(pair.Key);

        return keys;
    }

    private static TheoryData<string, bool> Cross(IEnumerable<string> keys, bool[] flags)
    {
        var data = new TheoryData<string, bool>();
        foreach (var key in keys)
            foreach (var flag in flags)
                data.Add(key, flag);

        return data;
    }

    private static Task<Result<int>> Call(string name, Task<Result<int>> source, bool flag, Op operation, List<int>? predicateCalls = null)
    {
        var overload = s_overloads[name];
        Func<int, bool> predicate = value =>
        {
            predicateCalls?.Add(value);
            return flag;
        };

        return overload.Run(source, predicate, flag, operation);
    }

    private static Task<Result<int>> Success(int value = 42) => Task.FromResult(Result.Ok(value));

    private static Task<Result<int>> Failure(bool persisted) =>
        Task.FromResult(persisted ? Result.FailAfterCommit<int>(SourceError) : Result.Fail<int>(SourceError));

    private static Result<int> OperationFailure(bool persisted) =>
        persisted ? Result.FailAfterCommit<int>(OperationError) : Result.Fail<int>(OperationError);

    private static bool PersistFlag(Result<int> result) => ((IPersistOnFailure)result).PersistOnFailure;

    private static Result<int> PassThrough(int value) => Result.Ok(value);

    private static int ValueOf(Result<int> result) => result.TryGetValue(out var value) ? value : -1;

    private static TaskCompletionSource<Result<int>> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    #endregion

    #region A failed source short-circuits

    [Theory]
    [MemberData(nameof(AllOverloadsWithFlagAndPersist))]
    public async Task A_failed_source_is_returned_untouched_and_nothing_is_evaluated(string overload, bool flag, bool persisted)
    {
        var predicateCalls = new List<int>();
        var operationCalls = new List<int>();

        var result = await Call(overload, Failure(persisted), flag, Op.Of(value =>
        {
            operationCalls.Add(value);
            return Result.Ok(value);
        }), predicateCalls);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeSameAs(SourceError);
        PersistFlag(result).Should().Be(persisted, "a failure that is never replaced keeps its own commit intent");
        predicateCalls.Should().BeEmpty("a failed source never reaches the predicate");
        operationCalls.Should().BeEmpty();
    }

    #endregion

    #region A successful source runs the selected branch once

    [Theory]
    [MemberData(nameof(AllOverloads))]
    public async Task A_selected_branch_runs_the_operation_once_with_the_value_and_its_result_replaces_the_source(string overload)
    {
        var operationCalls = new List<int>();
        var selectedFlag = s_overloads[overload].IsWhen;

        var result = await Call(overload, Success(21), selectedFlag, Op.Of(value =>
        {
            operationCalls.Add(value);
            return Result.Ok(value * 2);
        }));

        operationCalls.Should().Equal([21]);
        result.IsSuccess.Should().BeTrue();
        ValueOf(result).Should().Be(42);
    }

    [Theory]
    [MemberData(nameof(AllOverloads))]
    public async Task An_unselected_branch_skips_the_operation_and_returns_the_source_value(string overload)
    {
        var operationCalls = new List<int>();
        var unselectedFlag = !s_overloads[overload].IsWhen;

        var result = await Call(overload, Success(21), unselectedFlag, Op.Of(value =>
        {
            operationCalls.Add(value);
            return Result.Ok(value * 2);
        }));

        operationCalls.Should().BeEmpty();
        result.IsSuccess.Should().BeTrue();
        ValueOf(result).Should().Be(21);
    }

    [Theory]
    [MemberData(nameof(PredicateOverloadsWithFlag))]
    public async Task The_predicate_is_evaluated_exactly_once_with_the_value_and_decides_the_branch(string overload, bool flag)
    {
        var predicateCalls = new List<int>();
        var operationCalls = new List<int>();

        var result = await Call(overload, Success(21), flag, Op.Of(value =>
        {
            operationCalls.Add(value);
            return Result.Ok(value * 2);
        }), predicateCalls);

        predicateCalls.Should().Equal([21]);
        operationCalls.Count.Should().Be(s_overloads[overload].Runs(flag) ? 1 : 0);
        ValueOf(result).Should().Be(s_overloads[overload].Runs(flag) ? 42 : 21);
    }

    #endregion

    #region The operation's result replaces the source

    [Theory]
    [MemberData(nameof(AllOverloadsWithPersist))]
    public async Task An_operation_failure_replaces_the_success_and_its_commit_intent_governs(string overload, bool persisted)
    {
        var result = await Call(overload, Success(), s_overloads[overload].IsWhen, Op.Of(_ => OperationFailure(persisted)));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeSameAs(OperationError);
        PersistFlag(result).Should().Be(persisted);
    }

    [Theory]
    [MemberData(nameof(AllOverloads))]
    public async Task An_unselected_branch_after_a_success_never_acquires_a_commit_intent(string overload)
    {
        var result = await Call(overload, Success(), !s_overloads[overload].IsWhen, Op.Of(_ => OperationFailure(persisted: true)));

        result.IsSuccess.Should().BeTrue();
        PersistFlag(result).Should().BeFalse();
    }

    #endregion

    #region Guards

    [Theory]
    [MemberData(nameof(AllOverloads))]
    public async Task A_null_operation_is_rejected_even_when_the_source_has_failed(string overload)
    {
        Func<Task> act = () => Call(overload, Failure(persisted: false), flag: true, Op.Null);

        await act.ShouldRejectNullAsync("operation");
    }

    [Theory]
    [MemberData(nameof(PredicateOverloads))]
    public async Task A_null_predicate_is_rejected_even_when_the_source_has_failed(string overload)
    {
        Func<Task> act = () => s_overloads[overload].Run(Failure(persisted: false), null, true, Op.Of(PassThrough));

        await act.ShouldRejectNullAsync("predicate");
    }

    [Theory]
    [MemberData(nameof(TaskReceiverOverloads))]
    public async Task A_null_task_receiver_is_rejected(string overload)
    {
        Func<Task> act = () => Call(overload, null!, flag: true, Op.Of(PassThrough));

        await act.ShouldRejectNullAsync("resultTask");
    }

    [Theory]
    [MemberData(nameof(PendingSourceOverloads))]
    public async Task A_null_operation_is_rejected_before_the_pending_source_is_awaited(string overload)
    {
        var gate = Gate();

        var operation = Call(overload, gate.Task, flag: true, Op.Null);

        await operation.ShouldRejectNullBeforeAwaitAsync("operation");
    }

    [Theory]
    [MemberData(nameof(PendingSourcePredicateOverloads))]
    public async Task A_null_predicate_is_rejected_before_the_pending_source_is_awaited(string overload)
    {
        var gate = Gate();

        var operation = s_overloads[overload].Run(gate.Task, null, true, Op.Of(PassThrough));

        await operation.ShouldRejectNullBeforeAwaitAsync("predicate");
    }

    #endregion

    #region Tracing

    [Theory]
    [MemberData(nameof(AllOverloads))]
    public async Task A_selected_success_records_one_span_named_after_the_synchronous_operator(string overload)
    {
        using var activityTest = new ActivityTestHelper();
        var created = Result.Ok(1);

        await Call(overload, Success(), s_overloads[overload].IsWhen, Op.Of(_ => created));

        var activity = activityTest.CapturedActivities.Should().ContainSingle().Which;
        activity.DisplayName.Should().Be(s_overloads[overload].SpanName);
        activity.Status.Should().Be(ActivityStatusCode.Ok, "the span reports the returned result even when it was created before the operator ran");
    }

    [Theory]
    [MemberData(nameof(AllOverloads))]
    public async Task A_failed_source_records_one_error_span(string overload)
    {
        using var activityTest = new ActivityTestHelper();

        await Call(overload, Failure(persisted: false), s_overloads[overload].IsWhen, Op.Of(PassThrough));

        var activity = activityTest.CapturedActivities.Should().ContainSingle().Which;
        activity.DisplayName.Should().Be(s_overloads[overload].SpanName);
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Theory]
    [MemberData(nameof(AllOverloads))]
    public async Task A_selected_operation_failure_marks_the_span_as_an_error(string overload)
    {
        using var activityTest = new ActivityTestHelper();
        var created = OperationFailure(persisted: false);

        await Call(overload, Success(), s_overloads[overload].IsWhen, Op.Of(_ => created));

        var activity = activityTest.CapturedActivities.Should().ContainSingle().Which;
        activity.Status.Should().Be(ActivityStatusCode.Error, "the span reports the returned result even when it was created before the operator ran");
    }

    [Theory]
    [MemberData(nameof(AllOverloads))]
    public async Task An_unselected_success_records_one_ok_span(string overload)
    {
        using var activityTest = new ActivityTestHelper();

        await Call(overload, Success(), !s_overloads[overload].IsWhen, Op.Of(PassThrough));

        var activity = activityTest.CapturedActivities.Should().ContainSingle().Which;
        activity.DisplayName.Should().Be(s_overloads[overload].SpanName);
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    #endregion

    #region Completeness

    [Fact]
    public void Every_public_When_and_Unless_overload_has_an_adapter()
    {
        var published = new Dictionary<string, int>();
        foreach (var type in new[] { typeof(WhenExtensions), typeof(WhenExtensionsAsync) })
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                published.TryGetValue(method.Name, out var count);
                published[method.Name] = count + 1;
            }
        }

        var adapted = new Dictionary<string, int>();
        foreach (var overload in s_overloads.Values)
        {
            adapted.TryGetValue(overload.Method, out var count);
            adapted[overload.Method] = count + 1;
        }

        adapted.Should().BeEquivalentTo(published);
    }

    #endregion
}
