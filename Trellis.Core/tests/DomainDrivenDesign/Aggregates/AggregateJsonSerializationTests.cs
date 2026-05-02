namespace Trellis.Core.Tests.DomainDrivenDesign.Aggregates;

using System.Text.Json;
using Trellis;

/// <summary>
/// Pins down whether <see cref="Aggregate{TId}"/> leaks <c>DomainEvents</c> through
/// System.Text.Json by default (CORE-DDD-015 candidate).
/// </summary>
public class AggregateJsonSerializationTests
{
    [Fact]
    public void Serialize_AggregateWithRaisedEvents_DoesNotIncludeDomainEventsCollection()
    {
        // Arrange
        var aggregate = JsonTestAggregate.Create("agg-1", "name");
        aggregate.DoSomething();
        aggregate.DoSomething();

        // Act — default options (most realistic API leak path)
        var json = JsonSerializer.Serialize(aggregate);

        // Assert
        json.Should().NotContain("DomainEvents",
            "Aggregate.DomainEvents is internal bookkeeping and must not surface in API responses.");
        json.Should().NotContain("JsonTestEvent",
            "Concrete event payloads must not leak in the aggregate's JSON shape.");
    }

    [Fact]
    public void Serialize_AggregateWithRaisedEvents_DoesNotIncludeDomainEventsViaRuntimeType()
    {
        // Arrange — System.Text.Json walks the runtime type's properties; this exercises that path.
        Aggregate<string> aggregate = JsonTestAggregate.Create("agg-2", "name");
        ((JsonTestAggregate)aggregate).DoSomething();

        // Act
        var json = JsonSerializer.Serialize(aggregate, aggregate.GetType());

        // Assert
        json.Should().NotContain("DomainEvents",
            "Even when serialized via runtime type, DomainEvents must remain hidden from JSON output.");
    }
}

#region Test Aggregate and Events

internal record JsonTestEvent(string AggregateId, DateTimeOffset OccurredAt) : IDomainEvent;

internal class JsonTestAggregate : Aggregate<string>
{
    public string Name { get; private set; }

    private JsonTestAggregate(string id, string name) : base(id) => Name = name;

    public static JsonTestAggregate Create(string id, string name) => new(id, name);

    public void DoSomething()
    {
        Name = $"{Name}_modified";
        DomainEvents.Add(new JsonTestEvent(Id, DateTimeOffset.UtcNow));
    }
}

#endregion
