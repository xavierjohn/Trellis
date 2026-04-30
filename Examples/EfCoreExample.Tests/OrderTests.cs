namespace EfCoreExample.Tests;

using EfCoreExample.Entities;
using EfCoreExample.Enums;
using EfCoreExample.Events;
using EfCoreExample.ValueObjects;
using Trellis.Testing;

public class OrderTests
{
    [Fact]
    public void Submit_DraftOrder_TransitionsToSubmitted()
    {
        var order = Order.TryCreate(CustomerId.NewUniqueV4()).Unwrap();

        var result = order.Submit(TimeProvider.System);

        result.Should().BeSuccess().Which.Should().BeSameAs(order);
        order.State.Should().Be(OrderState.Submitted);
    }

    [Fact]
    public void Submit_NonDraftOrder_ReturnsFailure()
    {
        var order = Order.TryCreate(CustomerId.NewUniqueV4()).Unwrap();
        order.Submit(TimeProvider.System).Should().BeSuccess();
        order.AcceptChanges();

        var result = order.Submit(TimeProvider.System);

        result.Should().BeFailure()
            .Which.Detail.Should().Be("Only draft orders can be submitted.");
        order.State.Should().Be(OrderState.Submitted);
        order.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public void Submit_DraftOrder_EmitsOrderSubmittedMetadata()
    {
        var timestamp = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var clock = new FixedTimeProvider(timestamp);
        var order = Order.TryCreate(CustomerId.NewUniqueV4()).Unwrap();

        order.Submit(clock).Should().BeSuccess();

        var domainEvent = order.UncommittedEvents().Should().ContainSingle().Which;
        var submitted = domainEvent.Should().BeOfType<OrderSubmitted>().Subject;
        submitted.OrderId.Should().Be(order.Id);
        submitted.OccurredAt.Should().Be(timestamp.UtcDateTime);
        submitted.OccurredAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }
}