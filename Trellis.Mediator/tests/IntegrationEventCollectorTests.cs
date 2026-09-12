namespace Trellis.Mediator.Tests;

/// <summary>
/// Tests for the default <see cref="IIntegrationEventCollector"/> (<see cref="IntegrationEventCollector"/>).
/// </summary>
public class IntegrationEventCollectorTests
{
    [Fact]
    public void Add_OutsideRelayTranslation_ThrowsInsteadOfLosingEvent()
    {
        var collector = new IntegrationEventCollector();
        var act = () => collector.Add(new CollectorTestEvent(1, DateTimeOffset.UnixEpoch));
        act.Should().Throw<InvalidOperationException>().WithMessage("*translation*");
        collector.DrainPending().Should().BeEmpty();
    }

    [Fact]
    public void Add_NullEvent_Throws()
    {
        var collector = new IntegrationEventCollector();

        var act = () => collector.Add(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DrainPending_ReturnsInInsertionOrder_AndClears()
    {
        var collector = new IntegrationEventCollector();
        using var translation = collector.BeginTranslation();
        var first = new CollectorTestEvent(1, DateTimeOffset.UnixEpoch);
        var second = new CollectorTestEvent(2, DateTimeOffset.UnixEpoch);

        collector.Add(first);
        collector.Add(second);

        collector.DrainPending().Should().Equal(first, second);

        // A second drain in the same scope observes nothing — the buffer was cleared.
        collector.DrainPending().Should().BeEmpty();
    }

    [Fact]
    public void Add_AfterTranslationEnds_ThrowsAndDropsUndrainedBuffer()
    {
        var collector = new IntegrationEventCollector();
        using (collector.BeginTranslation())
            collector.Add(new CollectorTestEvent(1, DateTimeOffset.UnixEpoch));

        var act = () => collector.Add(new CollectorTestEvent(2, DateTimeOffset.UnixEpoch));
        act.Should().Throw<InvalidOperationException>();
        using var next = collector.BeginTranslation();
        collector.DrainPending().Should().BeEmpty();
    }

    [Fact]
    public async Task Add_ChildExecutionContextOutlivesTranslation_Throws()
    {
        var collector = new IntegrationEventCollector();
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task child;
        using (collector.BeginTranslation())
        {
            child = Task.Run(async () =>
            {
                await resume.Task;
                var act = () => collector.Add(new CollectorTestEvent(1, DateTimeOffset.UnixEpoch));
                act.Should().Throw<InvalidOperationException>();
            }, TestContext.Current.CancellationToken);
        }

        resume.SetResult();
        await child;
    }

    [Fact]
    public void BeginTranslation_NestedLease_Throws()
    {
        var collector = new IntegrationEventCollector();
        using var translation = collector.BeginTranslation();
        var act = () => collector.BeginTranslation();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DrainPending_WhenEmpty_ReturnsEmpty()
    {
        var collector = new IntegrationEventCollector();

        collector.DrainPending().Should().BeEmpty();
    }

    private sealed record CollectorTestEvent(int Id, DateTimeOffset OccurredAt) : IIntegrationEvent;
}