# Trellis.Messaging.AzureServiceBus

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Messaging.AzureServiceBus.svg)](https://www.nuget.org/packages/Trellis.Messaging.AzureServiceBus)

Azure Service Bus transport for Trellis integration events — the wire between a producer's transactional outbox and a consumer's deduplicating inbox.

## Installation

```bash
dotnet add package Trellis.Messaging.AzureServiceBus
```

## Quick Example

```csharp
services.AddAzureServiceBusIntegrationEventPublisher(
    IntegrationEventNameMap.FromAssemblies(typeof(OrderPlaced).Assembly),
    options => options.MessageSource = "orders-service");

services.AddAzureServiceBusIntegrationEventConsumer(
    IntegrationEventNameMap.FromAssemblies(typeof(OrderPlaced).Assembly),
    options => options.Subscribe("orders.order-placed.v1", "billing"));
```

Register one shared `ServiceBusClient` as a factory-created singleton
(`services.AddSingleton(_ => new ServiceBusClient(...))`) so DI owns and disposes it.
Consumers also require an `IInboxDispatcher`, normally from
`AddTrellisInbox<TContext>()`.

## Key Features

- Carries the producer's outbox row ID verbatim as the Service Bus `MessageId`.
- Carries persisted business correlation and causation plus W3C traceparent/tracestate unchanged across retries; optional metadata remains absent on legacy messages.
- Uses one topic per stable integration-event wire name by default.
- Replaces the in-process publisher to prevent duplicate local and broker delivery.
- Settles messages from the inbox outcome: complete processed or duplicate messages, retry handler failures, and dead-letter unusable payloads.
- Coordinates processor startup and shutdown; concurrent or repeated host stops share one cleanup, allowing in-flight handlers to settle before background-service cancellation. Caches first-caller cancellation without masking processor faults, and preserves both stop and disposal failures.

## What it is for

Trellis already ships both ends of reliable messaging: the outbox captures domain events with the business change, then stages translated integration events during its post-commit relay; the inbox makes consumption idempotent by recording `(ConsumerId, MessageId)`. This package is the piece in between.

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
| `CorrelationId` | Optional application-owned business workflow id. |
| `trellis-causation-id` | Optional source domain outbox row id. |
| `traceparent`, `tracestate` | Optional W3C trace context persisted by the producer's outbox. |

Standard Service Bus members are used wherever one exists, so a message stays diagnosable in the portal and Service Bus Explorer, and routable by subscription filters, without those tools knowing anything about Trellis.

Missing optional metadata is valid for legacy messages. Blank `CorrelationId` and blank/null `trellis-message-source` mean absent; non-string source or invalid `trellis-causation-id` is dead-lettered with `servicebus_malformed_metadata`. Malformed W3C trace strings remain available to the inbox; non-string trace properties mean absent and never block processing.

The default layout is **one topic per contract**, named after the wire name. Subscribers declare interest by subscribing to the topics they want rather than filtering a firehose. Override `TopicNameResolver` to prefix an environment segment or to collapse contracts onto a shared topic — if you collapse them, filter subscriptions on `sys.Label`, which always carries the wire name.

### Settlement

| Situation | Action | Why |
|---|---|---|
| `InboxDispatchOutcome.Processed` | Complete | Side effects and the dedup row committed together. |
| `InboxDispatchOutcome.SkippedDuplicate` | Complete | Durably accounted for already. Abandoning would loop forever, since every redelivery reaches the same conclusion. |
| Handler throws | Abandon (by the SDK) | The dispatcher rolled back, so nothing was applied. `MaxDeliveryCount` eventually dead-letters a persistently failing message. |
| Unusable id, missing or unknown `Subject`, malformed body, source type or causation id | Dead-letter with a reason code | A property of the bytes, not of this consumer's state — retrying cannot change the outcome. |

The subscriber identity used for deduplication is `InboxOptions.ConsumerId`, not a transport setting, so a message arriving twice by two different routes is still processed once.

## Testing against the emulator

```
docker compose -f Trellis.Messaging.AzureServiceBus/tests/emulator/docker-compose.yml up -d
```

The emulator declares its entities in `Config.json` at startup and cannot create them at runtime, which is why the compose file and its entity list are part of the test fixture. Duplicate detection is deliberately **off** on the test topic: if the broker collapsed duplicate ids, the tests would pass even if the transport invented a fresh id per publish. With no backend or connection selected, the suite probes the local emulator and skips visibly if it is unavailable; it never passes against a substitute.

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-messaging-azureservicebus.md)
- [Outbox guide](https://xavierjohn.github.io/Trellis/articles/integration-outbox.html)
- [Inbox guide](https://xavierjohn.github.io/Trellis/articles/integration-inbox.html)

## Development

Run the package tests from the repository root:

```powershell
dotnet test --project Trellis.Messaging.AzureServiceBus\tests\Trellis.Messaging.AzureServiceBus.Tests.csproj -c Release
```

For an isolated run, use Testcontainers with a running Linux-container Docker daemon:

```powershell
$env:TRELLIS_TEST_SERVICEBUS = 'testcontainers'
dotnet test --project Trellis.Messaging.AzureServiceBus\tests\Trellis.Messaging.AzureServiceBus.Tests.csproj -c Release `
    --filter-class Trellis.Messaging.AzureServiceBus.Tests.ServiceBusTransportIntegrationTests `
    --fail-skips on --minimum-expected-tests 6
```

The fixture owns one Service Bus emulator (`2.0.1`), its separate SQL Server backend
(`2022-CU27-ubuntu-22.04`), a private network, and the shared SDK client for the six
integration cases. It loads the existing `Config.json` and publishes dynamic loopback-only
ports without changing a locally running emulator. Startup is bounded to five minutes;
teardown waits up to thirty seconds and attempts to dispose the client, broker, SQL backend,
and network in that order. Explicit Testcontainers startup failures fail rather than skip.
Broker and SQL logs are retained under the test output directory's
`TestResults\servicebus-integration`.

CI runs these six cases automatically inside the `build` job with skips and empty discovery
treated as failures. The pinned broker and SQL images are pulled while restore/build runs;
both pulls must finish successfully before testing. TRX results and container logs are uploaded
even after failures.

Unset `TRELLIS_TEST_SERVICEBUS` to return to the default local-emulator probe and start the
emulator with the Compose command above. Other nonblank backend values are configuration errors.

To use an isolated emulator on another port, set `TRELLIS_TEST_SERVICEBUS_CONNECTION` to its
connection string, including `UseDevelopmentEmulator=true`, before running the tests. Provision
the same topic and subscriptions from `tests\emulator\Config.json`. An explicit connection must
be reachable; probe failures fail the tests rather than falling back to the shared emulator or
skipping. Do not combine this override with `TRELLIS_TEST_SERVICEBUS=testcontainers`.
Leave both variables unset to retain the default local emulator behavior.
