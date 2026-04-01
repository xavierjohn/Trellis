# Mediator Pipeline

**Level:** Intermediate 📚 | **Time:** 25-35 min | **Prerequisites:** [Basics](basics.md), [ASP.NET Core Authorization](integration-asp-authorization.md)

Build CQRS command/query pipelines with automatic authorization, validation, tracing, and error handling using the **Trellis.Mediator** package. This package provides pipeline behaviors that plug into the [Mediator](https://github.com/martinothamar/Mediator) library.

> **Note:** Trellis.Mediator depends on `Trellis.Authorization` for the `Actor`, `IAuthorize`, `IAuthorizeResource<T>`, and `IResourceLoader<,>` interfaces. You do not need to install `Trellis.Authorization` separately.

## Table of Contents

- [Installation](#installation)
- [Pipeline Overview](#pipeline-overview)
- [Registration](#registration)
- [Permission-Based Authorization](#permission-based-authorization)
- [Resource-Based Authorization](#resource-based-authorization)
- [Self-Validation](#self-validation)
- [Complete Example](#complete-example)
- [Best Practices](#best-practices)

## Installation

```bash
dotnet add package Trellis.Mediator
```

## Pipeline Overview

Every command or query dispatched through the mediator flows through a chain of behaviors before reaching the handler. Each behavior can short-circuit the pipeline by returning a failure `Result<T>`.

```
Request
  │
  ▼
ExceptionBehavior        ← catches unhandled exceptions → Error.Unexpected
  │
  ▼
TracingBehavior           ← creates OpenTelemetry Activity span
  │
  ▼
LoggingBehavior           ← structured logging with duration
  │
  ▼
AuthorizationBehavior     ← checks IAuthorize.RequiredPermissions → Error.Forbidden
  │
  ▼
ResourceAuthorizationBehavior  ← loads resource, calls IAuthorizeResource<T>.Authorize
  │
  ▼
ValidationBehavior        ← calls IValidate.Validate() → ValidationError
  │
  ▼
Handler                   ← your business logic
```

| Behavior | Activates When Message Implements | Short-Circuits With |
|----------|----------------------------------|---------------------|
| `ExceptionBehavior` | *(all messages)* | `Error.Unexpected` |
| `TracingBehavior` | *(all messages)* | *(never)* |
| `LoggingBehavior` | *(all messages)* | *(never)* |
| `AuthorizationBehavior` | `IAuthorize` | `Error.Forbidden` |
| `ResourceAuthorizationBehavior` | `IAuthorizeResource<TResource>` | `Error.Forbidden` or `Error.NotFound` |
| `ValidationBehavior` | `IValidate` | `ValidationError` |

Behaviors are **constraint-based** — if your command does not implement `IAuthorize`, the `AuthorizationBehavior` is skipped entirely. You opt in per command/query.

## Registration

### Basic Setup

```csharp
using Trellis.Mediator;

var builder = WebApplication.CreateBuilder(args);

// Register the Mediator library
builder.Services.AddMediator();

// Register all Trellis pipeline behaviors
builder.Services.AddTrellisBehaviors();
```

### Adding Resource Authorization

Resource authorization requires an additional registration step because each command has its own `TResource` type. Use assembly scanning or explicit registration:

```csharp
// Recommended: assembly scanning — discovers IResourceLoader<,> implementations
// and registers ResourceAuthorizationBehavior for each IAuthorizeResource<T> command.
// Pass all assemblies containing commands and resource loaders.
services.AddResourceAuthorization(
    typeof(CancelOrderCommand).Assembly,
    typeof(CancelOrderResourceLoader).Assembly);
```

```csharp
// AOT-compatible: explicit per-command registration
services.AddResourceAuthorization<CancelOrderCommand, Order, Result<Order>>();
services.AddResourceLoaders(typeof(CancelOrderResourceLoader).Assembly);
```

### Complete Program.cs

```csharp
using Trellis.Mediator;
using Trellis.Asp.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator();
builder.Services.AddTrellisBehaviors();

// Pass all assemblies containing commands and resource loaders
builder.Services.AddResourceAuthorization(typeof(Program).Assembly);

// Actor provider for authorization behaviors
if (builder.Environment.IsDevelopment())
    builder.Services.AddDevelopmentActorProvider();
else
    builder.Services.AddEntraActorProvider();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.Run();
```

## Permission-Based Authorization

Implement `IAuthorize` on a command or query to enforce static permission checks. The `AuthorizationBehavior` resolves the current `Actor` via `IActorProvider` and verifies that the actor has **all** required permissions.

### Defining a Protected Command

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;

public sealed record PublishDocumentCommand(DocumentId DocumentId)
    : ICommand<Result<Document>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["Documents.Publish"];
}
```

### How It Works

1. `AuthorizationBehavior` calls `IActorProvider.GetCurrentActorAsync()`
2. Checks `actor.HasAllPermissions(message.RequiredPermissions)`
3. If the actor lacks any permission → returns `Error.Forbidden("Insufficient permissions.")`
4. If authorized → proceeds to the next behavior in the pipeline

### Multiple Permissions

All listed permissions are required (AND logic):

```csharp
public sealed record TransferFundsCommand(AccountId From, AccountId To, Money Amount)
    : ICommand<Result<Transfer>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions =>
        ["Accounts.Read", "Transfers.Create"];
}
```

## Resource-Based Authorization

When authorization depends on the resource itself (e.g., "only the owner can cancel an order"), implement `IAuthorizeResource<TResource>`. This pattern separates concerns into three parts:

1. **Command** — declares the authorization check via `IAuthorizeResource<TResource>`
2. **Resource Loader** — loads the resource from the database via `IResourceLoader<TMessage, TResource>`
3. **Pipeline** — the `ResourceAuthorizationBehavior` orchestrates loading and authorization

### Step 1: Define the Command

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;

public sealed record CancelOrderCommand(OrderId OrderId)
    : ICommand<Result<Order>>, IAuthorizeResource<Order>
{
    public IResult Authorize(Actor actor, Order order)
    {
        if (!actor.IsOwner(order.CreatedByActorId))
            return Result.Failure(Error.Forbidden("Only the order owner can cancel."));

        return Result.Success();
    }
}
```

### Step 2: Implement the Resource Loader

Use `ResourceLoaderById<TMessage, TResource, TId>` for the common "extract ID, load by ID" pattern:

```csharp
using Trellis;
using Trellis.Authorization;

public sealed class CancelOrderResourceLoader(IOrderRepository repository)
    : ResourceLoaderById<CancelOrderCommand, Order, OrderId>
{
    protected override OrderId GetId(CancelOrderCommand message) => message.OrderId;

    protected override Task<Result<Order>> GetByIdAsync(
        OrderId id, CancellationToken cancellationToken)
        => repository.GetByIdAsync(id, cancellationToken);
}
```

Or implement `IResourceLoader<TMessage, TResource>` directly for custom loading logic:

```csharp
public sealed class EditDocumentResourceLoader(IDocumentRepository repository)
    : IResourceLoader<EditDocumentCommand, Document>
{
    public Task<Result<Document>> LoadAsync(
        EditDocumentCommand message, CancellationToken cancellationToken)
        => repository.GetByIdAsync(message.DocumentId, cancellationToken);
}
```

### Step 3: Register

```csharp
// Assembly scanning registers both the behavior and the loader
services.AddResourceAuthorization(typeof(CancelOrderCommand).Assembly);
```

### How It Works

1. `ResourceAuthorizationBehavior` resolves `IResourceLoader<TMessage, TResource>` from DI
2. Calls `loader.LoadAsync(message, ct)` — if the resource is not found, returns the loader's error (typically `NotFoundError`)
3. Calls `IActorProvider.GetCurrentActorAsync()` to get the current actor
4. Calls `message.Authorize(actor, resource)` — your authorization logic
5. If authorized → proceeds to the handler

### Combining Both Authorization Styles

A command can implement both `IAuthorize` and `IAuthorizeResource<T>`. Permission checks run first (in `AuthorizationBehavior`), then resource-based checks run (in `ResourceAuthorizationBehavior`):

```csharp
public sealed record CancelOrderCommand(OrderId OrderId)
    : ICommand<Result<Order>>, IAuthorize, IAuthorizeResource<Order>
{
    // Static permission check (runs first)
    public IReadOnlyList<string> RequiredPermissions => ["Orders.Cancel"];

    // Resource-based check (runs second, only if permission check passed)
    public IResult Authorize(Actor actor, Order order)
    {
        if (!actor.IsOwner(order.CreatedByActorId))
            return Result.Failure(Error.Forbidden("Only the order owner can cancel."));

        return Result.Success();
    }
}
```

## Self-Validation

Implement `IValidate` on a command or query to add validation that runs after authorization but before the handler. The `ValidationBehavior` calls `Validate()` and short-circuits with the returned error on failure.

```csharp
using Mediator;
using Trellis;
using Trellis.Mediator;

public sealed record CreateDocumentCommand(DocumentName Name, DocumentContent Content)
    : ICommand<Result<Document>>, IValidate
{
    public IResult Validate()
    {
        if (Content.Length > 1_000_000)
            return Result.Failure(Error.Validation("Document content exceeds maximum size."));

        return Result.Success();
    }
}
```

> **Tip:** For complex validation logic, consider using [FluentValidation](integration-fluentvalidation.md) inside the `Validate()` method, or implementing validation within the aggregate's factory method.

## Complete Example

Here is a full example with a command that uses permission-based authorization, resource-based authorization, and self-validation:

### Domain

```csharp
public sealed class Document : Aggregate<DocumentId>
{
    public DocumentName Name { get; private set; }
    public DocumentContent Content { get; private set; }
    public string CreatedByActorId { get; private set; }

    public static Result<Document> TryCreate(
        DocumentName name, DocumentContent content, string actorId)
    {
        var document = new Document
        {
            Id = DocumentId.NewUniqueV4(),
            Name = name,
            Content = content,
            CreatedByActorId = actorId
        };
        return Result.Success(document);
    }

    public Result<Document> Edit(DocumentName name, DocumentContent content)
    {
        Name = name;
        Content = content;
        return Result.Success(this);
    }
}
```

### Command

```csharp
public sealed record EditDocumentCommand(
    DocumentId DocumentId,
    DocumentName Name,
    DocumentContent Content)
    : ICommand<Result<Document>>,
      IAuthorize,
      IAuthorizeResource<Document>,
      IValidate
{
    public IReadOnlyList<string> RequiredPermissions => ["Documents.Edit"];

    public IResult Authorize(Actor actor, Document document)
    {
        if (!actor.IsOwner(document.CreatedByActorId))
            return Result.Failure(Error.Forbidden("Only the document owner can edit."));

        return Result.Success();
    }

    public IResult Validate()
    {
        if (Content.Length > 1_000_000)
            return Result.Failure(Error.Validation("Content exceeds maximum size."));

        return Result.Success();
    }
}
```

### Resource Loader

```csharp
public sealed class EditDocumentResourceLoader(IDocumentRepository repository)
    : ResourceLoaderById<EditDocumentCommand, Document, DocumentId>
{
    protected override DocumentId GetId(EditDocumentCommand message) => message.DocumentId;

    protected override Task<Result<Document>> GetByIdAsync(
        DocumentId id, CancellationToken cancellationToken)
        => repository.GetByIdAsync(id, cancellationToken);
}
```

### Handler

```csharp
public sealed class EditDocumentHandler(IDocumentRepository repository)
    : ICommandHandler<EditDocumentCommand, Result<Document>>
{
    public async ValueTask<Result<Document>> Handle(
        EditDocumentCommand command, CancellationToken cancellationToken)
        => await repository.GetByIdAsync(command.DocumentId, cancellationToken)
            .BindAsync(doc => Task.FromResult(doc.Edit(command.Name, command.Content)))
            .TapAsync(doc => repository.SaveAsync(doc, cancellationToken));
}
```

### Pipeline Execution Order

When `mediator.Send(new EditDocumentCommand(...))` is called:

1. **ExceptionBehavior** — wraps everything in try/catch
2. **TracingBehavior** — starts OpenTelemetry span `"EditDocumentCommand"`
3. **LoggingBehavior** — logs `"Handling EditDocumentCommand"`
4. **AuthorizationBehavior** — checks actor has `"Documents.Edit"` permission
5. **ResourceAuthorizationBehavior** — loads document, calls `Authorize(actor, document)`
6. **ValidationBehavior** — calls `Validate()` to check content size
7. **EditDocumentHandler** — executes the business logic

If any behavior short-circuits, subsequent behaviors and the handler are skipped.

## Best Practices

1. **Register behaviors once** — Call `AddTrellisBehaviors()` once in `Program.cs`. The pipeline order is fixed and consistent.

2. **Use assembly scanning** — `AddResourceAuthorization(assembly)` discovers both `IAuthorizeResource<T>` commands and `IResourceLoader<,>` implementations. Prefer this over explicit registration unless targeting AOT.

3. **Keep authorization logic in the command** — The `Authorize(Actor, TResource)` method on the command is the single source of truth for "who can do this to this resource."

4. **Use `ResourceLoaderById` for simple lookups** — Most resource loaders follow the "extract ID, load by ID" pattern. The base class eliminates boilerplate.

5. **Combine `IAuthorize` + `IAuthorizeResource<T>`** — Use `IAuthorize` for coarse permission gates and `IAuthorizeResource<T>` for fine-grained ownership checks. They compose naturally.

6. **Keep `Validate()` lightweight** — `IValidate.Validate()` runs synchronously. Use it for quick structural checks. For async validation (e.g., uniqueness checks), use [FluentValidation](integration-fluentvalidation.md) or validate in the handler.

7. **Return domain errors, not exceptions** — Behaviors convert unhandled exceptions to `Error.Unexpected`. Design your handlers to return `Result<T>` failures for expected error conditions.

## Next Steps

- [ASP.NET Core Authorization (Entra ID)](integration-asp-authorization.md) — Configure `IActorProvider` for production
- [Testing](integration-testing.md) — Test authorization with `TestActorProvider` and `FakeRepository`
- [FluentValidation](integration-fluentvalidation.md) — Advanced validation patterns
- [Observability & Monitoring](integration-observability.md) — OpenTelemetry tracing for the mediator pipeline
