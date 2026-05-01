---
title: Migrating from v1 to v2
package: Trellis (multiple)
topics: [migration, breaking-changes, v1-to-v2, result-unit, error-adt, package-renames]
related_api_reference: [trellis-api-core.md, trellis-api-asp.md, trellis-api-mediator.md, trellis-api-http.md, trellis-api-statemachine.md, trellis-api-analyzers.md]
last_verified: 2026-05-01
audience: [developer]
---
# Migrating from v1 to v2

A package- and namespace-rename combined with a tightened public surface. Per-package "Breaking changes from v1" sections in `api_reference/` are the authoritative source of truth; this guide is the cross-cutting index and the recommended migration order.

## Patterns Index

| Old API / artifact | New API / artifact | See |
|---|---|---|
| `Result.Success(...)` / `Result.Failure(...)` | `Result.Ok(...)` / `Result.Fail(...)` | [Result and Error renames](#result-and-error-renames-trelliscore) |
| Implicit `T → Result<T>` / `Error → Result<T>` | Explicit `Result.Ok(value)` / `Result.Fail<T>(error)` | [Result and Error renames](#result-and-error-renames-trelliscore) |
| `Result.SuccessIf` / `Result.FailureIf` / `*Async` variants | Inline ternary | [Removed factories](#removed-factories) |
| `Result.FromException(ex)` | `Result.Try` / `Result.TryAsync` or `new Error.InternalServerError(...)` | [Removed factories](#removed-factories) |
| Non-generic `Result` instance type | `Result<Unit>` (ADR-005) | [Non-generic Result removed (ADR-005)](#non-generic-result-removed-adr-005) |
| `result.Value` getter | `TryGetValue` / `Match` / `var (ok, v, err) = result;` | [Accessor changes](#accessor-changes) |
| `Error` open class hierarchy + `Error.X("msg")` factories | `Error` closed ADT + `new Error.X(payload) { Detail = "msg" }` | [Error becomes a closed ADT](#error-becomes-a-closed-adt) |
| `MatchErrorExtensions.MatchError(...)` | `Match(...)` + `switch` over the closed ADT | [Removed extensions](#removed-extensions) |
| `FlattenValidationErrorsExtensions` | `Combine` (auto-merges `Error.UnprocessableContent.Fields` / `.Rules`) | [Removed extensions](#removed-extensions) |
| `Error.Instance` field | Synthesized by ASP wire layer from request URL + `ResourceRef` | [Removed extensions](#removed-extensions) |
| `Trellis.Asp.WriteOutcome<T>` | `Trellis.WriteOutcome<T>` (in `Trellis.Core`) | [ASP.NET Core (Trellis.Asp)](#aspnet-core-trellisasp) |
| `Trellis.Stateless` package + namespace | `Trellis.StateMachine` package + namespace | [State machine (Trellis.StateMachine)](#state-machine-trellisstatemachine) |
| `ReadResultFromJsonAsync<T>` | `ReadJsonAsync<T>` | [HTTP (Trellis.Http)](#http-trellishttp) |
| `ReadResultMaybeFromJsonAsync<T>` | `ReadJsonMaybeAsync<T>` | [HTTP (Trellis.Http)](#http-trellishttp) |
| `HandleForbidden*` / `HandleClientError*` / `HandleServerError*` / `EnsureSuccess*` / `HandleFailureAsync<TContext>` | `ToResultAsync(statusMap)` or body-aware `ToResultAsync(mapper, ct)` | [HTTP (Trellis.Http)](#http-trellishttp) |
| Sync HTTP receivers (`HttpResponseMessage` / `Result<HRM>`) | Async-only canonical chain | [HTTP (Trellis.Http)](#http-trellishttp) |
| `Trellis.Results` package | `Trellis.Core` package (CLR namespace unchanged) | [Package map](#package-map-legacy--current) |
| `Trellis.DomainDrivenDesign` package | Folded into `Trellis.Core` | [Package map](#package-map-legacy--current) |
| `Trellis.Primitives.Generator` package | Bundled in `Trellis.Core.nupkg` | [Package map](#package-map-legacy--current) |
| `Trellis.AspSourceGenerator` package | Bundled in `Trellis.Asp.nupkg` | [Package map](#package-map-legacy--current) |
| `Trellis.EntityFrameworkCore.Generator` package | Bundled in `Trellis.EntityFrameworkCore.nupkg` | [Package map](#package-map-legacy--current) |
| `Trellis.Asp.Authorization` package | Folded into `Trellis.Asp.nupkg` (namespace unchanged) | [Package map](#package-map-legacy--current) |
| OpenTelemetry source `"Trellis.Results"` | `"Trellis.Core"` (`RopTrace.ActivitySourceName`) | [Observability](#observability) |
| Analyzer IDs `TRLSGEN001`..`TRLSGEN103` | `TRLS031`..`TRLS038` | [Analyzer ID renames](#analyzer-id-renames) |

## Use this guide when

- You are upgrading a service from a v1 `Trellis.*` surface (`Trellis.Results`, `Trellis.DomainDrivenDesign`, `Trellis.Stateless`, `Trellis.Asp.Authorization`, `Trellis.AspSourceGenerator`, `Trellis.EntityFrameworkCore.Generator`, `Trellis.Primitives.Generator`) to the consolidated v2 packages.
- You hit `CS0029` after pulling v2 because implicit `T → Result<T>` and `Error → Result<T>` operators were removed.
- You hit `CS1061` reading `result.Value` — the throwing getter was deleted.
- You hit `CS0117` calling `Result.Success(...)` / `Result.Failure(...)` / `Result.SuccessIf(...)` / `Result.FromException(...)`.
- Your handlers return `Task<Result>` and you need to migrate to `Task<Result<Unit>>` per ADR-005.
- You consumed the v1 `Trellis.Http` surface (60+ overloads) and need the canonical seven-method shape.

## Surface at a glance

| Category | What changed | Authoritative diff |
|---|---|---|
| Result factories | `Success`/`Failure` renamed to `Ok`/`Fail`; deferred / conditional / exception factories removed | [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1) |
| Result accessors | `.Value` getter removed; `.Error` is `Error?` and never throws | [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1) |
| Implicit conversions | Removed on `Result<T>`; explicit factory required | [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1) |
| Non-generic `Result` instance type | Removed; use `Result<Unit>`. `Result` is now a static factory class only. | [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1), [ADR-005](../adr/ADR-005-reintroduce-unit.md) |
| `Error` model | Open `class` + 18 subclasses + static factories → `abstract record` with 20 nested `sealed record` cases | [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1) |
| Removed extensions | `MatchError`, `FlattenValidationErrors`, `Error.Instance` field | [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1) |
| Package merges | DDD, Primitives generator, Asp source generator, EF Core generator, Asp authorization | [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1) |
| `WriteOutcome<T>` move | `Trellis.Asp.WriteOutcome<T>` → `Trellis.WriteOutcome<T>` (in `Trellis.Core`) | [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1) |
| Test helper namespace | `Trellis.Results.Tests.*` → `Trellis.Core.Tests.*` | [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1) |
| OTel `ActivitySource` name | `"Trellis.Results"` → `"Trellis.Core"` | [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1) |
| HTTP surface | 60+ overloads collapsed to one static class with seven methods; all sync removed; new disposal contract | [`trellis-api-http.md` → Breaking changes from v1](../api_reference/trellis-api-http.md#breaking-changes-from-v1) |
| State machine | Package and namespace renamed `Trellis.Stateless` → `Trellis.StateMachine`; public surface otherwise identical | [`trellis-api-statemachine.md` → Breaking changes from v1](../api_reference/trellis-api-statemachine.md#breaking-changes-from-v1) |
| Analyzer IDs | `TRLSGEN001`–`TRLSGEN103` renamed to `TRLS031`–`TRLS038` | [`trellis-api-analyzers.md`](../api_reference/trellis-api-analyzers.md) |

> [!TIP]
> Start with package and namespace rewrites, then run a build. The compiler will surface most remaining work via `CS0029`, `CS0117`, `CS1061`, and `CS1593`.

## Result and Error renames (Trellis.Core)

The full row-by-row diff (with migration notes) lives in [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1). Headlines below.

### Renamed factories

| v1 | v2 |
|---|---|
| `Result.Success(value)` / `Result.Success<T>(...)` / `Result.Success()` | `Result.Ok(value)` / `Result.Ok<T>(...)` / `Result.Ok()` |
| `Result.Failure<T>(error)` / `Result.Failure(error)` | `Result.Fail<T>(error)` / `Result.Fail(error)` |

`IsSuccess` / `IsFailure` are **not** renamed — predicates read as questions and stay long-form.

### Removed factories

`Result.Success(Func<T>)`, `Result.Failure<T>(Func<Error>)`, `Result.SuccessIf`, `Result.FailureIf`, `Result.SuccessIfAsync`, `Result.FailureIfAsync`, `Result.FromException` / `Result.FromException<T>` were removed. Migration patterns:

```csharp
// Deferred factory: inline the call
Result.Ok(funcOk());
Result.Fail<T>(errorFactory());

// Conditional: use a ternary
return cond ? Result.Ok(value) : Result.Fail<T>(error);

// Async conditional: parens are required because await binds tighter than ?:
return (await predicate()) ? Result.Ok(value) : Result.Fail<T>(error);

// Exception → result: use Try / TryAsync, or build the error explicitly
return Result.Try(() => DoWork());
return Result.Fail<T>(new Error.InternalServerError(faultId) { Detail = ex.Message, Cause = ex });
```

`OperationCanceledException` is always rethrown by `Try` / `TryAsync` rather than mapped.

### Implicit conversions removed

```csharp
// v1 (compiles)
Result<int> r = 5;
Result<int> r = error;

// v2 (CS0029) — use the explicit factory
Result<int> r = Result.Ok(5);
Result<int> r = Result.Fail<int>(error);
```

The compiler flags every site with `CS0029`.

### Accessor changes

`Result<T>.Value` is gone. `Result<T>.Error` stays but is now `Error?` and never throws.

```csharp
// v2 — extract the value
if (result.TryGetValue(out var v)) { /* use v */ }
result.Match(onSuccess: v => ..., onFailure: e => ...);
var (ok, v, err) = result;            // Deconstruct

// v2 — read the error (never throws)
if (result.Error is { } error) { /* use error */ }
result.TryGetError(out var err);
```

`Maybe<T>.Value` still exists but is hidden from IntelliSense and gated by analyzer `TRLS003` — guard with `HasValue` / `TryGetValue` / `Match` / `GetValueOrDefault`. The two types do not have symmetric value-access ergonomics.

### Error becomes a closed ADT

v1 `Error` was a `class` with 18 hand-written subclasses (`ValidationError`, `NotFoundError`, …) and static factory helpers (`Error.Validation(...)`, `Error.NotFound(...)`). <!-- v1-stale-ok -->

v2 `Error` is an `abstract record` with **20 nested `sealed record` cases** (`Error.NotFound`, `Error.UnprocessableContent`, …). The base constructor is `private` so the catalog is closed; there are no static factories.

```csharp
// v1
return Result.Failure<Order>(Error.NotFound("Order missing")); // v1-stale-ok

// v2
return Result.Fail<Order>(new Error.NotFound(ResourceRef.For<Order>(id)) { Detail = "Order missing" });
```

C# verifies exhaustiveness against the closed catalog when you `switch` on the cases.

### Removed extensions

| v1 | v2 replacement |
|---|---|
| `result.MatchError(onValidation: ..., onNotFound: ..., onUnexpected: ...)` | `result.Match(_ => ..., e => e switch { Error.NotFound nf => ..., Error.UnprocessableContent uc => ..., _ => ... })` |
| `result.FlattenValidationErrors()` | `Result.Combine(...)` automatically merges `Error.UnprocessableContent.Fields` and `.Rules` |
| `error.Instance` field | Synthesized by the ASP wire layer from request URL + any `ResourceRef` carried by the typed payload |

### Non-generic Result removed (ADR-005)

The non-generic `Result` instance type (peer to `Result<T>`) was removed. `Result` is now a `public static partial class` factory only. For no-payload success/failure, use `Result<Unit>` — returned by parameterless `Result.Ok()`, `Result.Fail(error)`, `Result.Ensure(...)`, `Result.Try(...)` factories. `Trellis.Unit` is a public `readonly record struct` with a single value (`Unit.Default`).

```csharp
// v1
public async ValueTask<Result> Handle(SubmitOrderCommand cmd, CancellationToken ct) { ... }

// v2 (ADR-005)
public async ValueTask<Result<Unit>> Handle(SubmitOrderCommand cmd, CancellationToken ct) { ... }
```

In lambdas after `.Bind(...)` / `BindAsync(...)`, accept the `Unit` argument explicitly: `_ =>` or `(Unit _) =>`. `AsUnit()` on `Result<T>` now returns `Result<Unit>` (it bridges value-bearing chains back to a no-payload terminal without crossing a type boundary). Background and trade-off analysis: [ADR-005](../adr/ADR-005-reintroduce-unit.md).

> [!IMPORTANT]
> `default(Result<T>)` is a **failure** carrying `new Error.Unexpected("default_initialized")`. Always construct via `Result.Ok(...)` / `Result.Fail(...)`. Analyzer `TRLS019` flags explicit `default(Result<T>)` at call sites.

## HTTP (Trellis.Http)

The v1 surface (60+ overloads across two static classes) collapsed to one static class with seven methods. There are no shims or compatibility redirects — this is a clean, pre-GA cut.

| v1 | v2 |
|---|---|
| `ReadResultFromJsonAsync<T>` (sync, `Result<HRM>`, `Task<HRM>`, `Task<Result<HRM>>`) | `ReadJsonAsync<T>(this Task<Result<HttpResponseMessage>>, JsonTypeInfo<T>, CancellationToken)` |
| `ReadResultMaybeFromJsonAsync<T>` (all shapes) | `ReadJsonMaybeAsync<T>(this Task<Result<HttpResponseMessage>>, JsonTypeInfo<T>, CancellationToken)` |
| `HandleNotFound` / `HandleConflict` / `HandleUnauthorized` (all shapes) | `Handle{NotFound,Conflict,Unauthorized}Async(this Task<HttpResponseMessage>, Error.{NotFound,Conflict,Unauthorized})` |
| `HandleForbidden*` | **Removed.** Use `ToResultAsync(status => status == HttpStatusCode.Forbidden ? new Error.Forbidden(...) : null)`. |
| `HandleClientError*` (4xx) / `HandleServerError*` (5xx) | **Removed.** Use `ToResultAsync(statusMap)` with a `switch` over `HttpStatusCode`. |
| `EnsureSuccess` / `EnsureSuccessAsync` (all shapes) | **Removed.** Use `ToResultAsync(status => (int)status >= 400 ? error : null)` or body-aware `ToResultAsync(mapper, ct)`. |
| `HandleFailureAsync<TContext>` | **Removed.** Use body-aware `ToResultAsync(mapper, ct)`; capture additional state via closure. |
| Sync receivers (`HttpResponseMessage`, `Result<HRM>`) | **Removed.** Wrap with `Task.FromResult(...)` if needed; in practice every `HttpClient` call is already async. |

Plus a new disposal contract: `Trellis.Http` disposes the underlying `HttpResponseMessage` on terminal and transformative paths; pass-through paths leave disposal to the caller until the chain reaches `ReadJson*`.

Full table and explanations: [`trellis-api-http.md` → Breaking changes from v1](../api_reference/trellis-api-http.md#breaking-changes-from-v1). Practical recipes: [`integration-http.md`](integration-http.md).

## State machine (Trellis.StateMachine)

```diff
- <PackageReference Include="Trellis.Stateless" Version="..." />
+ <PackageReference Include="Trellis.StateMachine" Version="..." />

- using Trellis.Stateless;
+ using Trellis.StateMachine;
```

The public surface is otherwise identical — `StateMachineExtensions.FireResult<TState, TTrigger>(...)` and `LazyStateMachine<TState, TTrigger>` are unchanged. The underlying [Stateless](https://github.com/dotnet-state-machine/stateless) library is still referenced directly, so `StateMachine<TState, TTrigger>` from the `Stateless` namespace remains visible in user code. There is no metapackage redirect — update the `PackageReference` directly. Full notes: [`trellis-api-statemachine.md` → Breaking changes from v1](../api_reference/trellis-api-statemachine.md#breaking-changes-from-v1).

## ASP.NET Core (Trellis.Asp)

Two cross-cutting changes affect ASP consumers:

- **`WriteOutcome<T>` moved to `Trellis.Core`.** The type, its case records, and member shapes are unchanged; only the assembly and namespace move. Replace `using Trellis.Asp;` with `using Trellis;` for any file that names `WriteOutcome<T>` directly. ASP-specific HTTP mapping stays in `Trellis.Asp` via `ToHttpResponse(...)` / `ToHttpResponseAsync(...)` and the typed MVC adapters `AsActionResult<T>()` / `AsActionResultAsync<T>()`.
- **`Trellis.Asp.Authorization` package was folded into `Trellis.Asp.nupkg`.** The actor providers (`ClaimsActorProvider`, `EntraActorProvider`, `DevelopmentActorProvider`, `CachingActorProvider`) and the `AddTrellisAspAuthorization()` extension are unchanged; the namespace stays `Trellis.Asp.Authorization`. Drop the standalone `PackageReference`. `Trellis.Asp` now transitively brings in `Trellis.Authorization`.

Both rows are documented in [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1) (the `WriteOutcome` move and the package-merge entries). The current ASP API surface lives in [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md).

## Mediator (Trellis.Mediator)

Mediator does not have its own v1 breaking-changes section; the migration impact is downstream of two `Trellis.Core` changes:

- Handlers that returned `Task<Result>` now return `Task<Result<Unit>>` (ADR-005). Update handler signatures and adjust trailing `_ =>` lambdas.
- Pipeline behaviors are constrained by `IFailureFactory<TResponse>`. With `Result<Unit>` as the canonical no-payload response, the constraint is satisfied without any new shape.

Behavioral semantics, registration helpers, and the validation-aggregation rule are documented in [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md).

## Package map (legacy → current)

| Legacy package | Current package | Notes |
|---|---|---|
| `Trellis.Results` | `Trellis.Core` | CLR namespace stays `Trellis` — no `using` changes. Legacy package is unlisted; no metapackage shim. |
| `Trellis.DomainDrivenDesign` | _(removed — merged into `Trellis.Core`)_ | DDD types (`Aggregate<T>`, `Entity<T>`, `ValueObject`, `Specification<T>`) moved into `Trellis.Core`. Namespace `Trellis` unchanged. |
| `Trellis.Primitives.Generator` | _(removed — bundled in `Trellis.Core.nupkg`)_ | Source generator now ships at `analyzers/dotnet/cs/Trellis.Core.Generator.dll`. |
| `Trellis.AspSourceGenerator` | _(removed — bundled in `Trellis.Asp.nupkg`)_ | Generator ships at `analyzers/dotnet/cs/Trellis.AspSourceGenerator.dll`. |
| `Trellis.EntityFrameworkCore.Generator` | _(removed — bundled in `Trellis.EntityFrameworkCore.nupkg`)_ | Generator ships at `analyzers/dotnet/cs/Trellis.EntityFrameworkCore.Generator.dll`. |
| `Trellis.Asp.Authorization` | _(removed — folded into `Trellis.Asp.nupkg`)_ | Namespace `Trellis.Asp.Authorization` unchanged; actor providers and `AddTrellisAspAuthorization()` are unchanged. |
| `Trellis.Stateless` | `Trellis.StateMachine` | Namespace also renamed; public surface unchanged. |

Authoritative diff (with `<PackageReference>` snippets): [`trellis-api-core.md` → Breaking changes from v1](../api_reference/trellis-api-core.md#breaking-changes-from-v1).

> [!NOTE]
> Earlier predecessors (`FunctionalDdd.RailwayOrientedProgramming`, `FunctionalDdd.DomainDrivenDesign`, `FunctionalDdd.PrimitiveValueObjects`, `FunctionalDdd.Asp`, `FunctionalDdd.Http`, `FunctionalDdd.FluentValidation`, `FunctionalDdd.PrimitiveValueObjectGenerator`) are not part of the v1 → v2 cut and are not documented in the api_reference breaking-changes sections. Treat them as out of scope; rename to the matching v2 package and then apply this guide.

## Observability

Update OpenTelemetry subscriptions when you upgrade:

```csharp
// v1
builder.AddSource("Trellis.Results");

// v2
builder.AddSource("Trellis.Core");
// or, programmatically:
builder.AddSource(RopTrace.ActivitySourceName);
```

The OTel extension method names (`AddResultsInstrumentation()`, `AddPrimitiveValueObjectInstrumentation()`) are unchanged. See [`integration-observability.md`](integration-observability.md) for tracing setup and [`debugging.md`](debugging.md) for ROP-trace forensics.

## Analyzer ID renames

The Primitives and EF Core source-generator diagnostics were renumbered into the main `TRLS` range:

| v1 ID | v2 ID |
|---|---|
| `TRLSGEN001` | `TRLS031` |
| `TRLSGEN002` | `TRLS032` |
| `TRLSGEN003` | `TRLS033` |
| `TRLSGEN004` | `TRLS034` |
| `TRLSGEN100` | `TRLS035` |
| `TRLSGEN101` | `TRLS036` |
| `TRLSGEN102` | `TRLS037` |
| `TRLSGEN103` | `TRLS038` |

Update any `<NoWarn>` / `#pragma warning disable` / editorconfig severity overrides accordingly. Full diagnostic catalog: [`trellis-api-analyzers.md`](../api_reference/trellis-api-analyzers.md).

## Practical guidance

Recommended order — each step is small enough that the build should succeed before the next:

1. **Pin a green baseline.** Tag the v1 commit and confirm `dotnet build` and `dotnet test` are clean.
2. **Update `PackageReference` / `Directory.Packages.props`.** Apply the [Package map](#package-map-legacy--current). Drop generator packages that are now bundled. Add `Trellis.StateMachine` if you used `Trellis.Stateless`.
3. **Mechanical rename of factories.** `Result.Success` → `Result.Ok`; `Result.Failure` → `Result.Fail`. Find-and-replace is safe because `IsSuccess` / `IsFailure` are unchanged and not affected.
4. **Replace `Result` returns and parameters with `Result<Unit>` (ADR-005).** Including `Task<Result>` → `Task<Result<Unit>>`. Add `_ =>` or `(Unit _) =>` to lambdas after `.Bind`/`.BindAsync`/`.Tap`/etc.
5. **Convert `Error.X("msg")` factory calls to constructor + `with` syntax.** `new Error.X(payload) { Detail = "msg" }`. Replace concrete subclass type names (`ValidationError`, `NotFoundError`) with the closed cases (`Error.UnprocessableContent`, `Error.NotFound`).
6. **Replace `result.Value` reads.** Use `TryGetValue`, `Match`, or deconstruction. Replace `result.Error` reads with `if (result.Error is { } e)` or `result.TryGetError(out var e)`.
7. **Remove `MatchError` / `FlattenValidationErrors` calls.** `MatchError` → `Match` + `switch`. `FlattenValidationErrors` is no-op — `Combine` already merges field/rule violations.
8. **Audit HTTP call sites.** Replace `EnsureSuccess*` / `HandleClientError*` / `HandleServerError*` / `HandleForbidden*` / `HandleFailureAsync<TContext>` with `ToResultAsync(statusMap)` or body-aware `ToResultAsync(mapper, ct)`. Rename `ReadResultFromJsonAsync` / `ReadResultMaybeFromJsonAsync` to `ReadJsonAsync` / `ReadJsonMaybeAsync`. Stop disposing `HttpResponseMessage` after the chain reaches `ReadJson*` — `Trellis.Http` owns it.
9. **Update OTel sources.** `"Trellis.Results"` → `"Trellis.Core"` (or `RopTrace.ActivitySourceName`).
10. **Update analyzer suppressions.** Apply the `TRLSGEN*` → `TRLS0xx` map.
11. **Build, run tests, and iterate.** The compiler errors (`CS0029`, `CS0117`, `CS1061`, `CS1593`) are deliberately the migration map — work through them top-down.
12. **Add `Trellis.Analyzers`** if you want the compiler to enforce current patterns (notably `TRLS003` on `Maybe<T>.Value` and `TRLS019` on `default(Result<T>)`).

> [!TIP]
> There are no shims or compatibility redirects — this is a clean pre-GA cut. The compiler is the migration script.

## Cross-references

- Result / Error / `Unit` semantics: [`trellis-api-core.md`](../api_reference/trellis-api-core.md), [ADR-005](../adr/ADR-005-reintroduce-unit.md)
- HTTP migration table (full): [`trellis-api-http.md` → Breaking changes from v1](../api_reference/trellis-api-http.md#breaking-changes-from-v1)
- HTTP usage recipes: [`integration-http.md`](integration-http.md)
- State machine package/namespace rename: [`trellis-api-statemachine.md` → Breaking changes from v1](../api_reference/trellis-api-statemachine.md#breaking-changes-from-v1)
- ASP.NET Core surface (post-merge): [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md)
- Mediator pipeline behaviors: [`trellis-api-mediator.md`](../api_reference/trellis-api-mediator.md)
- Analyzer catalog: [`trellis-api-analyzers.md`](../api_reference/trellis-api-analyzers.md)
- OTel wiring: [`integration-observability.md`](integration-observability.md)
- Recipe lookup table: [`trellis-api-cookbook.md`](../api_reference/trellis-api-cookbook.md)
