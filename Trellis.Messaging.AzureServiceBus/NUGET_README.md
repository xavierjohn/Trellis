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

Register a `ServiceBusClient` separately. Consumers also require an `IInboxDispatcher`, normally from `AddTrellisInbox<TContext>()`.

## Key Features

- Carries the producer's outbox row ID verbatim as the Service Bus `MessageId`, preserving inbox deduplication across redelivery.
- Uses one topic per stable integration-event wire name by default.
- Replaces the in-process publisher to prevent duplicate local and broker delivery.
- Completes processed or duplicate messages, retries handler failures, and dead-letters unusable payloads with a reason code.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-messaging-azureservicebus.html)
- [Outbox guide](https://xavierjohn.github.io/Trellis/articles/integration-outbox.html)
- [Inbox guide](https://xavierjohn.github.io/Trellis/articles/integration-inbox.html)
