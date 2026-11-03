namespace Trellis.Testing.Tests;

using Xunit.Sdk;

public class PendingOperationAssertionsTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<int> AddOne(Task<int> source) => await source + 1;

    private static async ValueTask<int> AddOne(ValueTask<int> source) => await source + 1;

    private static async Task Await(Task source) => await source;

    [Fact]
    public async Task ShouldBePendingThenReleaseAsync_Task_RunsPendingChecksThenReleasesThenReturnsResult()
    {
        var source = new ControlledAsync<int>();
        var order = new List<string>();

        var result = await AddOne(source.Task).ShouldBePendingThenReleaseAsync(
            () =>
            {
                order.Add("release");
                source.TryComplete(1);
            },
            Wait,
            Token,
            whilePending: () => order.Add("pending"));

        result.Should().Be(2);
        order.Should().Equal(["pending", "release"]);
    }

    [Fact]
    public async Task ShouldBePendingThenReleaseAsync_ValueTask_ReturnsResult()
    {
        var source = new ControlledAsync<int>();

        var result = await AddOne(source.ValueTask).ShouldBePendingThenReleaseAsync(
            () => source.TryComplete(10), Wait, Token);

        result.Should().Be(11);
    }

    [Fact]
    public async Task ShouldBePendingThenReleaseAsync_NonGenericTask_Completes()
    {
        var source = new ControlledAsync<int>();

        await Await(source.Task).ShouldBePendingThenReleaseAsync(() => source.TryComplete(1), Wait, Token);

        source.Task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldBePendingThenReleaseAsync_AlreadyCompleted_FailsButStillReleases()
    {
        var released = false;
        Func<Task> act = async () => _ = await Task.FromResult(1).ShouldBePendingThenReleaseAsync(
            () => released = true, Wait, Token);

        await act.Should().ThrowAsync<XunitException>().WithMessage("*pending*");

        released.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldBePendingThenReleaseAsync_PendingCheckThrows_StillReleasesAndPropagates()
    {
        var source = new ControlledAsync<int>();
        var released = false;
        Func<Task> act = async () => _ = await AddOne(source.Task).ShouldBePendingThenReleaseAsync(
            () =>
            {
                released = true;
                source.TryComplete(1);
            },
            Wait,
            Token,
            whilePending: () => throw new InvalidOperationException("pending.check"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("pending.check");

        released.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldBePendingThenReleaseAsync_ReleaseNeverCompletes_ThrowsTimeout()
    {
        var source = new ControlledAsync<int>();
        try
        {
            Func<Task> act = async () => _ = await AddOne(source.Task).ShouldBePendingThenReleaseAsync(
                () => { }, TimeSpan.FromMilliseconds(50), Token);

            await act.Should().ThrowAsync<TimeoutException>();
        }
        finally
        {
            source.TryComplete(0);
        }
    }

    [Fact]
    public async Task ShouldBePendingThenReleaseAsync_NullArguments_Throw()
    {
        var source = new ControlledAsync<int>();
        try
        {
            Func<Task> nullRelease = async () => _ = await source.Task.ShouldBePendingThenReleaseAsync(null!, Wait, Token);
            Func<Task> nullOperation = async () => _ = await ((Task<int>)null!).ShouldBePendingThenReleaseAsync(() => { }, Wait, Token);

            await nullRelease.Should().ThrowAsync<ArgumentNullException>().WithParameterName("release");
            await nullOperation.Should().ThrowAsync<ArgumentNullException>().WithParameterName("operation");
        }
        finally
        {
            source.TryComplete(0);
        }
    }
}