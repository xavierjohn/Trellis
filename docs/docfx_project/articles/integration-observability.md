---
title: Observability and Monitoring
package: Trellis.ServiceDefaults
topics: [observability, opentelemetry, tracing, metrics, logging, redaction, mediator, primitives]
related_api_reference: [trellis-api-servicedefaults.md, trellis-api-mediator.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Observability and Monitoring

Trellis emits OpenTelemetry `Activity` spans and `ILogger` entries from three `ActivitySource`s — `"Trellis.Mediator"`, `"Trellis.Primitives"`, and `"Trellis.Results"` — wired through the canonical `AddTrellis(...)` composition root, plus validation counters from one `Meter`, `Trellis.Validation`.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Wire mediator tracing + logging via the composition root | `services.AddTrellis(o => o.UseMediator())` | [Default registration](#default-registration) |
| Subscribe an OTel `TracerProvider` to mediator spans | `tracing.AddTrellisMediatorInstrumentation()` | [Tracing](#tracing) |
| Subscribe to value-object spans (validation/parsing) | `tracing.AddTrellisPrimitivesInstrumentation()` | [Tracing](#tracing) |
| Subscribe to ROP spans (deep `Bind` / `Map` debugging) | `tracing.AddTrellisResultsInstrumentation()` | [Tracing](#tracing) |
| Read structured per-message logs with elapsed-ms outcomes | Built in via `LoggingBehavior<,>` | [Logging](#logging) |
| Allow `Error.Detail` into telemetry (non-PII environments only) | `o.UseMediator(t => t.IncludeErrorDetail = true)` | [Redaction](#redaction) |
| Add business tags to the mediator span from a handler | `Activity.Current?.SetTag(...)` | [Custom enrichment](#custom-enrichment) |
| Get HTTP / DB / runtime metrics | Standard OTel instrumentation packages | [Metrics](#metrics) |
| Find out whether a validation rule ever actually fires | `metrics.AddTrellisValidationInstrumentation()` | [Metrics](#metrics) |

## Use this guide when

- You are wiring observability into a Trellis composition root and want the same span/log surface as `AddTrellisBehaviors()` produces.
- You need to know exactly which tags `TracingBehavior<,>` writes on success vs. failure, and what `LoggingBehavior<,>` emits.
- You are deciding which `ActivitySource`s to subscribe to — and which to leave off because they are noisy.
- You need to confirm that `Error.Detail` is redacted by default and how to opt in.

## Surface at a glance

| Surface | Type / API | Emits | Subscribed via |
|---|---|---|---|
| Mediator tracing | `TracingBehavior<TMessage, TResponse>` (`Trellis.Mediator`) | One `Activity` per mediator message; tags `error.code`, `error.type`; `ActivityStatusCode.Ok` / `Error` (request cancellations stay `Unset`) | Registered by `AddTrellisBehaviors()`; subscribe with `tracing.AddTrellisMediatorInstrumentation()` (source name: `"Trellis.Mediator"`) |
| Mediator logging | `LoggingBehavior<TMessage, TResponse>` (`Trellis.Mediator`) | `Debug` start, `Debug` end (with elapsed ms), `Warning` on failure (with `Error.Code`) | Registered by `AddTrellisBehaviors()`; consumed by any `ILogger` provider. Per-call timing is at Debug so production at the default `Information` minimum is quiet; raise via `"Trellis.Mediator": "Debug"` in logging configuration to opt back in. |
| Redaction | `TrellisMediatorTelemetryOptions` (`Trellis.Mediator`) | Controls whether `Error.Detail` flows into the activity status description and log message | DI singleton, configured via `o.UseMediator(t => ...)` or `AddTrellisBehaviors(t => ...)` |
| Primitive value-object tracing | `"Trellis.Primitives"` `ActivitySource` | Exactly one `Activity` per value-object factory call (`TryCreate` / `FromFraction`; a `Parse` call delegates to and is traced under `.TryCreate`), named after the factory (e.g. `EmailAddress.TryCreate`), with `Ok` / `Error` status on both success and failure | `tracing.AddTrellisPrimitivesInstrumentation()` |
| Result / ROP tracing | `"Trellis.Results"` `ActivitySource` (`RopTrace.ActivitySourceName`) | Spans for individual `Result` operations — verbose; intended for diagnostics | `tracing.AddTrellisResultsInstrumentation()` |
| Validation metrics | `ValidationMetrics` (`Trellis.Core`) | `trellis.validation.failures`, one increment per violation as a `FieldViolation` or `RuleViolation` is created; tags `validation.code`, `validation.violation` | `metrics.AddTrellisValidationInstrumentation()` (meter name: `"Trellis.Validation"`) |
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

`Trellis.Core` already references the OpenTelemetry SDK so `AddTrellisResultsInstrumentation()` and `AddTrellisPrimitivesInstrumentation()` work without extra packages. The exporter and ASP.NET Core / HTTP instrumentation packages above are standard OTel — Trellis does not wrap them.

## Quick start

Register Trellis (which wires `TracingBehavior<,>` and `LoggingBehavior<,>` into the mediator pipeline) and subscribe an OpenTelemetry `TracerProvider` to the mediator activity source.

```csharp
using Mediator;
using OpenTelemetry.Trace;
using Trellis;
using Trellis.Mediator;
using Trellis.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped);
builder.Services.AddTrellis(options => options
    .UseAsp()
    .UseMediator());

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddTrellisMediatorInstrumentation()
        .AddTrellisPrimitivesInstrumentation()
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
| `"Trellis.Primitives"` | (internal — use `AddTrellisPrimitivesInstrumentation()`) | Exactly one span per value-object factory call (`TryCreate` / `FromFraction`; a `Parse` call is traced under `.TryCreate`) | When you need to see *why* input validation rejected a request at the edge. |
| `"Trellis.Results"` | `RopTrace.ActivitySourceName` (= `"Trellis.Results"`) | One span per individual `Result` operation (`Bind`, `Map`, `Tap`, …) | Only for break-glass diagnostics — high cardinality, high volume. |

`TracingBehavior<,>` writes the following on every dispatched message:

| Outcome | Activity status | Tags written |
|---|---|---|
| Handler returns success | `ActivityStatusCode.Ok` | (none) |
| Handler returns `Result.Fail(error)` | `ActivityStatusCode.Error` | `error.code` = `error.Code`; `error.type` = stable error class name (e.g. `Error.NotFound`); `StatusDescription` left empty unless `IncludeErrorDetail = true` |
| Handler throws `OperationCanceledException` carrying the canceled request `CancellationToken` | `ActivityStatusCode.Unset` | `otel.status_description` = `canceled`; standard exception event tags (`exception.type`, `exception.message`, `exception.stacktrace`); the exception is **rethrown** |
| Handler throws any other exception (including `OperationCanceledException` from a non-request token) | `ActivityStatusCode.Error` | `error.type` = exception type name; standard exception event tags (`exception.type`, `exception.message`, `exception.stacktrace`); the exception is **rethrown**; the exception message is **not** copied into `Activity.StatusDescription` |

```csharp
using Mediator;
using OpenTelemetry.Trace;
using Trellis.Mediator;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddTrellisMediatorInstrumentation()
        .AddTrellisPrimitivesInstrumentation()
        .AddOtlpExporter());
```

To enable ROP forensics during an investigation, add `.AddTrellisResultsInstrumentation()` on top — and remove it again when the investigation is over.

When ROP instrumentation is not enabled, `Result` operators do not mutate the ambient ASP.NET request span
or the `Trellis.Mediator` pipeline span. Those outer spans keep their own status semantics; only ROP spans
created by the `"Trellis.Results"` source receive per-step `Ok` / `Error` status and `result.error.code` / `result.error.type` tags.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddTrellisMediatorInstrumentation()
        .AddTrellisPrimitivesInstrumentation()
        .AddTrellisResultsInstrumentation()
        .AddConsoleExporter());
```

> [!WARNING]
> `AddTrellisResultsInstrumentation()` is a debugging tool. It traces every `Bind`, `Map`, and `Tap` and can flood your collector. Keep it off in production unless you have an active incident.

## Logging

`LoggingBehavior<,>` is registered alongside `TracingBehavior<,>` and uses source-generated `LoggerMessage` methods — no allocation on the hot path. It emits exactly three messages per dispatch:

| Stage | Level | Template |
|---|---|---|
| Entry | `Debug` | `Handling {MessageName}` |
| Success exit | `Debug` | `Handled {MessageName} in {ElapsedMs:0.00}ms` |
| Failure exit | `Warning` | `Handled {MessageName} in {ElapsedMs:0.00}ms — Failed: {ErrorSummary}` |

> **Why Debug for the success path?** Cross-cutting per-message timing is observability noise, not a business event — at `Information` minimum (the ASP.NET Core default), every request would flood the log with at least two lines per mediator dispatch. Raise to `"Trellis.Mediator": "Debug"` in `appsettings.Development.json` (or any environment) to opt back in. Failures stay at `Warning` so they surface at the default minimum level even when Trellis.Mediator is filtered to Information.

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

Trellis publishes one `Meter`, `Trellis.Validation`, carrying a single counter: `trellis.validation.failures`, incremented once per violation as a validation failure is created. Subscribe with `AddTrellisValidationInstrumentation()`, alongside the standard OpenTelemetry instrumentation packages for runtime, ASP.NET Core, and HTTP-client metrics:

```csharp
using OpenTelemetry.Metrics;
using Trellis;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddTrellisValidationInstrumentation()
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
```

The counter carries two tags: `validation.code` (a `ValidationCodes` constant, or `other`) and `validation.violation` (`field` or `rule`).

### What the counter is for

Validation failures look like user error, and that label is why nobody looks at them. The reason to watch them is that **server-side validation is a backstop**: when a client enforces the same rules before sending, this counter sits near zero. That expected value is what makes it alertable — a rising count does not mean users got worse at typing. It means one of:

- **Client-side validation has drifted from the server's.** The same rules live in two places; this is the drift detector.
- **A client broke against you.** One code climbing on one route after someone else's deploy is an integration break.
- **You tightened a rule and did not notice.** A regex or length change silently rejects traffic that used to pass, and support tickets are otherwise the first alert.

All three are your defect arriving disguised as theirs.

**Alert on the rate of a code changing, not on absolute volume**, and expect a non-zero floor if third parties call you — you control your own client's rules, not theirs.

The `validation.code` tag is what makes this actionable, and it is why an HTTP-status metric is not a substitute: a 4xx rate tells you *something* drifted; the reason code tells you **which rule** did. It deliberately stops there — there is no field or route tag, because both are unbounded. Detection lives here; diagnosis lives in the trace, whose JSON pointer names the offending field.

A second, narrower use is the framework's own: "is this rule dead?" — whether a reason code the framework can emit is ever actually emitted. Nothing else answers it. A trace answers it only for sampled requests, so a code with zero volume is indistinguishable from one whose traces were all sampled away. Note that a code which never fires may be *shadowed* by an earlier check rather than genuinely unused; the two read identically until you look.

Two properties are worth knowing before you build a dashboard on it:

- **Violations are counted where the violation is created** — the `FieldViolation.ReasonCode` and `RuleViolation.ReasonCode` initializers — not at a reporting boundary and not on the `Error.InvalidInput` that carries them. A failure can surface at the HTTP boundary, at the mediator pipeline, at both, or — in a worker — at neither, so no reporting site sees each failure exactly once. The carrying failure is no better: it is rebuilt during pointer rebasing and error aggregation, so counting there counts one rule firing several times. The violation is created once when a rule fires and only copied afterwards. A consequence worth knowing: the counter measures rules firing, not responses sent — a violation created and then discarded is still counted.
- **Codes outside the framework vocabulary are bucketed as `other`.** An application code reaches the wire verbatim, so tagging it verbatim would let an application minting a code per entity or per tenant create unbounded time series. The total stays exact; only the breakdown is bucketed.

> **This gap is silent.** Until `AddTrellisValidationInstrumentation()` is called the counter is inert, and nothing reports the omission — a metric that never appears looks exactly like a rule that never fires.

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
- **Add `AddTrellisPrimitivesInstrumentation()` early.** Validation traces are the clearest signal for "why is this request being rejected at the edge?".
- **Treat `AddTrellisResultsInstrumentation()` as break-glass.** Turn it on for an incident, turn it off afterwards.
- **Keep `IncludeErrorDetail = false` in production.** Code + type are sufficient for dashboards and alerts; flip it on per-environment only when you have audited every `Error.Detail` write site.
- **Sample at the collector or via OTel.** Trellis does not sample. Set `SetSampler(new TraceIdRatioBasedSampler(0.1))` on the `TracerProvider` if you need to throttle.
- **Use `error.code` for grouping.** It is stable across releases (e.g. `validation.error`, `not.found.error`, `forbidden.error`) and far better than free-form messages.

## Cross-references

- Composition-root API: [`trellis-api-servicedefaults.md`](../api_reference/trellis-api-servicedefaults.md)
- Mediator behaviors, redaction options, pipeline order: [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md)
- ROP `ActivitySource` and `AddTrellisResultsInstrumentation`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Primitive value-object instrumentation: [`trellis-api-primitives.md`](../api_reference/trellis-api-primitives.md)
- Mediator integration article: [`integration-mediator.md`](integration-mediator.md)
- HTTP-client integration article: [`integration-http.md`](integration-http.md)
