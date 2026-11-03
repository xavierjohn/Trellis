namespace Trellis.Testing.Tests;

public class ControlledAsyncSingleUseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SingleUse_PendingUntilCompleted_DeliversValueAndCountsOneRead()
    {
        var source = ControlledAsync.SingleUse<int>();
        var valueTask = source.ValueTask;

        source.IsSingleUse.Should().BeTrue();
        valueTask.IsCompleted.Should().BeFalse();
        source.ResultReadCount.Should().Be(0);

        source.TryComplete(42).Should().BeTrue();

        (await valueTask.AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token)).Should().Be(42);
        source.ResultReadCount.Should().Be(1);
    }

    [Fact]
    public async Task SingleUse_LaterTransitions_AreRejectedWithoutChangingOutcome()
    {
        var source = ControlledAsync.SingleUse<int>();
        source.TryComplete(1).Should().BeTrue();

        source.TryComplete(2).Should().BeFalse();
        source.TryFail(new InvalidOperationException()).Should().BeFalse();
        source.TryCancel(Token).Should().BeFalse();

        (await source.ValueTask.AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token)).Should().Be(1);
    }

    [Fact]
    public async Task SingleUse_SecondConsumption_ThrowsAndIsCounted()
    {
        var source = ControlledAsync.SingleUse<int>();
        source.TryComplete(7);
        (await source.ValueTask).Should().Be(7);

        var act = async () => await source.ValueTask;

        await act.Should().ThrowAsync<InvalidOperationException>();
        source.ResultReadCount.Should().Be(2);
    }

    [Fact]
    public async Task SingleUse_TryFail_PreservesOriginalExceptionAndCountsRead()
    {
        var source = ControlledAsync.SingleUse<int>();
        var exception = new InvalidOperationException("single-use.fault");
        source.TryFail(exception).Should().BeTrue();

        await source.ValueTask.ShouldPropagateExceptionAsync(exception);

        source.ResultReadCount.Should().Be(1);
    }

    [Fact]
    public async Task SingleUse_TryCancel_PreservesTokenAndCountsRead()
    {
        using var canceled = CanceledToken.Create();
        var source = ControlledAsync.SingleUse<int>();
        source.TryCancel(canceled.Token).Should().BeTrue();

        await source.ValueTask.ShouldPropagateCancellationAsync(canceled.Token);

        source.ResultReadCount.Should().Be(1);
    }

    [Fact]
    public void SingleUse_TaskReceiver_IsUnavailable()
    {
        var source = ControlledAsync.SingleUse<int>();

        Action act = () => _ = source.Task;

        act.Should().Throw<InvalidOperationException>().WithMessage("*single-use*");
    }

    [Fact]
    public void SingleUse_TryFailNull_ThrowsWithoutCompleting()
    {
        var source = ControlledAsync.SingleUse<int>();

        var act = () => source.TryFail(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("exception");
        source.TryComplete(1).Should().BeTrue();
    }

    [Fact]
    public async Task SingleUse_ConcurrentTransitions_ExactlyOneWins()
    {
        var source = ControlledAsync.SingleUse<int>();
        var winners = 0;

        Parallel.For(0, 32, attempt =>
        {
            if (source.TryComplete(attempt))
                Interlocked.Increment(ref winners);
        });

        winners.Should().Be(1);
        _ = await source.ValueTask.AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token);
        source.ResultReadCount.Should().Be(1);
    }

    [Fact]
    public async Task SingleUse_Completion_DoesNotRunContinuationsInline()
    {
        var source = ControlledAsync.SingleUse<int>();
        using var completing = new ThreadLocal<bool>(() => false);
        var continuation = source.ValueTask.AsTask().ContinueWith(_ => completing.Value,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        completing.Value = true;
        try
        {
            source.TryComplete(1).Should().BeTrue();
        }
        finally
        {
            completing.Value = false;
        }

        (await continuation.WaitAsync(TimeSpan.FromSeconds(30), Token)).Should().BeFalse();
    }

    [Fact]
    public async Task SharedMode_IsNotSingleUse_AndReportsNoReads()
    {
        var source = new ControlledAsync<int>();
        source.TryComplete(5);

        source.IsSingleUse.Should().BeFalse();
        (await source.ValueTask).Should().Be(5);
        (await source.ValueTask).Should().Be(5);
        source.ResultReadCount.Should().Be(0);
    }

    [Fact]
    public async Task SingleUse_ReadBeforeCompletion_IsNotCountedAsAConsumption()
    {
        var source = ControlledAsync.SingleUse<int>();
        var premature = () => source.ValueTask.GetAwaiter().GetResult();

        premature.Should().Throw<InvalidOperationException>();
        source.ResultReadCount.Should().Be(0);

        source.TryComplete(9);
        (await source.ValueTask.AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token)).Should().Be(9);
        source.ResultReadCount.Should().Be(1);
    }
}