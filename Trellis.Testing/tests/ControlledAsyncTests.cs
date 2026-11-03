namespace Trellis.Testing.Tests;

public class ControlledAsyncTests
{
    [Fact]
    public async Task TryComplete_PendingReceivers_CompleteWithOriginalValueAndRejectLaterTransitions()
    {
        var source = new ControlledAsync<object>();
        var expected = new object();
        var valueTask = source.ValueTask;
        source.Task.IsCompleted.Should().BeFalse();
        valueTask.IsCompleted.Should().BeFalse();

        source.TryComplete(expected).Should().BeTrue();
        source.TryComplete(new object()).Should().BeFalse();
        source.TryFail(new InvalidOperationException()).Should().BeFalse();
        source.TryCancel(TestContext.Current.CancellationToken).Should().BeFalse();

        (await source.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).Should().BeSameAs(expected);
        (await valueTask).Should().BeSameAs(expected);
    }

    [Fact]
    public async Task TryFail_OriginalException_FaultsBothReceiversWithoutWrapping()
    {
        var source = new ControlledAsync<int>();
        var exception = new InvalidOperationException("test.source");
        source.TryFail(exception).Should().BeTrue();
        Func<Task> task = async () => await source.Task;
        Func<Task> valueTask = async () => await source.ValueTask;

        (await task.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
        (await valueTask.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task TryCancel_OriginalToken_CancelsBothReceivers()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new ControlledAsync<int>();
        source.TryCancel(cancellation.Token).Should().BeTrue();
        Func<Task> task = async () => await source.Task;
        Func<Task> valueTask = async () => await source.ValueTask;

        (await task.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
        (await valueTask.Should().ThrowAsync<OperationCanceledException>()).Which.CancellationToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task TryComplete_SynchronousContinuation_DoesNotRunInsideCompletion()
    {
        var source = new ControlledAsync<int>();
        using var completing = new ThreadLocal<bool>(() => false);
        var continuation = source.Task.ContinueWith(_ => completing.Value,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        completing.Value = true;
        try
        {
            source.TryComplete(42).Should().BeTrue();
        }
        finally
        {
            completing.Value = false;
            source.TryComplete(42);
        }

        (await continuation.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public void TryFail_NullException_RejectsWithoutCompleting()
    {
        var source = new ControlledAsync<int>();
        var act = () => source.TryFail(null!);

        try
        {
            act.Should().Throw<ArgumentNullException>().WithParameterName("exception");
            source.Task.IsCompleted.Should().BeFalse();
        }
        finally
        {
            source.TryComplete(42);
        }
    }
}