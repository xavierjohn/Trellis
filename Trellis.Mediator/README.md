# Trellis.Mediator

Result-aware pipeline behaviors for [martinothamar/Mediator](https://github.com/martinothamar/Mediator). This package bridges the Trellis `Result<T>` type system with Mediator's pipeline model, providing reusable, cross-cutting behaviors that understand success/failure and short-circuit correctly.

Authorization types (`Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<TResource>`) live in the separate [Trellis.Authorization](../Trellis.Authorization/README.md) package, which has no Mediator dependency. This means you can use the same authorization primitives in non-CQRS scenarios.

## What It Is

A thin integration layer — **not** a mediator implementation. The actual mediator is martinothamar/Mediator (source-generated, `ValueTask`-based, AOT-friendly). This package provides:

- **ValidationBehavior** — short-circuits on validation failure via `IValidate`
- **AuthorizationBehavior** — checks static permissions via `IAuthorize`
- **ResourceAuthorizationBehavior<TMessage, TResource, TResponse>** — loads resource via `IResourceLoader`, checks ownership via `IAuthorizeResource<TResource>`. Auto-discovered via `AddResourceAuthorization(Assembly)`.
- **LoggingBehavior** — structured logging with duration and Result outcome
- **TracingBehavior** — OpenTelemetry activity with Result status
- **ExceptionBehavior** — catches unhandled exceptions as `Error.Unexpected` failures

## Installation

```
dotnet add package Trellis.Mediator
```

## Quick Start

### 1. Register behaviors

```csharp
using Trellis.Mediator;

services.AddMediator(options =>
{
    options.Assemblies = [typeof(MyCommand).Assembly];
    options.PipelineBehaviors = [.. ServiceCollectionExtensions.PipelineBehaviors];
});

// Auto-discover IAuthorizeResource<T> commands and IResourceLoader<,> implementations
services.AddResourceAuthorization(typeof(MyCommand).Assembly);
```

### 2. Define a command with validation

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

public sealed record CreateOrderCommand(string CustomerId, int Quantity)
    : ICommand<Result<Order>>, IValidate
{
    public IResult Validate() =>
        string.IsNullOrWhiteSpace(CustomerId)
            ? Result.Failure<Order>(Error.Validation("Required", "CustomerId"))
            : Quantity <= 0
                ? Result.Failure<Order>(Error.Validation("Must be positive", "Quantity"))
                : Result.Success();
}
```

### 3. Add authorization

```csharp
using Trellis.Authorization;

// Static permission check
public sealed record DeleteOrderCommand(OrderId OrderId)
    : ICommand<Result<Unit>>, IAuthorize
{
    public IReadOnlyList<string> RequiredPermissions => ["Orders.Delete"];
}

// Resource-based check with loaded resource
public sealed record CancelOrderCommand(OrderId OrderId)
    : ICommand<Result<Order>>, IAuthorizeResource<Order>
{
    public IResult Authorize(Actor actor, Order order) =>
        actor.Id == order.OwnerId || actor.HasPermission("Orders.CancelAny")
            ? Result.Success()
            : Result.Failure(Error.Forbidden("Only the order owner or admins can cancel"));
}

// Resource loader registered in DI
public sealed class CancelOrderResourceLoader(IOrderRepository repo)
    : ResourceLoaderById<CancelOrderCommand, Order, OrderId>
{
    protected override OrderId GetId(CancelOrderCommand message) => message.OrderId;
    protected override Task<Result<Order>> GetByIdAsync(OrderId id, CancellationToken ct) =>
        repo.GetByIdAsync(id, ct);
}
```

### 4. Implement IActorProvider in your API layer

```csharp
internal sealed class HttpActorProvider(IHttpContextAccessor accessor) : IActorProvider
{
    public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)
    {
        var user = accessor.HttpContext?.User
            ?? throw new InvalidOperationException("No authenticated user.");
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var permissions = user.Claims
            .Where(c => c.Type == "permission")
            .Select(c => c.Value)
            .ToHashSet();
        return Task.FromResult(Actor.Create(id, permissions));
    }
}
```

## Pipeline Execution Order

```
Request → ExceptionBehavior → TracingBehavior → LoggingBehavior
  → AuthorizationBehavior → ResourceAuthorizationBehavior<,,>
    → ValidationBehavior → Handler → Result<T>
```

## Package References

| Project | Package |
|---------|---------|
| Domain/Application layer | `Trellis.Authorization` (auth types only, no Mediator dependency) |
| Application layer (CQRS) | `Trellis.Mediator` (includes `Trellis.Authorization` transitively) |
| API/Host (composition root) | `Mediator.SourceGenerator` |

`Mediator.SourceGenerator` is installed **only** in the outermost project.
