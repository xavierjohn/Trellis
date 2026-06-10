# Trellis

[![Build](https://github.com/xavierjohn/Trellis/actions/workflows/build.yml/badge.svg)](https://github.com/xavierjohn/Trellis/actions/workflows/build.yml)
[![codecov](https://codecov.io/gh/xavierjohn/Trellis/branch/main/graph/badge.svg)](https://codecov.io/gh/xavierjohn/Trellis)
[![NuGet](https://img.shields.io/nuget/v/Trellis.Core.svg)](https://www.nuget.org/packages/Trellis.Core)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trellis.Core.svg)](https://www.nuget.org/packages/Trellis.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)
[![C#](https://img.shields.io/badge/C%23-14.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![GitHub Stars](https://img.shields.io/github/stars/xavierjohn/Trellis?style=social)](https://github.com/xavierjohn/Trellis/stargazers)
[![Documentation](https://img.shields.io/badge/docs-online-blue.svg)](https://xavierjohn.github.io/Trellis/)

<p align="center">
  <img src="docs/images/hero-banner.png" alt="Trellis — Structured building blocks for AI-driven enterprise software" />
</p>

> Structured building blocks for AI-driven enterprise software.

Trellis helps AI create consistent, reliable .NET services by turning typed errors, validated value objects, and composable application pipelines into compiler-enforced guardrails.

## Before / After

**Without Trellis**

```csharp
if (string.IsNullOrWhiteSpace(request.Email))
    return Results.BadRequest(new { code = "validation.error", detail = "Email is required." });

if (!request.Email.Contains('@'))
    return Results.BadRequest(new { code = "validation.error", detail = "Email is invalid." });

return Results.Ok(new User(request.Email.Trim().ToLowerInvariant()));
```

**With Trellis**

```csharp
using Trellis.Asp;
using Trellis.Primitives;

return EmailAddress.TryCreate(request.Email)
    .Map(email => new User(email))
    .ToHttpResponse();
```

## What You Get

- `Result<T>` and `Maybe<T>` pipelines that make failures explicit.
- Strongly typed value objects that remove primitive obsession.
- DDD building blocks: `Aggregate`, `Entity`, `ValueObject`, `Specification`, and domain & integration events.
- Reliable, crash-safe event delivery via a transactional outbox—events persist atomically with state and are relayed after commit.
- ASP.NET Core, EF Core, Mediator, HttpClient, FluentValidation, and Stateless integrations.
- Roslyn analyzers and test helpers that keep teams on the happy path.
- AOT-friendly, allocation-conscious APIs built for modern .NET. (Per-package APIs are AOT- and trim-safe; the optional `Trellis.ServiceDefaults` composition builder exposes both AOT-safe per-type overloads — e.g. `UseFluentValidation<TValidator, TMessage>()` — and assembly-scanning overloads annotated with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` so the AOT analyzer surfaces the choice at the consumer's call site. `Trellis.EntityFrameworkCore` follows EF Core's own AOT policy and is intentionally excluded from the AOT publish gate.)

## Quick Start

```bash
dotnet add package Trellis.Core
```

```csharp
using Trellis;

var result = Result.Ok("ada@example.com")
    .Ensure(email => email.Contains('@'),
        Error.InvalidInput.ForField("email", "validation.error", "Email is invalid."))
    .Map(email => email.Trim().ToLowerInvariant());
```

## Packages

### Core

| Package | What it gives you |
| --- | --- |
| [Trellis.Core](https://www.nuget.org/packages/Trellis.Core) | `Result<T>`, `Maybe<T>`, typed errors, and pipeline operators |
| [Trellis.Primitives](https://www.nuget.org/packages/Trellis.Primitives) | Ready-to-use concrete value objects plus JSON/tracing infrastructure |
| [Trellis.Analyzers](https://www.nuget.org/packages/Trellis.Analyzers) | Compile-time guidance for Result, Maybe, and EF Core usage |

### Integration

| Package | What it gives you |
| --- | --- |
| [Trellis.Asp](https://www.nuget.org/packages/Trellis.Asp) | Result-to-HTTP mapping, scalar validation, JSON/model binding (bundles the AOT-friendly JSON converter generator), and ASP.NET actor providers (Claims, Entra, Development) |
| [Trellis.Authorization](https://www.nuget.org/packages/Trellis.Authorization) | `Actor`, permission checks, and resource authorization primitives |
| [Trellis.Http](https://www.nuget.org/packages/Trellis.Http) | `HttpClient` extensions that stay inside the Result pipeline |
| [Trellis.Http.Abstractions](https://www.nuget.org/packages/Trellis.Http.Abstractions) | HTTP-aware boundary primitives (`HttpError.*` cases, `EntityTagValue`, `PreconditionKind`, `RetryAfterValue`, `AuthChallenge`) shared by `Trellis.Asp` and `Trellis.Http` |
| [Trellis.Mediator](https://www.nuget.org/packages/Trellis.Mediator) | Result-aware pipeline behaviors for [Mediator](https://github.com/martinothamar/Mediator) |
| [Trellis.FluentValidation](https://www.nuget.org/packages/Trellis.FluentValidation) | FluentValidation output converted into Trellis results |
| [Trellis.EntityFrameworkCore](https://www.nuget.org/packages/Trellis.EntityFrameworkCore) | EF Core conventions, converters, Maybe queries, and safe save helpers (bundles the `Maybe<T>` / owned value-object source generator) |
| [Trellis.EntityFrameworkCore.Outbox](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Outbox) | Transactional outbox that captures domain events in the same transaction and relays them after commit, with domain/integration-event routing |
| [Trellis.ServiceDefaults](https://www.nuget.org/packages/Trellis.ServiceDefaults) | Opinionated composition builder for wiring Trellis web-service modules in the canonical order |
| [Trellis.StateMachine](https://www.nuget.org/packages/Trellis.StateMachine) | Stateless transitions that return `Result<TState>` |
| [Trellis.Testing](https://www.nuget.org/packages/Trellis.Testing) | FluentAssertions extensions for `Result<T>` and `Maybe<T>` |

## Performance

Typical overhead is measured in single-digit to low double-digit nanoseconds—tiny next to a database call or HTTP request. [Benchmarks](BENCHMARKS.md)

## Documentation

- [Full documentation](https://xavierjohn.github.io/Trellis/)
- [Getting started](https://xavierjohn.github.io/Trellis/articles/intro.html)
- [With vs without Trellis](https://xavierjohn.github.io/Trellis/articles/with-vs-without-trellis.html)
- [API reference](https://xavierjohn.github.io/Trellis/api/index.html)
- [Training lab](https://github.com/xavierjohn/trellis-training)

## Related repositories

The Trellis family extends the core framework into multi-service topologies, ready-to-scaffold templates, and operational telemetry. All packages live on nuget.org; all repos share the same MIT license, branch-protected `main`, and analyzer gates.

- [`xavierjohn/Trellis.Microservices`](https://github.com/xavierjohn/Trellis.Microservices) — microservice trust-boundary packages: YARP gateway that mints internal-network JWTs + consumer-side actor provider enforcing a strict claim contract that defends multi-tenant ABAC. Ships `Trellis.Microservices.Abstractions`, `Trellis.Microservices.AspNetCore`, `Trellis.Yarp`.
- [`xavierjohn/Trellis.Microservices.Template`](https://github.com/xavierjohn/Trellis.Microservices.Template) — `dotnet new trellis-microservices` template scaffolding a multi-tenant Project Tracker (YARP gateway + Projects + Members + Aspire AppHost) that demonstrates resource auth, the HideExistence pattern, and the deny-overrides-allow JWT contract.
- [`xavierjohn/Trellis.AspTemplate`](https://github.com/xavierjohn/Trellis.AspTemplate) — `dotnet new trellis-asp` template scaffolding a production-ready single-service ASP.NET application with Clean Architecture layout (API + Application + Domain + ACL), API versioning, EF Core, OpenAPI, and test infrastructure.
- [`xavierjohn/Trellis.ServiceLevelIndicators`](https://github.com/xavierjohn/Trellis.ServiceLevelIndicators) — latency SLI metrics library for emitting operation-duration histograms via `System.Diagnostics.Metrics` + OpenTelemetry, with rich dimensions (`CustomerResourceId`, `LocationId`, `Operation`, `Outcome`) and ASP.NET Core + API-versioning integrations.
- [`xavierjohn/trellis-training`](https://github.com/xavierjohn/trellis-training) — training lab + AI consistency benchmark: give an AI model Trellis, a template, and a business spec; let it ship a service in one shot; score against 57 criteria across 5 quality levels.

## Contributing

Contributions are welcome. For major changes, please open an issue first and run `dotnet test` before sending a PR.

## License

[MIT](LICENSE)
