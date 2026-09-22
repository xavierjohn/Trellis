namespace Trellis.Core.Tests.Results.Extensions.TraverseAll;

using System.Diagnostics;
using Trellis.Core.Tests.Helpers;
using Trellis.Testing;

public class TraverseAllIndexedAsyncTests
{
    public enum AsyncForm { Task, ValueTask, UnitTask }

    [Theory]
    [InlineData(AsyncForm.Task)]
    [InlineData(AsyncForm.ValueTask)]
    [InlineData(AsyncForm.UnitTask)]
    public async Task TraverseAllAsync_Indexed_Success_PreservesPositionsOrderAndToken(AsyncForm form)
    {
        using var cts = new CancellationTokenSource();
        var visited = new List<(int, int)>();
        var enumerations = 0;
        var disposals = 0;
        IEnumerable<int> Source()
        {
            enumerations++;
            try
            {
                yield return 40;
                yield return 10;
                yield return 40;
            }
            finally
            {
                disposals++;
            }
        }

        var result = await RunAsync(form, Source(), async (value, index, ct) =>
        {
            await Task.Yield();
            ct.Should().Be(cts.Token);
            visited.Add((value, index));
            return Result.Ok(value + index);
        }, cts.Token);

        result.Should().BeSuccess();
        if (form == AsyncForm.UnitTask)
            Assert.IsType<Result<Unit>>(result).Should().BeSuccess().Which.Should().Be(Unit.Default);
        else
            Assert.IsType<Result<IReadOnlyList<int>>>(result).Should().BeSuccess().Which.Should().Equal([40, 11, 42]);
        visited.Should().Equal([(40, 0), (10, 1), (40, 2)]);
        enumerations.Should().Be(1);
        disposals.Should().Be(1);
    }

    [Theory]
    [InlineData(AsyncForm.Task)]
    [InlineData(AsyncForm.ValueTask)]
    [InlineData(AsyncForm.UnitTask)]
    public async Task TraverseAllAsync_Indexed_Empty_DoesNotInvokeSelector(AsyncForm form)
    {
        var calls = 0;
        var result = await RunAsync(form, [], (value, index, _) =>
        {
            calls++;
            return Task.FromResult(Result.Ok(value + index));
        }, TestContext.Current.CancellationToken);

        result.Should().BeSuccess();
        if (form != AsyncForm.UnitTask)
            Assert.IsType<Result<IReadOnlyList<int>>>(result).Should().BeSuccess().Which.Should().BeEmpty();
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData(AsyncForm.Task)]
    [InlineData(AsyncForm.ValueTask)]
    [InlineData(AsyncForm.UnitTask)]
    public async Task TraverseAllAsync_Indexed_PendingSelector_DoesNotStartNextItem(AsyncForm form)
    {
        var completion = new TaskCompletionSource<Result<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var visited = new List<int>();
        var error = new Error.Forbidden("denied");
        var pending = RunAsync(form, [7, 7, 7], (value, index, _) =>
        {
            visited.Add(index);
            return index == 0 ? completion.Task : Task.FromResult(Result.Ok(value));
        }, TestContext.Current.CancellationToken);

        pending.IsCompleted.Should().BeFalse();
        visited.Should().Equal([0]);
        completion.SetResult(Result.Fail<int>(error));

        var result = await pending.WaitAsync(TestContext.Current.CancellationToken);
        result.Should().BeFailure().Which.Should().BeSameAs(error);
        visited.Should().Equal([0, 1, 2]);
    }

    [Theory]
    [InlineData(AsyncForm.Task)]
    [InlineData(AsyncForm.ValueTask)]
    [InlineData(AsyncForm.UnitTask)]
    public async Task TraverseAllAsync_Indexed_MultipleFailures_PreservesInputPointers(AsyncForm form)
    {
        var owner = InputPointer.Root.AppendProperty("items");
        var result = await RunAsync(form, [8, 8, 8, 8], (value, index, _) =>
            Task.FromResult(index % 2 == 0
                ? Result.Fail<int>(Error.InvalidInput.ForField(owner.AppendIndex(index), ValidationCodes.ValueNotEmpty))
                : Result.Ok(value)), TestContext.Current.CancellationToken);

        result.Should().BeFailureOfType<Error.InvalidInput>().Which.Fields.Items.Select(field => field.Field.Path)
            .Should().Equal(["/items/0", "/items/2"]);
    }

    [Theory]
    [InlineData(AsyncForm.Task, false)]
    [InlineData(AsyncForm.Task, true)]
    [InlineData(AsyncForm.ValueTask, false)]
    [InlineData(AsyncForm.ValueTask, true)]
    [InlineData(AsyncForm.UnitTask, false)]
    [InlineData(AsyncForm.UnitTask, true)]
    public async Task TraverseAllAsync_Indexed_MixedFailures_PreservesPersistIntent(AsyncForm form, bool persist)
    {
        var first = new Error.Forbidden("denied");
        var last = new Error.Conflict(null, "duplicate");
        var result = await RunAsync(form, [0, 1, 2], (value, index, _) =>
            Task.FromResult(index switch
            {
                0 => persist ? Result.FailAfterCommit<int>(first) : Result.Fail<int>(first),
                2 => Result.Fail<int>(last),
                _ => Result.Ok(value)
            }), TestContext.Current.CancellationToken);

        result.Should().BeFailureOfType<Error.Aggregate>().Which.Errors.Items.Should().Equal([first, last]);
        Assert.IsAssignableFrom<IPersistOnFailure>(result).PersistOnFailure.Should().Be(persist);
    }

    [Theory]
    [InlineData(AsyncForm.Task, false)]
    [InlineData(AsyncForm.Task, true)]
    [InlineData(AsyncForm.ValueTask, false)]
    [InlineData(AsyncForm.ValueTask, true)]
    [InlineData(AsyncForm.UnitTask, false)]
    [InlineData(AsyncForm.UnitTask, true)]
    public async Task TraverseAllAsync_Indexed_Cancellation_StopsSelectorsAndDisposes(AsyncForm form, bool cancelBeforeStart)
    {
        using var cts = new CancellationTokenSource();
        var visited = new List<int>();
        var disposed = false;
        IEnumerable<int> Source()
        {
            try
            {
                yield return 7;
                yield return 8;
            }
            finally
            {
                disposed = true;
            }
        }

        if (cancelBeforeStart)
            cts.Cancel();
        var act = () => RunAsync(form, Source(), (_, index, _) =>
        {
            visited.Add(index);
            cts.Cancel();
            return Task.FromResult(Result.Fail<int>(new Error.Forbidden("denied")));
        }, cts.Token);

        (await act.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cts.Token);
        visited.Should().Equal(cancelBeforeStart ? [] : [0]);
        disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(AsyncForm.Task, false)]
    [InlineData(AsyncForm.Task, true)]
    [InlineData(AsyncForm.ValueTask, false)]
    [InlineData(AsyncForm.ValueTask, true)]
    [InlineData(AsyncForm.UnitTask, false)]
    [InlineData(AsyncForm.UnitTask, true)]
    public async Task TraverseAllAsync_Indexed_SelectorThrows_PropagatesAndDisposes(AsyncForm form, bool throwSynchronously)
    {
        var exception = new InvalidOperationException("selector failed");
        var disposed = false;
        var visited = new List<int>();
        IEnumerable<int> Source()
        {
            try
            {
                yield return 7;
                yield return 8;
                yield return 9;
            }
            finally
            {
                disposed = true;
            }
        }

        Task<Result<int>> Select(int value, int index, CancellationToken _)
        {
            visited.Add(index);
            if (index == 0)
                return Task.FromResult(Result.Fail<int>(new Error.Forbidden("denied")));
            if (throwSynchronously)
                throw exception;
            return Task.FromException<Result<int>>(exception);
        }

        var act = () => RunAsync(form, Source(), Select, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
        visited.Should().Equal([0, 1]);
        disposed.Should().BeTrue();
    }

    [Fact]
    public async Task TraverseAllAsync_Indexed_NullDelegates_FaultReturnedAwaitablesBeforeEnumeration()
    {
        var enumerated = false;
        IEnumerable<int> Source()
        {
            enumerated = true;
            yield return 1;
        }

        Func<int, int, CancellationToken, Task<Result<int>>> taskSelector = null!;
        Func<int, int, CancellationToken, ValueTask<Result<int>>> valueTaskSelector = null!;
        Func<int, int, CancellationToken, Task<Result<Unit>>> unitSelector = null!;

        var task = Source().TraverseAllAsync(taskSelector, TestContext.Current.CancellationToken);
        var valueTask = Source().TraverseAllAsync(valueTaskSelector, TestContext.Current.CancellationToken);
        var unitTask = Source().TraverseAllAsync(unitSelector, TestContext.Current.CancellationToken);
        var taskAct = () => task;
        var valueTaskAct = () => valueTask.AsTask();
        var unitAct = () => unitTask;

        await taskAct.Should().ThrowAsync<ArgumentNullException>().WithParameterName("selector");
        await valueTaskAct.Should().ThrowAsync<ArgumentNullException>().WithParameterName("selector");
        await unitAct.Should().ThrowAsync<ArgumentNullException>().WithParameterName("selector");
        enumerated.Should().BeFalse();
    }

    [Theory]
    [InlineData(AsyncForm.Task)]
    [InlineData(AsyncForm.ValueTask)]
    [InlineData(AsyncForm.UnitTask)]
    public async Task TraverseAllAsync_Indexed_NullSource_DoesNotInvokeSelector(AsyncForm form)
    {
        var calls = 0;
        var act = () => RunAsync(form, null!, (value, index, _) =>
        {
            calls++;
            return Task.FromResult(Result.Ok(value + index));
        }, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("source");
        calls.Should().Be(0);
    }

    [Fact]
    public async Task TraverseAllAsync_Indexed_OverloadResolution_SelectsTaskForAsyncLambdas()
    {
        int[] items = [10, 20];
#pragma warning disable xUnit1051 // Verify calls that deliberately omit the optional cancellation token.
        Task<Result<IReadOnlyList<int>>> task = items.TraverseAllAsync(async (value, index, ct) =>
        {
            await Task.Yield();
            ct.Should().Be(CancellationToken.None);
            return Result.Ok(value + index);
        });
        Task<Result<Unit>> unitTask = items.TraverseAllAsync(async (_, index, ct) =>
        {
            await Task.Yield();
            ct.Should().Be(CancellationToken.None);
            return Result.Ensure(index >= 0, () => new Error.Forbidden("negative-index"));
        });
        ValueTask<Result<IReadOnlyList<int>>> valueTask = items.TraverseAllAsync(
            (value, index, _) => ValueTask.FromResult(Result.Ok(value + index)));
#pragma warning restore xUnit1051

        (await task).Should().BeSuccess().Which.Should().Equal([10, 21]);
        (await unitTask).Should().BeSuccess();
        (await valueTask).Should().BeSuccess().Which.Should().Equal([10, 21]);
    }

    [Fact]
    public async Task TraverseAllAsync_Unindexed_TwoParameterSelectors_StillReceiveToken()
    {
        int[] items = [1];
        using var cts = new CancellationTokenSource();
        var task = items.TraverseAllAsync((_, token) => Task.FromResult(Result.Ok(token)), cts.Token);
        var valueTask = items.TraverseAllAsync((_, token) => ValueTask.FromResult(Result.Ok(token)), cts.Token);
        Task<Result<Unit>> unitTask = items.TraverseAllAsync((_, token) =>
        {
            token.Should().Be(cts.Token);
            return Task.FromResult(Result.Ok());
        }, cts.Token);

        (await task).Should().BeSuccess().Which.Should().Equal([cts.Token]);
        (await valueTask).Should().BeSuccess().Which.Should().Equal([cts.Token]);
        (await unitTask).Should().BeSuccess();
    }

    [Theory]
    [InlineData(AsyncForm.Task, true)]
    [InlineData(AsyncForm.Task, false)]
    [InlineData(AsyncForm.ValueTask, true)]
    [InlineData(AsyncForm.ValueTask, false)]
    [InlineData(AsyncForm.UnitTask, true)]
    [InlineData(AsyncForm.UnitTask, false)]
    public async Task TraverseAllAsync_Indexed_Tracing_UsesOneTraversalActivity(AsyncForm form, bool success)
    {
        using var tracing = new ActivityTestHelper();
        var result = await RunAsync(form, [0, 1], (value, index, _) =>
            Task.FromResult(success || index == 1
                ? Result.Ok(value)
                : Result.Fail<int>(new Error.Forbidden("denied"))), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().Be(success);
        var activity = tracing.CapturedActivities.Where(a => a.DisplayName == "TraverseAllAsync")
            .Should().ContainSingle().Which;
        activity.Status.Should().Be(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    private static async Task<IResult> RunAsync(
        AsyncForm form,
        IEnumerable<int> source,
        Func<int, int, CancellationToken, Task<Result<int>>> selector,
        CancellationToken cancellationToken = default)
    {
        switch (form)
        {
            case AsyncForm.Task:
                return await source.TraverseAllAsync(selector, cancellationToken);
            case AsyncForm.ValueTask:
                return await source.TraverseAllAsync(
                    (value, index, ct) => new ValueTask<Result<int>>(selector(value, index, ct)), cancellationToken);
            case AsyncForm.UnitTask:
                return await source.TraverseAllAsync(
                    async (value, index, ct) => (await selector(value, index, ct)).Map(_ => Unit.Default), cancellationToken);
            default:
                throw new ArgumentOutOfRangeException(nameof(form));
        }
    }
}
