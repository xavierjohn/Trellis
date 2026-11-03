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
        var source = new TaskCompletionSource<Maybe<object>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new object();
        var calls = 0;
        var conversion = source.Task.ToResultAsync(() =>
        {
            calls++;
            return new Error.Forbidden("test.absent");
        });

        try
        {
            conversion.IsCompleted.Should().BeFalse();
            calls.Should().Be(0);
        }
        finally
        {
            source.TrySetResult(Maybe.From(expected));
        }

        var result = await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        result.Should().BeSuccess().Which.Should().BeSameAs(expected);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_Factory_PendingNone_DefersAndPreservesError()
    {
        var source = new TaskCompletionSource<Maybe<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new Error.Forbidden("test.absent");
        var calls = 0;
        var conversion = new ValueTask<Maybe<int>>(source.Task).ToResultAsync(() =>
        {
            calls++;
            return expected;
        }).AsTask();

        try
        {
            conversion.IsCompleted.Should().BeFalse();
            calls.Should().Be(0);
        }
        finally
        {
            source.TrySetResult(Maybe<int>.None);
        }

        var result = await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        result.Should().BeFailure().Which.Should().BeSameAs(expected);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task ToResultAsync_Task_Factory_FaultedReceiver_PreservesExceptionWithoutFactory()
    {
        var exception = new InvalidOperationException("receiver-source-fault");
        var calls = 0;
        var conversion = Task.FromException<Maybe<int>>(exception).ToResultAsync(() =>
        {
            calls++;
            return new Error.Forbidden("test.absent");
        });
        var act = async () => await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var failure = await act.Should().ThrowAsync<InvalidOperationException>();

        failure.Which.Should().BeSameAs(exception);
        calls.Should().Be(0);
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_Factory_CanceledReceiver_PreservesTokenWithoutFactory()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var conversion = ValueTask.FromCanceled<Maybe<int>>(cancellation.Token).ToResultAsync(() =>
        {
            calls++;
            return new Error.Forbidden("test.absent");
        }).AsTask();
        var act = async () => await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var failure = await act.Should().ThrowAsync<OperationCanceledException>();

        failure.Which.CancellationToken.Should().Be(cancellation.Token);
        calls.Should().Be(0);
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
        var source = new TaskCompletionSource<Maybe<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var conversion = source.Task.ToResultAsync((Func<Error>)null!);

        try
        {
            var act = async () => await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("ferror");
            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TrySetResult(Maybe<int>.None);
        }
    }

    [Fact]
    public async Task ToResultAsync_ValueTask_Factory_PendingReceiver_RejectsNullFactoryBeforeAwait()
    {
        var source = new TaskCompletionSource<Maybe<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var conversion = new ValueTask<Maybe<int>>(source.Task).ToResultAsync((Func<Error>)null!).AsTask();

        try
        {
            var act = async () => await conversion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("ferror");
            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TrySetResult(Maybe<int>.None);
        }
    }
}