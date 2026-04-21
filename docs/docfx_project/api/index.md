# Trellis API Reference

Trellis is a .NET framework that **guides AI to develop structured, maintainable enterprise software**. It combines Railway-Oriented Programming and Domain-Driven Design into building blocks where the compiler enforces correctness — errors must be handled, objects cannot be constructed in invalid states, and business logic reads like the specification it was generated from.

A trellis guides growth in the right direction. In a garden, plants grow along the trellis rather than sprawling randomly. In software, Trellis guides AI-generated code into correct, composable patterns — turning a plain-language specification into working, tested software with zero warnings and zero skipped validations.

```csharp
// Without Trellis — AI scatters validation, misses edge cases, drifts across endpoints
if (string.IsNullOrWhiteSpace(request.Email))
    return Results.BadRequest(new { code = "validation.error", detail = "Email is required." });
if (!request.Email.Contains('@'))
    return Results.BadRequest(new { code = "validation.error", detail = "Email is invalid." });
return Results.Ok(new User(request.Email.Trim().ToLowerInvariant()));

// With Trellis — AI follows the types, the compiler catches the rest
return EmailAddress.TryCreate(request.Email)
    .Map(email => new User(email))
    .ToHttpResult();
```

> New to Trellis? Start with the [Introduction](~/articles/intro.md), see [how AI uses Trellis](~/articles/ai-code-generation.md), or browse the [examples](~/articles/examples.md).

---

## Core Types

The foundation — zero dependencies beyond the .NET runtime. One `using Trellis;` makes these available.

| Type | Purpose |
|------|---------|
| [`Result<T>`](xref:Trellis.Result`1) | An operation that succeeds with `T` or fails with an [`Error`](xref:Trellis.Error) — the core of Railway-Oriented Programming |
| [`Maybe<T>`](xref:Trellis.Maybe`1) | A value that may or may not exist — type-safe alternative to `null` |
| [`Error`](xref:Trellis.Error) | Typed error hierarchy with built-in HTTP status code mapping |
| [`Unit`](xref:Trellis.Unit) | Represents "no value" for void operations like `Result` |

### Pipeline Operations

Chain operations into readable workflows. Each method has sync, `Task`, and `ValueTask` overloads.

| Operation | What it does |
|-----------|-------------|
| [`Bind`](xref:Trellis.BindExtensions) | Chain an operation that returns `Result` — the railway switch |
| [`Map`](xref:Trellis.MapExtensions) | Transform the success value |
| [`Tap`](xref:Trellis.TapExtensions) | Run a side effect without changing the result |
| [`Ensure`](xref:Trellis.EnsureExtensions) | Validate a business rule — fail if the predicate is false |
| [`Combine`](xref:Trellis.CombineExtensions) | Merge multiple results, collecting **all** errors |
| [`Check`](xref:Trellis.CheckExtensions) | Run a validation that returns `Result`, keep the original value on success |
| [`Match`](xref:Trellis.MatchExtensions) | Unwrap the result by handling both success and failure |
| [`RecoverOnFailure`](xref:Trellis.RecoverOnFailureExtensions) | Provide a fallback value on failure |
| [`MapOnFailure`](xref:Trellis.MapOnFailureExtensions) | Transform the value on the failure track |
| [`TapOnFailure`](xref:Trellis.TapOnFailureExtensions) | Run a side effect on the failure track |
| [`When`](xref:Trellis.WhenExtensions) | Execute conditionally based on a predicate |

> Learn the pipeline patterns: [Core Concepts](~/articles/basics.md) · [Error Handling](~/articles/error-handling.md) · [Advanced Features](~/articles/advanced-features.md)

---

## Domain-Driven Design

DDD building blocks for modeling your domain. Also in the `Trellis` namespace.

| Type | Purpose |
|------|---------|
| [`Aggregate<T>`](xref:Trellis.Aggregate`1) | Consistency boundary with a typed identity and domain events |
| [`Entity<T>`](xref:Trellis.Entity`1) | Domain object with a unique identity |
| [`ValueObject`](xref:Trellis.ValueObject) | Immutable object defined by its attributes, not its identity |
| [`Specification<T>`](xref:Trellis.Specification`1) | Composable business rule that can be combined with `And`, `Or`, `Not` |

> Learn more: [Aggregate Factory Pattern](~/articles/aggregate-factory-pattern.md) · [Specifications](~/articles/specifications.md)

---

## Value Objects

Base classes in the `Trellis` namespace; ready-to-use primitives in `Trellis.Primitives`.

| Base class | Wraps | Example |
|------------|-------|---------|
| [`RequiredString<TSelf>`](xref:Trellis.RequiredString`1) | Non-empty `string` | `FirstName`, `ProductCode` |
| [`RequiredGuid<TSelf>`](xref:Trellis.RequiredGuid`1) | Non-empty `Guid` | `OrderId`, `UserId` |
| [`RequiredInt<TSelf>`](xref:Trellis.RequiredInt`1) | Validated `int` | `Quantity`, `LineNumber` |
| [`RequiredDecimal<TSelf>`](xref:Trellis.RequiredDecimal`1) | Validated `decimal` | `Price`, `Weight` |
| [`RequiredEnum<TSelf>`](xref:Trellis.RequiredEnum`1) | Validated `enum` | `OrderStatus`, `Priority` |
| [`ScalarValueObject<TSelf, T>`](xref:Trellis.ScalarValueObject`2) | Any single primitive | Custom scalar values |

**Ready-to-use:** [`EmailAddress`](xref:Trellis.Primitives.EmailAddress), [`Money`](xref:Trellis.Primitives.Money), [`PhoneNumber`](xref:Trellis.Primitives.PhoneNumber), [`Url`](xref:Trellis.Primitives.Url), [`Slug`](xref:Trellis.Primitives.Slug), [`CountryCode`](xref:Trellis.Primitives.CountryCode), [`CurrencyCode`](xref:Trellis.Primitives.CurrencyCode), [`Percentage`](xref:Trellis.Primitives.Percentage), and [more](xref:Trellis.Primitives).

> Learn more: [Primitive Value Objects](~/articles/primitives.md)

---

## Integration Packages

Each integration has its own namespace and pulls in the relevant third-party dependency.

| Namespace | What it does | Guide |
|-----------|-------------|-------|
| [`Trellis.Asp`](xref:Trellis.Asp) | `Result<T>` → HTTP responses for MVC and Minimal APIs | [ASP.NET Core](~/articles/integration-aspnet.md) |
| [`Trellis.Asp.Authorization`](xref:Trellis.Asp.Authorization) | Claims, Entra ID, and development actor providers | [Authorization](~/articles/integration-asp-authorization.md) |
| [`Trellis.Authorization`](xref:Trellis.Authorization) | `Actor`, permission checks, resource authorization | [Authorization](~/articles/integration-asp-authorization.md) |
| [`Trellis.Http`](xref:Trellis.Http) | `HttpClient` extensions that stay inside the Result pipeline | [HTTP Client](~/articles/integration-http.md) |
| [`Trellis.FluentValidation`](xref:Trellis.FluentValidation) | FluentValidation output → Trellis `Result` | [FluentValidation](~/articles/integration-fluentvalidation.md) |
| [`Trellis.EntityFrameworkCore`](xref:Trellis.EntityFrameworkCore) | Value object converters, `Maybe<T>` queries, safe save | [EF Core](~/articles/integration-ef.md) |
| [`Trellis.Mediator`](xref:Trellis.Mediator) | Result-aware pipeline behaviors for Mediator | [Mediator](~/articles/integration-mediator.md) |
| [`Trellis.Stateless`](xref:Trellis.Stateless) | State machine transitions returning `Result<TState>` | [State Machines](~/articles/state-machines.md) |
| [`Trellis.Testing`](xref:Trellis.Testing) | FluentAssertions extensions for `Result<T>` and `Maybe<T>` | [Testing](~/articles/integration-testing.md) |

---

## Tooling

| Package | What it does |
|---------|-------------|
| [Trellis.Analyzers](https://www.nuget.org/packages/Trellis.Analyzers) | 19+ Roslyn analyzers that catch common mistakes at build time |
| [Trellis.Core.Generator](https://www.nuget.org/packages/Trellis.Core.Generator) | Source generation for `RequiredString` and related primitives |


> See all analyzer rules: [Analyzer Rules](~/articles/analyzers/toc.yml)

---

## Observability

Built-in OpenTelemetry tracing via [`ResultsTraceProviderBuilderExtensions`](xref:Trellis.ResultsTraceProviderBuilderExtensions) and [`PrimitiveValueObjectTraceProviderBuilderExtensions`](xref:Trellis.PrimitiveValueObjectTraceProviderBuilderExtensions). Automatic span creation for pipeline operations and value object creation boundaries.

> Learn more: [Observability & Monitoring](~/articles/integration-observability.md)

---

## Next Steps

| Goal | Where to go |
|------|-------------|
| Understand the concepts | [Introduction](~/articles/intro.md) |
| Learn the pipeline operations | [Core Concepts](~/articles/basics.md) |
| See it compared side-by-side | [With vs Without Trellis](~/articles/with-vs-without-trellis.md) |
| Browse working code | [Examples](~/articles/examples.md) |
| Set up integrations | [Integration Overview](~/articles/integration.md) |
| Run the benchmarks | [Performance](~/articles/performance.md) |

