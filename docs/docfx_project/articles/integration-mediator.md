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
        ValueTask.FromResult(Result.Ok(Unit.Value));
}
```

> [!IMPORTANT]
> Pass `opts => opts.ServiceLifetime = ServiceLifetime.Scoped`. The Trellis behaviors depend on per-request services (`IActorProvider`, `IUnitOfWork`, `IMessageValidator<>` adapters). Mediator's default lifetime is `Singleton`, which fails ASP.NET's root-scope validation as soon as a behavior tries to resolve a scoped dependency.

## Pipeline order

`AddTrellisBehaviors()` registers the five always-on behaviors in this fixed order (outermost → innermost). The opt-in entries in rows 5 and 7 slot in only when their registration helpers are called.

| # | Behavior | Runs for | What it does |
|---|---|---|---|
| 1 | `ExceptionBehavior` | all messages | Catches everything except `OperationCanceledException`; returns `Error.InternalServerError`. |
| 2 | `TracingBehavior` | all messages | Opens an `Activity` under `"Trellis.Mediator"`; tags `error.code` / `error.type` on failure. |
| 3 | `LoggingBehavior` | all messages | Structured start/end with elapsed ms; emits `Error.Code` on failure. |
| 4 | `AuthorizationBehavior` | `IAuthorize` messages | Resolves the actor and checks `RequiredPermissions`. |
| 5 | `ResourceAuthorizationBehavior` *(opt-in)* | `IAuthorizeResource<T>` messages | Loads the resource and calls `Authorize(actor, resource)`. Inserted by `AddResourceAuthorization(...)` immediately before `ValidationBehavior`. |
| 6 | `ValidationBehavior` | all messages | Runs `IValidate.Validate()` and every `IMessageValidator<TMessage>`; aggregates `Error.UnprocessableContent`. |
| 7 | `TransactionalCommandBehavior` *(opt-in, EFCore)* | `ICommand<TResponse>` | `IUnitOfWork.CommitAsync` on success; wraps each command in `using var scope = unitOfWork.BeginScope();` so nested commands defer commit to the outermost scope. Register **after** `AddTrellisBehaviors()` so it lands innermost. See [Nested commands and scope-aware commit](integration-ef.md#nested-commands-and-scope-aware-commit). |

The first five live in `ServiceCollectionExtensions.PipelineBehaviors` for the AOT-friendly source-generator path; assign that list to `MediatorOptions.PipelineBehaviors` when configuring `AddMediator`.

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

## Validation

`ValidationBehavior` runs for every message and pulls violations from two sources.

| Source | Use it for |
|---|---|
| `IValidate.Validate()` on the message | Cross-field invariants and domain rules awkward to express as property checks. |
| `IEnumerable<IMessageValidator<TMessage>>` from DI | Property-level validation, FluentValidation adapter, or any custom validator package. |

**Aggregation rules**

- All `Error.UnprocessableContent` failures from both sources are merged into a single `Error.UnprocessableContent` whose `Fields` and `Rules` collect every reported violation. The caller never gets "the first failure" — they get the full list in one round trip.
- An `Error.UnprocessableContent` with empty `Fields` and empty `Rules` still short-circuits the handler.
- A non-`Error.UnprocessableContent` failure (e.g., `Error.Conflict`, `Error.Forbidden`) returned by any source short-circuits the stage immediately and is propagated as-is.

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
        ValueTask.FromResult(Result.Ok(Unit.Value));
}
```

### Custom `IMessageValidator<TMessage>`

Implement `IMessageValidator<TMessage>` to plug an arbitrary async validator into the same stage as `IValidate`. Field-level violations should be wrapped in `Error.UnprocessableContent` so they aggregate with other validators' output.

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
            ? Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(
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

Add the optional `Trellis.FluentValidation` package and call `AddTrellisFluentValidation()` to surface every registered `IValidator<TMessage>` through `IMessageValidator<TMessage>`. The adapter normalizes FluentValidation property paths (e.g., `Lines[0].Memo`) into RFC 6901 JSON Pointers (`/Lines/0/Memo`) so `Error.UnprocessableContent.Fields` has a consistent pointer shape regardless of which source produced each violation.

```csharp
using System.Collections.Generic;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.FluentValidation;
using Trellis.Mediator;

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

## Exception safety net

`ExceptionBehavior` is the outermost behavior. It:

- Catches every unhandled exception **except** `OperationCanceledException` (which propagates so cancellation flows correctly).
- Logs the exception, then returns `TResponse.CreateFailure(new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = "An unexpected error occurred while processing the request." })`.

The generated `"N"`-format Guid is the fault correlation id surfaced in `Error.Code` so operators can join the failed response to the logged stack trace.

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

`LoggingBehavior` writes Information on success and Warning on failure; `TracingBehavior` sets `ActivityStatusCode.Error` on the failure path.

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
using Trellis.FluentValidation;
using Trellis.Mediator;

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
