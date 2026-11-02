namespace Trellis.Core.Tests.DomainDrivenDesign.Aggregates;

using System.Text.Json;
using Trellis;

/// <summary>
/// Verifies that both System.Text.Json entry points preserve domain fields without
/// serializing aggregate event bookkeeping.
/// </summary>
public class AggregateJsonSerializationTests
{
    [Fact]
    public void Serialize_ConcreteAggregate_PreservesDomainFieldsWithoutEventBookkeeping()
    {
        // Arrange
        var aggregate = JsonTestAggregate.Create("agg-1", "name");
        aggregate.DoSomething();
        aggregate.DoSomething();

        // Act — default options (most realistic API leak path)
        var json = JsonSerializer.Serialize(aggregate);

        aggregate.IsChanged.Should().BeTrue();
        aggregate.UncommittedEvents().Should().HaveCount(2);

        // Assert
        json.Should().NotContain("DomainEvents",
            "Aggregate.DomainEvents is internal bookkeeping and must not surface in API responses.");
        json.Should().NotContain("JsonTestEvent",
            "Concrete event payloads must not leak in the aggregate's JSON shape.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Object);
        root.TryGetProperty("Id", out var id).Should().BeTrue();
        id.GetString().Should().Be("agg-1");
        root.TryGetProperty("Name", out var name).Should().BeTrue();
        name.GetString().Should().Be("name_modified_modified");
        root.TryGetProperty("IsChanged", out _).Should().BeFalse();
    }

    [Fact]
    public void Serialize_RuntimeTypeAggregate_PreservesDomainFieldsWithoutEventBookkeeping()
    {
        // Arrange — System.Text.Json walks the runtime type's properties; this exercises that path.
        Aggregate<string> aggregate = JsonTestAggregate.Create("agg-2", "name");
        ((JsonTestAggregate)aggregate).DoSomething();

        // Act
        var json = JsonSerializer.Serialize(aggregate, aggregate.GetType());

        aggregate.IsChanged.Should().BeTrue();
        aggregate.UncommittedEvents().Should().HaveCount(1);

        // Assert
        json.Should().NotContain("DomainEvents",
            "Even when serialized via runtime type, DomainEvents must remain hidden from JSON output.");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.ValueKind.Should().Be(JsonValueKind.Object);
        root.TryGetProperty("Id", out var id).Should().BeTrue();
        id.GetString().Should().Be("agg-2");
        root.TryGetProperty("Name", out var name).Should().BeTrue();
        name.GetString().Should().Be("name_modified");
        root.TryGetProperty("IsChanged", out _).Should().BeFalse();
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