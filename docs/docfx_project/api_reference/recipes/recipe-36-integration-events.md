---
name: recipe-36-integration-events
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows translating a domain event into a stable integration event and publishing it through the outbox, a broker or Azure Service Bus. Use when publishing integration events or linking traces."
---
# Recipe 36 — Translating a domain event into an integration event

**Requires:** `Trellis.Core`, plus `Trellis.EntityFrameworkCore`, `Trellis.EntityFrameworkCore.Outbox`, `Trellis.Mediator`, `Trellis.ServiceDefaults`.

**Problem.** You want to publish that an order was placed to *other* services, but your `OrderPlaced` domain event carries internal value objects (`OrderId`, `CustomerEmail`, `Money`). Relaying it as-is couples external consumers to your domain model, and every refactor becomes a breaking wire change.

**Fix.** Keep the domain event internal and publish a deliberately-shaped `IIntegrationEvent` translated from it. The translator is an ordinary domain-event handler that adds to `IIntegrationEventCollector`; the outbox relay stages the integration event durably and later hands it to `IIntegrationEventPublisher`.

```csharp
// 1. The external contract — primitive/nullable members, no internal value objects.
public sealed record OrderPlacedIntegrationEvent(Guid OrderId, string CustomerEmail, decimal Total, DateTimeOffset OccurredAt)
    : IIntegrationEvent;

// 2. The translator — a domain-event handler that emits the contract.
public sealed class OrderPlacedTranslator(IIntegrationEventCollector collector) : IDomainEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        collector.Add(new OrderPlacedIntegrationEvent(
            domainEvent.OrderId.Value, domainEvent.CustomerEmail.Value, domainEvent.Total.Amount, domainEvent.OccurredAt));
        return ValueTask.CompletedTask;
    }
}

// 3. A best-effort in-process consumer (or replace IIntegrationEventPublisher with a broker adapter).
public sealed class NotifyShippingHandler : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public ValueTask HandleAsync(OrderPlacedIntegrationEvent e, CancellationToken ct) { /* ... */ return ValueTask.CompletedTask; }
}

// 4. Wire it — domain rows report handler outcomes; integration rows use one publisher handoff.
services.AddTrellis(trellis => trellis
    .UseDomainEvents(typeof(Program).Assembly)
    .UseIntegrationEvents(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseOutbox<AppDbContext>());
```

**Semantics to remember.**

- The integration event is enrolled **only after** its source domain event is durably committed and dispatched by the relay — never for state that rolled back. The relay stages drained collector events as `OutboxMessageKind.Integration` rows atomically with the source row's saved handler progress (or overall completion), then publishes them on a later drain. There is no per-handler collector rollback: an event added by a translator that subsequently throws can still be enrolled, as can output from a successful translator whose sibling fails. The source row can remain pending while those integration rows are already eligible for publication; integration publication is not proof that the translator or every local handler completed.
- Each translated integration row receives its own UTC `CapturedAt` when the relay stages
  it. It does not inherit the source domain row's capture age; retries and replay preserve
  each row's original evidence.
- The default `IIntegrationEventPublisher` is **best-effort local notification**: it fans out to `IIntegrationEventHandler<T>`, logs and swallows ordinary consumer failures, and returns normally. The relay then processes the integration row without a failed attempt (`Attempts == 0` on a new row) or a consumer retry. This is suitable for modular monoliths and tests only when that tradeoff is acceptable.
- A **broker publisher** must await publication acceptance and propagate publication failures so the relay can retry the pending row under the same message id. Acceptance does not mean downstream consumer success. The default domain publisher's post-commit/no-retry rationale does not apply here: the integration relay owns retries, but sees only exposed failures.
- **Transactional inbox consumption** is separate. An inbound adapter must call `IInboxDispatcher` with the stable message id and expose failures for transport redelivery. Handler writes through the inbox's context and the dedup row commit together; external calls do not. A new message with no registered integration-event handler throws without writing its dedup row, so correct the registration or use an explicit no-op handler. The built-in EF dispatcher requires its matching EF store; a custom store needs a coordinated custom dispatcher. Enabling `UseInbox` does not reroute the default local publisher through it.
- Publication can repeat after an exposed failure or a crash before bookkeeping. Routine domain retries skip translators whose success was recorded, so a failed sibling does not by itself re-enroll their integration events. A translator that added an event and then failed is retried and can produce a new row with a new message id; crashes before progress is saved can also repeat translation. Deduplicate repeated delivery by message id, and use business identity when semantic duplicates can have distinct message ids.
- Integration events require the outbox. The collector accepts `Add` only inside an active outbox-relay translator invocation; outside a relay translation `Add` throws `InvalidOperationException`. From a command handler or a direct translator call the exception reaches the caller; from ordinary in-process domain dispatch the default publisher logs it and swallows it, so the command succeeds and no integration event is produced. Use the persistence/capture setup from [Recipe 35](recipe-35-outbox.md) plus `UseOutbox<TContext>()`; the translator above is invoked by the relay, not by the command handler.
- `CausationId` on a translated integration row is the source **domain row** id; the domain row's cause is the inbound envelope `MessageId` when present. Both rows share the persisted W3C trace and optional application-owned business `CorrelationId` (supply one by wrapping the whole command dispatch in `using var _ = IntegrationMessageContext.BeginCorrelation("workflow-id");` in the caller of `Send` or in an outer behavior; calling it inside an async handler has no effect on the commit, which runs after the handler returns, and the row is persisted with a null correlation). Do not derive business correlation from a trace or HTTP request id.

See [trellis-api-efcore-outbox.md](../trellis-api-efcore-outbox.md#integration-events) for the routing contract.
