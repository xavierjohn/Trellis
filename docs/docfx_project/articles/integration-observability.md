---
title: Observability and Monitoring
package: Trellis.ServiceDefaults
topics: [observability, opentelemetry, tracing, logging, redaction, mediator, primitives]
related_api_reference: [trellis-api-servicedefaults.md, trellis-api-mediator.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Observability and Monitoring

Trellis emits OpenTelemetry `Activity` spans and `ILogger` entries from three `ActivitySource`s — `Trellis.Mediator`, `Trellis.Primitives`, and `Trellis.Core` — wired through the canonical `AddTrellis(...)` composition root.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Wire mediator tracing + logging via the composition root | `services.AddTrellis(o => o.UseMediator())` | [Default registration](#default-registration) |
| Subscribe an OTel `TracerProvider` to mediator spans | `tracing.AddSource("Trellis.Mediator")` | [Tracing](#tracing) |
| Subscribe to value-object spans (validation/parsing) | `tracing.AddPrimitiveValueObjectInstrumentation()` | [Tracing](#tracing) |
| Subscribe to ROP spans (deep `Bind` / `Map` debugging) | `tracing.AddResultsInstrumentation()` | [Tracing](#tracing) |
| Read structured per-message logs with elapsed-ms outcomes | Built in via `LoggingBehavior<,>` | [Logging](#logging) |
| Allow `Error.Detail` into telemetry (non-PII environments only) | `o.UseMediator(t => t.IncludeErrorDetail = true)` | [Redaction](#redaction) |
| Add business tags to the mediator span from a handler | `Activity.Current?.SetTag(...)` | [Custom enrichment](#custom-enrichment) |
| Get HTTP / DB / runtime metrics | Standard OTel instrumentation packages | [Metrics](#metrics) |

## Use this guide when

- You are wiring observability into a Trellis composition root and want the same span/log surface as `AddTrellisBehaviors()` produces.
- You need to know exactly which tags `TracingBehavior<,>` writes on success vs. failure, and what `LoggingBehavior<,>` emits.
- You are deciding which `ActivitySource`s to subscribe to — and which to leave off because they are noisy.
- You need to confirm that `Error.Detail` is redacted by default and how to opt in.

## Surface at a glance

| Surface | Type / API | Emits | Subscribed via |
|---|---|---|---|
| Mediator tracing | `TracingBehavior<TMessage, TResponse>` (`Trellis.Mediator`) | One `Activity` per mediator message; tags `error.code`, `error.type`; `ActivityStatusCode.Ok` / `Error` | Registered by `AddTrellisBehaviors()`; subscribe with `tracing.AddSource(TracingBehavior<,>.ActivitySourceName)` (value: `"Trellis.Mediator"`) |
| Mediator logging | `LoggingBehavior<TMessage, TResponse>` (`Trellis.Mediator`) | `Information` start, `Information` end (with elapsed ms), `Warning` on failure (with `Error.Code`) | Registered by `AddTrellisBehaviors()`; consumed by any `ILogger` provider |
| Redaction | `TrellisMediatorTelemetryOptions` (`Trellis.Mediator`) | Controls whether `Error.Detail` flows into the activity status description and log message | DI singleton, configured via `o.UseMediator(t => ...)` or `AddTrellisBehaviors(t => ...)` |
| Primitive value-object tracing | `Trellis.Primitives` `ActivitySource` | One `Activity` per `TryCreate` / `Parse` on a `Required*<TSelf>` value object | `tracing.AddPrimitiveValueObjectInstrumentation()` |
| Result / ROP tracing | `Trellis.Core` `ActivitySource` (`RopTrace.ActivitySourceName`) | Spans for individual `Result` operations — verbose; intended for diagnostics | `tracing.AddResultsInstrumentation()` |
| Composition root | `TrellisServiceBuilder.UseMediator(Action<TrellisMediatorTelemetryOptions>?)` | Registers the five canonical behaviors and the telemetry options | `services.AddTrellis(o => o.UseMediator(...))` |

Full signatures: [`trellis-api-servicedefaults.md`](../api_reference/trellis-api-servicedefaults.md), [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md), [`trellis-api-core.md`](../api_reference/trellis-api-core.md).

## Installation

```bash
dotnet add package Trellis.ServiceDefaults
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
```

`Trellis.Core` already references the OpenTelemetry SDK so `AddResultsInstrumentation()` and `AddPrimitiveValueObjectInstrumentation()` work without extra packages. The exporter and ASP.NET Core / HTTP instrumentation packages above are standard OTel — Trellis does not wrap them.

## Quick start

Register Trellis (which wires `TracingBehavior<,>` and `LoggingBehavior<,>` into the mediator pipeline) and subscribe an OpenTelemetry `TracerProvider` to the mediator activity source.

```csharp
using Mediator;
using OpenTelemetry.Trace;
using Trellis;
using Trellis.Mediator;
using Trellis.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator();
builder.Services.AddTrellis(options => options
    .UseAsp()
    .UseMediator());

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(TracingBehavior<IMessage, IResult>.ActivitySourceName)
        .AddPrimitiveValueObjectInstrumentation()
        .AddOtlpExporter());

var app = builder.Build();
app.Run();

public sealed record CreateOrder(string CustomerId, decimal Total) : ICommand<Result<Unit>>;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrder, Result<Unit>>
{
    public ValueTask<Result<Unit>> Handle(CreateOrder command, CancellationToken cancellationToken)
        => ValueTask.FromResult(Result.Ok());
}
```

Every dispatch of `CreateOrder` now produces:

- one `Activity` named `"CreateOrder"` under source `"Trellis.Mediator"`, and
- two structured log entries (`Handling CreateOrder`, `Handled CreateOrder in N.NNms`).

## Default registration

`AddTrellis(o => o.UseMediator())` is the canonical entry point. It calls `AddTrellisBehaviors()` and is **idempotent** — `UseFluentValidation`, `UseResourceAuthorization`, and `UseEntityFrameworkUnitOfWork<TContext>` all imply `UseMediator`, so repeating the call cannot duplicate registrations.

| Wiring step | What it adds |
|---|---|
| `o.UseMediator()` | Registers `ExceptionBehavior<,>`, `TracingBehavior<,>`, `LoggingBehavior<,>`, `AuthorizationBehavior<,>`, `ValidationBehavior<,>` and a default `TrellisMediatorTelemetryOptions` (Detail redacted). |
| `o.UseMediator(t => t.IncludeErrorDetail = true)` | Same, but replaces the options singleton so `Error.Detail` flows into telemetry. |

Pipeline order (outermost → innermost) is: Exception → Tracing → Logging → Authorization → (ResourceAuthorization, opt-in) → Validation → (Transactional, opt-in via `UseEntityFrameworkUnitOfWork<TContext>`). Every inner behavior runs *inside* the `Trellis.Mediator` activity, so its logs and child spans inherit the same trace and span correlation IDs. See [`trellis-api-mediator.md` → Canonical pipeline order](../api_reference/trellis-api-mediator.md#canonical-pipeline-order).

## Tracing

Trellis ships three independent `ActivitySource`s. Subscribe to each on its own merits.

| Source | Constant | Volume | When to subscribe |
|---|---|---|---|
| `"Trellis.Mediator"` | `TracingBehavior<,>.ActivitySourceName` | One span per mediator message | Always — this is the primary failure-localisation surface. |
| `"Trellis.Primitives"` | (internal — use `AddPrimitiveValueObjectInstrumentation()`) | One span per `TryCreate` / `Parse` on a `Required*<TSelf>` value object | When you need to see *why* input validation rejected a request at the edge. |
| `"Trellis.Core"` | `RopTrace.ActivitySourceName` (= `"Trellis.Core"`) | One span per individual `Result` operation (`Bind`, `Map`, `Tap`, …) | Only for break-glass diagnostics — high cardinality, high volume. |

`TracingBehavior<,>` writes the following on every dispatched message:

| Outcome | Activity status | Tags written |
|---|---|---|
| Handler returns success | `ActivityStatusCode.Ok` | (none) |
| Handler returns `Result.Fail(error)` | `ActivityStatusCode.Error` | `error.code` = `error.Code`; `error.type` = stable error class name (e.g. `Error.NotFound`); `StatusDescription` left empty unless `IncludeErrorDetail = true` |
| Handler throws | `ActivityStatusCode.Error` | `error.type` = exception type name; the exception is **rethrown**; the exception message is **not** copied into telemetry |

```csharp
using Mediator;
using OpenTelemetry.Trace;
using Trellis.Mediator;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(TracingBehavior<IMessage, IResult>.ActivitySourceName)
        .AddPrimitiveValueObjectInstrumentation()
        .AddOtlpExporter());
```

To enable ROP forensics during an investigation, add `.AddResultsInstrumentation()` on top — and remove it again when the investigation is over.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(TracingBehavior<IMessage, IResult>.ActivitySourceName)
        .AddPrimitiveValueObjectInstrumentation()
        .AddResultsInstrumentation()
        .AddConsoleExporter());
```

> [!WARNING]
> `AddResultsInstrumentation()` is a debugging tool. It traces every `Bind`, `Map`, and `Tap` and can flood your collector. Keep it off in production unless you have an active incident.

## Logging

`LoggingBehavior<,>` is registered alongside `TracingBehavior<,>` and uses source-generated `LoggerMessage` methods — no allocation on the hot path. It emits exactly three messages per dispatch:

| Stage | Level | Template |
|---|---|---|
| Entry | `Information` | `Handling {MessageName}` |
| Success exit | `Information` | `Handled {MessageName} in {ElapsedMs:0.00}ms` |
| Failure exit | `Warning` | `Handled {MessageName} in {ElapsedMs:0.00}ms — Failed: {ErrorSummary}` |

`{MessageName}` is `typeof(TMessage).Name`. `{ErrorSummary}` is `error.Code` by default; it becomes `error.GetDisplayMessage()` (which includes `Error.Detail`) only when `IncludeErrorDetail = true`. Because the log entries are written inside the mediator activity, every entry already carries the trace/span IDs propagated by your logging provider's OTel integration.

```csharp
builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.IncludeScopes = true;
    o.AddOtlpExporter();
});
```

## Redaction

`TrellisMediatorTelemetryOptions.IncludeErrorDetail` is the single redaction switch. It defaults to `false`. Both `TracingBehavior<,>` and `LoggingBehavior<,>` consume it.

| Setting | `error.Code` | `error.type` | `Error.Detail` |
|---|---|---|---|
| `IncludeErrorDetail = false` (default) | Emitted | Emitted | Redacted from `Activity.StatusDescription` and from log messages |
| `IncludeErrorDetail = true` | Emitted | Emitted | Included in `Activity.StatusDescription` and the failure log message |

Configure via the composition root:

```csharp
services.AddTrellis(options => options
    .UseAsp()
    .UseMediator(telemetry => telemetry.IncludeErrorDetail = builder.Environment.IsDevelopment()));
```

Or directly when not using `AddTrellis(...)`:

```csharp
services.AddTrellisBehaviors(telemetry => telemetry.IncludeErrorDetail = false);
```

> [!WARNING]
> `Error.Detail` is the only Trellis-emitted field that may carry user input or domain payloads (an order id, an email address, a free-text validation message). `Error.Code` and the stable error type name are operator-defined identifiers and remain PII-free regardless of this setting.

## Metrics

Trellis does not register an OpenTelemetry `Meter` and emits no framework counters or histograms. Use the standard OpenTelemetry instrumentation packages for runtime, ASP.NET Core, and HTTP-client metrics:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
```

For per-message latency dashboards, derive metrics from the mediator activity (`Trellis.Mediator`) or from the `Handled {MessageName} in {ElapsedMs:0.00}ms` log entries — both already carry the elapsed time.

## Custom enrichment

`TracingBehavior<,>` opens its activity *before* the inner behaviors run, so any code inside the handler (or in inner behaviors) can tag the same activity through `Activity.Current`.

```csharp
using System.Diagnostics;
using Mediator;
using Trellis;

public sealed record SubmitInvoice(string CustomerId, decimal Amount) : ICommand<Result<Unit>>;

public sealed class SubmitInvoiceHandler : ICommandHandler<SubmitInvoice, Result<Unit>>
{
    public ValueTask<Result<Unit>> Handle(SubmitInvoice command, CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("invoice.customer_id", command.CustomerId);
        Activity.Current?.SetTag("invoice.amount", command.Amount);

        return ValueTask.FromResult(Result.Ok());
    }
}
```

For broader cross-cutting enrichment (every span, not just mediator messages), use a standard OpenTelemetry `BaseProcessor<Activity>` registered on the `TracerProviderBuilder` — Trellis does not provide a wrapper because the OTel SDK already does.

## Composition

The mediator activity is the natural parent for any spans your business code opens around the same operation. Open a child `ActivitySource` from your application, and it will be correlated with the `Trellis.Mediator` activity (and any ASP.NET Core request span above it) automatically.

```csharp
using System.Diagnostics;
using Mediator;
using Trellis;

public sealed record SettleBatch(string BatchId) : ICommand<Result<Unit>>;

public sealed class SettleBatchHandler : ICommandHandler<SettleBatch, Result<Unit>>
{
    private static readonly ActivitySource Source = new("Acme.Billing");

    public ValueTask<Result<Unit>> Handle(SettleBatch command, CancellationToken cancellationToken)
    {
        using var activity = Source.StartActivity("Billing.SettleBatch");
        activity?.SetTag("batch.id", command.BatchId);

        return ValueTask.FromResult(Result.Ok());
    }
}
```

Subscribe to the new source the same way:

```csharp
tracing.AddSource("Acme.Billing");
```

## Practical guidance

- **Subscribe to `Trellis.Mediator` always.** It is one span per message and is the cheapest lever for failure localisation.
- **Add `AddPrimitiveValueObjectInstrumentation()` early.** Validation traces are the clearest signal for "why is this request being rejected at the edge?".
- **Treat `AddResultsInstrumentation()` as break-glass.** Turn it on for an incident, turn it off afterwards.
- **Keep `IncludeErrorDetail = false` in production.** Code + type are sufficient for dashboards and alerts; flip it on per-environment only when you have audited every `Error.Detail` write site.
- **Sample at the collector or via OTel.** Trellis does not sample. Set `SetSampler(new TraceIdRatioBasedSampler(0.1))` on the `TracerProvider` if you need to throttle.
- **Use `error.code` for grouping.** It is stable across releases (e.g. `validation.error`, `not.found.error`, `forbidden.error`) and far better than free-form messages.

## Cross-references

- Composition-root API: [`trellis-api-servicedefaults.md`](../api_reference/trellis-api-servicedefaults.md)
- Mediator behaviors, redaction options, pipeline order: [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md)
- ROP `ActivitySource` and `AddResultsInstrumentation`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Primitive value-object instrumentation: [`trellis-api-primitives.md`](../api_reference/trellis-api-primitives.md)
- Mediator integration article: [`integration-mediator.md`](integration-mediator.md)
- HTTP-client integration article: [`integration-http.md`](integration-http.md)
