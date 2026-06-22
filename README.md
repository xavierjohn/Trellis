# Trellis

[![Build](https://github.com/xavierjohn/Trellis/actions/workflows/build.yml/badge.svg)](https://github.com/xavierjohn/Trellis/actions/workflows/build.yml)
[![codecov](https://codecov.io/gh/xavierjohn/Trellis/branch/main/graph/badge.svg)](https://codecov.io/gh/xavierjohn/Trellis)
[![NuGet](https://img.shields.io/nuget/v/Trellis.Core.svg)](https://www.nuget.org/packages/Trellis.Core)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trellis.Core.svg)](https://www.nuget.org/packages/Trellis.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/download)
[![C#](https://img.shields.io/badge/C%23-14.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![GitHub Stars](https://img.shields.io/github/stars/xavierjohn/Trellis?style=social)](https://github.com/xavierjohn/Trellis/stargazers)
[![YouTube Channel Subscribers](https://img.shields.io/youtube/channel/subscribers/UC30bqiObz9ML3NMP6a0U7jw?style=social&label=%40trellisdev)](https://www.youtube.com/@trellisdev)
[![Documentation](https://img.shields.io/badge/docs-online-blue.svg)](https://xavierjohn.github.io/Trellis/)

<p align="center">
  <img src="docs/images/hero-banner.png" alt="Trellis — Compiler-enforced guardrails for .NET." />
</p>

> **Compiler-enforced guardrails for .NET.**

Trellis is an opinionated .NET service framework with compiler and analyzer guardrails that make generated code more predictable. It turns typed errors, validated value objects, and composable application pipelines into structure the compiler can enforce — so a whole class of common mistakes fails at build time, whether the code is written by a human or an AI assistant.

📺 **Watch the series:** [youtube.com/@trellisdev](https://www.youtube.com/@trellisdev) — Railway-Oriented Programming, Domain-Driven Design, and more.

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

- **Compiler-enforced guardrails** — Roslyn analyzers and types so a whole class of illegal states won't compile; humans and AI stay on the happy path.
- `Result<T>` and `Maybe<T>` pipelines that make failures explicit — no exceptions for control flow.
- Strongly typed value objects that eliminate primitive obsession.
- DDD building blocks: `Aggregate`, `Entity`, `ValueObject`, `Specification`, and domain & integration events.
- Reliable, crash-safe event delivery via a transactional outbox — events persist atomically with state and relay after commit.
- ASP.NET Core, EF Core, Mediator, HttpClient, FluentValidation, and state-machine integrations.
- AOT-friendly, allocation-conscious APIs built for modern .NET.

> **AOT:** per-package APIs are trim- and AOT-safe; `Trellis.ServiceDefaults` exposes both AOT-safe per-type overloads and assembly-scanning overloads (annotated so the AOT analyzer flags the choice). `Trellis.EntityFrameworkCore` follows EF Core's own AOT policy. See the [docs](https://xavierjohn.github.io/Trellis/) for details.

## Quick Start

**Add the library:**

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

**Or scaffold a full production-ready service** — Clean Architecture, API versioning, EF Core, OpenAPI, and tests — with the [`trellis-asp`](https://github.com/xavierjohn/Trellis.AspTemplate) template:

```bash
dotnet new trellis-asp -n MyService
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
| [Trellis.Persistence.Abstractions](https://www.nuget.org/packages/Trellis.Persistence.Abstractions) | Store-agnostic persistence contracts (`IUnitOfWork`, `IInboxStore`, `IConsumerCheckpointStore`) implementable over EF Core, Dapper, Cosmos DB, or any store |
| [Trellis.FluentValidation](https://www.nuget.org/packages/Trellis.FluentValidation) | FluentValidation output converted into Trellis results |
| [Trellis.EntityFrameworkCore](https://www.nuget.org/packages/Trellis.EntityFrameworkCore) | EF Core conventions, converters, Maybe queries, and safe save helpers (bundles the `Maybe<T>` / owned value-object source generator) |
| [Trellis.EntityFrameworkCore.Outbox](https://www.nuget.org/packages/Trellis.EntityFrameworkCore.Outbox) | Transactional outbox that captures domain events in the same transaction and relays them after commit, with domain/integration-event routing |
| [Trellis.ServiceDefaults](https://www.nuget.org/packages/Trellis.ServiceDefaults) | Opinionated composition builder for wiring Trellis web-service modules in the canonical order |
| [Trellis.StateMachine](https://www.nuget.org/packages/Trellis.StateMachine) | Stateless transitions that return `Result<TState>` |
| [Trellis.Testing](https://www.nuget.org/packages/Trellis.Testing) | FluentAssertions extensions for `Result<T>` and `Maybe<T>` |

## Performance

Typical overhead is measured in single-digit to low double-digit nanoseconds—tiny next to a database call or HTTP request. [Benchmarks](BENCHMARKS.md)

## Learn

- 📺 **YouTube — [@trellisdev](https://www.youtube.com/@trellisdev):** the Trellis video series covering Railway-Oriented Programming, Domain-Driven Design, and more.
- [Full documentation](https://xavierjohn.github.io/Trellis/)
- [Getting started](https://xavierjohn.github.io/Trellis/articles/intro.html)
- [With vs without Trellis](https://xavierjohn.github.io/Trellis/articles/with-vs-without-trellis.html)
- [API reference](https://xavierjohn.github.io/Trellis/api/index.html)
- [Training lab + AI consistency benchmark](https://github.com/xavierjohn/trellis-training) — hand an AI model Trellis, a template, and a spec; let it ship a service in one shot; score against **66 criteria across 6 quality levels**.

## Related repositories

The Trellis family extends the core framework into multi-service topologies, ready-to-scaffold templates, and operational telemetry. All packages live on nuget.org; all repos share the same MIT license, branch-protected `main`, and analyzer gates.

- [`xavierjohn/Trellis.Microservices`](https://github.com/xavierjohn/Trellis.Microservices) — microservice trust-boundary packages: YARP gateway that mints internal-network JWTs + consumer-side actor provider enforcing a strict claim contract that defends multi-tenant ABAC. Ships `Trellis.Microservices.Abstractions`, `Trellis.Microservices.AspNetCore`, `Trellis.Yarp`.
- [`xavierjohn/Trellis.Microservices.Template`](https://github.com/xavierjohn/Trellis.Microservices.Template) — `dotnet new trellis-microservices` template scaffolding a multi-tenant Project Tracker (YARP gateway + Projects + Members + Aspire AppHost) that demonstrates resource auth, the HideExistence pattern, and the deny-overrides-allow JWT contract.
- [`xavierjohn/Trellis.AspTemplate`](https://github.com/xavierjohn/Trellis.AspTemplate) — `dotnet new trellis-asp` template scaffolding a production-ready single-service ASP.NET application with Clean Architecture layout (API + Application + Domain + ACL), API versioning, EF Core, OpenAPI, and test infrastructure.
- [`xavierjohn/Trellis.ServiceLevelIndicators`](https://github.com/xavierjohn/Trellis.ServiceLevelIndicators) — latency SLI metrics library for emitting operation-duration histograms via `System.Diagnostics.Metrics` + OpenTelemetry, with rich dimensions (`CustomerResourceId`, `LocationId`, `Operation`, `Outcome`) and ASP.NET Core + API-versioning integrations.
- [`xavierjohn/trellis-training`](https://github.com/xavierjohn/trellis-training) — training lab + AI consistency benchmark: give an AI model Trellis, a template, and a business spec; let it ship a service in one shot; score against 66 criteria across 6 quality levels.

## Contributing

Contributions are welcome. For major changes, please open an issue first and run `dotnet test` before sending a PR.

## License

[MIT](LICENSE)
