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
            ? Result.Fail(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(nameof(Id)), "validation.error") { Detail = "Order ID is required." })))
            : Result.Ok();
}

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellisBehaviors();
```

> [!IMPORTANT]
> Use `ServiceLifetime.Scoped` when calling `AddMediator(...)` in a host with a request scope. The Trellis behaviors are scoped (they depend on per-request services); the Mediator default of `Singleton` will fail ASP.NET's root-scope validation as soon as the first behavior tries to resolve a scoped dependency.

## Key Features
- Adds validation, authorization, tracing, logging, and exception behaviors that understand `Result<T>`.
- Short-circuits failures before handlers do unnecessary work.
- Unified `ValidationBehavior` composes `IValidate` + every `IMessageValidator<TMessage>` (e.g., the `Trellis.FluentValidation` adapter) and aggregates failures into one response.
- Supports resource authorization with explicit or assembly-scanned registration.
- Per-resource `AuthFailureExposurePolicy` (`HideAsNotFound`) translates `Forbidden` / `AuthenticationRequired` to `NotFound(ResourceRef)` for sensitive resources whose mere existence is itself a leak; configured via `ResourceAuthorizationOptions.HideExistence<TResource>()`.
- **Domain event dispatch**: implement `IDomainEventHandler<TEvent>`, register with `AddDomainEventDispatch(...)`, and the framework snapshots `IAggregate.UncommittedEvents()` after a successful `Result<TAggregate>` command. It publishes only that snapshot, calls `AcceptChanges()` only on clean validation, and throws `DomainEventHandlerCascadedException` if the pending-event list at the end of dispatch differs from the entry snapshot (length or reference equality — i.e., a handler raised new events, cleared via `AcceptChanges`, replaced, or reordered).
- **Tracked-aggregate dispatch (opt-in)**: `TrackedAggregateDomainEventDispatchBehavior<,>` reads committed aggregates from the unit of work and applies the same snapshot contract across all of them, including cross-aggregate cascade detection. Mutually exclusive with response-shape dispatch.
- **Operational caveat**: dispatch runs after EF unit-of-work commit. Cascade detection can return a failure-shaped response after the database write is durable; durable at-least-once side effects require an outbox (planned, not shipped). Non-cancellation handler exceptions are still logged and swallowed by the default publisher.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-mediator.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
