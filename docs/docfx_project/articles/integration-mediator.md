# Mediator Pipeline

**Level:** Intermediate 📘 | **Time:** 20-25 min | **Prerequisites:** [Basics](basics.md), [ASP.NET Core Authorization](integration-asp-authorization.md)

Handlers should focus on business work. Authorization, validation, tracing, and exception safety should happen around them, not inside them.

That is what `Trellis.Mediator` gives you: result-aware pipeline behaviors for the [Mediator](https://github.com/martinothamar/Mediator) library.

## Why use it?

Without a pipeline, handlers tend to accumulate cross-cutting concerns:

- permission checks
- resource ownership checks
- input validation
- trace/log boilerplate
- try/catch safety nets

With `Trellis.Mediator`, those concerns become opt-in behaviors.

```mermaid
flowchart TD
    A[Message] --> B[ExceptionBehavior]
    B --> C[TracingBehavior]
    C --> D[LoggingBehavior]
    D --> E[AuthorizationBehavior]
    E --> F{Resource auth registered?}
    F -->|yes| G[ResourceAuthorizationBehavior]
    F -->|no| H[ValidationBehavior]
    G --> H[ValidationBehavior]
    H --> I[Handler]
```

## Installation

```bash
dotnet add package Trellis.Mediator
```

## Quick start

Start by registering Mediator and the core Trellis behaviors.

```csharp
using Trellis.Mediator;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
```

> [!IMPORTANT]
> Pass `opts => opts.ServiceLifetime = ServiceLifetime.Scoped`. The Trellis pipeline behaviors are registered as scoped (they depend on per-request services such as `IActorProvider`, `IUnitOfWork`, and `IMessageValidator<>` adapters). The Mediator default lifetime is **Singleton**, which fails ASP.NET's root-scope validation as soon as the first behavior tries to resolve a scoped dependency. `Scoped` is the right lifetime for any host that has a request scope (Web API, Worker with scoped processing, etc.).

That registration adds these behaviors, in this order:

| Behavior | Runs for | What it does |
| --- | --- | --- |
| `ExceptionBehavior` | all messages | Converts unhandled exceptions into `new Error.InternalServerError("fault-id") { Detail = ... }` failures |
| `TracingBehavior` | all messages | Creates an OpenTelemetry activity. Tags failures with stable `error.code` / `error.type`; redacts `Error.Detail` by default (see [Redacting `Error.Detail`](#redacting-errordetail-in-logs-and-traces)) |
| `LoggingBehavior` | all messages | Logs execution and failures. Emits `Error.Code` for failed responses; redacts `Error.Detail` by default (see [Redacting `Error.Detail`](#redacting-errordetail-in-logs-and-traces)) |
| `AuthorizationBehavior` | messages implementing `IAuthorize` | Enforces required permissions |
| `ValidationBehavior` | **all messages** | Runs `IValidate.Validate()` (when implemented) AND every registered `IMessageValidator<TMessage>` (e.g., the FluentValidation adapter); aggregates `Error.UnprocessableContent` failures from every source into one response |

> [!NOTE]
> `ResourceAuthorizationBehavior` is **not** included by `AddTrellisBehaviors()`. It is only added when you call `AddResourceAuthorization(...)`.

## Permission-based authorization

Use `IAuthorize` when a command or query always needs the same permission set.

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;

public sealed record PublishDocumentCommand(Guid DocumentId)
    : ICommand<Result>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["Documents.Publish"];
}
```

When this command goes through the pipeline:

1. `AuthorizationBehavior` asks `IActorProvider` for the current actor
2. it calls `actor.HasAllPermissions(RequiredPermissions)`
3. if the actor is missing any permission, the pipeline returns `new Error.Forbidden("authorization.insufficient.permissions") { Detail = "Insufficient permissions." }`

## Resource-based authorization

Static permissions are not enough when the answer depends on the resource itself. For example: “the caller must have `Documents.Edit`, and they must own this document.”

### Step 1: put the rule on the message

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

public sealed record Document(Guid Id, string OwnerId, string Title);

public sealed record RenameDocumentCommand(Guid DocumentId, string Title)
    : ICommand<Result<Document>>,
      IAuthorize,
      IAuthorizeResource<Document>,
      IValidate
{
    public IReadOnlyList<string> RequiredPermissions => ["Documents.Edit"];

    public IResult Authorize(Actor actor, Document resource) =>
        actor.IsOwner(resource.OwnerId)
            ? Result.Ok()
            : Result.Fail(new Error.Forbidden("policy.id") { Detail = "Only the owner can rename this document." });

    public IResult Validate() =>
        string.IsNullOrWhiteSpace(Title)
            ? Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(Title)), "validation.error") { Detail = "Title is required." })))
            : Result.Ok();
}
```

### Step 2: add a resource loader

`ResourceLoaderById<TMessage, TResource, TId>` handles the common “message contains an id, repository loads by id” case.

```csharp
using Trellis;
using Trellis.Authorization;

public interface IDocumentRepository
{
    Task<Result<Document>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<Document>> RenameAsync(
        Document document,
        string title,
        CancellationToken cancellationToken = default);
}

public sealed class RenameDocumentResourceLoader(IDocumentRepository repository)
    : ResourceLoaderById<RenameDocumentCommand, Document, Guid>
{
    protected override Guid GetId(RenameDocumentCommand message) => message.DocumentId;

    protected override Task<Result<Document>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        repository.GetByIdAsync(id, cancellationToken);
}
```

### Step 3: register resource authorization

```csharp
using Trellis.Mediator;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddResourceAuthorization(
    typeof(RenameDocumentCommand).Assembly,
    typeof(RenameDocumentResourceLoader).Assembly);
```

Now the order for `RenameDocumentCommand` becomes:

1. permission check
2. resource load + `Authorize(actor, resource)`
3. validation
4. handler

> [!TIP]
> For AOT or trimming-sensitive apps, use explicit registration:
>
> ```csharp
> builder.Services.AddResourceAuthorization<RenameDocumentCommand, Document, Result<Document>>();
> builder.Services.AddScoped<IResourceLoader<RenameDocumentCommand, Document>, RenameDocumentResourceLoader>();
> ```

## Writing handlers stays simple

Once the pipeline owns authorization and validation, the handler can stay focused.

```csharp
using Mediator;
using Trellis;

public sealed class RenameDocumentHandler(IDocumentRepository repository)
    : ICommandHandler<RenameDocumentCommand, Result<Document>>
{
    public async ValueTask<Result<Document>> Handle(
        RenameDocumentCommand command,
        CancellationToken cancellationToken)
    {
        var documentResult = await repository.GetByIdAsync(command.DocumentId, cancellationToken);
        if (!documentResult.TryGetValue(out var document))
            return Result.Fail<Document>(documentResult.Error!);

        return await repository.RenameAsync(document, command.Title, cancellationToken);
    }
}
```

## Validation behavior details

Why call this out? Because `ValidationBehavior` is the single, unified validation stage. It runs for **every** message and pulls failures from two independent sources:

1. **`IValidate.Validate()`** on the message itself — for cross-cutting business invariants that are awkward to express as property rules (e.g., "the batch must be non-empty" or "no line may target the source account").
2. **Every `IMessageValidator<TMessage>` registered in DI** — the extension seam used by `Trellis.FluentValidation` (and any other validator package) to plug into the same stage without occupying its own pipeline slot.

### Aggregation rules

- All `Error.UnprocessableContent` failures from both sources are merged into a single response whose `Fields` array contains every reported violation. The pipeline never returns "the first failure"; the caller always gets the full list in one round trip.
- Any **non-UPC** failure (`Error.Conflict`, `Error.Forbidden`, …) returned by any validator short-circuits the stage immediately and is propagated as-is. No further validators are consulted. This lets `IValidate` express "this is not a field problem, this is a domain rule violation":

```csharp
using Mediator;
using Trellis;
using Trellis.Mediator;

public sealed record ArchiveDocumentCommand(Guid DocumentId, bool IsArchived)
    : ICommand<Result>, IValidate
{
    public IResult Validate() =>
        IsArchived
            ? Result.Ok()
            : Result.Fail(new Error.Conflict(null, "domain.violation") { Detail = "Only archived documents can be processed." });
}
```

### Plugging in FluentValidation

Add the optional `Trellis.FluentValidation` package and call `AddTrellisFluentValidation()` to make every `IValidator<TMessage>` registered for the message participate in the same validation stage:

```csharp
using FluentValidation;
using Trellis.FluentValidation;
using Trellis.Mediator;

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddTrellisFluentValidation();
builder.Services.AddScoped<IValidator<SubmitBatchTransfersCommand>, SubmitBatchTransfersValidator>();
```

`AddTrellisFluentValidation()` registers an open-generic `FluentValidationMessageValidatorAdapter<>` as `IMessageValidator<>`. The adapter normalizes FluentValidation property names (e.g., `Metadata.Reference`, `Lines[0].Memo`) into RFC 6901 JSON Pointers (`/Metadata/Reference`, `/Lines/0/Memo`) so the resulting `Error.UnprocessableContent.Fields` has consistent pointer shapes regardless of which validation source produced each violation.

See [FluentValidation Integration](integration-fluentvalidation.md#mediator-integration-addtrellisfluentvalidation) for the full Mediator-pipeline section, including the AOT vs. assembly-scanning overloads.

## Exception behavior details

`ExceptionBehavior` is a safety net, not a design goal.

- unexpected exception → logged, then returned as `new Error.InternalServerError("fault-id") { Detail = ... }`
- `OperationCanceledException` → **not** swallowed; it flows through normally

> [!WARNING]
> Do not use exceptions for expected business outcomes. Return `Result<T>` failures instead and let `ExceptionBehavior` handle only true surprises.

## Full application setup

```csharp
using Trellis.Asp.Authorization;
using Trellis.Mediator;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
builder.Services.AddResourceAuthorization(typeof(Program).Assembly);

if (builder.Environment.IsDevelopment())
    builder.Services.AddDevelopmentActorProvider();
else
    builder.Services.AddEntraActorProvider();
```

## Practical guidance

### Keep permission checks coarse, resource checks precise

Use `IAuthorize` for broad gates like `Documents.Edit`. Use `IAuthorizeResource<T>` for ownership, tenancy, or state-specific rules.

### Register resource authorization intentionally

If you forget `AddResourceAuthorization(...)`, the resource authorization behavior will not run.

### Keep `Validate()` fast

`IValidate.Validate()` is synchronous. Use it for cheap checks. Put I/O-heavy validation in handlers or separate validators.

### Trace source name

`TracingBehavior` uses the activity source name:

- `Trellis.Mediator`

That is the source to add to your OpenTelemetry configuration when you want mediator spans.

### Redacting `Error.Detail` in logs and traces

Both `LoggingBehavior` and `TracingBehavior` emit the **stable** error identity for failed responses:

- `LoggingBehavior` includes `error.Code` in the Warning-level "Handled" message
- `TracingBehavior` tags the activity with `error.code` and `error.type` and sets `ActivityStatusCode.Error`

The free-text `Error.Detail` string is **redacted by default** — it is frequently composed from user input or domain payloads (an order id, an email address, a free-text validation message) and must not flow into log aggregators or distributed traces without explicit opt-in.

To opt in (e.g. in development, or in environments where you have verified no PII can flow through any `Error.Detail`):

```csharp
builder.Services.AddTrellisBehaviors(opts => opts.IncludeErrorDetail = true);
```

Or mutate the singleton directly after registration:

```csharp
builder.Services.AddTrellisBehaviors();
builder.Services.AddSingleton(new TrellisMediatorTelemetryOptions { IncludeErrorDetail = true });
```

The `error.code` tag and the `Error.Code` value are operator-defined identifiers and are always emitted regardless of this setting.

## Next steps

- [Observability & Monitoring](integration-observability.md)
- [Testing](integration-testing.md)
- [trellis-api-mediator.md](../api_reference/trellis-api-mediator.md)
