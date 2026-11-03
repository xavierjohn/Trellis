namespace Trellis.Core.Tests.Maybes.Extensions;

using System;
using System.Threading.Tasks;
using Trellis;

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
    public async Task MatchAsync_Task_SyncBranches_NullSome_Throws()
    {
        Func<Task> act = async () => await SomeTask(1).MatchAsync(some: (Func<int, int>)null!, none: () => 0);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("some");
    }

    [Fact]
    public async Task MatchAsync_Task_SyncBranches_NullNone_Throws()
    {
        Func<Task> act = async () => await SomeTask(1).MatchAsync(some: v => v, none: (Func<int>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("none");
    }

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
    public async Task MatchAsync_ValueTask_SyncBranches_NullSome_Throws()
    {
        Func<Task> act = async () => await SomeValueTask(1).MatchAsync(some: (Func<int, int>)null!, none: () => 0).AsTask();

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("some");
    }

    [Fact]
    public async Task MatchAsync_ValueTask_SyncBranches_NullNone_Throws()
    {
        Func<Task> act = async () => await SomeValueTask(1).MatchAsync(some: v => v, none: (Func<int>)null!).AsTask();

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("none");
    }

    #endregion

    #region Overload 3: Task<Maybe<T>>, async branches (Task<TResult>)

    [Fact]
    public async Task MatchAsync_Task_AsyncBranches_HasValue_AwaitsSomeBranch()
    {
        var tcs = new TaskCompletionSource<int>();
        var matchTask = SomeTask(5).MatchAsync(
            some: async v => { await tcs.Task; return v + 100; },
            none: () => Task.FromResult(-1));

        matchTask.IsCompleted.Should().BeFalse("the some branch should be awaited");
        tcs.SetResult(0);

        var result = await matchTask;
        result.Should().Be(105);
    }

    [Fact]
    public async Task MatchAsync_Task_AsyncBranches_HasNoValue_AwaitsNoneBranch()
    {
        var tcs = new TaskCompletionSource<int>();
        var matchTask = NoneTask().MatchAsync(
            some: v => Task.FromResult(v),
            none: async () => { await tcs.Task; return -42; });

        matchTask.IsCompleted.Should().BeFalse("the none branch should be awaited");
        tcs.SetResult(0);

        var result = await matchTask;
        result.Should().Be(-42);
    }

    [Fact]
    public async Task MatchAsync_Task_AsyncBranches_NullSome_Throws()
    {
        Func<Task> act = async () => await SomeTask(1).MatchAsync(
            some: (Func<int, Task<int>>)null!,
            none: () => Task.FromResult(0));

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("some");
    }

    [Fact]
    public async Task MatchAsync_Task_AsyncBranches_NullNone_Throws()
    {
        Func<Task> act = async () => await SomeTask(1).MatchAsync(
            some: v => Task.FromResult(v),
            none: (Func<Task<int>>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("none");
    }

    #endregion

    #region Overload 4: ValueTask<Maybe<T>>, async branches (ValueTask<TResult>)

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncBranches_HasValue_AwaitsSomeBranch()
    {
        var tcs = new TaskCompletionSource<int>();
        var matchTask = SomeValueTask(5).MatchAsync(
            some: async v => { await tcs.Task; return v + 100; },
            none: () => new ValueTask<int>(-1)).AsTask();

        matchTask.IsCompleted.Should().BeFalse("the some branch should be awaited");
        tcs.SetResult(0);

        var result = await matchTask;
        result.Should().Be(105);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncBranches_HasNoValue_AwaitsNoneBranch()
    {
        var tcs = new TaskCompletionSource<int>();
        var matchTask = NoneValueTask().MatchAsync(
            some: v => new ValueTask<int>(v),
            none: async () => { await tcs.Task; return -42; }).AsTask();

        matchTask.IsCompleted.Should().BeFalse("the none branch should be awaited");
        tcs.SetResult(0);

        var result = await matchTask;
        result.Should().Be(-42);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncBranches_NullSome_Throws()
    {
        Func<Task> act = async () => await SomeValueTask(1).MatchAsync(
            some: (Func<int, ValueTask<int>>)null!,
            none: () => new ValueTask<int>(0)).AsTask();

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("some");
    }

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncBranches_NullNone_Throws()
    {
        Func<Task> act = async () => await SomeValueTask(1).MatchAsync(
            some: v => new ValueTask<int>(v),
            none: (Func<ValueTask<int>>)null!).AsTask();

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("none");
    }

    #endregion

    [Fact]
    public async Task MatchAsync_Task_SyncBranches_PendingReceiver_DefersBranchSelection()
    {
        var source = new TaskCompletionSource<Maybe<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var someInvocations = 0;
        var noneInvocations = 0;
        var matchTask = source.Task.MatchAsync<int, int>(
            some: value =>
            {
                someInvocations++;
                return value * 2;
            },
            none: () =>
            {
                noneInvocations++;
                return -1;
            });

        try
        {
            matchTask.IsCompleted.Should().BeFalse();
            someInvocations.Should().Be(0);
            noneInvocations.Should().Be(0);
        }
        finally
        {
            source.SetResult(Maybe.From(7));
        }

        var result = await matchTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        result.Should().Be(14);
        someInvocations.Should().Be(1);
        noneInvocations.Should().Be(0);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncBranches_PendingReceiver_DefersBranchSelection()
    {
        var source = new TaskCompletionSource<Maybe<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var someInvocations = 0;
        var noneInvocations = 0;
        var matchTask = new ValueTask<Maybe<int>>(source.Task).MatchAsync<int, int>(
            some: value =>
            {
                someInvocations++;
                return new ValueTask<int>(value * 2);
            },
            none: () =>
            {
                noneInvocations++;
                return new ValueTask<int>(-42);
            }).AsTask();

        try
        {
            matchTask.IsCompleted.Should().BeFalse();
            someInvocations.Should().Be(0);
            noneInvocations.Should().Be(0);
        }
        finally
        {
            source.SetResult(Maybe<int>.None);
        }

        var result = await matchTask.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        result.Should().Be(-42);
        someInvocations.Should().Be(0);
        noneInvocations.Should().Be(1);
    }

    [Fact]
    public async Task MatchAsync_Task_AsyncBranches_FaultedReceiver_PreservesException()
    {
        var exception = new InvalidOperationException("receiver-source-fault");
        var source = Task.FromException<Maybe<int>>(exception);
        var someInvocations = 0;
        var noneInvocations = 0;
        Func<Task> act = async () => await source.MatchAsync<int, int>(
            some: value =>
            {
                someInvocations++;
                return Task.FromResult(value * 2);
            },
            none: () =>
            {
                noneInvocations++;
                return Task.FromResult(-1);
            });

        var failure = await act.Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Should().BeSameAs(exception);
        someInvocations.Should().Be(0);
        noneInvocations.Should().Be(0);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_SyncBranches_FaultedReceiver_PreservesException()
    {
        var exception = new InvalidOperationException("receiver-source-fault");
        var someInvocations = 0;
        var noneInvocations = 0;
        Func<Task> act = async () => await ValueTask.FromException<Maybe<int>>(exception).MatchAsync<int, int>(
            some: value =>
            {
                someInvocations++;
                return value * 2;
            },
            none: () =>
            {
                noneInvocations++;
                return -1;
            });

        var failure = await act.Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Should().BeSameAs(exception);
        someInvocations.Should().Be(0);
        noneInvocations.Should().Be(0);
    }

    [Fact]
    public async Task MatchAsync_Task_SyncBranches_CanceledReceiver_PreservesToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = Task.FromCanceled<Maybe<int>>(cancellation.Token);
        var someInvocations = 0;
        var noneInvocations = 0;
        Func<Task> act = async () => await source.MatchAsync<int, int>(
            some: value =>
            {
                someInvocations++;
                return value * 2;
            },
            none: () =>
            {
                noneInvocations++;
                return -1;
            });

        var failure = await act.Should().ThrowAsync<OperationCanceledException>();

        failure.Which.CancellationToken.Should().Be(cancellation.Token);
        someInvocations.Should().Be(0);
        noneInvocations.Should().Be(0);
    }

    [Fact]
    public async Task MatchAsync_ValueTask_AsyncBranches_CanceledReceiver_PreservesToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var someInvocations = 0;
        var noneInvocations = 0;
        Func<Task> act = async () => await ValueTask.FromCanceled<Maybe<int>>(cancellation.Token).MatchAsync<int, int>(
            some: value =>
            {
                someInvocations++;
                return new ValueTask<int>(value * 2);
            },
            none: () =>
            {
                noneInvocations++;
                return new ValueTask<int>(-42);
            });

        var failure = await act.Should().ThrowAsync<OperationCanceledException>();

        failure.Which.CancellationToken.Should().Be(cancellation.Token);
        someInvocations.Should().Be(0);
        noneInvocations.Should().Be(0);
    }
}