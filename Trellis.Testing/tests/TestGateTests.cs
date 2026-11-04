namespace Trellis.Testing.Tests;

public class TestGateTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ArriveAsync_BlocksUntilReleased_AndRecordsTheArrival()
    {
        using var gate = new TestGate();
        var flow = Task.Run(() => gate.ArriveAsync(Token), Token);

        await gate.WaitForArrivalAsync(Wait, Token);

        flow.IsCompleted.Should().BeFalse();
        gate.ArrivalCount.Should().Be(1);

        gate.Release();
        await flow.WaitAsync(Wait, Token);
    }

    [Fact]
    public async Task WaitForArrivalAsync_AlreadyArrived_CompletesImmediately()
    {
        using var gate = new TestGate();
        gate.Signal();

        await gate.WaitForArrivalAsync(Wait, Token);

        gate.ArrivalCount.Should().Be(1);
    }

    [Fact]
    public async Task WaitForArrivalAsync_NoArrival_TimesOutNamingTheCounts()
    {
        using var gate = new TestGate();
        Func<Task> act = () => gate.WaitForArrivalAsync(TimeSpan.FromMilliseconds(50), Token);

        await act.Should().ThrowAsync<TimeoutException>().WithMessage("*1*0*");
    }

    [Fact]
    public async Task WaitForArrivalsAsync_CompletesOnlyWhenTheCountIsReached()
    {
        using var gate = new TestGate();
        var both = gate.WaitForArrivalsAsync(2, Wait, Token);

        gate.Signal();
        both.IsCompleted.Should().BeFalse();
        gate.Signal();

        await both.WaitAsync(Wait, Token);
        gate.ArrivalCount.Should().Be(2);
    }

    [Fact]
    public async Task Signal_RepeatedArrivals_DoNotThrowAndAreCounted()
    {
        using var gate = new TestGate();

        gate.Signal();
        gate.Signal();
        gate.Signal();

        gate.ArrivalCount.Should().Be(3);
        await gate.WaitForArrivalsAsync(3, Wait, Token);
    }

    [Fact]
    public async Task Signal_ConcurrentArrivals_AreAllCounted()
    {
        using var gate = new TestGate();

        Parallel.For(0, 64, _ => gate.Signal());

        gate.ArrivalCount.Should().Be(64);
        await gate.WaitForArrivalsAsync(64, Wait, Token);
    }

    [Fact]
    public async Task Release_Permanent_LaterArrivalsPassThrough()
    {
        using var gate = new TestGate();
        gate.Release();

        await gate.ArriveAsync(Token).WaitAsync(Wait, Token);
        await gate.WaitUntilReleasedAsync(Token).WaitAsync(Wait, Token);

        gate.ArrivalCount.Should().Be(1);
    }

    [Fact]
    public async Task Release_NotPermanent_FreesCurrentWaitersButRearmsForLaterOnes()
    {
        using var gate = new TestGate(permanentRelease: false);
        var first = gate.ArriveAsync(Token);
        gate.Release();
        await first.WaitAsync(Wait, Token);

        var second = gate.ArriveAsync(Token);

        second.IsCompleted.Should().BeFalse();
        gate.Release();
        await second.WaitAsync(Wait, Token);
    }

    [Fact]
    public void ReleaseIsPermanent_DefaultsToTrue_AndReflectsTheFlag()
    {
        using var permanent = new TestGate();
        using var oneShot = new TestGate(permanentRelease: false);

        permanent.ReleaseIsPermanent.Should().BeTrue();
        oneShot.ReleaseIsPermanent.Should().BeFalse();
    }

    [Fact]
    public async Task Release_IsIdempotent()
    {
        using var gate = new TestGate();
        var flow = gate.ArriveAsync(Token);

        gate.Release();
        gate.Release();

        await flow.WaitAsync(Wait, Token);
    }

    [Fact]
    public async Task Dispose_ReleasesWaitersEvenWhenNotPermanent_AndFutureArrivalsPass()
    {
        var gate = new TestGate(permanentRelease: false);
        var stranded = gate.ArriveAsync(Token);

        gate.Dispose();

        await stranded.WaitAsync(Wait, Token);
        await gate.ArriveAsync(Token).WaitAsync(Wait, Token);
        gate.Dispose();
    }

    [Fact]
    public async Task WaitUntilReleasedAsync_CanceledToken_ThrowsWithoutReleasing()
    {
        using var gate = new TestGate();
        using var canceled = CanceledToken.Create();
        Func<Task> act = () => gate.WaitUntilReleasedAsync(canceled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        var later = gate.WaitUntilReleasedAsync(Token);
        later.IsCompleted.Should().BeFalse();
        gate.Release();
        await later.WaitAsync(Wait, Token);
    }

    [Fact]
    public async Task ArriveAsync_CanceledWhileWaiting_StillCountedAsAnArrival()
    {
        using var gate = new TestGate();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var flow = gate.ArriveAsync(source.Token);
        await gate.WaitForArrivalAsync(Wait, Token);

        await source.CancelAsync();

        Func<Task> act = () => flow;
        await act.Should().ThrowAsync<OperationCanceledException>();
        gate.ArrivalCount.Should().Be(1);
    }

    [Fact]
    public async Task WaitForArrivalsAsync_InvalidCount_Throws()
    {
        using var gate = new TestGate();
        Func<Task> act = () => gate.WaitForArrivalsAsync(0, Wait, Token);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("count");
    }

    [Fact]
    public async Task ArriveAsync_ReleaseRightAfterArrival_NeverStrandsTheArriver()
    {
        using var gate = new TestGate(permanentRelease: false);
        var bound = TimeSpan.FromSeconds(5);

        for (var arrival = 1; arrival <= 30000; arrival++)
        {
            var flow = Task.Run(() => gate.ArriveAsync(Token), Token);
            await gate.WaitForArrivalsAsync(arrival, bound, Token);

            gate.Release();

            await flow.WaitAsync(bound, Token);
        }
    }
}