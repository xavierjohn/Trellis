namespace Trellis.Core.Tests.Maybes.Extensions;

using System;
using System.Threading.Tasks;
using Trellis;
using Trellis.Testing;

/// <summary>
/// Tests for the asynchronous <c>MatchAsync</c> extension methods on
/// <see cref="Task{TResult}"/> and <see cref="ValueTask{TResult}"/> of <see cref="Maybe{TValue}"/>.
/// Covers all 8 overloads (Task / ValueTask carriers × sync / async branches).
/// </summary>
public class MatchAsyncTests : TestBase
{
    private static Task<Maybe<int>> SomeTask(int v) => Task.FromResult(Maybe.From(v));

    private static Task<Maybe<int>> NoneTask() => Task.FromResult(Maybe<int>.None);

    private static ValueTask<Maybe<int>> SomeValueTask(int v) => new(Maybe.From(v));

    private static ValueTask<Maybe<int>> NoneValueTask() => new(Maybe<int>.None);

    private static async Task RejectsNullBranchBeforeAwaitAsync(Func<ControlledAsync<Maybe<int>>, Task> start, string paramName)
    {
        var source = new ControlledAsync<Maybe<int>>();
        try
        {
            await ((Func<Task>)(() => start(source))).ShouldRejectNullBeforeAwaitAsync(paramName);
        }
        finally
        {
            source.TryComplete(Maybe.From(1));
        }
    }

    #region Overload 1: Task<Maybe<T>>, sync branches

    [Fact]
    public async Task MatchAsync_Task_SyncBranches_HasValue_ReturnsSomeResult()
    {
        var result = await SomeTask(7).MatchAsync(some: v => v * 2, none: () => -1);

        result.Should().Be(14);
    }

    [Fact]
    public async Task MatchAsync_Task_SyncBranches_HasNoValue_ReturnsNoneResult()
    {
        var result = await NoneTask().MatchAsync(some: v => v * 2, none: () => -1);

        result.Should().Be(-1);
    }

    [Fact]
    public Task MatchAsync_Task_SyncBranches_NullSome_Throws() =>
        RejectsNullBranchBeforeAwaitAsync(
            source => source.Task.MatchAsync(some: (Func<int, int>)null!, none: () => 0),
            "some");

    [Fact]
    public Task MatchAsync_Task_SyncBranches_NullNone_Throws() =>
        RejectsNullBranchBeforeAwaitAsync(
            source => source.Task.MatchAsync(some: v => v, none: (Func<int>)null!),
            "none");

    #endregion

    #region Overload 2: ValueTask<Maybe<T>>, sync branches

    [Fact]
    public async Task MatchAsync_ValueTask_SyncBranches_HasValue_ReturnsSomeResult()
    {
        var result = await SomeValueTask(7).MatchAsync(some: v => v * 2, none: () => -1);

        result.Should().Be(14);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_SyncBranches_HasNoValue_ReturnsNoneResult()
    {
        var result = await NoneValueTask().MatchAsync(some: v => v * 2, none: () => -1);

        result.Should().Be(-1);
    }

    [Fact]
    public Task MatchAsync_ValueTask_SyncBranches_NullSome_Throws() =>
        RejectsNullBranchBeforeAwaitAsync(
            source => source.ValueTask.MatchAsync(some: (Func<int, int>)null!, none: () => 0).AsTask(),
            "some");

    [Fact]
    public Task MatchAsync_ValueTask_SyncBranches_NullNone_Throws() =>
        RejectsNullBranchBeforeAwaitAsync(
            source => source.ValueTask.MatchAsync(some: v => v, none: (Func<int>)null!).AsTask(),
            "none");

    #endregion

    #region Overload 3: Task<Maybe<T>>, async branches (Task<TResult>)

    [Fact]
    public async Task MatchAsync_Task_AsyncBranches_HasValue_AwaitsSomeBranch()
    {
        var source = new ControlledAsync<int>();
        var result = await SomeTask(5).MatchAsync(
            some: async v => { await source.Task; return v + 100; },
            none: () => Task.FromResult(-1)).ShouldBePendingThenReleaseAsync(
            () => source.TryComplete(0),
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        result.Should().Be(105);
    }

    [Fact]
    public async Task MatchAsync_Task_AsyncBranches_HasNoValue_AwaitsNoneBranch()
    {
        var source = new ControlledAsync<int>();
        var result = await NoneTask().MatchAsync(
            some: v => Task.FromResult(v),
            none: async () => { await source.Task; return -42; }).ShouldBePendingThenReleaseAsync(
            () => source.TryComplete(0),
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        result.Should().Be(-42);
    }

    [Fact]
    public Task MatchAsync_Task_AsyncBranches_NullSome_Throws() =>
        RejectsNullBranchBeforeAwaitAsync(
            source => source.Task.MatchAsync(
                some: (Func<int, Task<int>>)null!,
                none: () => Task.FromResult(0)),
            "some");

    [Fact]
    public Task MatchAsync_Task_AsyncBranches_NullNone_Throws() =>
        RejectsNullBranchBeforeAwaitAsync(
            source => source.Task.MatchAsync(
                some: v => Task.FromResult(v),
                none: (Func<Task<int>>)null!),
            "none");

    #endregion

    #region Overload 4: ValueTask<Maybe<T>>, async branches (ValueTask<TResult>)

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncBranches_HasValue_AwaitsSomeBranch()
    {
        var source = new ControlledAsync<int>();
        var result = await SomeValueTask(5).MatchAsync(
            some: async v => { await source.Task; return v + 100; },
            none: () => new ValueTask<int>(-1)).AsTask().ShouldBePendingThenReleaseAsync(
            () => source.TryComplete(0),
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        result.Should().Be(105);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncBranches_HasNoValue_AwaitsNoneBranch()
    {
        var source = new ControlledAsync<int>();
        var result = await NoneValueTask().MatchAsync(
            some: v => new ValueTask<int>(v),
            none: async () => { await source.Task; return -42; }).AsTask().ShouldBePendingThenReleaseAsync(
            () => source.TryComplete(0),
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);
        result.Should().Be(-42);
    }

    [Fact]
    public Task MatchAsync_ValueTask_AsyncBranches_NullSome_Throws() =>
        RejectsNullBranchBeforeAwaitAsync(
            source => source.ValueTask.MatchAsync(
                some: (Func<int, ValueTask<int>>)null!,
                none: () => new ValueTask<int>(0)).AsTask(),
            "some");

    [Fact]
    public Task MatchAsync_ValueTask_AsyncBranches_NullNone_Throws() =>
        RejectsNullBranchBeforeAwaitAsync(
            source => source.ValueTask.MatchAsync(
                some: v => new ValueTask<int>(v),
                none: (Func<ValueTask<int>>)null!).AsTask(),
            "none");

    #endregion

    [Fact]
    public async Task MatchAsync_Task_SyncBranches_PendingReceiver_DefersBranchSelection()
    {
        var source = new ControlledAsync<Maybe<int>>();
        var someInvocations = new CallbackRecorder<int>();
        var noneInvocations = new CallbackRecorder();
        var result = await source.Task.MatchAsync<int, int>(
            some: someInvocations.Wrap(value => value * 2),
            none: noneInvocations.Wrap(() => -1)).ShouldBePendingThenReleaseAsync(
            () => source.TryComplete(Maybe.From(7)),
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken,
            whilePending: () =>
            {
                someInvocations.CallCount.Should().Be(0);
                noneInvocations.CallCount.Should().Be(0);
            });

        result.Should().Be(14);
        someInvocations.CallCount.Should().Be(1);
        noneInvocations.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncBranches_PendingReceiver_DefersBranchSelection()
    {
        var source = new ControlledAsync<Maybe<int>>();
        var someInvocations = new CallbackRecorder<int>();
        var noneInvocations = new CallbackRecorder();
        var result = await source.ValueTask.MatchAsync<int, int>(
            some: someInvocations.Wrap(value => new ValueTask<int>(value * 2)),
            none: noneInvocations.Wrap<ValueTask<int>>(() => new ValueTask<int>(-42))).AsTask().ShouldBePendingThenReleaseAsync(
            () => source.TryComplete(Maybe<int>.None),
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken,
            whilePending: () =>
            {
                someInvocations.CallCount.Should().Be(0);
                noneInvocations.CallCount.Should().Be(0);
            });

        result.Should().Be(-42);
        someInvocations.CallCount.Should().Be(0);
        noneInvocations.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task MatchAsync_Task_AsyncBranches_FaultedReceiver_PreservesException()
    {
        var exception = new InvalidOperationException("receiver-source-fault");
        var someInvocations = new CallbackRecorder<int>();
        var noneInvocations = new CallbackRecorder();

        await Task.FromException<Maybe<int>>(exception).MatchAsync<int, int>(
            some: someInvocations.Wrap(value => Task.FromResult(value * 2)),
            none: noneInvocations.Wrap(() => Task.FromResult(-1))).ShouldPropagateExceptionAsync(exception, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        someInvocations.CallCount.Should().Be(0);
        noneInvocations.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_SyncBranches_FaultedReceiver_PreservesException()
    {
        var exception = new InvalidOperationException("receiver-source-fault");
        var someInvocations = new CallbackRecorder<int>();
        var noneInvocations = new CallbackRecorder();

        await ValueTask.FromException<Maybe<int>>(exception).MatchAsync<int, int>(
            some: someInvocations.Wrap(value => value * 2),
            none: noneInvocations.Wrap(() => -1)).ShouldPropagateExceptionAsync(exception, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        someInvocations.CallCount.Should().Be(0);
        noneInvocations.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MatchAsync_Task_SyncBranches_CanceledReceiver_PreservesToken()
    {
        using var canceled = CanceledToken.Create();
        var someInvocations = new CallbackRecorder<int>();
        var noneInvocations = new CallbackRecorder();

        await canceled.ToTask<Maybe<int>>().MatchAsync<int, int>(
            some: someInvocations.Wrap(value => value * 2),
            none: noneInvocations.Wrap(() => -1)).ShouldPropagateCancellationAsync(canceled.Token, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        someInvocations.CallCount.Should().Be(0);
        noneInvocations.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncBranches_CanceledReceiver_PreservesToken()
    {
        using var canceled = CanceledToken.Create();
        var someInvocations = new CallbackRecorder<int>();
        var noneInvocations = new CallbackRecorder();

        await canceled.ToValueTask<Maybe<int>>().MatchAsync<int, int>(
            some: someInvocations.Wrap(value => new ValueTask<int>(value * 2)),
            none: noneInvocations.Wrap<ValueTask<int>>(() => new ValueTask<int>(-42))).ShouldPropagateCancellationAsync(canceled.Token, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        someInvocations.CallCount.Should().Be(0);
        noneInvocations.CallCount.Should().Be(0);
    }
}