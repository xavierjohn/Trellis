---
package: Trellis.Core
namespaces: [Trellis]
types: [Result, "Result<T>", IResult, "IResult<TValue>", "IFailureFactory<TSelf>", IPersistOnFailure, "Maybe<T>", Maybe, MaybeInvariant, Error, ITransportFault, RetryAdvice, RetryClassification, ErrorRetryExtensions, Unit, "Page<T>", Page, Cursor, PageSize, CursorCodec, PageBuilder, "EquatableArray<T>", EquatableArray, ResourceRef, InputPointer, FieldViolation, RuleViolation, IAggregate, "Aggregate<TId>", IETagStampable, IReconstitutionStampable, IEntity, "Entity<TId>", IDomainEvent, IIntegrationEvent, ITrackedAggregateSource, ValueObject, "ScalarValueObject<TSelf,T>", "IScalarValue<TSelf,TPrimitive>", "IFormattableScalarValue<TSelf,TPrimitive>", "RequiredString<TSelf>", "RequiredInt<TSelf>", "RequiredLong<TSelf>", "RequiredDecimal<TSelf>", "RequiredBool<TSelf>", "RequiredGuid<TSelf>", "RequiredDateTime<TSelf>", "RequiredDateTimeOffset<TSelf>", "RequiredEnum<TSelf>", "RequiredEnumJsonConverter<T>", "ParsableJsonConverter<T>", ResultRequiresExplicitHttpMappingConverter, PrimitiveValueObjectTrace, "Specification<T>", TrellisJsonValidationException, RangeAttribute, StringLengthAttribute, NotDefaultAttribute, TrimAttribute, PositiveAttribute, NonNegativeAttribute, NegativeAttribute, NonPositiveAttribute, RailwayTrackAttribute, TrackBehavior, EnumValueAttribute, ResourceCollectionNameAttribute, ResultDebugSettings]
version: v3
last_verified: 2026-06-03
audience: [llm]
---
# Trellis.Core API Reference

**Package:** `Trellis.Core`  
**Namespace:** `Trellis`  
**Purpose:** Provides Trellis result, maybe, scalar-value, and transport-agnostic error primitives for railway-oriented application flows.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#trellis-cross-package-cookbook) — recipes using this package, [trellis-api-http-abstractions.md](trellis-api-http-abstractions.md#package-role), [trellis-api-asp.md](trellis-api-asp.md#use-this-file-when), [trellis-api-primitives.md](trellis-api-primitives.md#trellis-api-primitives).

---

## Use this file when

- You need exact signatures for `Result`, `Result<T>`, `Maybe<T>`, `Error`, `ITransportFault`, `Page<T>`, DDD primitives, specifications, custom primitive base classes, or generated primitive JSON/tracing support.
- You are composing domain/application flows and need the canonical ROP operation: `Bind`, `Map`, `Tap`, `Ensure`, `Combine`, `ParallelAsync`, `AsTask`, or `AsValueTask`.
- You are defining aggregates, entities, domain events, specifications, or source-generated `Required*<TSelf>` value objects.

## Error-handling philosophy

Trellis models **expected failures as values, not exceptions.** Anything a caller can reasonably anticipate — input validation, not-found, conflict, forbidden, optional absence — is returned as `Result<T>` (or modelled as `Maybe<T>`) and pattern-matched at the boundary. This keeps the railway intact and every failure path testable.

`throw` is **not** banned — it is reserved for the *truly exceptional*: a programming error, a "can't happen" invariant that was violated, or a startup/configuration/infrastructure fault the process cannot recover from. The rule is "never throw for an **expected** outcome," **not** "never throw at all."

| Situation | Do | Not |
|---|---|---|
| Expected domain failure (validation, not-found, conflict, forbidden) | `Result.Fail<T>(new Error.X(...))` | `throw` |
| Expected absence of a value | `Maybe<T>` | `null` / `throw` |
| Truly exceptional / unrecoverable (bug, broken environment, violated precondition) | `throw` | wrap a normal outcome in a Result just to avoid throwing |
| Internal "shouldn't happen" you still want to flow as a value | return `new Error.Unexpected(reasonCode, faultId?)` (renders 500 at the boundary, no exception) | `throw new Exception(...)` inside a Result chain |

Analyzer **TRLS010** flags `throw` inside Result chains (`Bind`/`Map`/`Tap`/`Ensure`); reading `result.Error` never throws. Never use `try`/`catch` in Domain or Application layers to drive an *expected* outcome — use `Result.Try(...)` only to convert a genuinely unexpected exception at an integration seam into a typed `Error`.

## Patterns Index

Use this table before searching the long type catalog.

| Goal | Canonical API | See |
|---|---|---|
| Return success/failure without payload | `Result.Ok()` / `Result.Fail(error)` (returns `Result<Unit>`) | [`Result`](#public-static-partial-class-result) |
| Return success/failure with payload | `Result.Ok(value)` / `Result.Fail<T>(error)` | [`Result<TValue>`](#public-readonly-struct-resulttvalue--iresulttvalue-iequatableresulttvalue-ifailurefactoryresulttvalue-ipersistonfailure) |
| Fail but still persist staged work (worker pattern) | `Result.FailAfterCommit<T>(error)` / `Result.FailAfterCommit(error)` | [`Result`](#public-static-partial-class-result), [`IPersistOnFailure`](#public-interface-ipersistonfailure) |
| Classify an `Error` for a worker/consumer retry loop | `error.Classify()` / `error.IsTransient()` / `error.IsPermanent()` / `error.IsFailFast()` / `error.GetRetryAdvice()` | [`Retry classification`](#retry-classification-errorretryextensions) |
| Turn a boolean guard into a result | `Result.Ensure(condition, error)` or `.Ensure(...)` in a chain | [`Result`](#public-static-partial-class-result), [`Ensure family`](#ensure-family--ensureextensions-ensureextensionsasync-ensureallextensions-ensureallextensionsasync) |
| Start independent async result-producing operations concurrently | `Result.ParallelAsync(...)`, then combine the returned tasks | [`Result`](#public-static-partial-class-result) |
| Combine multiple validated *typed* fields into a tuple | static `Result.Combine<T1,T2>(Result<T1>, Result<T2>)` or instance `r1.Combine(r2)`, then `.Map(...)` | [`Combine family`](#combine-family--combineextensions-combineextensionsasync-combineerrorextensions) |
| Combine multiple boolean guards | `Result.Ensure(...).Combine(Result.Ensure(...))` then `.Bind(...)` (extension `Combine` aggregates errors and adds each value as the next tuple element; pass a `Result<Unit>` from a no-payload guard and ignore it with `_` in the next lambda) | [`Combine family`](#combine-family--combineextensions-combineextensionsasync-combineerrorextensions) |
| Adapt an already-computed result to async APIs | `.AsTask()` / `.AsValueTask()` | [`ResultTaskAdapterExtensions`](#task-adapter-family--resulttaskadapterextensions) |
| Model expected absence | `Maybe<T>`, `Maybe.From(value)`, `Maybe<T>.None` | [`Maybe<T>`](#public-readonly-struct-maybet-where-t--notnull) |
| Convert absence to a domain failure | `maybe.ToResult(error)` / `maybe.ToResult(errorFactory)` | [`MaybeExtensions`](#maybeextensions) |
| Convert a nullable reference / value to a domain failure (sync or async) | `obj.ToResult(error)` / `task.ToResultAsync(error)` / `valueTask.ToResultAsync(errorFactory)` — works on `T?` for both `class` and `struct`, plus `Task<T?>` and `ValueTask<T?>` | [`Nullable to Result`](#nullable-to-result--nullableextensions-nullableextensionsasync) |
| Create HTTP-oriented domain errors | Closed `Error` cases plus `ResourceRef.For<TResource>(id)` | [`Error`](#public-abstract-record-error), [`Error Cases`](#error-cases-closed-adt) |
| Page list responses | `new Page<T>(items, next, previous, requestedLimit, appliedLimit)` (or `Page.Empty<T>(...)` when there are no items), `Cursor` | [`Pagination`](#pagination) |
| Model aggregates/entities/events | `Aggregate<TId>`, `Entity<TId>`, `IDomainEvent` | [`Domain-Driven Design`](#domain-driven-design) |
| Define a stable published integration contract | `IIntegrationEvent` | [`IIntegrationEvent`](#iintegrationevent) |
| Move reusable query predicates out of repositories | `Specification<T>` | [`Specification<T>`](#specificationt) |
| Define custom required value objects | `partial class X : RequiredString<X>` / `RequiredGuid<X>` / other `Required*` bases | [`Primitive value object base classes`](#primitive-value-object-base-classes) |
| Extract a success value or throw at a trust boundary (DTO → entity rehydration, JSON deserialization) | `result.GetValueOrThrow($"context message")` / `GetValueOrThrowAsync(...)` | [`GetValueOrThrowExtensions`](#extension-class-catalog-full-signatures) (entry in the extension catalog), [cookbook Recipe 30](trellis-api-cookbook.md#recipe-30--rehydrating-entities-from-persistence-fail-loud-vs-result-track) |

## Canonical async handler skeleton

Every async command/query handler that composes Trellis primitives follows the same await-then-chain shape. **The sync verbs (`Bind`/`Map`/`Ensure`) extend `Result<T>` receivers with sync delegates only. The async verbs (`BindAsync`/`MapAsync`/`EnsureAsync`) extend `Result<T>`, `Task<Result<T>>`, *and* `ValueTask<Result<T>>` receivers; on a sync receiver they take an async delegate (`Task<...>` or `ValueTask<...>`), while on a `Task`/`ValueTask` receiver they additionally provide sync-delegate convenience overloads.** A `Task<Result<T>>` is *not* a `Result<T>` and does not expose the sync extensions — calling `.Bind(...)` on a `Task<Result<T>>` fails with `CS1929: 'Task<Result<T>>' does not contain a definition for 'Bind'`.

```csharp
// Generic handler — Task<Result<TOut>>
public async Task<Result<OrderResponse>> Handle(CreateDraftOrderCommand cmd, CancellationToken ct)
{
    // 1. Sync precondition — produces a Result<Unit>, chains synchronously.
    var preconditions = Result.Ensure(cmd.LineItems.Count > 0,
                            Error.InvalidInput.ForField("lineItems", "required", "..."))
                        .Bind(_ => Result.Ensure(!cmd.HasDuplicates,
                            Error.InvalidInput.ForField("lineItems", "duplicate_product", "...")));

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
| Exception → result helpers | `Result.FromException(ex)` / `Result.FromException<T>(ex)` | *(removed)* | Use `Result.Try` / `Result.TryAsync` for inline exception capture, or log the exception and return `Result.Fail(new Error.Unexpected("unhandled_exception", faultId) { Detail = "An unexpected error occurred while processing the request." })`. Do not copy `ex.Message` into public `Detail`. |
| Implicit operators on `Result<T>` | `Result<T> r = value;` and `Result<T> r = error;` | *(removed)* | Use the explicit factory: `Result.Ok(value)` / `Result.Fail<T>(error)`. The compiler flags every site with CS0029. |
| Non-generic `Result` for void flows | `Result` was a separate `readonly struct` for success/failure with no payload, distinct from `Result<T>`. | The non-generic `Result` instance type was removed. `Result` is now a `public static partial class` factory only; for no-payload success/failure use `Result<Unit>` (returned by parameterless `Result.Ok()` / `Result.Fail(error)` / `Result.Ensure(...)` / `Result.Try(...)` factories). The `Trellis.Unit` type is a public `readonly record struct` with a single value (`Unit.Default`). | Replace `Result` parameter/return types with `Result<Unit>`; replace `Task<Result>` with `Task<Result<Unit>>`; in lambdas after `.Bind(...)` / `BindAsync(...)` accept the `Unit` argument explicitly (`_ =>` or `(Unit _) =>`). |
| `Error` as open class hierarchy | `Error` was a `class` with 18 hand-written subclasses (`ValidationError`, `NotFoundError`, …) and static factory helpers (`Error.Validation(...)`, `Error.NotFound(...)`, …). | `Error` is an `abstract record` with **12 nested `sealed record` cases** (`Error.NotFound`, `Error.InvalidInput`, …). Closed via `private` constructor; no static factories. | Replace not-found factories with `new Error.NotFound(ResourceRef.For<TResource>(id)) { Detail = "..." }`. Replace validation factories with `Error.InvalidInput.ForField(field, code, detail)` or `Error.InvalidInput.ForRule(code, detail)`. Replace concrete subclass type names (`ValidationError`, `NotFoundError`) with `Error.InvalidInput`, `Error.NotFound`. See "Error Cases (closed ADT)" below. | <!-- v1-stale-ok: migration-comparison row intentionally cites removed v1 factories -->
| `MatchErrorExtensions` | `result.MatchError(onValidation: ..., onNotFound: ..., onUnexpected: ...)` | *(removed)* | Use a `switch` expression on the closed ADT: `result.Match(_ => ..., e => e switch { Error.NotFound nf => ..., Error.InvalidInput uc => ..., _ => ... })`. C# verifies exhaustiveness against the closed catalog. |
| `FlattenValidationErrorsExtensions` | `result.FlattenValidationErrors()` | *(removed)* | `Combine` over multiple `Result<T>` automatically merges `Error.InvalidInput.Fields` and `.Rules`. |
| `Error.Instance` field | `error.Instance` (string-shaped HTTP vocabulary) | *(removed)* | The ASP wire layer populates `ProblemDetails.Instance` from the server-relative request path+query (RFC 9457 §3.1). Typed payloads expose `ResourceRef` directly via fields like `Error.NotFound.Resource` for callers that need to assert on the resource identity. |
| Public `Value` / `Error` accessors on `Result<T>` | Both threw on the wrong branch. | `result.Error` is `public Error?` and **never throws** (null on success). The throwing `result.Value` getter was removed entirely because it was the primary cause of unsafe value access. | Read errors with `if (result.Error is { } error) { ... }` or `result.TryGetError(out var error)`. Extract success values with `result.TryGetValue(out var v)`, `result.TryGetValue(out var v, out var err)`, `result.Match(...)`, or `var (ok, v, err) = result;` (Deconstruct). | <!-- stale-doc-ok: migration-comparison row intentionally cites removed value accessor -->
| HTTP transport abstractions package | `Trellis.Core` | `Trellis.Http.Abstractions` | Add a PackageReference to `Trellis.Http.Abstractions` for code that names `WriteOutcome<T>`, `RepresentationMetadata`, `EntityTagValue`, `RetryAfterValue`, `PreconditionKind`, `AuthChallenge`, or `AggregateETagExtensions`. The CLR namespace stays `Trellis`, so most source files only need the package-reference change. |
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
| `public static Result<TValue> FailAfterCommit<TValue>(Error error)` | Persist-on-failure factory. Still a failure (`IsFailure == true`), but sets [`IPersistOnFailure.PersistOnFailure`](#public-interface-ipersistonfailure) so `TransactionalCommandBehavior` (and any other opt-in pipeline behavior) commits staged changes alongside the failure. Canonical use: worker handler that converts a transient external-service rejection into a persisted `permanently_failed` row. Throws `ArgumentNullException` on null `error`. |

> **`null` and `Result.Ok`.** `Ok<TValue>(TValue value)` is unconstrained and performs **no null check** — at runtime a `null` argument yields a *successful* result whose value is `null` (`IsSuccess == true`, `TryGetValue` returns `true` with a `null` out-value). Nullable-reference annotations catch only the obvious mistake: a literal `null` against a non-nullable `T` (e.g. `Result.Ok<string>(null)`) raises the nullable-reference *warning* `CS8625` — a build error only where warnings are promoted (`TreatWarningsAsErrors`, as this repo sets). They do **not** stop the common case — passing an already-nullable value, where inference widens `TValue` to the nullable type and the null success compiles silently. A success that wraps `null` is almost never intended: model optionality with `Maybe<T>` (absence is data) and a missing-but-required value with `Result.Fail(Error.NotFound.For<T>(id))`, not `Result.Ok(null)`.
| `public static Result<Unit> FailAfterCommit(Error error)` | No-payload persist-on-failure factory (returns `Result<Unit>`). |
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

The default exception mapper produces `new Error.Unexpected("unhandled_exception", Guid.NewGuid().ToString("N")) { Detail = "An unexpected error occurred while processing the request." }`. It never copies `Exception.Message` into public `Detail`; log exception details at the call site or provide a custom mapper with safe, domain-specific text. `OperationCanceledException` is always rethrown rather than mapped.

#### Factory Methods

`Ok`, `Fail`, `FailAfterCommit`, `Ensure`, `Try`, `TryAsync`, `Combine`, and `ParallelAsync`. Removed from the current API (see "Breaking changes from v1" above): `Success`, `Failure`, `Success(Func<T>)`, `Failure<T>(Func<Error>)`, `SuccessIf`, `FailureIf`, `SuccessIfAsync`, `FailureIfAsync`, `FromException`, and the non-generic `Result` instance type itself (`CreateFailure`, `IFailureFactory<Result>`, `IEquatable<Result>`, etc.).

---

### `public interface IPersistOnFailure`

Opt-in marker carried by result types whose **failure** outcome should still trigger any post-handler persistence step (notably `TransactionalCommandBehavior`'s commit). The canonical producer is `Result.FailAfterCommit<T>(error)`; the canonical consumer is `TransactionalCommandBehavior`, which checks `result is IPersistOnFailure { PersistOnFailure: true }` to decide whether to commit a failed handler outcome.

> **Per-instance, not type-level.** `Result<T>` implements `IPersistOnFailure` unconditionally — the same struct represents both ordinary failures and persist-on-failure failures, and the per-instance `PersistOnFailure` property discriminates. A type-only `is IPersistOnFailure` check is therefore *insufficient* and would incorrectly include ordinary `Result.Fail<T>(error)` values; consumers must use the property-bound pattern `result is IPersistOnFailure { PersistOnFailure: true }`.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `PersistOnFailure` | `bool` | `true` for instances created via `Result.FailAfterCommit<T>(error)` / `Result.FailAfterCommit(error)`, and for failures projected or aggregated from such an instance by railway operators (e.g. `Map`, `Bind`, `Combine`). The flag is sticky once propagated. `Result.Ok(...)`, `Result.Fail(...)`, and `default(Result<T>)` all return `false`. |

#### Pipeline composition

| Behavior | Persist-on-failure handling |
| --- | --- |
| `TransactionalCommandBehavior` (`Trellis.Mediator`) | Commits staged changes on success **or** persist-on-failure. Commit error on a persist-on-failure outcome replaces the handler error in the returned response. |
| `DomainEventDispatchBehavior` (`Trellis.Mediator`) | Treats persist-on-failure as failure: events are **not** dispatched. Events the handler raised on aggregates remain on those in-memory instances and are discarded with the request scope — they are not a durable retry buffer. Model post-failure side effects via an outbox row or a dedicated follow-up command. |

#### Anti-pattern — composing `FailAfterCommit` with aggregating operators

`Result.FailAfterCommit<T>(error)` is a **leaf** worker-handler operation: it converts a single aggregate's transient external rejection into a persisted `permanently_failed` state and returns. Threading that result through `Combine` / `TraverseAll` / `SequenceAll` / `WhenAllAsync` OR-accumulates the `PersistOnFailure` flag onto the aggregated failure — `TransactionalCommandBehavior` then commits the staged permanent-failure mutation alongside whatever the other legs produced, which is almost never what the handler author intended.

**Restructure** such handlers so the aggregating step runs to its terminal outcome first and `FailAfterCommit` is invoked at the end (or in a follow-up command), never as a leg inside a multi-aggregate composition. See [`trellis-api-anti-patterns.md`](trellis-api-anti-patterns.md#no-analyzer--resultfailaftercommit-composed-with-aggregating-operators) for the WRONG / FIX gallery entry.

---

### `public readonly struct Result<TValue> : IResult<TValue>, IEquatable<Result<TValue>>, IFailureFactory<Result<TValue>>, IPersistOnFailure`

Represents either a successful `TValue` or a failure `Error`.

> **Default-state invariant.** `default(Result<T>)` represents a **failure** carrying
> the shared `new Error.Unexpected("default_initialized")` sentinel — *not* success with `default(T)`.
> All failure-facing APIs (`Error`, `TryGetError`, `Deconstruct`, `Equals`, `GetHashCode`, `ToString`,
> `AsUnit`) route through this sentinel so that `default(Result<T>)` is observationally equivalent to
> `Result.Fail<T>(new Error.Unexpected("default_initialized"))`. Always construct via `Result.Ok(value)`
> or `Result.Fail<T>(error)`. Analyzer **`TRLS019`** flags explicit `default(Result<T>)` at call sites.

> **JSON serialization fails fast.** `Result<T>` (and the `IResult` / `IResult<T>` interfaces) carry a default `[JsonConverter(typeof(ResultRequiresExplicitHttpMappingConverter))]` that throws `NotSupportedException` on any direct `JsonSerializer.Serialize` / `Deserialize` call. The intended pattern is to call `.ToHttpResponse()` from `Trellis.Asp` on the result before it reaches STJ (the resulting `Microsoft.AspNetCore.Http.IResult` writes the body itself; the struct is never serialized), or to unwrap the value via `Match` / `TryGetValue` for non-HTTP contexts. Consumers who genuinely need a raw JSON dump (logging, IPC, storage) can register a converter (or a `JsonConverterFactory`) in `JsonSerializerOptions.Converters` — option-registered converters take precedence over the type-level `[JsonConverter]` attribute. **The override must match the declared static type:** a `JsonConverter<Result<T>>` covers only `Result<T>`-declared values; `IResult<T>`-declared values need `JsonConverter<IResult<T>>`; `IResult`-declared values need `JsonConverter<IResult>`. Use a `JsonConverterFactory` if you need to cover multiple result shapes at once.

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
| `public Result<Unit> AsUnit()` | Discards the success value, returning a `Result<Unit>`. On a default-initialized failure, returns an explicit `Result.Fail(sentinel)` (never another `default`). Preserves the persist-on-failure flag when the source was created via `Result.FailAfterCommit<T>(error)`. |
| `public bool Equals(Result<TValue> other)` | Value equality. Equal if both are success with `EqualityComparer<TValue>.Default.Equals` over the values, or both are failure with equal `Error` *and* equal persist-on-failure intent. Default-initialized failures route through the shared sentinel; an ordinary `Result.Fail(error)` and a `Result.FailAfterCommit(error)` with the same `Error` are **not** equal. |
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

> **Factory naming.** The factory is `Maybe.From(value)` / `Maybe<T>.From(value)` — there is **no** `Some` factory. Writing `Maybe.Some(...)` (a common habit from other option types) raises `CS0117`. Use `From` for presence and `Maybe<T>.None` for absence.

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

Multi-field validation helpers for `Maybe<T>` values. Each method returns `Result<Unit>` — success when the invariant holds, or an `Error.InvalidInput` whose `Fields` list carries one `FieldViolation` per offending field. Field paths are normalized via `InputPointer.ForProperty(name)` (RFC 6901 JSON Pointer).

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Result<Unit> AllOrNone<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | All fields present or all absent. Arities 2, 3, 4. |
| `public static Result<Unit> Requires<T1, T2>(Maybe<T1> source, Maybe<T2> required, string sourceFieldName, string requiredFieldName)` | If `source` is present, `required` must be too. Arity 2. |
| `public static Result<Unit> MutuallyExclusive<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | At most one field may be present. Arities 2, 3, 4. |
| `public static Result<Unit> ExactlyOne<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | Exactly one field must be present. Arities 2, 3, 4. |
| `public static Result<Unit> AtLeastOne<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | At least one field must be present. Arities 2, 3, 4. |

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
| `public Maybe<TResult> Map<TResult>(Func<T, TResult> selector) where TResult : notnull` | Maps present value. A selector that returns `null` collapses to `None` (Maybe never holds `null`); the `notnull` constraint discourages this at compile time but cannot fully enforce it for nullable reference types. |
| `public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none)` | Branches on presence |
| `public Maybe<TResult> Bind<TResult>(Func<T, Maybe<TResult>> selector) where TResult : notnull` | Flat-map |
| `public Maybe<T> Or(T fallback)` | Fallback value |
| `public Maybe<T> Or(Func<T> fallbackFactory)` | Deferred fallback value |
| `public Maybe<T> Or(Maybe<T> fallback)` | Fallback maybe |
| `public Maybe<T> Or(Func<Maybe<T>> fallbackFactory)` | Deferred fallback maybe |
| `public Maybe<T> Where(Func<T, bool> predicate)` | Keeps value only when predicate passes |
| `public bool HasValueWhere(Func<T, bool> predicate)` | `HasValue && predicate(Value)`. The predicate is not invoked when this instance is `None`. `MaybeQueryInterceptor` in `Trellis.EntityFrameworkCore` rewrites this to `EF.Property<T?>(entity, "_field") != null AND predicate-body` for inline expression-bodied lambdas, so the same shape translates to SQL. Method-group conversions and captured `Func<T,bool>` variables are not translatable — only inline lambdas. |
| `public Maybe<T> Tap(Action<T> action)` | Side effect on value |
| `public override bool Equals(object? obj)` | Equality |
| `public bool Equals(Maybe<T> other)` | Equality |
| `public bool Equals(T? other)` | Equality against raw value. `Maybe<T>.None.Equals((T?)null)` returns `true` — the absence of a value converges with the canonical `null` sentinel; use `HasValue` / `HasNoValue` if the distinction matters. |
| `public override int GetHashCode()` | Hash code |
| `public override string ToString()` | Debug string |

#### Operators

| Signature | Notes |
| --- | --- |
| `public static implicit operator Maybe<T>(T value)` | Implicit success-like wrap |
| `public static bool operator ==(Maybe<T> maybe, T value)` | Equality |
| `public static bool operator !=(Maybe<T> maybe, T value)` | Inequality |
| `public static bool operator ==(Maybe<T> first, Maybe<T> second)` | Equality |
| `public static bool operator !=(Maybe<T> first, Maybe<T> second)` | Inequality |

> **Removed.** The `(Maybe<T>, object?)` `==` / `!=` overloads were removed because they silently absorbed cross-type comparisons (e.g. `Maybe<int> count = 5; count == "five"` previously compiled and always returned `false`). For boxed comparisons use the `Equals(object?)` instance method: `maybe.Equals(boxedValue)`.

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
| `static virtual TSelf Create(TPrimitive value)` | Throws on validation failure. **Generic-constraint dispatch**: invoked from generic code via `T.Create(value)` where `T : IScalarValue<T, P>`. Concrete-class call sites (e.g. `EmailAddress.Create("…")`) bind to `ScalarValueObject<TSelf, T>.Create(T)` instead — the static-virtual default does not participate in concrete-type lookup. |

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

Closed discriminated union of domain error values. Each case is a nested `sealed record` carrying a typed payload. The base record has a `private` constructor — only the cases declared in `Error.cs` may inherit, which makes `switch` over an `Error` reference exhaustive at the language level.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `Kind` | `string` | Stable domain slug (e.g. `"not-found"`, `"invalid-input"`). Survives CLR renames. Suitable for telemetry. Wire-format mapping for HTTP problem-details `type` is the boundary's responsibility — see `trellis-api-asp.md`. |
| `Code` | `string` | Per-instance machine-readable code. Defaults to `Kind`; cases whose payload carries a per-instance reason (for example `Conflict`, `Forbidden`, `InvariantViolation`, `Unexpected`) override it. |
| `Detail` | `string?` | Human-readable detail. Init-only (`Detail = "..."`). Boundary renderers prefer it when non-null; otherwise they compute a message from `Kind`/`Code` plus the typed payload. |
| `Cause` | `Error?` | Structured cause chain. **Never holds a live `System.Exception`** — wrap context as a child `Error`. Cycles are detected at `init` and throw `InvalidOperationException`. |

#### Methods

| Signature | Notes |
| --- | --- |
| `public string GetDisplayMessage()` | Computes the rendered detail. Returns `Detail` when non-null; otherwise composes from `Kind`/`Code` and the typed payload. For an `InvalidInput` carrying a single `FieldViolation`, returns just that violation's `Detail`. |
| `public override bool Equals(object? obj)` / `Equals(Error? other)` | Value equality over discriminator + typed payload + `Detail`. **`Cause` is excluded** so two errors with identical surface payload compare equal regardless of how deeply they were wrapped (mirrors `System.Exception` precedent). Collection-bearing payloads use `EquatableArray<T>` for sequence equality. |
| `public override int GetHashCode()` | Hash matches `Equals`. |

#### Construction and case-scoped factories

Construct cases directly: `new Error.NotFound(payload) { Detail = "..." }`. The base `Error` type intentionally exposes no static `Error.Validation(...)` / `Error.NotFound(...)` helpers — every call site names the case it produces. <!-- v1-stale-ok: explanatory note about removed v1 factory helpers --> For the common single-payload shapes, the resource-bearing cases (`NotFound`, `Gone`, `Conflict`, `Forbidden`, `InvariantViolation`) and `InvalidInput` expose **case-scoped** convenience factories (e.g. `Error.NotFound.For<Order>(id, detail)`) that bundle the typed payload and an optional trailing `detail` argument while still naming the case — see each case row below.

> **Why only some cases have factories.** The case-scoped factories exist to remove the one piece of construction ceremony these cases share: wrapping an id into a `ResourceRef` (and, for `InvalidInput`, a field into an `InputPointer`). So **every case that carries a `ResourceRef` provides a `For<TResource>(...)` factory** — `NotFound`, `Gone`, `Conflict`, `Forbidden`, and `InvariantViolation`. The exact companions vary with each case's shape: the resource-subject cases (`NotFound`, `Gone`, `Conflict`, `InvariantViolation`) also expose a `For(type, …)` overload that names the resource type as a string; cases whose resource is *optional* add a resourceless companion (`Conflict.ForReason` / `InvariantViolation.ForReason` / `Forbidden.ForPolicy`); `Forbidden` leads with its `policyId` (`For<TResource>(policyId, id)` / `ForPolicy`) rather than a `For(type, …)` overload. The remaining single-error cases (`AuthenticationRequired`, `RateLimited`, `Unavailable`, `Unexpected`, `TransportFault`) carry no resource, so their primary constructors are already minimal (e.g. `new Error.Unexpected("db_timeout")`) and deliberately have no factory — the constructor *is* the idiomatic path. (`Aggregate` is the composition case — a collection of errors built from its own multi-error constructor, not a single-payload shape.) This is the rule that makes the surface consistent: factory ⇔ resource ceremony, not factory-on-everything.

---

### Concrete error cases

Nested `sealed record` cases under `Error`. The base constructor is `private`, so the case set is closed even though each nested case is publicly instantiable with `new`.

| Case | Constructor | Domain semantics |
| --- | --- | --- |
| `Error.InvalidInput` | `(EquatableArray<FieldViolation> Fields, EquatableArray<RuleViolation> Rules = default)` | Request input failed semantic validation. Use `ForField(...)` / `ForRule(...)` for the common single-violation shapes. |
| `Error.InvariantViolation` | `(string ReasonCode, ResourceRef? Resource = null)` | Domain rule failed outside field-bound request validation; use for cross-aggregate invariants or internal preconditions. Use `For<TResource>(reasonCode, id, detail)` / `For(type, reasonCode, id, detail)` / `ForReason(reasonCode, detail)` (resourceless). `reasonCode` leads (it is the invariant's required identity; the resource id is optional). |
| `Error.NotFound` | `(ResourceRef Resource)` | The addressed resource does not exist. Use `For<TResource>(id, detail)` / `For(type, id, detail)` for the common shape. |
| `Error.Forbidden` | `(string PolicyId, ResourceRef? Resource = null)` | The caller is authenticated but not allowed by the named policy. Use `For<TResource>(policyId, id, detail)` or `ForPolicy(policyId, detail)` (resourceless). |
| `Error.Conflict` | `(ResourceRef? Resource, string ReasonCode)` | The request collides with current state (for example duplicate keys or concurrent modification). Use `For<TResource>(id, reasonCode, detail)` / `For(type, id, reasonCode, detail)` / `ForReason(reasonCode, detail)` (resourceless). |
| `Error.Gone` | `(ResourceRef Resource)` | The resource previously existed but has been permanently removed (tombstone). Use `For<TResource>(id, detail)` / `For(type, id, detail)` for the common shape. |
| `Error.AuthenticationRequired` | `(string? Scheme = null, string? ReasonCode = null)` | Authentication is missing or could not be established. `ReasonCode` (when supplied) distinguishes causes that share the 401 surface — e.g. `"Authentication.InvalidCredentials"` vs `"Authentication.MissingCredentials"` vs `"Authentication.TokenExpired"` — so telemetry, dashboards, and client branching don't have to parse `Detail`. |
| `Error.Unavailable` | `(string? ReasonCode = null, RetryAdvice? Retry = null)` | A dependency or subsystem is temporarily unavailable; retry may succeed later. |
| `Error.RateLimited` | `(RetryAdvice? Retry = null)` | The caller exceeded a quota or rate limit. |
| `Error.Unexpected` | `(string ReasonCode, string? FaultId = null)` | Unhandled internal failure or “shouldn't happen” condition. `FaultId`, when supplied, correlates to telemetry. |
| `Error.Aggregate` | `(EquatableArray<Error> Errors)` <br> `(IEnumerable<Error> errors)` <br> `(params Error[] errors)` | Composition node that flattens nested aggregates and preserves every inner error. |
| `Error.TransportFault` | `(ITransportFault Fault)` | Opaque envelope for lower-layer, transport-specific faults produced outside the domain model. |

#### Closed union — exhaustive matching

The catalog is closed to these 12 cases: `InvalidInput`, `InvariantViolation`, `NotFound`, `Forbidden`, `Conflict`, `Gone`, `AuthenticationRequired`, `Unavailable`, `RateLimited`, `Unexpected`, `Aggregate`, and `TransportFault`. Pattern matching over `Error` stays explicit and exhaustive at the language level.

#### Error.TransportFault envelope

Domain code treats `ITransportFault` as opaque and does not inspect concrete transport payloads. Boundary layers such as `Trellis.Asp` unwrap `HttpError` to synthesize HTTP status codes, companion headers, and problem-details extensions. The built-in HTTP transport payload union lives in [Trellis.Http.Abstractions](trellis-api-http-abstractions.md#httperror).

#### RetryAdvice

`RetryAdvice` is a transport-neutral retry hint: `public readonly record struct RetryAdvice(TimeSpan? After = null, DateTimeOffset? At = null);`. `Error.RateLimited` and `Error.Unavailable` carry it so the boundary can emit `Retry-After` without teaching the domain about HTTP headers.

HTTP-specific status codes, headers, and problem-details `type` tokens are not the domain's responsibility. See [trellis-api-asp.md](trellis-api-asp.md#trellisaspoptions) for the boundary mapping table.

#### Retry classification (ErrorRetryExtensions)

`ErrorRetryExtensions` (static class in the `Trellis` namespace) and the `RetryClassification` enum (`Transient = 0`, `Permanent = 1`, `FailFast = 2`) translate the closed `Error` catalog into the three decisions a worker, message-broker consumer, or outbound-gateway caller has to make on every failed item: retry, give up, or halt the batch.

| Extension on `Error` | Returns | Purpose |
| --- | --- | --- |
| `RetryClassification Classify(this Error error)` | `RetryClassification` | Default mapping for every nested `Error` case. Throws `ArgumentNullException` on `null`. |
| `bool IsTransient(this Error error)` | `bool` | Shorthand for `Classify(error) == RetryClassification.Transient`. |
| `bool IsPermanent(this Error error)` | `bool` | Shorthand for `Classify(error) == RetryClassification.Permanent`. |
| `bool IsFailFast(this Error error)` | `bool` | Shorthand for `Classify(error) == RetryClassification.FailFast`. |
| `RetryAdvice? GetRetryAdvice(this Error error)` | `RetryAdvice?` | Returns advice carried by `Error.RateLimited` / `Error.Unavailable`. Returns `null` for every other case, including `Error.Aggregate` (intentional — see below). |

**Default classification table.** Every non-aggregate nested case of `Error` is enumerated; the catalog is closed so the table is exhaustive at framework-publish time:

| Error case | Classification |
| --- | --- |
| `Error.Unavailable`, `Error.RateLimited`, `Error.Unexpected` | `Transient` |
| `Error.AuthenticationRequired` | `FailFast` |
| `Error.Forbidden`, `Error.InvalidInput`, `Error.InvariantViolation`, `Error.NotFound`, `Error.Gone`, `Error.Conflict`, `Error.TransportFault` | `Permanent` |

`Error.Aggregate` is classified by max-severity over its inner errors (the constructor flattens nested aggregates, so the inners are never themselves `Aggregate`): `FailFast` if any inner classifies as `FailFast`; otherwise `Permanent` if any inner classifies as `Permanent`; otherwise `Transient`. The rule answers "should I retry the operation that produced this aggregate **as an indivisible unit?**" — a mixed `Aggregate(Permanent, Transient)` is `Permanent` because retrying the unit will not change the permanent inner's outcome. Callers that want per-inner retry granularity must iterate over `agg.Errors` themselves.

**`GetRetryAdvice` for `Error.Aggregate` always returns `null`.** Two reasons: (1) returning the first transient inner's advice would contradict `Classify` whenever the aggregate is `Permanent` or `FailFast`, and (2) under-waiting is order-dependent (a `RateLimited(1s)` followed by a `RateLimited(60s)` would suggest 1 s). Callers that want a merged hint must define their own policy over the inner errors.

**Conflict and eventually-consistent reads.** `Error.Conflict` defaults to `Permanent` because a generic outer retry loop cannot perform the conflict-specific work (rowversion reload, natural-key regeneration) that 409-shaped failures usually require. Domains with a meaningful conflict retry strategy should override locally — branch on the concrete shape first and only call `Classify()` in the fallback arm — or use a dedicated primitive such as `DbContext.SaveChangesWithRetryAsync` (see [trellis-api-efcore.md](trellis-api-efcore.md#dbcontextretryextensions)). `Error.NotFound` and `Error.Gone` default to `Permanent`; services whose reads are eventually consistent should override the mapping locally rather than expect the framework default to infer their consistency model.

**Why `Error.Unexpected` is `Transient`.** `Error.Unexpected` wraps unhandled exceptions and "this should not have happened" conditions. A retry can hide a deterministic bug or recover a momentary failure; the default favours availability. Producers should reserve `Error.Unexpected` for unknown internal faults and surface deterministic failures as `Error.InvariantViolation`, `Error.InvalidInput`, or `Error.Conflict` instead. Consumers must still cap retries.

**Why `Error.TransportFault` is `Permanent`.** `ITransportFault` is opaque from `Trellis.Core`'s perspective; concrete payloads (such as `HttpError` in `Trellis.Http.Abstractions`) are defined by transport-specific packages. The retryable transient outcomes those transports produce (HTTP 429, HTTP 503, gRPC `UNAVAILABLE`) are mapped at the boundary to `Error.RateLimited` and `Error.Unavailable` — which carry `RetryAdvice` — and never reach `Error.TransportFault`. Every `HttpError` case shipped today (405, 406, 412, 413, 415, 416, 428) is a caller-side error that will not succeed by waiting and retrying. Transport packages that surface their own retryable transient faults via `Error.TransportFault` should provide a transport-aware classification extension that overrides this default for the specific faults that are genuinely retryable.

#### Supporting types

HTTP-specific supporting types (`AuthChallenge`, `EntityTagValue`, `RetryAfterValue`, `PreconditionKind`, `RepresentationMetadata`, `WriteOutcome<T>`, and `AggregateETagExtensions`) now live in [Trellis.Http.Abstractions](trellis-api-http-abstractions.md#use-this-file-when).

| Type | Shape | Purpose |
| --- | --- | --- |
| `ResourceRef` | `readonly record struct (string Type, string? Id = null)` plus `ResourceRef.For(string type, object? id = null)` and `ResourceRef.For<TResource>(object? id = null)` | Aggregate identity. The `For(...)` helpers convert IDs with invariant formatting when possible. `For<TResource>` peels `Maybe<T>` wrappers and strips generic arity. |
| `InputPointer` | `readonly record struct` with `InputPointer(string Path)` | RFC 6901 JSON Pointer (for example `/email`). Construct simple property names via `InputPointer.ForProperty("email")`, or use `InputPointer.Root` for the document root. |
| `FieldViolation` | `sealed record (InputPointer Field, string ReasonCode, ImmutableDictionary<string,string>? Args = null, string? Detail = null)` | Single per-field violation inside `InvalidInput.Fields`. `Equals` / `GetHashCode` compare `Args` by content. |
| `RuleViolation` | `sealed record (string ReasonCode, EquatableArray<InputPointer> Fields = default, ImmutableDictionary<string,string>? Args = null, string? Detail = null)` | Multi-field invariant or object-level rule inside `InvalidInput.Rules`. `Equals` / `GetHashCode` compare `Args` by content. |
| `ITransportFault` | marker interface | Transport-specific payload contract used by `Error.TransportFault`. HTTP-aware code uses `HttpError` from `Trellis.Http.Abstractions`; other transports can define their own implementations. |
| `RetryAdvice` | `readonly record struct (TimeSpan? After = null, DateTimeOffset? At = null)` | Transport-neutral retry hint carried by `Error.RateLimited` and `Error.Unavailable`. Boundary layers translate it to headers such as `Retry-After`. |
| `EquatableArray<T>` | `readonly struct (ImmutableArray<T> Items)` | Wraps `ImmutableArray<T>` so records get sequence equality instead of reference equality. |

---

### Moved HTTP transport types

`AuthChallenge`, `EntityTagValue`, `RetryAfterValue`, `PreconditionKind`, `RepresentationMetadata`, `WriteOutcome<T>`, and `AggregateETagExtensions` are no longer part of `Trellis.Core`.
Use [Trellis.Http.Abstractions](trellis-api-http-abstractions.md#use-this-file-when) when you need header-aware HTTP payloads, conditional-request helpers, representation metadata, or HTTP-shaped write outcomes.

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

#### Performance characteristics

The per-operation tracing is essentially free when no listener is registered. `AddResultsInstrumentation` is the Trellis-provided helper for registering the `"Trellis.Core"` source with OpenTelemetry; consumers may also call `AddSource("Trellis.Core")` directly or attach an `ActivityListener`. Measured on .NET 10 / x64 with an ambient ASP.NET request activity present (benchmark in `Trellis.Benchmark/TracingOverheadBenchmarks.cs`):

| Pipeline depth | No listener | Listener attached (`AllDataAndRecorded` sampling) |
|---|---|---|
| 1-step `Bind` | ~20 ns, 0 B | ~228 ns, 400 B |
| 5-step `Bind` chain | ~107 ns, 0 B | ~1,135 ns, 2,000 B |
| 10-step `Bind` chain | ~242 ns, 0 B | ~2,266 ns, 4,000 B |
| 10-step `Map` chain | ~115 ns, 0 B | ~2,227 ns, 4,000 B |
| 10-step `Tap` chain | ~176 ns, 0 B | ~2,281 ns, 4,096 B |

**No listener registered (default):** ~14–20 ns per `Bind`/`Map`/`Tap`, **0 bytes allocated**. The per-extension `using var activity = ActivitySource.StartActivity(...)` returns null almost immediately when no consumer has registered the `"Trellis.Core"` source, and the `Result<T>` constructor's `Activity.Current?.SetStatus(...)` updates the ambient activity in place without allocating (subsequent `SetTag` calls update the same dictionary entry; the steady-state allocation count is zero).

**With `AddResultsInstrumentation` registered:** each combinator costs ~200 ns and allocates ~400 B (the Activity object + name + tags). At 10 000 RPS with a 10-step pipeline that's ~22 ms/sec of CPU and ~40 MB/sec of GC pressure — material at high throughput.

#### Granularity guidance

Per-Result-extension spans add limited signal beyond the outer pipeline span (`Trellis.Mediator.TracingBehavior`) or the ASP.NET request span. They appear as a deeply nested tree under the outer span with no business context — most observability backends collapse or charge per span.

- **Production / high-throughput services**: instrument at the pipeline-behavior altitude (already covered by `Trellis.Mediator.TracingBehavior`) and the HTTP-boundary altitude (`AddAspNetCoreInstrumentation`); skip `AddResultsInstrumentation`.
- **Development / debugging / low-rate paths**: register `AddResultsInstrumentation` to get step-by-step ROP visibility. The cost is intentional — you opted in.

The cost is bounded by the consumer's choice; the framework does not gate it further. If `AddResultsInstrumentation` is registered, the spans appear; if not, they don't, and the cost is noise-floor.

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

`ToResult(..., Func<Error> ferror)` validates `ferror` before inspecting the `Maybe<T>` state. A null factory throws `ArgumentNullException` even when the maybe currently has a value; async overloads validate the factory before awaiting the receiver.

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

### `MaybeLinqExtensionsTaskAsync` / `MaybeLinqExtensionsTaskLeftAsync` / `MaybeLinqExtensionsTaskRightAsync`

LINQ query syntax over `Task<Maybe<T>>`. Closes the syntactic gap so `from x in FindAsync()` compiles when `FindAsync()` returns `Task<Maybe<T>>`. Mirrors the Result LINQ async surface.

| Signature |
| --- |
| `public static Task<Maybe<TOut>> Select<TIn, TOut>(this Task<Maybe<TIn>> maybeTask, Func<TIn, TOut> selector) where TIn : notnull where TOut : notnull` |
| `public static Task<Maybe<TResult>> SelectMany<TSource, TCollection, TResult>(this Task<Maybe<TSource>> source, Func<TSource, Task<Maybe<TCollection>>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector) where TSource : notnull where TCollection : notnull where TResult : notnull` |
| `public static Task<Maybe<TResult>> SelectMany<TSource, TCollection, TResult>(this Task<Maybe<TSource>> source, Func<TSource, Maybe<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector) where TSource : notnull where TCollection : notnull where TResult : notnull` |
| `public static Task<Maybe<TResult>> SelectMany<TSource, TCollection, TResult>(this Maybe<TSource> source, Func<TSource, Task<Maybe<TCollection>>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector) where TSource : notnull where TCollection : notnull where TResult : notnull` |
| `public static Task<Maybe<TSource>> Where<TSource>(this Task<Maybe<TSource>> source, Func<TSource, bool> predicate) where TSource : notnull` |

### `MaybeLinqExtensionsValueTaskAsync` / `MaybeLinqExtensionsValueTaskLeftAsync` / `MaybeLinqExtensionsValueTaskRightAsync`

LINQ query syntax over `ValueTask<Maybe<T>>` for zero-allocation scenarios. Same shape as the Task overloads.

| Signature |
| --- |
| `public static ValueTask<Maybe<TOut>> Select<TIn, TOut>(this ValueTask<Maybe<TIn>> maybeTask, Func<TIn, TOut> selector) where TIn : notnull where TOut : notnull` |
| `public static ValueTask<Maybe<TResult>> SelectMany<TSource, TCollection, TResult>(this ValueTask<Maybe<TSource>> source, Func<TSource, ValueTask<Maybe<TCollection>>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector) where TSource : notnull where TCollection : notnull where TResult : notnull` |
| `public static ValueTask<Maybe<TResult>> SelectMany<TSource, TCollection, TResult>(this ValueTask<Maybe<TSource>> source, Func<TSource, Maybe<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector) where TSource : notnull where TCollection : notnull where TResult : notnull` |
| `public static ValueTask<Maybe<TResult>> SelectMany<TSource, TCollection, TResult>(this Maybe<TSource> source, Func<TSource, ValueTask<Maybe<TCollection>>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector) where TSource : notnull where TCollection : notnull where TResult : notnull` |
| `public static ValueTask<Maybe<TSource>> Where<TSource>(this ValueTask<Maybe<TSource>> source, Func<TSource, bool> predicate) where TSource : notnull` |

### `MaybeTaskAdapterExtensions`

| Signature |
| --- |
| `public static Task<Maybe<T>> AsTask<T>(this Maybe<T> maybe) where T : notnull` |
| `public static ValueTask<Maybe<T>> AsValueTask<T>(this Maybe<T> maybe) where T : notnull` |

Wraps a synchronous `Maybe<T>` in a completed `Task` / `ValueTask` for participation in async pipelines and disambiguation of overloads at call sites.

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
| `GetValueOrThrowExtensions` | Production-safe throwing extractor for trust-boundary crossings (DTO → entity rehydration, JSON deserialization). Mirrors `Maybe<T>.GetValueOrThrow(string?)`; throws `InvalidOperationException` on failure. Sync + `Task<Result<T>>` + `ValueTask<Result<T>>` overloads. See cookbook Recipe 30. |
| `ResultLinqExtensions`, `ResultLinqExtensionsTaskAsync`, `ResultLinqExtensionsTaskLeftAsync`, `ResultLinqExtensionsTaskRightAsync`, `ResultLinqExtensionsValueTaskAsync`, `ResultLinqExtensionsValueTaskLeftAsync`, `ResultLinqExtensionsValueTaskRightAsync` | LINQ query syntax support via `Select`/`SelectMany`/`Where` for `Result<T>`, `Task<Result<T>>` and `ValueTask<Result<T>>` (mixed sync/async sources and continuations) |
| `MapExtensions`, `MapExtensionsAsync`, `MapIfExtensions`, `MapOnFailureExtensions` | Success-path mapping, conditional mapping, and failure remapping; tuple overloads generated for arities 2-9 |
| `MatchExtensions`, `MatchExtensionsAsync`, `MatchTupleExtensions`, `MatchTupleExtensionsAsync` | Terminal branching for normal and tuple results. (The previous `MatchErrorExtensions` API was removed — use `result.Match(_ => ..., e => e switch { Error.NotFound nf => ..., ... })` against the closed catalog.) |
| `NullableExtensions`, `NullableExtensionsAsync` | Converts nullable reference/value types to `Result<T>` |
| `RecoverExtensions`, `RecoverExtensionsAsync`, `RecoverOnFailureExtensions`, `RecoverOnFailureExtensionsAsync` | Converts failures into fallback success values or results |
| `TapExtensions`, `TapExtensionsAsync`, `TapOnFailureExtensions`, `TapOnFailureExtensionsAsync` | Side effects on success or failure; tuple overloads generated for arities 2-9 |
| `ToMaybeExtensions`, `ToMaybeExtensionsAsync` | Converts `Result<T>` to `Maybe<T>` |
| `TraverseExtensions`, `SequenceAllExtensions`, `TraverseAllExtensions` | Traverses collections through result-producing functions; `*All` variants accumulate failures via `Error.Combine` instead of short-circuiting |
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

> **Async-lambda overload resolution.** The sync-`Result<T>`-receiver `BindAsync` / `MapAsync` /
> `TapAsync` / `CheckAsync` / `EnsureAsync` / `MatchAsync` Task-delegate overloads carry
> `[OverloadResolutionPriority(1)]` so inline `async` lambdas whose return type would otherwise
> be ambiguous between `Task<Result<R>>` and `ValueTask<Result<R>>` resolve to the Task overload.
> Callers who specifically want the ValueTask overload still get it by passing a strongly-typed
> `Func<T, ValueTask<Result<R>>>` delegate (the Task overload is not applicable, so priority is
> not consulted). The LINQ `SelectMany` overloads in `Trellis.Core/src/Result/Extensions/Linq.*Right.cs`
> are an exception: the priority attribute is applied for documentation but does not currently
> disambiguate across the two distinct extension classes; LINQ query syntax over async Result
> composition still benefits from a typed local delegate when the async return type is ambiguous.

#### Map family — `MapExtensions`, `MapExtensionsAsync`, `MapIfExtensions`, `MapOnFailureExtensions`

Pure transformation of the success value (or failure error). Use `Map` when the lambda returns a plain value; switch to `Bind` when it returns a `Result`.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> func)` | `Result<TOut>` | Synchronous map on `Result<T>`. The selector contract requires a non-null return for reference types — `Map` does not null-check the result and downstream stages will see a `Result<TOut>` carrying a `null` value. Use `Bind` if a step can legitimately produce no value. |
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
            Error.InvalidInput u => UnprocessableEntity(u.Fields),
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

Predicate-based validation. `Ensure` short-circuits on the first failed predicate; `EnsureAll` accumulates every failure via `Error.Combine` (homogeneous `Error.InvalidInput` failures merge into a single `Error.InvalidInput`; heterogeneous failures fold into `Error.Aggregate`) for applicative-style validation.

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
        (x => x.Total > 0,            Error.InvalidInput.ForField("total", "must_be_positive")),
        (x => x.Currency.Length == 3, Error.InvalidInput.ForField("currency", "iso4217")));

Result<string> NotBlank(string? raw) =>
    raw.EnsureNotNullOrWhiteSpace(Error.InvalidInput.ForField(InputPointer.Root, "blank"));
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
| `public static Result<(T1, T2)> Combine<T1, T2>(this Result<T1> t1, Result<T2> t2)` | `Result<(T1, T2)>` | Tuple combine; failures fold via `Error.Combine` (two `Error.InvalidInput` merge into one `Error.InvalidInput`; otherwise `Error.Aggregate`). When either operand is `Result<Unit>`, `Unit` becomes the next tuple element (use `_` in the destructuring lambda to ignore it). |
| `public static Task<Result<(T1, T2)>> CombineAsync<T1, T2>(this Task<Result<T1>> tt1, Task<Result<T2>> tt2)` | `Task<Result<(T1, T2)>>` | `CombineExtensionsAsync` covers every Task/ValueTask × Task/ValueTask combination. Task overloads validate null `Task` inputs before awaiting either side. |
| `public static Error Combine(this Error? left, Error right)` | `Error` | On `CombineErrorExtensions`: if both errors are `Error.InvalidInput`, merges their `Fields`/`Rules` into a single `Error.InvalidInput`; otherwise combines into an `Error.Aggregate` (flattening nested aggregates). Treats `null` left as right. |

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

#### LINQ query-syntax family — `ResultLinqExtensions`, `ResultLinqExtensionsTaskAsync`, `ResultLinqExtensionsTaskLeftAsync`, `ResultLinqExtensionsTaskRightAsync`, `ResultLinqExtensionsValueTaskAsync`, `ResultLinqExtensionsValueTaskLeftAsync`, `ResultLinqExtensionsValueTaskRightAsync`

LINQ query expression support for `Result<T>`, `Task<Result<T>>`, and `ValueTask<Result<T>>`. The async overloads let `from ... in ...` clauses chain async result-producing operations directly — without `await`-ing each step into a sync block. Failures short-circuit subsequent steps with the same semantics as `Bind` / `Map` / `Ensure`.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<TOut> Select<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> selector)` | `Result<TOut>` | Sync `Select` — projects a successful value (delegates to `Map`). |
| `public static Result<TResult> SelectMany<TSource, TCollection, TResult>(this Result<TSource> source, Func<TSource, Result<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)` | `Result<TResult>` | Sync `SelectMany` — enables multi-`from` chains. |
| `public static Result<TSource> Where<TSource>(this Result<TSource> source, Func<TSource, bool> predicate)` | `Result<TSource>` | Sync `Where` — converts to a generic "filtered out" failure when the predicate is false. Prefer `Ensure` for meaningful errors. |
| `public static Task<Result<TOut>> Select<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, TOut> selector)` | `Task<Result<TOut>>` | `Select` over an async receiver. |
| `public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this Task<Result<TSource>> source, Func<TSource, Task<Result<TCollection>>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)` | `Task<Result<TResult>>` | `SelectMany` — async source, async continuation. |
| `public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this Task<Result<TSource>> source, Func<TSource, Result<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)` | `Task<Result<TResult>>` | `SelectMany` — async source, sync continuation (`.Left`). |
| `public static Task<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this Result<TSource> source, Func<TSource, Task<Result<TCollection>>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)` | `Task<Result<TResult>>` | `SelectMany` — sync source, async continuation (`.Right`). |
| `public static Task<Result<TSource>> Where<TSource>(this Task<Result<TSource>> source, Func<TSource, bool> predicate)` | `Task<Result<TSource>>` | `Where` over an async receiver. |
| `public static ValueTask<Result<TOut>> Select<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, Func<TIn, TOut> selector)` | `ValueTask<Result<TOut>>` | `Select` over a `ValueTask` receiver. |
| `public static ValueTask<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this ValueTask<Result<TSource>> source, Func<TSource, ValueTask<Result<TCollection>>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)` | `ValueTask<Result<TResult>>` | `SelectMany` — `ValueTask` source and continuation. |
| `public static ValueTask<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this ValueTask<Result<TSource>> source, Func<TSource, Result<TCollection>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)` | `ValueTask<Result<TResult>>` | `SelectMany` — `ValueTask` source, sync continuation (`.Left`). |
| `public static ValueTask<Result<TResult>> SelectMany<TSource, TCollection, TResult>(this Result<TSource> source, Func<TSource, ValueTask<Result<TCollection>>> collectionSelector, Func<TSource, TCollection, TResult> resultSelector)` | `ValueTask<Result<TResult>>` | `SelectMany` — sync source, `ValueTask` continuation (`.Right`). |
| `public static ValueTask<Result<TSource>> Where<TSource>(this ValueTask<Result<TSource>> source, Func<TSource, bool> predicate)` | `ValueTask<Result<TSource>>` | `Where` over a `ValueTask` receiver. |

```csharp
// All-async LINQ — Task<Result<T>> participates in query syntax directly.
var orderDto = await (
    from user  in GetUserAsync(id)         // Task<Result<User>>
    from order in GetOrderAsync(user)      // Task<Result<Order>>
    select new OrderDto(user, order));

// Mixed sync/async — sync source flows into an async continuation (.Right).
var summary = await (
    from u in LoadCachedUser(id)           // Result<User>
    from o in FetchOrderAsync(u)           // Task<Result<Order>>
    select new Summary(u, o));

// And the reverse — async source with a sync validation step (.Left).
var validated = await (
    from u in LoadUserAsync(id)            // Task<Result<User>>
    from p in ValidatePermissions(u)       // Result<Permissions>
    select new Authorized(u, p));
```

> **CancellationToken pattern.** Closure-capture a `CancellationToken` from the surrounding method and call `ct.ThrowIfCancellationRequested()` inside any async selector that needs to honor cancellation; the query expression itself does not introduce a token parameter.
>
> **Exceptions.** Exceptions thrown inside selectors propagate through the `await` (matching `BindAsync` / `MapAsync` semantics). They are not converted to `Result.Fail`.

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

#### TraverseAll / SequenceAll — `TraverseAllExtensions`, `SequenceAllExtensions`

Accumulating-error counterparts to `Traverse` / `Sequence`. Run the selector over every item (no short-circuit) and fold failures via the existing `Error.Combine` extension. A single failure returns unchanged (no `Error.Aggregate` wrap); multiple `InvalidInput` failures merge their fields/rules; heterogeneous failures flatten into `Error.Aggregate`. Use these when you need to surface every failure (form-style validation) rather than the first.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<IReadOnlyList<TOut>> TraverseAll<TIn, TOut>(this IEnumerable<TIn> source, Func<TIn, Result<TOut>> selector)` | `Result<IReadOnlyList<TOut>>` | Accumulating sync traversal; folds every failure via `Error.Combine`. |
| `public static Task<Result<IReadOnlyList<TOut>>> TraverseAllAsync<TIn, TOut>(this IEnumerable<TIn> source, Func<TIn, Task<Result<TOut>>> selector)` | `Task<Result<IReadOnlyList<TOut>>>` | Accumulating async traversal; selectors are awaited sequentially. |
| `public static Task<Result<IReadOnlyList<TOut>>> TraverseAllAsync<TIn, TOut>(this IEnumerable<TIn> source, Func<TIn, CancellationToken, Task<Result<TOut>>> selector, CancellationToken cancellationToken = default)` | `Task<Result<IReadOnlyList<TOut>>>` | Accumulating async traversal with cancellation; mirrors `TraverseAsync` shape. |
| `public static ValueTask<Result<IReadOnlyList<TOut>>> TraverseAllAsync<TIn, TOut>(this IEnumerable<TIn> source, Func<TIn, ValueTask<Result<TOut>>> selector)` | `ValueTask<Result<IReadOnlyList<TOut>>>` | Accumulating `ValueTask` traversal for zero-allocation scenarios. |
| `public static ValueTask<Result<IReadOnlyList<TOut>>> TraverseAllAsync<TIn, TOut>(this IEnumerable<TIn> source, Func<TIn, CancellationToken, ValueTask<Result<TOut>>> selector, CancellationToken cancellationToken = default)` | `ValueTask<Result<IReadOnlyList<TOut>>>` | Accumulating `ValueTask` traversal with cancellation. |
| `public static Task<Result<Unit>> TraverseAllAsync<TIn>(this IEnumerable<TIn> source, Func<TIn, CancellationToken, Task<Result<Unit>>> selector, CancellationToken cancellationToken = default)` | `Task<Result<Unit>>` | Accumulating `Result<Unit>` traversal with cancellation; void-flavoured pipelines. |
| `public static Result<IReadOnlyList<T>> SequenceAll<T>(this IEnumerable<Result<T>> source)` | `Result<IReadOnlyList<T>>` | Identity-selector accumulating sequence; visits every item, folds failures. |
| `public static Result<Unit> SequenceAll(this IEnumerable<Result<Unit>> source)` | `Result<Unit>` | Accumulating `Sequence` over `Result<Unit>` for void-flavoured pipelines. |

```csharp
// Form-style validation: collect every field error in one pass.
Result<IReadOnlyList<EmailAddress>> emails =
    raw.TraverseAll(EmailAddress.TryCreate);
//   ↳ on multiple invalid entries, returns one Error.InvalidInput
//     whose Fields/Rules concatenate every per-item violation.

// Heterogeneous failures flatten into Error.Aggregate:
Result<IReadOnlyList<Order>> orders =
    operations.SequenceAll();   // Result<NotFound> + Result<Conflict> → Error.Aggregate
```

`TraverseAll` matches `Traverse`'s full async surface: sync, `Task`, `Task` + `CancellationToken`, `ValueTask`, `ValueTask` + `CancellationToken`, plus a `Task<Result<Unit>>` + `CancellationToken` overload. `SequenceAll` is sync-only because the existing `Sequence` is sync-only; if `Sequence` ever gains async siblings, `SequenceAll` follows at the same time.

#### When / WhenAll — `WhenExtensions`, `WhenExtensionsAsync`, `WhenAllExtensionsAsync`

Conditional execution and async fan-in.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<T> When<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Result<T>> operation)` | `Result<T>` | Runs `operation` only when the predicate holds. |
| `public static Result<T> Unless<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Result<T>> operation)` | `Result<T>` | Inverse of `When`. |
| `public static Task<Result<T>> WhenAsync<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Task<Result<T>>> operation)` | `Task<Result<T>>` | `WhenExtensionsAsync` covers Task/ValueTask × predicate-/no-predicate × Result/Task-Result combinations for both `WhenAsync` and `UnlessAsync`. |
| `public static Task<Result<T>> UnlessAsync<T>(this Task<Result<T>> resultTask, Func<T, bool> predicate, Func<T, Task<Result<T>>> operation)` | `Task<Result<T>>` | Async inverse-`When` (a `bool condition` overload also exists). |
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

#### Nullable to Result — `NullableExtensions`, `NullableExtensionsAsync`

Bridge a plain nullable value (`T?` for reference or value types) into the Result track. Mirrors `Maybe<T>.ToResult` for repositories that return raw nullables instead of `Maybe<T>`. The async overloads extend `Task<T?>` and `ValueTask<T?>` directly, so call sites can chain `.ToResultAsync(error)` onto a repository call without an intermediate `await` — matching the receiver pattern used by `BindAsync` / `EnsureAsync` / `MapAsync`.

The `Func<Error>` async overloads delegate to the sync `ToResult` after awaiting, so the `ArgumentNullException.ThrowIfNull(errorFactory)` check fires on the awaited continuation rather than synchronously. The thrown exception still surfaces from the returned `Task` / `ValueTask`. This intentionally differs from `MaybeExtensionsAsync.ToResultAsync(Func<Error>)`, which validates the factory before awaiting.

**Sync — `NullableExtensions` (4 overloads)**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<T> ToResult<T>(this T? nullable, Error error) where T : struct` | `Result<T>` | Value-type variant. `null` → `Fail(error)`; otherwise `Ok(nullable.Value)`. |
| `public static Result<T> ToResult<T>(this T? nullable, Func<Error> errorFactory) where T : struct` | `Result<T>` | Value-type factory variant. Throws `ArgumentNullException` when `errorFactory` is `null`. |
| `public static Result<T> ToResult<T>(this T? obj, Error error) where T : class` | `Result<T>` | Reference-type variant. |
| `public static Result<T> ToResult<T>(this T? obj, Func<Error> errorFactory) where T : class` | `Result<T>` | Reference-type factory variant. |

**Async — `NullableExtensionsAsync` (8 overloads)**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Task<Result<T>> ToResultAsync<T>(this Task<T?> nullableTask, Error error) where T : struct` | `Task<Result<T>>` | `Task<T?>` value-type variant. |
| `public static Task<Result<T>> ToResultAsync<T>(this Task<T?> nullableTask, Func<Error> errorFactory) where T : struct` | `Task<Result<T>>` | `Task<T?>` value-type factory variant. |
| `public static Task<Result<T>> ToResultAsync<T>(this Task<T?> nullableTask, Error error) where T : class` | `Task<Result<T>>` | `Task<T?>` reference-type variant — the canonical "repo returns `Task<User?>`" bridge. |
| `public static Task<Result<T>> ToResultAsync<T>(this Task<T?> nullableTask, Func<Error> errorFactory) where T : class` | `Task<Result<T>>` | `Task<T?>` reference-type factory variant. |
| `public static ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> nullableTask, Error error) where T : struct` | `ValueTask<Result<T>>` | `ValueTask<T?>` value-type variant. |
| `public static ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> nullableTask, Func<Error> errorFactory) where T : struct` | `ValueTask<Result<T>>` | `ValueTask<T?>` value-type factory variant. |
| `public static ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> nullableTask, Error error) where T : class` | `ValueTask<Result<T>>` | `ValueTask<T?>` reference-type variant — the canonical "repo returns `ValueTask<Order?>`" bridge. |
| `public static ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> nullableTask, Func<Error> errorFactory) where T : class` | `ValueTask<Result<T>>` | `ValueTask<T?>` reference-type factory variant. |

```csharp
// Repository returns ValueTask<Order?>. ToResultAsync extends the task receiver
// directly, so no intermediate await + sync ToResult call is required.
private ValueTask<Result<Order>> LoadOrderAsync(OrderId id, CancellationToken ct) =>
    _orderRepository.FindByIdAsync(id, ct)
        .ToResultAsync(new Error.NotFound(ResourceRef.For<Order>(id)));
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
| `Page(IReadOnlyList<T>, Cursor?, Cursor?, int, int)` | Validated constructor. Throws `ArgumentNullException` on null `Items`, `ArgumentException` when `Next`/`Previous` is `default(Cursor)` (use `null` to signal absence), `ArgumentOutOfRangeException` on a non-positive limit or `AppliedLimit > RequestedLimit`. Copies the input sequence so later caller-side list mutations cannot change the page. |
| `Items` | The items returned for this page. Never null; `default(Page<T>)` observes an empty sequence. |
| `Next` | Cursor for the next page, or `null` on the last page. |
| `Previous` | Cursor for the previous page, or `null` on the first page (or when the source doesn't support reverse). |
| `RequestedLimit` | The limit the client requested. |
| `AppliedLimit` | The limit the server actually applied (after server-side cap). |
| `DeliveredCount` | `Items.Count`, defensive against `default(Page<T>)` (returns 0 when `Items` is null). |
| `WasCapped` | `true` when `AppliedLimit < RequestedLimit`. |

`Page<T>` equality and hash code include item sequence contents (not the caller's list reference) plus cursors and limits.

### `public static class Page`

Non-generic factory companion (mirrors the `Result` / `Result<T>` split — keeps the generic surface minimal per CA1000).

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Page<T> Empty<T>(int requestedLimit, int appliedLimit)` | `Page<T>` | An empty page (no items, no cursors) for the supplied limits. |

### `Page<T>.Map<TOut>`

```csharp
public Page<TOut> Map<TOut>(Func<T, TOut> selector);
```

| Member | Description |
| --- | --- |
| `Map<TOut>(Func<T, TOut>)` | Projects each item to a new type, preserving `Next`, `Previous`, `RequestedLimit`, and `AppliedLimit`. Throws `ArgumentNullException` when `selector` is null. Use to return `Page<Dto>` from a repository call that yielded `Page<Entity>` without re-running the cursor/limit ceremony. |

### `public readonly record struct PageSize`

```csharp
public readonly record struct PageSize
{
    public const int Default = 50;
    public const int Max = 100;

    public PageSize(int requested, int applied);   // validates: requested > 0, applied > 0, applied <= requested
    public int  Requested { get; }
    public int  Applied   { get; }
    public bool WasCapped { get; }

    public static PageSize FromRequested(int? requested, int max = Max);
    public static Result<PageSize> TryCreate(int? requested, int max = Max, string? fieldName = null);
}
```

| Member | Description |
| --- | --- |
| `Default` / `Max` | Convention constants. `Default = 50` is used when the client doesn't supply a limit; `Max = 100` is the server-side ceiling. |
| `PageSize(int, int)` | Validated constructor. Throws `ArgumentOutOfRangeException` when either limit is non-positive or when `applied > requested`. |
| `Requested` / `Applied` | The pair the caller asked for and the value the server actually used. Composes directly with `Page<T>` so `WasCapped` round-trips through the wire envelope. |
| `WasCapped` | `true` when `Applied < Requested`. |
| `FromRequested(int?, int)` | Lenient parser. When `requested` is `null` or non-positive, returns `Requested = Default` and `Applied = min(Default, max)` (so a custom `max < Default` still clamps `Applied` and surfaces as `WasCapped`). When `requested` is positive, preserves it verbatim and clamps `Applied` to `max`. |
| `TryCreate(int?, int, string?)` | Strict parser. A `null` `requested` returns `Result.Ok` with `Default` (like `FromRequested`); a non-positive or out-of-range value returns `Result.Fail<PageSize>` with `Error.InvalidInput` (uses `fieldName ?? "pageSize"` for the field violation). |

### `public static class CursorCodec`

```csharp
public static class CursorCodec
{
    public static Cursor Encode<TKey>(TKey id)
        where TKey : notnull;

    public static Result<TKey> TryDecode<TKey>(Cursor cursor, string? fieldName = null)
        where TKey : IParsable<TKey>;

    public static Cursor Encode<TKey>(DateTimeOffset createdAt, TKey id)
        where TKey : notnull;

    public static Result<(DateTimeOffset CreatedAt, TKey Id)> TryDecodeComposite<TKey>(
        Cursor cursor, string? fieldName = null)
        where TKey : IParsable<TKey>;
}
```

| Member | Description |
| --- | --- |
| `Encode<TKey>(TKey)` | Single-key cursor: URL-safe base64 of the key's invariant-culture string form. Supported keys include `Guid`, `long`, `int`, and `string`. Source-generated `Required*` value-object IDs round-trip directly (they inherit `IFormattable` and gain `IParsable<TSelf>` from the generator); only hand-written value objects lacking `IParsable<TSelf>` need projecting to their underlying primitive (`.Value`). |
| `TryDecode<TKey>(Cursor, string?)` | Inverse of the single-key `Encode`. Returns `Error.InvalidInput` (reason code `cursor.malformed`, field `fieldName ?? "cursor"`) on malformed base64, oversized tokens, invalid UTF-8, or unparseable payload. |
| `Encode<TKey>(DateTimeOffset, TKey)` | Composite cursor for stable time-ordered seek: URL-safe base64 of `"{createdAt:O}&#124;{id}"` in invariant culture. |
| `TryDecodeComposite<TKey>` | Inverse of the composite `Encode`. Returns `Error.InvalidInput` (reason code `cursor.malformed`, field `fieldName ?? "cursor"`) on malformed base64, oversized tokens, invalid UTF-8, missing separator, or unparseable segments. Splits at the **first** `&#124;` only, so an Id that happens to contain a pipe is still unambiguous. |

**Opacity, not anti-tamper.** Cursors are server-opaque so that clients don't reverse-engineer the sort key, but the encoding is **not** signed. Services that need to defend against tampering must wrap or replace this codec with a signed variant; authorization filtering must always apply to the underlying query.

**AOT-friendly.** No JSON, no reflection. The codec only uses `Convert.ToBase64String`, URL-safe substitution, and `IParsable<TKey>.TryParse` with `CultureInfo.InvariantCulture`.

### `public static class PageBuilder`

```csharp
public static class PageBuilder
{
    public static Page<T> FromOverFetch<T, TKey>(
        IReadOnlyList<T> overFetched,
        PageSize pageSize,
        Func<T, TKey> idSelector)
        where TKey : notnull;

    public static Page<T> FromOverFetch<T, TKey>(
        IReadOnlyList<T> overFetched,
        PageSize pageSize,
        Func<T, DateTimeOffset> createdAtSelector,
        Func<T, TKey> idSelector)
        where TKey : notnull;
}
```

Storage-agnostic over-fetch slicer. The caller asks the data source for `pageSize.Applied + 1` rows ordered by the same key(s) returned by the selectors; `FromOverFetch` keeps the first `pageSize.Applied` rows, emits a `Next` cursor from the **last kept** item via `CursorCodec`, and returns a validated `Page<T>`. Works with EF Core, Dapper, Cosmos, gRPC, or in-memory sources alike.

**Forward-only.** `Previous` is always `null`. Trellis does not yet ship a reverse-seek API; emitting a previous cursor that the forward URL builder would walk would re-fetch the current page rather than the page before it.

**Selector contract.** The selectors passed to `FromOverFetch` MUST match the sort keys used in the upstream query. Mismatched selectors produce semantically wrong cursors — the boundary item the cursor points at will not be the one the next query would seek past.

**Wire shape.** `Trellis.Asp` projects `Page<T>` to `200 OK` with a JSON body envelope and a co-emitted `Link` header (RFC 8288). See `HttpResponseExtensions.ToHttpResponse` for the `Result<Page<T>>` overload. Trellis intentionally does **not** use `206 Partial Content` for collection pagination — RFC 9110 §14 was designed for byte-range transfer and lacks proxy/CDN support for collection ranges.

```csharp
public async Task<Result<Page<OrderListItem>>> Handle(ListOrdersQuery query, CancellationToken ct)
{
    var pageSize = PageSize.FromRequested(query.Limit);

    Guid? afterId = null;
    if (query.Cursor is { } cursorToken)
    {
        if (cursorToken.Length == 0)
            return Result.Fail<Page<OrderListItem>>(
                Error.InvalidInput.ForField("cursor", "cursor.malformed", "Cursor must not be empty."));

        var decoded = CursorCodec.TryDecode<Guid>(new Cursor(cursorToken), fieldName: "cursor");
        if (!decoded.TryGetValue(out var id, out var cursorError))
            return Result.Fail<Page<OrderListItem>>(cursorError);

        afterId = id;
    }

    var ordered = db.Orders.AsNoTracking().OrderBy(o => o.Id);
    var filtered = afterId is { } cursorId
        ? ordered.Where(o => o.Id.Value > cursorId)
        : (IQueryable<Order>)ordered;

    var rows = await filtered.Take(pageSize.Applied + 1).ToListAsync(ct);

    return Result.Ok(
        PageBuilder.FromOverFetch(rows, pageSize, o => o.Id.Value)
            .Map(o => new OrderListItem(o.Id.Value, o.Total.Amount, o.Total.Currency.Value)));
}
```

---

## Error Cases (closed ADT)

| Case | Constructor | Default Code | Kind slug |
| --- | --- | --- | --- |
| `Error.InvalidInput` | `(EquatableArray<FieldViolation> Fields, EquatableArray<RuleViolation> Rules = default)` | `invalid-input` | `invalid-input` |
| `Error.InvariantViolation` | `(string ReasonCode, ResourceRef? Resource = null)` | `ReasonCode` | `invariant-violation` |
| `Error.NotFound` | `(ResourceRef Resource)` | `not-found` | `not-found` |
| `Error.Forbidden` | `(string PolicyId, ResourceRef? Resource = null)` | `PolicyId` | `forbidden` |
| `Error.Conflict` | `(ResourceRef? Resource, string ReasonCode)` — plus `[JsonIgnore]` init-only `ConstraintName`/`ConstraintTableName` (telemetry-only, set by EF Core helpers such as `TryInsertUniqueAsync`) | `ReasonCode` | `conflict` |
| `Error.Gone` | `(ResourceRef Resource)` | `gone` | `gone` |
| `Error.AuthenticationRequired` | `(string? Scheme = null, string? ReasonCode = null)` | `ReasonCode ?? "authentication-required"` | `authentication-required` |
| `Error.Unavailable` | `(string? ReasonCode = null, RetryAdvice? Retry = null)` | `ReasonCode ?? "unavailable"` | `unavailable` |
| `Error.RateLimited` | `(RetryAdvice? Retry = null)` | `rate-limited` | `rate-limited` |
| `Error.Unexpected` | `(string ReasonCode, string? FaultId = null)` | `ReasonCode` | `unexpected` |
| `Error.Aggregate` | `(EquatableArray<Error> Errors)` <br> `(IEnumerable<Error> errors)` <br> `(params Error[] errors)` | `aggregate` | `aggregate` |
| `Error.TransportFault` | `(ITransportFault Fault)` | `transport-fault` | `transport-fault` |

---

## Examples

### Result flow

```csharp
using Trellis;

Result<int> Divide(int left, int right) =>
    Result.Ensure(right != 0, Error.InvalidInput.ForRule("right_must_not_be_zero", "Right operand must not be zero"))
        .Map(_ => left / right);
```

### Maybe to Result

```csharp
using Trellis;

Maybe<string> maybeEmail = Maybe.From("user@example.com");

Result<string> emailResult = maybeEmail.ToResult(
    Error.InvalidInput.ForField("email", "required", "Email is required"));
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
        Error.InvalidInput uc => UnprocessableEntity(uc.Fields),
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

// Combine merges any InvalidInput.Fields/Rules from multiple results
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
public abstract class Aggregate<TId> : Entity<TId>, IAggregate, IETagStampable, IReconstitutionStampable where TId : notnull
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

### `IETagStampable`

```csharp
public interface IETagStampable
```

Persistence-infrastructure seam for stamping an aggregate's optimistic-concurrency token (`IAggregate.ETag`) without reflection. `Aggregate<TId>` implements it **explicitly**, so the method stays off the domain surface and is reachable only by casting to `IETagStampable`. The EF Core integration stamps the ETag through its change-tracker interceptor; non-EF persistence adapters (Dapper, raw ADO, Cosmos SDK) use this seam instead — on load to restore the stored token, on save to apply a fresh one.

| Signature | Returns | Description |
| --- | --- | --- |
| `void StampETag(string etag)` | `void` | Sets `IAggregate.ETag` to `etag`, which must be a valid unquoted RFC 9110 opaque tag (e.g. `Guid.NewGuid().ToString("N")`). Throws `ArgumentNullException` for null and `ArgumentException` for empty/whitespace or non-opaque-tag characters. |

### `IReconstitutionStampable`

```csharp
public interface IReconstitutionStampable
```

Persistence-infrastructure seam for restoring a reconstituted aggregate's persistence-managed metadata (audit timestamps and the optimistic-concurrency token) and clearing its uncommitted domain events, through an explicit infra-only method. `Aggregate<TId>` implements it **explicitly**. The aggregate author rebuilds *domain* state via its own `Reconstitute(...)` factory (private constructor + assigning get-only properties and private child collections); a non-EF adapter then casts to `IReconstitutionStampable` to restore the *infrastructure* metadata it loaded from storage. Post-conditions: timestamps and ETag restored, no uncommitted domain events (so `IsChanged == false` under the default event-based change tracking). The EF Core integration does the equivalent via its materializer and interceptors.

| Signature | Returns | Description |
| --- | --- | --- |
| `void StampReconstitutedState(DateTimeOffset createdAt, DateTimeOffset lastModified, string etag)` | `void` | Restores audit timestamps and the optimistic-concurrency token, and clears uncommitted domain events. `etag` must be a valid unquoted RFC 9110 opaque tag; throws `ArgumentNullException` for null and `ArgumentException` for empty/whitespace or non-opaque-tag characters. |

### `IDomainEvent`

```csharp
public interface IDomainEvent
```

| Name | Type | Description |
| --- | --- | --- |
| `OccurredAt` | `DateTimeOffset` | Timestamp (with explicit UTC offset) for when the domain event occurred. |

### `IIntegrationEvent`

```csharp
public interface IIntegrationEvent
```

Represents an integration event - the stable, published contract a bounded context emits to the outside world (other services or bounded contexts), as distinct from an [`IDomainEvent`](#idomainevent) which stays inside the context and is raised by aggregates.

Domain events are internal: they are raised by aggregates, dispatched in-process to `IDomainEventHandler<T>`, and free to expose the domain's ubiquitous language because only the owning context observes them. Integration events are external: they are versioned wire contracts other systems depend on, so they should be deliberately shaped, stable, and free of internal domain types.

Integration events are typically translated from domain events: a domain-event handler observes a domain event and produces one or more integration events describing the same business fact in contract terms. Publish them through the transactional outbox so external delivery is atomic with the state change and survives a crash. The outbox relays integration events through `IIntegrationEventPublisher`, whose default implementation fans out to in-process `IIntegrationEventHandler<T>` registrations and can be replaced with a message-broker adapter.

| Name | Type | Description |
| --- | --- | --- |
| `OccurredAt` | `DateTimeOffset` | Timestamp (with explicit UTC offset) for when the business fact this integration event describes occurred. Use this as the single event timestamp. |

### `ITrackedAggregateSource`

```csharp
public interface ITrackedAggregateSource
```

Sidecar contract for a unit-of-work that snapshots the aggregates it persisted during the most recent successful commit. Read by `Trellis.Mediator.TrackedAggregateDomainEventDispatchBehavior<,>` so it can drain events from each tracked aggregate after the commit succeeds, regardless of the command's response shape. `Trellis.EntityFrameworkCore.EfUnitOfWork<TContext>` implements this interface; custom `IUnitOfWork` implementations that need to compose with the tracked dispatcher must also implement it (`AddTrellisUnitOfWork<TContext>()` forwards via cast from `IUnitOfWork` and throws `InvalidOperationException` at first resolve if the cast fails).

| Name | Type | Description |
| --- | --- | --- |
| `CommittedAggregates` | `IReadOnlyList<IAggregate>` | The aggregates the unit-of-work persisted on its most recent successful commit. Empty before any commit, empty after a failed or thrown commit (cleared before save and only repopulated on success), and unchanged during deferred nested commits — only the outermost commit owns the snapshot. |

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
| `public static TSelf Create(T value)` | `TSelf` | Calls `TSelf.TryCreate(value)` and throws `InvalidOperationException` on failure. This is the **concrete-type dispatch** entry point — invoked when the call site names a derived class (e.g. `EmailAddress.Create("…")`). The `IScalarValue<TSelf, TPrimitive>.Create(TPrimitive)` static-virtual member on the interface is the **generic-constraint dispatch** entry point used from generic code (`T.Create(value)` where `T : IScalarValue<T, P>`); the two methods coexist because C# does not route concrete-type calls through interface static-virtual defaults. |
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

Defined in `Trellis.Http.Abstractions`; listed here because the extension methods remain in `namespace Trellis` and are commonly used alongside aggregates.

```csharp
public static class AggregateETagExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<T> OptionalETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate` | `Result<T>` | If `expectedETags` is `null`, returns the original result unchanged; otherwise enforces strong ETag matching. Failure modes wrap `HttpError.PreconditionFailed` in `Error.TransportFault`; `EntityTagValue.Wildcard()` short-circuits to success. |
| `public static Result<T> RequireETag<T>(this Result<T> result, EntityTagValue[]? expectedETags) where T : IAggregate` | `Result<T>` | Requires an `If-Match` value and enforces strong ETag matching. Missing headers now produce `Error.TransportFault(new HttpError.PreconditionRequired(PreconditionKind.IfMatch))`; all other failure modes wrap `HttpError.PreconditionFailed`. |
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

| Property | Type | Description |
| --- | --- | --- |
| `UnprocessableContent` | `Error.InvalidInput?` (init-only) | Optional structured payload describing per-field violations recovered during deserialization. Populated by `CompositeValueObjectJsonConverter<T>` when a composite VO's `TryCreate` returns an `Error.InvalidInput`. When non-null with at least one `FieldViolation`, `Trellis.Asp`'s `ScalarValueValidationMiddleware` emits one wire entry per `FieldViolation` keyed `<parentPath>.<leaf>` (MVC dot+bracket convention) instead of collapsing all leaves into the single `;`-joined `Message`. When `null` or `Fields` is empty (e.g., rules-only `Error.InvalidInput`), the middleware falls back to a single entry under the translated parent path with `Message` as the value, preserving the curated message. |

Marker subclass of `System.Text.Json.JsonException` thrown by Trellis JSON converters when a structured value object's invariants are violated during deserialization (e.g., `CompositeValueObjectJsonConverter<Money>` rejecting a negative amount). `Trellis.Asp`'s `ScalarValueValidationMiddleware` recognizes this subtype and surfaces its content in the resulting Problem Details payload — preferring the structured per-field shape from `Error.InvalidInput` when present (one entry per `FieldViolation`), and falling back to surfacing `Message` and `JsonException.Path` as a single entry otherwise. Plain `JsonException` instances are deliberately not surfaced because their messages can include internal type names; converters opt in to message surfacing by throwing this subclass with a curated message (e.g., `error.GetDisplayMessage()` from a `Result` failure).

## Primitive value object base classes

These types ship in `Trellis.Core`. They are the building blocks for strongly-typed primitive value objects — derive a `partial class` from one of the `Required*<TSelf>` bases and the bundled `Trellis.Core.Generator` source generator emits the `TryCreate` / `Create` / `Parse` / `TryParse` / `JsonConverter` boilerplate. The validation attributes (`StringLengthAttribute`, `RangeAttribute`, `EnumValueAttribute`), opt-in behavior attributes (`NotDefaultAttribute`, `TrimAttribute`), and numeric sign attributes (`PositiveAttribute`, `NonNegativeAttribute`, `NegativeAttribute`, `NonPositiveAttribute`) attach declarative metadata that the generator wires into validation. The concrete primitives that derive from these bases (`EmailAddress`, `Money`, etc.) live in `Trellis.Primitives` — see [trellis-api-primitives.md](trellis-api-primitives.md#types).

#### `Required*<TSelf>` default behavior and opt-outs

Every `Required*<TSelf>` base is **lenient by default**. The generated `TryCreate` rejects `null` for every base and accepts every concrete value. Use `[NotDefault]` to opt into sentinel rejection for a given base, and `[Trim]` to opt into string trimming on `RequiredString<TSelf>`.

| Base | Default rejects | Opt-in attributes |
|---|---|---|
| `RequiredString<TSelf>` | `null` only (accepts `""`, whitespace; no auto-trim) | `[NotDefault]` rejects `""`; `[Trim]` enables trimming; combine for strict trim-then-reject-empty |
| `RequiredGuid<TSelf>` | `null` only (accepts `Guid.Empty`) | `[NotDefault]` rejects `Guid.Empty` |
| `RequiredDateTime<TSelf>` | `null` only (accepts `DateTime.MinValue`) | `[NotDefault]` rejects `DateTime.MinValue` |
| `RequiredDateTimeOffset<TSelf>` | `null` only (accepts `DateTimeOffset.MinValue`) | `[NotDefault]` rejects `DateTimeOffset.MinValue` |
| `RequiredInt<TSelf>` | `null` only (accepts `0`) | `[NotDefault]` rejects `0` |
| `RequiredLong<TSelf>` | `null` only (accepts `0L`) | `[NotDefault]` rejects `0L` |
| `RequiredDecimal<TSelf>` | `null` only (accepts `0m`) | `[NotDefault]` rejects `0m` |
| `RequiredBool<TSelf>` | `null` | (no sentinel — `false` remains valid) |
| `RequiredEnum<TSelf>` | `null`, undeclared member names | (smart-enum lookup via `TryCreate`) |

`RequiredString<TSelf>` validation order: `null` check → trim (only if `[Trim]` present) → empty check (only if `[NotDefault]` present) → `[StringLength]` → `ValidateAdditional`. `[Trim]` without `[NotDefault]` trims the value before storage but does not reject `""` or whitespace-only input. Combine `[Trim, NotDefault]` for trim-then-reject-empty behavior.

Numeric Required bases (`RequiredInt`, `RequiredLong`, `RequiredDecimal`) accept four convenience sign-check attributes — `[Positive]`, `[NonNegative]`, `[Negative]`, `[NonPositive]` — mutually exclusive with each other. `RequiredInt` and `RequiredLong` translate them into the equivalent `[Range]` bounds; `RequiredDecimal` emits a direct sign comparison (the full `decimal` range exceeds what `double`-backed `[Range]` could express). Applying a convenience attribute to a non-numeric base is TRLS043; applying more than one is TRLS044. Note: a `[Range(1, 100)] RequiredInt` rejects `0` via the range message ("must be at least 1"), not a sentinel message — no `[NotDefault]` needed when a range already excludes zero.

The lenient defaults also drive the EF Core `TrellisScalarConverter` read path: only `null` triggers `TrellisPersistenceMappingException` during materialization. When `[NotDefault]` is present, persisted sentinel values also throw. Add `[NotDefault]` for columns where the sentinel is never valid persisted domain state.

### `ResultRequiresExplicitHttpMappingConverter`

```csharp
public sealed class ResultRequiresExplicitHttpMappingConverter : JsonConverterFactory
```

Default `[JsonConverter]` factory attached to `Result<T>`, `IResult`, and `IResult<T>`. Throws `NotSupportedException` on any direct `JsonSerializer.Serialize` / `Deserialize` call, with an actionable message that names the canonical fix:

1. **HTTP path** — call `.ToHttpResponse()` (Trellis.Asp) on the result. The returned `Microsoft.AspNetCore.Http.IResult` writes the body itself; the struct never reaches STJ.
2. **Non-HTTP path** — unwrap the value with `Match` / `TryGetValue` before serialization.
3. **Explicit override** — register a converter (or a `JsonConverterFactory`) in `JsonSerializerOptions.Converters`. Option-registered converters take precedence over the type-level `[JsonConverter]` attribute. **The override must match the declared static type:** a `JsonConverter<Result<T>>` only covers `Result<T>`-declared values; `IResult<T>`-declared values need `JsonConverter<IResult<T>>`; `IResult`-declared values need `JsonConverter<IResult>`. Use a `JsonConverterFactory` whose `CanConvert` matches every shape to cover the mixed case in one registration.

The attribute lives on both the struct AND the interfaces because STJ resolves `[JsonConverter]` against the static declared type: an endpoint declared as `Task<IResult<int>> GetAsync()` would otherwise bypass a converter attached only to the struct, silently producing a public-state dump (for example, `{"IsSuccess": true, "IsFailure": false, "Error": null}` for a success, with no success value) instead of using the explicit HTTP mapping the converter exists to enforce.

| Signature | Returns | Description |
| --- | --- | --- |
| `public override bool CanConvert(Type typeToConvert)` | `bool` | `true` for `Result<T>`, `IResult<T>`, and the non-generic `IResult` interface. |
| `public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)` | `JsonConverter` | Always throws `NotSupportedException` directly — the factory is terminal and never returns a typed converter. Throwing here (instead of returning a converter that throws on `Read` / `Write`) keeps the path AOT-safe: no `MakeGenericType` / `Activator.CreateInstance` reflection is needed, so Native AOT consumers see the actionable Trellis message instead of a "native code not available" error before the message can fire. The exception message names the declared shape (`Result<T>`, `IResult<T>`, or `IResult`) so the consumer sees the exact type to register an override for. |

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

### `NotDefaultAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NotDefaultAttribute : Attribute
```

Opt-in attribute consumed by `Trellis.Core.Generator`. When present, the generated `TryCreate` rejects the type's sentinel value in addition to `null`. On `RequiredString<TSelf>`, rejects `""` (after any trimming applied by `[Trim]`). On `RequiredGuid<TSelf>`, rejects `Guid.Empty`. On numeric bases, rejects `0` / `0L` / `0m`. On date bases, rejects `DateTime.MinValue` or `DateTimeOffset.MinValue`.

| Signature | Returns | Description |
| --- | --- | --- |
| `public NotDefaultAttribute()` | `NotDefaultAttribute` | Marker only — no constructor arguments. |

### `TrimAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TrimAttribute : Attribute
```

Opt-in attribute consumed by `Trellis.Core.Generator`. When present on `RequiredString<TSelf>`, the generated `TryCreate` trims leading and trailing whitespace before applying further checks. When combined with `[NotDefault]`, whitespace-only input trims to `""` and is rejected. Without `[NotDefault]`, trimming normalizes the stored value but does not reject `""` or whitespace-only input.

| Signature | Returns | Description |
| --- | --- | --- |
| `public TrimAttribute()` | `TrimAttribute` | Marker only — no constructor arguments. |

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

### `ResourceCollectionNameAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ResourceCollectionNameAttribute : Attribute
```

Overrides the default URI collection name (`{TypeName.ToLowerInvariant()}s`) used when `Trellis.Asp` synthesises `ProblemDetails.Instance` from a failing `ResourceRef`. Apply to the aggregate type whose default naive plural is wrong or unwanted (for example `[ResourceCollectionName("people")]` on `Person`, or `[ResourceCollectionName("statuses")]` on `Status`).

The attribute validates the supplied name at construction time so misconfiguration fails fast at host start when `services.AddResourceCollectionNames(...)` scans the assembly. The name must be a non-empty single URL path segment composed entirely of RFC 3986 `unreserved` characters: ASCII letters and digits plus `-`, `.`, `_`, and `~`. Reserved characters (`/`, `?`, `#`), percent (`%`), `+`, and whitespace are rejected because the value is emitted unencoded into the synthesised URI. When the attribute is absent and no DI override is registered, the writer falls back to `{TypeName.ToLowerInvariant()}s`.

| Name | Type | Description |
| --- | --- | --- |
| `Name` | `string` | The URL collection segment (for example `"people"`). |

| Signature | Returns | Description |
| --- | --- | --- |
| `public ResourceCollectionNameAttribute(string name)` | `ResourceCollectionNameAttribute` | Throws `ArgumentNullException` if `name` is `null`; throws `ArgumentException` if `name` is empty/whitespace or contains any character outside RFC 3986 `unreserved`. |
| `public static bool IsSafePathSegment(string? name)` | `bool` | Predicate used by the attribute constructor and by `Trellis.Asp.ResourceCollectionNameRegistry` to validate names from DI helpers. Returns `false` for `null`, empty, or whitespace input. Exposed publicly so other layers can apply the same rule. |

### `StringExtensions`

```csharp
public static class StringExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Static helper type. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static string NormalizeFieldName(this string? fieldName, string defaultName)` | `string` | Camel-cases `fieldName` when present; otherwise returns `defaultName` verbatim. |
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
| `public override bool HandleNull` | `bool` | `true`; routes JSON `null` tokens through the converter so required enums can reject them explicitly. |
| `public override TRequiredEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)` | `TRequiredEnum?` | Accepts only JSON `string`; string values are resolved through `TRequiredEnum.TryCreate(name)`, and `null` throws `JsonException`. |
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
| `public bool StartsWith(string value)` | `bool` | Delegates to `string.StartsWith(string)` for EF Core-translatable query predicates. |
| `public bool StartsWith(string value, StringComparison comparisonType)` | `bool` | Delegates to `string.StartsWith(string, StringComparison)`. This overload is not EF Core translatable; use the single-argument overload for queries. |
| `public bool Contains(string value)` | `bool` | Delegates to `string.Contains(string)` for EF Core-translatable query predicates. |
| `public bool Contains(string value, StringComparison comparisonType)` | `bool` | Delegates to `string.Contains(string, StringComparison)`. This overload is not EF Core translatable; use the single-argument overload for queries. |
| `public bool Contains(char value, StringComparison comparisonType)` | `bool` | Delegates to `string.Contains(char, StringComparison)`. This overload is not EF Core translatable; use the single-argument overload for queries. |
| `public bool EndsWith(string value)` | `bool` | Delegates to `string.EndsWith(string)` for EF Core-translatable query predicates. |
| `public bool EndsWith(string value, StringComparison comparisonType)` | `bool` | Delegates to `string.EndsWith(string, StringComparison)`. This overload is not EF Core translatable; use the single-argument overload for queries. |
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

### `RequiredDateTimeOffset<TSelf>`

```csharp
public abstract class RequiredDateTimeOffset<TSelf> : ScalarValueObject<TSelf, DateTimeOffset>
    where TSelf : RequiredDateTimeOffset<TSelf>, IScalarValue<TSelf, DateTimeOffset>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `DateTimeOffset` | Inherited scalar value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public override string ToString()` | `string` | Formats `Value` using invariant round-trip format `"O"`. |
| `public static TSelf Create(DateTimeOffset value)` | `TSelf` | Inherited throwing scalar factory. Source-generated overloads are listed below. |

### `RequiredEnum<TSelf>`

```csharp
public abstract class RequiredEnum<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TSelf>
    : IEquatable<RequiredEnum<TSelf>>, IComparable<RequiredEnum<TSelf>>, IComparable
    where TSelf : RequiredEnum<TSelf>, IScalarValue<TSelf, string>
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `string` | Canonical symbolic identity; defaults to the public static field name unless `[EnumValue]` overrides it. |
| `Ordinal` | `int` | Declaration-order metadata; not a wire/storage identity. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IReadOnlyCollection<TSelf> GetAll()` | `IReadOnlyCollection<TSelf>` | Returns all discovered public static readonly members. |
| `public bool Is(params TSelf[] values)` | `bool` | True when this instance matches any provided member. |
| `public bool IsNot(params TSelf[] values)` | `bool` | Negation of `Is(params TSelf[])`. |
| `public override string ToString()` | `string` | Returns `Value`. |
| `public override int GetHashCode()` | `int` | Case-insensitive hash of `Value`. |
| `public override bool Equals(object? obj)` | `bool` | Case-insensitive symbolic equality. |
| `public bool Equals(RequiredEnum<TSelf>? other)` | `bool` | Case-insensitive symbolic equality. |
| `public static bool operator ==(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Equality operator. |
| `public static bool operator !=(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Inequality operator. |
| `public static implicit operator string(RequiredEnum<TSelf> value)` | `string` | Unwraps the member to its symbolic `Value`, matching the implicit unwrap on `ScalarValueObject<TSelf, T>`-derived primitives so every `Required*` value object converts to its primitive transparently. Member-to-member `==` keeps its case-insensitive `Value` semantics — the exact-match operator above wins over a double conversion to `string`. |
| `public int CompareTo(RequiredEnum<TSelf>? other)` | `int` | Orders by `Ordinal` (declaration order), like the C# `enum` it replaces; stays consistent with `Value`-based equality (`Value` and `Ordinal` are both unique per member); `null` sorts first. |
| `int IComparable.CompareTo(object? obj)` | `int` | Non-generic comparison; enables members to be used as composite `ValueObject` equality components and sorted by the default comparer. Throws `ArgumentException` when `obj` is non-null and not a `RequiredEnum<TSelf>` (consistent with `Equals(object?)`). |
| `public static bool operator <(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Less-than by declaration order. |
| `public static bool operator <=(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Less-than-or-equal by declaration order. |
| `public static bool operator >(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Greater-than by declaration order. |
| `public static bool operator >=(RequiredEnum<TSelf>? left, RequiredEnum<TSelf>? right)` | `bool` | Greater-than-or-equal by declaration order. |

### `ParsableJsonConverter<T>`

```csharp
public class ParsableJsonConverter<T> : JsonConverter<T>
    where T : IParsable<T>
```

Core-owned JSON converter emitted by the `Required*<TSelf>` source generator for non-enum generated primitives. It reads JSON strings, numbers, and booleans; JSON `null` is routed to the generated parse path and fails because generated scalar value objects are non-nullable. Numeric scalar value objects write JSON numbers when their string representation parses invariantly as a decimal; all other values write JSON strings.

| Signature | Returns | Description |
| --- | --- | --- |
| `public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)` | `T?` | Converts supported JSON token types to invariant strings and calls `T.Parse(raw, default)`. |
| `public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)` | `void` | Writes numeric scalar primitives as numbers when possible; otherwise writes `value.ToString()` as a JSON string. |

### `PrimitiveValueObjectTrace`

```csharp
public static class PrimitiveValueObjectTrace
```

Core-owned trace source used by generated `Required*<TSelf>` value objects and concrete primitives. The activity source name remains `"Trellis.Primitives"` for telemetry compatibility and is exposed as `PrimitiveValueObjectTrace.ActivitySourceName`; register it with `AddPrimitiveValueObjectInstrumentation()` from `Trellis.Primitives` when using the primitives package, or call `TracerProviderBuilder.AddSource(PrimitiveValueObjectTrace.ActivitySourceName)` directly for Core-only generated primitives.

> **Versioning:** the OpenTelemetry activity source `Version` is stamped from the assembly that physically contains this type — `Trellis.Core` — even when consumers depend on `Trellis.Primitives`. The two packages ship lockstep from a single `version.json`, so the version reported in spans is the version of both packages.

| Name | Type | Description |
| --- | --- | --- |
| `ActivitySource` | `ActivitySource` | Activity source used by generated primitive creation/parsing/validation operations. |
| `ActivitySourceName` | `string` | Public activity source name for Core-only OpenTelemetry wiring. |

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

- Built-in validation: rejects `null`. `""` and whitespace-only input are accepted without trimming by default. Add `[NotDefault]` to also reject `""`, `[Trim]` to enable trimming, or `[Trim, NotDefault]` for strict trim-then-reject-empty. `[StringLength]` operates after any applied trim.

#### `RequiredGuid<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static TSelf NewUniqueV4()
public static TSelf NewUniqueV7()
public static TSelf NewUniqueV7(TimeProvider timeProvider)
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

- Built-in validation: rejects `null`; `Guid.Empty` is accepted. Add `[NotDefault]` to also reject `Guid.Empty` when the all-zero GUID is not valid domain state.
- `NewUniqueV7(TimeProvider timeProvider)` uses `timeProvider.GetUtcNow()` as the Version 7 timestamp and throws `ArgumentNullException` when `timeProvider` is `null`.

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

- Built-in validation: rejects `null` for nullable inputs; `0` is accepted. Optional `[Range(int, int)]` and sign-check attributes run after the null check. Add `[NotDefault]` to also reject `0`.

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

- Built-in validation: rejects `null` for nullable inputs; `0m` is accepted. Optional `[Range(int, int)]`, `[Range(double, double)]`, and sign-check attributes run after the null check. Add `[NotDefault]` to also reject `0m`.
- String parsing: the plain `TryCreate(string?, string?)` overload uses invariant culture; use the `IFormatProvider` overload for culture-aware decimal formats.

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

- Built-in validation: rejects `null` for nullable inputs; `0L` is accepted. Optional `[Range(long, long)]` and sign-check attributes run after the null check. Add `[NotDefault]` to also reject `0L`.

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

- Built-in validation: rejects `null` for nullable inputs; both `true` and `false` are valid. There is no strictness opt-out for `RequiredBool<TSelf>`.

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

- Built-in validation: rejects `null`; `DateTime.MinValue` is accepted. Add `[NotDefault]` to also reject `DateTime.MinValue` when the BCL minimum value is not valid domain state.

#### `RequiredDateTimeOffset<TSelf>`

```csharp
[JsonConverter(typeof(ParsableJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(DateTimeOffset value, string? fieldName = null)
public static Result<TSelf> TryCreate(DateTimeOffset? valueOrNothing, string? fieldName = null)
public static Result<TSelf> TryCreate(string? stringOrNull, string? fieldName = null)
public static Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null)
public static new TSelf Create(DateTimeOffset value)
public static TSelf Create(string stringValue)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static explicit operator TSelf(DateTimeOffset value)
static partial void ValidateAdditional(DateTimeOffset value, string fieldName, ref string? errorMessage)
```

- Built-in validation: rejects `null`; `DateTimeOffset.MinValue` is accepted. Add `[NotDefault]` to also reject `DateTimeOffset.MinValue` when the BCL minimum value is not valid domain state.

#### `RequiredEnum<TSelf>`

```csharp
[JsonConverter(typeof(RequiredEnumJsonConverter<TSelf>))]
public static Result<TSelf> TryCreate(string value)
public static Result<TSelf> TryCreate(string? value, string? fieldName = null)
public static TSelf Parse(string s, IFormatProvider? provider)
public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out TSelf result)
public static TSelf Create(string value)
```

- `TryCreate(string)` / `TryCreate(string?, string?)` are inherited from the `RequiredEnum<TSelf>` base — an enum's creation is a uniform symbolic lookup, so it is not generated per type. `Create`, `Parse`, and `TryParse` are generated and route through `TryCreate`.
- The enum JSON converter resolves through `TryCreate`; there is no `TryFromValue` or `TryFromName` API.

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

Defined in `Trellis.Http.Abstractions`; the signatures stay the same in `namespace Trellis`.

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
- `expectedETags is null` means "no `If-Match` header supplied". `OptionalETag` returns success unchanged; `RequireETag` fails with `Error.TransportFault(new HttpError.PreconditionRequired(PreconditionKind.IfMatch))` whose `Detail` is `"If-Match header is required."`.
- `expectedETags.Length == 0` fails with `Error.TransportFault(new HttpError.PreconditionFailed(ResourceRef.For<T>(), PreconditionKind.IfMatch))` whose `Detail` is `"If-Match header is empty."`.
- A non-empty array containing only weak tags fails with the same wrapped `HttpError.PreconditionFailed`, with `Detail` = `"If-Match contains only weak ETags. Strong comparison is required."` (RFC 9110 forbids weak comparison for `If-Match`).
- A non-empty array of strong tags with no match fails with the same wrapped `HttpError.PreconditionFailed`, with `Detail` = `"Resource has been modified. Please reload and retry."`.
- `EntityTagValue.Wildcard()` bypasses value comparison and succeeds immediately.

## Internal types

- `AndSpecification<T>`, `OrSpecification<T>`, and `NotSpecification<T>` are internal implementation types returned by the public combinators on `Specification<T>`.

## Code examples

### Aggregate, entity, and ETag validation

```csharp
using System;
using Trellis;

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed record OrderPlaced(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class Order : Aggregate<OrderId>
{
    public string Description { get; private set; }

    private Order(OrderId id, string description) : base(id) => Description = description;

    public static Result<Order> Create(string description)
    {
        var order = new Order(OrderId.NewUniqueV7(), description);
        order.DomainEvents.Add(new OrderPlaced(order.Id, DateTimeOffset.UtcNow));
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

- [Trellis.Core API reference](trellis-api-core.md#trelliscore-api-reference) — `Result<T>`, `Maybe<T>`, `Error`, `ITransportFault`, `IScalarValue<TSelf, TPrimitive>`, and `IFormattableScalarValue<TSelf, TPrimitive>`
- [Trellis.Http.Abstractions API reference](trellis-api-http-abstractions.md#use-this-file-when) — `HttpError`, `EntityTagValue`, `RetryAfterValue`, `RepresentationMetadata`, `WriteOutcome<T>`, and `AggregateETagExtensions`
- [Trellis.Primitives API reference](trellis-api-primitives.md#trellis-api-primitives) — built-in scalar and composite value objects that build on these DDD primitives
- [Trellis.EntityFrameworkCore API reference](trellis-api-efcore.md#trellisentityframeworkcore) — EF Core conventions and interceptors for `IEntity`, `IAggregate`, `ValueObject`, and `Maybe<T>`

