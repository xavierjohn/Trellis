namespace Trellis.Mediator.Tests;

/// <summary>
/// Tests for the default <see cref="IIntegrationEventCollector"/> (<see cref="IntegrationEventCollector"/>).
/// </summary>
public class IntegrationEventCollectorTests
{
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
        var first = new CollectorTestEvent(1, DateTimeOffset.UnixEpoch);
        var second = new CollectorTestEvent(2, DateTimeOffset.UnixEpoch);

        collector.Add(first);
        collector.Add(second);

        collector.DrainPending().Should().Equal(first, second);

        // A second drain in the same scope observes nothing — the buffer was cleared.
        collector.DrainPending().Should().BeEmpty();
    }

    [Fact]
    public void DrainPending_WhenEmpty_ReturnsEmpty()
    {
        var collector = new IntegrationEventCollector();

        collector.DrainPending().Should().BeEmpty();
    }

    private sealed record CollectorTestEvent(int Id, DateTimeOffset OccurredAt) : IIntegrationEvent;
}