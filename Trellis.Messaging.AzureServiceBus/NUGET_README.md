# Trellis.Messaging.AzureServiceBus

Azure Service Bus transport for [Trellis](https://github.com/xavierjohn/Trellis) integration events — the wire between a producer's transactional outbox and a consumer's deduplicating inbox.

The transport's central obligation is to carry the producer's outbox row id verbatim as the Service Bus `MessageId`. Outbox relay delivery is at-least-once, so the same row can be published more than once; carrying its id is what lets the consumer's `(ConsumerId, MessageId)` inbox dedup collapse those copies into one effective processing.

## Producing

```csharp
services.AddAzureServiceBusIntegrationEventPublisher(
    IntegrationEventNameMap.FromAssemblies(typeof(OrderPlaced).Assembly),
    options => options.MessageSource = "orders-service");
```

Replaces the in-process publisher. One topic per contract by default, named after the event's `[IntegrationEventName]`.

## Consuming

```csharp
services.AddAzureServiceBusIntegrationEventConsumer(
    IntegrationEventNameMap.FromAssemblies(typeof(OrderPlaced).Assembly),
    options => options.Subscribe("orders.order-placed.v1", "billing"));
```

Requires `AddTrellisInbox<TContext>()`. Messages are completed on both `Processed` and `SkippedDuplicate`, abandoned when a handler throws, and dead-lettered with a reason code when the message itself is unusable.

See the [package documentation](https://github.com/xavierjohn/Trellis) for the full wire format and settlement rules.
