---
package: Trellis.Core
namespaces: [Trellis]
types: [Result, Result<T>, Maybe<T>, Error, Unit, Page<T>, Cursor, EquatableArray<T>, ResourceRef, EntityTagValue, RetryAfterValue, PreconditionKind, InputPointer, FieldViolation, RuleViolation, AuthChallenge, Aggregate<TId>, Entity<TId>, IDomainEvent, ScalarValueObject<TSelf,T>, Specification<T>, RepresentationMetadata]
version: v3
last_verified: 2026-05-01
audience: [llm]
---
# Trellis.Core API Reference

**Package:** `Trellis.Core`  
**Namespace:** `Trellis`  
**Purpose:** Provides Trellis result, maybe, scalar-value, and HTTP-oriented error primitives for railway-oriented application flows.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package, [trellis-api-asp.md](trellis-api-asp.md), [trellis-api-primitives.md](trellis-api-primitives.md).

---

## Use this file when

- You need exact signatures for `Result`, `Result<T>`, `Maybe<T>`, `Error`, `WriteOutcome<T>`, `Page<T>`, DDD primitives, specifications, or custom primitive base classes.
- You are composing domain/application flows and need the canonical ROP operation: `Bind`, `Map`, `Tap`, `Ensure`, `Combine`, `ParallelAsync`, `AsTask`, or `AsValueTask`.
- You are defining aggregates, entities, domain events, specifications, or source-generated `Required*<TSelf>` value objects.

## Patterns Index

Use this table before searching the long type catalog.

| Goal | Canonical API | See |
|---|---|---|
| Return success/failure without payload | `Result.Ok()` / `Result.Fail(error)` (returns `Result<Unit>`) | [`Result`](#public-static-partial-class-result) |
| Return success/failure with payload | `Result.Ok(value)` / `Result.Fail<T>(error)` | [`Result<TValue>`](#public-readonly-struct-resulttvalue--iresulttvalue-iequatableresulttvalue-ifailurefactoryresulttvalue) |
| Turn a boolean guard into a result | `Result.Ensure(condition, error)` or `.Ensure(...)` in a chain | [`Result`](#public-static-partial-class-result), [`Ensure family`](#ensure-family--ensureextensions-ensureextensionsasync-ensureallextensions-ensureallextensionsasync) |
| Start independent async result-producing operations concurrently | `Result.ParallelAsync(...)`, then combine the returned tasks | [`Result`](#public-static-partial-class-result) |
| Combine multiple validated *typed* fields into a tuple | static `Result.Combine<T1,T2>(Result<T1>, Result<T2>)` or instance `r1.Combine(r2)`, then `.Map(...)` | [`Combine family`](#combine-family--combineextensions-combineextensionsasync-combineerrorextensions) |
| Combine multiple boolean guards | `Result.Ensure(...).Combine(Result.Ensure(...))` then `.Bind(...)` (extension `Combine` aggregates errors and adds each value as the next tuple element; pass a `Result<Unit>` from a no-payload guard and ignore it with `_` in the next lambda) | [`Combine family`](#combine-family--combineextensions-combineextensionsasync-combineerrorextensions) |
| Adapt an already-computed result to async APIs | `.AsTask()` / `.AsValueTask()` | [`ResultTaskAdapterExtensions`](#task-adapter-family--resulttaskadapterextensions) |
| Model expected absence | `Maybe<T>`, `Maybe.Some(value)`, `Maybe<T>.None` | [`Maybe<T>`](#public-readonly-struct-maybet-where-t--notnull) |
| Convert absence to a domain failure | `maybe.ToResult(error)` / `maybe.ToResult(errorFactory)` | [`MaybeExtensions`](#maybeextensions) |
| Create HTTP-oriented domain errors | Closed `Error` cases plus `ResourceRef.For<TResource>(id)` | [`Error`](#public-abstract-record-error), [`Error Cases`](#error-cases-closed-adt) |
| Page list responses | `new Page<T>(items, next, previous, requestedLimit, appliedLimit)` (or `Page.Empty<T>(...)` when there are no items), `Cursor` | [`Pagination`](#pagination) |
| Model aggregates/entities/events | `Aggregate<TId>`, `Entity<TId>`, `IDomainEvent` | [`Domain-Driven Design`](#domain-driven-design) |
| Move reusable query predicates out of repositories | `Specification<T>` | [`Specification<T>`](#specificationt) |
| Define custom required value objects | `partial class X : RequiredString<X>` / `RequiredGuid<X>` / other `Required*` bases | [`Primitive value object base classes`](#primitive-value-object-base-classes) |

## Canonical async handler skeleton

Every async command/query handler that composes Trellis primitives follows the same await-then-chain shape. **The sync verbs (`Bind`/`Map`/`Ensure`) extend `Result<T>` receivers with sync delegates only. The async verbs (`BindAsync`/`MapAsync`/`EnsureAsync`) extend `Result<T>`, `Task<Result<T>>`, *and* `ValueTask<Result<T>>` receivers; on a sync receiver they take an async delegate (`Task<...>` or `ValueTask<...>`), while on a `Task`/`ValueTask` receiver they additionally provide sync-delegate convenience overloads.** A `Task<Result<T>>` is *not* a `Result<T>` and does not expose the sync extensions — calling `.Bind(...)` on a `Task<Result<T>>` fails with `CS1929: 'Task<Result<T>>' does not contain a definition for 'Bind'`.

```csharp
// Generic handler — Task<Result<TOut>>
public async Task<Result<OrderResponse>> Handle(CreateDraftOrderCommand cmd, CancellationToken ct)
{
    // 1. Sync precondition — produces a Result<Unit>, chains synchronously.
    var preconditions = Result.Ensure(cmd.LineItems.Count > 0,
                            Error.UnprocessableContent.ForField("lineItems", "required", "..."))
                        .Bind(_ => Result.Ensure(!cmd.HasDuplicates,
                            Error.UnprocessableContent.ForField("lineItems", "duplicate_product", "...")));

    if (preconditions.IsFailure) return Result.Fail<OrderResponse>(preconditions.Error);

    // 2. Async precondition — returns Task<Result<T>>; await BEFORE chaining sync extensions,
    //    or use the *Async siblings (BindAsync/EnsureAsync/MapAsync) which extend Task<Result<T>>.
    return await LoadCustomerAsync(cmd.CustomerId, ct)                  // Task<Result<Customer>>
        .EnsureAsync(c => c.IsActive,
            c => new Error.Forbidden("customer.inactive",
                ResourceRef.For<Customer>(c.Id)))                        // Task<Result<Customer>>
        .BindAsync(c => LoadProductsAsync(cmd.LineItems, ct))           // Task<Result<IReadOnlyList<Product>>>
        .MapAsync(products => Order.CreateDraft(cmd, products))         // Task<Result<Order>>
        .MapAsync(order => OrderResponse.From(order));                  // Task<Result<OrderResponse>>
}

// No-payload handler — Task<Result<Unit>>
public Task<Result<Unit>> Handle(CancelOrderCommand cmd, CancellationToken ct) =>
    LoadOrderAsync(cmd.OrderId, ct)                                     // Task<Result<Order>>
        .BindAsync(order => order.Cancel())                             // Task<Result<Unit>> (Cancel returns Result<Unit>)
        .BindAsync(_ => _uow.SaveChangesResultAsync(ct));               // Task<Result<Unit>>
```

**Common build failures and their fix:**

| Diagnostic | What the model wrote | Fix |
| --- | --- | --- |
| `CS1929: 'Task<Result<T>>' does not contain a definition for 'Bind'` | `LoadAsync(...).Bind(x => ...)` | Use `BindAsync` (which extends `Task<Result<T>>`), or `await` first then `.Bind(...)` |
| `CS0411: type arguments for 'Map<TOut>' cannot be inferred` | `someTaskResult.Map(...)` after a `CheckAsync` whose result type can't be inferred | `await` the precondition into a concrete `Result<T>` before projecting; or use `MapAsync<TOut>(...)` |
| `CS0121: ambiguous between 'BindAsync<...>(Result<T>, Func<T, Task<Result<R>>>)' and 'BindAsync<...>(Result<T>, Func<T, ValueTask<Result<R>>>)'` | A sync `Result<T>` (not `Task<Result<T>>`) receiver with an inline `async` lambda whose return type can't be inferred between `Task` and `ValueTask` — both delegate-shape overloads exist on the sync receiver. | Either extract the lambda to a named method with an explicit `Task<Result<R>>` return type, or pin the delegate type at the call site: `Func<T, Task<Result<R>>> next = c => ...; result.BindAsync(next);`. See [`Bind family — Task vs ValueTask ambiguity`](#bind-family--bindextensions-bindextensionsasync-bindzipextensions-bindzipextensionsasync) |


## Common traps

- Do not use throwing value access in production code. Prefer `TryGetValue`, `Match`, `Bind`, `Map`, or deconstruction guarded by the success flag.
- Do not use `default(Result<T>)` as success. The default state is a typed `new Error.Unexpected("default_initialized")` failure.
- For no-payload success, use `Result.Ok()` (which returns `Result<Unit>`). The `Trellis.Unit` type is the canonical "no value" payload — it is a public `readonly record struct` with a single value (`Unit.Default`).
- Use `ParallelAsync` only for independent work. If operation B depends on operation A, compose with `Bind`/`BindAsync` instead.

### First-30-minutes surprises

| Surprise | What it actually is |
|---|---|
| `Result<T>.Value` getter does not exist (`CS1061`) | Removed from the current API because it was the primary cause of unsafe value access. Extract via `TryGetValue(out var v)`, `Match(...)`, deconstruction `var (ok, v, err) = result;`, or chain with `Bind`/`Map`. `Maybe<T>.Value` *does* exist but is hidden from IntelliSense and gated by analyzer `TRLS003` — guard with `HasValue`/`TryGetValue`/`Match`/`GetValueOrDefault`. The two types do not have symmetric value-access ergonomics. |
| Implicit `T → Result<T>` and `Error → Result<T>` were removed (`CS0029`) | Use the explicit factory: `return Result.Ok(value);` and `return Result.Fail<T>(error);`. The C# compiler flags every site with `CS0029: cannot implicitly convert type 'T' to 'Result<T>'`. |
| `Aggregate<TId>` / `Entity<TId>` already provide `CreatedAt` and `LastModified` (`CS0108`) | Both are `DateTimeOffset` (not `DateTime`) and infrastructure-managed by Trellis EF Core. Defining your own `public DateTime CreatedAt { ... }` on the aggregate triggers `CS0108: 'X.CreatedAt' hides inherited member 'Entity<TId>.CreatedAt'`. If your spec calls for an audit timestamp, use the inherited base property instead of declaring a new one. |
| `Result<T>` has `.Error` (nullable, never throws) but **not** `.Value` | `result.Error` returns `Error?` (null on success). `result.TryGetError(out var err)` is the safe Boolean form. Reading the success value still requires `TryGetValue`/`Match`/destructuring. |

## Breaking changes from v1

Migration notes for users moving from the previous `Trellis.Core` API surface.

| Change | Previous API | Current API | Migration |
|---|---|---|---|
| Result success factory | `Result.Success(value)` / `Result.Success<T>(...)` / `Result.Success()` | `Result.Ok(value)` / `Result.Ok<T>(...)` / `Result.Ok()` | Mechanical find-and-replace of `Result.Success` → `Result.Ok` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Result failure factory | `Result.Failure<T>(error)` / `Result.Failure(error)` | `Result.Fail<T>(error)` / `Result.Fail(error)` | Mechanical find-and-replace of `Result.Failure` → `Result.Fail` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Deferred success factory | `Result.Success(Func<T> funcOk)` | *(removed)* | Inline the factory: `Result.Ok(funcOk())` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Deferred failure factory | `Result.Failure<T>(Func<Error> errorFactory)` | *(removed)* | Inline the factory: `Result.Fail<T>(errorFactory())` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Conditional factory | `Result.SuccessIf(cond, value, error)` / `Result.SuccessIf(cond, t1, t2, error)` | *(removed)* | Use a ternary: `cond ? Result.Ok(value) : Result.Fail<T>(error)` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Inverse-conditional factory | `Result.FailureIf(cond, value, error)` / `Result.FailureIf(predicate, value, error)` | *(removed)* | Use a ternary: `cond ? Result.Fail<T>(error) : Result.Ok(value)` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Async-conditional factories | `Result.SuccessIfAsync(predicate, value, error)` / `Result.FailureIfAsync(predicate, value, error)` | *(removed)* | `(await predicate()) ? Result.Ok(value) : Result.Fail<T>(error)` (invert as needed; parens required because `await` binds tighter than `?:`) | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Exception → result helpers | `Result.FromException(ex)` / `Result.FromException<T>(ex)` | *(removed)* | Use `Result.Fail(new Error.InternalServerError(faultId) { Detail = ex.Message, Cause = ... })` or rely on `Result.Try` / `Result.TryAsync` for inline exception capture. |
| Implicit operators on `Result<T>` | `Result<T> r = value;` and `Result<T> r = error;` | *(removed)* | Use the explicit factory: `Result.Ok(value)` / `Result.Fail<T>(error)`. The compiler flags every site with CS0029. |
| Non-generic `Result` for void flows | `Result` was a separate `readonly struct` for success/failure with no payload, distinct from `Result<T>`. | The non-generic `Result` instance type was removed. `Result` is now a `public static partial class` factory only; for no-payload success/failure use `Result<Unit>` (returned by parameterless `Result.Ok()` / `Result.Fail(error)` / `Result.Ensure(...)` / `Result.Try(...)` factories). The `Trellis.Unit` type is a public `readonly record struct` with a single value (`Unit.Default`). | Replace `Result` parameter/return types with `Result<Unit>`; replace `Task<Result>` with `Task<Result<Unit>>`; in lambdas after `.Bind(...)` / `BindAsync(...)` accept the `Unit` argument explicitly (`_ =>` or `(Unit _) =>`). |
| `Error` as open class hierarchy | `Error` was a `class` with 18 hand-written subclasses (`ValidationError`, `NotFoundError`, …) and static factory helpers (`Error.Validation(...)`, `Error.NotFound(...)`, …). | `Error` is an `abstract record` with **20 nested `sealed record` cases** (`Error.NotFound`, `Error.UnprocessableContent`, …). Closed via `private` constructor; no static factories. | Replace `Error.X("msg")` factories with `new Error.X(payload) { Detail = "msg" }`. Replace concrete subclass type names (`ValidationError`, `NotFoundError`) with `Error.UnprocessableContent`, `Error.NotFound`. See "Error Cases (closed ADT)" below. | <!-- v1-stale-ok: migration-comparison row intentionally cites removed v1 factories -->
| `MatchErrorExtensions` | `result.MatchError(onValidation: ..., onNotFound: ..., onUnexpected: ...)` | *(removed)* | Use a `switch` expression on the closed ADT: `result.Match(_ => ..., e => e switch { Error.NotFound nf => ..., Error.UnprocessableContent uc => ..., _ => ... })`. C# verifies exhaustiveness against the closed catalog. |
| `FlattenValidationErrorsExtensions` | `result.FlattenValidationErrors()` | *(removed)* | `Combine` over multiple `Result<T>` automatically merges `Error.UnprocessableContent.Fields` and `.Rules`. |
| `Error.Instance` field | `error.Instance` (string-shaped HTTP vocabulary) | *(removed)* | The ASP wire layer synthesizes `ProblemDetails.Instance` from the request URL plus any `ResourceRef` carried by the typed payload. |
| Public `Value` / `Error` accessors on `Result<T>` | Both threw on the wrong branch. | `result.Error` is `public Error?` and **never throws** (null on success). The throwing `result.Value` getter was removed entirely because it was the primary cause of unsafe value access. | Read errors with `if (result.Error is { } error) { ... }` or `result.TryGetError(out var error)`. Extract success values with `result.TryGetValue(out var v)`, `result.TryGetValue(out var v, out var err)`, `result.Match(...)`, or `var (ok, v, err) = result;` (Deconstruct). | <!-- stale-doc-ok: migration-comparison row intentionally cites removed value accessor -->
| `WriteOutcome<T>` package + namespace | `Trellis.Asp.WriteOutcome<T>` (in `Trellis.Asp`) | `Trellis.WriteOutcome<T>` (in `Trellis.Core`) | Replace `using Trellis.Asp;` with `using Trellis;` for any file that names `WriteOutcome<T>` directly. The type, its case records, and member shapes are unchanged; only the assembly and namespace move. ASP-specific HTTP mapping remains in `Trellis.Asp` through `ToHttpResponse(...)` / `ToHttpResponseAsync(...)` and the typed MVC adapters `AsActionResult<T>()` / `AsActionResultAsync<T>()`. |
| Package id | `Trellis.Results` | `Trellis.Core` | Replace `<PackageReference Include="Trellis.Results" ... />` with `<PackageReference Include="Trellis.Core" ... />`. The CLR namespace stays `Trellis` — no `using` changes are needed. The legacy `Trellis.Results` package is unlisted with a redirect notice; there is no metapackage shim. | <!-- stale-doc-ok: migration-comparison row intentionally cites previous package id -->
| OpenTelemetry `ActivitySource` name | `"Trellis.Results"` | `"Trellis.Core"` | Update OTel subscriptions: `builder.AddSource("Trellis.Results")` → `builder.AddSource("Trellis.Core")`. The `RopTrace.ActivitySourceName` constant exposes the name programmatically. | <!-- stale-doc-ok: migration-comparison row intentionally cites previous activity source name -->
| Test helper namespace | `Trellis.Results.Tests.*` | `Trellis.Core.Tests.*` | Internal change only — affects users who took an InternalsVisibleTo dependency on the test assembly (none expected). | <!-- stale-doc-ok: migration-comparison row intentionally cites previous test namespace -->
| Package merge: DDD | <PackageReference Include="Trellis.DomainDrivenDesign" .../> | *(removed)* | All DDD types (`Aggregate<T>`, `Entity<T>`, `ValueObject`, `Specification<T>`, etc.) moved into `Trellis.Core`. Drop the `Trellis.DomainDrivenDesign` PackageReference; the types are still in `namespace Trellis;` so no using changes are needed. | <!-- stale-doc-ok: migration-comparison row intentionally cites previous package id -->
| Package merge: Primitives generator | <PackageReference Include="Trellis.Primitives.Generator" .../> | *(removed)* | The Required* source generator is now bundled inside `Trellis.Core.nupkg` (`analyzers/dotnet/cs/Trellis.Core.Generator.dll`). Installing `Trellis.Core` (or any package depending on it) attaches the analyzer automatically. Drop the standalone PackageReference. |
| `Required*` base classes | `Trellis.Primitives` | `Trellis.Core` | Source-tree consumers may need to ensure they reference `Trellis.Core`. Namespace is unchanged (`Trellis`), so no using edits are required. |
| Package merge: Asp generator | `<PackageReference Include="Trellis.AspSourceGenerator" .../>` | *(removed)* | The ASP source generator is now bundled inside `Trellis.Asp.nupkg` (`analyzers/dotnet/cs/Trellis.AspSourceGenerator.dll`). Installing `Trellis.Asp` attaches the analyzer automatically. Drop the standalone PackageReference. |
| Package merge: EF Core generator | `<PackageReference Include="Trellis.EntityFrameworkCore.Generator" .../>` | *(removed)* | The EF Core source generator (Maybe&lt;T&gt; partial properties + owned value-object helpers) is now bundled inside `Trellis.EntityFrameworkCore.nupkg` (`analyzers/dotnet/cs/Trellis.EntityFrameworkCore.Generator.dll`). Installing `Trellis.EntityFrameworkCore` attaches the analyzer automatically. Drop the standalone PackageReference. |
| Package merge: Asp authorization | `<PackageReference Include="Trellis.Asp.Authorization" .../>` | *(removed)* | The ASP.NET actor providers (`ClaimsActorProvider`, `EntraActorProvider`, `DevelopmentActorProvider`, `CachingActorProvider`, `AddTrellisAspAuthorization()`) are now part of `Trellis.Asp.nupkg`. The CLR namespace stays `Trellis.Asp.Authorization` — no `using` changes needed. Drop the standalone PackageReference. `Trellis.Asp` now transitively brings in `Trellis.Authorization`. |

The renames bring the factory names in line with Rust (`Ok`/`Err`), F# (`Ok`), and FluentResults (`Ok`/`Fail`). The `IsSuccess`/`IsFailure` predicate properties are **not** renamed — predicates read as questions and stay long-form.

---

## Types

### `public interface IResult`

Base success/failure contract.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `IsSuccess` | `bool` | `true` for success results. Marked `[MemberNotNullWhen(false, nameof(Error))]`. |
| `IsFailure` | `bool` | `true` for failure results. Marked `[MemberNotNullWhen(true, nameof(Error))]`. |
| `Error` | `Error?` | `null` on success; never throws. |

#### Methods

| Signature | Notes |
| --- | --- |
| `bool TryGetError(out Error? error)` | Non-throwing failure extractor. `[NotNullWhen(true)]` on the out parameter. |

#### Factory Methods

None.

---

### `public interface IResult<TValue> : IResult`

Typed success/failure contract. Note: there is **no** `Value` property — the previous `Value` getter threw on failure and was the leading source of `TRLS003`. Use `TryGetValue` to extract the success payload.

#### Properties

None (inherits `IsSuccess`, `IsFailure`, `Error` from `IResult`).

#### Methods

| Signature | Notes |
| --- | --- |
| `bool TryGetValue([MaybeNullWhen(false)] out TValue value)` | Non-throwing success extractor. Returns `true` and binds `value` on success; returns `false` and leaves `value` at `default` on failure. |

#### Factory Methods

None.

---

### `public interface IFailureFactory<TSelf> where TSelf : IFailureFactory<TSelf>`

Static factory contract for producing a failure instance of the implementing type.

#### Properties

None.

#### Methods

| Signature | Notes |
| --- | --- |
| `static abstract TSelf CreateFailure(Error error)` | Used by generic pipeline code |

#### Factory Methods

`CreateFailure(Error error)`.

---

### `public static partial class Result`

Static factory and helper surface for `Result<TValue>`. There is no non-generic instance `Result` type — for no-payload success/failure, use `Result<Unit>` (returned by the parameterless overloads listed below).

> **Default-state invariant.** `default(Result<T>)` represents a **failure** carrying the
> shared `new Error.Unexpected("default_initialized")` sentinel — *not* success. This makes uninitialized
> state a typed failure rather than a silent success that would hide a programming error. Always
> construct via `Result.Ok(...)` or `Result.Fail(error)`. Analyzer **`TRLS019`** flags explicit
> `default(Result<T>)` at call sites.

`Result` is `public static partial class Result`. It hosts the static factory and helper methods used to build every `Result<TValue>`.

#### Static factory methods

| Signature | Notes |
| --- | --- |
| `public static Result<TValue> Ok<TValue>(TValue value)` | Success factory |
| `public static Result<Unit> Ok()` | Success without payload (returns `Result<Unit>`) |
| `public static Result<TValue> Fail<TValue>(Error error)` | Failure factory |
| `public static Result<Unit> Fail(Error error)` | Failure without payload (returns `Result<Unit>`) |
| `public static Result<Unit> Ensure(bool flag, Error error)` | Converts a boolean to `Result<Unit>` |
| `public static Result<Unit> Ensure(Func<bool> predicate, Error error)` | Deferred predicate version |
| `public static Task<Result<Unit>> EnsureAsync(Func<Task<bool>> predicate, Error error)` | Async predicate version |
| `public static Result<T> Try<T>(Func<T> func, Func<Exception, Error>? map = null)` | Converts thrown exceptions to failures |
| `public static Task<Result<T>> TryAsync<T>(Func<Task<T>> func, Func<Exception, Error>? map = null)` | Async exception capture |
| `public static Result<Unit> Try(Action work, Func<Exception, Error>? map = null)` | No-payload exception capture (returns `Result<Unit>`) |
| `public static Task<Result<Unit>> TryAsync(Func<Task> work, Func<Exception, Error>? map = null)` | Async no-payload exception capture |
| `public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> r1, Result<T2> r2)` | Combines two results; passing a `Result<Unit>` adds `Unit` as the next tuple element |
| `public static Result<(T1, ..., T9)> Combine<...>(...)` | Additional generated arities up to 9 |
| `public static (Task<Result<T1>>, ..., Task<Result<T9>>) ParallelAsync<...>(...)` | Starts async result-producing operations in parallel, arities 2-9 |

The default exception mapper produces `new Error.InternalServerError(FaultId: Guid.NewGuid().ToString("N")) { Detail = ex.Message }`. `OperationCanceledException` is always rethrown rather than mapped.

#### Factory Methods

`Ok`, `Fail`, `Ensure`, `Try`, `TryAsync`, `Combine`, and `ParallelAsync`. Removed from the current API (see "Breaking changes from v1" above): `Success`, `Failure`, `Success(Func<T>)`, `Failure<T>(Func<Error>)`, `SuccessIf`, `FailureIf`, `SuccessIfAsync`, `FailureIfAsync`, `FromException`, and the non-generic `Result` instance type itself (`CreateFailure`, `IFailureFactory<Result>`, `IEquatable<Result>`, etc.).

---

### `public readonly struct Result<TValue> : IResult<TValue>, IEquatable<Result<TValue>>, IFailureFactory<Result<TValue>>`

Represents either a successful `TValue` or a failure `Error`.

> **Default-state invariant.** `default(Result<T>)` represents a **failure** carrying
> the shared `new Error.Unexpected("default_initialized")` sentinel — *not* success with `default(T)`.
> All failure-facing APIs (`Error`, `TryGetError`, `Deconstruct`, `Equals`, `GetHashCode`, `ToString`,
> `AsUnit`) route through this sentinel so that `default(Result<T>)` is observationally equivalent to
> `Result.Fail<T>(new Error.Unexpected("default_initialized"))`. Always construct via `Result.Ok(value)`
> or `Result.Fail<T>(error)`. Analyzer **`TRLS019`** flags explicit `default(Result<T>)` at call sites.

> **No `Value` property.** The throwing `public TValue Value` getter was removed. Use `TryGetValue`, `Match`, or `Deconstruct` to extract success values.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `Error` | `Error?` | `null` on success; never throws. Pattern-match on the value (e.g. `if (result.Error is { } error)`) for imperative branches. For `default(Result<T>)`, returns the shared `new Error.Unexpected("default_initialized")` sentinel. |
| `IsSuccess` | `bool` | Success flag. `[MemberNotNullWhen(false, nameof(Error))]`. |
| `IsFailure` | `bool` | Failure flag. `[MemberNotNullWhen(true, nameof(Error))]`. `default(Result<T>).IsFailure` is `true`. |

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Result<TValue> CreateFailure(Error error)` | Implements `IFailureFactory<Result<TValue>>`; lets generic pipeline behaviors construct failures polymorphically. Equivalent to `Result.Fail<TValue>(error)`. |
| `public bool TryGetValue([MaybeNullWhen(false)] out TValue value)` | Non-throwing success extractor. `[MemberNotNullWhen(false, nameof(Error))]`. |
| `public bool TryGetValue([MaybeNullWhen(false)] out TValue value, [NotNullWhen(false)] out Error? error)` | Combined extractor — binds both `value` (on success) and `error` (on failure) in one call, eliminating the need for `result.Error!` after a failed single-out `TryGetValue`. |
| `public bool TryGetError([NotNullWhen(true)] out Error? error)` | Non-throwing failure extractor; on `default(Result<T>)` returns `true` with the `Error.Unexpected` sentinel. |
| `public void Deconstruct(out bool isSuccess, out TValue? value, out Error? error)` | Deconstruction support: `var (ok, value, error) = result;`. |
| `public Result<Unit> AsUnit()` | Discards the success value, returning a `Result<Unit>`. On a default-initialized failure, returns an explicit `Result.Fail(sentinel)` (never another `default`). |
| `public bool Equals(Result<TValue> other)` | Value equality. Equal if both are success with `EqualityComparer<TValue>.Default.Equals` over the values, or both are failure with equal `Error`. Default-initialized failures route through the shared sentinel. |
| `public override bool Equals(object? obj)` | Object equality. |
| `public override int GetHashCode()` | Hash code matching `Equals`. |
| `public override string ToString()` | `"Success({value})"` or `"Failure({Code}: {Detail})"`. |

#### Operators

The implicit conversion operators (`TValue → Result<TValue>`, `Error → Result<TValue>`) were removed from the current API. Use `Result.Ok(value)` / `Result.Fail<T>(error)`.

| Signature | Notes |
| --- | --- |
| `public static bool operator ==(Result<TValue> left, Result<TValue> right)` | Equality |
| `public static bool operator !=(Result<TValue> left, Result<TValue> right)` | Inequality |

#### Factory Methods

Use the static `Result` type.

---

### `public static class Maybe`

Non-generic helpers for creating `Maybe<T>` and optional result flows.

#### Properties

None.

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Maybe<T> From<T>(T? value) where T : notnull` | Wraps nullable input |
| `public static Result<Maybe<TOut>> Optional<TIn, TOut>(TIn? value, Func<TIn, Result<TOut>> function) where TIn : class where TOut : notnull` | Runs function only when a reference value exists |
| `public static Result<Maybe<TOut>> Optional<TIn, TOut>(TIn? value, Func<TIn, Result<TOut>> function) where TIn : struct where TOut : notnull` | Value-type overload |

#### Factory Methods

`From` and `Optional`.

---

### `public static class MaybeInvariant`

Multi-field validation helpers for `Maybe<T>` values. Each method returns `Result<Unit>` — success when the invariant holds, or an `Error.UnprocessableContent` whose `Fields` list carries one `FieldViolation` per offending field. Field paths are normalized via `InputPointer.ForProperty(name)` (RFC 6901 JSON Pointer).

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Result<Unit> AllOrNone<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | All fields present or all absent. Arities 2, 3, 4. |
| `public static Result<Unit> Requires<T1, T2>(Maybe<T1> source, Maybe<T2> required, string sourceFieldName, string requiredFieldName)` | If `source` is present, `required` must be too. Arity 2. |
| `public static Result<Unit> MutuallyExclusive<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | At most one field may be present. Arities 2, 3. |
| `public static Result<Unit> ExactlyOne<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | Exactly one field must be present. Arities 2, 3. |
| `public static Result<Unit> AtLeastOne<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | At least one field must be present. Arities 2, 3. |

#### Usage

```csharp
// All-or-none: street + city must both be provided or both omitted
MaybeInvariant.AllOrNone(command.Street, command.City, "street", "city");

// Requires: if discount is given, reason is required
MaybeInvariant.Requires(command.Discount, command.DiscountReason, "discount", "discountReason");

// ExactlyOne: must provide either email or phone
MaybeInvariant.ExactlyOne(command.Email, command.Phone, "email", "phone");
```

---

### `public readonly struct Maybe<T> where T : notnull`

Optional value container for domain optionality.

> **Default-state invariant.** `default(Maybe<T>)` equals `Maybe<T>.None` (the type already uses an
> `_isValueSet` discriminator). Although correct, prefer the explicit `Maybe<T>.None` for readability.
> Analyzer **`TRLS019`** flags explicit `default(Maybe<T>)` at call sites and recommends `Maybe<T>.None`
> instead.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `None` | `Maybe<T>` | Static empty instance |
| `Value` | `T` | Throws when `HasNoValue` is `true` |
| `HasValue` | `bool` | Present flag |
| `HasNoValue` | `bool` | Empty flag |

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Maybe<T> From(T? value)` | Static constructor |
| `public T GetValueOrThrow(string? errorMessage = null)` | Throwing extractor |
| `public T GetValueOrDefault(T defaultValue)` | Fallback extractor |
| `public T GetValueOrDefault(Func<T> defaultFactory)` | Deferred fallback |
| `public bool TryGetValue(out T value)` | Non-throwing extractor |
| `public Maybe<TResult> Map<TResult>(Func<T, TResult> selector) where TResult : notnull` | Maps present value |
| `public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none)` | Branches on presence |
| `public Maybe<TResult> Bind<TResult>(Func<T, Maybe<TResult>> selector) where TResult : notnull` | Flat-map |
| `public Maybe<T> Or(T fallback)` | Fallback value |
| `public Maybe<T> Or(Func<T> fallbackFactory)` | Deferred fallback value |
| `public Maybe<T> Or(Maybe<T> fallback)` | Fallback maybe |
| `public Maybe<T> Or(Func<Maybe<T>> fallbackFactory)` | Deferred fallback maybe |
| `public Maybe<T> Where(Func<T, bool> predicate)` | Keeps value only when predicate passes |
| `public Maybe<T> Tap(Action<T> action)` | Side effect on value |
| `public override bool Equals(object? obj)` | Equality |
| `public bool Equals(Maybe<T> other)` | Equality |
| `public bool Equals(T? other)` | Equality against raw value |
| `public override int GetHashCode()` | Hash code |
| `public override string ToString()` | Debug string |

#### Operators

| Signature | Notes |
| --- | --- |
| `public static implicit operator Maybe<T>(T value)` | Implicit success-like wrap |
| `public static bool operator ==(Maybe<T> maybe, T value)` | Equality |
| `public static bool operator !=(Maybe<T> maybe, T value)` | Inequality |
| `public static bool operator ==(Maybe<T> maybe, object? other)` | Equality |
| `public static bool operator !=(Maybe<T> maybe, object? other)` | Inequality |
| `public static bool operator ==(Maybe<T> first, Maybe<T> second)` | Equality |
| `public static bool operator !=(Maybe<T> first, Maybe<T> second)` | Inequality |

#### Factory Methods

`None` and `From`.

---

### `public interface IScalarValue<TSelf, TPrimitive> where TSelf : IScalarValue<TSelf, TPrimitive> where TPrimitive : IComparable`

Contract for scalar value objects that validate and expose a primitive payload.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `Value` | `TPrimitive` | Wrapped primitive |

#### Methods

| Signature | Notes |
| --- | --- |
| `static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null)` | Primitive-based validation entry point |
| `static abstract Result<TSelf> TryCreate(string? value, string? fieldName = null)` | String-based validation entry point |
| `static virtual TSelf Create(TPrimitive value)` | Throws on validation failure |

#### Factory Methods

`TryCreate` and `Create`.

---

### `public interface IFormattableScalarValue<TSelf, TPrimitive> : IScalarValue<TSelf, TPrimitive> where TSelf : IFormattableScalarValue<TSelf, TPrimitive> where TPrimitive : IComparable`

Extends `IScalarValue` for culture-aware string parsing.

#### Properties

Inherited only.

#### Methods

| Signature | Notes |
| --- | --- |
| `static abstract Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)` | Culture-aware parse-and-validate |

#### Factory Methods

`TryCreate(string?, IFormatProvider?, string?)`.

---

### `public abstract record Error`

Closed discriminated union of error values. Each case is a nested `sealed record` mirroring an entry from the IANA HTTP Status Code Registry (RFC 9110, RFC 6585) and carrying a strongly-typed payload. The base record has a `private` constructor — only the cases declared in `Error.cs` may inherit, which makes `switch` over an `Error` reference exhaustive at the language level.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `Kind` | `string` | Stable, IANA-aligned slug (e.g. `"not-found"`, `"unprocessable-content"`). Survives CLR renames. Suitable for telemetry, problem-details `type` URI synthesis, and wire serialization. Abstract; each case overrides. |
| `Code` | `string` | Per-instance machine-readable code. Defaults to `Kind`; cases whose payload carries a per-instance `ReasonCode` (e.g. `Conflict`, `Forbidden`, `BadRequest`, `InternalServerError`) override this. |
| `Detail` | `string?` | Human-readable detail. Init-only (`Detail = "..."`). Boundary renderer prefers it when non-null; otherwise it computes a localized message from `Kind`/`Code` plus the typed payload. |
| `Cause` | `Error?` | Structured cause chain. **Never holds a live `System.Exception`** — wrap context as a child `Error`. Cycles are detected at `init` and throw `InvalidOperationException`. |

#### Methods

| Signature | Notes |
| --- | --- |
| `public string GetDisplayMessage()` | Computes the rendered detail. Returns `Detail` when non-null; otherwise composes from `Kind`/`Code` and the typed payload. For an `UnprocessableContent` carrying a single `FieldViolation`, returns just that violation's `Detail`. |
| `public override bool Equals(object? obj)` / `Equals(Error? other)` | Value equality over discriminator + typed payload + `Detail`. **`Cause` is excluded** so two errors with identical surface payload compare equal regardless of how deeply they were wrapped (mirrors `System.Exception` precedent). Collection-bearing payloads use `EquatableArray<T>` for sequence equality. |
| `public override int GetHashCode()` | Hash matches `Equals`. |

#### Construction (no static factory methods)

Construct cases directly: `new Error.NotFound(payload) { Detail = "..." }`. The base type intentionally exposes no static `Error.Validation(...)` / `Error.NotFound(...)` helpers — every call site names the case it produces. <!-- v1-stale-ok: explanatory note about removed v1 factory helpers -->

---

### Concrete error cases

Twenty nested `sealed record` cases under `Error`. Each case constructor is `internal` from external code's perspective only insofar as the base ctor is `private`; cases themselves are `public sealed record`s and instantiable with `new`.

| Case | Constructor | Wire status | Notes |
| --- | --- | --- | --- |
| `Error.BadRequest` | `(string ReasonCode, InputPointer? At = null)` | 400 | `Code` returns `ReasonCode`; `At` is an optional RFC 6901 JSON Pointer to the offending input. |
| `Error.Unauthorized` | `(EquatableArray<AuthChallenge> Challenges = default)` | 401 | Round-trips real `WWW-Authenticate` (per RFC 9110 §11.6.1). |
| `Error.Forbidden` | `(string PolicyId, ResourceRef? Resource = null)` | 403 | `Code` returns `PolicyId`. |
| `Error.NotFound` | `(ResourceRef Resource)` | 404 | `Resource` (e.g. `ResourceRef.For<Order>(orderId)`) drives ProblemDetails `instance`. |
| `Error.MethodNotAllowed` | `(EquatableArray<string> Allow)` | 405 | `Allow` populates the `Allow` response header (RFC 9110 §15.5.6). |
| `Error.NotAcceptable` | `(EquatableArray<string> Available)` | 406 | Available media types. |
| `Error.Conflict` | `(ResourceRef? Resource, string ReasonCode)` | 409 | `Code` returns `ReasonCode`. |
| `Error.Gone` | `(ResourceRef Resource)` | 410 | Soft-deleted resource. |
| `Error.PreconditionFailed` | `(ResourceRef Resource, PreconditionKind Condition)` | 412 | Optimistic concurrency mismatch. `Condition` is the typed precondition kind (`IfMatch`, `IfNoneMatch`, `IfModifiedSince`, `IfUnmodifiedSince`). |
| `Error.ContentTooLarge` | `(long? MaxBytes = null)` | 413 | |
| `Error.UnsupportedMediaType` | `(EquatableArray<string> Supported)` | 415 | |
| `Error.RangeNotSatisfiable` | `(long CompleteLength, string Unit = "bytes")` | 416 | Drives `Content-Range` synthesis. |
| `Error.UnprocessableContent` | `(EquatableArray<FieldViolation> Fields, EquatableArray<RuleViolation> Rules = default)` | 422 | The single domain-validation case — replaces the previous validation error class. Carries both per-field violations and cross-field rule violations. **Single-violation factories** (preferred over manual construction): `Error.UnprocessableContent.ForField(string propertyName, string reasonCode, string? detail = null)` (escapes `propertyName` via `InputPointer.ForProperty`), `ForField(InputPointer field, string reasonCode, string? detail = null)` (use for nested/array pointers or `InputPointer.Root`), and `ForRule(string reasonCode, string? detail = null)` (single rule, empty fields). Use `Validate` builder when aggregating multiple violations. |
| `Error.PreconditionRequired` | `(PreconditionKind Condition)` | 428 | Missing concurrency token on PUT. |
| `Error.TooManyRequests` | `(RetryAfterValue? RetryAfter = null)` | 429 | |
| `Error.InternalServerError` | `(string FaultId)` | 500 | `Code` returns `FaultId`. **Never holds a live `Exception`**; the `FaultId` indexes into the logging/telemetry layer. |
| `Error.Unexpected` | `(string ReasonCode)` | 500 | "Shouldn't happen" condition: default-initialized `Result`/`Result<T>` (per §3.5.1), exhausted match arms, internal invariant violations. `Code` returns `ReasonCode` (e.g. `"default_initialized"`). Distinct from `InternalServerError`: no opaque per-incident `FaultId`, and the ASP boundary does **not** attach a `faultId` extension. |
| `Error.NotImplemented` | `(string Feature)` | 501 | `Code` returns `Feature`. |
| `Error.ServiceUnavailable` | `(RetryAfterValue? RetryAfter = null)` | 503 | |
| `Error.Aggregate` | `(EquatableArray<Error> Errors)` <br> `(IEnumerable<Error> errors)` <br> `(params Error[] errors)` | 207 / `extensions.errors` | Composition node. **Disallows `Cause`** (pure composition). Auto-flattens nested `Aggregate` instances at construction. Three constructor overloads accept either an `EquatableArray<Error>`, any `IEnumerable<Error>`, or a `params` array; all three throw `ArgumentException` if no errors are supplied. `InputPointer.ForProperty` is used to escape `~` and `/` per RFC 6901. |

#### Supporting types

| Type | Shape | Purpose |
| --- | --- | --- |
| `ResourceRef` | `readonly record struct (string Type, string? Id = null)` plus `ResourceRef.For(string type, object? id = null)` and `ResourceRef.For<TResource>(object? id = null)` | Aggregate identity. The `For(...)` helpers convert IDs with invariant formatting when possible and `For<TResource>` uses `typeof(TResource).Name` exactly. |
| `InputPointer` | `readonly record struct (string Path)` | RFC 6901 JSON Pointer (e.g. `/email`). Construct via `InputPointer.ForProperty("email")`, or use the document-root sentinel `InputPointer.Root` (path `""`). `InputPointer.Root` is the canonical pointer for whole-body / object-level violations. |
| `FieldViolation` | `sealed record (InputPointer Field, string ReasonCode, ImmutableDictionary<string,string>? Args = null, string? Detail = null)` | Single per-field violation inside `UnprocessableContent.Fields`. `Detail` is the 4th positional parameter; supplies the boundary renderer's user-facing message when non-null. `Equals`/`GetHashCode` compare `Args` by content. |
| `RuleViolation` | `sealed record (string ReasonCode, EquatableArray<InputPointer> Fields = default, ImmutableDictionary<string,string>? Args = null, string? Detail = null)` | Multi-field invariant or object-level rule inside `UnprocessableContent.Rules`. `Detail` is the 4th positional parameter. `Equals`/`GetHashCode` compare `Args` by content. |
| `AuthChallenge` | `sealed record (string Scheme, ImmutableDictionary<string,string>? Params = null)` | Carried by `Unauthorized` to round-trip `WWW-Authenticate`. `Equals`/`GetHashCode` compare `Params` by content; parameter order is not significant. |
| `RetryAfterValue` | sealed class, see below | `Retry-After` as delay seconds or absolute date. |
| `PreconditionKind` | `enum { IfMatch, IfNoneMatch, IfModifiedSince, IfUnmodifiedSince }` | Typed precondition vocabulary. |
| `EquatableArray<T>` | `readonly struct (ImmutableArray<T> Items)` | Wraps `ImmutableArray<T>` so records get sequence equality (built-in records compare arrays by reference). See dedicated section below. Construct with `EquatableArray.Create(...)`, `EquatableArray.From(items)`, `EquatableArray<T>.Empty`, or implicitly from an `ImmutableArray<T>`. |

---

### `public sealed class RetryAfterValue : IEquatable<RetryAfterValue>`

Represents `Retry-After` as either delay seconds or a date.

#### Properties

| Name | Type |
| --- | --- |
| `IsDelaySeconds` | `bool` |
| `IsDate` | `bool` |
| `DelaySeconds` | `int` |
| `Date` | `DateTimeOffset` |

#### Methods

| Signature | Notes |
| --- | --- |
| `public static RetryAfterValue FromSeconds(int seconds)` | Delay form |
| `public static RetryAfterValue FromDate(DateTimeOffset date)` | Absolute-date form |
| `public string ToHeaderValue()` | RFC header value |
| `public override string ToString()` | String form |
| `public bool Equals(RetryAfterValue? other)` | Equality |
| `public override bool Equals(object? obj)` | Equality |
| `public override int GetHashCode()` | Hash code |

#### Factory Methods

`FromSeconds` and `FromDate`.

---

### `public sealed record EntityTagValue`

Represents strong, weak, or wildcard ETags.

#### Properties

| Name | Type |
| --- | --- |
| `OpaqueTag` | `string` |
| `IsWeak` | `bool` |
| `IsWildcard` | `bool` |

#### Methods

| Signature | Notes |
| --- | --- |
| `public static EntityTagValue Strong(string opaqueTag)` | Strong ETag |
| `public static EntityTagValue Weak(string opaqueTag)` | Weak ETag |
| `public static EntityTagValue Wildcard()` | Wildcard ETag |
| `public static Result<EntityTagValue> TryParse(string? headerValue)` | Parse from HTTP header |
| `public bool StrongEquals(EntityTagValue other)` | Strong comparison |
| `public bool WeakEquals(EntityTagValue other)` | Weak comparison |
| `public string ToHeaderValue()` | RFC header form |
| `public override string ToString()` | String form |

#### Factory Methods

`Strong`, `Weak`, `Wildcard`, and `TryParse`.

---

### `public sealed class RepresentationMetadata`

Metadata used by Trellis ASP helpers for validators, caching, and response headers.

#### Properties

| Name | Type |
| --- | --- |
| `ETag` | `EntityTagValue?` |
| `LastModified` | `DateTimeOffset?` |
| `Vary` | `IReadOnlyList<string>?` |
| `ContentLanguage` | `IReadOnlyList<string>?` |
| `ContentLocation` | `string?` |
| `AcceptRanges` | `string?` |

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Builder Create()` | Starts fluent builder |
| `public static RepresentationMetadata WithETag(EntityTagValue eTag)` | Convenience metadata |
| `public static RepresentationMetadata WithStrongETag(string opaqueTag)` | Strong ETag convenience |

#### Factory Methods

`Create`, `WithETag`, `WithStrongETag`.

---

### `public sealed class RepresentationMetadata.Builder`

Fluent builder for `RepresentationMetadata`.

#### Properties

None.

#### Methods

| Signature | Notes |
| --- | --- |
| `public Builder SetETag(EntityTagValue eTag)` | Sets ETag |
| `public Builder SetStrongETag(string opaqueTag)` | Convenience strong ETag |
| `public Builder SetWeakETag(string opaqueTag)` | Convenience weak ETag |
| `public Builder SetLastModified(DateTimeOffset lastModified)` | Sets last modified |
| `public Builder AddVary(params string[] fieldNames)` | Adds `Vary` fields |
| `public Builder AddContentLanguage(params string[] languages)` | Adds content languages |
| `public Builder SetContentLocation(string uri)` | Sets content location |
| `public Builder SetAcceptRanges(string value)` | Sets `Accept-Ranges` |
| `public RepresentationMetadata Build()` | Builds metadata |

#### Factory Methods

Use `RepresentationMetadata.Create()`.

---

### `public abstract record WriteOutcome<T>`

Closed union representing the outcome of a write operation (create / replace / accept-for-async) returned by Application-layer repositories. Transport adapters (e.g. `Trellis.Asp`'s `WriteOutcomeExtensions`) translate each case to a protocol-specific response. The case set aligns with RFC 9110 §9.3.4 because HTTP is the most commonly served transport, but `WriteOutcome<T>` itself takes no dependency on any transport package.

```csharp
public abstract record WriteOutcome<T>
{
    public sealed record Created(T Value, string Location, RepresentationMetadata? Metadata = null)         : WriteOutcome<T>;
    public sealed record Updated(T Value, RepresentationMetadata? Metadata = null)                          : WriteOutcome<T>;
    public sealed record UpdatedNoContent(RepresentationMetadata? Metadata = null)                          : WriteOutcome<T>;
    public sealed record Accepted(T StatusBody, string? MonitorUri = null, RetryAfterValue? RetryAfter = null)      : WriteOutcome<T>;
    public sealed record AcceptedNoContent(string? MonitorUri = null, RetryAfterValue? RetryAfter = null)           : WriteOutcome<T>;
}
```

| Case | Members | Transports as |
| --- | --- | --- |
| `Created` | `T Value`, `string Location`, `RepresentationMetadata? Metadata` | HTTP `201 Created` + `Location` |
| `Updated` | `T Value`, `RepresentationMetadata? Metadata` | HTTP `200 OK` |
| `UpdatedNoContent` | `RepresentationMetadata? Metadata` | HTTP `204 No Content` |
| `Accepted` | `T StatusBody`, `string? MonitorUri`, `RetryAfterValue? RetryAfter` | HTTP `202 Accepted` + body |
| `AcceptedNoContent` | `string? MonitorUri`, `RetryAfterValue? RetryAfter` | HTTP `202 Accepted` |

The base record's constructor is `private`; new cases cannot be added by consumers.

---

### `public sealed class RailwayTrackAttribute : Attribute`

Annotates result helpers with whether they operate on the success or failure railway.

#### Properties

| Name | Type |
| --- | --- |
| `Track` | `TrackBehavior` |

#### Methods

| Signature | Notes |
| --- | --- |
| `public RailwayTrackAttribute(TrackBehavior track)` | Constructor |

#### Factory Methods

None.

---

### `public enum TrackBehavior`

Values: `Success`, `Failure`.

---

### `public static class ResultDebugSettings`

Global debug switch for result tracing.

#### Properties

| Name | Type |
| --- | --- |
| `EnableDebugTracing` | `bool` |

#### Methods

None.

#### Factory Methods

None.

---

### `public static class ResultsTraceProviderBuilderExtensions`

OpenTelemetry helper for Trellis result instrumentation. Lives in `Trellis.Core\src\ResultsTraceProviderBuilderExtensions.cs` and takes a hard dependency on the `OpenTelemetry.Trace` package — `Trellis.Core` references the OpenTelemetry SDK so consumers do not need a separate package reference to opt in.

#### Methods

| Signature | Notes |
| --- | --- |
| `public static TracerProviderBuilder AddResultsInstrumentation(this TracerProviderBuilder builder)` | Registers the Trellis ROP `ActivitySource` (named `"Trellis.Core"`, exposed as `RopTrace.ActivitySourceName`) with the supplied OpenTelemetry tracer-provider builder. Returns the same builder for chaining. |

---

### `public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>`

Wraps `ImmutableArray<T>` so records and other value-equal types get sequence equality. Built-in `record` equality compares arrays by reference; this wrapper restores element-wise comparison. A default-initialized `EquatableArray<T>` represents an empty sequence — two `default` values compare equal, and `Items` always returns `ImmutableArray<T>.Empty` instead of an uninitialized array.

> **LINQ / FluentAssertions / `IEnumerable<T>` consumers.** `EquatableArray<T>` exposes a duck-typed `GetEnumerator()` (allocation-free `foreach`) but **does not implement `IEnumerable<T>`** — this is intentional, to keep the value-type sequence-equality wrapper allocation-free. Methods that bind on `IEnumerable<T>` (`Select`, `Where`, `Any`, `ToList`, FluentAssertions' `Should().ContainSingle()` / `Should().HaveCount(...)` / `Should().BeEquivalentTo(...)`, `string.Join`, etc.) won't see the contents directly. **Project through `.Items` first** (which returns the wrapped `ImmutableArray<T>`, an `IEnumerable<T>`):
>
> ```csharp
> // ❌ Doesn't compile: 'EquatableArray<RuleViolation>' does not contain a definition for 'Where'
> unproc.Rules.Where(r => r.ReasonCode == "...");
>
> // ❌ FluentAssertions: 'object does not contain a definition for ContainSingle'
> unproc.Rules.Should().ContainSingle();
>
> // ✅ Use .Items
> unproc.Rules.Items.Where(r => r.ReasonCode == "...");
> unproc.Rules.Items.Should().ContainSingle().Which.ReasonCode.Should().Be("...");
> ```

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `Items` | `ImmutableArray<T>` | The wrapped array. Returns `ImmutableArray<T>.Empty` for default-initialized values rather than the uninitialized default. |
| `Length` | `int` | Number of items. |
| `IsEmpty` | `bool` | True when the wrapped array is empty. |
| `this[int index]` | `T` | Indexer over the wrapped array. |
| `Empty` | `EquatableArray<T>` | Static empty instance, mirrors `ImmutableArray<T>.Empty`. |

#### Methods

| Signature | Returns | Description |
| --- | --- | --- |
| `public EquatableArray(ImmutableArray<T> items)` | — | Wraps an existing immutable array. |
| `public static EquatableArray<T> Create(params T[] items)` | `EquatableArray<T>` | Builds from a `params` array. |
| `public static EquatableArray<T> From(IEnumerable<T> items)` | `EquatableArray<T>` | Builds from any enumerable. |
| `public ImmutableArray<T>.Enumerator GetEnumerator()` | `ImmutableArray<T>.Enumerator` | Allocation-free `foreach` support. |
| `public bool Equals(EquatableArray<T> other)` | `bool` | Sequence equality using `EqualityComparer<T>.Default`. |
| `public override bool Equals(object? obj)` | `bool` | Object equality. |
| `public override int GetHashCode()` | `int` | Combines hashes of all items via `HashCode`. |

#### Operators

| Signature | Notes |
| --- | --- |
| `public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)` | Equality |
| `public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)` | Inequality |
| `public static implicit operator EquatableArray<T>(ImmutableArray<T> items)` | Implicit conversion from `ImmutableArray<T>` |

---

### `public static class EquatableArray`

Non-generic factory companion that allows type inference at the call site.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static EquatableArray<T> Create<T>(params T[] items)` | `EquatableArray<T>` | Inferred-`T` factory; equivalent to `EquatableArray<T>.Create(items)`. |
| `public static EquatableArray<T> From<T>(IEnumerable<T> items)` | `EquatableArray<T>` | Inferred-`T` factory; equivalent to `EquatableArray<T>.From(items)`. |

---

## Extension Methods

### `MaybeExtensions`

| Signature |
| --- |
| `public static Maybe<T> AsMaybe<T>(this T? value) where T : struct` |
| `public static Maybe<T> AsMaybe<T>(this T value) where T : class` |
| `public static T? AsNullable<T>(in this Maybe<T> value) where T : struct` |
| `public static Result<TValue> ToResult<TValue>(in this Maybe<TValue> maybe, Error error) where TValue : notnull` |
| `public static Result<TValue> ToResult<TValue>(in this Maybe<TValue> maybe, Func<Error> ferror) where TValue : notnull` |
| `public static Result<TValue> ToResult<TValue>(this TValue value)` |

### `MaybeExtensionsAsync`

| Signature |
| --- |
| `public static Task<Result<TValue>> ToResultAsync<TValue>(this Task<Maybe<TValue>> maybeTask, Error error) where TValue : notnull` |
| `public static ValueTask<Result<TValue>> ToResultAsync<TValue>(this ValueTask<Maybe<TValue>> maybeTask, Error error) where TValue : notnull` |
| `public static Task<Result<TValue>> ToResultAsync<TValue>(this Task<Maybe<TValue>> maybeTask, Func<Error> ferror) where TValue : notnull` |
| `public static ValueTask<Result<TValue>> ToResultAsync<TValue>(this ValueTask<Maybe<TValue>> maybeTask, Func<Error> ferror) where TValue : notnull` |
| `public static Task<TResult> MatchAsync<TValue, TResult>(this Task<Maybe<TValue>> maybeTask, Func<TValue, TResult> some, Func<TResult> none) where TValue : notnull` |
| `public static ValueTask<TResult> MatchAsync<TValue, TResult>(this ValueTask<Maybe<TValue>> maybeTask, Func<TValue, TResult> some, Func<TResult> none) where TValue : notnull` |
| `public static Task<TResult> MatchAsync<TValue, TResult>(this Task<Maybe<TValue>> maybeTask, Func<TValue, Task<TResult>> some, Func<Task<TResult>> none) where TValue : notnull` |
| `public static ValueTask<TResult> MatchAsync<TValue, TResult>(this ValueTask<Maybe<TValue>> maybeTask, Func<TValue, ValueTask<TResult>> some, Func<ValueTask<TResult>> none) where TValue : notnull` |

### `MaybeChooseExtensions`

| Signature |
| --- |
| `public static IEnumerable<T> Choose<T>(this IEnumerable<Maybe<T>> source) where T : notnull` |
| `public static IEnumerable<TResult> Choose<T, TResult>(this IEnumerable<Maybe<T>> source, Func<T, TResult> selector) where T : notnull` |

### `MaybeLinqExtensions`

| Signature |
| --- |
| `public static Maybe<TOut> Select<TIn, TOut>(this Maybe<TIn> maybe, Func<TIn, TOut> selector) where TIn : notnull where TOut : notnull` |
| `public static Maybe<TResult> SelectMany<TSource, TCollection, TResult>(this Maybe<TSource> source, Func<TSource, Maybe<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector) where TSource : notnull where TCollection : notnull where TResult : notnull` |

### `MaybeCollectionExtensions`

| Signature |
| --- |
| `public static Maybe<T> TryFirst<T>(this IEnumerable<T> source) where T : notnull` |
| `public static Maybe<T> TryFirst<T>(this IEnumerable<T> source, Func<T, bool> predicate) where T : notnull` |
| `public static Maybe<T> TryLast<T>(this IEnumerable<T> source) where T : notnull` |
| `public static Maybe<T> TryLast<T>(this IEnumerable<T> source, Func<T, bool> predicate) where T : notnull` |

### Result pipeline extension families

The result API contains a large generated extension surface. Exact public families:

| Static Class | Public Surface |
| --- | --- |
| `BindExtensions`, `BindExtensionsAsync` | `Bind`/`BindAsync` for `Result<T>` plus generated tuple overloads for arities 2-9 |
| `BindZipExtensions`, `BindZipExtensionsAsync` | Zips one result into another result-producing function, with sync/`Task`/`ValueTask` combinations and tuple arities |
| `CheckExtensions`, `CheckExtensionsAsync` | Runs side-effect validations that return `Result<T>` (use `Result<Unit>` for no-payload validators) while preserving original success value |
| `CheckIfExtensions`, `CheckIfExtensionsAsync` | Conditional `Check` variants |
| `CombineExtensions`, `CombineExtensionsAsync`, `CombineErrorExtensions` | Combines results, including tuple and enumerable forms |
| `DiscardExtensions`, `DiscardTaskExtensions`, `DiscardValueTaskExtensions` | Drops the `Result<T>` value entirely (returns `void`/`Task`/`ValueTask`) for intentional fire-and-forget pipelines |
| `EnsureExtensions`, `EnsureExtensionsAsync`, `EnsureAllExtensions`, `EnsureAllExtensionsAsync` | Predicate-based validation on successful values; includes collection-wide validation |
| `GetValueOrDefaultExtensions` | Non-throwing value fallback helpers |
| `ResultLinqExtensions` | LINQ query syntax support via `Select`/`SelectMany`/`Where` |
| `MapExtensions`, `MapExtensionsAsync`, `MapIfExtensions`, `MapOnFailureExtensions` | Success-path mapping, conditional mapping, and failure remapping; tuple overloads generated for arities 2-9 |
| `MatchExtensions`, `MatchExtensionsAsync`, `MatchTupleExtensions`, `MatchTupleExtensionsAsync` | Terminal branching for normal and tuple results. (The previous `MatchErrorExtensions` API was removed — use `result.Match(_ => ..., e => e switch { Error.NotFound nf => ..., ... })` against the closed catalog.) |
| `NullableExtensions`, `NullableExtensionsAsync` | Converts nullable reference/value types to `Result<T>` |
| `RecoverExtensions`, `RecoverExtensionsAsync`, `RecoverOnFailureExtensions`, `RecoverOnFailureExtensionsAsync` | Converts failures into fallback success values or results |
| `TapExtensions`, `TapExtensionsAsync`, `TapOnFailureExtensions`, `TapOnFailureExtensionsAsync` | Side effects on success or failure; tuple overloads generated for arities 2-9 |
| `ToMaybeExtensions`, `ToMaybeExtensionsAsync` | Converts `Result<T>` to `Maybe<T>` |
| `TraverseExtensions` | Traverses collections through result-producing functions |
| `WhenExtensions`, `WhenExtensionsAsync`, `WhenAllExtensionsAsync` | Conditional execution and async fan-in utilities |

Representative exact signatures:

```csharp
public static Result<TResult> Bind<TValue, TResult>(this Result<TValue> result, Func<TValue, Result<TResult>> func)
public static Task<Result<TResult>> BindAsync<TValue, TResult>(this Result<TValue> result, Func<TValue, Task<Result<TResult>>> func)
public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> map)
public static Result<TValue> Ensure<TValue>(this Result<TValue> result, Func<TValue, bool> predicate, Error error)
public static TOut Match<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> onSuccess, Func<Error, TOut> onFailure)
public static Result<T> ToResult<T>(this T? obj, Error error) where T : class
public static Maybe<T> ToMaybe<T>(this Result<T> result) where T : notnull
public static Result<TValue> Recover<TValue>(this Result<TValue> result, Func<Error, TValue> fallbackFunc)
public static Result<TValue> TapOnFailure<TValue>(this Result<TValue> result, Action<Error> action)
```

For tuple-enabled families, generated overloads cover the declared arity ranges shown above; no `ValueTuple` arities higher than 9 are public in this package.

---

### Extension class catalog (full signatures)

The reference signatures below cover every `Result*Extensions(Async)` static class shipped by `Trellis.Core`. Each subsection lists the static class name(s), a methods table, and one representative usage example. All members live in the `Trellis` namespace.

#### Task adapter family — `ResultTaskAdapterExtensions`

Adapters for returning a synchronous `Result<T>` from an async-shaped API without target-typed `new(...)` wrappers.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<Result<T>> AsTask<T>(this Result<T> result)` | `Task<Result<T>>` | Wraps the exact result state in a completed `Task`. |
| `public static ValueTask<Result<T>> AsValueTask<T>(this Result<T> result)` | `ValueTask<Result<T>>` | Wraps the exact result state in a completed `ValueTask`. |

```csharp
public ValueTask<Result<OrderId>> Handle(CreateOrderCommand cmd, CancellationToken ct) =>
    OrderId.TryCreate(cmd.OrderId)
        .Bind(id => Order.Create(id))
        .Tap(repo.Add)
        .Map(order => order.Id)
        .AsValueTask();
```

#### Bind family — `BindExtensions`, `BindExtensionsAsync`, `BindZipExtensions`, `BindZipExtensionsAsync`

Sequential composition of result-producing functions. `Bind` is the monadic flatMap; `BindZip` keeps the upstream value in scope by zipping it into the next stage. For no-payload steps, return `Result<Unit>` and use `_` to ignore the `Unit` argument in the next lambda.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<TResult> Bind<TValue, TResult>(this Result<TValue> result, Func<TValue, Result<TResult>> func)` | `Result<TResult>` | Generic-to-generic flatMap. Short-circuits on failure. |
| `public static Task<Result<TResult>> BindAsync<TValue, TResult>(this Task<Result<TValue>> resultTask, Func<TValue, Task<Result<TResult>>> func)` | `Task<Result<TResult>>` | All combinations of `Result<T>`/`Task<Result<T>>`/`ValueTask<Result<T>>` × sync-/async-lambda are exposed (12 overloads on `BindExtensionsAsync`). |
| `public static Result<(T1, T2)> BindZip<T1, T2>(this Result<T1> result, Func<T1, Result<T2>> func)` | `Result<(T1, T2)>` | Zips upstream value with the bind result so downstream stages see both. Tuple arities 2–9 are generated. |
| `public static Task<Result<(T1, T2)>> BindZipAsync<T1, T2>(this Task<Result<T1>> resultTask, Func<T1, Task<Result<T2>>> func)` | `Task<Result<(T1, T2)>>` | Async BindZip; generated for every Result/Task/ValueTask combination. |

```csharp
Result<Order> Place(OrderId id) =>
    LoadCustomer(id)
        .BindZip(c => LoadCart(c.Id))    // Result<(Customer, Cart)>
        .Bind((customer, cart) => Charge(customer, cart));
```

> **Trap — `BindAsync` Task vs ValueTask overload ambiguity (`CS0121`).** When the lambda passed to `BindAsync` is an inline expression whose return type can't be inferred between `Task<Result<R>>` and `ValueTask<Result<R>>`, the compiler reports both overloads as candidates. Two reliable fixes:
>
> 1. **Named method with explicit return type** — extract the lambda body to a method declared as `private Task<Result<R>> NextStage(T value, CancellationToken ct) { ... }` and pass `NextStage` (the method group resolves unambiguously).
> 2. **Typed local delegate** — `Func<T, Task<Result<R>>> next = c => ...; .BindAsync(next);` forces the `Task` overload.
>
> Avoid storing the lambda inline as `var next = ...;` because the inferred type may still be ambiguous.

#### Map family — `MapExtensions`, `MapExtensionsAsync`, `MapIfExtensions`, `MapOnFailureExtensions`

Pure transformation of the success value (or failure error). Use `Map` when the lambda returns a plain value; switch to `Bind` when it returns a `Result`.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> func)` | `Result<TOut>` | Synchronous map on `Result<T>`. |
| `public static Task<Result<TOut>> MapAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, Task<TOut>> func)` | `Task<Result<TOut>>` | `MapExtensionsAsync` exposes all Task/ValueTask × sync-/async-lambda combinations (6 overloads). |
| `public static Result<T> MapOnFailure<T>(this Result<T> result, Func<Error, Error> map)` | `Result<T>` | Replaces the failure `Error`. |
| `public static Task<Result<T>> MapOnFailureAsync<T>(this Task<Result<T>> resultTask, Func<Error, Task<Error>> mapAsync)` | `Task<Result<T>>` | `MapOnFailureExtensions` exposes all sync/Task/ValueTask combinations of `MapOnFailure`/`MapOnFailureAsync`. |

```csharp
Task<Result<OrderDto>> Pipeline(OrderId id) =>
    LoadOrderAsync(id)
        .MapAsync(o => OrderDto.From(o))
        .MapOnFailureAsync(e => e is Error.NotFound ? new Error.Gone(ResourceRef.For<Order>(id)) : e);
```

#### Tap and TapOnFailure families — `TapExtensions`, `TapExtensionsAsync`, `TapOnFailureExtensions`, `TapOnFailureExtensionsAsync`

Side effects without altering the result. `Tap` runs on success; `TapOnFailure` runs on failure.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<TValue> Tap<TValue>(this Result<TValue> result, Action<TValue> action)` | `Result<TValue>` | Sync side effect on success. |
| `public static Task<Result<TValue>> TapAsync<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, Task> func)` | `Task<Result<TValue>>` | `TapExtensionsAsync` covers all sync/Task/ValueTask × value-/no-value-lambda combinations (12 overloads). |
| `public static Result<TValue> TapOnFailure<TValue>(this Result<TValue> result, Action<Error> action)` | `Result<TValue>` | Sync side effect on failure. |
| `public static Task<Result<TValue>> TapOnFailureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<Error, Task> func)` | `Task<Result<TValue>>` | `TapOnFailureExtensionsAsync` covers all sync/Task/ValueTask × error-/no-arg-lambda combinations (12 non-tuple overloads; tuple arities 2–9 generate the same set per arity). |

```csharp
Task<Result<Order>> Save(Order o) =>
    repo.SaveAsync(o)
        .TapAsync(saved => logger.LogInformationAsync($"saved {saved.Id}"))
        .TapOnFailureAsync(err => logger.LogWarningAsync($"failed: {err.Code}"));
```

#### Match family — `MatchExtensions`, `MatchExtensionsAsync`, `MatchTupleExtensions`, `MatchTupleExtensionsAsync`

Terminal branching: produce a value (`Match`) or run side effects (`Switch`).

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TOut Match<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> onSuccess, Func<Error, TOut> onFailure)` | `TOut` | Sync match on `Result<T>`. |
| `public static void Switch<TIn>(this Result<TIn> result, Action<TIn> onSuccess, Action<Error> onFailure)` | `void` | Sync side-effect terminal. |
| `public static Task<TOut> MatchAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)` | `Task<TOut>` | `MatchExtensionsAsync` covers all sync/Task/ValueTask × sync-/async-/cancellation-lambda combinations (~10 overloads). |
| `public static Task SwitchAsync<TIn>(this Task<Result<TIn>> resultTask, Func<TIn, Task> onSuccess, Func<Error, Task> onFailure)` | `Task` | `SwitchAsync` overloads cover Task and ValueTask, with optional `CancellationToken` variants. |
| `public static Task<TOut> MatchAsync<T1, T2, TOut>(this Result<(T1, T2)> result, Func<T1, T2, Task<TOut>> onSuccess, Func<Error, Task<TOut>> onFailure)` | `Task<TOut>` | `MatchTupleExtensions` (sync) and `MatchTupleExtensionsAsync` (async) generate `MatchAsync` / `SwitchAsync` for tuple arities 2–9. |

```csharp
IActionResult Render(Result<Order> r) =>
    r.Match(
        order => Ok(OrderDto.From(order)),
        err   => err switch
        {
            Error.NotFound nf            => NotFound(nf.Resource.Id),
            Error.UnprocessableContent u => UnprocessableEntity(u.Fields),
            _                            => Problem(err.GetDisplayMessage()),
        });
```

#### Recover family — `RecoverExtensions`, `RecoverExtensionsAsync`, `RecoverOnFailureExtensions`, `RecoverOnFailureExtensionsAsync`

Convert failures back into successes (`Recover`) or chain a fallback result-producing operation (`RecoverOnFailure`).

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<TValue> Recover<TValue>(this Result<TValue> result, Func<Error, TValue> fallbackFunc)` | `Result<TValue>` | Three sync overloads on `RecoverExtensions`: constant fallback, `Func<TValue>`, `Func<Error, TValue>`. |
| `public static Task<Result<TValue>> RecoverAsync<TValue>(this Task<Result<TValue>> resultTask, Func<Error, Task<TValue>> fallbackFunc)` | `Task<Result<TValue>>` | `RecoverExtensionsAsync` covers all Task/ValueTask × sync-/async-lambda combinations. |
| `public static Result<T> RecoverOnFailure<T>(this Result<T> result, Func<Error, Result<T>> func)` | `Result<T>` | Four sync overloads on `RecoverOnFailureExtensions`: with/without `Error` argument, with/without predicate gate. |
| `public static Task<Result<T>> RecoverOnFailureAsync<T>(this Task<Result<T>> resultTask, Func<Error, Task<Result<T>>> funcAsync)` | `Task<Result<T>>` | `RecoverOnFailureExtensionsAsync` exposes ~16 overloads for Task/ValueTask × predicate-gated/ungated × value-/error-lambda. |

```csharp
Task<Result<Settings>> Load(UserId id) =>
    settingsRepo.LoadAsync(id)
        .RecoverOnFailureAsync(
            e => e is Error.NotFound,
            err => Task.FromResult(Result.Ok(Settings.Defaults)));
```

#### Ensure family — `EnsureExtensions`, `EnsureExtensionsAsync`, `EnsureAllExtensions`, `EnsureAllExtensionsAsync`

Predicate-based validation. `Ensure` short-circuits on the first failed predicate; `EnsureAll` accumulates every failure into a single `Error.Aggregate` for applicative-style validation.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<TValue> Ensure<TValue>(this Result<TValue> result, Func<TValue, bool> predicate, Error error)` | `Result<TValue>` | Sync ensure with predicate + `Error`. Five sync overloads (with/without value arg, factory error, embedded result). |
| `public static Result<TValue> Ensure<TValue>(this Result<TValue> result, Func<TValue, bool> predicate, Func<TValue, Error> errorPredicate)` | `Result<TValue>` | Sync ensure with lazy error factory. |
| `public static Result<string> EnsureNotNullOrWhiteSpace(this string? str, Error error)` | `Result<string>` | Lifts a possibly-blank string to `Result<string>`. |
| `public static Result<T> EnsureNotNull<T>(this Result<T?> result, Error error) where T : class` | `Result<T>` | Reference-type `EnsureNotNull` overload that strips the nullable annotation. |
| `public static Result<T> EnsureNotNull<T>(this Result<T?> result, Error error) where T : struct` | `Result<T>` | Value-type `EnsureNotNull` overload that unwraps the nullable. |
| `public static Task<Result<TValue>> EnsureAsync<TValue>(this Task<Result<TValue>> resultTask, Func<TValue, Task<bool>> predicate, Error error)` | `Task<Result<TValue>>` | `EnsureExtensionsAsync` covers all `Result<T>`/`Task<Result<T>>`/`ValueTask<Result<T>>` receivers × `Func<TValue, bool>`/`Task<bool>`/`ValueTask<bool>` predicates × constant-, factory-, async-factory- and embedded-`Result<TValue>` error producers (~34 overloads across the six `Ensure.*` partial files). |
| `public static Result<TValue> EnsureAll<TValue>(this Result<TValue> result, params (Func<TValue, bool> predicate, Error error)[] checks)` | `Result<TValue>` | Applicative validation: runs every check and folds failures via `error.Combine(...)` into one `Error.Aggregate`. |
| `public static Task<Result<TValue>> EnsureAllAsync<TValue>(this Task<Result<TValue>> resultTask, params (Func<TValue, bool> predicate, Error error)[] checks)` | `Task<Result<TValue>>` | Task overload of `EnsureAllAsync`. |
| `public static ValueTask<Result<TValue>> EnsureAllAsync<TValue>(this ValueTask<Result<TValue>> resultTask, params (Func<TValue, bool> predicate, Error error)[] checks)` | `ValueTask<Result<TValue>>` | ValueTask overload of `EnsureAllAsync`. |

```csharp
Result<Quote> Validate(Quote q) =>
    Result.Ok(q).EnsureAll(
        (x => x.Total > 0,            Error.UnprocessableContent.ForField("total", "must_be_positive")),
        (x => x.Currency.Length == 3, Error.UnprocessableContent.ForField("currency", "iso4217")));

Result<string> NotBlank(string? raw) =>
    raw.EnsureNotNullOrWhiteSpace(new Error.BadRequest("blank", InputPointer.Root));
```

#### Check / CheckIf families — `CheckExtensions`, `CheckExtensionsAsync`, `CheckIfExtensions`, `CheckIfExtensionsAsync`

Run a side-effect validator that returns its own `Result`/`Result<TK>` while preserving the upstream success value. `CheckIf` adds a conditional gate.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<T> Check<T, TK>(this Result<T> result, Func<T, Result<TK>> func)` | `Result<T>` | Validator returning `Result<TK>`; original value is preserved on success. For no-payload validators, return `Result<Unit>`. |
| `public static Task<Result<T>> CheckAsync<T, TK>(this Task<Result<T>> resultTask, Func<T, Task<Result<TK>>> func)` | `Task<Result<T>>` | `CheckExtensionsAsync` covers all Task/ValueTask combinations. |
| `public static Result<T> CheckIf<T, TK>(this Result<T> result, bool condition, Func<T, Result<TK>> func)` | `Result<T>` | Boolean-gated check; runs only when `condition` is true. |
| `public static Result<T> CheckIf<T, TK>(this Result<T> result, Func<T, bool> predicate, Func<T, Result<TK>> func)` | `Result<T>` | Predicate-gated check. |
| `public static Task<Result<T>> CheckIfAsync<T, TK>(this Task<Result<T>> resultTask, bool condition, Func<T, Task<Result<TK>>> func)` | `Task<Result<T>>` | `CheckIfExtensionsAsync` covers all Task/ValueTask × bool-/predicate-gated combinations. |

```csharp
Result<Quote> q = Result.Ok(quote)
    .Check(QuoteValidators.AllItemsInStock)
    .CheckIf(quote.IsExpedited, QuoteValidators.HonorsCutoff);
```

#### Combine family — `CombineExtensions`, `CombineExtensionsAsync`, `CombineErrorExtensions`

Aggregates results into tuples (success-track) or merges errors via `Error.Aggregate` (failure-track). Tuple arities 2–9 are generated.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<(T1, T2)> Combine<T1, T2>(this Result<T1> t1, Result<T2> t2)` | `Result<(T1, T2)>` | Tuple combine; failures fold via `Error.Aggregate`. When either operand is `Result<Unit>`, `Unit` becomes the next tuple element (use `_` in the destructuring lambda to ignore it). |
| `public static Task<Result<(T1, T2)>> CombineAsync<T1, T2>(this Task<Result<T1>> tt1, Task<Result<T2>> tt2)` | `Task<Result<(T1, T2)>>` | `CombineExtensionsAsync` covers every Task/ValueTask × Task/ValueTask combination. |
| `public static Error Combine(this Error? left, Error right)` | `Error` | On `CombineErrorExtensions`: combines two errors into an `Error.Aggregate`, flattening nested aggregates and treating `null` left as right. |

```csharp
return Result.Combine(streetCity, contact)
    .Map(_ => new Address(cmd.Street, cmd.City));
```

#### Discard family — `DiscardExtensions`, `DiscardTaskExtensions`, `DiscardValueTaskExtensions`

Drop the success value entirely (returns `void`/`Task`/`ValueTask`) for intentional fire-and-forget pipelines.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static void Discard<T>(this Result<T> result)` | `void` | Documents intent that the success value is intentionally ignored. |
| `public static Task DiscardAsync<T>(this Task<Result<T>> resultTask)` | `Task` | Awaits and discards; on `DiscardTaskExtensions`. |
| `public static ValueTask DiscardAsync<T>(this ValueTask<Result<T>> resultTask)` | `ValueTask` | ValueTask variant on `DiscardValueTaskExtensions`. |

```csharp
await SendEmailAsync(msg).DiscardAsync(); // intentionally fire-and-forget the value
```

#### AsUnit family — `AsUnitExtensions`

Async wrappers around `Result<T>.AsUnit()` that strip the value while preserving success/failure state.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<Result<Unit>> AsUnitAsync<T>(this Task<Result<T>> resultTask)` | `Task<Result<Unit>>` | Awaits and projects to `Result<Unit>`. |
| `public static ValueTask<Result<Unit>> AsUnitAsync<T>(this ValueTask<Result<T>> resultTask)` | `ValueTask<Result<Unit>>` | ValueTask variant. |

```csharp
Task<Result<Unit>> done = pipeline.RunAsync(input).AsUnitAsync();
```

#### Debug family — `ResultDebugExtensions`, `ResultDebugExtensionsAsync`

Non-allocating diagnostic taps gated by `ResultDebugSettings`. They never alter the result; they only emit through `Debug.WriteLine` / configured sinks.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<TValue> Debug<TValue>(this Result<TValue> result, string message = "")` | `Result<TValue>` | Logs success or failure with the optional message. |
| `public static Result<TValue> DebugDetailed<TValue>(this Result<TValue> result, string message = "")` | `Result<TValue>` | Includes the success value and full error in the log. |
| `public static Result<TValue> DebugWithStack<TValue>(this Result<TValue> result, string message = "", bool includeStackTrace = true)` | `Result<TValue>` | Adds the current stack trace. |
| `public static Result<TValue> DebugOnSuccess<TValue>(this Result<TValue> result, Action<TValue> action)` | `Result<TValue>` | Custom sink invoked only on success. |
| `public static Result<TValue> DebugOnFailure<TValue>(this Result<TValue> result, Action<Error> action)` | `Result<TValue>` | Custom sink invoked only on failure. |
| `public static Task<Result<TValue>> DebugAsync<TValue>(this Task<Result<TValue>> resultTask, string message = "")` | `Task<Result<TValue>>` | `ResultDebugExtensionsAsync` mirrors every sync overload (`DebugDetailedAsync`, `DebugWithStackAsync`, `DebugOnSuccessAsync`, `DebugOnFailureAsync`) for `Task<Result<T>>` — including `Func<T, Task>` / `Func<Error, Task>` async sinks. |

```csharp
return await LoadAsync(id)
    .DebugAsync("after-load")
    .BindAsync(ChargeAsync)
    .DebugDetailedAsync("after-charge");
```

#### Traverse — `TraverseExtensions`

Folds a sequence of inputs through a `Result`-producing selector into a single `Result<IReadOnlyList<TOut>>`.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<IReadOnlyList<TOut>> Traverse<TIn, TOut>(this IEnumerable<TIn> source, Func<TIn, Result<TOut>> selector)` | `Result<IReadOnlyList<TOut>>` | Sync traversal; short-circuits on the first failure. |
| `public static Task<Result<IReadOnlyList<TOut>>> TraverseAsync<TIn, TOut>(this IEnumerable<TIn> source, Func<TIn, Task<Result<TOut>>> selector)` | `Task<Result<IReadOnlyList<TOut>>>` | Async traversal; sequential evaluation. |
| `public static Task<Result<IReadOnlyList<TOut>>> TraverseAsync<TIn, TOut>(this IEnumerable<TIn> source, Func<TIn, CancellationToken, Task<Result<TOut>>> selector, CancellationToken cancellationToken = default)` | `Task<Result<IReadOnlyList<TOut>>>` | Cancellation-token overload. |
| `public static ValueTask<Result<IReadOnlyList<TOut>>> TraverseAsync<TIn, TOut>(this IEnumerable<TIn> source, Func<TIn, ValueTask<Result<TOut>>> selector)` | `ValueTask<Result<IReadOnlyList<TOut>>>` | ValueTask variant. |
| `public static ValueTask<Result<IReadOnlyList<TOut>>> TraverseAsync<TIn, TOut>(this IEnumerable<TIn> source, Func<TIn, CancellationToken, ValueTask<Result<TOut>>> selector, CancellationToken cancellationToken = default)` | `ValueTask<Result<IReadOnlyList<TOut>>>` | ValueTask + cancellation-token variant. |
| `public static Task<Result<Unit>> TraverseAsync<TIn>(this IEnumerable<TIn> source, Func<TIn, CancellationToken, Task<Result<Unit>>> selector, CancellationToken cancellationToken = default)` | `Task<Result<Unit>>` | No-payload selector overload — short-circuits on the first failure and returns `Result<Unit>` for void-flavoured fan-out. |
| `public static Result<IReadOnlyList<T>> Sequence<T>(this IEnumerable<Result<T>> source)` | `Result<IReadOnlyList<T>>` | Identity-selector form of `Traverse`. Lifts an `IEnumerable<Result<T>>` to `Result<IReadOnlyList<T>>`; short-circuits on the first failure. |
| `public static Result<Unit> Sequence(this IEnumerable<Result<Unit>> source)` | `Result<Unit>` | No-payload `Sequence` overload for void-flavoured pipelines; short-circuits on the first failure. |

```csharp
Task<Result<IReadOnlyList<Order>>> orders =
    ids.TraverseAsync((id, ct) => repo.LoadAsync(id, ct), cancellationToken);

// Sequence: when you already have IEnumerable<Result<T>> from a Select.
Result<IReadOnlyList<Money>> subtotals =
    lineItems.Select(item => item.ComputeSubtotal()).Sequence();
```

#### When / WhenAll — `WhenExtensions`, `WhenExtensionsAsync`, `WhenAllExtensionsAsync`

Conditional execution and async fan-in.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<T> When<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Result<T>> operation)` | `Result<T>` | Runs `operation` only when the predicate holds. |
| `public static Result<T> Unless<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Result<T>> operation)` | `Result<T>` | Inverse of `When`. |
| `public static Task<Result<T>> WhenAsync<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Task<Result<T>>> operation)` | `Task<Result<T>>` | `WhenExtensionsAsync` covers Task/ValueTask × predicate-/no-predicate × Result/Task-Result combinations for both `WhenAsync` and `UnlessAsync`. |
| `public static Task<Result<T>> UnlessAsync<T>(this Task<Result<T>> resultTask, Func<T, Task<Result<T>>> operation)` | `Task<Result<T>>` | Async inverse-`When`. |
| `public static Task<Result<(T1, T2)>> WhenAllAsync<T1, T2>(this (Task<Result<T1>> t1, Task<Result<T2>> t2) tasks)` | `Task<Result<(T1, T2)>>` | `WhenAllExtensionsAsync` runs tasks concurrently via `Task.WhenAll` and folds the results. Tuple arities 2–9 are generated. |

```csharp
Task<Result<(Profile, Preferences)>> bundle =
    (LoadProfileAsync(id), LoadPreferencesAsync(id)).WhenAllAsync();
```

#### ToMaybe — `ToMaybeExtensions`, `ToMaybeExtensionsAsync`

Project a `Result<T>` to a `Maybe<T>` (failure → `None`).

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Maybe<TValue> ToMaybe<TValue>(this Result<TValue> result) where TValue : notnull` | `Maybe<TValue>` | Sync projection. |
| `public static Task<Maybe<TValue>> ToMaybeAsync<TValue>(this Task<Result<TValue>> resultTask) where TValue : notnull` | `Task<Maybe<TValue>>` | Awaits and projects. |
| `public static ValueTask<Maybe<TValue>> ToMaybeAsync<TValue>(this ValueTask<Result<TValue>> resultTask) where TValue : notnull` | `ValueTask<Maybe<TValue>>` | ValueTask variant. |

```csharp
Maybe<Order> maybe = await repo.TryLoadAsync(id).ToMaybeAsync();
```

---

## Pagination

Cursor-based pagination primitives. `Cursor` is opaque to clients; servers choose the encoding. `Page<T>` couples items with adjacent cursors and observable server-side limit clamping.

### `public readonly record struct Cursor`

```csharp
public readonly record struct Cursor
{
    public Cursor(string token);
    public string Token { get; }
}
```

| Member | Description |
| --- | --- |
| `Cursor(string token)` | Constructs a cursor; throws `ArgumentException` if `token` is null or empty. |
| `Token` | The opaque continuation token. Server-defined encoding; clients must echo it back unchanged. |

Absence of a cursor is represented by `null` (`Cursor?`). There is no "empty cursor" — a constructed `Cursor` always carries a non-empty token.

### `public readonly record struct Page<T>`

```csharp
public readonly record struct Page<T>
{
    public Page(
        IReadOnlyList<T> items,
        Cursor? next,
        Cursor? previous,
        int requestedLimit,
        int appliedLimit);

    public IReadOnlyList<T> Items { get; }
    public Cursor?          Next { get; }
    public Cursor?          Previous { get; }
    public int              RequestedLimit { get; }
    public int              AppliedLimit { get; }
    public int              DeliveredCount { get; }
    public bool             WasCapped { get; }
}
```

| Member | Description |
| --- | --- |
| `Page(IReadOnlyList<T>, Cursor?, Cursor?, int, int)` | Validated constructor. Throws `ArgumentNullException` on null `Items`, `ArgumentOutOfRangeException` on a non-positive limit or `AppliedLimit > RequestedLimit`. |
| `Items` | The items returned for this page. Never null when constructed via the public ctor. |
| `Next` | Cursor for the next page, or `null` on the last page. |
| `Previous` | Cursor for the previous page, or `null` on the first page (or when the source doesn't support reverse). |
| `RequestedLimit` | The limit the client requested. |
| `AppliedLimit` | The limit the server actually applied (after server-side cap). |
| `DeliveredCount` | `Items.Count`, defensive against `default(Page<T>)` (returns 0 when `Items` is null). |
| `WasCapped` | `true` when `AppliedLimit < RequestedLimit`. |

### `public static class Page`

Non-generic factory companion (mirrors the `Result` / `Result<T>` split — keeps the generic surface minimal per CA1000).

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Page<T> Empty<T>(int requestedLimit, int appliedLimit)` | `Page<T>` | An empty page (no items, no cursors) for the supplied limits. |

**Wire shape.** `Trellis.Asp` projects `Page<T>` to `200 OK` with a JSON body envelope and a co-emitted `Link` header (RFC 8288). See `HttpResponseExtensions.ToHttpResponse` for the `Result<Page<T>>` overload. Trellis intentionally does **not** use `206 Partial Content` for collection pagination — RFC 9110 §14 was designed for byte-range transfer and lacks proxy/CDN support for collection ranges.

```csharp
public Task<Result<Page<Order>>> List(string? cursorToken, int limit, CancellationToken ct) =>
    repo.ListAsync(cursorToken is null ? null : new Cursor(cursorToken), limit, ct)
        .MapAsync(rows => new Page<Order>(
            Items: rows.Items,
            Next: rows.HasMore ? new Cursor(rows.NextToken!) : null,
            Previous: rows.PrevToken is null ? null : new Cursor(rows.PrevToken),
            RequestedLimit: limit,
            AppliedLimit: Math.Min(limit, MaxLimit)));
```

---

## Error Cases (closed ADT)

| Case | Constructor | Default `Code` | HTTP Status |
| --- | --- | --- | --- |
| `Error.BadRequest` | `(string ReasonCode, InputPointer? At = null)` | `ReasonCode` | 400 |
| `Error.Unauthorized` | `(EquatableArray<AuthChallenge> Challenges = default)` | `unauthorized` | 401 |
| `Error.Forbidden` | `(string PolicyId, ResourceRef? Resource = null)` | `PolicyId` | 403 |
| `Error.NotFound` | `(ResourceRef Resource)` | `not-found` | 404 |
| `Error.MethodNotAllowed` | `(EquatableArray<string> Allow)` | `method-not-allowed` | 405 |
| `Error.NotAcceptable` | `(EquatableArray<string> Available)` | `not-acceptable` | 406 |
| `Error.Conflict` | `(ResourceRef? Resource, string ReasonCode)` | `ReasonCode` | 409 |
| `Error.Gone` | `(ResourceRef Resource)` | `gone` | 410 |
| `Error.PreconditionFailed` | `(ResourceRef Resource, PreconditionKind Condition)` | `precondition-failed` | 412 |
| `Error.ContentTooLarge` | `(long? MaxBytes = null)` | `content-too-large` | 413 |
| `Error.UnsupportedMediaType` | `(EquatableArray<string> Supported)` | `unsupported-media-type` | 415 |
| `Error.RangeNotSatisfiable` | `(long CompleteLength, string Unit = "bytes")` | `range-not-satisfiable` | 416 |
| `Error.UnprocessableContent` | `(EquatableArray<FieldViolation> Fields, EquatableArray<RuleViolation> Rules = default)` | `unprocessable-content` | 422 |
| `Error.PreconditionRequired` | `(PreconditionKind Condition)` | `precondition-required` | 428 |
| `Error.TooManyRequests` | `(RetryAfterValue? RetryAfter = null)` | `too-many-requests` | 429 |
| `Error.InternalServerError` | `(string FaultId)` | `FaultId` | 500 |
| `Error.Unexpected` | `(string ReasonCode)` | `ReasonCode` | 500 |
| `Error.NotImplemented` | `(string Feature)` | `Feature` | 501 |
| `Error.ServiceUnavailable` | `(RetryAfterValue? RetryAfter = null)` | `service-unavailable` | 503 |
| `Error.Aggregate` | `(EquatableArray<Error> Errors)` | `aggregate` | depends on contained errors; serialized via `ProblemDetails.Extensions["errors"]` (RFC 9457) |

---

## Examples

### Result flow

```csharp
using Trellis;

Result<int> Divide(int left, int right) =>
    Result.Ensure(right != 0, new Error.BadRequest("right_must_not_be_zero")
        { Detail = "Right operand must not be zero" })
        .Map(_ => left / right);
```

### Maybe to Result

```csharp
using Trellis;

Maybe<string> maybeEmail = Maybe.From("user@example.com");

Result<string> emailResult = maybeEmail.ToResult(
    Error.UnprocessableContent.ForField("email", "required", "Email is required"));
```

### Reading errors without throwing

```csharp
using Trellis;

Result<Order> result = await mediator.SendAsync(new PlaceOrder(...));

// Pattern-matching: result.Error is null on success, never throws
if (!result.TryGetValue(out var order, out var error))
{
    return error switch
    {
        Error.NotFound nf            => NotFound(nf.Resource.Id),
        Error.UnprocessableContent uc => UnprocessableEntity(uc.Fields),
        Error.Conflict c             => Conflict(c.ReasonCode),
        _                            => Problem(error.GetDisplayMessage()),
    };
}

return Ok(order);
```

### Multi-field validation

```csharp
using Trellis;

var streetCity = MaybeInvariant.AllOrNone(cmd.Street, cmd.City, "street", "city");
var contact    = MaybeInvariant.ExactlyOne(cmd.Email, cmd.Phone, "email", "phone");

// Combine merges any UnprocessableContent.Fields/Rules from multiple results
return Result.Combine(streetCity, contact)
    .Map(_ => new Address(cmd.Street, cmd.City));
```


---

## Domain-Driven Design

The DDD primitives (`Aggregate<T>`, `Entity<T>`, `ValueObject`, `Specification<T>`, ...) live in `Trellis.Core`. They share the `Trellis` namespace.

### Types

### `IEntity`

```csharp
public interface IEntity
```

| Name | Type | Description |
| --- | --- | --- |
| `CreatedAt` | `DateTimeOffset` | UTC timestamp for the first successful persistence of the entity. |
| `LastModified` | `DateTimeOffset` | UTC timestamp for the latest successful persistence update. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No methods. |

### `Entity<TId>`

```csharp
public abstract class Entity<TId> : IEntity where TId : notnull
```

| Name | Type | Description |
| --- | --- | --- |
| `Id` | `TId` | Immutable identity value for the entity. |
| `CreatedAt` | `DateTimeOffset` | Infrastructure-managed creation timestamp. |
| `LastModified` | `DateTimeOffset` | Infrastructure-managed last-modified timestamp. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected Entity(TId id)` | — | Initializes the entity identity. |
| `public override bool Equals(object? obj)` | `bool` | Returns `true` for the same reference before checking default IDs; otherwise compares exact runtime type and non-default IDs. |
| `public static bool operator ==(Entity<TId>? a, Entity<TId>? b)` | `bool` | Identity-based equality operator. |
| `public static bool operator !=(Entity<TId>? a, Entity<TId>? b)` | `bool` | Identity-based inequality operator. |
| `public override int GetHashCode()` | `int` | Combines runtime type and `Id`. |

### `IAggregate`

```csharp
public interface IAggregate : IChangeTracking
```

| Name | Type | Description |
| --- | --- | --- |
| `ETag` | `string` | Optimistic concurrency token for the aggregate. |
| `IsChanged` | `bool` | Inherited from `IChangeTracking`; implemented by `Aggregate<TId>` as domain-event-based change tracking by default. |

| Signature | Returns | Description |
| --- | --- | --- |
| `IReadOnlyList<IDomainEvent> UncommittedEvents()` | `IReadOnlyList<IDomainEvent>` | Returns the domain events raised since the last `AcceptChanges()`. |
| `void AcceptChanges()` | `void` | Inherited from `IChangeTracking`; marks the aggregate as committed. |

### `Aggregate<TId>`

```csharp
public abstract class Aggregate<TId> : Entity<TId>, IAggregate where TId : notnull
```

| Name | Type | Description |
| --- | --- | --- |
| `DomainEvents` | `List<IDomainEvent>` | Protected mutable event buffer for derived aggregate methods. |
| `ETag` | `string` | Persistence-managed optimistic concurrency token. |
| `IsChanged` | `bool` | `[JsonIgnore]` virtual change-tracking flag; default implementation is `DomainEvents.Count > 0`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected Aggregate(TId id)` | — | Initializes the aggregate identity. |
| `public IReadOnlyList<IDomainEvent> UncommittedEvents()` | `IReadOnlyList<IDomainEvent>` | Returns a read-only snapshot of current domain events. |
| `public void AcceptChanges()` | `void` | Clears `DomainEvents`. |

### `IDomainEvent`

```csharp
public interface IDomainEvent
```

| Name | Type | Description |
| --- | --- | --- |
| `OccurredAt` | `DateTime` | UTC timestamp for when the domain event occurred. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No methods. |

### `ValueObject`

```csharp
public abstract class ValueObject : IComparable<ValueObject>, IComparable, IEquatable<ValueObject>
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public or protected properties. Equality and ordering are driven by methods. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected abstract IEnumerable<IComparable?> GetEqualityComponents()` | `IEnumerable<IComparable?>` | Returns the ordered components used for equality, comparison, and hash-code generation. |
| `protected static IComparable? MaybeComponent<T>(Maybe<T> maybe) where T : notnull, IComparable` | `IComparable?` | Converts `Maybe<T>` to an equality component by returning the inner value or `null`. |
| `public override bool Equals(object? obj)` | `bool` | Delegates to `Equals(ValueObject? other)`. |
| `public bool Equals(ValueObject? other)` | `bool` | Structural equality check against the same runtime type. |
| `public override int GetHashCode()` | `int` | Computes and caches a hash code from the equality components. |
| `public virtual int CompareTo(ValueObject? other)` | `int` | Compares equality components in order. |
| `public static bool operator ==(ValueObject? a, ValueObject? b)` | `bool` | Structural equality operator. |
| `public static bool operator !=(ValueObject? a, ValueObject? b)` | `bool` | Structural inequality operator. |
| `public static bool operator <(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator based on `CompareTo(ValueObject?)`. |
| `public static bool operator <=(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator based on `CompareTo(ValueObject?)`. |
| `public static bool operator >(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator based on `CompareTo(ValueObject?)`. |
| `public static bool operator >=(ValueObject? left, ValueObject? right)` | `bool` | Ordering operator based on `CompareTo(ValueObject?)`. |

### `ScalarValueObject<TSelf, T>`

```csharp
public abstract class ScalarValueObject<TSelf, T> : ValueObject, IConvertible, IFormattable
where TSelf : ScalarValueObject<TSelf, T>, IScalarValue<TSelf, T>
where T : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `T` | Wrapped scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected ScalarValueObject(T value)` | — | Stores the wrapped scalar value. |
| `protected override IEnumerable<IComparable?> GetEqualityComponents()` | `IEnumerable<IComparable?>` | Default scalar equality uses only `Value`. |
| `public override string ToString()` | `string` | Returns `Value?.ToString() ?? string.Empty`. |
| `public static implicit operator T(ScalarValueObject<TSelf, T> valueObject)` | `T` | Unwraps the scalar value object to its primitive value. |
| `public static TSelf Create(T value)` | `TSelf` | Calls `TSelf.TryCreate(value)` and throws `InvalidOperationException` on failure. |
| `public TypeCode GetTypeCode()` | `TypeCode` | Returns `Type.GetTypeCode(typeof(T))`. |
| `public bool ToBoolean(IFormatProvider? provider)` | `bool` | Converts `Value` with `Convert.ToBoolean`. |
| `public byte ToByte(IFormatProvider? provider)` | `byte` | Converts `Value` with `Convert.ToByte`. |
| `public char ToChar(IFormatProvider? provider)` | `char` | Converts `Value` with `Convert.ToChar`. |
| `public DateTime ToDateTime(IFormatProvider? provider)` | `DateTime` | Converts `Value` with `Convert.ToDateTime`. |
| `public decimal ToDecimal(IFormatProvider? provider)` | `decimal` | Converts `Value` with `Convert.ToDecimal`. |
| `public double ToDouble(IFormatProvider? provider)` | `double` | Converts `Value` with `Convert.ToDouble`. |
| `public short ToInt16(IFormatProvider? provider)` | `short` | Converts `Value` with `Convert.ToInt16`. |
| `public int ToInt32(IFormatProvider? provider)` | `int` | Converts `Value` with `Convert.ToInt32`. |
| `public long ToInt64(IFormatProvider? provider)` | `long` | Converts `Value` with `Convert.ToInt64`. |
| `public sbyte ToSByte(IFormatProvider? provider)` | `sbyte` | Converts `Value` with `Convert.ToSByte`. |
| `public float ToSingle(IFormatProvider? provider)` | `float` | Converts `Value` with `Convert.ToSingle`. |
| `public string ToString(IFormatProvider? provider)` | `string` | Converts `Value` with `Convert.ToString`. |
| `public string ToString(string? format, IFormatProvider? formatProvider)` | `string` | Uses `IFormattable` when the wrapped value supports it; otherwise uses `Convert.ToString`. |
| `public object ToType(Type conversionType, IFormatProvider? provider)` | `object` | Converts `Value` to an arbitrary type via `Convert.ChangeType`. |
| `public ushort ToUInt16(IFormatProvider? provider)` | `ushort` | Converts `Value` with `Convert.ToUInt16`. |
| `public uint ToUInt32(IFormatProvider? provider)` | `uint` | Converts `Value` with `Convert.ToUInt32`. |
| `public ulong ToUInt64(IFormatProvider? provider)` | `ulong` | Converts `Value` with `Convert.ToUInt64`. |

### `AggregateETagExtensions`

```csharp
public static class AggregateETagExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<T> OptionalETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate` | `Result<T>` | If `expectedETags` is `null`, returns the original result unchanged; otherwise enforces strong ETag matching. |
| `public static Result<T> RequireETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate` | `Result<T>` | Requires an `If-Match` value and enforces strong ETag matching. |
| `public static Task<Result<T>> OptionalETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate` | `Task<Result<T>>` | Async `Task` wrapper for `OptionalETag<T>`. |
| `public static ValueTask<Result<T>> OptionalETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate` | `ValueTask<Result<T>>` | Async `ValueTask` wrapper for `OptionalETag<T>`. |
| `public static Task<Result<T>> RequireETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate` | `Task<Result<T>>` | Async `Task` wrapper for `RequireETag<T>`. |
| `public static ValueTask<Result<T>> RequireETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate` | `ValueTask<Result<T>>` | Async `ValueTask` wrapper for `RequireETag<T>`. |

### `Specification<T>`

```csharp
public abstract class Specification<T>
```

| Name | Type | Description |
| --- | --- | --- |
| `CacheCompilation` | `bool` | Protected virtual switch that controls whether `IsSatisfiedBy(T entity)` reuses a lazily compiled delegate. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected Specification()` | — | Initializes the lazy compiled delegate cache. |
| `public abstract Expression<Func<T, bool>> ToExpression()` | `Expression<Func<T, bool>>` | Returns the canonical expression tree for the specification. |
| `public bool IsSatisfiedBy(T entity)` | `bool` | Evaluates the specification in memory. |
| `public Specification<T> And(Specification<T> other)` | `Specification<T>` | Returns a composed AND specification. |
| `public Specification<T> Or(Specification<T> other)` | `Specification<T>` | Returns a composed OR specification. |
| `public Specification<T> Not()` | `Specification<T>` | Returns a negated specification. |
| `public static implicit operator Expression<Func<T, bool>>(Specification<T> spec)` | `Expression<Func<T, bool>>` | Converts the specification directly to its expression tree. |

### `TrellisJsonValidationException`

```csharp
namespace Trellis;

public sealed class TrellisJsonValidationException : System.Text.Json.JsonException
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public TrellisJsonValidationException()` | — | Default constructor. |
| `public TrellisJsonValidationException(string message)` | — | Creates an instance with a curated, user-safe message. |
| `public TrellisJsonValidationException(string message, Exception innerException)` | — | Wraps an inner exception with the supplied message. |

Marker subclass of `System.Text.Json.JsonException` thrown by Trellis JSON converters when a structured value object's invariants are violated during deserialization (e.g., `CompositeValueObjectJsonConverter<Money>` rejecting a negative amount). `Trellis.Asp`'s `ScalarValueValidationMiddleware` recognizes this subtype and surfaces its `Message` and `JsonException.Path` in the resulting Problem Details payload, restoring DX parity with MVC's per-field model-binder error reporting. Plain `JsonException` instances are deliberately not surfaced because their messages can include internal type names; converters opt in to message surfacing by throwing this subclass with a curated message (e.g., `error.GetDisplayMessage()` from a `Result` failure).

## Primitive value object base classes

These types ship in `Trellis.Core`. They are the building blocks for strongly-typed primitive value objects — derive a `partial class` from one of the `Required*<TSelf>` bases and the bundled `Trellis.Core.Generator` source generator emits the `TryCreate` / `Create` / `Parse` / `TryParse` / `JsonConverter` boilerplate. The validation attributes (`StringLengthAttribute`, `RangeAttribute`, `EnumValueAttribute`) attach declarative invariants that the generator wires into the generated validation. The concrete primitives that derive from these bases (`EmailAddress`, `Money`, etc.) live in `Trellis.Primitives` — see [trellis-api-primitives.md](trellis-api-primitives.md).

### `RangeAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RangeAttribute : Attribute
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Constructor arguments are consumed by the source generator; no public properties are exposed. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public RangeAttribute(int minimum, int maximum)` | `RangeAttribute` | Range metadata for `RequiredInt<TSelf>` and whole-number `RequiredDecimal<TSelf>`. |
| `public RangeAttribute(long minimum, long maximum)` | `RangeAttribute` | Range metadata for `RequiredLong<TSelf>`. |
| `public RangeAttribute(double minimum, double maximum)` | `RangeAttribute` | Fractional range metadata for `RequiredDecimal<TSelf>`. |

### `StringLengthAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class StringLengthAttribute : Attribute
```

| Name | Type | Description |
| --- | --- | --- |
| `MaximumLength` | `int` | Inclusive maximum length. |
| `MinimumLength` | `int` | Inclusive minimum length; defaults to `0`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public StringLengthAttribute(int maximumLength)` | `StringLengthAttribute` | Length metadata for `RequiredString<TSelf>`. |

### `EnumValueAttribute`

```csharp
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class EnumValueAttribute : Attribute
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Canonical symbolic name for a `RequiredEnum<TSelf>` member. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public EnumValueAttribute(string value)` | `EnumValueAttribute` | Overrides the default field-name-based symbolic value. |

### `StringExtensions`

```csharp
public static class StringExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Static helper type. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static string NormalizeFieldName(this string? fieldName, string defaultName)` | `string` | Uses `fieldName` when present, otherwise camel-cases `defaultName`. |
| `public static T ParseScalarValue<T>(string? s) where T : class, IScalarValue<T, string>` | `T` | Throws `FormatException` based on `T.TryCreate`. |
| `public static bool TryParseScalarValue<T>([NotNullWhen(true)] string? s, [MaybeNullWhen(false)] out T result) where T : class, IScalarValue<T, string>` | `bool` | Safe parsing helper based on `T.TryCreate`. |
| `public static string ToCamelCase(this string? str)` | `string` | Lowercases the first character only. |

### `RequiredEnumJsonConverter<TRequiredEnum>`

```csharp
public sealed class RequiredEnumJsonConverter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TRequiredEnum> : JsonConverter<TRequiredEnum>
    where TRequiredEnum : RequiredEnum<TRequiredEnum>, IScalarValue<TRequiredEnum, string>
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Converter type; no public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override TRequiredEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)` | `TRequiredEnum?` | Accepts only JSON `string` and `null`; string values are resolved through `RequiredEnum<TRequiredEnum>.TryFromName(name)`. |
| `public override void Write(Utf8JsonWriter writer, TRequiredEnum value, JsonSerializerOptions options)` | `void` | Writes `value.Value` as a JSON string. |

### `RequiredString<TSelf>`

```csharp
public abstract class RequiredString<TSelf> : ScalarValueObject<TSelf, string>
    where TSelf : RequiredString<TSelf>, IScalarValue<TSelf, string>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Inherited scalar value. |
| `Length` | `int` | Convenience access to `Value.Length`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public bool StartsWith(string value)` | `bool` | Delegates to `string.StartsWith(string)`. |
| `public bool Contains(string value)` | `bool` | Delegates to `string.Contains(string)`. |
| `public bool EndsWith(string value)` | `bool` | Delegates to `string.EndsWith(string)`. |
| `public static TSelf Create(string value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredGuid<TSelf>`

```csharp
public abstract class RequiredGuid<TSelf> : ScalarValueObject<TSelf, Guid>
    where TSelf : RequiredGuid<TSelf>, IScalarValue<TSelf, Guid>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `Guid` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(Guid value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredInt<TSelf>`

```csharp
public abstract class RequiredInt<TSelf> : ScalarValueObject<TSelf, int>
    where TSelf : RequiredInt<TSelf>, IScalarValue<TSelf, int>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `int` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(int value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredDecimal<TSelf>`

```csharp
public abstract class RequiredDecimal<TSelf> : ScalarValueObject<TSelf, decimal>
    where TSelf : RequiredDecimal<TSelf>, IScalarValue<TSelf, decimal>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `decimal` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(decimal value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredLong<TSelf>`

```csharp
public abstract class RequiredLong<TSelf> : ScalarValueObject<TSelf, long>
    where TSelf : RequiredLong<TSelf>, IScalarValue<TSelf, long>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `long` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(long value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredBool<TSelf>`

```csharp
public abstract class RequiredBool<TSelf> : ScalarValueObject<TSelf, bool>
    where TSelf : RequiredBool<TSelf>, IScalarValue<TSelf, bool>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `bool` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static TSelf Create(bool value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredDateTime<TSelf>`

```csharp
public abstract class RequiredDateTime<TSelf> : ScalarValueObject<TSelf, DateTime>
    where TSelf : RequiredDateTime<TSelf>, IScalarValue<TSelf, DateTime>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `DateTime` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override string ToString()` | `string` | Formats `Value` using invariant round-trip format `"O"`. |
| `public static TSelf Create(DateTime value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredEnum<TSelf>`

```csharp
public abstract class RequiredEnum<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TSelf>
    : IEquatable<RequiredEnum<TSelf>>
    where TSelf : RequiredEnum<TSelf>, IScalarValue<TSelf, string>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Canonical symbolic identity; defaults to the public static field name unless `[EnumValue]` overrides it. |
| `Ordinal` | `int` | Declaration-order metadata; not a wire/storage identity. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IReadOnlyCollection<TSelf> GetAll()` | `IReadOnlyCollection<TSelf>` | Returns all discovered public static readonly members. |
| `public static Result<TSelf> TryFromName(string? name, string? fieldName = null)` | `Result<TSelf>` | Case-insensitive symbolic lookup. |
| `public bool Is(params TSelf[] values)` | `bool` | True when this instance matches any provided member. |
| `public bool IsNot(params TSelf[] values)` | `bool` | Negation of `Is(params TSelf[])`. |
| `public override string ToString()` | `string` | Returns `Value`. |
| `public override int GetHashCode()` | `int` | Case-insensitive hash of `Value`. |
| `public override bool Equals(object? obj)` | `bool` | Case-insensitive symbolic equality. |
| `public bool Equals(RequiredEnum<TSelf>? other)` | `bool` | Case-insensitive symbolic equality. |
| `public static bool operator ==(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Equality operator. |
| `public static bool operator !=(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Inequality operator. |

### Source-generated members

The incremental generator at `Trellis.Core/generator/RequiredPartialClassGenerator.cs` (bundled inside `Trellis.Core.nupkg` at `analyzers/dotnet/cs/Trellis.Core.Generator.dll`) augments partial classes that inherit a `Required*<TSelf>` base type.

#### `RequiredString<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(string? value, string? fieldName = null)
public static TSelf Create(string? value, string? fieldName = null)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(string value)
static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
```

- Built-in validation: null/empty/whitespace rejection, trimming, optional `[StringLength]` checks.

#### `RequiredGuid<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static TSelf NewUniqueV4()
public static TSelf NewUniqueV7()
public static Result<TSelf> TryCreate(Guid value, string? fieldName = null)
public static Result<TSelf> TryCreate(Guid? requiredGuidOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static new TSelf Create(Guid value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(Guid value)
static partial void ValidateAdditional(Guid value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `Guid.Empty` rejection.

#### `RequiredInt<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(int value, string? fieldName = null)
public static Result<TSelf> TryCreate(int? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
public static new TSelf Create(int value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(int value)
static partial void ValidateAdditional(int value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `null` rejection for nullable inputs, optional `[Range(int, int)]`.

#### `RequiredDecimal<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(decimal value, string? fieldName = null)
public static Result<TSelf> TryCreate(decimal? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
public static new TSelf Create(decimal value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(decimal value)
static partial void ValidateAdditional(decimal value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `null` rejection for nullable inputs, optional `[Range(int, int)]` or `[Range(double, double)]`.

#### `RequiredLong<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(long value, string? fieldName = null)
public static Result<TSelf> TryCreate(long? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
public static new TSelf Create(long value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(long value)
static partial void ValidateAdditional(long value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `null` rejection for nullable inputs, optional `[Range(long, long)]`.

#### `RequiredBool<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(bool value, string? fieldName = null)
public static Result<TSelf> TryCreate(bool? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static new TSelf Create(bool value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(bool value)
static partial void ValidateAdditional(bool value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `null` rejection for nullable inputs; `false` is valid.

#### `RequiredDateTime<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(DateTime value, string? fieldName = null)
public static Result<TSelf> TryCreate(DateTime? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
public static new TSelf Create(DateTime value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(DateTime value)
static partial void ValidateAdditional(DateTime value, string fieldName, ref string? errorMessage)
```

- Built-in validation: `DateTime.MinValue` rejection.

#### `RequiredEnum<TSelf>`

```csharp
[JsonConverter(typeof(RequiredEnumJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(string value)
public static Result<TSelf> TryCreate(string? value, string? fieldName = null)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static TSelf Create(string value)
```

- Generated `TryCreate` delegates only to `TryFromName`.
- The enum JSON converter also uses only `TryFromName`; there is no `TryFromValue` path.

### Building your own primitive

```csharp
using System.Globalization;
using Trellis;

namespace Demo;

[StringLength(50)]
public partial class CustomerName : RequiredString<CustomerName> { }

public partial class OrderId : RequiredGuid<OrderId> { }

[Range(1, 999)]
public partial class LineCount : RequiredInt<LineCount> { }

public partial class SubmittedAt : RequiredDateTime<SubmittedAt> { }

public partial class OrderState : RequiredEnum<OrderState>
{
    public static readonly OrderState Draft = new();

    [EnumValue("submitted")]
    public static readonly OrderState Submitted = new();
}

public static class Example
{
    public static void Run()
    {
        var orderId = OrderId.NewUniqueV7();
        var name = CustomerName.Create("Ada");
        var lines = LineCount.TryCreate("42", CultureInfo.InvariantCulture).TryGetValue(out var v) ? v : null!;
        var submittedAt = SubmittedAt.Parse("2026-01-15T12:00:00Z", CultureInfo.InvariantCulture);
        var state = OrderState.Create("submitted");

        _ = (orderId, name, lines, submittedAt, state);
    }
}
```

## Extension methods

### `AggregateETagExtensions`

```csharp
public static Result<T> OptionalETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate
public static Result<T> RequireETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate
public static Task<Result<T>> OptionalETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate
public static ValueTask<Result<T>> OptionalETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate
public static Task<Result<T>> RequireETagAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate
public static ValueTask<Result<T>> RequireETagAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? expectedETags) where T : IAggregate
```

Notes:

- Matching is always strong RFC 9110 comparison.
- `expectedETags is null` means “no `If-Match` header supplied”.
- `expectedETags.Length == 0` fails with `Error.PreconditionFailed` because the header contained only weak ETags.
- `EntityTagValue.Wildcard()` bypasses value comparison and succeeds immediately.

## Internal types

- `AndSpecification<T>`, `OrSpecification<T>`, and `NotSpecification<T>` are internal implementation types returned by the public combinators on `Specification<T>`.

## Code examples

### Aggregate, entity, and ETag validation

```csharp
using System;
using Trellis;

public sealed class OrderId : ScalarValueObject<OrderId, Guid>, IScalarValue<OrderId, Guid>
{
    private OrderId(Guid value) : base(value) { }

    public static Result<OrderId> TryCreate(Guid value, string? fieldName = null) =>
        value == Guid.Empty
            ? Result.Fail<OrderId>(Error.UnprocessableContent.ForField(fieldName ?? "orderId", "required", "Order ID is required."))
            : Result.Ok(new OrderId(value));

    public static Result<OrderId> TryCreate(string? value, string? fieldName = null) =>
        Guid.TryParse(value, out var guid)
            ? TryCreate(guid, fieldName)
            : Result.Fail<OrderId>(Error.UnprocessableContent.ForField(fieldName ?? "orderId", "must_be_guid", "Order ID must be a GUID."));
}

public sealed record OrderPlaced(OrderId OrderId, DateTime OccurredAt) : IDomainEvent;

public sealed class Order : Aggregate<OrderId>
{
    public string Description { get; private set; }

    private Order(OrderId id, string description) : base(id) => Description = description;

    public static Result<Order> Create(string description)
    {
        var order = new Order(OrderId.Create(Guid.NewGuid()), description);
        order.DomainEvents.Add(new OrderPlaced(order.Id, DateTime.UtcNow));
        return Result.Ok(order);
    }
}

Result<Order> orderResult = Order.Create("starter-order");
if (orderResult.TryGetValue(out var order))
{
    var guarded = Result.Ok(order).OptionalETag(new[] { EntityTagValue.Strong(order.ETag) });
}
```

### Specification composition

```csharp
using System;
using System.Linq.Expressions;
using Trellis;

public sealed class Subscription
{
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsCancelled { get; init; }
}

public sealed class ExpiredSubscriptionSpec(DateTimeOffset now) : Specification<Subscription>
{
    public override Expression<Func<Subscription, bool>> ToExpression() =>
        subscription => subscription.ExpiresAt < now;
}

public sealed class ActiveSubscriptionSpec : Specification<Subscription>
{
    public override Expression<Func<Subscription, bool>> ToExpression() =>
        subscription => !subscription.IsCancelled;
}

var spec = new ExpiredSubscriptionSpec(DateTimeOffset.UtcNow)
    .And(new ActiveSubscriptionSpec());
```

## Cross-references

- [Trellis.Core API reference](trellis-api-core.md) — `Result<T>`, `Maybe<T>`, `Error`, `EntityTagValue`, `IScalarValue<TSelf, TPrimitive>`, and `IFormattableScalarValue<TSelf, TPrimitive>`
- [Trellis.Primitives API reference](trellis-api-primitives.md) — built-in scalar and composite value objects that build on these DDD primitives
- [Trellis.EntityFrameworkCore API reference](trellis-api-efcore.md) — EF Core conventions and interceptors for `IEntity`, `IAggregate`, `ValueObject`, and `Maybe<T>`

