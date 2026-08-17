# Azure Service Bus Transport

The [transactional outbox](integration-outbox.md) stages integration events durably, and the [transactional inbox](integration-inbox.md) makes consuming them idempotent. `Trellis.Messaging.AzureServiceBus` is the wire between the two, so those guarantees hold across service boundaries instead of only inside one process.

## The guarantee that matters

Everything in this package exists to preserve one value.

Outbox relay delivery is **at-least-once**. If the process crashes between publishing a row and saving the relay's bookkeeping, that row is published again. The consumer's inbox deduplicates on `(ConsumerId, MessageId)`, so those two copies only collapse into one effective processing if they arrive under the *same* message id.

That id is the producer's outbox row id, and the transport's job is to carry it verbatim:

```csharp
var serviceBusMessage = new ServiceBusMessage(body)
{
    MessageId = message.MessageId.ToString(),  // the outbox row id, unchanged
    Subject = wireName,
    ContentType = "application/json",
};
```

A transport that generated its own id per publish attempt would put a different `MessageId` on each copy. The inbox would still be there, still look correct, and still deduplicate nothing. That is why `IIntegrationEventPublisher` takes an `OutboundIntegrationMessage` carrying the id, rather than the bare event: publishing without the identity is not expressible.

> [!NOTE]
> This collapses redeliveries of a *single* outbox row. A retried domain row re-runs its translator and stages a genuinely new integration row with a new id — a different duplicate that still needs business-identity deduplication. See [Transactional Outbox](integration-outbox.md) for the distinction.

## Naming events on the wire

The outbox stores `Type.AssemblyQualifiedName`, which is fine for relaying inside one process and useless across services: the consumer's assemblies differ, and the string embeds an assembly version that a routine upgrade invalidates.

Give each contract a stable name it owns:

```csharp
[IntegrationEventName("orders.order-placed.v1")]
public sealed record OrderPlaced(string OrderNumber, DateTimeOffset OccurredAt) : IIntegrationEvent;
```

Both sides build an `IntegrationEventNameMap` and resolve the name in whichever direction they need. The producer never has to know what the consumer calls the type.

## Producing

```csharp
builder.Services.AddSingleton(new ServiceBusClient(connectionString));

builder.Services.AddAzureServiceBusIntegrationEventPublisher(
    IntegrationEventNameMap.FromAssemblies(typeof(OrderPlaced).Assembly),
    options => options.MessageSource = "orders-service");
```

That is the only change to a service that already has an outbox. The relay keeps draining rows; they now leave the process.

This **replaces** the in-process publisher rather than adding to it. The two are alternatives, not layers — running both would deliver each event locally *and* over the wire, so a service subscribed to its own topic would handle everything twice.

By default each contract gets its own topic, named after its wire name. Subscribers declare interest by subscribing to the topics they want rather than filtering everything. Use `TopicNameResolver` to prefix an environment segment or to collapse contracts onto a shared topic — and if you collapse them, filter each subscription on `sys.Label`, which always carries the wire name.

## Consuming

```csharp
builder.Services.AddSingleton(new ServiceBusClient(connectionString));

builder.Services.AddTrellisInbox<BillingDbContext>(o => o.ConsumerId = "billing");

builder.Services.AddAzureServiceBusIntegrationEventConsumer(
    IntegrationEventNameMap.FromAssemblies(typeof(OrderPlaced).Assembly),
    options => options.Subscribe("orders.order-placed.v1", "billing"));
```

The dedup identity is `InboxOptions.ConsumerId`, not a transport setting. That is deliberate: a service that consumes the same logical message from two routes should still process it once.

## How messages are settled

Settlement follows from what the inbox reports, so `AutoCompleteMessages` is off.

| Outcome | Action | Why |
|---|---|---|
| `Processed` | Complete | Handler side effects and the dedup row committed together. |
| `SkippedDuplicate` | Complete | Already durably accounted for. Abandoning it would redeliver forever — every attempt would reach the same conclusion. |
| Handler threw | Abandon | The dispatcher rolled back, so nothing was applied. Redelivery is correct; `MaxDeliveryCount` dead-letters a persistently failing message. |
| Message unusable | Dead-letter | No parseable id, no contract name, an unknown contract, or a body that will not deserialize. Retrying the same bytes cannot change the outcome, so the message is dead-lettered immediately with a reason code rather than after burning the delivery count. |

Dead-letter reasons are `servicebus_unusable_message_id`, `servicebus_missing_subject`, `servicebus_unknown_contract`, and `servicebus_malformed_body`, each with a description naming the specific problem.

`servicebus_unknown_contract` is worth calling out: on a shared topic it is normal traffic, not a defect. A producer may emit contracts you do not model. Dead-lettering is a routing decision you can monitor, not a crash.

## Running the tests locally

```
docker compose -f Trellis.Messaging.AzureServiceBus/tests/emulator/docker-compose.yml up -d
```

The emulator declares its topics and subscriptions in `Config.json` before it starts and cannot create them at runtime, so that file is part of the test fixture rather than something the tests provision.

Two details in that fixture are deliberate:

- **Duplicate detection is off.** If the broker collapsed duplicate message ids, the tests would pass even if the transport invented a fresh id per publish. The suite asserts that Trellis carries the id, not that Service Bus can be configured to hide the consequences of losing it.
- **The tests skip visibly** when no emulator is reachable, rather than passing against an in-memory stand-in. A suite that quietly substitutes a fake proves nothing about a transport.
