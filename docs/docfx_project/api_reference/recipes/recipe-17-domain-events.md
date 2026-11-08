---
name: recipe-17-domain-events
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows custom domain events where OccurredAt is the only timestamp. Use when defining domain events."
---
# Recipe 17 — Defining custom domain events: `OccurredAt` is the only timestamp

**Requires:** `Trellis.Core`, plus `Trellis.Authorization`.

**Problem.** You're modeling an order workflow and reach for a domain event:

```csharp
// ❌ Wrong — CS0535: 'OrderSubmitted' does not implement interface member 'IDomainEvent.OccurredAt'
public sealed record OrderSubmitted(OrderId OrderId, Money Total, DateTimeOffset SubmittedAt) : IDomainEvent;
```

The compile error is unambiguous, but the obvious "fix" — adding `OccurredAt` *alongside* `SubmittedAt` — is the wrong shape:

```csharp
// ❌ Wrong — duplicate timestamps. SubmittedAt and OccurredAt always carry the same value.
public sealed record OrderSubmitted(OrderId OrderId, Money Total, DateTimeOffset SubmittedAt, DateTimeOffset OccurredAt) : IDomainEvent;
```

**Fix.** `OccurredAt` is the canonical, only timestamp on every domain event. The semantic meaning ("when the order was submitted") is carried by the *event type name* (`OrderSubmitted`), not by a parallel timestamp field. Drop the semantic alias:

```csharp
// ✅ Correct — OccurredAt is the timestamp; the event name carries the semantic.
public sealed record OrderSubmitted(OrderId OrderId, Money Total, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderApproved(OrderId OrderId, ActorId ApprovedBy, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record OrderShipped(OrderId OrderId, TrackingNumber Tracking, DateTimeOffset OccurredAt) : IDomainEvent;
```

**Raising the event.** Always pass `TimeProvider.GetUtcNow()` (the testable-clock primitive returns `DateTimeOffset` directly — no conversion needed). The aggregate's domain method, not the event constructor, is where time enters the system:

```csharp
public Result<Order> Submit(TimeProvider clock)
{
    return Result.Ok(this)
        .Ensure(static order => order.Status == OrderStatus.Draft, static _ =>
            Error.InvalidInput.ForRule(code: "order.already-submitted", detail: "Already submitted"))
        .Tap(_ =>
        {
            Status = OrderStatus.Submitted;
            DomainEvents.Add(new OrderSubmitted(Id, Total, clock.GetUtcNow()));
        });
}
```

> [!IMPORTANT]
> Domain-event handlers are side-effect-only. The mediator dispatch behaviors snapshot `UncommittedEvents()` after the owning commit, immediately before publishing, and publish only that snapshot. If a handler raises an event on the same aggregate, post-dispatch validation throws `DomainEventHandlerCascadedException` and the aggregate's events are not cleared. That exception is raised after the commit, so the write is already durable while the caller sees a failure (an `Unexpected` failure through the standard `ExceptionBehavior`); do not retry the command to recover. If a side effect needs more domain mutation, send a separate Mediator command after the originating command completes, or queue post-commit work that runs as its own top-level command — not from inside the handler.

**On the aggregate.** If your aggregate also exposes a public `SubmittedAt` property (e.g., to drive UI sort order or read-model projections), source it from the event timestamp at write time — don't track it independently:

```csharp
public DateTimeOffset? SubmittedAt { get; private set; }

public Result<Order> Submit(TimeProvider clock)
{
    var occurredAt = clock.GetUtcNow();
    // ... ensure rules ...
    Status = OrderStatus.Submitted;
    SubmittedAt = occurredAt;
    DomainEvents.Add(new OrderSubmitted(Id, Total, occurredAt));
    return Result.Ok(this);
}
```

**Why a single timestamp.** Domain events flow into outbox tables, integration buses, audit projections, and event-sourced read models. Every consumer assumes `OccurredAt` is *the* occurrence time. Adding `SubmittedAt`/`ApprovedAt`/`ShippedAt` to individual events forces every consumer to know which field to project per event type — and the two fields can drift if the aggregate's setter and the event constructor are passed different `clock.GetUtcNow()` calls.

**Why `DateTimeOffset`.** `OccurredAt` is `DateTimeOffset` (not `DateTime`) so the explicit offset is a part of the value and round-trips unambiguously through serialization. `TimeProvider.GetUtcNow()` returns `DateTimeOffset` directly — events stored in outbox tables, integration buses, and audit projections retain their authored instant without timezone-loss bugs.

**See also.** The XML doc on `IDomainEvent.OccurredAt` (in `Trellis.Core`) calls this out explicitly. If your IDE shows the doc on hover, the rule is right there before you hit the compile error. For dispatch-handler cascade traps, see [`trellis-api-anti-patterns.md`](../trellis-api-anti-patterns.md#no-analyzer--domain-event-handler-raises-more-domain-events-during-dispatch).
