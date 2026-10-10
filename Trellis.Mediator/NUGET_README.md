# Trellis.Mediator

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Mediator.svg)](https://www.nuget.org/packages/Trellis.Mediator)

Result-aware pipeline behaviors for [Mediator](https://github.com/martinothamar/Mediator) that keep handlers focused on business work.

## Installation
```bash
dotnet add package Trellis.Mediator
```

## Quick Example
```csharp
using Mediator;
using Trellis;
using Trellis.Mediator;

public sealed record GetOrderQuery(string Id) : IQuery<Result<string>>, IValidate
{
    public IResult Validate() =>
        string.IsNullOrWhiteSpace(Id)
            ? Result.Fail(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(Id)), ValidationCodes.ValueNotEmpty) { Detail = "Order ID is required." })))
            : Result.Ok();
}

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
```

> [!IMPORTANT]
> Use `ServiceLifetime.Scoped` when calling `AddMediator(...)` in a host with a request scope. The Trellis behaviors are scoped (they depend on per-request services); the Mediator default of `Singleton` will fail ASP.NET's root-scope validation as soon as the first behavior tries to resolve a scoped dependency.

## Key Features
- Six parameterless actor-aware bases: `ActorCommandHandler`, `ActorQueryHandler`, direct `ActorResourceCommandHandler` / `ActorResourceQueryHandler`, and indirect `ActorResourceViaCommandHandler` / `ActorResourceViaQueryHandler`. Override protected `Handle(message, actor[, resource/leaf], token)`; concrete constructors keep business dependencies only.
- Static/resource stages share one Actor reference per dispatch; the integral `AuthorizationContextBehavior` makes it available only after all gates succeed and isolates nested/concurrent sends. Via handlers receive the leaf, not owners.
- The only public handler entry is `Handle(message, token)`, which requires an authorized dispatch. Test through Mediator with `TestActorProvider` and fake loaders/business dependencies; no public actor/resource bypass or replacement test seam is provided. Existing accessor-only handlers and provider extensions remain supported.
- Adds validation, authorization, tracing, logging, and exception behaviors that understand `Result<T>`.
- Tracing is registered but not *collected* until you call `AddTrellisMediatorInstrumentation()` on your `TracerProviderBuilder`; without it the handler span is silently never recorded.
- Short-circuits failures before handlers do unnecessary work.
- Unified `ValidationBehavior` composes `IValidate` + every `IMessageValidator<TMessage>` (e.g., the `Trellis.FluentValidation` adapter) and aggregates failures into one response.
- Supports resource authorization with explicit or assembly-scanned registration.
- `AddSharedResourceAuthorization<TMessage, TResource, TId, TResponse>()` registers the authorization behavior, authorized-resource accessor, and shared-loader adapter without scanning. Register `SharedResourceLoaderById<TResource,TId>` separately. Existing per-message loaders are preserved; the lower-level registration APIs remain unchanged.
- Per-resource `HideAsNotFound` gives missing, removed (`Gone`), and withheld resources the same public NotFound, without original metadata or causes. `HideExistence<TResource>(code: ..., detail: ...)` optionally supplies fixed public metadata; the projection form selects a separate public type.
- **Domain event dispatch**: implement `IDomainEventHandler<TEvent>`, register with `AddDomainEventDispatch(...)`, and the framework snapshots `IAggregate.UncommittedEvents()` after a successful `Result<TAggregate>` command. It publishes only that snapshot, calls `AcceptChanges()` only on clean validation, and throws `DomainEventHandlerCascadedException` if the pending-event list at the end of dispatch differs from the entry snapshot (length or reference equality — i.e., a handler raised new events, cleared via `AcceptChanges`, replaced, or reordered).
- **Tracked-aggregate dispatch (opt-in)**: `TrackedAggregateDomainEventDispatchBehavior<,>` reads committed aggregates from the unit of work and applies the same snapshot contract across all of them, including cross-aggregate cascade detection. Mutually exclusive with response-shape dispatch.
- **Operational caveat**: domain dispatch runs after EF unit-of-work commit. Cascade detection can return a failure-shaped response after the database write is durable. In-pipeline domain-handler exceptions are logged and swallowed by the default publisher because its post-commit callers cannot retry; the transactional outbox (`Trellis.EntityFrameworkCore.Outbox`) uses reporting domain dispatch to retry failed domain handlers individually. Integration publication has a separate completion boundary.

- Nested domain-event dispatch waits for the owning successful unit-of-work commit, retaining inner aggregate responses even when the outer command returns a DTO or Unit. Failure/throw discards the dispatch batch without clearing events; the outbox still captures events on successful `FailAfterCommit` saves.
- `IIntegrationEventCollector` is translator-only. The outbox relay opens `BeginTranslation()` while publishing and draining; `Add` from a command or outside that active lease throws rather than silently losing events.
- Default `IIntegrationEventPublisher` fan-out is best-effort local notification: ordinary consumer failures are logged and swallowed, so the relay processes the integration row without a consumer retry. Broker adapters must await publication acceptance and propagate publication failures; acceptance is not downstream consumer success.
- `IIntegrationEventHandler<T>` is also used by the non-swallowing transactional inbox. An adapter must call `IInboxDispatcher` with the stable message id and expose failures for transport redelivery; registering an inbox does not reroute the default publisher. Only handler writes through the inbox's context share its atomic dedup commit.
- `IntegrationMessageContext.BeginCorrelation("workflow-id")` supplies application-owned business correlation; inbox dispatch scopes inherit nonblank inbound correlation and make the inbound message id available to outbox capture. `OutboundIntegrationMessage` carries the persisted lineage and W3C trace context across transports.

## Documentation
- [Handler migration and dispatch contracts](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-mediator.html#actor-aware-handler-bases): standard managed registrations supply the context automatically; manual authorization pipelines must add it before authorization.
- [Native AOT registration](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-mediator.html#native-aot-registration): literal generator `typeof(...)` configuration provides closed behaviors. Native DI cannot close open behaviors over struct `Result<T>` responses; do not mix that shape with `AddTrellisBehaviors` or builder slots that imply it.
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-mediator.html)
- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-mediator.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
