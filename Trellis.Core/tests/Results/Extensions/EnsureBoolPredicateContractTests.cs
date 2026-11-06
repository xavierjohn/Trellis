namespace Trellis.Core.Tests.Results.Extensions;

using System.Diagnostics;
using System.Reflection;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

/// <summary>
/// Pins one contract across every <c>Ensure</c>/<c>EnsureAsync</c> overload that takes a boolean predicate (sync,
/// <see cref="Task{TResult}"/> and <see cref="ValueTask{TResult}"/> receivers; sync and async predicates; static,
/// synchronous and asynchronous error factories): a failed source short-circuits before the predicate or an error factory
/// runs, a passing predicate returns the source, a failing one returns the supplied error, guards fire before anything is
/// awaited, and the span is named <c>Ensure</c>.
/// </summary>
/// <remarks>
/// Each theory runs one scenario through a small adapter per overload, so an overload that breaks the rule fails by
/// name. The completeness test fails when an overload exists that has no adapter. The overloads whose predicate returns a
/// <see cref="Result{TValue}"/> are covered by <c>ResultFailAfterCommitReplacementTests</c>.
/// </remarks>
public class EnsureBoolPredicateContractTests
{
    private static readonly Error SourceError = new Error.Conflict(Resource: null, Code: "source") { Detail = "source" };
    private static readonly Error Rejected = new Error.Conflict(Resource: null, Code: "rejected") { Detail = "rejected" };

    private enum Receiver { Result, Task, ValueTask }

    private enum Awaitable { None, Task, ValueTask }

    private enum ErrorKind { Static, Factory, AsyncFactory }

    private sealed record Pred(Func<int, bool>? Sync, Func<int, Task<bool>>? Async)
    {
        public static readonly Pred Null = new(null, null);

        public static Pred Of(Func<int, bool> predicate) => new(predicate, value => Task.FromResult(predicate(value)));

        public static Pred FromAsync(Func<int, Task<bool>> predicate) => new(null, predicate);

        public static Pred Throws(Exception exception) => new(_ => throw exception, _ => throw exception);
    }

    private sealed record Fac(Func<int, Error>? Sync, Func<int, Task<Error>>? Async)
    {
        public static readonly Fac Null = new(null, null);

        public static Fac Of(Func<int, Error> factory) => new(factory, value => Task.FromResult(factory(value)));

        public static Fac FromAsync(Func<int, Task<Error>> factory) => new(null, factory);
    }

    private sealed record Form(
        Receiver Receiver,
        Awaitable Predicate,
        ErrorKind ErrorKind,
        bool PredicateTakesValue,
        Func<Task<Result<int>>, Pred, Error, Fac, Task<Result<int>>> Run)
    {
        public bool PendingSource => Receiver != Receiver.Result;

        public bool AsyncPredicate => Predicate != Awaitable.None;

        public bool HasFactory => ErrorKind != ErrorKind.Static;
    }

    private static Func<bool>? NoValue(Pred p) => p.Sync is null ? null : () => p.Sync(0);

    private static Func<Task<bool>>? NoValueTask(Pred p) => p.Async is null ? null : () => p.Async(0);

    private static Func<ValueTask<bool>>? NoValueValueTask(Pred p) =>
        p.Sync is not null ? () => new ValueTask<bool>(p.Sync(0)) : p.Async is null ? null : () => new ValueTask<bool>(p.Async(0));

    private static Func<int, ValueTask<bool>>? ValueTaskOf(Pred p) =>
        p.Sync is not null ? value => new ValueTask<bool>(p.Sync(value)) : p.Async is null ? null : value => new ValueTask<bool>(p.Async(value));

    private static Func<int, ValueTask<Error>>? ValueTaskOf(Fac f) =>
        f.Sync is not null ? value => new ValueTask<Error>(f.Sync(value)) : f.Async is null ? null : value => new ValueTask<Error>(f.Async(value));

    private static ValueTask<Result<int>> Wrap(Task<Result<int>> source) => new(source);

    #region Adapters

    private static readonly Dictionary<string, Form> s_forms = new()
    {
        ["Ensure(bool(), Error)"] = new(Receiver.Result, Awaitable.None, ErrorKind.Static, false,
            async (s, p, e, _) => (await s).Ensure(NoValue(p)!, e)),
        ["Ensure(bool(value), Error)"] = new(Receiver.Result, Awaitable.None, ErrorKind.Static, true,
            async (s, p, e, _) => (await s).Ensure(p.Sync!, e)),
        ["Ensure(bool(value), Func<value, Error>)"] = new(Receiver.Result, Awaitable.None, ErrorKind.Factory, true,
            async (s, p, _, f) => (await s).Ensure(p.Sync!, f.Sync!)),

        ["Task.EnsureAsync(Task<bool>(value), Error)"] = new(Receiver.Task, Awaitable.Task, ErrorKind.Static, true,
            (s, p, e, _) => s.EnsureAsync(p.Async!, e)),
        ["Task.EnsureAsync(Task<bool>(value), Func<value, Error>)"] = new(Receiver.Task, Awaitable.Task, ErrorKind.Factory, true,
            (s, p, _, f) => s.EnsureAsync(p.Async!, f.Sync!)),
        ["Task.EnsureAsync(Task<bool>(value), Func<value, Task<Error>>)"] = new(Receiver.Task, Awaitable.Task, ErrorKind.AsyncFactory, true,
            (s, p, _, f) => s.EnsureAsync(p.Async!, f.Async!)),

        ["Task.EnsureAsync(bool(), Error)"] = new(Receiver.Task, Awaitable.None, ErrorKind.Static, false,
            (s, p, e, _) => s.EnsureAsync(NoValue(p)!, e)),
        ["Task.EnsureAsync(bool(value), Error)"] = new(Receiver.Task, Awaitable.None, ErrorKind.Static, true,
            (s, p, e, _) => s.EnsureAsync(p.Sync!, e)),
        ["Task.EnsureAsync(bool(value), Func<value, Error>)"] = new(Receiver.Task, Awaitable.None, ErrorKind.Factory, true,
            (s, p, _, f) => s.EnsureAsync(p.Sync!, f.Sync!)),
        ["Task.EnsureAsync(bool(value), Func<value, Task<Error>>)"] = new(Receiver.Task, Awaitable.None, ErrorKind.AsyncFactory, true,
            (s, p, _, f) => s.EnsureAsync(p.Sync!, f.Async!)),

        ["Result.EnsureAsync(Task<bool>(), Error)"] = new(Receiver.Result, Awaitable.Task, ErrorKind.Static, false,
            async (s, p, e, _) => await (await s).EnsureAsync(NoValueTask(p)!, e)),
        ["Result.EnsureAsync(Task<bool>(value), Error)"] = new(Receiver.Result, Awaitable.Task, ErrorKind.Static, true,
            async (s, p, e, _) => await (await s).EnsureAsync(p.Async!, e)),
        ["Result.EnsureAsync(Task<bool>(value), Func<value, Error>)"] = new(Receiver.Result, Awaitable.Task, ErrorKind.Factory, true,
            async (s, p, _, f) => await (await s).EnsureAsync(p.Async!, f.Sync!)),
        ["Result.EnsureAsync(Task<bool>(value), Func<value, Task<Error>>)"] = new(Receiver.Result, Awaitable.Task, ErrorKind.AsyncFactory, true,
            async (s, p, _, f) => await (await s).EnsureAsync(p.Async!, f.Async!)),

        ["ValueTask.EnsureAsync(ValueTask<bool>(value), Error)"] = new(Receiver.ValueTask, Awaitable.ValueTask, ErrorKind.Static, true,
            (s, p, e, _) => Wrap(s).EnsureAsync(ValueTaskOf(p)!, e).AsTask()),
        ["ValueTask.EnsureAsync(ValueTask<bool>(value), Func<value, Error>)"] = new(Receiver.ValueTask, Awaitable.ValueTask, ErrorKind.Factory, true,
            (s, p, _, f) => Wrap(s).EnsureAsync(ValueTaskOf(p)!, f.Sync!).AsTask()),
        ["ValueTask.EnsureAsync(ValueTask<bool>(value), Func<value, ValueTask<Error>>)"] = new(Receiver.ValueTask, Awaitable.ValueTask, ErrorKind.AsyncFactory, true,
            (s, p, _, f) => Wrap(s).EnsureAsync(ValueTaskOf(p)!, ValueTaskOf(f)!).AsTask()),

        ["ValueTask.EnsureAsync(bool(), Error)"] = new(Receiver.ValueTask, Awaitable.None, ErrorKind.Static, false,
            (s, p, e, _) => Wrap(s).EnsureAsync(NoValue(p)!, e).AsTask()),
        ["ValueTask.EnsureAsync(bool(value), Error)"] = new(Receiver.ValueTask, Awaitable.None, ErrorKind.Static, true,
            (s, p, e, _) => Wrap(s).EnsureAsync(p.Sync!, e).AsTask()),
        ["ValueTask.EnsureAsync(bool(value), Func<value, Error>)"] = new(Receiver.ValueTask, Awaitable.None, ErrorKind.Factory, true,
            (s, p, _, f) => Wrap(s).EnsureAsync(p.Sync!, f.Sync!).AsTask()),
        ["ValueTask.EnsureAsync(bool(value), Func<value, ValueTask<Error>>)"] = new(Receiver.ValueTask, Awaitable.None, ErrorKind.AsyncFactory, true,
            (s, p, _, f) => Wrap(s).EnsureAsync(p.Sync!, ValueTaskOf(f)!).AsTask()),

        ["Result.EnsureAsync(ValueTask<bool>(), Error)"] = new(Receiver.Result, Awaitable.ValueTask, ErrorKind.Static, false,
            async (s, p, e, _) => await (await s).EnsureAsync(NoValueValueTask(p)!, e)),
        ["Result.EnsureAsync(ValueTask<bool>(value), Error)"] = new(Receiver.Result, Awaitable.ValueTask, ErrorKind.Static, true,
            async (s, p, e, _) => await (await s).EnsureAsync(ValueTaskOf(p)!, e)),
        ["Result.EnsureAsync(ValueTask<bool>(value), Func<value, Error>)"] = new(Receiver.Result, Awaitable.ValueTask, ErrorKind.Factory, true,
            async (s, p, _, f) => await (await s).EnsureAsync(ValueTaskOf(p)!, f.Sync!)),
        ["Result.EnsureAsync(ValueTask<bool>(value), Func<value, ValueTask<Error>>)"] = new(Receiver.Result, Awaitable.ValueTask, ErrorKind.AsyncFactory, true,
            async (s, p, _, f) => await (await s).EnsureAsync(ValueTaskOf(p)!, ValueTaskOf(f)!)),
    };

    public static TheoryData<string> AllForms() => new(Keys(_ => true));

    public static TheoryData<string> ValuePredicateForms() => new(Keys(form => form.PredicateTakesValue));

    public static TheoryData<string> AsyncPredicateForms() => new(Keys(form => form.AsyncPredicate));

    public static TheoryData<string> PendingSourceForms() => new(Keys(form => form.PendingSource));

    public static TheoryData<string> TaskReceiverForms() => new(Keys(form => form.Receiver == Receiver.Task));

    public static TheoryData<string> FactoryForms() => new(Keys(form => form.HasFactory));

    public static TheoryData<string> AsyncFactoryForms() => new(Keys(form => form.ErrorKind == ErrorKind.AsyncFactory));

    public static TheoryData<string> PendingSourceFactoryForms() => new(Keys(form => form.PendingSource && form.HasFactory));

    public static TheoryData<string, bool> AllFormsWithPersist()
    {
        var data = new TheoryData<string, bool>();
        foreach (var key in s_forms.Keys)
        {
            data.Add(key, true);
            data.Add(key, false);
        }

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

    private static Task<Result<int>> Run(string form, Task<Result<int>> source, Pred predicate, Fac? factory = null) =>
        s_forms[form].Run(source, predicate, Rejected, factory ?? Fac.Of(_ => Rejected));

    private static Task<Result<int>> Success(int value = 42) => Task.FromResult(Result.Ok(value));

    private static Task<Result<int>> Failure(bool persisted) =>
        Task.FromResult(persisted ? Result.FailAfterCommit<int>(SourceError) : Result.Fail<int>(SourceError));

    private static bool PersistFlag(Result<int> result) => ((IPersistOnFailure)result).PersistOnFailure;

    private static int ValueOf(Result<int> result) => result.TryGetValue(out var value) ? value : -1;

    private static TaskCompletionSource<Result<int>> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    #endregion

    #region A failed source short-circuits

    [Theory]
    [MemberData(nameof(AllFormsWithPersist))]
    public async Task A_failed_source_is_returned_untouched_and_neither_the_predicate_nor_a_factory_runs(string form, bool persisted)
    {
        var predicateCalls = new List<int>();
        var factoryCalls = new List<int>();

        var result = await Run(form, Failure(persisted), Pred.Of(value =>
        {
            predicateCalls.Add(value);
            return true;
        }), Fac.Of(value =>
        {
            factoryCalls.Add(value);
            return Rejected;
        }));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeSameAs(SourceError);
        PersistFlag(result).Should().Be(persisted, "a failure that is never replaced keeps its own commit intent");
        predicateCalls.Should().BeEmpty("a failed source never reaches the predicate");
        factoryCalls.Should().BeEmpty();
    }

    #endregion

    #region A successful source

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_passing_predicate_returns_the_source_and_never_builds_the_error(string form)
    {
        var predicateCalls = new List<int>();
        var factoryCalls = new List<int>();

        var result = await Run(form, Success(21), Pred.Of(value =>
        {
            predicateCalls.Add(value);
            return true;
        }), Fac.Of(value =>
        {
            factoryCalls.Add(value);
            return Rejected;
        }));

        result.IsSuccess.Should().BeTrue();
        ValueOf(result).Should().Be(21);
        predicateCalls.Should().HaveCount(1);
        factoryCalls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_failing_predicate_returns_the_error_as_a_plain_failure(string form)
    {
        var result = await Run(form, Success(21), Pred.Of(_ => false));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeSameAs(Rejected);
        PersistFlag(result).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task The_predicate_runs_exactly_once(string form)
    {
        var predicateCalls = new List<int>();

        await Run(form, Success(21), Pred.Of(value =>
        {
            predicateCalls.Add(value);
            return false;
        }));

        predicateCalls.Should().HaveCount(1);
    }

    [Theory]
    [MemberData(nameof(ValuePredicateForms))]
    public async Task The_predicate_receives_the_value(string form)
    {
        var predicateCalls = new List<int>();

        await Run(form, Success(21), Pred.Of(value =>
        {
            predicateCalls.Add(value);
            return false;
        }));

        predicateCalls.Should().Equal([21]);
    }

    [Theory]
    [MemberData(nameof(FactoryForms))]
    public async Task The_error_factory_runs_once_with_the_value_and_its_error_is_returned(string form)
    {
        var factoryCalls = new List<int>();

        var result = await Run(form, Success(21), Pred.Of(_ => false), Fac.Of(value =>
        {
            factoryCalls.Add(value);
            return Rejected;
        }));

        factoryCalls.Should().Equal([21]);
        result.Error.Should().BeSameAs(Rejected);
    }

    #endregion

    #region Guards

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_null_predicate_is_rejected_even_when_the_source_has_failed(string form)
    {
        Func<Task> act = () => Run(form, Failure(persisted: false), Pred.Null);

        await act.ShouldRejectNullAsync("predicate");
    }

    [Theory]
    [MemberData(nameof(FactoryForms))]
    public async Task A_null_error_factory_is_rejected_even_when_the_source_has_failed(string form)
    {
        Func<Task> act = () => Run(form, Failure(persisted: false), Pred.Of(_ => true), Fac.Null);

        await act.ShouldRejectNullAsync("errorPredicate");
    }

    [Theory]
    [MemberData(nameof(TaskReceiverForms))]
    public async Task A_null_task_receiver_is_rejected(string form)
    {
        Func<Task> act = () => Run(form, null!, Pred.Of(_ => true));

        await act.ShouldRejectNullAsync("resultTask");
    }

    [Theory]
    [MemberData(nameof(PendingSourceForms))]
    public async Task A_null_predicate_is_rejected_before_the_pending_source_is_awaited(string form)
    {
        var gate = Gate();

        var operation = Run(form, gate.Task, Pred.Null);

        await operation.ShouldRejectNullBeforeAwaitAsync("predicate");
    }

    [Theory]
    [MemberData(nameof(PendingSourceFactoryForms))]
    public async Task A_null_error_factory_is_rejected_before_the_pending_source_is_awaited(string form)
    {
        var gate = Gate();

        var operation = Run(form, gate.Task, Pred.Of(_ => true), Fac.Null);

        await operation.ShouldRejectNullBeforeAwaitAsync("errorPredicate");
    }

    #endregion

    #region Completion and exceptions of the async overloads

    [Theory]
    [MemberData(nameof(PendingSourceForms))]
    public async Task A_pending_source_is_awaited_before_the_predicate_runs(string form)
    {
        var gate = Gate();
        var predicateCalls = new List<int>();

        var all = Run(form, gate.Task, Pred.Of(value =>
        {
            predicateCalls.Add(value);
            return true;
        }));

        all.IsCompleted.Should().BeFalse();
        predicateCalls.Should().BeEmpty("the predicate cannot run before the source completes");
        gate.SetResult(Result.Ok(21));
        var result = await all;
        ValueOf(result).Should().Be(21);
        predicateCalls.Should().HaveCount(1);
    }

    [Theory]
    [MemberData(nameof(PendingSourceForms))]
    public async Task A_faulted_source_rethrows_without_evaluating_anything(string form)
    {
        var predicateCalls = new List<int>();
        var source = Task.FromException<Result<int>>(new InvalidOperationException("source"));

        Func<Task> act = () => Run(form, source, Pred.Of(value =>
        {
            predicateCalls.Add(value);
            return true;
        }));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("source");
        predicateCalls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(PendingSourceForms))]
    public async Task A_canceled_source_cancels_without_evaluating_anything(string form)
    {
        var predicateCalls = new List<int>();
        var source = Task.FromCanceled<Result<int>>(new CancellationToken(canceled: true));

        var all = Run(form, source, Pred.Of(value =>
        {
            predicateCalls.Add(value);
            return true;
        }));

        Func<Task> act = () => all;
        await act.Should().ThrowAsync<OperationCanceledException>();
        all.IsCanceled.Should().BeTrue();
        predicateCalls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AsyncPredicateForms))]
    public async Task A_pending_predicate_is_awaited_and_its_verdict_decides(string form)
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var all = Run(form, Success(21), Pred.FromAsync(_ => gate.Task));

        all.IsCompleted.Should().BeFalse("the predicate has not completed yet");
        gate.SetResult(false);
        var result = await all;
        result.Error.Should().BeSameAs(Rejected);
    }

    [Theory]
    [MemberData(nameof(AsyncPredicateForms))]
    public async Task A_faulted_predicate_rethrows_and_builds_no_error(string form)
    {
        var factoryCalls = new List<int>();

        Func<Task> act = () => Run(form, Success(21), Pred.FromAsync(_ => Task.FromException<bool>(new InvalidOperationException("predicate"))), Fac.Of(value =>
        {
            factoryCalls.Add(value);
            return Rejected;
        }));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("predicate");
        factoryCalls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AsyncPredicateForms))]
    public async Task A_canceled_predicate_cancels(string form)
    {
        var all = Run(form, Success(21), Pred.FromAsync(_ => Task.FromCanceled<bool>(new CancellationToken(canceled: true))));

        Func<Task> act = () => all;
        await act.Should().ThrowAsync<OperationCanceledException>();
        all.IsCanceled.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_predicate_that_throws_escapes_unwrapped(string form)
    {
        Func<Task> act = () => Run(form, Success(21), Pred.Throws(new InvalidOperationException("predicate")));

        await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("predicate");
    }

    [Theory]
    [MemberData(nameof(AsyncFactoryForms))]
    public async Task A_pending_error_factory_is_awaited_and_its_error_is_returned(string form)
    {
        var gate = new TaskCompletionSource<Error>(TaskCreationOptions.RunContinuationsAsynchronously);

        var all = Run(form, Success(21), Pred.Of(_ => false), Fac.FromAsync(_ => gate.Task));

        all.IsCompleted.Should().BeFalse("the error factory has not completed yet");
        gate.SetResult(Rejected);
        var result = await all;
        result.Error.Should().BeSameAs(Rejected);
    }

    [Theory]
    [MemberData(nameof(AsyncFactoryForms))]
    public async Task A_faulted_error_factory_rethrows(string form)
    {
        Func<Task> act = () => Run(form, Success(21), Pred.Of(_ => false),
            Fac.FromAsync(_ => Task.FromException<Error>(new InvalidOperationException("factory"))));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("factory");
    }

    #endregion

    #region Tracing

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_passing_predicate_records_one_ok_span_named_Ensure(string form)
    {
        using var activityTest = new ActivityTestHelper();

        await Run(form, Success(), Pred.Of(_ => true));

        var activity = activityTest.CapturedActivities.Should().ContainSingle().Which;
        activity.DisplayName.Should().Be("Ensure");
        activity.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_failed_source_records_one_error_span(string form)
    {
        using var activityTest = new ActivityTestHelper();

        await Run(form, Failure(persisted: false), Pred.Of(_ => true));

        var activity = activityTest.CapturedActivities.Should().ContainSingle().Which;
        activity.DisplayName.Should().Be("Ensure");
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Theory]
    [MemberData(nameof(AllForms))]
    public async Task A_failing_predicate_records_one_error_span(string form)
    {
        using var activityTest = new ActivityTestHelper();

        await Run(form, Success(), Pred.Of(_ => false));

        var activity = activityTest.CapturedActivities.Should().ContainSingle().Which;
        activity.DisplayName.Should().Be("Ensure");
        activity.Status.Should().Be(ActivityStatusCode.Error);
    }

    #endregion

    #region Completeness

    [Fact]
    public void Every_public_boolean_predicate_Ensure_overload_has_an_adapter()
    {
        var published = new Dictionary<string, int>();
        foreach (var type in new[] { typeof(EnsureExtensions), typeof(EnsureExtensionsAsync) })
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.Name is not ("Ensure" or "EnsureAsync") || !TakesBooleanPredicate(method))
                    continue;

                var key = $"{ReceiverOf(method)}/{PredicateAwaitableOf(method)}/{ErrorKindOf(method)}";
                published.TryGetValue(key, out var count);
                published[key] = count + 1;
            }
        }

        var adapted = new Dictionary<string, int>();
        foreach (var form in s_forms.Values)
        {
            var key = $"{form.Receiver}/{form.Predicate}/{form.ErrorKind}";
            adapted.TryGetValue(key, out var count);
            adapted[key] = count + 1;
        }

        adapted.Should().BeEquivalentTo(published);
    }

    private static bool TakesBooleanPredicate(MethodInfo method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length != 3 || parameters[1].Name != "predicate")
            return false;

        var verdict = parameters[1].ParameterType.GetGenericArguments()[^1];
        return verdict == typeof(bool) || verdict == typeof(Task<bool>) || verdict == typeof(ValueTask<bool>);
    }

    private static Receiver ReceiverOf(MethodInfo method)
    {
        var receiver = method.GetParameters()[0].ParameterType;
        if (!receiver.IsGenericType)
            return Receiver.Result;

        var definition = receiver.GetGenericTypeDefinition();
        if (definition == typeof(Task<>))
            return Receiver.Task;

        return definition == typeof(ValueTask<>) ? Receiver.ValueTask : Receiver.Result;
    }

    private static ErrorKind ErrorKindOf(MethodInfo method)
    {
        var error = method.GetParameters()[2].ParameterType;
        if (!error.IsGenericType)
            return ErrorKind.Static;

        return error.GetGenericArguments()[^1] == typeof(Error) ? ErrorKind.Factory : ErrorKind.AsyncFactory;
    }

    private static Awaitable PredicateAwaitableOf(MethodInfo method)
    {
        var verdict = method.GetParameters()[1].ParameterType.GetGenericArguments()[^1];
        if (verdict == typeof(Task<bool>))
            return Awaitable.Task;

        return verdict == typeof(ValueTask<bool>) ? Awaitable.ValueTask : Awaitable.None;
    }

    #endregion
}
