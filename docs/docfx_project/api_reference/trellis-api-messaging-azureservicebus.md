---
package: Trellis.Messaging.AzureServiceBus
namespaces: [Trellis.Messaging.AzureServiceBus]
types: [ServiceBusIntegrationEventPublisher, ServiceBusInboxConsumer, AzureServiceBusPublisherOptions, AzureServiceBusConsumerOptions, ServiceBusSubscription, ServiceBusMessageFormat, AzureServiceBusServiceCollectionExtensions]
version: v1
last_verified: 2026-06-19
audience: [llm]
---
# Trellis.Messaging.AzureServiceBus &mdash; API Reference

**Package:** `Trellis.Messaging.AzureServiceBus`
**Namespace:** `Trellis.Messaging.AzureServiceBus`
**Purpose:** Carry integration events between services over Azure Service Bus, preserving the message identity the transactional inbox deduplicates on.

See also: [trellis-api-efcore-outbox.md](trellis-api-efcore-outbox.md#outboxmessage), [trellis-api-efcore-inbox.md](trellis-api-efcore-inbox.md#integrationenvelope), [trellis-api-mediator.md](trellis-api-mediator.md#iintegrationeventpublisher).

## Use this file when

- You are publishing integration events to another service rather than fanning them out in process.
- You are consuming integration events from Service Bus into a Trellis inbox.
- You need the exact wire format (which Service Bus member carries what).
- You are deciding how a received message should be settled.

## Why this package exists

Trellis ships both ends of reliable messaging and, until this package, no wire between them:

| Stage | Component | Guarantee |
|---|---|---|
| Produce | `OutboxCaptureInterceptor` + `OutboxRelay` | The event is staged in the same transaction as the business change, then relayed at-least-once. |
| **Transport** | **this package** | **The producer's message identity survives the wire.** |
| Consume | `IInboxDispatcher` + `IInboxStore` | Handler side effects and the `(ConsumerId, MessageId)` dedup row commit together, so a redelivery is skipped. |

The middle row is load-bearing. `OutboxRelay` delivery is at-least-once — a crash between publishing and the relay's bookkeeping save republishes the row — so a transport that minted its own id per attempt would put a *different* `MessageId` on each copy. The consumer's dedup would miss and handlers would run twice. Because [`IIntegrationEventPublisher`](trellis-api-mediator.md#iintegrationeventpublisher) takes an `OutboundIntegrationMessage`, which carries the outbox row id, publishing without it is not expressible.

This collapses redeliveries of a *single* outbox row. It does not collapse the other duplicate the outbox can produce — a retried domain row re-running its translator stages a genuinely new row with a new id — which still needs business-identity deduplication. See [the outbox reference](trellis-api-efcore-outbox.md#two-different-duplicates--only-one-is-the-message-ids-job) for the distinction.

## Wire format

`ServiceBusMessageFormat` names the members Trellis assigns meaning to.

| Service Bus member | Carries | Notes |
|---|---|---|
| `MessageId` | The producer's outbox row id (UUIDv7) | The consumer's dedup key. Must parse as a non-empty `Guid`. |
| `Subject` | The event's stable wire name | From `[IntegrationEventName]`; resolved through `IntegrationEventNameMap`. |
| `Body` | The event as UTF-8 JSON | Serialized against the event's **runtime** type. |
| `ContentType` | `ServiceBusMessageFormat.JsonContentType` (`application/json`) | |
| `trellis-message-source` (application property) | `IntegrationEnvelope.MessageSource` | Optional; observability only. Omitted when unset or blank. |

Standard Service Bus members are preferred over custom application properties wherever one exists: `MessageId` and `Subject` are indexed by the broker, surfaced in the portal and Service Bus Explorer, and usable in subscription filters, so a message stays diagnosable and routable by tools that know nothing about Trellis.

`IntegrationEnvelope`'s lineage members `CausationId` and `CorrelationId` are **not** on the wire, because nothing on the publish side can populate them: `OutboundIntegrationMessage` deliberately omits them until the outbox persists them.

## Topology

The default layout is **one topic per contract**, named after the wire name. A subscriber declares interest by subscribing to the topics it wants, rather than filtering a firehose.

`AzureServiceBusPublisherOptions.TopicNameResolver` changes that mapping — prefix an environment segment (`name => $"prod.{name}"`), or collapse several contracts onto one topic. If you collapse them, add a correlation filter on `sys.Label` (the message's `Subject`) to each subscription, or a subscriber will receive contracts it cannot deserialize and dead-letter them.

## AzureServiceBusPublisherOptions

```csharp
public sealed class AzureServiceBusPublisherOptions
{
    public string? MessageSource { get; set; }
    public Func<string, string> TopicNameResolver { get; set; }
    public JsonSerializerOptions JsonSerializerOptions { get; set; }
}
```

| Member | Default | Notes |
|---|---|---|
| `MessageSource` | `null` | The producing service or bounded context. Observability only — never affects dedup or routing. |
| `TopicNameResolver` | identity | Wire name → topic name. |
| `JsonSerializerOptions` | `JsonSerializerOptions.Web` | A wire-format decision shared with every consumer. Change it before the first message ships, or accept that in-flight messages written with the previous settings must still deserialize. |

## AzureServiceBusConsumerOptions

```csharp
public sealed class AzureServiceBusConsumerOptions
{
    public IList<ServiceBusSubscription> Subscriptions { get; }
    public int MaxConcurrentCalls { get; set; }
    public int PrefetchCount { get; set; }
    public JsonSerializerOptions JsonSerializerOptions { get; set; }

    public AzureServiceBusConsumerOptions Subscribe(string topicName, string subscriptionName);
}
```

| Member | Default | Notes |
|---|---|---|
| `Subscriptions` | empty | Each gets its own processor. At least one is required. |
| `MaxConcurrentCalls` | `1` | Each concurrent message runs handlers in its own database transaction, so raising this raises connection-pool pressure and write contention. Deduplication stays correct at any value — concurrent delivery of the same message is resolved by the inbox's composite primary key — so this is purely a throughput knob. |
| `PrefetchCount` | `0` | Prefetched messages have their lock clock already running; a large prefetch with slow handlers expires locks and causes redelivery. Safe (the inbox absorbs it) but wasteful. |
| `JsonSerializerOptions` | `JsonSerializerOptions.Web` | Must agree with the producer. |

**The subscriber identity is not here.** Deduplication keys on `InboxOptions.ConsumerId`, so every transport a service consumes from shares one dedup namespace and a message arriving twice by two routes is still processed once.

Validation runs at registration, not on the first message: no subscriptions, `MaxConcurrentCalls < 1`, a negative `PrefetchCount`, or the same `(topic, subscription)` registered twice each throw `InvalidOperationException` from the `AddAzureServiceBusIntegrationEventConsumer` call.

The duplicate check spans calls. Configuration accumulates onto one options instance, so two registrations that are each valid alone can still add the same subscription twice — which would start two processors competing on one subscription. Registration validates the accumulated configuration, and the consumer re-validates what it was actually handed, since `Subscriptions` is a mutable list that can be appended to directly.

`TopicNameResolver` and both `JsonSerializerOptions` properties reject `null` on assignment rather than failing with a `NullReferenceException` on the first message.

## Registration

```csharp
public static IServiceCollection AddAzureServiceBusIntegrationEventPublisher(
    this IServiceCollection services,
    IntegrationEventNameMap nameMap,
    Action<AzureServiceBusPublisherOptions>? configure = null);

public static IServiceCollection AddAzureServiceBusIntegrationEventConsumer(
    this IServiceCollection services,
    IntegrationEventNameMap nameMap,
    Action<AzureServiceBusConsumerOptions> configure);
```

Both require a `ServiceBusClient` in the container; neither owns its lifetime.

`AddAzureServiceBusIntegrationEventPublisher` **replaces** any existing `IIntegrationEventPublisher` registration rather than adding to it. In-process fan-out and broker publication are alternatives, not layers: registering both would deliver each event locally *and* over the wire, so a service subscribed to its own topic would handle everything twice. Replacing also makes the registration order-independent.

`AddAzureServiceBusIntegrationEventConsumer` requires an `IInboxDispatcher` (`AddTrellisInbox<TContext>()`); consuming without one runs handlers with no deduplication, which is the failure the inbox exists to prevent. The dispatcher is resolved per message from a fresh scope, so it need not be registered before this call.

**Each helper keeps the map it was handed.** The `nameMap` argument is captured by the component being registered rather than published as a shared container service, so a service that publishes one set of contracts and consumes another can pass a different map to each call without the first silently winning. Calling the consumer helper more than once still runs a single consumer, configured with every caller's subscriptions.

**Neither helper has a `TrellisServiceBuilder.UseXxx()` slot.** Surfacing them would force every `Trellis.ServiceDefaults` consumer to take a transitive dependency on the Azure SDK to use features unrelated to Azure. This matches `Trellis.Asp.Idempotency.Cosmos`; see [trellis-api-servicedefaults.md](trellis-api-servicedefaults.md#registrations-without-a-builder-slot).

## Settlement

`ServiceBusInboxConsumer` runs with `AutoCompleteMessages = false`, because settlement follows from the inbox outcome.

| Situation | Action | Why |
|---|---|---|
| `InboxDispatchOutcome.Processed` | Complete | Handler side effects and the dedup row committed together. |
| `InboxDispatchOutcome.SkippedDuplicate` | Complete | Already durably accounted for. Abandoning would redeliver forever, because every attempt reaches the same conclusion. |
| Handler throws | Abandon | The dispatcher rolled its transaction back, so nothing was applied. The exception is deliberately not caught: the SDK abandons an unsettled message whose handler threw and routes the exception to the error handler. `MaxDeliveryCount` dead-letters a persistently failing message. |
| Message is unusable | Dead-letter with a reason code | A property of the bytes, not of this consumer's state, so retrying cannot change the outcome. Abandoning would burn the delivery count and dead-letter anyway, with no diagnosis attached. |

Dead-letter reasons:

| Reason | Meaning |
|---|---|
| `servicebus_unusable_message_id` | `MessageId` is absent or not a non-empty GUID, so the message has no stable inbox dedup key. |
| `servicebus_missing_subject` | No `Subject`, so no contract can be chosen to deserialize the body. |
| `servicebus_unknown_contract` | The wire name is well-formed but unregistered here. Normal traffic on a shared topic, not necessarily a bug. |
| `servicebus_malformed_body` | The contract is known but the body does not deserialize to it — either the JSON is invalid for that shape, or the contract itself cannot be materialized (an abstract or interface-typed member, or a shape with no converter). Both are permanent for these bytes, so both dead-letter rather than escaping as a fault the consumer would retry until the delivery count is exhausted. |

The dead-letter description carries the specific diagnosis (the offending wire name, the JSON error, and so on).

## Publishing an unmapped event

`ServiceBusIntegrationEventPublisher.PublishAsync` throws `InvalidOperationException` when the event type has no registered wire name. This is deliberately fatal to the publish rather than a skip: the event cannot be named on the wire, so no consumer could identify it. The outbox row stays unsent and the relay retries, surfacing a missing `[IntegrationEventName]` as a stuck message rather than as silently dropped traffic.

## Testing

Integration tests are marked `[Trait("Category", "Integration")]` and skip visibly when no emulator is reachable, so they never pass against a substitute.

```
docker compose -f Trellis.Messaging.AzureServiceBus/tests/emulator/docker-compose.yml up -d
```

The emulator declares its entities in `Config.json` at container start and cannot create them at runtime, so the compose file and its entity list are part of the fixture. Duplicate detection is deliberately **off** on the test topic: if the broker collapsed duplicate message ids, the suite would pass even if the transport minted a fresh id per publish. The assertion is that Trellis carries the id — not that Service Bus can be configured to hide the consequences of losing it.
