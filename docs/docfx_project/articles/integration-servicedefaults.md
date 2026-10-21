---
title: Service Defaults (Composition Root)
package: Trellis.ServiceDefaults
topics: [composition-root, service-collection, di-wiring, builder, pipeline-ordering, messaging]
related_api_reference: [trellis-api-servicedefaults.md, trellis-api-mediator.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-efcore-inbox.md, trellis-api-efcore-outbox.md, trellis-api-messaging-azureservicebus.md, trellis-api-fluentvalidation.md, trellis-api-cookbook.md]
last_verified: 2026-10-20
audience: [developer]
---
# Service Defaults (Composition Root)

`Trellis.ServiceDefaults` provides one fluent builder — `services.AddTrellis(o => ...)` — that wires the Trellis integration modules in the canonical order so the mediator pipeline ends up with the right behaviors at the right positions.

It is **not** a mandatory dependency. If you only need one integration package, calling that package's direct registration helper (`AddTrellisAsp`, `AddTrellisBehaviors`, `AddTrellisFluentValidation` from `Trellis.Mediator.FluentValidation`, etc.) remains a valid choice. The builder exists to make composition-root code easier to read when several integrations are combined.

## Quick start

```csharp
builder.Services.AddTrellis(options => options
    .UseAsp()
    .UseScalarValueValidation()
    .UseMediator()
    .UseFluentValidation(typeof(Program).Assembly)
    .UseClaimsActorProvider()
    .UseResourceAuthorization(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>());

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
```

`AddTrellis` does NOT register your `DbContext`, your mediator handlers, or your route constraints — those are always application-owned. Validators, resource loaders, and domain event handlers are registered only when you opt into assembly scanning via the params-`Assembly[]` overloads (`UseFluentValidation(asm)`, `UseResourceAuthorization(asm)`, `UseDomainEvents(asm)`); the parameterless overloads register only the adapter / pipeline behavior and leave per-type registrations to you.

`UseScalarValueValidation()` is separate from `UseAsp()` because scalar-value validation mutates global `MvcOptions` / `JsonOptions` (model binders, JSON converters, `SuppressModelStateInvalidFilter` flip). Hosts that only need error-to-status mapping (e.g. an MVC site that does not bind value-object DTOs) can call `UseAsp()` alone without paying for the binder / converter mutation.

## Canonical order

Event dispatch retains nested aggregate responses until the owning successful unit-of-work commit; an outer DTO/Unit response still releases the nested batch. `UseOutbox<TContext>()` validates the reporting publisher during host startup and validates an integration publisher only when integration services are enabled. The collector is relay-translator-only: `Add` outside its active translation lease throws.

`AddTrellis(o => ...)` records requested modules during the configure callback. The following modules apply in this order; the [API reference](../api_reference/trellis-api-servicedefaults.md#behavior) lists every optional stage:

1. **ASP integration** (`UseAsp`) — `AddTrellisAsp(...)`.
2. **Scalar-value validation** (`UseScalarValueValidation`) — `AddScalarValueValidation()`.
3. **Actor provider** (`UseClaimsActorProvider` / `UseEntraActorProvider` / `UseDevelopmentActorProvider`), the optional **caching wrap** (`UseCachingActorProvider<T>`), and the optional **worker wrap** (`UseWorkerActor(systemActor)`) — the worker wrap is applied after caching so HTTP requests still flow through the inner provider (and its cache) and background-worker scopes short-circuit to the supplied system actor.
4. **Mediator behaviors** (`UseMediator`) — `AddTrellisBehaviors(...)`. Always present when any other Use that implies it is selected (FluentValidation, ResourceAuthorization, DomainEvents, EntityFrameworkUnitOfWork).
5. **Resource authorization** scanning (`UseResourceAuthorization(asm)`) when assemblies are supplied.
6. **FluentValidation** adapter and (optionally) scanning (`UseFluentValidation`).
7. **Domain event dispatch** (`UseDomainEvents`) — `DomainEventDispatchBehavior<,>` + default `IDomainEventPublisher` + scanned handlers.
8. **Integration event registration** (`UseIntegrationEvents`) — default best-effort publisher, scoped collector, and selected handler bindings.
9. **Typed/open command pre-commit hooks** (`UseCommandPreCommitHook`) — scoped bindings executed inside the transaction behavior.
10. **EF Core Unit of Work** (`UseEntityFrameworkUnitOfWork<TContext>`) — innermost Mediator behavior.
11. **Outbox relay** (`UseOutbox<TContext>`) — hosted-service registration, not a pipeline behavior.
12. **Opt-in outbox readiness** (`UseOutboxReadiness<TContext>(profile)`) — named health-check registration, not a pipeline behavior.
13. **Inbox dispatcher** (`UseInbox<TContext>`) — inbound service registration, not a pipeline behavior.

This sequence preserves the central pipeline invariant: the transactional behavior is closest to the handler, so commit failures remain visible to outer logging/tracing/exception behaviors. Domain events fire after the transaction commits because dispatch is registered before the unit of work.

The standard Mediator stage now includes `AuthorizationContextBehavior` before static
and direct/via resource checks. Those checks share one Actor snapshot per dispatch, which
the [six actor-aware handler bases](integration-mediator.md#checked-actor-and-resource-parameters)
can receive along with the loaded resource/leaf. Existing builder slots install the
context automatically; no new `UseXxx` call is needed. Closed/open context descriptors
normalize without duplicate execution, including resource registrations preceding the
standard pipeline.

## Same-commit staging and validation

```csharp
builder.Services.AddTrellis(options => options
    .UseCommandPreCommitHook(typeof(RequiredAuditHook<,>))
    .UseEntityFrameworkUnitOfWork<AppDbContext>());
```

`UseCommandPreCommitHook` implies `UseMediator` and installs the transaction behavior,
but does not register the store or an audit schema. Hooks run after each committable
handler and before persistence, regardless of fluent or direct unit-of-work registration
order. Repeated identical bindings deduplicate; distinct hooks run in binding order.
The constrained open form applies to every matching command, including future commands
carrying its marker. Use `UseCommandPreCommitHook<TMessage,TResponse,THook>()` for a
single explicit binding.

Ordinary handler failures skip hooks. The first hook failure vetoes even explicit
persist-on-failure outcomes; nested hooks stage before their deferred commit.
Any hook veto, exception, or cancellation permanently invalidates the unit of work:
even ignored nested failures block its owner and every later commit. Scope disposal
or new scopes never reset it. Discard the unit of work and context; this is not rollback.
`CommandDispatchState` provides typed handler/hook entries without changing entity results.
The behavior opens a fresh frame before the handler and always closes it; nested frames
isolate and restore parent entries. Hooks read/remove only their own entries, and failed
handlers still receive frame cleanup. See [Recipe 16](../api_reference/trellis-api-cookbook.md#same-commit-audit-staging)
for the complete application-owned audit pattern.

For explicitly closed/generated pipelines, bind the hook interface directly alongside
the closed transaction behavior and register `CommandDispatchState` directly when needed;
do not use these slots, which imply the managed open pipeline. Hook registration does
not remove Native DI's struct-result restriction.

## Integration delivery boundaries

All `UseIntegrationEvents(...)` overloads wire the default **best-effort local** publisher when none is registered. It logs and swallows ordinary consumer failures, so the relay processes an integration row without retrying those consumers. For reliable broker handoff, replace the publisher with an adapter that awaits acceptance and propagates publication failures; acceptance is not downstream consumer success.

`UseInbox<TContext>()` registers a separate non-swallowing consumption seam: handler writes through its context and the dedup row commit atomically. It does not reroute default local publication. An adapter must actually call `IInboxDispatcher.DispatchAsync` with the stable message id and expose failures for transport redelivery. See [outbox delivery semantics](integration-outbox.md#delivery-semantics) and [inbox wiring](integration-inbox.md#the-wiring).

### Messaging setup by deployment role

Do not treat messaging as one switch. The producer selects domain/integration events,
the unit of work, and outbox; a broker publisher is registered directly. The consumer
selects integration handlers and inbox; a broker consumer is registered directly.

| Process responsibility | Select here | Complete outside the builder |
|---|---|---|
| Produce crash-safe external events | `UseDomainEvents`, `UseIntegrationEvents`, `UseEntityFrameworkUnitOfWork`, `UseOutbox` | Context interceptor, outbox model/migration, translators, stable wire-name contracts |
| Publish to Azure Service Bus | No vendor slot | One shared singleton `ServiceBusClient` + `AddAzureServiceBusIntegrationEventPublisher` |
| Consume idempotently | `UseIntegrationEvents`, `UseInbox` | Inbox model/migration and integration-event handlers |
| Receive from Azure Service Bus | No vendor slot | The shared singleton `ServiceBusClient` + `AddAzureServiceBusIntegrationEventConsumer` |

See the canonical [producer and consumer compositions](../api_reference/trellis-api-servicedefaults.md#messaging-setup-by-deployment-role)
for complete registration examples. The direct transport calls are deliberate: they
keep the Azure SDK out of `Trellis.ServiceDefaults`. The inbox registration is not
routing, and broker acceptance is not downstream consumer success.

## Mutually-exclusive slots

Enable outbox readiness only by explicitly selecting
`OutboxReadinessOptions.CreateStagedOperationalProfile()` and adding
`.UseOutboxReadiness<AppDbContext>(profile)` alongside `.UseOutbox<AppDbContext>()`.
The profile is immutable and can be tuned with `with`. The named `trellis-outbox`
check reports bounded metadata and local relay evidence. Capture age and
application-provided consumer lag are separately enabled on that profile; register
`IConsumerLagProvider` directly because it is a leaf adapter, not another builder slot.
The host owns authorization and traffic-readiness versus alert-only
exposure, and no endpoint or liveness policy is added. See [outbox readiness](../api_reference/trellis-api-efcore-outbox.md#outbox-readiness).

For split API/worker deployment, the API can select
`OutboxReadinessOptions.CreateMetadataOnlyProfile(workerMaxAttempts)` through the same
slot without `UseOutbox`. Match `WorkerMaxAttempts` to the worker's retry policy using
one configuration key; incidental local options are ignored without comparison or warning.
Metadata-only cannot detect a dead low-volume worker. Generic Host lifecycle startup
validates local-mode relay configuration before hosted services start; bare providers
do not run this validation.

| Slot | Single-selection rule |
|---|---|
| Actor provider | At most one of `UseClaimsActorProvider`, `UseEntraActorProvider`, `UseDevelopmentActorProvider`. Throws `InvalidOperationException` on duplicate (same kind or different). |
| Caching actor wrap | At most one `UseCachingActorProvider<T>()`. Throws `InvalidOperationException` on duplicate. |
| Worker actor wrap | At most one `UseWorkerActor(systemActor)`. Throws `InvalidOperationException` on duplicate. The underlying `AddTrellisWorkerActor(...)` also rejects singleton `IActorProvider` registered via type or factory (silent lifetime conversion) and transient `IActorProvider` (silent upgrade to scoped); singleton via `ImplementationInstance` is supported. |
| Unit of work | At most one `UseEntityFrameworkUnitOfWork<TContext>()` per composition. Throws `InvalidOperationException` on duplicate (same `TContext` or different). Read/write context splits should run as separate composition roots or use a multi-tenant `DbContext`. |
| Outbox readiness | At most one `UseOutboxReadiness<TContext>(profile)` selection. Direct/builder calls with equal context/profile deduplicate; conflicts throw. Local mode requires the matching relay; metadata-only requires the explicit worker retry limit and may coexist with a different-context relay. |

## Per-request actor caching

`UseCachingActorProvider<T>()` wraps the inner `IActorProvider` with a per-request caching decorator. Chain it AFTER the matching `UseXxxActorProvider(...)` so the inner provider's `IOptions<TOptions>` is configured before the wrap replaces the `IActorProvider` slot.

```csharp
builder.Services.AddTrellis(options => options
    .UseClaimsActorProvider(o => o.ActorIdClaim = "sub")
    .UseCachingActorProvider<ClaimsActorProvider>());
```

## Order-independence for explicit resource-authorization registrations

The no-assembly form `UseResourceAuthorization()` avoids scanning while consumers register each `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` explicitly via `services.AddResourceAuthorization<TMessage, TResource, TResponse>()`. Those explicit calls can be made **before** `AddTrellis(...)`; `AddTrellisBehaviors()` (called by `UseMediator()`) detects pre-existing closed-generic resource-auth behaviors and re-positions them to sit immediately before `ValidationBehavior<,>` in the canonical pipeline envelope. This mirrors the same symmetry between `AddTrellisUnitOfWork<TContext>` and `AddDomainEventDispatch`: pipeline-position-aware registrations are order-independent regardless of which one runs first.

```csharp
// Either order works; resource-auth ends up in the canonical position.
builder.Services.AddResourceAuthorization<UpdateOrderCommand, Order, Result<string>>();
builder.Services.AddScoped<IResourceLoader<UpdateOrderCommand, Order>, OrderLoader>();
builder.Services.AddTrellis(options => options.UseResourceAuthorization());
```

## AOT compatibility

`Trellis.ServiceDefaults` itself is **AOT- and trim-compatible** (`<IsAotCompatible>true</IsAotCompatible>`, `<IsTrimmable>true</IsTrimmable>`, `<EnableAotAnalyzer>true</EnableAotAnalyzer>`, `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`). The compatibility surface is split between explicit AOT-safe overloads and annotated convenience overloads:

| Slot | AOT-safe builder shape | Scanning overload (`[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`) |
|---|---|---|
| FluentValidation | `o.UseFluentValidation()` plus `o.UseFluentValidation<TValidator, TMessage>()` per validator | `o.UseFluentValidation(asm)` |
| Resource authorization | `o.UseResourceAuthorization()` plus `o.UseResourceAuthorization<TMessage, TResource, TResponse>()` per command | `o.UseResourceAuthorization(asm)` |
| Resource authorization with shared loader | `o.UseSharedResourceAuthorization<TMessage,TResource,TId,TResponse>()` per message, plus a separately registered shared-loader implementation | `o.UseResourceAuthorization(asm)` |
| Domain events (response-shape) | `o.UseDomainEvents()` plus `o.UseDomainEvents<TEvent, THandler>()` per handler | `o.UseDomainEvents(asm)` |
| Domain events (tracked-aggregate) | `o.UseTrackedAggregateDomainEvents()` plus `o.UseTrackedAggregateDomainEvents<TEvent, THandler>()` per handler | `o.UseTrackedAggregateDomainEvents(asm)` |

The AOT-safe overloads use open-generic DI registrations and explicit closed-type method calls — no reflection over assemblies. The scanning overloads remain available for non-AOT consumers and surface IL2026 / IL3050 at the consumer's call site; trimmed/AOT applications must either switch to explicit registrations or make that choice visible on their own composition code, for example with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` or an intentional suppression.

**Runtime DI qualification:** Native AOT cannot close open behaviors over value-type
`Result<T>` responses. Such a host needs the [literal closed-generator
pipeline](../api_reference/trellis-api-mediator.md#native-aot-registration) and direct
typed resource registrations, without `UseMediator` or slots that imply it. The
analyzer-clean registration surface alone does not prove native dispatch.

`UseEntityFrameworkUnitOfWork<TContext>()` is also annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` because the EF Core integration depends on runtime reflection and is excluded from the Trellis AOT publish gate. AOT consumers can still use the ASP, Mediator, FluentValidation, authorization, and domain-event slots through this builder, but should compose data access separately.

## Layered configuration via repeated calls

`UseAsp(configure)` and `UseMediator(configureTelemetry)` allow repeated calls; the configure delegates compose in call order rather than overwriting. This supports library-then-host configuration patterns:

```csharp
// Library defaults applied first…
builder.Services.AddTrellis(o => o
    .UseAsp(opts => opts.MapError<MyDomainError>(StatusCodes.Status409Conflict))
    .UseAsp(opts => opts.HonorPrefer())); // host adds prefer handling on top
```

## See also

- [API reference](../api_reference/trellis-api-servicedefaults.md) — type tables and method signatures.
- [Cookbook Recipe 12: DI wiring playbook](../api_reference/trellis-api-cookbook.md#recipe-12--di-wiring-playbook-addtrellis-composition-builder).
- Per-package articles: [`integration-aspnet.md`](integration-aspnet.md), [`integration-mediator.md`](integration-mediator.md), [`integration-fluentvalidation.md`](integration-fluentvalidation.md), [`integration-ef.md`](integration-ef.md), [`integration-asp-authorization.md`](integration-asp-authorization.md).
