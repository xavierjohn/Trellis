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
}