namespace Trellis.Testing.Tests;

using Xunit.Sdk;

public class DomainEventAssertionsTests
{
    private sealed record OrderPlaced(string OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed record OrderShipped(string OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

    private sealed class Order : Aggregate<string>
    {
        public Order(string id) : base(id) { }

        public Order Place()
        {
            DomainEvents.Add(new OrderPlaced(Id, DateTimeOffset.UnixEpoch));
            return this;
        }

        public Order Ship()
        {
            DomainEvents.Add(new OrderShipped(Id, DateTimeOffset.UnixEpoch));
            return this;
        }
    }

    [Fact]
    public void HaveRaised_ExactlyOneMatch_ReturnsTheEvent()
    {
        var order = new Order("o-1").Place();

        var raised = order.UncommittedEvents().Should().HaveRaised<OrderPlaced>();

        raised.Which.OrderId.Should().Be("o-1");
    }

    [Fact]
    public void HaveRaised_Predicate_NarrowsTheMatch()
    {
        var events = new Order("o-1").Place().UncommittedEvents()
            .Concat(new Order("o-2").Place().UncommittedEvents()).ToArray();

        var raised = events.Should().HaveRaised<OrderPlaced>(placed => placed.OrderId == "o-2");

        raised.Which.OrderId.Should().Be("o-2");
    }

    [Fact]
    public void HaveRaised_PredicateChangesSource_EvaluatesEntireSnapshotInOrder()
    {
        var first = new OrderPlaced("first", DateTimeOffset.UnixEpoch);
        var last = new OrderPlaced("last", DateTimeOffset.UnixEpoch);
        List<IDomainEvent> events = [first, new OrderShipped("first", DateTimeOffset.UnixEpoch), last];
        var visited = new List<OrderPlaced>();

        var raised = events.Should().HaveRaised<OrderPlaced>(placed =>
        {
            visited.Add(placed);
            events.Clear();
            return ReferenceEquals(placed, first);
        });

        visited.Should().Equal([first, last]);
        raised.Which.Should().BeSameAs(first);
    }

    [Fact]
    public void HaveRaised_MixedEvents_DoesNotAllocateAMatchCollection()
    {
        IDomainEvent[] events =
        [
            new OrderPlaced("first", DateTimeOffset.UnixEpoch),
            new OrderShipped("first", DateTimeOffset.UnixEpoch),
            new OrderPlaced("second", DateTimeOffset.UnixEpoch),
            new OrderShipped("second", DateTimeOffset.UnixEpoch)
        ];

        var countOnly = Measure(() => events.Should().HaveRaisedCount<OrderPlaced>(2));
        var withMatch = Measure(() => events.Should().HaveRaised<OrderPlaced>(static evt => evt.OrderId == "first"));

        withMatch.Should().BeLessThanOrEqualTo(countOnly + (countOnly / 10));

        static long Measure(Func<object> assertion)
        {
            _ = Sample(assertion);
            return Math.Min(Sample(assertion), Sample(assertion));
        }

        static long Sample(Func<object> assertion)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 128; i++)
                GC.KeepAlive(assertion());
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    [Fact]
    public void HaveRaised_NoMatch_FailsNamingTheEventTypeAndWhatWasRaised()
    {
        var events = new Order("o-1").Ship().UncommittedEvents();

        var act = () => events.Should().HaveRaised<OrderPlaced>();

        act.Should().Throw<XunitException>().WithMessage("*OrderPlaced*OrderShipped*");
    }

    [Fact]
    public void HaveRaised_MoreThanOneMatch_FailsWithCount()
    {
        var events = new Order("o-1").Place().Place().UncommittedEvents();

        var act = () => events.Should().HaveRaised<OrderPlaced>();

        act.Should().Throw<XunitException>().WithMessage("*exactly one*2*");
    }

    [Fact]
    public void NotHaveRaised_AbsentEvent_Passes()
    {
        var events = new Order("o-1").Place().UncommittedEvents();

        events.Should().NotHaveRaised<OrderShipped>();
    }

    [Fact]
    public void NotHaveRaised_PresentEvent_Fails()
    {
        var events = new Order("o-1").Place().UncommittedEvents();

        var act = () => events.Should().NotHaveRaised<OrderPlaced>();

        act.Should().Throw<XunitException>().WithMessage("*OrderPlaced*");
    }

    [Fact]
    public void HaveRaisedCount_ReportsMismatch()
    {
        var events = new Order("o-1").Place().Place().Ship().UncommittedEvents();

        events.Should().HaveRaisedCount<OrderPlaced>(2);
        var act = () => events.Should().HaveRaisedCount<OrderPlaced>(1);

        act.Should().Throw<XunitException>().WithMessage("*1*2*");
    }

    [Fact]
    public void HaveRaisedExactlyInOrder_MatchingSequence_Passes()
    {
        var events = new Order("o-1").Place().Ship().UncommittedEvents();

        events.Should().HaveRaisedExactlyInOrder(typeof(OrderPlaced), typeof(OrderShipped));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HaveRaisedExactlyInOrder_WrongOrderMissingOrExtra_Fails(int variant)
    {
        var events = new Order("o-1").Place().Ship().UncommittedEvents();
        var expected = variant switch
        {
            0 => new[] { typeof(OrderShipped), typeof(OrderPlaced) },
            1 => new[] { typeof(OrderPlaced), typeof(OrderShipped), typeof(OrderShipped) },
            _ => new[] { typeof(OrderPlaced) },
        };

        var act = () => events.Should().HaveRaisedExactlyInOrder(expected);

        act.Should().Throw<XunitException>().WithMessage("*OrderPlaced*OrderShipped*");
    }

    [Fact]
    public void HaveNoEvents_AfterAcceptChanges_ViaBeEmpty()
    {
        var order = new Order("o-1").Place();
        order.AcceptChanges();

        order.UncommittedEvents().Should().BeEmpty();
    }

    [Fact]
    public void Assertions_WorkOnFakeRepositoryPublishedEvents()
    {
        var repository = new FakeRepository<Order, string>();
        repository.Add(new Order("o-1").Place());

        repository.PublishedEvents.Should().HaveRaised<OrderPlaced>().Which.OrderId.Should().Be("o-1");
    }
}