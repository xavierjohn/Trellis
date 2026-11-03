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
        var conversion = source.Task.ToResultAsync(calls.Wrap<Error>(() => new Error.Forbidden("test.absent")));

        try
        {
            conversion.IsCompleted.Should().BeFalse();
            calls.CallCount.Should().Be(0);
        }
        finally
        {
            source.TryComplete(Maybe.From(expected));
        }

        (await conversion.BeSuccessAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken))
            .Which.Should().BeSameAs(expected);
        calls.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_Factory_PendingNone_DefersAndPreservesError()
    {
        var source = new ControlledAsync<Maybe<int>>();
        var expected = new Error.Forbidden("test.absent");
        var calls = new CallbackRecorder();
        var conversion = source.ValueTask.ToResultAsync(calls.Wrap<Error>(() => expected)).AsTask();

        try
        {
            conversion.IsCompleted.Should().BeFalse();
            calls.CallCount.Should().Be(0);
        }
        finally
        {
            source.TryComplete(Maybe<int>.None);
        }

        (await conversion.BeFailureAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken))
            .Which.Should().BeSameAs(expected);
        calls.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ToResultAsync_Task_Factory_FaultedReceiver_PreservesExceptionWithoutFactory()
    {
        var exception = new InvalidOperationException("receiver-source-fault");
        var calls = new CallbackRecorder();
        var conversion = Task.FromException<Maybe<int>>(exception)
            .ToResultAsync(calls.Wrap<Error>(() => new Error.Forbidden("test.absent")));
        var act = async () => await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var failure = await act.Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Should().BeSameAs(exception);
        calls.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_Factory_CanceledReceiver_PreservesTokenWithoutFactory()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = new CallbackRecorder();
        var conversion = ValueTask.FromCanceled<Maybe<int>>(cancellation.Token)
            .ToResultAsync(calls.Wrap<Error>(() => new Error.Forbidden("test.absent"))).AsTask();
        var act = async () => await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var failure = await act.Should().ThrowAsync<OperationCanceledException>();

        failure.Which.CancellationToken.Should().Be(cancellation.Token);
        calls.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_Error_FaultedReceiver_PreservesException()
    {
        var exception = new InvalidOperationException("receiver-source-fault");
        var conversion = ValueTask.FromException<Maybe<int>>(exception)
            .ToResultAsync(new Error.Forbidden("test.absent")).AsTask();
        var act = async () => await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var failure = await act.Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task ToResultAsync_Task_Error_CanceledReceiver_PreservesToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var conversion = Task.FromCanceled<Maybe<int>>(cancellation.Token)
            .ToResultAsync(new Error.Forbidden("test.absent"));
        var act = async () => await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var failure = await act.Should().ThrowAsync<OperationCanceledException>();

        failure.Which.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task ToResultAsync_Task_Factory_PendingReceiver_RejectsNullFactoryBeforeAwait()
    {
        var source = new ControlledAsync<Maybe<int>>();
        var conversion = source.Task.ToResultAsync((Func<Error>)null!);

        try
        {
            var act = async () => await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("ferror");
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
        var conversion = source.ValueTask.ToResultAsync((Func<Error>)null!).AsTask();

        try
        {
            var act = async () => await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("ferror");
            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TryComplete(Maybe<int>.None);
        }
    }
}