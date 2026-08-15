namespace Trellis.EntityFrameworkCore;

using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Captures uncommitted domain events from tracked aggregates into <see cref="OutboxMessage"/> rows
/// so they commit in the same transaction as the aggregate change, then — only after the save
/// succeeds — clears them from the aggregates via the aggregate's <c>AcceptChanges</c>.
/// </summary>
/// <remarks>
/// <para>
/// Rows are added during <c>SavingChanges</c> (enrolling them in the current transaction); the
/// aggregates' event lists are cleared in <c>SavedChanges</c> so a failed save leaves the in-memory
/// events intact for retry, and the interceptor-added rows are detached on <c>SaveChangesFailed</c>
/// so a retry on the same context does not double-capture.
/// </para>
/// <para>
/// Because the events are cleared after the commit, a post-commit in-pipeline
/// <c>DomainEventDispatchBehavior</c> observes an empty list and dispatches nothing — the outbox
/// relay becomes the single durable dispatch path.
/// </para>
/// <para>
/// <b>Interceptor constraint.</b> No other <see cref="SaveChangesInterceptor"/> may raise domain events
/// on a tracked aggregate between this interceptor's <c>SavingChanges</c> and <c>SavedChanges</c>
/// callbacks. Capture reads each aggregate's uncommitted events during <c>SavingChanges</c>, while
/// <c>SavedChanges</c> clears them with <c>AcceptChanges</c>, which empties the whole list — so any
/// event raised in between is discarded without ever reaching the outbox. No Trellis interceptor
/// raises domain events, so this only applies to caller-supplied interceptors. Raise events from the
/// domain model before <c>SaveChanges</c> is called instead.
/// </para>
/// <para>
/// <b>Serialization.</b> Events are serialized with <see cref="OutboxEventSerialization.Options"/>.
/// Value objects that carry a <c>[JsonConverter]</c> attribute (the scalar and composite Trellis
/// primitives) round-trip correctly, and <c>Maybe&lt;T&gt;</c> members are supported — a present value
/// serializes as the underlying value and an absent one as JSON <c>null</c>. Events whose properties rely
/// on other (non-attribute, caller-registered) converters are not supported by this MVP — use a nullable
/// transport in the event. A configurable serializer is a planned follow-up.
/// </para>
/// </remarks>
internal sealed class OutboxCaptureInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ClearCapturedAggregateEvents(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        // No cancellation check here: this runs AFTER a successful commit, so the outbox rows are
        // already durable. The aggregate's events must be cleared unconditionally (as the sync path
        // does) — bailing on a cancelled token would leave them uncleared, and a retry on the same
        // context would re-capture the already-committed events into new rows.
        ClearCapturedAggregateEvents(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        DetachPendingOutboxRows(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        DetachPendingOutboxRows(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <inheritdoc />
    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        // EF routes a cancelled save to the *Canceled callbacks, not SaveChangesFailed, so the rows
        // staged in SavingChanges must be detached here too or a retry on the same context double-captures.
        DetachPendingOutboxRows(eventData.Context);
        base.SaveChangesCanceled(eventData);
    }

    /// <inheritdoc />
    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        DetachPendingOutboxRows(eventData.Context);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    private static void Capture(DbContext? context)
    {
        if (context is null)
            return;

        List<OutboxMessage>? messages = null;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is not IAggregate aggregate)
                continue;

            var events = aggregate.UncommittedEvents();
            if (events.Count == 0)
                continue;

            messages ??= [];
            foreach (var domainEvent in events)
            {
                var type = domainEvent.GetType();
                var eventType = type.AssemblyQualifiedName
                    ?? throw new InvalidOperationException(
                        $"Domain event type '{type}' has no AssemblyQualifiedName and cannot be relayed from the outbox; use a concrete, non-generic event type.");
                messages.Add(OutboxMessage.Create(
                    Guid.CreateVersion7(),
                    domainEvent.OccurredAt,
                    eventType,
                    JsonSerializer.Serialize(domainEvent, type, OutboxEventSerialization.Options),
                    OutboxMessageKind.Domain));
            }
        }

        // Enrol the new rows in the current SaveChanges so they commit atomically with the aggregate
        // change. The aggregates' events are cleared later, in SavedChanges, only once the save succeeds.
        if (messages is not null)
            context.Set<OutboxMessage>().AddRange(messages);
    }

    /// <summary>
    /// Clears the uncommitted events of every tracked aggregate once the save has succeeded.
    /// </summary>
    /// <remarks>
    /// <c>AcceptChanges</c> empties an aggregate's entire event list, so this clears any event raised
    /// after <see cref="Capture"/> ran as well as the ones it captured. See the interceptor constraint
    /// documented on the class.
    /// </remarks>
    private static void ClearCapturedAggregateEvents(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAggregate aggregate && aggregate.UncommittedEvents().Count > 0)
                aggregate.AcceptChanges();
        }
    }

    private static void DetachPendingOutboxRows(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<OutboxMessage>())
        {
            if (entry.State == EntityState.Added)
                entry.State = EntityState.Detached;
        }
    }
}
