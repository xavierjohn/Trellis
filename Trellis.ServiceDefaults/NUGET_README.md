# Trellis.ServiceDefaults

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.ServiceDefaults.svg)](https://www.nuget.org/packages/Trellis.ServiceDefaults)

An ordered composition root for Trellis services. Configure authorization, validation, EF Core transactions, outbox, inbox, idempotency, and integration events in one fluent call.

## Installation

```bash
dotnet add package Trellis.ServiceDefaults
```

## Quick Example

```csharp
using Trellis.ServiceDefaults;

builder.Services.AddTrellis(trellis => trellis
    .UseAsp()
    .UseScalarValueValidation()
    .UseProblemDetails()
    .UseMediator()
    .UseFluentValidation(typeof(Program).Assembly)
    .UseClaimsActorProvider()
    .UseResourceAuthorization(typeof(Program).Assembly)
    .UseEntityFrameworkUnitOfWork<AppDbContext>());
```

Call `AddTrellis(...)` once. If omitted, the composition features are intentionally not registered.

`UseInsideTransactionBehavior<TMessage,TResponse,TBehavior>()` or
`UseInsideTransactionBehavior(typeof(RequiredAuditBehavior<,>))` implies `UseMediator()`
and registers an application-owned behavior inside the transaction scope, before the
handler. Its post-handler work finishes before the commit decision, independent of
fluent/direct registration order. Register `IUnitOfWork` separately, typically with
`UseEntityFrameworkUnitOfWork<TContext>()`.

Identical bindings deduplicate; behaviors nest in selection order and unwind in reverse
order. Applications own recorder state, result checks, and cleanup. A returned ordinary
failure skips this invocation's commit, without rollback or permanent rejection of
an outer/later commit. See the same-commit audit recipe for the complete checkpoint pattern.

## Key Features

- Applies Trellis registrations in canonical pipeline order.
- Existing Mediator/resource slots automatically install the dispatch authorization context: static/resource stages and actor-aware handlers share one checked Actor reference. No new actor-handler toggle is needed.
- Keeps feature selection explicit and discoverable at the application composition root.
- Provides slots for ASP.NET Core, authorization, FluentValidation, EF Core unit of work, outbox, inbox, idempotency, and events.
- Preserves package-specific options through focused `UseXxx(...)` methods.
- Existing actor/cache/worker slots preserve identity-only resolution. Supported `UseCachingActorProvider<T>()` binds both contracts to one scoped wrapper with independent lazy caches; custom providers/subclasses opt in explicitly.
- Leaves vendor stores and transport adapters in their owning packages, avoiding unnecessary SDK dependencies.

For most applications, use the builder instead of mixing standalone `AddTrellis*` calls. Provider-specific stores and adapters remain separate registrations by design.

Every `UseIntegrationEvents(...)` overload wires the default **best-effort local** publisher when none is registered. It logs and swallows ordinary consumer failures, so processed integration outbox rows do not retry those consumers. For reliable broker handoff, replace the publisher with an adapter that awaits acceptance and propagates publication failures; acceptance is not consumer success. `UseInbox<TContext>()` supplies a separate non-swallowing consumption seam, not automatic routing: an adapter must call `IInboxDispatcher.DispatchAsync` with the stable message id and expose failures for redelivery.

See the [complete producer/consumer compositions](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-servicedefaults.html#messaging-setup-by-deployment-role).
Transport registrations stay direct so ServiceDefaults does not acquire a vendor SDK.

`UseOutboxReadiness<TContext>(OutboxReadinessOptions.CreateStagedOperationalProfile())`
explicitly enables the named `trellis-outbox` read-only health check alongside
`UseOutbox<TContext>()`. Existing outbox/inbox slots do not enable it. Endpoint
authorization and traffic-readiness versus alerting remain host-owned. Durable capture
age is enabled on the same immutable profile after its nullable schema migration.
Optional consumer lag uses the same slot: register an application-owned
`IConsumerLagProvider` directly and select `ConsumerLagMeasures.Age`, `Count`, or both.
The provider is a leaf adapter, not another builder feature. See [outbox readiness](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore-outbox.html#outbox-readiness).

For a separate worker, pass
`OutboxReadinessOptions.CreateMetadataOnlyProfile(workerMaxAttempts)` to the same slot
in the API, without `UseOutbox`. Match `WorkerMaxAttempts` to the worker's retry policy
using one configuration key. Local options are ignored without comparison or warning.
Metadata-only can evaluate enabled capture age but does not observe remote worker responsiveness.
Generic Host lifecycle startup validates local-mode relay configuration before services
start; bare service providers do not run that validation.

Common `HttpContext.PageUrl(...)` builders require no versioning SDK. Versioned hosts
install `Trellis.Asp.ApiVersioning` and configure the existing
`UseAsp(asp => asp.UseVersionedPageUrls())` callback alongside normal API-versioning
registration; ServiceDefaults itself remains versioning-independent.

Native AOT hosts with struct `Result<T>` responses need the [literal closed-generator
pipeline](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-mediator.html#native-aot-registration)
and direct typed resource registrations, without `UseMediator` or slots that imply it:
native DI cannot dynamically close open behaviors over value-type responses.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-servicedefaults.html)
- [Cross-package cookbook](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-cookbook.html)
- [Mediator reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-mediator.html)
- [EF Core reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-efcore.html)
