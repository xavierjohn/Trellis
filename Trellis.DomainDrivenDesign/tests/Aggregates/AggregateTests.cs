namespace Trellis.DomainDrivenDesign.Tests.Aggregates;

using Trellis;

public class AggregateTests
{
    #region Type Tests

    [Fact]
    public void Aggregate_is_abstract() => typeof(Aggregate<>).IsAbstract.Should().BeTrue();

    [Fact]
    public void Aggregate_inherits_from_Entity()
    {
        // Use a closed generic type to test inheritance
        typeof(TestAggregate).BaseType.Should().Be<Aggregate<string>>();
        typeof(Aggregate<string>).BaseType.Should().Be<Entity<string>>();
    }

    [Fact]
    public void Aggregate_implements_IAggregate() => typeof(IAggregate).IsAssignableFrom(typeof(Aggregate<string>)).Should().BeTrue();

    #endregion

    #region Domain Events Tests

    [Fact]
    public void NewAggregate_HasNoUncommittedEvents()
    {
        // Arrange & Act
        var aggregate = TestAggregate.Create("1");

        // Assert
        aggregate.UncommittedEvents().Should().BeEmpty();
        aggregate.IsChanged.Should().BeFalse();
    }

    [Fact]
    public void Aggregate_RaisingEvent_AddsToUncommittedEvents()
    {
        // Arrange
        var aggregate = TestAggregate.Create("1");

        // Act
        aggregate.DoSomething();

        // Assert
        aggregate.UncommittedEvents().Should().HaveCount(1);
        aggregate.UncommittedEvents()[0].Should().BeOfType<TestEvent>();
        aggregate.IsChanged.Should().BeTrue();
    }

    [Fact]
    public void Aggregate_RaisingMultipleEvents_TracksAllEvents()
    {
        // Arrange
        var aggregate = TestAggregate.Create("1");

        // Act
        aggregate.DoSomething();
        aggregate.DoSomething();
        aggregate.DoSomething();

        // Assert
        aggregate.UncommittedEvents().Should().HaveCount(3);
        aggregate.IsChanged.Should().BeTrue();
    }

    [Fact]
    public void AcceptChanges_ClearsUncommittedEvents()
    {
        // Arrange
        var aggregate = TestAggregate.Create("1");
        aggregate.DoSomething();
        aggregate.DoSomething();

        // Act
        aggregate.AcceptChanges();

        // Assert
        aggregate.UncommittedEvents().Should().BeEmpty();
        aggregate.IsChanged.Should().BeFalse();
    }

    [Fact]
    public void AcceptChanges_AllowsNewEventsToBeAdded()
    {
        // Arrange
        var aggregate = TestAggregate.Create("1");
        aggregate.DoSomething();
        aggregate.AcceptChanges();

        // Act
        aggregate.DoSomething();

        // Assert
        aggregate.UncommittedEvents().Should().HaveCount(1);
        aggregate.IsChanged.Should().BeTrue();
    }

    [Fact]
    public void UncommittedEvents_ReturnsReadOnlyList()
    {
        // Arrange
        var aggregate = TestAggregate.Create("1");
        aggregate.DoSomething();

        // Act
        var events = aggregate.UncommittedEvents();

        // Assert
        events.Should().BeAssignableTo<IReadOnlyList<IDomainEvent>>();
    }

    [Fact]
    public void UncommittedEvents_ReturnsSnapshot_NotLiveView()
    {
        // Arrange
        var aggregate = TestAggregate.Create("1");
        aggregate.DoSomething();
        aggregate.DoSomething();

        // Act
        var events = aggregate.UncommittedEvents();
        aggregate.AcceptChanges();

        // Assert
        events.Should().HaveCount(2);
        aggregate.UncommittedEvents().Should().BeEmpty();
    }

    #endregion

    #region Version Tests

    [Fact]
    public void NewAggregate_HasVersionZero()
    {
        // Arrange & Act
        var aggregate = TestAggregate.Create("1");

        // Assert
        aggregate.Version.Should().Be(0);
    }

    [Fact]
    public void Version_IsAccessibleViaIAggregate()
    {
        // Arrange
#pragma warning disable CA1859 // Intentionally using interface type to verify contract
        IAggregate aggregate = TestAggregate.Create("1");
#pragma warning restore CA1859

        // Assert
        aggregate.Version.Should().Be(0);
    }

    #endregion

    #region IsChanged Tests

    [Fact]
    public void IsChanged_IsFalse_WhenNoEvents()
    {
        // Arrange & Act
        var aggregate = TestAggregate.Create("1");

        // Assert
        aggregate.IsChanged.Should().BeFalse();
    }

    [Fact]
    public void IsChanged_IsTrue_WhenEventsExist()
    {
        // Arrange
        var aggregate = TestAggregate.Create("1");

        // Act
        aggregate.DoSomething();

        // Assert
        aggregate.IsChanged.Should().BeTrue();
    }

    [Fact]
    public void IsChanged_IsFalse_AfterAcceptChanges()
    {
        // Arrange
        var aggregate = TestAggregate.Create("1");
        aggregate.DoSomething();

        // Act
        aggregate.AcceptChanges();

        // Assert
        aggregate.IsChanged.Should().BeFalse();
    }

    #endregion
}

#region Test Aggregate and Events

internal record TestEvent(string AggregateId, DateTime OccurredAt) : IDomainEvent;

internal class TestAggregate : Aggregate<string>
{
    public string Name { get; private set; }

    private TestAggregate(string id, string name) : base(id) => Name = name;

    public static TestAggregate Create(string id, string name = "Test") => new(id, name);

    public void DoSomething()
    {
        Name = $"{Name}_modified";
        DomainEvents.Add(new TestEvent(Id, DateTime.UtcNow));
    }
}

#endregion