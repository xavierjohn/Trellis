namespace Trellis.Mediator.Tests;

using System.Reflection;

#pragma warning disable CA1707 // readable xUnit test names

/// <summary>
/// Tests for <see cref="IntegrationEventNameMap"/> — the producer/consumer wire-name contract that lets a
/// broker transport identify an event without exchanging assembly-qualified type names.
/// </summary>
public sealed class IntegrationEventNameMapTests
{
    [Fact]
    public void FromAssemblies_MapsAnnotatedEventsInBothDirections()
    {
        var map = IntegrationEventNameMap.FromAssemblies(Assembly.GetExecutingAssembly());

        map.TypeFor("trellis.tests.named-event.v1").TryGetValue(out var type).Should().BeTrue();
        type.Should().Be<NamedIntegrationEvent>();

        map.NameFor(typeof(NamedIntegrationEvent)).TryGetValue(out var name).Should().BeTrue();
        name.Should().Be("trellis.tests.named-event.v1");
    }

    [Fact]
    public void FromAssemblies_SkipsEventsWithoutTheAttribute()
    {
        var map = IntegrationEventNameMap.FromAssemblies(Assembly.GetExecutingAssembly());

        // An unannotated event is in-process only by choice, not an error.
        map.NameFor(typeof(UnnamedIntegrationEvent)).HasValue.Should().BeFalse();
    }

    // A producer may emit contracts this consumer does not subscribe to; that is policy, not a crash.
    [Fact]
    public void TypeFor_UnknownName_ReturnsNone() =>
        IntegrationEventNameMap.Empty.TypeFor("orders.never-heard-of-it.v1").HasValue.Should().BeFalse();

    [Fact]
    public void TypeFor_IsCaseSensitive()
    {
        var map = new IntegrationEventNameMap([new("orders.order-placed.v1", typeof(NamedIntegrationEvent))]);

        map.TypeFor("Orders.Order-Placed.V1").HasValue.Should().BeFalse();
    }

    [Fact]
    public void Constructor_DuplicateName_Throws()
    {
        var act = () => new IntegrationEventNameMap(
        [
            new("orders.order-placed.v1", typeof(NamedIntegrationEvent)),
            new("orders.order-placed.v1", typeof(UnnamedIntegrationEvent)),
        ]);

        act.Should().Throw<ArgumentException>().WithMessage("*must identify exactly one type*");
    }

    [Fact]
    public void Constructor_SameTypeUnderTwoNames_Throws()
    {
        var act = () => new IntegrationEventNameMap(
        [
            new("orders.order-placed.v1", typeof(NamedIntegrationEvent)),
            new("orders.order-placed.v2", typeof(NamedIntegrationEvent)),
        ]);

        act.Should().Throw<ArgumentException>().WithMessage("*exactly one name*");
    }

    [Fact]
    public void Constructor_TypeThatIsNotAnIntegrationEvent_Throws()
    {
        var act = () => new IntegrationEventNameMap([new("orders.order-placed.v1", typeof(string))]);

        act.Should().Throw<ArgumentException>().WithMessage("*does not implement IIntegrationEvent*");
    }

    [Fact]
    public void Constructor_AbstractType_Throws()
    {
        var act = () => new IntegrationEventNameMap([new("orders.order-placed.v1", typeof(AbstractIntegrationEvent))]);

        act.Should().Throw<ArgumentException>().WithMessage("*not a concrete type*");
    }

    [Fact]
    public void Constructor_OpenGenericType_Throws()
    {
        // It is concrete, but it would be keyed under its type definition: NameFor(GenericIntegrationEvent<int>)
        // would miss, and TypeFor would return a type no deserializer can materialize. FromAssemblies routes
        // through this same constructor, so an annotated open generic is rejected there too.
        var act = () => new IntegrationEventNameMap(
            [new("orders.order-placed.v1", typeof(GenericIntegrationEvent<>))]);

        act.Should().Throw<ArgumentException>().WithMessage("*unbound generic parameters*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankName_Throws(string name)
    {
        var act = () => new IntegrationEventNameMap([new(name, typeof(NamedIntegrationEvent))]);

        act.Should().Throw<ArgumentException>();
    }
}

[IntegrationEventName("trellis.tests.named-event.v1")]
internal sealed record NamedIntegrationEvent(DateTimeOffset OccurredAt) : IIntegrationEvent;

internal sealed record UnnamedIntegrationEvent(DateTimeOffset OccurredAt) : IIntegrationEvent;

internal abstract record AbstractIntegrationEvent(DateTimeOffset OccurredAt) : IIntegrationEvent;

// Deliberately unannotated: scanning must skip it, so the suite's FromAssemblies tests stay unaffected.
internal sealed record GenericIntegrationEvent<T>(T Payload, DateTimeOffset OccurredAt) : IIntegrationEvent;
