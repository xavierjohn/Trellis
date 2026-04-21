# Trellis.Core API Reference

**Package:** `Trellis.Core`  
**Namespace:** `Trellis`  
**Purpose:** Provides Trellis result, maybe, scalar-value, and HTTP-oriented error primitives for railway-oriented application flows.

See also: [trellis-api-patterns.md](trellis-api-patterns.md), [trellis-api-asp.md](trellis-api-asp.md), [trellis-api-primitives.md](trellis-api-primitives.md).

---

## Breaking changes from v1

Running list of v2 breaking changes in `Trellis.Core`. See `docs/adr/ADR-001-result-api-surface.md` for the full design rationale.

| Change | v1 | v2 | Migration |
|---|---|---|---|
| Result success factory | `Result.Success(value)` / `Result.Success<T>(...)` / `Result.Success()` | `Result.Ok(value)` / `Result.Ok<T>(...)` / `Result.Ok()` | Mechanical find-and-replace of `Result.Success` → `Result.Ok` |
| Result failure factory | `Result.Failure<T>(error)` / `Result.Failure(error)` | `Result.Fail<T>(error)` / `Result.Fail(error)` | Mechanical find-and-replace of `Result.Failure` → `Result.Fail` |
| Deferred success factory | `Result.Success(Func<T> funcOk)` | *(removed)* | Inline the factory: `Result.Ok(funcOk())` |
| Deferred failure factory | `Result.Failure<T>(Func<Error> errorFactory)` | *(removed)* | Inline the factory: `Result.Fail<T>(errorFactory())` |
| Conditional factory | `Result.SuccessIf(cond, value, error)` / `Result.SuccessIf(cond, t1, t2, error)` | *(removed)* | Use a ternary: `cond ? Result.Ok(value) : Result.Fail<T>(error)` |
| Inverse-conditional factory | `Result.FailureIf(cond, value, error)` / `Result.FailureIf(predicate, value, error)` | *(removed)* | Use a ternary: `cond ? Result.Fail<T>(error) : Result.Ok(value)` |
| Async-conditional factories | `Result.SuccessIfAsync(predicate, value, error)` / `Result.FailureIfAsync(predicate, value, error)` | *(removed)* | `await predicate() ? Result.Ok(value) : Result.Fail<T>(error)` (invert as needed) |
| Exception → result helpers | `Result.FromException(ex)` / `Result.FromException<T>(ex)` | *(removed)* | Use `Result.Fail(new Error.InternalServerError(faultId) { Detail = ex.Message, Cause = ... })` or rely on `Result.Try` / `Result.TryAsync` for inline exception capture. |
| Implicit operators on `Result<T>` | `Result<T> r = value;` and `Result<T> r = error;` | *(removed)* | Use the explicit factory: `Result.Ok(value)` / `Result.Fail<T>(error)`. The compiler flags every site with CS0029. |
| Non-generic `Result` for void flows | `Result<Unit>` | `Result` (non-generic struct) | `Result<Unit>` is still accepted for source compat, but new code should write `Result`. `Unit` is retained for tuple-result interop. |
| `Error` as open class hierarchy | `Error` was a `class` with 18 hand-written subclasses (`ValidationError`, `NotFoundError`, …) and static factory helpers (`Error.Validation(...)`, `Error.NotFound(...)`, …). | `Error` is an `abstract record` with **18 nested `sealed record` cases** (`Error.NotFound`, `Error.UnprocessableContent`, …). Closed via `private` constructor; no static factories. | Replace `Error.X("msg")` factories with `new Error.X(payload) { Detail = "msg" }`. Replace concrete subclass type names (`ValidationError`, `NotFoundError`) with `Error.UnprocessableContent`, `Error.NotFound`. See "Error Cases (closed ADT)" below. |
| `MatchErrorExtensions` | `result.MatchError(onValidation: ..., onNotFound: ..., onUnexpected: ...)` | *(removed)* | Use a `switch` expression on the closed ADT: `result.Match(_ => ..., e => e switch { Error.NotFound nf => ..., Error.UnprocessableContent uc => ..., _ => ... })`. C# verifies exhaustiveness against the closed catalog. |
| `FlattenValidationErrorsExtensions` | `result.FlattenValidationErrors()` | *(removed)* | `Combine` over multiple `Result<T>` automatically merges `Error.UnprocessableContent.Fields` and `.Rules`. |
| `Error.Instance` field | `error.Instance` (string-shaped HTTP vocabulary) | *(removed)* | The ASP wire layer synthesizes `ProblemDetails.Instance` from the request URL plus any `ResourceRef` carried by the typed payload. |
| Public `Value` / `Error` accessors on `Result<T>` | Both threw on the wrong branch. | `result.Error` is `public Error?` and **never throws** (null on success). `result.Value` remains `public T` and throws on failure (required because `T` may be a struct whose `default` is indistinguishable from a real value). | Read errors with `if (result.Error is { } error) { ... }` or `result.TryGetError(out var error)`. Read values with `result.Match(...)` / `result.TryGetValue(out var v)`; only call `.Value` after an `IsSuccess` guard. |
| `WriteOutcome<T>` package + namespace | `Trellis.Asp.WriteOutcome<T>` (in `Trellis.Asp`) | `Trellis.WriteOutcome<T>` (in `Trellis.Core`) | Replace `using Trellis.Asp;` with `using Trellis;` for any file that names `WriteOutcome<T>` directly. The type, its case records, and member shapes are unchanged; only the assembly and namespace move. ASP-specific extensions (`ToActionResult`, `ToHttpResult`, `ToUpdatedActionResult*`) still live in `Trellis.Asp`. |
| Package id | `Trellis.Results` | `Trellis.Core` | Replace `<PackageReference Include="Trellis.Results" ... />` with `<PackageReference Include="Trellis.Core" ... />`. The CLR namespace stays `Trellis` — no `using` changes are needed. The legacy `Trellis.Results` package is unlisted at v2.0.0 with a redirect notice; there is no metapackage shim. |
| OpenTelemetry `ActivitySource` name | `"Trellis.Results"` | `"Trellis.Core"` | Update OTel subscriptions: `builder.AddSource("Trellis.Results")` → `builder.AddSource("Trellis.Core")`. The `RopTrace.ActivitySourceName` constant exposes the name programmatically. |
| Test helper namespace | `Trellis.Results.Tests.*` | `Trellis.Core.Tests.*` | Internal change only — affects users who took an InternalsVisibleTo dependency on the test assembly (none expected). |
| Package merge: DDD | <PackageReference Include="Trellis.DomainDrivenDesign" .../> | *(removed)* | All DDD types (`Aggregate<T>`, `Entity<T>`, `ValueObject`, `Specification<T>`, etc.) moved into `Trellis.Core`. Drop the `Trellis.DomainDrivenDesign` PackageReference; the types are still in `namespace Trellis;` so no using changes are needed. |
| Package merge: Primitives generator | <PackageReference Include="Trellis.Primitives.Generator" .../> | *(removed)* | The Required* source generator is now bundled inside `Trellis.Core.nupkg` (`analyzers/dotnet/cs/Trellis.Core.Generator.dll`). Installing `Trellis.Core` (or any package depending on it) attaches the analyzer automatically. Drop the standalone PackageReference. |
| `Required*` base classes | `Trellis.Primitives` | `Trellis.Core` | Source-tree consumers may need to ensure they reference `Trellis.Core`. Namespace is unchanged (`Trellis`), so no using edits are required. |

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

Typed success/failure contract.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `Value` | `TValue` | Throws when the result is a failure |

#### Methods

None.

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

### `public readonly partial struct Result`

Static factory and helper surface for `Result<TValue>` and the non-generic `Result` (success/failure for void flows).

#### Properties

None.

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Result<TValue> Ok<TValue>(TValue value)` | Success factory |
| `public static Result Ok()` | Success without payload (non-generic `Result`) |
| `public static Result<TValue> Fail<TValue>(Error error)` | Failure factory |
| `public static Result Fail(Error error)` | Failure without payload (non-generic `Result`) |
| `public static Result Ensure(bool flag, Error error)` | Converts a boolean to non-generic `Result` |
| `public static Result Ensure(Func<bool> predicate, Error error)` | Deferred predicate version |
| `public static Task<Result> EnsureAsync(Func<Task<bool>> predicate, Error error)` | Async predicate version |
| `public static Result<T> Try<T>(Func<T> func, Func<Exception, Error>? map = null)` | Converts thrown exceptions to failures |
| `public static Task<Result<T>> TryAsync<T>(Func<Task<T>> func, Func<Exception, Error>? map = null)` | Async exception capture |
| `public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> r1, Result<T2> r2)` | Combines two results |
| `public static Result<(T1, ..., T9)> Combine<...>(...)` | Additional generated arities up to 9 |
| `public static (Task<Result<T1>>, ..., Task<Result<T9>>) ParallelAsync<...>(...)` | Starts async result-producing operations in parallel, arities 2-9 |

#### Factory Methods

`Ok`, `Fail`, `Ensure`, `Try`, `TryAsync`, `Combine`, and `ParallelAsync`. Removed in v2 (see "Breaking changes from v1" above): `Success`, `Failure`, `Success(Func<T>)`, `Failure<T>(Func<Error>)`, `SuccessIf`, `FailureIf`, `SuccessIfAsync`, `FailureIfAsync`, `FromException`.

---

### `public readonly partial struct Result<TValue> : IResult<TValue>, IEquatable<Result<TValue>>, IFailureFactory<Result<TValue>>`

Represents either a successful `TValue` or a failure `Error`.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `Value` | `TValue` | Throws `InvalidOperationException` when `IsFailure` is `true`. Required because `TValue` may be a struct whose `default` is indistinguishable from a real value; use `IsSuccess`/`TryGetValue`/`Match` to gate access. |
| `Error` | `Error?` | `null` on success; never throws. Pattern-match on the value (e.g. `if (result.Error is { } error)`) for imperative branches. |
| `IsSuccess` | `bool` | Success flag |
| `IsFailure` | `bool` | Failure flag |

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Result<TValue> CreateFailure(Error error)` | Implements `IFailureFactory<Result<TValue>>` |
| `public bool TryGetValue(out TValue value)` | Non-throwing success extractor |
| `public bool TryGetError(out Error error)` | Non-throwing failure extractor |
| `public void Deconstruct(out bool isSuccess, out TValue? value, out Error? error)` | Deconstruction support |
| `public bool Equals(Result<TValue> other)` | Value equality |
| `public override bool Equals(object? obj)` | Object equality |
| `public override int GetHashCode()` | Hash code |
| `public override string ToString()` | Debug-friendly string |

#### Operators

The implicit conversion operators (`TValue → Result<TValue>`, `Error → Result<TValue>`) were removed in v2. Use `Result.Ok(value)` / `Result.Fail<T>(error)`.

| Signature | Notes |
| --- | --- |
| `public static bool operator ==(Result<TValue> left, Result<TValue> right)` | Equality |
| `public static bool operator !=(Result<TValue> left, Result<TValue> right)` | Inequality |

#### Factory Methods

Use the static `Result` type.

---

### `public record struct Unit`

Represents "no value" used for tuple-result interop (e.g. `Result<(Unit, T2)>`). For void-style success/failure use the non-generic `Result` returned by `Result.Ok()` / `Result.Fail(error)`.

#### Properties

None.

#### Methods

None.

#### Factory Methods

Use `Result.Ok()`.

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

Multi-field validation helpers for `Maybe<T>` values. Each method returns `Result` (non-generic) — success when the invariant holds, or an `Error.UnprocessableContent` whose `Fields` list carries one `FieldViolation` per offending field. Field paths are normalized via `InputPointer.ForProperty(name)` (RFC 6901 JSON Pointer).

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Result AllOrNone<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | All fields present or all absent. Arities 2, 3, 4. |
| `public static Result Requires<T1, T2>(Maybe<T1> source, Maybe<T2> required, string sourceFieldName, string requiredFieldName)` | If `source` is present, `required` must be too. Arity 2. |
| `public static Result MutuallyExclusive<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | At most one field may be present. Arities 2, 3. |
| `public static Result ExactlyOne<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | Exactly one field must be present. Arities 2, 3. |
| `public static Result AtLeastOne<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstFieldName, string secondFieldName)` | At least one field must be present. Arities 2, 3. |

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

Closed discriminated union of error values. Each case is a nested `sealed record` mirroring an entry from the IANA HTTP Status Code Registry (RFC 9110, RFC 6585) and carrying a strongly-typed payload. The base record has a `private` constructor — only the cases declared in `Error.cs` may inherit, which makes `switch` over an `Error` reference exhaustive at the language level. See `docs/adr/ADR-001-result-api-surface.md` for the design rationale.

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

Construct cases directly: `new Error.NotFound(payload) { Detail = "..." }`. The base type intentionally exposes no static `Error.Validation(...)` / `Error.NotFound(...)` helpers — every call site names the case it produces.

---

### Concrete error cases

Eighteen nested `sealed record` cases under `Error`. Each case constructor is `internal` from external code's perspective only insofar as the base ctor is `private`; cases themselves are `public sealed record`s and instantiable with `new`.

| Case | Constructor | Wire status | Notes |
| --- | --- | --- | --- |
| `Error.BadRequest` | `(string ReasonCode, InputPointer? At = null)` | 400 | `Code` returns `ReasonCode`; `At` is an optional RFC 6901 JSON Pointer to the offending input. |
| `Error.Unauthorized` | `(EquatableArray<AuthChallenge> Challenges = default)` | 401 | Round-trips real `WWW-Authenticate` (per RFC 9110 §11.6.1). |
| `Error.Forbidden` | `(string PolicyId, ResourceRef? Resource = null)` | 403 | `Code` returns `PolicyId`. |
| `Error.NotFound` | `(ResourceRef Resource)` | 404 | `Resource` (e.g. `new ResourceRef("Order", "42")`) drives ProblemDetails `instance`. |
| `Error.MethodNotAllowed` | `(EquatableArray<string> Allow)` | 405 | `Allow` populates the `Allow` response header (RFC 9110 §15.5.6). |
| `Error.NotAcceptable` | `(EquatableArray<string> Available)` | 406 | Available media types. |
| `Error.Conflict` | `(ResourceRef? Resource, string ReasonCode)` | 409 | `Code` returns `ReasonCode`. |
| `Error.Gone` | `(ResourceRef Resource)` | 410 | Soft-deleted resource. |
| `Error.PreconditionFailed` | `(ResourceRef Resource, PreconditionKind Condition)` | 412 | Optimistic concurrency mismatch. `Condition` is the typed precondition kind (`IfMatch`, `IfNoneMatch`, `IfUnmodifiedSince`, `IfModifiedSince`). |
| `Error.ContentTooLarge` | `(long? MaxBytes = null)` | 413 | |
| `Error.UnsupportedMediaType` | `(EquatableArray<string> Supported)` | 415 | |
| `Error.RangeNotSatisfiable` | `(long CompleteLength, string Unit = "bytes")` | 416 | Drives `Content-Range` synthesis. |
| `Error.UnprocessableContent` | `(EquatableArray<FieldViolation> Fields, EquatableArray<RuleViolation> Rules = default)` | 422 | The single domain-validation case — replaces v1's `ValidationError`. Carries both per-field violations and cross-field rule violations. |
| `Error.PreconditionRequired` | `(PreconditionKind Condition)` | 428 | Missing concurrency token on PUT. |
| `Error.TooManyRequests` | `(RetryAfterValue? RetryAfter = null)` | 429 | |
| `Error.InternalServerError` | `(string FaultId)` | 500 | `Code` returns `FaultId`. **Never holds a live `Exception`**; the `FaultId` indexes into the logging/telemetry layer. |
| `Error.NotImplemented` | `(string Feature)` | 501 | `Code` returns `Feature`. |
| `Error.ServiceUnavailable` | `(RetryAfterValue? RetryAfter = null)` | 503 | |
| `Error.Aggregate` | `(EquatableArray<Error> Errors)` | 207 / `extensions.errors` | Composition node. **Disallows `Cause`** (pure composition). Auto-flattens nested `Aggregate` instances at construction. |

#### Supporting types

| Type | Shape | Purpose |
| --- | --- | --- |
| `ResourceRef` | `readonly record struct (string Type, string? Id = null)` | Aggregate identity (e.g. `new ResourceRef("Order", "42")`). |
| `InputPointer` | `readonly record struct (string Path)` | RFC 6901 JSON Pointer (e.g. `/email`). Construct via `InputPointer.ForProperty("email")`. |
| `FieldViolation` | `sealed record (InputPointer Field, string ReasonCode, ImmutableDictionary<string,string>? Args = null) { string? Detail }` | Single per-field violation inside `UnprocessableContent.Fields`. |
| `RuleViolation` | `sealed record (string ReasonCode, EquatableArray<InputPointer> Fields = default, ImmutableDictionary<string,string>? Args = null) { string? Detail }` | Multi-field invariant or object-level rule inside `UnprocessableContent.Rules`. |
| `AuthChallenge` | `sealed record (string Scheme, ImmutableDictionary<string,string>? Params = null)` | Carried by `Unauthorized` to round-trip `WWW-Authenticate`. |
| `RetryAfterValue` | sealed class, see below | `Retry-After` as delay seconds or absolute date. |
| `PreconditionKind` | `enum { IfMatch, IfNoneMatch, IfUnmodifiedSince, IfModifiedSince }` | Typed precondition vocabulary. |
| `EquatableArray<T>` | `readonly record struct (ImmutableArray<T> Items)` | Wraps `ImmutableArray<T>` so records get sequence equality (built-in records compare arrays by reference). Construct with `EquatableArray.Create(...)` or `EquatableArray<T>.Empty`. |

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

OpenTelemetry helper for Trellis result instrumentation.

#### Methods

| Signature | Notes |
| --- | --- |
| `public static TracerProviderBuilder AddResultsInstrumentation(this TracerProviderBuilder builder)` | Registers result tracing |

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
| `CheckExtensions`, `CheckExtensionsAsync` | Runs side-effect validations that return `IResult` / non-generic `Result` while preserving original success value |
| `CheckIfExtensions`, `CheckIfExtensionsAsync` | Conditional `Check` variants |
| `CombineExtensions`, `CombineExtensionsAsync`, `CombineErrorExtensions` | Combines results, including tuple and enumerable forms |
| `DiscardExtensions`, `DiscardTaskExtensions`, `DiscardValueTaskExtensions` | Converts `Result<T>` to non-generic `Result` |
| `EnsureExtensions`, `EnsureExtensionsAsync`, `EnsureAllExtensions`, `EnsureAllExtensionsAsync` | Predicate-based validation on successful values; includes collection-wide validation |
| `GetValueOrDefaultExtensions` | Non-throwing value fallback helpers |
| `ResultLinqExtensions` | LINQ query syntax support via `Select`/`SelectMany` |
| `MapExtensions`, `MapExtensionsAsync`, `MapIfExtensions`, `MapOnFailureExtensions` | Success-path mapping, conditional mapping, and failure remapping; tuple overloads generated for arities 2-9 |
| `MatchExtensions`, `MatchExtensionsAsync`, `MatchTupleExtensions`, `MatchTupleExtensionsAsync` | Terminal branching for normal and tuple results. (v1's `MatchErrorExtensions` was removed in v2 — use `result.Match(_ => ..., e => e switch { Error.NotFound nf => ..., ... })` against the closed catalog.) |
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
    new Error.UnprocessableContent(EquatableArray.Create(
        new FieldViolation(InputPointer.ForProperty("email"), "required")
        { Detail = "Email is required" })));
```

### Reading errors without throwing

```csharp
using Trellis;

Result<Order> result = await mediator.SendAsync(new PlaceOrder(...));

// Pattern-matching: result.Error is null on success, never throws
if (result.Error is { } error)
{
    return error switch
    {
        Error.NotFound nf            => NotFound(nf.Resource.Id),
        Error.UnprocessableContent uc => UnprocessableEntity(uc.Fields),
        Error.Conflict c             => Conflict(c.ReasonCode),
        _                            => Problem(error.GetDisplayMessage()),
    };
}

return Ok(result.Value);
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

The DDD primitives (Aggregate<T>, Entity<T>, ValueObject, Specification<T>, ...) live in `Trellis.Core` (Phase 2). They share the `Trellis` namespace.

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
- `IScalarValue<TSelf, TPrimitive>` and `IFormattableScalarValue<TSelf, TPrimitive>` are **not** in this package. They are defined in `Trellis.Core` and are referenced by `ScalarValueObject<TSelf, T>` through its generic constraint.

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
            ? Result.Fail<OrderId>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "orderId"), "required") { Detail = "Order ID is required." })))
            : Result.Ok(new OrderId(value));

    public static Result<OrderId> TryCreate(string? value, string? fieldName = null) =>
        Guid.TryParse(value, out var guid)
            ? TryCreate(guid, fieldName)
            : Result.Fail<OrderId>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "orderId"), "must_be_guid") { Detail = "Order ID must be a GUID." })));
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
var guarded = orderResult.OptionalETag(new[] { EntityTagValue.Strong(orderResult.Value.ETag) });
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

