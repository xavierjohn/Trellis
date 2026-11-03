namespace Trellis.Core.Tests.Maybes;

using Trellis.Testing;

public class MaybeToResultTests
{
    [Fact]
    public void ToResult_Func_WhenSomeAndFactoryIsNull_ThrowsArgumentNullException()
    {
        var maybe = Maybe.From(42);

        var act = () => maybe.ToResult((Func<Error>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ferror");
    }

    [Fact]
    public void ToResult_Func_WhenNoneAndFactoryIsNull_ThrowsArgumentNullException()
    {
        var maybe = Maybe<int>.None;

        var act = () => maybe.ToResult((Func<Error>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ferror");
    }

    [Fact]
    public async Task ToResultAsync_Task_WhenFactoryIsNull_ThrowsArgumentNullException()
    {
        var maybeTask = Task.FromResult(Maybe.From(42));

        var act = async () => await maybeTask.ToResultAsync((Func<Error>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("ferror");
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_WhenFactoryIsNull_ThrowsArgumentNullException()
    {
        var maybeTask = ValueTask.FromResult(Maybe.From(42));

        var act = async () => await maybeTask.ToResultAsync((Func<Error>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("ferror");
    }

    [Fact]
    public async Task ToResultAsync_Task_Factory_PendingSome_DefersAndSkipsFactory()
    {
        var source = new ControlledAsync<Maybe<object>>();
        var expected = new object();
        var calls = new CallbackRecorder();

        var result = await source.Task.ToResultAsync(calls.Wrap<Error>(() => new Error.Forbidden("test.absent")))
            .ShouldBePendingThenReleaseAsync(
                () => source.TryComplete(Maybe.From(expected)),
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken,
                whilePending: () => calls.CallCount.Should().Be(0));

        result.Should().BeSuccess().Which.Should().BeSameAs(expected);
        calls.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_Factory_PendingNone_DefersAndPreservesError()
    {
        var source = new ControlledAsync<Maybe<int>>();
        var expected = new Error.Forbidden("test.absent");
        var calls = new CallbackRecorder();

        var result = await source.ValueTask.ToResultAsync(calls.Wrap<Error>(() => expected))
            .ShouldBePendingThenReleaseAsync(
                () => source.TryComplete(Maybe<int>.None),
                TimeSpan.FromSeconds(30),
                TestContext.Current.CancellationToken,
                whilePending: () => calls.CallCount.Should().Be(0));

        result.Should().BeFailure().Which.Should().BeSameAs(expected);
        calls.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ToResultAsync_Task_Factory_FaultedReceiver_PreservesExceptionWithoutFactory()
    {
        var exception = new InvalidOperationException("receiver-source-fault");
        var calls = new CallbackRecorder();

        await Task.FromException<Maybe<int>>(exception)
            .ToResultAsync(calls.Wrap<Error>(() => new Error.Forbidden("test.absent")))
            .ShouldPropagateExceptionAsync(exception, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        calls.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_Factory_CanceledReceiver_PreservesTokenWithoutFactory()
    {
        using var canceled = CanceledToken.Create();
        var calls = new CallbackRecorder();

        await canceled.ToValueTask<Maybe<int>>()
            .ToResultAsync(calls.Wrap<Error>(() => new Error.Forbidden("test.absent")))
            .ShouldPropagateCancellationAsync(canceled.Token, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        calls.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_Error_FaultedReceiver_PreservesException()
    {
        var exception = new InvalidOperationException("receiver-source-fault");

        await ValueTask.FromException<Maybe<int>>(exception)
            .ToResultAsync(new Error.Forbidden("test.absent"))
            .ShouldPropagateExceptionAsync(exception, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ToResultAsync_Task_Error_CanceledReceiver_PreservesToken()
    {
        using var canceled = CanceledToken.Create();

        await canceled.ToTask<Maybe<int>>()
            .ToResultAsync(new Error.Forbidden("test.absent"))
            .ShouldPropagateCancellationAsync(canceled.Token, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ToResultAsync_Task_Factory_PendingReceiver_RejectsNullFactoryBeforeAwait()
    {
        var source = new ControlledAsync<Maybe<int>>();
        try
        {
            Func<Task> start = () => source.Task.ToResultAsync((Func<Error>)null!);

            await start.ShouldRejectNullBeforeAwaitAsync("ferror");

            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TryComplete(Maybe<int>.None);
        }
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_Factory_PendingReceiver_RejectsNullFactoryBeforeAwait()
    {
        var source = new ControlledAsync<Maybe<int>>();
        try
        {
            Func<Task> start = () => source.ValueTask.ToResultAsync((Func<Error>)null!).AsTask();

            await start.ShouldRejectNullBeforeAwaitAsync("ferror");

            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TryComplete(Maybe<int>.None);
        }
    }
}