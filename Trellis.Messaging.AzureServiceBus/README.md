# Trellis.Messaging.AzureServiceBus

Azure Service Bus transport for Trellis integration events — the wire between a producer's transactional outbox and a consumer's deduplicating inbox.

## What it is for

Trellis already ships both ends of reliable messaging: the outbox stages integration events in the same transaction as the business change, and the inbox makes consumption idempotent by recording `(ConsumerId, MessageId)`. This package is the piece in between.

Its central obligation is one line of code: the producer's outbox row id becomes the Service Bus `MessageId`, carried verbatim.

Outbox relay delivery is at-least-once — a crash between publishing and the relay's bookkeeping save republishes the row. If the transport minted its own id per attempt, each copy would reach the consumer under a different `MessageId`, the inbox dedup would miss, and handlers would run twice. Because `IIntegrationEventPublisher` takes an `OutboundIntegrationMessage`, which carries the id, publishing without it is not expressible.

## Wire format

| Service Bus member | Carries |
|---|---|
| `MessageId` | The producer's outbox row id (UUIDv7). The consumer's dedup key. |
| `Subject` | The event's stable wire name, from `[IntegrationEventName("orders.order-placed.v1")]`. |
| `Body` | The event serialized as UTF-8 JSON. |
| `ContentType` | `application/json`. |
| `trellis-message-source` | Optional producing service or bounded context; observability only. |

Standard Service Bus members are used wherever one exists, so a message stays diagnosable in the portal and Service Bus Explorer, and routable by subscription filters, without those tools knowing anything about Trellis.

The default layout is **one topic per contract**, named after the wire name. Subscribers declare interest by subscribing to the topics they want rather than filtering a firehose. Override `TopicNameResolver` to prefix an environment segment or to collapse contracts onto a shared topic — if you collapse them, filter subscriptions on `sys.Label`, which always carries the wire name.

## Producing

```csharp
services.AddAzureServiceBusIntegrationEventPublisher(
    IntegrationEventNameMap.FromAssemblies(typeof(OrderPlaced).Assembly),
    options => options.MessageSource = "orders-service");
```

This **replaces** the in-process publisher rather than adding to it. The two are alternatives, not layers: registering both would deliver each event locally *and* over the wire, so a service subscribed to its own topic would handle everything twice.

Register a `ServiceBusClient` in the container; the publisher does not own its lifetime.

## Consuming

```csharp
services.AddAzureServiceBusIntegrationEventConsumer(
    IntegrationEventNameMap.FromAssemblies(typeof(OrderPlaced).Assembly),
    options => options.Subscribe("orders.order-placed.v1", "billing"));
```

Requires an `IInboxDispatcher` (`AddTrellisInbox<TContext>()`). Consuming without one would run handlers with no deduplication — the failure the inbox exists to prevent.

### Settlement

| Situation | Action | Why |
|---|---|---|
| `InboxDispatchOutcome.Processed` | Complete | Side effects and the dedup row committed together. |
| `InboxDispatchOutcome.SkippedDuplicate` | Complete | Durably accounted for already. Abandoning would loop forever, since every redelivery reaches the same conclusion. |
| Handler throws | Abandon (by the SDK) | The dispatcher rolled back, so nothing was applied. `MaxDeliveryCount` eventually dead-letters a persistently failing message. |
| Unusable id, missing or unknown `Subject`, malformed body | Dead-letter with a reason code | A property of the bytes, not of this consumer's state — retrying cannot change the outcome. |

The subscriber identity used for deduplication is `InboxOptions.ConsumerId`, not a transport setting, so a message arriving twice by two different routes is still processed once.

## Testing against the emulator

```
docker compose -f Trellis.Messaging.AzureServiceBus/tests/emulator/docker-compose.yml up -d
```

The emulator declares its entities in `Config.json` at startup and cannot create them at runtime, which is why the compose file and its entity list are part of the test fixture. Duplicate detection is deliberately **off** on the test topic: if the broker collapsed duplicate ids, the tests would pass even if the transport invented a fresh id per publish. The suite skips visibly when no emulator is reachable, so it never passes against a substitute.
