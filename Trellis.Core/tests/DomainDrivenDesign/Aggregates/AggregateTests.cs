using Trellis.Testing;
namespace Trellis.Core.Tests.DomainDrivenDesign.Aggregates;

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

    [Fact]
    public void Aggregate_implements_IEntity() => typeof(IEntity).IsAssignableFrom(typeof(Aggregate<string>)).Should().BeTrue();

    [Fact]
    public void Aggregate_InheritsTimestamps()
    {
        // Arrange
        var aggregate = TestAggregate.Create("agg-1");
        var timestamp = new DateTimeOffset(2025, 3, 15, 8, 0, 0, TimeSpan.Zero);

        // Act
        aggregate.CreatedAt = timestamp;
        aggregate.LastModified = timestamp;

        // Assert
        aggregate.CreatedAt.Should().Be(timestamp);
        aggregate.LastModified.Should().Be(timestamp);
    }

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

    #region ETag Tests

    [Fact]
    public void NewAggregate_HasEmptyETag()
    {
        // Arrange & Act
        var aggregate = TestAggregate.Create("1");

        // Assert
        aggregate.ETag.Should().BeEmpty();
    }

    [Fact]
    public void ETag_IsAccessibleViaIAggregate()
    {
        // Arrange
#pragma warning disable CA1859 // Intentionally using interface type to verify contract
        IAggregate aggregate = TestAggregate.Create("1");
#pragma warning restore CA1859

        // Assert
        aggregate.ETag.Should().BeEmpty();
    }

    #endregion

    #region Typed EntityTagValue OptionalETag Tests

    [Fact]
    public void OptionalETag_TypedNull_SkipsValidation()
    {
        var aggregate = TestAggregate.Create("1");
        var result = Result.Ok(aggregate);

        result.OptionalETag((EntityTagValue[]?)null).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void OptionalETag_TypedMatchingStrongTag_ReturnsSuccess()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("abc123");
        var result = Result.Ok(aggregate);

        result.OptionalETag([EntityTagValue.Strong("abc123")]).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void OptionalETag_TypedWeakTagExcluded_ReturnsPreconditionFailed()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("abc123");
        var result = Result.Ok(aggregate);

        // Weak tags should not match via strong comparison
        var ensured = result.OptionalETag([EntityTagValue.Weak("abc123")]);
        ensured.IsSuccess.Should().BeFalse();
        ensured.UnwrapError().Should().BeOfType<Error.PreconditionFailed>();
    }

    [Fact]
    public void OptionalETag_TypedWildcard_MatchesAny()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("anything");
        var result = Result.Ok(aggregate);

        result.OptionalETag([EntityTagValue.Wildcard()]).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void OptionalETag_TypedEmptyArray_ReturnsPreconditionFailed()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("abc123");
        var result = Result.Ok(aggregate);

        var ensured = result.OptionalETag(Array.Empty<EntityTagValue>());
        ensured.IsSuccess.Should().BeFalse();
        ensured.UnwrapError().Should().BeOfType<Error.PreconditionFailed>();
    }

    [Fact]
    public void OptionalETag_TypedMultipleTags_MatchesAnyStrong()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("current");
        var result = Result.Ok(aggregate);

        result.OptionalETag([EntityTagValue.Strong("stale"), EntityTagValue.Strong("current")]).IsSuccess.Should().BeTrue();
    }

    #endregion

    #region Typed EntityTagValue RequireETag Tests

    [Fact]
    public void RequireETag_TypedNull_ReturnsPreconditionRequired()
    {
        var aggregate = TestAggregate.Create("1");
        var result = Result.Ok(aggregate);

        var ensured = result.RequireETag((EntityTagValue[]?)null);
        ensured.IsSuccess.Should().BeFalse();
        ensured.UnwrapError().Should().BeOfType<Error.PreconditionRequired>();
    }

    [Fact]
    public void RequireETag_TypedMatchingStrongTag_ReturnsSuccess()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("abc123");
        var result = Result.Ok(aggregate);

        result.RequireETag([EntityTagValue.Strong("abc123")]).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void RequireETag_TypedFailedResult_PreservesOriginalError()
    {
        var result = Result.Fail<TestAggregate>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "not found" });

        var ensured = result.RequireETag((EntityTagValue[]?)null);
        ensured.IsSuccess.Should().BeFalse();
        ensured.UnwrapError().Should().BeOfType<Error.NotFound>("existing failure should be preserved, not replaced by PreconditionRequired");
    }

    [Fact]
    public void OptionalETag_TypedFailedResult_PreservesOriginalError()
    {
        var result = Result.Fail<TestAggregate>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "not found" });

        var ensured = result.OptionalETag([EntityTagValue.Strong("abc123")]);
        ensured.IsSuccess.Should().BeFalse();
        ensured.UnwrapError().Should().BeOfType<Error.NotFound>("existing failure should be preserved, not replaced by PreconditionFailed");
    }

    [Fact]
    public void RequireETag_TypedFailedResult_WithETags_PreservesOriginalError()
    {
        var result = Result.Fail<TestAggregate>(new Error.NotFound(new ResourceRef("Resource", null)) { Detail = "not found" });

        var ensured = result.RequireETag([EntityTagValue.Strong("abc123")]);
        ensured.IsSuccess.Should().BeFalse();
        ensured.UnwrapError().Should().BeOfType<Error.NotFound>("existing failure should be preserved, not replaced by PreconditionFailed");
    }

    [Fact]
    public void OptionalETag_TypedNonMatchingStrongTag_ReturnsPreconditionFailed()
    {
        var aggregate = TestAggregate.Create("1");
        aggregate.SetTestETag("current");
        var result = Result.Ok(aggregate);

        var ensured = result.OptionalETag([EntityTagValue.Strong("stale")]);
        ensured.IsSuccess.Should().BeFalse();
        ensured.UnwrapError().Should().BeOfType<Error.PreconditionFailed>();
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

    /// <summary>Test-only helper to simulate a persisted ETag.</summary>
    public void SetTestETag(string etag) =>
        typeof(Aggregate<string>).GetProperty(nameof(ETag))!.SetValue(this, etag);
}

#endregion
