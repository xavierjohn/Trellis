# Trellis Examples

A curated set of runnable samples that demonstrate the canonical Trellis patterns. Each sample is intentionally focused on a small set of teachings, not a kitchen sink — the goal is that an AI generator (or a human reading the code) can copy a pattern from here and have it be idiomatic.

> **Audit framing.** Every sample in this folder is held to the v2 *axiom scorecard* (A1–A11). See per-sample READMEs for what each axiom enforces.

## What you'll learn

- How Trellis composes value objects, Result/Error, and aggregates into a runnable HTTP service.
- How the same domain ideas are expressed in **MVC** vs **Minimal API** style.
- How to bring conditional requests (ETag), authentication (SSO), persistence (EF Core), and unit/integration testing into a Trellis codebase.
- How the *application/workflow boundary* keeps event publication, persistence, and side-effects in one well-defined place.

## Prerequisites

- .NET 10 SDK

## Sample map

| Sample | Stack | Primary teachings |
|---|---|---|
| [`Showcase`](./Showcase) | MVC + Minimal API over one banking domain | Aggregate + workflow + (controllers ‖ endpoints); full Error ADT walkthrough; `System.TimeProvider`; lifecycle state machine via `Trellis.StateMachine`; integration tests with `WebApplicationFactory` for both hosting styles. **Start here.** |
| [`ConditionalRequestExample`](./ConditionalRequestExample) | Minimal API + EF Sqlite | RFC 9110 conditional requests (`If-Match` / `If-None-Match`), strong ETags, and 304/412/428 mapping via `Trellis.Asp` extensions. |
| [`SsoExample`](./SsoExample) | MVC | Two authentication modes wired side-by-side: `AddDevelopmentActorProvider()` (reads `X-Test-Actor` for local/dev) and `AddClaimsActorProvider()` (JWT bearer for production). |
| [`EfCoreExample`](./EfCoreExample) | console | EF Core conventions and interceptors that Trellis layers on top of `DbContext`: VO ID conversions, automatic timestamps, value object composition. |
| [`TestingPatterns`](./TestingPatterns) | xUnit | Test-only project demonstrating async usage, parallel execution, `Maybe`, `EquatableArray`, and validating-by-result patterns. |

## Run a web sample

```bash
dotnet run --project Showcase/src/Showcase.Mvc/Showcase.Mvc.csproj
dotnet run --project Showcase/src/Showcase.MinimalApi/Showcase.MinimalApi.csproj
dotnet run --project ConditionalRequestExample/ConditionalRequestExample.csproj
dotnet run --project SsoExample/SsoExample.csproj --launch-profile Development
```

## Run all sample tests

```bash
dotnet test Trellis.slnx -c Release --filter "FullyQualifiedName~Examples"
```

## Conventions enforced across every sample

These are the rules each sample is held to. If you see a sample violate one, file an issue — it's a bug.

| Axiom | Rule |
|---|---|
| **A1a** | Scalar VOs are wire types unconditionally (DTOs, route params, query params). |
| **A1b** | Structured VOs (e.g. `Money`, `MonetaryAmount`) appear in DTOs when their natural JSON shape matches the contract. |
| **A2** | Route params bind to VOs via generator-emitted `IParsable` — `{id:ProductId}` not `{id:guid}`. |
| **A3** | No `.Value` on `Result<T>` in production code. Permitted only in tests and seed/bootstrap with literal-construction inputs. |
| **A4** | Composite VOs are composed via `Result.Combine` and chained through `Bind`/`BindAsync`. |
| **A5** | Errors travel as `Result<T>` and are surfaced via `ToActionResult` / `ToHttpResultAsync`. No hand-built `ProblemDetails`, no `BadRequest(...)`. |
| **A6** | One canonical solution per use-case in a sample. Alternatives belong in docs/blog/test fixtures, not adjacent in the same project. |
| **A7** | Workflows depend on injected abstractions for all external effects (clock, identity, fraud, repository, publisher, HTTP). |
| **A8** | Domain layer purity — no references to ASP.NET, EF Core, or the FluentValidation adapter from a domain project. |
| **A9** | Typed failures only. `Result.Fail` always takes a concrete `Error` case; never a raw string. |
| **A10** | Every state-changing use case crosses exactly one application/workflow boundary that owns: mutate aggregate → publish events → accept changes → persist. |
| **A11** | No exceptions for expected business invalidity. Programmer-error guards (`ArgumentNullException.ThrowIfNull`) on non-nullable parameters are OK. |

## Related docs

- [Examples](https://xavierjohn.github.io/Trellis/articles/examples.html)
- [Introduction](https://xavierjohn.github.io/Trellis/articles/intro.html)
- [Basics](https://xavierjohn.github.io/Trellis/articles/basics.html)
- [Error Handling](../docs/docfx_project/articles/error-handling.md)
