---
title: Mediator Pipeline
package: Trellis.Mediator
topics: [mediator, command, query, pipeline, behaviors, authorization, validation, telemetry]
related_api_reference: [trellis-api-mediator.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Mediator Pipeline

`Trellis.Mediator` registers result-aware pipeline behaviors around the [Mediator](https://github.com/martinothamar/Mediator) library so handlers stay focused on business work while exception safety, tracing, logging, authorization, and validation run as composable pre/post stages.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Register the standard Trellis behaviors | `services.AddTrellisBehaviors()` | [Quick start](#quick-start) |
| Inspect or override the canonical behavior order | `ServiceCollectionExtensions.PipelineBehaviors` | [Pipeline order](#pipeline-order) |
| Gate a message on static permissions | Implement `IAuthorize` on the message | [Permission authorization](#permission-authorization) |
| Authorize against a loaded resource (ownership, tenancy) | Implement `IAuthorizeResource<T>` and register a loader | [Resource authorization](#resource-authorization) |
| Reuse one loader across many commands for the same resource | `IIdentifyResource<T, TId>` + `SharedResourceLoaderById<T, TId>` | [Shared resource loaders](#shared-resource-loaders) |
| Self-validate a message | Implement `IValidate.Validate()` | [Validation](#validation) |
| Plug FluentValidation into the same stage | `services.AddTrellisFluentValidation()` | [FluentValidation adapter](#fluentvalidation-adapter) |
| Show `Error.Detail` in logs/traces (dev only) | `AddTrellisBehaviors(o => o.IncludeErrorDetail = true)` | [Telemetry redaction](#telemetry-redaction) |
| Convert thrown exceptions to typed failures | `ExceptionBehavior` (always-on) | [Exception safety net](#exception-safety-net) |
| Dispatch domain events that aggregates raised during a command | `services.AddDomainEventDispatch()` + `IDomainEventHandler<TEvent>` | [Domain event dispatch](#domain-event-dispatch) |
| Manually dispatch an aggregate's events from a non-aggregate handler or `BackgroundService` | `IDomainEventPublisher.DispatchAggregateEventsAsync(aggregate, ct)` | [Dispatching events from non-aggregate response shapes](#dispatching-events-from-non-aggregate-response-shapes-post-commit-safe) |
| Auto-dispatch domain events from *every* aggregate the unit-of-work tracked, regardless of response shape | `services.AddTrellis(t => t.UseTrackedAggregateDomainEvents(...))` | [Auto-dispatching from outcome-DTO commands](#auto-dispatching-from-outcome-dto-commands-opt-in-tracked-behavior) |
| Use a custom envelope response type around an aggregate | `TResponse : IResult<TAggregate>, IFailureFactory<TResponse>` | [Custom envelope response types](#custom-envelope-response-types) |
| Persist a `permanently_failed` row alongside a failure outcome (worker pattern) | `Result.FailAfterCommit<T>(error)` | [Persisting failure state from a worker handler](#persisting-failure-state-from-a-worker-handler) |

## Use this guide when

- You are wiring `Trellis.Mediator` into a Web API or Worker host and need the canonical behavior registration.
- You want to move authorization or validation off your handlers and into the pipeline.
- You want consistent OpenTelemetry spans and structured logs for every command/query.
- You need to plug FluentValidation (or another validation library) into the same validation stage as `IValidate`.

## Surface at a glance

| Type / member | Kind | Purpose |
|---|---|---|
| `AddTrellisBehaviors()` | DI extension | Registers the five always-on behaviors (idempotent). |
| `AddTrellisBehaviors(Action<TrellisMediatorTelemetryOptions>)` | DI extension | Same, with telemetry options (e.g., `IncludeErrorDetail`). |
| `AddResourceAuthorization(params Assembly[])` | DI extension | Scans assemblies for `IAuthorizeResource<>`, loaders, and shared loaders. |
| `AddResourceAuthorization<TMessage, TResource, TResponse>()` | DI extension | Explicit registration (AOT/trimming friendly). |
| `AddSharedResourceLoader<TMessage, TResource, TId>()` | DI extension | Bridges an `IIdentifyResource<T,TId>` message to a `SharedResourceLoaderById<T,TId>`. |
| `IValidate` | Interface | Message-side hook; `IResult Validate()` runs before the handler. |
| `IMessageValidator<TMessage>` | Interface | DI-resolved async validator; aggregated by `ValidationBehavior`. |
| `TrellisMediatorTelemetryOptions.IncludeErrorDetail` | Property | Opt-in to include `Error.Detail` in logs/traces (default `false`). |
| `TracingBehavior<,>.ActivitySourceName` | `const string` | `"Trellis.Mediator"` — add this to your OpenTelemetry config. |
| `ServiceCollectionExtensions.PipelineBehaviors` | Property | Ordered behavior list for AOT `MediatorOptions.PipelineBehaviors`. |
| `AddDomainEventDispatch()` / `AddDomainEventDispatch(params Assembly[])` | DI extension | Registers `DomainEventDispatchBehavior<,>` (open-generic) + the default `IDomainEventPublisher`; the assembly overload also scans for `IDomainEventHandler<TEvent>` implementations. Idempotent. |
| `AddDomainEventHandler<TEvent, THandler>()` | DI extension | AOT/trim-friendly per-handler registration (also wires up the dispatch behavior + publisher). |
| `IDomainEventHandler<TEvent>` | Interface | Side-effect handler invoked once per matching event after the command commits. |
| `IDomainEventPublisher` | Interface | Resolves `IDomainEventHandler<TEvent>` instances and fans events out; default impl is `MediatorDomainEventPublisher` (DI-resolved, scoped). |
| `DomainEventPublisherExtensions.DispatchAggregateEventsAsync(this IDomainEventPublisher, IAggregate, CancellationToken)` | Extension method | Post-commit-only helper that uses the strict snapshot-and-cascade-validation contract for handlers that do not return `Result<TAggregate>`. |
| `TrackedAggregateDomainEventDispatchBehavior<TMessage, TResponse>` | Pipeline behavior | Opt-in alternative to `DomainEventDispatchBehavior<,>` that auto-dispatches events from every aggregate the unit-of-work tracked at commit time, regardless of response shape. Registered via `AddTrackedAggregateDomainEventDispatch()` or `TrellisServiceBuilder.UseTrackedAggregateDomainEvents(...)`. Mutually exclusive with the response-shape behavior. |
| `AddTrackedAggregateDomainEventDispatch()` | DI extension | Registers the tracked behavior + default publisher and replaces any prior response-shape dispatch registration. Idempotent. |
| `ITrackedAggregateSource` | Interface | Sidecar exposed by `EfUnitOfWork<TContext>` (or a custom `IUnitOfWork`) — returns the aggregates the unit-of-work committed on its most recent successful save. Empty after a failed commit or thrown save. |

Full signatures: [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md).

## Installation

```bash
dotnet add package Trellis.Mediator
```

## Quick start

Register Mediator with the **scoped** lifetime, add the Trellis behaviors, and your handlers immediately get exception safety, tracing, logging, authorization, and validation.

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();

var app = builder.Build();
app.Run();

public sealed record PublishDocumentCommand(string DocumentId)
    : ICommand<Result<Unit>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["documents:publish"];
}

public sealed class PublishDocumentHandler : ICommandHandler<PublishDocumentCommand, Result<Unit>>
{
    public ValueTask<Result<Unit>> Handle(PublishDocumentCommand command, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result.Ok());
}
```

> [!IMPORTANT]
> Pass `opts => opts.ServiceLifetime = ServiceLifetime.Scoped`. The Trellis behaviors depend on per-request services (`IActorProvider`, `IUnitOfWork`, `IMessageValidator<>` adapters). Mediator's default lifetime is `Singleton`, which fails ASP.NET's root-scope validation as soon as a behavior tries to resolve a scoped dependency.

## Pipeline order

`AddTrellisBehaviors()` registers the five always-on behaviors in this fixed order (outermost → innermost). The opt-in entries in rows 5, 7, and 8 slot in only when their registration helpers are called.

| # | Behavior | Runs for | What it does |
|---|---|---|---|
| 1 | `ExceptionBehavior` | all messages | Catches everything except `OperationCanceledException`; returns `Error.Unexpected`. |
| 2 | `TracingBehavior` | all messages | Opens an `Activity` under `"Trellis.Mediator"`; tags `error.code` / `error.type` on failure, records exception events for thrown handlers, and leaves consumer-initiated cancellations non-error. |
| 3 | `LoggingBehavior` | all messages | Structured start/end with elapsed ms; emits `Error.Code` on failure. |
| 4 | `AuthorizationBehavior` | `IAuthorize` messages | Resolves the actor and checks `RequiredPermissions`. |
| 5 | `ResourceAuthorizationBehavior` *(opt-in)* | `IAuthorizeResource<T>` messages | Loads the resource and calls `Authorize(actor, resource)`. Inserted by `AddResourceAuthorization(...)` immediately before `ValidationBehavior`. |
| 6 | `ValidationBehavior` | all messages | Runs `IValidate.Validate()` and every `IMessageValidator<TMessage>`; aggregates `Error.InvalidInput`. |
| 7 | `DomainEventDispatchBehavior` *(opt-in)* | `ICommand<TResponse>` where `TResponse : IResult` | After a successful response, extracts the aggregate via `IResult<TAggregate>` and publishes the events it raised. Inserted by `AddDomainEventDispatch(...)`. See [Domain event dispatch](#domain-event-dispatch). |
| 8 | `TransactionalCommandBehavior` *(opt-in, EFCore)* | `ICommand<TResponse>` | `IUnitOfWork.CommitAsync` on success; wraps each command in `using var scope = unitOfWork.BeginScope();` so nested commands defer commit to the outermost scope. Register **after** `AddTrellisBehaviors()` so it lands innermost. See [Nested commands and scope-aware commit](integration-ef.md#nested-commands-and-scope-aware-commit). |

The first five live in `ServiceCollectionExtensions.PipelineBehaviors` for the AOT-friendly source-generator path; assign that list to `MediatorOptions.PipelineBehaviors` when configuring `AddMediator`.

> [!NOTE]
> Rows 7 and 8 are designed to be **registration-order-independent**: `AddDomainEventDispatch(...)` and `AddTrellisUnitOfWork<TContext>()` both detect the other and shuffle so the canonical order (events fire after the transaction commits, so handlers see committed state) holds regardless of which `services.Add*` call comes first.

## Permission authorization

Implement `IAuthorize` when a message always requires the same permission set. `AuthorizationBehavior` resolves the current `Actor` from `IActorProvider` and rejects with `new Error.Forbidden("authorization.insufficient.permissions") { Detail = "Insufficient permissions." }` when any required permission is missing.

```csharp
using System.Collections.Generic;
using Mediator;
using Trellis;
using Trellis.Authorization;

public sealed record PublishDocumentCommand(string DocumentId)
    : ICommand<Result<Unit>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["documents:publish"];
}
```

`AuthorizationBehavior` performs no I/O — it only reads from the resolved `Actor`. Use `IAuthorizeResource<T>` (next section) when the answer depends on the resource itself.

## Resource authorization

Use `IAuthorizeResource<TResource>` when authorization depends on the resource (ownership, tenancy, state). The pipeline loads the resource first, then calls `message.Authorize(actor, resource)`.

`ResourceAuthorizationBehavior` is **opt-in**: it is added only when you call `AddResourceAuthorization(...)`. Without that call the behavior never runs even if the message implements `IAuthorizeResource<T>`.

### Per-message loader

Use `ResourceLoaderById<TMessage, TResource, TId>` for the common "message has an id, repository loads by id" case.

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

public sealed record Document(Guid Id, string OwnerId, string Title);

public interface IDocumentRepository
{
    Task<Result<Document>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<Document>> RenameAsync(Document document, string title, CancellationToken cancellationToken);
}

public sealed record RenameDocumentCommand(Guid DocumentId, string Title)
    : ICommand<Result<Document>>, IAuthorize, IAuthorizeResource<Document>
{
    public IReadOnlyList<string> RequiredPermissions => ["documents:edit"];

    public IResult Authorize(Actor actor, Document resource) =>
        actor.IsOwner(resource.OwnerId)
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("documents.rename") { Detail = "Only the owner can rename this document." });
}

public sealed class RenameDocumentResourceLoader(IDocumentRepository repository)
    : ResourceLoaderById<RenameDocumentCommand, Document, Guid>
{
    protected override Guid GetId(RenameDocumentCommand message) => message.DocumentId;

    protected override Task<Result<Document>> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        repository.GetByIdAsync(id, cancellationToken);
}

public static class Composition
{
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
        builder.Services.AddTrellisBehaviors();
        builder.Services.AddResourceAuthorization(typeof(RenameDocumentCommand).Assembly);
    }
}
```

For the `RenameDocumentCommand` above, the per-request order becomes: permission check → resource load + `Authorize(actor, resource)` → validation → handler.

### Shared resource loaders

When several commands authorize against the same resource, register one `SharedResourceLoaderById<TResource, TId>` and let messages declare `IIdentifyResource<TResource, TId>`. Assembly scanning auto-bridges them; explicit registration uses `AddSharedResourceLoader<,,>`.

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

public sealed record Order(Guid Id, string OwnerId);

public interface IOrderRepository
{
    Task<Result<Order>> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class OrderResourceLoader(IOrderRepository repository)
    : SharedResourceLoaderById<Order, Guid>
{
    public override Task<Result<Order>> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        repository.GetByIdAsync(id, cancellationToken);
}

public sealed record CancelOrderCommand(Guid OrderId)
    : ICommand<Result<Unit>>, IAuthorizeResource<Order>, IIdentifyResource<Order, Guid>
{
    public Guid GetResourceId() => OrderId;

    public IResult Authorize(Actor actor, Order resource) =>
        actor.IsOwner(resource.OwnerId)
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("orders.cancel") { Detail = "Only the owner can cancel this order." });
}

public static class Composition
{
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
        builder.Services.AddTrellisBehaviors();
        builder.Services.AddScoped<SharedResourceLoaderById<Order, Guid>, OrderResourceLoader>();

        // Explicit (AOT/trimming friendly):
        builder.Services.AddResourceAuthorization<CancelOrderCommand, Order, Result<Unit>>();
        builder.Services.AddSharedResourceLoader<CancelOrderCommand, Order, Guid>();

        // Equivalent via assembly scan (not AOT-friendly):
        // builder.Services.AddResourceAuthorization(typeof(CancelOrderCommand).Assembly);
    }
}
```

> [!TIP]
> Explicit `IResourceLoader<TMessage, TResource>` registrations always win over the shared-loader bridge.

### Hide existence with `AuthFailureExposurePolicy.HideAsNotFound`

For resources whose mere existence is sensitive — incident reports, security findings, internal correspondence, private profiles — the default `Forbidden` response leaks that the resource exists. Opt the resource into `AuthFailureExposurePolicy.HideAsNotFound` via `ResourceAuthorizationOptions` and the pipeline translates `Error.Forbidden` and `Error.AuthenticationRequired` to `new Error.NotFound(ResourceRef)`. Other error kinds (`Unexpected`, `Unavailable`, loader-`NotFound`, transport faults) returned by the **direct** loader pass through verbatim — operational signal is never hidden. For multi-hop `IAuthorizeResourceVia<TOwner>` commands, intermediate / owner hop failures are already collapsed to a synthetic `Forbidden("resource.authorization-via.load-failed")` by the v1 multi-hop security model (existence-leak protection on related resources) before exposure translation runs; under `HideAsNotFound` that synthetic Forbidden becomes `404`, so a downstream owner-service `Unavailable` surfaces as `404` to the consumer. See cookbook [Recipe 32](../api_reference/trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound) for the leaf-vs-hop distinction in detail.

```csharp
builder.Services.AddTrellis(options => options
    .UseResourceAuthorization()                                                // pipeline enabled
    .UseResourceAuthorization<GetIncidentQuery, Incident, Result<IncidentDto>>()
    .UseResourceAuthorization(o => o.HideExistence<Incident>()));              // opt-in per resource
```

Default is `Propagate` — no behavior change for resources that don't opt in. Set `DefaultExposurePolicy = HideAsNotFound` to flip the default service-wide and use `Propagate<TResource>()` for individual safe-to-disclose resources. Each translation emits a structured `[LoggerMessage]` event `ExistenceHidden` carrying the original `Kind` and `Code` so SecOps can audit the underlying denial reason via SIEM.

**Caveats.**
- **`AuthorizationBehavior` runs first.** Commands implementing both `IAuthorize` and `IAuthorizeResource<T>` have static-permission `AuthenticationRequired` / `Forbidden` surfaced by `AuthorizationBehavior` — those are NOT translated, because the static-auth behavior has no concept of the resource. Commands needing existence-hiding to apply to anonymous probes must omit `IAuthorize`.
- **Cache safety.** Synthetic 404s look identical to real 404s on the wire — pair these endpoints with `Cache-Control: no-store` or `private` so a shared cache cannot serve an unauthorized actor's 404 to a later authorized actor.
- **Via commands key on the leaf.** `HideExistence<Match>()` covers commands implementing `IAuthorizeResourceVia<Team>` + `IIdentifyResource<Match, MatchId>` — the synthetic NotFound references the leaf the command identifies, never the owner.

See cookbook [Recipe 32](../api_reference/trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound) for the projection-loader overload, SIEM query examples, and the full worked example.

## Validation

`ValidationBehavior` runs for every message and pulls violations from two sources.

| Source | Use it for |
|---|---|
| `IValidate.Validate()` on the message | Cross-field invariants and domain rules awkward to express as property checks. |
| `IEnumerable<IMessageValidator<TMessage>>` from DI | Property-level validation, FluentValidation adapter, or any custom validator package. |

**Aggregation rules**

- All `Error.InvalidInput` failures from both sources are merged into a single `Error.InvalidInput` whose `Fields` and `Rules` collect every reported violation. The caller never gets "the first failure" — they get the full list in one round trip.
- An `Error.InvalidInput` with empty `Fields` and empty `Rules` still short-circuits the handler.
- A non-`Error.InvalidInput` failure (e.g., `Error.Conflict`, `Error.Forbidden`) returned by any source short-circuits the stage immediately and is propagated as-is.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Trellis;
using Trellis.Mediator;

public sealed record ArchiveDocumentCommand(string DocumentId, bool IsArchived)
    : ICommand<Result<Unit>>, IValidate
{
    public IResult Validate() =>
        IsArchived
            ? Result.Ok()
            : Result.Fail(new Error.Conflict(null, "domain.violation") { Detail = "Only archived documents can be processed." });
}

public sealed class ArchiveDocumentHandler : ICommandHandler<ArchiveDocumentCommand, Result<Unit>>
{
    public ValueTask<Result<Unit>> Handle(ArchiveDocumentCommand command, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result.Ok());
}
```

### Custom `IMessageValidator<TMessage>`

Implement `IMessageValidator<TMessage>` to plug an arbitrary async validator into the same stage as `IValidate`. Field-level violations should be wrapped in `Error.InvalidInput` so they aggregate with other validators' output.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Mediator;

public sealed record CreateUserCommand(string Email)
    : ICommand<Result<Unit>>;

public interface IUserDirectory
{
    Task<bool> IsEmailTakenAsync(string email, CancellationToken cancellationToken);
}

public sealed class UniqueEmailValidator(IUserDirectory directory)
    : IMessageValidator<CreateUserCommand>
{
    public async ValueTask<IResult> ValidateAsync(CreateUserCommand message, CancellationToken cancellationToken)
    {
        var taken = await directory.IsEmailTakenAsync(message.Email, cancellationToken).ConfigureAwait(false);
        return taken
            ? Result.Fail(new Error.InvalidInput(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(nameof(message.Email)), "email.taken") { Detail = "Email already in use." })))
            : Result.Ok();
    }
}

public static class Composition
{
    public static void Register(IServiceCollection services) =>
        services.AddScoped<IMessageValidator<CreateUserCommand>, UniqueEmailValidator>();
}
```

### FluentValidation adapter

Add the optional `Trellis.Mediator.FluentValidation` package and call `AddTrellisFluentValidation()` to surface every registered `IValidator<TMessage>` through `IMessageValidator<TMessage>`. The adapter normalizes FluentValidation property paths (e.g., `Lines[0].Memo`) into RFC 6901 JSON Pointers (`/Lines/0/Memo`) so `Error.InvalidInput.Fields` has a consistent pointer shape regardless of which source produced each violation.

```csharp
using System.Collections.Generic;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;

public sealed record TransferLine(string TargetAccount, decimal Amount, string? Memo);

public sealed record SubmitBatchTransfersCommand(string SourceAccount, IReadOnlyList<TransferLine> Lines)
    : ICommand<Result<Unit>>;

public sealed class SubmitBatchTransfersValidator : AbstractValidator<SubmitBatchTransfersCommand>
{
    public SubmitBatchTransfersValidator()
    {
        RuleFor(x => x.SourceAccount).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.TargetAccount).NotEmpty();
            line.RuleFor(l => l.Amount).GreaterThan(0);
        });
    }
}

public static class Composition
{
    public static void Configure(WebApplicationBuilder builder)
    {
        builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
        builder.Services.AddTrellisBehaviors();
        builder.Services.AddTrellisFluentValidation();
        builder.Services.AddScoped<IValidator<SubmitBatchTransfersCommand>, SubmitBatchTransfersValidator>();
    }
}
```

See [FluentValidation Integration](integration-fluentvalidation.md#mediator-integration) for the AOT vs. assembly-scanning registration overloads.

## Domain event dispatch

`DomainEventDispatchBehavior<TMessage, TResponse>` (registered by `AddDomainEventDispatch(...)`) closes the loop between the domain layer's `DomainEvents.Add(...)` calls and the outside world. It runs as an inner pipeline behavior — after the handler returns a successful response — and fans out the events the aggregate accumulated to every registered `IDomainEventHandler<TEvent>`. Dispatch is strict single-wave snapshot dispatch: the behavior snapshots the aggregate's `UncommittedEvents()` once, publishes only that snapshot, and throws `DomainEventHandlerCascadedException` if the pending-event list at the end of dispatch differs from the entry snapshot (length + reference equality — covers handlers that raise new events, clear via `AcceptChanges`, replace, or reorder). When `Trellis.EntityFrameworkCore` is also wired up, the behavior sits **outside** `TransactionalCommandBehavior` in the pipeline, so handlers see the post-commit state and a transaction failure suppresses dispatch automatically.

### What gets dispatched, and when

| Aspect | Behavior |
|---|---|
| Message types covered | `ICommand<TResponse>` only — queries with the same response shape are skipped at the type-constraint level. |
| Response shape required | `TResponse` must implement `IResult<TAggregate>` where `TAggregate : IAggregate`. The canonical case is `Result<TAggregate>`; custom envelope types also work — see [Custom envelope response types](#custom-envelope-response-types). |
| When events fire | After the handler returns a successful response, before the response is returned up the pipeline. With `AddTrellisUnitOfWork<TContext>()` registered, that means **after** the transaction commits. |
| Failure path | If the handler returns `Result.Fail`, no events are dispatched and the aggregate retains them. |
| Per-event ordering | Events are dispatched sequentially in the order the aggregate raised them. |
| Multiple handlers per event | Each `IDomainEventHandler<TEvent>` registered for the runtime event type runs in turn (registration order). One handler's failure does not stop the next from running — see "Handler exceptions" below. |
| Cascade detection | Handler-raised events are not dispatched. If post-dispatch validation finds any new events on the aggregate, dispatch throws `DomainEventHandlerCascadedException` and does not call `AcceptChanges()`. Handlers must be side-effect-only. |
| Cancellation | `cancellationToken` is checked between each event; cancellation propagates and leaves undispatched events on the aggregate. `AcceptChanges()` runs only after clean validation, so a mid-dispatch cancellation does not clear the queue. |

> [!WARNING]
> **Post-commit throw caveat.** With `AddTrellisUnitOfWork<TContext>()` registered, the database commit is already durable before dispatch starts. If cascade detection throws, the request returns a failure-shaped response even though the write committed; a client retry may encounter "already committed" semantics. Durable at-least-once delivery requires the outbox pattern — planned for a future release, not shipped today.

### Registration

Three registration shapes; pick by composition style.

```csharp
// 1. AOT/trim-friendly: register each handler explicitly. Implies AddDomainEventDispatch().
services.AddDomainEventHandler<UserRegistered, SendWelcomeEmailHandler>();
services.AddDomainEventHandler<UserRegistered, ProvisionTenantHandler>();

// 2. Assembly scanning: discovers every concrete IDomainEventHandler<TEvent> in the listed assemblies.
//    Carries [RequiresUnreferencedCode] / [RequiresDynamicCode] — not for AOT.
services.AddDomainEventDispatch(typeof(SendWelcomeEmailHandler).Assembly);

// 3. Service-defaults builder (Trellis.ServiceDefaults). Order-safe with the other Use* slots.
builder.Services.AddTrellis(trellis => trellis
    .UseEntraActorProvider()
    .UseDomainEvents(typeof(SendWelcomeEmailHandler).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>());
```

`AddDomainEventDispatch()` is **idempotent** — calling it more than once registers the behavior and the default `IDomainEventPublisher` exactly once. Both the per-handler overload and the assembly-scan overload call it for you.

### Handler shape

```csharp
using System.Threading;
using System.Threading.Tasks;
using Trellis;

public sealed record UserRegistered(UserId UserId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class SendWelcomeEmailHandler : IDomainEventHandler<UserRegistered>
{
    private readonly IEmailSender _email;

    public SendWelcomeEmailHandler(IEmailSender email) => _email = email;

    public ValueTask HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken) =>
        _email.SendWelcomeAsync(domainEvent.UserId, cancellationToken);
}
```

Handlers are registered as **scoped** services (one instance per request). Inject side-effect services — `HttpClient` factories, message-bus producers, mailers, projection writers — directly through the constructor.

Handlers must stay side-effect-only. Do not mutate the source aggregate, mutate another aggregate, raise a new domain event, or send a nested Mediator command from inside the dispatch loop. If a side effect needs more domain mutation, issue a follow-up command from the application layer after the originating command completes, or enqueue post-commit work that runs as a separate top-level command.

### Handler exceptions

`MediatorDomainEventPublisher` (the default implementation) treats handler failures defensively:

- **Non-cancellation exceptions** thrown by a handler are logged at `Error` level and **swallowed**; the publisher continues with the next handler so a single misbehaving handler does not block other side effects of the same event.
- **`OperationCanceledException`** matching the supplied cancellation token propagates so the originating request can abort cleanly.
- **No handler resolved** for a given runtime event type is logged at `Debug` and treated as a no-op.

Cascade detection does not change handler-exception semantics. A swallowed handler failure can still be followed by clean snapshot validation and `AcceptChanges()`, so the default publisher is best-effort, not durable retry. Durable at-least-once side effects require the outbox pattern — planned for a future release, not shipped today.

Event-to-handler matching uses `domainEvent.GetType()` exactly. Handlers registered against a base class or interface of the runtime event type are **not** invoked — register one handler per concrete event type (or one type implementing multiple `IDomainEventHandler<TEvent>` interfaces, each of which is wired up separately).

### Custom envelope response types

The dispatch behavior walks `TResponse.GetInterfaces()` looking for an `IResult<TValue>` where `TValue : IAggregate`. The common case (`Result<TAggregate>`) is detected directly; less common shapes also work:

```csharp
// A non-generic envelope that exposes an aggregate-valued result. Both interfaces are required:
//   IResult<Order>          → so the dispatch behavior can extract the aggregate
//   IFailureFactory<TSelf>  → so failure-projecting behaviors (e.g. ResourceAuthorizationBehavior)
//                             can construct a failure of this envelope type
public sealed class OrderEnvelope : IResult<Order>, IFailureFactory<OrderEnvelope>
{
    private readonly Result<Order> _inner;
    public OrderEnvelope(Result<Order> inner) => _inner = inner;

    public bool IsSuccess => _inner.IsSuccess;
    public bool IsFailure => _inner.IsFailure;
    public Error? Error => _inner.Error;
    public bool TryGetValue(out Order value) => _inner.TryGetValue(out value!);
    public bool TryGetError(out Error? error) => _inner.TryGetError(out error);

    public static OrderEnvelope CreateFailure(Error error) => new(Result.Fail<Order>(error));
}
```

> [!IMPORTANT]
> If a message implements `IAuthorizeResource<TResource>`, its `TResponse` must satisfy **both** `IResult` *and* `IFailureFactory<TResponse>`. `Result<T>` does both automatically. `AddResourceAuthorization<TMessage, TResource, TResponse>()` (and the assembly-scanning overload) **fails fast at registration** with `InvalidOperationException` if either interface is missing — the security-marked command will not silently ship without resource authorization.

Other response shapes pass through the dispatch behavior untouched:

| `TResponse` | Effect |
|---|---|
| `Result<TAggregate>` where `TAggregate : IAggregate` | Events extracted and dispatched. |
| Custom type implementing `IResult<TAggregate>` (envelope) | Same as above. |
| `Result<Unit>`, `Result<string>`, `Result<TDto>` | No `IResult<TAggregate>` interface → behavior is a no-op. |
| `Result<(A, B)>` (tuple) | Same — no `IResult<TAggregate>` match. Manual dispatch remains the option. |
| Custom type with **two** distinct `IResult<TAggregate1>` / `IResult<TAggregate2>` interfaces | **Fails fast at startup** with `InvalidOperationException` — the behavior cannot disambiguate which aggregate's events to dispatch. |

When the response is `Result<Unit>` or any non-aggregate shape and you still need events to fire, dispatch them yourself (e.g. through an injected `IDomainEventPublisher`) — but prefer the canonical `Result<TAggregate>` shape so the pipeline owns the boundary.

### Dispatching events from non-aggregate response shapes (post-commit safe)

For the manual case the cleanest entry point is `IDomainEventPublisher.DispatchAggregateEventsAsync(aggregate)` — an extension method on the publisher that uses the same strict snapshot contract as `DomainEventDispatchBehavior<,>` (same cancellation contract, same cascade exception, same final `AcceptChanges()` only after clean validation) so you do not have to re-implement the edge cases.

> [!WARNING]
> **POST-COMMIT ONLY.** The helper publishes events immediately. If you call it inside a handler whose commit is run by `TransactionalCommandBehavior` (i.e., the handler is chained behind `AddTrellisUnitOfWork<TContext>()`), the events fire *before* the database transaction commits — and if the commit then fails, the handlers have already observed state that was rolled back. `AcceptChanges()` has cleared the events off the aggregate, so the failure is non-replayable.

**Wrong shape — helper called inside a transactional handler:**

```csharp
// AddTrellisUnitOfWork<AppDbContext>() is wired; TransactionalCommandBehavior owns the commit.
public async ValueTask<Result<MarkReadDto>> Handle(
    MarkInboxReadCommand cmd,
    CancellationToken cancellationToken)
{
    var inbox = await _db.Inboxes.FirstAsync(i => i.Id == cmd.Id, cancellationToken).ConfigureAwait(false);
    inbox.MarkRead(_clock.UtcNow); // raises InboxRead event

    // ⚠️ Events fire NOW, before TransactionalCommandBehavior commits. If the commit fails after
    // this handler returns, handlers have already seen InboxRead — and AcceptChanges() has erased
    // it from the aggregate so nothing on the next retry will re-raise it.
    await _publisher.DispatchAggregateEventsAsync(inbox, cancellationToken).ConfigureAwait(false);

    return Result.Ok(new MarkReadDto(inbox.Id, inbox.ReadAt!.Value));
}
```

**Right shape 1 — handler owns its commit, helper runs after it succeeds:**

```csharp
// No AddTrellisUnitOfWork<>() chain. The handler is responsible for saving and dispatching.
public async ValueTask<Result<MarkReadDto>> Handle(
    MarkInboxReadCommand cmd,
    CancellationToken cancellationToken)
{
    var inbox = await _db.Inboxes.FirstAsync(i => i.Id == cmd.Id, cancellationToken).ConfigureAwait(false);
    inbox.MarkRead(_clock.UtcNow);

    await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    // Commit is durable. Safe to publish. If cancellation or cascade detection throws,
    // AcceptChanges() will NOT have run; handlers must still be idempotent because some
    // snapshot events may already have fired.
    await _publisher.DispatchAggregateEventsAsync(inbox, cancellationToken).ConfigureAwait(false);

    return Result.Ok(new MarkReadDto(inbox.Id, inbox.ReadAt!.Value));
}
```

**Right shape 2 — `BackgroundService` tick after `SaveChangesAsync`:**

```csharp
public sealed class InboxCleanupWorker(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<InboxCleanupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDomainEventPublisher>();

            var stale = await db.Inboxes
                .Where(i => i.ReadAt == null && i.CreatedAt < clock.GetUtcNow().AddDays(-30))
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);

            foreach (var inbox in stale)
                inbox.Discard(clock.GetUtcNow()); // raises InboxDiscarded

            await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

            // Commit is durable; now drain each aggregate's events.
            foreach (var inbox in stale)
                await publisher.DispatchAggregateEventsAsync(inbox, stoppingToken).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
        }
    }
}
```

Failure / cancellation contract:

- On `OperationCanceledException`, `AcceptChanges()` is **not** called, so undispatched events remain on the aggregate. Events that already fired stay fired — handlers must be idempotent on retry.
- On `DomainEventHandlerCascadedException`, `AcceptChanges()` is **not** called and the original plus cascaded events stay on the aggregate so the caller can inspect them. Domain event handlers must not raise new events on the same aggregate they were given; if you have a cascade requirement, model it with a separate top-level command after the original command completes.
- On an exception thrown by an event handler, behavior follows the publisher's contract. The default `MediatorDomainEventPublisher` **logs and swallows** non-cancellation handler exceptions (same as `DomainEventDispatchBehavior<,>`), so the helper continues dispatching the remaining snapshot events and reaches `AcceptChanges()` after clean cascade validation. If you supply a custom `IDomainEventPublisher` that propagates handler exceptions, the helper rethrows and `AcceptChanges()` is not called. Use idempotent handlers and an outbox for durable downstream retries; the helper is not a retry buffer.
- Re-entrant calls on the same aggregate are **not supported**. Do not call `DispatchAggregateEventsAsync` from inside an `IDomainEventHandler<TEvent>` that is currently draining the same aggregate: the nested call creates a second snapshot before the outer dispatch has validated or cleared the queue. Treat domain event handlers as side-effect-only and let exactly one outer call own the drain.

> [!NOTE]
> An opt-in pipeline behavior — `TrackedAggregateDomainEventDispatchBehavior` — covers the common case where you want post-commit dispatch against aggregates the handler tracks via the EF change tracker (instead of returning them through `Result<TAggregate>`). See [Auto-dispatching from outcome-DTO commands](#auto-dispatching-from-outcome-dto-commands-opt-in-tracked-behavior) below.

### Auto-dispatching from outcome-DTO commands (opt-in tracked behavior)

`DomainEventDispatchBehavior<,>` only fires when the handler returns `IResult<TAggregate>` — it has no way to know which aggregates the handler mutated otherwise. For handlers that return an outcome DTO (`Result<MarkReadDto>`, `Result<Unit>`, `Result<(A, B)>`, ...) and stage their changes through `IUnitOfWork.CommitAsync(...)`, the alternative `TrackedAggregateDomainEventDispatchBehavior<,>` reads the aggregates the unit-of-work tracked at commit time and dispatches their events automatically. No manual `DispatchAggregateEventsAsync` call is needed.

The behavior is **opt-in** and **mutually exclusive** with `DomainEventDispatchBehavior<,>` — pick one model per host.

```csharp
// Outcome-DTO command. Handler returns Result<MarkReadDto>, not Result<Inbox>.
public sealed record MarkInboxReadCommand(Guid Id) : ICommand<Result<MarkReadDto>>;
public sealed record MarkReadDto(Guid Id, DateTimeOffset ReadAt);

public sealed class MarkInboxReadHandler(AppDbContext db, TimeProvider clock)
    : ICommandHandler<MarkInboxReadCommand, Result<MarkReadDto>>
{
    public async ValueTask<Result<MarkReadDto>> Handle(
        MarkInboxReadCommand cmd,
        CancellationToken cancellationToken)
    {
        var inbox = await db.Inboxes.FirstAsync(i => i.Id == cmd.Id, cancellationToken).ConfigureAwait(false);
        inbox.MarkRead(clock.GetUtcNow()); // raises InboxRead — staged on the aggregate

        // No SaveChangesAsync or DispatchAggregateEventsAsync needed:
        // TransactionalCommandBehavior commits, then TrackedAggregateDomainEventDispatchBehavior
        // reads ITrackedAggregateSource.CommittedAggregates and drains events from each.
        return Result.Ok(new MarkReadDto(inbox.Id, inbox.ReadAt!.Value));
    }
}
```

Registration via the `TrellisServiceBuilder`:

```csharp
services.AddTrellis(t => t
    .UseEntityFrameworkUnitOfWork<AppDbContext>()
    .UseTrackedAggregateDomainEvents(typeof(MarkInboxReadHandler).Assembly));
```

Or via DI extensions directly:

```csharp
services.AddTrackedAggregateDomainEventDispatch();
services.AddTrellisUnitOfWork<AppDbContext>();
services.AddDomainEventHandler<InboxRead, SendReadReceiptHandler>();
```

#### How it composes with the rest of the pipeline

- The tracked behavior sits at the same slot as `DomainEventDispatchBehavior<,>` — just outside `TransactionalCommandBehavior`, so dispatch runs after commit. Pipeline order is enforced by `AddTrackedAggregateDomainEventDispatch()` regardless of registration order with `AddTrellisUnitOfWork<>()`.
- After the inner pipeline returns `IsSuccess`, the behavior reads `ITrackedAggregateSource.CommittedAggregates` (the snapshot the unit-of-work captured at commit time), snapshots each aggregate's `UncommittedEvents()`, publishes only those snapshots, and then validates every snapshot aggregate before clearing anything.
- Same-aggregate and cross-aggregate cascades both throw `DomainEventHandlerCascadedException`. If dispatching aggregate A's event causes a handler to append events to aggregate B that was also in the committed snapshot, B is listed in `Offenders`; no aggregate events are cleared on throw.
- The unit-of-work snapshot is **cleared before save** and **only repopulated on success**. A failed commit (or a thrown save) leaves it empty, so a subsequent successful commit never auto-dispatches events from a previously-failed handler.
- `Result.FailAfterCommit<T>(error)` (a failure whose `IPersistOnFailure.PersistOnFailure` flag is set) commits the staged state, but the tracked behavior **skips dispatch** because the result is a failure — the worker pattern in [Persisting failure state from a worker handler](#persisting-failure-state-from-a-worker-handler) continues to work as documented.
- Re-entrant calls (a handler schedules another command via `IMediator.Send`) are blocked by the `TrackedAggregateDispatchReentrancyGuard`, so the nested invocation of the tracked behavior is **skipped** for every closed-generic behavior shape. A nested command sent from inside a domain-event handler can leave its own aggregate events stranded. Queue follow-up commands from the application layer after the originating command completes instead.

#### When to pick which model

| Handler shape | Recommendation |
|---|---|
| Returns `Result<TAggregate>` for one aggregate per command | `AddDomainEventDispatch()` (the response-shape behavior). Most precise — the response identifies the aggregate. |
| Returns an outcome DTO (`Result<DoneDto>`, `Result<Unit>`, `Result<(A, B)>`) and mutates one or more aggregates via the EF change tracker | `AddTrackedAggregateDomainEventDispatch()`. The unit-of-work tells the behavior which aggregates committed. |
| Hand-rolled `BackgroundService` tick without `TransactionalCommandBehavior` | `IDomainEventPublisher.DispatchAggregateEventsAsync(aggregate, ct)` after your own `SaveChangesAsync`. |
| Custom `IUnitOfWork` that does not implement `ITrackedAggregateSource` | Throws at first resolve. Either implement the interface on your unit-of-work or use the response-shape model. |

> [!NOTE]
> `AddTrackedAggregateDomainEventDispatch()` and `AddDomainEventDispatch()` are mutually exclusive. The tracked extension removes any prior response-shape registration, and `AddDomainEventDispatch()` short-circuits if the tracked behavior is already registered. The `TrellisServiceBuilder` slots `UseDomainEvents(...)` and `UseTrackedAggregateDomainEvents(...)` throw `InvalidOperationException` if you call both.

### Persisting failure state from a worker handler

A worker handler often needs to mark a domain record as `permanently_failed` *and* return failure to the caller (so retries stop, alerts fire, the outbox doesn't redrive). The naive shape — handler stages the state change then returns `Result.Fail<T>(error)` — loses the persisted state because `TransactionalCommandBehavior` rolls back on failure.

`Result.FailAfterCommit<T>(error)` solves this. It is still a failure (`IsFailure == true`, dispatch is skipped, callers see the error), but it carries an `IPersistOnFailure.PersistOnFailure` flag that `TransactionalCommandBehavior` reads to decide whether to commit:

```csharp
public async ValueTask<Result<Reminder>> Handle(
    SendReminderCommand cmd,
    CancellationToken cancellationToken)
{
    var reminder = await _repo.GetAsync(cmd.ReminderId, cancellationToken).ConfigureAwait(false);
    if (reminder is null)
        return Result.Fail<Reminder>(new Error.NotFound(ResourceRef.For<Reminder>(cmd.ReminderId)));

    var gatewayResult = await _gateway.SendAsync(reminder, cancellationToken).ConfigureAwait(false);
    if (gatewayResult.IsSuccess)
    {
        reminder.MarkSent(_clock.UtcNow);
        return Result.Ok(reminder);
    }

    // Fail-fast (e.g., AuthenticationRequired) → ordinary failure; nothing persists,
    // and the caller halts the batch instead of treating the item as permanently failed.
    if (gatewayResult.Error.IsFailFast())
        return Result.Fail<Reminder>(gatewayResult.Error);

    // Transient → ordinary failure: nothing persists, retry will re-enter the handler.
    if (gatewayResult.Error.IsTransient())
        return Result.Fail<Reminder>(gatewayResult.Error);

    // Permanent failure → mark the row and persist that decision alongside the failure outcome.
    // Without FailAfterCommit, MarkPermanentlyFailed's tracked changes roll back and the next
    // tick re-enters this handler against the same row.
    reminder.MarkPermanentlyFailed(gatewayResult.Error.Code, _clock.UtcNow);
    return Result.FailAfterCommit<Reminder>(gatewayResult.Error);
}
```

The `ErrorRetryExtensions` helpers in the `Trellis` namespace (`Error.IsTransient()`, `IsPermanent()`, `IsFailFast()`, `Classify()`) are the canonical worker-side retry classifier. They cover `Error.Unavailable` and `Error.RateLimited` as transient, fold in `Error.Unexpected` (so an unanticipated exception in a downstream gateway doesn't permanently park the message), separate `Error.AuthenticationRequired` as fail-fast (halt the batch — not the same as a per-item permanent failure), and handle `Error.Aggregate` with max-severity semantics. The hand-written `is Error.Unavailable or Error.RateLimited` switch is incomplete on every axis and should not be repeated.

Pipeline behavior at this point:

- `TransactionalCommandBehavior` sees `result is IPersistOnFailure { PersistOnFailure: true }` and runs `CommitAsync`. If the commit itself fails (e.g., DB unavailable), that commit error replaces the handler's gateway error in the returned response — there is no partial commit.
- `DomainEventDispatchBehavior` sees an `IsFailure` result and skips dispatch. The `MarkPermanentlyFailed` event remains on the in-memory `reminder` aggregate and is discarded when the request scope ends; it is not a durable retry buffer. If you need the permanent-failure transition to drive downstream notifications, write an outbox row inside the same handler (committed by `TransactionalCommandBehavior` alongside `MarkPermanentlyFailed`'s row updates) and dispatch from there.
- Callers (worker tick loop, outbox processor, HTTP controller) see a plain failure result and react accordingly (log + alert + don't retry).

Guidance:

- Only the **permanent** branch uses `FailAfterCommit`. Transient errors must stay as `Result.Fail<T>(...)` so the retry path re-runs against fresh state.
- `Result.FailAfterCommit<T>(error)` and `Result.Fail<T>(error)` with the same `Error` are **not** equal — equality discriminates on the per-instance `PersistOnFailure` flag.
- `IFailureFactory<Result<T>>.CreateFailure(error)` (the constructor used by upstream behaviors when projecting a failure into the response type) deliberately produces a plain `Result.Fail<T>(error)`, **not** a `FailAfterCommit`. This prevents a commit-error from looping back into another commit attempt.

## Exception safety net

`ExceptionBehavior` is the outermost behavior. It:

- Catches every unhandled exception **except** `OperationCanceledException` (which propagates so cancellation flows correctly).
- Logs the exception, then returns `TResponse.CreateFailure(new Error.Unexpected(Guid.NewGuid().ToString("N")) { Detail = "An unexpected error occurred while processing the request." })`.

The generated `"N"`-format Guid becomes `Error.ReasonCode` (and therefore `Error.Code`) today, so operators can join the failed response to the logged stack trace.

> [!WARNING]
> Don't use exceptions for expected business outcomes — return `Result<T>` failures instead and let `ExceptionBehavior` handle only true surprises.

## Telemetry

`TracingBehavior` opens an `Activity` per message under the activity source `"Trellis.Mediator"` (also exposed as the constant `TracingBehavior<,>.ActivitySourceName`). Add it to your OpenTelemetry tracing config or you will get no spans:

```csharp
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry().WithTracing(tracing =>
    tracing.AddSource("Trellis.Mediator"));
```

On a failed result, both `LoggingBehavior` and `TracingBehavior` always emit:

- `Error.Code` (operator-defined identifier, e.g., `"orders.cancel"`).
- The stable `Error` type name (e.g., `Error.Forbidden`) on the activity as `error.type`.

`LoggingBehavior` writes Debug on success and Warning on failure; `TracingBehavior` sets `ActivityStatusCode.Error` on failed results and non-cancellation exceptions, records standard exception-event tags when a handler throws, and leaves request-token cancellations at the default `Unset` status. Per-call timing is at Debug to keep production logs quiet at the default `Information` minimum; raise via `"Trellis.Mediator": "Debug"` in logging configuration to surface every dispatch.

### Telemetry redaction

The free-text `Error.Detail` string is **redacted by default** because it is frequently composed from user input or domain payloads (an order id, an email, a free-text validation message) and must not flow into log aggregators or distributed traces without explicit opt-in.

To opt in (typically development only, or environments verified PII-free):

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Mediator;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors(options => options.IncludeErrorDetail = true);
```

The `error.code` tag and the `Error.Code` value are operator-defined identifiers and are always emitted regardless of this setting.

## Composition

Resource authorization, FluentValidation, and the EF Core unit-of-work behavior compose into one pipeline. Register Trellis behaviors first, then any extension validators, then the actor provider, and finally `AddTrellisUnitOfWork<TContext>()` so the transactional behavior lands innermost (closest to the handler) and commit failures stay visible to outer logging/tracing.

```csharp
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trellis.Asp.Authorization;
using Trellis.EntityFrameworkCore;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddTrellisFluentValidation();
builder.Services.AddResourceAuthorization(typeof(Program).Assembly);

if (builder.Environment.IsDevelopment())
    builder.Services.AddDevelopmentActorProvider();
else
    builder.Services.AddEntraActorProvider();

builder.Services.AddTrellisUnitOfWork<AppDbContext>();

var app = builder.Build();
app.Run();
```

## Practical guidance

- **Use the `Scoped` Mediator lifetime.** All Trellis behaviors depend on per-request services; `Singleton` (the Mediator default) fails the root-scope check on first request.
- **`IAuthorize` for coarse gates, `IAuthorizeResource<T>` for fine rules.** Static permissions (`documents:edit`) belong on `IAuthorize`; ownership / tenancy / state rules belong on `IAuthorizeResource<T>`.
- **Prefer shared resource loaders.** Register one `SharedResourceLoaderById<TResource, TId>` per resource and let messages implement `IIdentifyResource<,>` — avoids one loader class per command.
- **Don't forget `AddResourceAuthorization(...)`.** Implementing `IAuthorizeResource<T>` is not enough; the behavior must be registered or it never runs.
- **Keep `IValidate.Validate()` synchronous and cheap.** It runs on every request. Push I/O-bound checks into an `IMessageValidator<TMessage>` (which is async) or into the handler.
- **Return `Result<Unit>` from commands** (not bare `Result`), and never throw for expected business outcomes — `ExceptionBehavior` is for surprises only.
- **Leave `IncludeErrorDetail = false` in production.** `Error.Detail` is free text and may contain PII.
- **Add the `"Trellis.Mediator"` activity source** to your OpenTelemetry config or you will not see mediator spans.

## Cross-references

- API surface: [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md)
- `Result<T>`, `Maybe<T>`, `Error` semantics: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Authorization primitives (`Actor`, `IAuthorize`, `IAuthorizeResource`, loaders): [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md)
- FluentValidation integration article: [integration-fluentvalidation.md](integration-fluentvalidation.md)
- ASP.NET integration (`IActorProvider` wiring): [integration-asp-authorization.md](integration-asp-authorization.md)
- Observability article: [integration-observability.md](integration-observability.md)
