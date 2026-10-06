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
- Six parameterless actor-aware bases: `ActorCommandHandler`, `ActorQueryHandler`, direct `ActorResourceCommandHandler` / `ActorResourceQueryHandler`, and indirect `ActorResourceViaCommandHandler` / `ActorResourceViaQueryHandler`. Override protected `HandleCore`; concrete constructors keep business dependencies only.
- One Actor reference per dispatch across static/resource authorization and these handler bases. The integral `AuthorizationContextBehavior` guards pending/mismatched/expired snapshots and isolates nested and concurrent sends. Via business methods receive the leaf, not owners.
- Explicit `Handle(message, actor[, resource/leaf], token)` overloads support isolated business tests without DI. They bypass authorization, validation, commits, and events; use actual Mediator sends for policy tests. Existing accessor-only handlers and provider extensions remain supported.
- Adds validation, authorization, tracing, logging, and exception behaviors that understand `Result<T>`.
- Tracing is registered but not *collected* until you call `AddTrellisMediatorInstrumentation()` on your `TracerProviderBuilder`; without it the handler span is silently never recorded.
- Short-circuits failures before handlers do unnecessary work.
- Unified `ValidationBehavior` composes `IValidate` + every `IMessageValidator<TMessage>` (e.g., the `Trellis.FluentValidation` adapter) and aggregates failures into one response.
- Supports resource authorization with explicit or assembly-scanned registration.
- `AddSharedResourceAuthorization<TMessage, TResource, TId, TResponse>()` registers the authorization behavior, authorized-resource accessor, and shared-loader adapter without scanning. Register `SharedResourceLoaderById<TResource,TId>` separately. Existing per-message loaders are preserved; the lower-level registration APIs remain unchanged.
- Per-resource `AuthFailureExposurePolicy` (`HideAsNotFound`) translates `Forbidden` / `AuthenticationRequired` to `NotFound(ResourceRef)` for sensitive resources whose mere existence is itself a leak.

- Nested domain-event dispatch waits for the owning successful unit-of-work commit, retaining inner aggregate responses even when the outer command returns a DTO or Unit. Failure/throw discards the dispatch batch without clearing events; the outbox still captures events on successful `FailAfterCommit` saves.
- `IIntegrationEventCollector` is translator-only. The outbox relay opens `BeginTranslation()` while publishing and draining; `Add` from a command or outside that active lease throws rather than silently losing events.
- `IntegrationMessageContext.BeginCorrelation("workflow-id")` supplies an application-owned business correlation id; inbox dispatch scopes inherit nonblank inbound correlation and expose the inbound message id as the direct cause for outbox capture. `OutboundIntegrationMessage` carries persisted lineage and W3C trace context without guessing from the relay's ambient activity.

## Documentation
- [Handler migration and dispatch contracts](../docs/docfx_project/api_reference/trellis-api-mediator.md#actor-aware-handler-bases): replace custom actor/resource bases, rename business overrides to `HandleCore`, and remove lookup-only constructor dependencies. Standard managed registrations supply the context automatically; hand-built authorization pipelines must add it before authorization.
- [Native AOT registration](../docs/docfx_project/api_reference/trellis-api-mediator.md#native-aot-registration): use literal generator `typeof(...)` configuration for closed behaviors. Native DI cannot close open behaviors over struct `Result<T>` responses; do not combine the closed-generator shape with `AddTrellisBehaviors` or builder slots that imply it.
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-mediator.html)
- [Package API reference](../docs/docfx_project/api_reference/trellis-api-mediator.md)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.Mediator\tests\Trellis.Mediator.Tests.csproj -c Release
```
