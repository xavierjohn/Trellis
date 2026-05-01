---
title: Integration Guides
package: Trellis (multiple)
topics: [integration, http, mediator, ef, asp, fluentvalidation, sso, testing, observability]
related_api_reference: [trellis-api-core.md, trellis-api-mediator.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-fluentvalidation.md, trellis-api-http.md]
last_verified: 2026-05-01
audience: [developer]
---
# Integration

When you adopt Trellis, the hard part is rarely `Result<T>` itself. The real work is making it feel natural in HTTP APIs, validation, persistence, authorization, and observability. These guides show the pieces that fit around Trellis in real applications.

## Patterns Index

| Goal | Article | Topics |
|---|---|---|
| Map `Result<T>` to HTTP responses (MVC, Minimal APIs, Problem Details, ETags, `Prefer`) | [ASP.NET Core](integration-aspnet.md) | asp, http, problem-details |
| Authorize HTTP requests with actor-based claims | [ASP.NET Authorization](integration-asp-authorization.md) | asp, authorization, actor |
| Call HTTP APIs and bridge responses into `Result<T>` / `Maybe<T>` | [HTTP Client](integration-http.md) | http, httpclient, json |
| Add validator-based input checks at the application boundary | [FluentValidation](integration-fluentvalidation.md) | validation, fluentvalidation |
| Persist aggregates with EF Core and explicit failure mapping | [Entity Framework Core](integration-ef.md) | ef, persistence, repository |
| Apply pipeline behaviors (validation, logging, transactions) around handlers | [Mediator](integration-mediator.md) | mediator, pipeline |
| Configure least-privilege database permissions | [Database Permissions](integration-db-permissions.md) | ef, security, permissions |
| Sign users in via external identity providers | [SSO](integration-sso.md) | sso, authentication |
| Test against Microsoft Entra in integration tests | [Entra Testing](integration-entra-testing.md) | testing, entra, sso |
| Write integration tests for Trellis-based services | [Testing](integration-testing.md) | testing, integration |
| Trace, correlate, and diagnose production behavior | [Observability](integration-observability.md) | observability, opentelemetry, tracing |

> [!TIP]
> If you are building a web API, start with [ASP.NET Core Integration](integration-aspnet.md). It gives you the cleanest end-to-end path from domain results to HTTP responses.

## Pick the guide you need

| Guide | Use it when you need to... | What you will learn |
| --- | --- | --- |
| [ASP.NET Core Integration](integration-aspnet.md) | Turn `Result<T>` into clean HTTP responses | MVC, Minimal APIs, Problem Details, ETags, `Prefer`, pagination |
| [HTTP Client Integration](integration-http.md) | Consume HTTP APIs without exception-driven control flow | Functional status handling, deserialization into `Result<T>` and `Maybe<T>` |
| [FluentValidation Integration](integration-fluentvalidation.md) | Add richer validation rules at the application boundary | Validator composition, async validation, converting failures to Trellis results |
| [Entity Framework Core Integration](integration-ef.md) | Keep persistence explicit and result-driven | Repository patterns, database exception mapping, pagination |
| [Observability & Monitoring](integration-observability.md) | Trace and diagnose production behavior | OpenTelemetry, correlation, break-glass Result tracing |

## How the pieces fit together

```mermaid
flowchart LR
    Client[Client Request]
    Asp[ASP.NET Core + Trellis.Asp]
    App[Application Layer]
    Domain[Domain Model]
    Infra[Infrastructure]

    Client --> Asp
    Asp --> App
    App --> Domain
    App --> Infra
    Domain --> App
    Infra --> App
    App --> Asp
    Asp --> Client
```

In practice, most Trellis applications follow the same flow:

1. **Validate input early** with scalar-value validation and/or FluentValidation.
2. **Keep `Result<T>` inside your application layer** while business rules run.
3. **Map results once at the boundary** using ASP.NET Core or HttpClient integrations.
4. **Attach operational concerns** like authorization, concurrency, persistence, and telemetry around that core flow.

## A sensible learning path

### If you are building an HTTP API

1. Start with [ASP.NET Core Integration](integration-aspnet.md)
2. Add [FluentValidation Integration](integration-fluentvalidation.md) for business rules
3. Add [Entity Framework Core Integration](integration-ef.md) for persistence
4. Add [ASP.NET Core Authorization](integration-asp-authorization.md) if requests need actor-based authorization
5. Finish with [Observability & Monitoring](integration-observability.md)

### If you are calling other APIs

Start with [HTTP Client Integration](integration-http.md), then add the validation and observability guides as needed.

## What stays the same across all guides

No matter which integration you use, the same design rule keeps paying off:

> [!NOTE]
> Let Trellis types describe success and failure inside your application. Convert them to framework-specific responses only at the edge.

That usually means:

- `Result<T>` stays in handlers, services, and repositories
- ASP.NET Core controllers/endpoints call `ToHttpResponse(...)`, with `.AsActionResult<T>()` for typed MVC signatures
- outbound HTTP code turns responses back into `Result<T>`
- validation, authorization, and persistence all return explicit failures instead of throwing for expected conditions

## Quick start

If you want the shortest path to something useful:

```csharp
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddTrellisAsp();

var app = builder.Build();
app.MapControllers();
app.Run();
```

From there:

- add `AddScalarValueValidation()` when you use scalar value objects in requests
- add `integration-asp-authorization.md` when HTTP claims need to become an `Actor`
- add EF Core and FluentValidation integrations as your app grows

## Next step

Go to [ASP.NET Core Integration](integration-aspnet.md).

## Cross-references

- Core `Result<T>` / `Maybe<T>` / `Error` semantics: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Mediator pipeline behaviors: [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md)
- ASP.NET Core response mapping surface: [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md)
- EF Core integration surface: [`trellis-api-efcore.md`](../api_reference/trellis-api-efcore.md)
- FluentValidation integration surface: [`trellis-api-fluentvalidation.md`](../api_reference/trellis-api-fluentvalidation.md)
- HttpClient extensions surface: [`trellis-api-http.md`](../api_reference/trellis-api-http.md)
