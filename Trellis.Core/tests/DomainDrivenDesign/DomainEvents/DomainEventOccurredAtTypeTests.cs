namespace Trellis.Core.Tests.DomainDrivenDesign.DomainEvents;

using System.Text.Json;

/// <summary>
/// Locks in that <see cref="IDomainEvent.OccurredAt"/> is <see cref="DateTimeOffset"/>, not <see cref="DateTime"/>.
/// The offset must round-trip through <see cref="JsonSerializer"/> so that events whose origin clock is non-UTC
/// (or events that flow through a system with a clock-skew tolerant store) preserve their authored instant.
/// </summary>
public class DomainEventOccurredAtTypeTests
{
    [Fact]
    public void OccurredAt_TypeIsDateTimeOffset_AtCompileTime()
    {
        var moment = new DateTimeOffset(2026, 5, 2, 17, 30, 0, TimeSpan.FromHours(5));
        var evt = new TimestampedEvent("agg-1", moment);

        // Compile-time check via the interface — this assignment only compiles
        // if IDomainEvent.OccurredAt is DateTimeOffset (not DateTime).
        DateTimeOffset captured = ((IDomainEvent)evt).OccurredAt;

        captured.Should().Be(moment);
    }

    [Fact]
    public void OccurredAt_PreservesOffsetThroughJsonRoundTrip()
    {
        var moment = new DateTimeOffset(2026, 5, 2, 17, 30, 0, TimeSpan.FromHours(5));
        var evt = new TimestampedEvent("agg-1", moment);

        var json = JsonSerializer.Serialize(evt);
        json.Should().Contain("+05:00");

        var roundTrip = JsonSerializer.Deserialize<TimestampedEvent>(json)!;
        roundTrip.OccurredAt.Should().Be(moment);
        roundTrip.OccurredAt.Offset.Should().Be(TimeSpan.FromHours(5));
    }

    [Fact]
    public void OccurredAt_AcceptsTimeProviderGetUtcNowDirectly()
    {
        // Regression guard: TimeProvider.GetUtcNow() returns DateTimeOffset.
        // Authoring an event from it must compile without .UtcDateTime conversion.
        TimeProvider clock = TimeProvider.System;
        DateTimeOffset now = clock.GetUtcNow();

        var evt = new TimestampedEvent("agg-1", now);

        ((IDomainEvent)evt).OccurredAt.Should().Be(now);
        ((IDomainEvent)evt).OccurredAt.Offset.Should().Be(TimeSpan.Zero);
    }
}

internal record TimestampedEvent(string AggregateId, DateTimeOffset OccurredAt) : IDomainEvent;
