# Trellis — AI API Reference

> **Purpose**: Machine-readable reference for AI coding assistants. Covers every public type, method signature, and usage pattern in the Trellis library ecosystem.

## Quick Facts

- **.NET 10** functional programming library
- **Railway Oriented Programming (ROP)**, DDD primitives, value objects
- Root namespace: `Trellis` for core types, integration packages get their own namespace
- All value objects use `TryCreate` → `Result<T>` and `Create` → `T` (throws) factory pattern
- NuGet packages: `Trellis.Results`, `Trellis.DomainDrivenDesign`, `Trellis.Primitives`, `Trellis.Authorization`, `Trellis.Asp`, `Trellis.Asp.Authorization`, `Trellis.Http`, `Trellis.Mediator`, `Trellis.Testing`, `Trellis.FluentValidation`, `Trellis.Stateless`, `Trellis.EntityFrameworkCore`

---

## Package → Namespace Mapping

| Package | Namespace | Dependency |
|---------|-----------|------------|
| Trellis.Results | `Trellis` | None (core) |
| Trellis.DomainDrivenDesign | `Trellis` | Trellis.Results |
| Trellis.Primitives | `Trellis` (base types), `Trellis.Primitives` (concrete VOs) | Trellis.DomainDrivenDesign |
| Trellis.Authorization | `Trellis.Authorization` | Trellis.Results |
| Trellis.Asp | `Trellis.Asp` | ASP.NET Core |
| Trellis.Asp.Authorization | `Trellis.Asp.Authorization` | Trellis.Authorization, ASP.NET Core |
| Trellis.Http | `Trellis.Http` | Trellis.Results |
| Trellis.Mediator | `Trellis.Mediator` | Mediator, Trellis.Authorization |
| Trellis.Testing | `Trellis.Testing`, `Trellis.Testing.Fakes` | FluentAssertions, Trellis.Authorization |
| Trellis.FluentValidation | `Trellis.FluentValidation` | FluentValidation |
| Trellis.Stateless | `Trellis.Stateless` | Stateless |
| Trellis.EntityFrameworkCore | `Trellis.EntityFrameworkCore` | EF Core |

---

# 1. Trellis.Results — Core ROP Types

**Namespace: `Trellis`**

## Result\<TValue\> (readonly struct)

Represents success (with value) or failure (with error). Implements `IResult<TValue>`, `IEquatable<Result<TValue>>`, `IFailureFactory<Result<TValue>>`.

## Core Interfaces

Core abstractions for the Result type system.

### IResult (interface)

Non-generic base — exposes success/failure state and error.

```csharp
bool IsSuccess { get; }
bool IsFailure { get; }
Error Error { get; }       // throws if success
```

### IResult\<TValue\> (interface, extends IResult)

```csharp
TValue Value { get; }      // throws if failure
```

### IFailureFactory\<TSelf\> (interface)

Enables construction of failure results without knowing the inner type parameter. Used by generic pipeline behaviors (e.g., `AuthorizationBehavior`).

```csharp
static abstract TSelf CreateFailure(Error error);
```

`Result<TValue>` implements this via `Result<TValue>.CreateFailure(Error error)`.

### Properties & Methods

Instance members on `Result<T>` for checking state and extracting values.

```csharp
TValue Value { get; }              // throws if failure
Error Error { get; }               // throws if success
bool IsSuccess { get; }
bool IsFailure { get; }
bool TryGetValue(out TValue value)
bool TryGetError(out Error error)
void Deconstruct(out bool isSuccess, out TValue? value, out Error? error)
```

### Operators

Implicit conversion operators: `T` → `Result<T>` (success) and `Error` → `Result<T>` (failure).

```csharp
implicit operator Result<TValue>(TValue value)   // auto-wrap success
implicit operator Result<TValue>(Error error)     // auto-wrap failure
```

### Static Factories (on `Result`)

Static methods on the non-generic `Result` class for creating `Result<T>` instances.

```csharp
Result<TValue> Success<TValue>(TValue value)
Result<TValue> Success<TValue>(Func<TValue> funcOk)
Result<Unit> Success()
Result<TValue> Failure<TValue>(Error error)
Result<TValue> Failure<TValue>(Func<Error> error)
Result<Unit> Failure(Error error)
Result<TValue> SuccessIf<TValue>(bool isSuccess, in TValue value, Error error)
Result<(T1, T2)> SuccessIf<T1, T2>(bool isSuccess, in T1 t1, in T2 t2, Error error)
Result<TValue> FailureIf<TValue>(bool isFailure, TValue value, Error error)
Result<TValue> FailureIf<TValue>(Func<bool> failurePredicate, in TValue value, Error error)
Task<Result<TValue>> SuccessIfAsync<TValue>(Func<Task<bool>> predicate, TValue value, Error error)
Task<Result<TValue>> FailureIfAsync<TValue>(Func<Task<bool>> failurePredicate, TValue value, Error error)
Result<T> Try<T>(Func<T> func, Func<Exception, Error>? map = null)
Task<Result<T>> TryAsync<T>(Func<Task<T>> func, Func<Exception, Error>? map = null)
Result<Unit> FromException(Exception ex, Func<Exception, Error>? map = null)
Result<T> FromException<T>(Exception ex, Func<Exception, Error>? map = null)
Result<(T1, T2)> Combine<T1, T2>(Result<T1> r1, Result<T2> r2)
// ... through 9-tuple arity:
Result<(T1,...,T9)> Combine<T1,...,T9>(Result<T1> r1, ..., Result<T9> r9)
```

## RailwayTrackAttribute & TrackBehavior

Metadata attribute for IDE extensions, analyzers, and documentation generators. Indicates which railway track an ROP method executes on.

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class RailwayTrackAttribute : Attribute
{
    public TrackBehavior Track { get; }
    public RailwayTrackAttribute(TrackBehavior track)
}

public enum TrackBehavior { Success, Failure }
```

## Unit (record struct)

Represents void/no value. Used as `Result<Unit>` for operations that succeed without returning data.

## Maybe\<T\> (readonly struct, where T : notnull)

Domain-level optionality. Use instead of `T?` for optional value objects.

```csharp
T Value { get; }                    // throws if none
bool HasValue { get; }
bool HasNoValue { get; }
T GetValueOrThrow(string? errorMessage = null)
T GetValueOrDefault(T defaultValue)
T GetValueOrDefault(Func<T> defaultFactory)
bool TryGetValue(out T value)
Maybe<TResult> Map<TResult>(Func<T, TResult> selector) where TResult : notnull
Maybe<TResult> Bind<TResult>(Func<T, Maybe<TResult>> selector) where TResult : notnull
Maybe<T> Or(T fallback)
Maybe<T> Or(Func<T> fallbackFactory)
Maybe<T> Or(Maybe<T> fallback)
Maybe<T> Or(Func<Maybe<T>> fallbackFactory)
Maybe<T> Where(Func<T, bool> predicate)
Maybe<T> Tap(Action<T> action)
TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none)
implicit operator Maybe<T>(T value)  // T → Maybe<T> (implicit)
// No implicit conversion from Maybe<T> → T (by design — use .Value, Match, or TryGetValue)
```

### Maybe Static Members

Factory methods on `Maybe<T>`: `None` (empty), `From(T?)` (wraps nullable), and implicit conversion from `T`.

```csharp
// On Maybe<T> struct:
static Maybe<T> None { get; }           // e.g., Maybe<PhoneNumber>.None
static Maybe<T> From(T? value)          // e.g., Maybe<PhoneNumber>.From(phone)

// On Maybe static helper class (type inference convenience):
Maybe<T> From<T>(T? value) where T : notnull    // e.g., Maybe.From(phone) — infers T
Result<Maybe<TOut>> Optional<TIn, TOut>(TIn? value, Func<TIn, Result<TOut>> function) where TIn : class, TOut : notnull
Result<Maybe<TOut>> Optional<TIn, TOut>(TIn? value, Func<TIn, Result<TOut>> function) where TIn : struct, TOut : notnull
```

### Maybe Extension Methods

Pipeline operations for `Maybe<T>`: `AsMaybe`, `AsNullable`, `ToMaybe`, `ToResult`, `Map`, `Match`, and `GetValueOrDefault`.

```csharp
// AsMaybe
Maybe<T> AsMaybe<T>(this T? value) where T : struct
Maybe<T> AsMaybe<T>(this T value) where T : class

// AsNullable
T? AsNullable<T>(this Maybe<T> maybe) where T : struct

// ToMaybe (from Result) — discards error, keeps value if success
Maybe<T> ToMaybe<T>(this Result<T>) where T : notnull
Task<Maybe<T>> ToMaybeAsync<T>(this Task<Result<T>>) where T : notnull
ValueTask<Maybe<T>> ToMaybeAsync<T>(this ValueTask<Result<T>>) where T : notnull

// ToResult (from Maybe)
Result<T> ToResult<T>(this Maybe<T>, Error) where T : notnull
Result<T> ToResult<T>(this Maybe<T>, Func<Error>) where T : notnull
Task<Result<T>> ToResultAsync<T>(this Task<Maybe<T>>, Error)
Task<Result<T>> ToResultAsync<T>(this Task<Maybe<T>>, Func<Error>)
ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<Maybe<T>>, Error)
ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<Maybe<T>>, Func<Error>)

// ToResult (from nullable)
Result<T> ToResult<T>(this T? value, Error) where T : struct
Result<T> ToResult<T>(this T? value, Func<Error>) where T : struct
Result<T> ToResult<T>(this T? value, Error) where T : class
Result<T> ToResult<T>(this T? value, Func<Error>) where T : class
Task<Result<T>> ToResultAsync<T>(this Task<T?> valueTask, Error) where T : struct
Task<Result<T>> ToResultAsync<T>(this Task<T?> valueTask, Func<Error>) where T : struct
Task<Result<T>> ToResultAsync<T>(this Task<T?> valueTask, Error) where T : class
Task<Result<T>> ToResultAsync<T>(this Task<T?> valueTask, Func<Error>) where T : class
ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> valueTask, Error) where T : struct
ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> valueTask, Func<Error>) where T : struct
ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> valueTask, Error) where T : class
ValueTask<Result<T>> ToResultAsync<T>(this ValueTask<T?> valueTask, Func<Error>) where T : class
```

### Collection Helpers — Safe Enumerable → Maybe

Extension methods on `IEnumerable<T>` for safely extracting elements as `Maybe<T>`, and filtering/unwrapping collections of `Maybe<T>`.

```csharp
// TryFirst — safe first element (no exception on empty)
Maybe<T> TryFirst<T>(this IEnumerable<T>) where T : notnull
Maybe<T> TryFirst<T>(this IEnumerable<T>, Func<T, bool> predicate) where T : notnull

// TryLast — safe last element (no exception on empty)
Maybe<T> TryLast<T>(this IEnumerable<T>) where T : notnull
Maybe<T> TryLast<T>(this IEnumerable<T>, Func<T, bool> predicate) where T : notnull

// Choose — filter and unwrap Maybe collections (like Seq.choose in F#)
IEnumerable<T> Choose<T>(this IEnumerable<Maybe<T>>) where T : notnull
IEnumerable<TResult> Choose<T, TResult>(this IEnumerable<Maybe<T>>, Func<T, TResult>)
```

---

## Error Hierarchy

Trellis uses typed errors instead of exceptions for expected failures. Each error type maps to an HTTP status code via `ToActionResult()`. Use `Error.Validation` for input errors, `Error.NotFound` for missing resources, `Error.Conflict` for duplicate keys, `Error.Forbidden` for authorization failures.

### Error (base class)

Abstract base class for all Trellis errors. Contains `Code` (machine-readable), `Detail` (human-readable message), and `Instance` (optional resource identifier).

```csharp
string Code { get; }
string Detail { get; }
string? Instance { get; }
```

**Equality:** `Equals` and `GetHashCode` are `virtual`. Base `Error` compares `GetType()`, `Code`, `Detail`, and `Instance` (DDD Value Object semantics). `ValidationError` additionally compares `FieldErrors`. `AggregateError` additionally compares `Errors`. Override in custom error types to include additional properties.

### Factory Methods

Static factory methods on `Error` for creating typed errors without constructing specific subclasses directly.

```csharp
// Default code factories
ValidationError Error.Validation(string fieldDetail, string fieldName = "", string? detail = null, string? instance = null)
ValidationError Error.Validation(ImmutableArray<FieldError> fieldDetails, string detail = "", string? instance = null)
BadRequestError Error.BadRequest(string detail, string? instance = null)
ConflictError Error.Conflict(string detail, string? instance = null)
NotFoundError Error.NotFound(string detail, string? instance = null)
UnauthorizedError Error.Unauthorized(string detail, string? instance = null)
ForbiddenError Error.Forbidden(string detail, string? instance = null)
UnexpectedError Error.Unexpected(string detail, string? instance = null)
DomainError Error.Domain(string detail, string? instance = null)
RateLimitError Error.RateLimit(string detail, string? instance = null)
ServiceUnavailableError Error.ServiceUnavailable(string detail, string? instance = null)

// Custom code factories (same types with additional code parameter)
BadRequestError Error.BadRequest(string detail, string code, string? instance = null)
// ... same pattern for all non-Validation types
```

### Concrete Error Types

Each error type maps to a specific HTTP status code. `ValidationError` → 400, `NotFoundError` → 404, `UnauthorizedError` → 401, `ForbiddenError` → 403, `ConflictError` → 409, `PreconditionFailedError` → 412, `DomainError` → 422, `PreconditionRequiredError` → 428, `UnexpectedError` → 500.

| Type | Default Code |
|------|-------------|
| `ValidationError` | `"validation.error"` |
| `BadRequestError` | `"bad.request"` |
| `ConflictError` | `"conflict.error"` |
| `PreconditionFailedError` | `"precondition.failed.error"` |
| `NotFoundError` | `"not.found"` |
| `UnauthorizedError` | `"unauthorized.access"` |
| `ForbiddenError` | `"forbidden.access"` |
| `DomainError` | `"domain.error"` |
| `PreconditionRequiredError` | `"precondition.required.error"` |
| `RateLimitError` | `"rate.limit"` |
| `UnexpectedError` | `"unexpected.error"` |
| `ServiceUnavailableError` | `"service.unavailable"` |

### ValidationError (extends Error)

Represents input validation failures with field-level error details. Use `ValidationError.For(fieldName, message)` to create, or let value object `TryCreate` methods produce them automatically.

> ⚠️ **Parameter order differs:** `Error.Validation(fieldDetail, fieldName)` vs `ValidationError.For(fieldName, message)`. The factory method on `Error` takes the detail first; the static method on `ValidationError` takes the field name first.

```csharp
ImmutableArray<FieldError> FieldErrors { get; }
readonly record struct FieldError(string FieldName, ImmutableArray<string> Details)

static ValidationError For(string fieldName, string message, string code = "validation.error", string? detail = null, string? instance = null)
ValidationError And(string fieldName, string message)
ValidationError And(string fieldName, params string[] messages)
ValidationError Merge(ValidationError other)
IDictionary<string, string[]> ToDictionary()
```

### AggregateError (extends Error)

Combines multiple errors from parallel validation. Use `Result.Combine()` or `EnsureAll()` to accumulate errors instead of failing on the first one.

```csharp
IReadOnlyList<Error> Errors { get; }
AggregateError(IReadOnlyList<Error> errors)
AggregateError(IReadOnlyList<Error> errors, string code)

// Extracts and merges all nested ValidationError field errors.
// Non-validation errors are ignored. Returns null if no validation errors exist.
ValidationError? FlattenValidationErrors()
```

### FlattenValidationErrors — Result Extension

Convenience extension on `Result<T>` that delegates to `AggregateError.FlattenValidationErrors()` when the error is an `AggregateError`, or returns the error directly when it is a `ValidationError`.

```csharp
ValidationError? FlattenValidationErrors<T>(this Result<T> result)
```

### CombineErrorExtensions — Merge Errors

```csharp
Error Combine(this Error? thisError, Error otherError)
// If both are ValidationError → merges field errors
// Otherwise → wraps in AggregateError
```

---

## Extension Methods — ROP Pipeline Operations

All extension methods follow a consistent async pattern:
- **Sync**: `Method(this Result<T>, ...)` → `Result<TOut>`
- **Task Left-only**: `MethodAsync(this Task<Result<T>>, sync_predicate)` → `Task<Result<TOut>>`
- **Task Right-only**: `MethodAsync(this Result<T>, async_predicate)` → `Task<Result<TOut>>`
- **Task Both**: `MethodAsync(this Task<Result<T>>, async_predicate)` → `Task<Result<TOut>>`
- **ValueTask**: Same three patterns with `ValueTask<Result<T>>`

### Bind — FlatMap / Chain

Transforms value inside Result, function returns `Result<TOut>`. Short-circuits on failure.

```csharp
// Sync
Result<TOut> Bind<TIn, TOut>(this Result<TIn>, Func<TIn, Result<TOut>>)

// Async (all 6 variants)
Task<Result<TOut>> BindAsync<TIn, TOut>(this Task<Result<TIn>>, Func<TIn, Task<Result<TOut>>>)
Task<Result<TOut>> BindAsync<TIn, TOut>(this Task<Result<TIn>>, Func<TIn, Result<TOut>>)
Task<Result<TOut>> BindAsync<TIn, TOut>(this Result<TIn>, Func<TIn, Task<Result<TOut>>>)
ValueTask<Result<TOut>> BindAsync<TIn, TOut>(this ValueTask<Result<TIn>>, Func<TIn, ValueTask<Result<TOut>>>)
ValueTask<Result<TOut>> BindAsync<TIn, TOut>(this ValueTask<Result<TIn>>, Func<TIn, Result<TOut>>)
ValueTask<Result<TOut>> BindAsync<TIn, TOut>(this Result<TIn>, Func<TIn, ValueTask<Result<TOut>>>)
```

### Map — Transform Value

Transforms value, wraps in new Result. Short-circuits on failure.

```csharp
Result<TOut> Map<TIn, TOut>(this Result<TIn>, Func<TIn, TOut>)
// + 6 async variants (same pattern as Bind)
```

### MapIf — Conditional Pure Transformation

Transforms value only when a condition is met, otherwise passes through unchanged. Short-circuits on failure. Useful for optional transformations in pipelines without branching into `When`/`Unless`.

```csharp
// Static condition
Result<T> MapIf<T>(this Result<T>, bool condition, Func<T, T> func)

// Value-based predicate
Result<T> MapIf<T>(this Result<T>, Func<T, bool> predicate, Func<T, T> func)
```

### Ensure — Add Validation

Validates value, returns failure if predicate fails. Short-circuits on prior failure.

```csharp
// Bool predicate + static error
Result<T> Ensure<T>(this Result<T>, Func<T, bool> predicate, Error error)
Result<T> Ensure<T>(this Result<T>, Func<bool> predicate, Error error)

// Bool predicate + error factory
Result<T> Ensure<T>(this Result<T>, Func<T, bool> predicate, Func<T, Error> error)

// Result-returning predicate
Result<T> Ensure<T>(this Result<T>, Func<T, Result<T>> predicate)
Result<T> Ensure<T>(this Result<T>, Func<Result<T>> predicate)

// Static helpers
static Result<Unit> Ensure(bool flag, Error error)
static Result<string> EnsureNotNullOrWhiteSpace(this string?, Error error)

// Async: 5 overloads × 6 async patterns (Task Left/Right/Both + ValueTask Left/Right/Both) = 30 variants
```

### EnsureAll — Validation Accumulation

Runs ALL validation checks and accumulates failures into a single error, instead of short-circuiting on the first failure. Uses `Error.Combine()` to merge errors — `ValidationError` instances are merged, mixed types create `AggregateError`.

```csharp
Result<T> EnsureAll<T>(this Result<T>, params (Func<T, bool> predicate, Error error)[] checks)
// + Task and ValueTask async variants
```

Example:
```csharp
var result = Result.Success(request)
    .EnsureAll(
        (r => r.Name.Length > 0, Error.Validation("Name required", "name")),
        (r => r.Age >= 18, Error.Validation("Must be 18+", "age")),
        (r => r.Email.Contains('@'), Error.Validation("Invalid email", "email")));
// Returns ONE ValidationError with all 3 field errors if all fail
```

### EnsureNotNull — Null-Guard + Type Narrowing

Validates that a nullable value is not null, narrowing `Result<T?>` to `Result<T>`. Returns the supplied error on null. Supports both reference and value types.

```csharp
Result<T> EnsureNotNull<T>(this Result<T?>, Error error) where T : class
Result<T> EnsureNotNull<T>(this Result<T?>, Error error) where T : struct
```

### Check — Validate Without Losing Pipeline Value

Runs a validation function that returns a Result, but discards the inner value and keeps the original pipeline value on success. Useful for "fire a validation, keep the current value" patterns — like `Ensure`, but the validation itself is expressed as a `Result`-returning function.

```csharp
Result<T> Check<T, TK>(this Result<T>, Func<T, Result<TK>>)
Result<T> Check<T>(this Result<T>, Func<T, Result<Unit>>)
// Async: CheckAsync with Task/ValueTask Left/Right/Both variants
```

### BindZip — Sequential Tuple Accumulation

Binds a function over the current value and zips the original value with the new result into a tuple. Enables sequential accumulation of values through a pipeline without nested closures. T4-generated overloads support growing tuples from 2 up to 9 elements.

```csharp
Result<(T1, T2)> BindZip<T1, T2>(this Result<T1>, Func<T1, Result<T2>>)
// T4-generated: tuple continuation overloads (2 → 9 tuples)
// e.g., Result<(T1, T2, T3)> BindZip<T1, T2, T3>(this Result<(T1, T2)>, Func<T1, T2, Result<T3>>)
// Async: BindZipAsync with Task/ValueTask Left/Right/Both variants
```

### Tap — Side Effects on Success

Executes action on success, returns original Result unchanged.

```csharp
Result<T> Tap<T>(this Result<T>, Action)
Result<T> Tap<T>(this Result<T>, Action<T>)
// + 12 async variants (Task and ValueTask with Action, Func<Task>, Func<T,Task>, Func<ValueTask>, Func<T,ValueTask>)
```

### TapOnFailure — Side Effects on Failure

Executes action on failure, returns original Result unchanged.

```csharp
Result<T> TapOnFailure<T>(this Result<T>, Action)
Result<T> TapOnFailure<T>(this Result<T>, Action<Error>)
// + 14 async variants
```

### Match — Terminal Pattern Match

Unwraps Result into a single value by providing both success and failure handlers.

```csharp
TOut Match<TIn, TOut>(this Result<TIn>, Func<TIn, TOut> onSuccess, Func<Error, TOut> onFailure)
void Switch<TIn>(this Result<TIn>, Action<TIn> onSuccess, Action<Error> onFailure)
// + async variants (Task/ValueTask, with CancellationToken overloads)
```

### MatchError — Typed Error Pattern Match

Pattern match on specific error types for fine-grained error handling.

```csharp
TOut MatchError<TIn, TOut>(
    this Result<TIn>,
    Func<TIn, TOut> onSuccess,
    Func<ValidationError, TOut>? onValidation = null,
    Func<NotFoundError, TOut>? onNotFound = null,
    Func<ConflictError, TOut>? onConflict = null,
    Func<BadRequestError, TOut>? onBadRequest = null,
    Func<UnauthorizedError, TOut>? onUnauthorized = null,
    Func<ForbiddenError, TOut>? onForbidden = null,
    Func<DomainError, TOut>? onDomain = null,
    Func<RateLimitError, TOut>? onRateLimit = null,
    Func<ServiceUnavailableError, TOut>? onServiceUnavailable = null,
    Func<UnexpectedError, TOut>? onUnexpected = null,
    Func<AggregateError, TOut>? onAggregate = null,  // handles AggregateError specifically; falls through to onError when null
    Func<Error, TOut>? onError = null)
// + async variants (Task Left-only, Task Both with CancellationToken)
```

### SwitchError — Typed Error Side Effects

Same as `MatchError` but void — executes actions instead of returning values.

```csharp
void SwitchError<TIn>(
    this Result<TIn>,
    Action<TIn> onSuccess,
    Action<ValidationError>? onValidation = null,
    // ... same error type parameters as MatchError ...
    Action<AggregateError>? onAggregate = null,      // handles AggregateError specifically; falls through to onError when null
    Action<Error>? onError = null)
// + SwitchErrorAsync (Task with CancellationToken)
```

### Combine — Merge Multiple Results

Combines two Results into a tuple Result. If any fails, returns failure.

```csharp
Result<(T1, T2)> Combine<T1, T2>(this Result<T1>, Result<T2>)
Result<T1> Combine<T1>(this Result<T1>, Result<Unit>)  // Unit variant
// + 8 async variants (Task/ValueTask permutations)
// + T4-generated overloads to grow tuples from 2 to 9 elements
```

### MapOnFailure — Transform Error

Transforms the error inside a failed Result, preserves success.

```csharp
Result<T> MapOnFailure<T>(this Result<T>, Func<Error, Error>)
// + 6 async variants
```

### Recover — Simple Fallback on Failure

Converts any failure to success with a simple fallback value. Sugar for the most common `RecoverOnFailure` pattern.

```csharp
Result<T> Recover<T>(this Result<T>, T fallback)
Result<T> Recover<T>(this Result<T>, Func<T> fallbackFunc)
Result<T> Recover<T>(this Result<T>, Func<Error, T> fallbackFunc)
// + 6 async variants (Task and ValueTask)
```

Example:
```csharp
var maxRetries = configService.GetInt("max_retries").Recover(3);
var items = recommendationEngine.GetFor(userId).Recover(Array.Empty<Product>());
```

### RecoverOnFailure — Recover from Failure

Attempts to recover from a failed Result by providing an alternative Result.

```csharp
Result<T> RecoverOnFailure<T>(this Result<T>, Func<Result<T>>)
Result<T> RecoverOnFailure<T>(this Result<T>, Func<Error, Result<T>>)
Result<T> RecoverOnFailure<T>(this Result<T>, Func<Error, bool> predicate, Func<Result<T>>)
Result<T> RecoverOnFailure<T>(this Result<T>, Func<Error, bool> predicate, Func<Error, Result<T>>)
// + 22 async variants (Task and ValueTask Left/Right/Both patterns)
```

### When / Unless — Conditional Pipeline

Conditionally apply a pipeline step. `When` executes the step only if the predicate is true; `Unless` executes only if false. Use for optional validation or conditional side effects.

```csharp
Result<T> When<T>(this Result<T>, Func<T, bool> predicate, Func<T, Result<T>> action)
Result<T> When<T>(this Result<T>, bool condition, Func<T, Result<T>> action)
Result<T> Unless<T>(this Result<T>, Func<T, bool> predicate, Func<T, Result<T>> action)
Result<T> Unless<T>(this Result<T>, bool condition, Func<T, Result<T>> action)
// + async variants, including Task<Result<T>> and ValueTask<Result<T>> boolean-condition overloads
```

### Traverse — Apply to Collection

Applies a Result-returning function to each element in a collection, collecting all successes or short-circuiting on the first failure. Use for batch validation or processing lists of items.

```csharp
Result<IReadOnlyList<TOut>> Traverse<TIn, TOut>(this IEnumerable<TIn>, Func<TIn, Result<TOut>>)
Task<Result<IReadOnlyList<TOut>>> TraverseAsync<TIn, TOut>(this IEnumerable<TIn>, Func<TIn, Task<Result<TOut>>>)
// + CancellationToken overloads, ValueTask variants
// Returns IReadOnlyList<TOut> (not IEnumerable<TOut>) — materializes eagerly
```

### Nullable → Result

Converts nullable values to Result types. `null` becomes `Failure` with the specified error; non-null becomes `Success`. Bridges between nullable C# patterns and the ROP pipeline.

```csharp
Result<T> ToResult<T>(this T? value, Error error) where T : struct
Result<T> ToResult<T>(this T? value, Error error) where T : class
// + Task/ValueTask async variants
```

### ToResult — Wrap as Success

Wraps a plain value as a successful `Result<T>`. Use to enter the ROP pipeline from imperative code.

```csharp
Result<T> ToResult<T>(this T value)  // wraps value as Success
```

### LINQ Support (Result)

Enables LINQ query syntax (`from`...`select`) over Result types. Alternative to method chain syntax for developers who prefer query expressions.

```csharp
Result<TOut> Select<TIn, TOut>(this Result<TIn>, Func<TIn, TOut>)            // = Map
Result<TResult> SelectMany<TSource, TCollection, TResult>(...)                // = Bind+Map
Result<TSource> Where<TSource>(this Result<TSource>, Func<TSource, bool>)     // = Ensure
```

### LINQ Support (Maybe)

Enables `from`/`select` query syntax for composing optional values.

```csharp
Maybe<TOut> Select<TIn, TOut>(this Maybe<TIn>, Func<TIn, TOut>)              // = Map
Maybe<TResult> SelectMany<TSource, TCollection, TResult>(
    this Maybe<TSource>,
    Func<TSource, Maybe<TCollection>>,
    Func<TSource, TCollection, TResult>)                                      // = FlatMap
```

Example:
```csharp
Maybe<string> fullName =
    from first in firstName
    from last in lastName
    select $"{first} {last}";
```

### WhenAll — Parallel Execution

Awaits multiple `Task<Result<T>>` in parallel and combines into a tuple result.
**This is an extension method on a tuple of tasks**, enabling fluent chaining with `ParallelAsync`.

```csharp
// Extension method on value tuple — enables .WhenAllAsync() fluent chain
Task<Result<(T1, T2)>> WhenAllAsync<T1, T2>(this (Task<Result<T1>>, Task<Result<T2>>) tasks)
// ... through 9-tuple arity

// Usage — fluent chain with ParallelAsync
var result = await Result.ParallelAsync(
    () => _customerRepo.GetByIdAsync(customerId, ct),
    () => _productRepo.GetByIdsAsync(productIds, ct))
    .WhenAllAsync()
    .BindAsync((Customer customer, List<Product> products) =>
        Order.TryCreate(customer, products, lineItems));
```

### ParallelAsync — Launch Parallel Operations

Launches multiple async operations in parallel, returning tuple of tasks.

```csharp
(Task<Result<T1>>, Task<Result<T2>>) ParallelAsync<T1, T2>(Func<Task<Result<T1>>>, Func<Task<Result<T2>>>)
// ... through 9-tuple arity
```

### Tuple Destructuring Extensions (T4-generated, arities 2-9)

All pipeline methods support tuple destructuring for `Result<(T1, T2, ...)>`:

```csharp
// Bind with destructured arguments
Result<TResult> Bind<T1, T2, TResult>(this Result<(T1, T2)>, Func<T1, T2, Result<TResult>>)

// Map with destructured arguments
Result<TOut> Map<T1, T2, TOut>(this Result<(T1, T2)>, Func<T1, T2, TOut>)

// Tap with destructured arguments
Result<(T1, T2)> Tap<T1, T2>(this Result<(T1, T2)>, Action<T1, T2>)

// Match with destructured arguments
TOut Match<T1, T2, TOut>(this Result<(T1, T2)>, Func<T1, T2, TOut>, Func<Error, TOut>)

// Combine growing tuples
Result<(T1, T2, T3)> Combine<T1, T2, T3>(this Result<(T1, T2)>, Result<T3>)
```

Each has sync + Task (3 variants) + ValueTask (3 variants) async overloads.

### Debug — Pipeline Inspection

Pipeline inspection extensions that emit values and errors to OpenTelemetry activity spans. Use during development to trace intermediate values in ROP chains. Guarded by `#if DEBUG`.

```csharp
Result<T> Debug<T>(this Result<T>, string? label = null)
Result<T> DebugDetailed<T>(this Result<T>, string? label = null)
Result<T> DebugWithStack<T>(this Result<T>, string? label = null, bool includeStack = true)
Result<T> DebugOnSuccess<T>(this Result<T>, Action<T>)
Result<T> DebugOnFailure<T>(this Result<T>, Action<Error>)
// + async variants
```

### GetValueOrDefault — Terminal Extraction

Extracts the value from a successful Result or returns a default/fallback. Terminal operator — exits the ROP pipeline. Supports static defaults, lazy factories, and error-aware factories.

```csharp
TValue GetValueOrDefault<TValue>(this Result<TValue>, TValue defaultValue)
TValue GetValueOrDefault<TValue>(this Result<TValue>, Func<TValue> defaultFactory)
TValue GetValueOrDefault<TValue>(this Result<TValue>, Func<Error, TValue> defaultFactory)
```

## OpenTelemetry Tracing

ROP operations automatically create `Activity` spans when instrumentation is enabled. Each `Bind`, `Map`, `Tap`, `Ensure`, `RecoverOnFailure`, and `Combine` call starts a child activity with success/error status.

Use `AddResultsInstrumentation()` when you need deep pipeline forensics. It traces every `Result<T>` step and can be noisy in normal production monitoring.
For lower-noise day-to-day diagnostics, `AddPrimitiveValueObjectInstrumentation()` is often the better default because it emits spans at value creation and validation boundaries.

### Registration

```csharp
services.AddOpenTelemetry()
    .WithTracing(builder => builder
    .AddPrimitiveValueObjectInstrumentation());     // Recommended default diagnostic signal

// AddResultsInstrumentation() is available when you need to trace the full ROP pipeline.
```

### Extension Methods

```csharp
// Trellis.Results — namespace Trellis
TracerProviderBuilder AddResultsInstrumentation(this TracerProviderBuilder builder)

// Trellis.Primitives — namespace Trellis
TracerProviderBuilder AddPrimitiveValueObjectInstrumentation(this TracerProviderBuilder builder)
```

### Public Trace Sources

```csharp
// Trellis.Primitives — namespace Trellis
public static class PrimitiveValueObjectTrace
{
    public static ActivitySource ActivitySource { get; }   // "Trellis.Primitives"
}
```

`RopTrace` is internal — consumers register it via `AddResultsInstrumentation()` only.

### Activity Behavior

| Context | Activity Status Set By |
|---------|------------------------|
| Value object `TryCreate` | `Result<T>` constructor (activity IS `Activity.Current`) |
| ROP extensions (Bind, Map, Tap, etc.) | `result.LogActivityStatus()` (child activity ≠ `Activity.Current`) |

---

# 2. Trellis.DomainDrivenDesign — DDD Primitives

**Namespace: `Trellis`**

## Entity\<TId\> (abstract class, where TId : notnull)

Identity-based equality. Two entities are equal iff same type and same non-default ID.

```csharp
public TId Id { get; init; }
protected Entity(TId id)
// Operators: ==, !=
// Overrides: Equals, GetHashCode
```

## IAggregate (interface, extends IChangeTracking)

Marker interface for aggregates. Implemented by `Aggregate<TId>`.

```csharp
public interface IAggregate : IChangeTracking
{
    IReadOnlyList<IDomainEvent> UncommittedEvents();
    string ETag { get; }  // optimistic concurrency token per RFC 9110 (opaque string, auto-generated on save)
}
```

## Aggregate\<TId\> (abstract class, extends Entity\<TId\>, implements IAggregate)

Consistency boundary that encapsulates domain state, enforces business rules through domain methods, and publishes domain events. Inherits `Entity<TId>`. Use for root entities that own child entities and control their lifecycle.

```csharp
protected Aggregate(TId id)
protected List<IDomainEvent> DomainEvents { get; }
bool IsChanged { get; }                    // true if DomainEvents.Count > 0
string ETag { get; private set; }          // RFC 9110 entity tag (auto-generated on save)
IReadOnlyList<IDomainEvent> UncommittedEvents()
void AcceptChanges()                       // clears DomainEvents
```

### RFC 9110 Optimistic Concurrency

`ETag` provides automatic optimistic concurrency per RFC 9110:
- `AggregateETagConvention` (registered by `ApplyTrellisConventions`) marks `ETag` as `IsConcurrencyToken()`
- `AggregateETagInterceptor` (registered by `AddTrellisInterceptors()`) generates a new GUID ETag on Added and Modified entries
- EF Core generates `UPDATE ... WHERE ETag = @original`; if stale → `ConflictError`
- `OptionalETag(expectedETags)` — skips if absent; returns `PreconditionFailedError` (412) on mismatch
- `RequireETag(expectedETags)` — returns `PreconditionRequiredError` (428) if absent; `PreconditionFailedError` (412) on mismatch

### OptionalETag / RequireETag Extensions

```csharp
// Optional — If-Match absent → unconditional update
Result<T> OptionalETag<T>(this Result<T> result, string[]? expectedETags) where T : IAggregate
// Required — If-Match absent → 428 Precondition Required
Result<T> RequireETag<T>(this Result<T> result, string[]? expectedETags) where T : IAggregate
// Async overloads
Task<Result<T>> OptionalETagAsync<T>(this Task<Result<T>>, string[]? expectedETags)
ValueTask<Result<T>> OptionalETagAsync<T>(this ValueTask<Result<T>>, string[]? expectedETags)
Task<Result<T>> RequireETagAsync<T>(this Task<Result<T>>, string[]? expectedETags)
ValueTask<Result<T>> RequireETagAsync<T>(this ValueTask<Result<T>>, string[]? expectedETags)
// null → no header (unconditional); ["*"] → wildcard; ["a","b"] → match any; [] → weak-only (unsatisfiable → 412)
```

## IDomainEvent (interface)

Marker interface for domain events raised by aggregates. Events are collected via `UncommittedEvents()` and published after persistence. Use to decouple side effects from the domain operation that triggered them.

```csharp
DateTime OccurredAt { get; }
```

## ValueObject (abstract class)

Structural equality based on `GetEqualityComponents()`. Hash code is cached (immutability assumed).

```csharp
protected abstract IEnumerable<IComparable?> GetEqualityComponents()

// Helper for including Maybe<T> in equality components.
// Returns the inner value if present, or null if empty.
protected static IComparable? MaybeComponent<T>(Maybe<T> maybe) where T : notnull, IComparable

// Operators: ==, !=, <, <=, >, >=
// Implements: IComparable<ValueObject>, IEquatable<ValueObject>
```

## ScalarValueObject\<TSelf, T\> (abstract class, extends ValueObject)

Single-value wrapper. Constraints: `TSelf : ScalarValueObject<TSelf, T>, IScalarValue<TSelf, T>` and `T : IComparable`.

```csharp
T Value { get; }
protected ScalarValueObject(T value)
static TSelf Create(T value)               // calls TryCreate, throws on failure
implicit operator T(ScalarValueObject<TSelf, T> vo)  // unwrap to primitive
// Implements IConvertible
```

## IScalarValue\<TSelf, TPrimitive\> (interface)

Interface for value objects wrapping a single primitive value. Enables automatic ASP.NET Core model binding, JSON serialization, and EF Core value conversion. Implemented by the source generator on `RequiredString`, `RequiredInt`, `RequiredGuid`, etc.

```csharp
static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null)
static virtual TSelf Create(TPrimitive value)  // default: TryCreate + throw
TPrimitive Value { get; }
```

## IFormattableScalarValue\<TSelf, TPrimitive\> (interface)

Extends `IScalarValue` with culture-aware string parsing. Implemented by numeric and date value objects where culture affects string parsing (decimal separators, date formats).

```csharp
static abstract Result<TSelf> TryCreate(string? value, IFormatProvider? provider, string? fieldName = null);
```

Implementors: `Age`, `MonetaryAmount`, `Percentage` (hand-implemented), `RequiredInt<T>`, `RequiredDecimal<T>`, `RequiredLong<T>`, `RequiredDateTime<T>` (source-generated). Not implemented by string-based types (`EmailAddress`, `Slug`, etc.) — culture doesn't affect their parsing.

## Specification\<T\> (abstract class)

Composable business rules that produce `Expression<Func<T, bool>>`.

```csharp
abstract Expression<Func<T, bool>> ToExpression()
bool IsSatisfiedBy(T entity)
protected virtual bool CacheCompilation => true    // when true (default), IsSatisfiedBy caches the compiled expression
                                                   // override to false for specifications that capture mutable state
Specification<T> And(Specification<T> other)
Specification<T> Or(Specification<T> other)
Specification<T> Not()
implicit operator Expression<Func<T, bool>>(Specification<T> spec)
```

---

# 3. Trellis.Primitives — Base Types & Concrete Value Objects

## JSON Converters (namespace: `Trellis`)

Trellis provides automatic JSON serialization for all value objects. `ParsableJsonConverter` handles scalar types; `MoneyJsonConverter` handles the `Money` composite type.

### ParsableJsonConverter\<T\>

Generic `System.Text.Json` converter for all types implementing `IParsable<T>`. Auto-applied via `[JsonConverter]` on source-generated value objects.

```csharp
public class ParsableJsonConverter<T> : JsonConverter<T> where T : IParsable<T>
```

Reads via `T.Parse(reader.GetString()!)`; writes via `writer.WriteStringValue(value.ToString())`.

### MoneyJsonConverter (namespace: `Trellis.Primitives`)

Serializes/deserializes `Money` as `{"amount": 99.99, "currency": "USD"}`.

```csharp
public class MoneyJsonConverter : JsonConverter<Money>
```

## Base Types (namespace: `Trellis`)

Value object base classes. Declare a `partial class` inheriting from these to trigger source generation of `TryCreate`, `Create`, `Parse`, JSON converters, and model binding.

### RequiredString\<TSelf\>

Inherits `ScalarValueObject<TSelf, string>`. Source generator provides on each `partial class Foo : RequiredString<Foo>`:

```csharp
// Auto-generated
static Result<Foo> TryCreate(string? value, string? fieldName = null)  // rejects null/empty/whitespace, auto-trims
static Foo Create(string? value, string? fieldName = null)
static explicit operator Foo(string value)
// IParsable<Foo>: Parse, TryParse
// [JsonConverter(typeof(ParsableJsonConverter<Foo>))]
```

#### `[StringLength]` — Optional Length Constraints

Apply `[StringLength(max)]` or `[StringLength(max, MinimumLength = min)]` to the class to add length validation into the generated `TryCreate`:

```csharp
[StringLength(50)]                        // max only
public partial class FirstName : RequiredString<FirstName> { }

[StringLength(500, MinimumLength = 10)]   // min + max
public partial class Description : RequiredString<Description> { }
```

Generated validation errors: `"{Name} must be at least {min} characters."`, `"{Name} must be {max} characters or fewer."`

> **Namespace note:** This is `Trellis.StringLengthAttribute`, not `System.ComponentModel.DataAnnotations.StringLengthAttribute`. If both namespaces are imported, disambiguate with `[Trellis.StringLength(max)]`.

#### `ValidateAdditional` — Optional Custom Validation Hook

Implement the `ValidateAdditional` partial method to add domain-specific validation (regex patterns, format checks, etc.). Called after built-in validations pass. If not implemented, the compiler removes the call — zero overhead.

```csharp
[StringLength(10)]
public partial class Sku : RequiredString<Sku>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
    {
        if (!Regex.IsMatch(value, @"^SKU-\d{6}$"))
            errorMessage = "Sku must match pattern SKU-XXXXXX.";
    }
}
```

**Signature:** `static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)`
- `value` — the validated string (not null, not whitespace, length-checked)
- `fieldName` — the normalized field name for error messages
- `errorMessage` — set to a non-null string to reject; leave null to accept. The generator wraps it in `Error.Validation(errorMessage, fieldName)` automatically.

### RequiredGuid\<TSelf\>

Inherits `ScalarValueObject<TSelf, Guid>`. Source generator provides:

```csharp
static Foo NewUniqueV4()
static Foo NewUniqueV7()
static Result<Foo> TryCreate(Guid value, string? fieldName = null)      // rejects Guid.Empty
static Result<Foo> TryCreate(Guid? value, string? fieldName = null)
static Result<Foo> TryCreate(string? value, string? fieldName = null)   // validates GUID format
static new Foo Create(Guid value)
static Foo Create(string stringValue)
static explicit operator Foo(Guid value)
// IParsable<Foo>: Parse, TryParse
// [JsonConverter(typeof(ParsableJsonConverter<Foo>))]
```

#### `ValidateAdditional` — Optional Custom Validation Hook

Same pattern as RequiredString. Signature: `static partial void ValidateAdditional(Guid value, string fieldName, ref string? errorMessage)`

```csharp
public partial class TenantId : RequiredGuid<TenantId>
{
    static partial void ValidateAdditional(Guid value, string fieldName, ref string? errorMessage)
    {
        if (value.Version != 7)
            errorMessage = "Tenant Id must be a v7 UUID.";
    }
}
```

### RequiredInt\<TSelf\>

Inherits `ScalarValueObject<TSelf, int>`. Source generator provides:

```csharp
static Result<Foo> TryCreate(int value, string? fieldName = null)       // accepts any int
static Result<Foo> TryCreate(int? value, string? fieldName = null)     // rejects null
static Result<Foo> TryCreate(string? value, string? fieldName = null)
static new Foo Create(int value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
```

With range constraints using `[Range]`:

```csharp
[Range(1, 999)]
public partial class LineItemQuantity : RequiredInt<LineItemQuantity> { }

[Range(0, 100)]  // constrains to 0–100 inclusive
public partial class StockQuantity : RequiredInt<StockQuantity> { }

// Generated TryCreate validates: min <= value <= max
// Error: "Line Item Quantity must be at least 1." / "Line Item Quantity must be at most 999."
```

#### `ValidateAdditional` — Optional Custom Validation Hook

Same pattern as RequiredString. Signature: `static partial void ValidateAdditional(int value, string fieldName, ref string? errorMessage)`

```csharp
[Range(1, 100)]
public partial class EvenPercentage : RequiredInt<EvenPercentage>
{
    static partial void ValidateAdditional(int value, string fieldName, ref string? errorMessage)
    {
        if (value % 2 != 0)
            errorMessage = "Even Percentage must be an even number.";
    }
}
```

### RequiredDecimal\<TSelf\>

Inherits `ScalarValueObject<TSelf, decimal>`. Same pattern as RequiredInt with `decimal`.

```csharp
static Result<Foo> TryCreate(decimal value, string? fieldName = null)
static Result<Foo> TryCreate(string? value, string? fieldName = null)
static Foo Create(decimal value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
```

#### `[Range]` — Optional Range Constraints

```csharp
[Range(1, 999)]           // whole-number bounds (int constructor)
public partial class UnitPrice : RequiredDecimal<UnitPrice> { }

[Range(0.01, 99.99)]      // fractional bounds (double constructor)
public partial class TaxRate : RequiredDecimal<TaxRate> { }
```

> **Note:** C# does not allow `decimal` in attribute constructors, so fractional ranges use the `double` constructor overload. The generated validation still operates on the `decimal` value.

`ValidateAdditional` is also available: `static partial void ValidateAdditional(decimal value, string fieldName, ref string? errorMessage)`

### RequiredLong\<TSelf\>

Inherits `ScalarValueObject<TSelf, long>`. Source generator provides:

```csharp
static Result<Foo> TryCreate(long value, string? fieldName = null)
static Result<Foo> TryCreate(long? value, string? fieldName = null)     // rejects null
static Result<Foo> TryCreate(string? value, string? fieldName = null)
static new Foo Create(long value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
```

With range constraints using `[Range(long, long)]` — supports ranges exceeding `int.MaxValue`:

```csharp
[Range(0L, 5_000_000_000L)]
public partial class LargeId : RequiredLong<LargeId> { }

[Range(1L, 9_999_999_999L)]
public partial class PhoneNumber : RequiredLong<PhoneNumber> { }
```

#### `ValidateAdditional` — Optional Custom Validation Hook

Same pattern as RequiredInt. Signature: `static partial void ValidateAdditional(long value, string fieldName, ref string? errorMessage)`

### RequiredBool\<TSelf\>

Inherits `ScalarValueObject<TSelf, bool>`. Distinguishes `false` (an explicit value) from `null`/missing — solves the "was the property `false` or not provided?" problem.

```csharp
static Result<Foo> TryCreate(bool value, string? fieldName = null)      // always succeeds
static Result<Foo> TryCreate(bool? value, string? fieldName = null)     // rejects null
static Result<Foo> TryCreate(string? value, string? fieldName = null)   // parses "true"/"false"
static new Foo Create(bool value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
```

```csharp
public partial class GiftWrap : RequiredBool<GiftWrap> { }

// Usage
var wrap = GiftWrap.Create(false);   // wrap.Value == false (explicitly false, not missing)
var result = GiftWrap.TryCreate(null as bool?);  // failure — null is not allowed
```

#### `ValidateAdditional` — Optional Custom Validation Hook

Signature: `static partial void ValidateAdditional(bool value, string fieldName, ref string? errorMessage)`

### RequiredDateTime\<TSelf\>

Inherits `ScalarValueObject<TSelf, DateTime>`. Rejects `DateTime.MinValue` (the "empty" equivalent for `DateTime`). Overrides `ToString()` to use ISO 8601 round-trip format (`"O"`) for deterministic JSON serialization.

```csharp
static Result<Foo> TryCreate(DateTime value, string? fieldName = null)    // rejects DateTime.MinValue
static Result<Foo> TryCreate(DateTime? value, string? fieldName = null)   // rejects null
static Result<Foo> TryCreate(string? value, string? fieldName = null)     // invariant culture parsing
static new Foo Create(DateTime value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
```

```csharp
public partial class OrderDate : RequiredDateTime<OrderDate> { }

// Usage
var date = OrderDate.Create(DateTime.UtcNow);
var bad = OrderDate.TryCreate(DateTime.MinValue);  // failure — MinValue rejected
```

#### `ValidateAdditional` — Optional Custom Validation Hook

Signature: `static partial void ValidateAdditional(DateTime value, string fieldName, ref string? errorMessage)`

### `[Range]` Attribute Reference

The `[Range]` attribute constrains numeric value objects at creation time. The source generator emits min/max validation into `TryCreate`.

| Constructor | Applies To | Example |
|---|---|---|
| `[Range(int min, int max)]` | `RequiredInt`, `RequiredDecimal` (whole numbers) | `[Range(1, 999)]` |
| `[Range(long min, long max)]` | `RequiredLong` (values exceeding `int.MaxValue`) | `[Range(0L, 5_000_000_000L)]` |
| `[Range(double min, double max)]` | `RequiredDecimal` (fractional bounds) | `[Range(0.01, 99.99)]` |

> **Namespace note:** This is `Trellis.RangeAttribute`, not `System.ComponentModel.DataAnnotations.RangeAttribute`. If both namespaces are imported, disambiguate with `[Trellis.Range(min, max)]`.

### RequiredEnum\<TSelf\>

**NOT a ScalarValueObject** — standalone hierarchy. Smart enum pattern.

```csharp
string Value { get; }      // semantic symbolic value; defaults to field name or [EnumValue(...)]
int Ordinal { get; }       // declaration-order metadata, not stable identity

static IReadOnlyCollection<TSelf> GetAll()
static Result<TSelf> TryFromName(string? name, string? fieldName = null)  // case-insensitive symbolic value lookup
bool Is(TSelf value)                               // allocation-free single-value check
bool Is(TSelf value1, TSelf value2)                // allocation-free two-value check
bool Is(params TSelf[] values)
bool IsNot(TSelf value)                            // allocation-free single-value check
bool IsNot(TSelf value1, TSelf value2)             // allocation-free two-value check
bool IsNot(params TSelf[] values)

// Source-generated:
static Result<Foo> TryCreate(string? value, string? fieldName = null)
static Foo Create(string value)   // throws on invalid input (from IScalarValue)
// IParsable<Foo>, [JsonConverter(typeof(RequiredEnumJsonConverter<Foo>))]
```

Use `[EnumValue("code")]` only when the external name must differ from the default field name.

### EnumValueAttribute

Customizes the wire/storage name for a `RequiredEnum` member. Applied to static fields.

```csharp
[AttributeUsage(AttributeTargets.Field)]
public sealed class EnumValueAttribute(string value) : Attribute
{
    public string Value { get; }
}

// Usage — custom wire name different from field name
public partial class PaymentMethod : RequiredEnum<PaymentMethod>
{
    [EnumValue("credit-card")]
    public static readonly PaymentMethod CreditCard = new();

    [EnumValue("bank-transfer")]
    public static readonly PaymentMethod BankTransfer = new();
}
```

### RequiredEnumJsonConverter\<T\>

JSON converter for `RequiredEnum<T>` types. Auto-applied by the source generator via `[JsonConverter(typeof(RequiredEnumJsonConverter<T>))]`. Serializes to/from the string value (field name or `[EnumValue]` override).

```csharp
public sealed class RequiredEnumJsonConverter<TRequiredEnum> : JsonConverter<TRequiredEnum>
    where TRequiredEnum : RequiredEnum<TRequiredEnum>, IScalarValue<TRequiredEnum, string>
// Reads: string → TryFromName/TryFromValue → TRequiredEnum
// Writes: TRequiredEnum → Value (string)
// Null tokens → null
```

You do not need to register this manually — the source generator adds it to each `RequiredEnum<T>` type.

## Concrete Value Objects (namespace: `Trellis.Primitives`)

All have `TryCreate` → `Result<T>` and `Create` → `T` (throws). All implement `IParsable<T>` and have `[JsonConverter]`.

| Type | Primitive | Validation | Extra Members |
|------|-----------|------------|---------------|
| `EmailAddress` | `string` | RFC 5322 regex, case-insensitive, trims | — |
| `PhoneNumber` | `string` | E.164 format (`^\+[1-9]\d{7,14}$`), normalizes | `GetCountryCode()` |
| `Url` | `string` | Valid absolute URI, HTTP/HTTPS only | `Scheme`, `Host`, `Port`, `Path`, `Query`, `IsSecure`, `ToUri()` |
| `Hostname` | `string` | RFC 1123 compliant, ≤255 chars | — |
| `IpAddress` | `string` | `System.Net.IPAddress.TryParse` (v4/v6) | `ToIPAddress()` |
| `Slug` | `string` | Lowercase alphanumeric + hyphens, no consecutive/leading/trailing | — |
| `CountryCode` | `string` | 2 letters, ISO 3166-1 alpha-2, uppercase | — |
| `CurrencyCode` | `string` | 3 letters, ISO 4217, uppercase | — |
| `LanguageCode` | `string` | 2 letters, ISO 639-1, lowercase | — |
| `Age` | `int` | 0–150 inclusive | — |
| `Percentage` | `decimal` | 0–100 inclusive | `Zero`, `Full`, `AsFraction()`, `Of(decimal)`, `FromFraction(decimal, fieldName?)`, `TryCreate(decimal?)` |
| `MonetaryAmount` | `decimal` | Non-negative, rounds to 2 dp | `Zero`, `Add`, `Subtract`, `Multiply(int)`, `Multiply(decimal)` |
| `Money` | multi-value | Amount ≥ 0, valid currency code | See below |

### MonetaryAmount (extends ScalarValueObject)

Scalar value object for single-currency systems where currency is a system-wide policy, not per-row data. Wraps a non-negative `decimal` rounded to 2 decimal places. JSON: plain number (e.g. `99.99`). EF Core: maps to 1 `decimal` column (via `ApplyTrellisConventions`).

```csharp
// Implements: ScalarValueObject<MonetaryAmount, decimal>, IScalarValue<MonetaryAmount, decimal>, IParsable<MonetaryAmount>

static Result<MonetaryAmount> TryCreate(decimal value)
static Result<MonetaryAmount> TryCreate(decimal? value)
static MonetaryAmount Create(decimal value)
static MonetaryAmount Zero { get; }

// Arithmetic (returns Result — handles overflow)
Result<MonetaryAmount> Add(MonetaryAmount other)
Result<MonetaryAmount> Subtract(MonetaryAmount other)
Result<MonetaryAmount> Multiply(int quantity)
Result<MonetaryAmount> Multiply(decimal multiplier)
```

### Money (extends ValueObject, NOT ScalarValueObject)

Structured value object with two semantic components: `Amount` (decimal) + `Currency` (CurrencyCode). JSON: `{"amount": 99.99, "currency": "USD"}`.

```csharp
decimal Amount { get; }
CurrencyCode Currency { get; }

static Result<Money> TryCreate(decimal amount, string currencyCode, string? fieldName = null)
static Money Create(decimal amount, string currencyCode)
static Result<Money> Zero(string currencyCode = "USD")

// Arithmetic (returns Result — enforces same currency)
Result<Money> Add(Money other)
Result<Money> Subtract(Money other)
Result<Money> Multiply(decimal multiplier)
Result<Money> Multiply(int quantity)
Result<Money> Divide(decimal divisor)
Result<Money> Divide(int divisor)
Result<Money[]> Allocate(params int[] ratios)

// Comparison
bool IsGreaterThan(Money other)
bool IsGreaterThanOrEqual(Money other)
bool IsLessThan(Money other)
bool IsLessThanOrEqual(Money other)
```

---

# 4. Trellis.Authorization

**Namespace: `Trellis.Authorization`**

## Actor (sealed record)

Represents the authenticated user making the current request. Contains identity, permissions, forbidden permissions, and contextual attributes. Hydrated during authentication middleware. Used by authorization behaviors to check permissions and resource ownership.

```csharp
Actor(string Id, IReadOnlySet<string> Permissions, IReadOnlySet<string> ForbiddenPermissions, IReadOnlyDictionary<string, string> Attributes)

static Actor Create(string id, IReadOnlySet<string> permissions)
bool HasPermission(string permission)
bool HasPermission(string permission, string scope)     // checks "permission:scope"
bool HasAllPermissions(IEnumerable<string> permissions)
bool HasAnyPermission(IEnumerable<string> permissions)
bool IsOwner(string resourceOwnerId)
bool HasAttribute(string key)
string? GetAttribute(string key)
```

## Interfaces

- **`IActorProvider`** — Provides the current authenticated actor asynchronously. Unified interface for all actor resolution (JWT claims, database lookups, etc.).
- **`IAuthorize`** — Declares required permissions; checked by authorization pipeline behavior.
- **`IAuthorizeResource<TResource>`** — Resource-based authorization; receives the loaded resource and actor.
- **`IResourceLoader<TMessage, TResource>`** — Loads the resource for resource-based authorization checks.
- **`ResourceLoaderById<TMessage, TResource, TId>`** — Base class for the common "extract ID from message, load by ID" pattern.

```csharp
interface IActorProvider { Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default); }
interface IAuthorize { IReadOnlyList<string> RequiredPermissions { get; } }
interface IAuthorizeResource<TResource> { IResult Authorize(Actor actor, TResource resource); }
interface IResourceLoader<TMessage, TResource> { Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken cancellationToken); }
abstract class ResourceLoaderById<TMessage, TResource, TId> : IResourceLoader<TMessage, TResource>
{
    protected abstract TId GetId(TMessage message);
    protected abstract Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken cancellationToken);
}
```

## ActorAttributes Constants

Well-known attribute keys for ABAC (Attribute-Based Access Control): `IpAddress`, `MfaAuthenticated`, `TenantId`, `PreferredUsername`, etc.

```csharp
const string TenantId = "tid";
const string PreferredUsername = "preferred_username";
const string AuthorizedParty = "azp";
const string AuthorizedPartyAcr = "azpacr";
const string AuthContextClassReference = "acrs";
const string IpAddress = "ip_address";
const string MfaAuthenticated = "mfa";
```

---

# 5. Trellis.Asp — ASP.NET Core Integration

**Namespace: `Trellis.Asp`**

## Error → HTTP Status Mapping

| Error Type | HTTP Status |
|-----------|-------------|
| `ValidationError` | 400 |
| `BadRequestError` | 400 |
| `UnauthorizedError` | 401 |
| `ForbiddenError` | 403 |
| `NotFoundError` | 404 |
| `ConflictError` | 409 |
| `PreconditionFailedError` | 412 |
| `DomainError` | 422 |
| `PreconditionRequiredError` | 428 |
| `RateLimitError` | 429 |
| `UnexpectedError` | 500 |
| `ServiceUnavailableError` | 503 |

Customizable via `TrellisAspOptions.MapError<TError>(int statusCode)`.

### TrellisAspOptions

Configures custom error-to-HTTP status code mappings. The default mappings (above) can be overridden for custom error types.

```csharp
public sealed class TrellisAspOptions
{
    TrellisAspOptions MapError<TError>(int statusCode) where TError : Error
}

// Usage
builder.Services.AddTrellisAsp(options => options.MapError<MyCustomError>(418));
```

## MVC Controller Extensions

Extension methods for mapping `Result<T>` to `ActionResult` in MVC controllers. `ToActionResult` maps errors to RFC 9457 Problem Details responses.

```csharp
ActionResult<T> ToActionResult<T>(this Result<T> result, ControllerBase controller)
ActionResult<T> ToCreatedAtActionResult<T>(this Result<T> result, ControllerBase controller,
    string actionName, Func<T, object?> routeValues, string? controllerName = null)

// Transform overloads — map domain type to DTO inline
ActionResult<TOut> ToActionResult<TIn, TOut>(this Result<TIn> result, ControllerBase controller,
    Func<TIn, TOut> map)
ActionResult<TOut> ToActionResult<TIn, TOut>(this Result<TIn> result, ControllerBase controller,
    Func<TIn, ContentRangeHeaderValue> funcRange, Func<TIn, TOut> funcValue)
ActionResult<TOut> ToCreatedAtActionResult<TValue, TOut>(this Result<TValue> result, ControllerBase controller,
    string actionName, Func<TValue, object?> routeValues, Func<TValue, TOut> map, string? controllerName = null)
// + async variants for Task<Result<T>> and ValueTask<Result<T>>
// + partial content (206) variant with ContentRangeHeaderValue

// Error direct conversion
ActionResult<TValue> ToActionResult<TValue>(this Error error, ControllerBase controller)
```

## Minimal API Extensions

Extension methods for mapping `Result<T>` to `IResult` in Minimal API endpoints. Same error-to-HTTP mapping as MVC but returns `IResult` instead of `ActionResult`.

```csharp
IResult ToHttpResult<T>(this Result<T> result, TrellisAspOptions? options = null)
IResult ToCreatedAtRouteHttpResult<T>(this Result<T> result,
    string routeName, Func<T, RouteValueDictionary> routeValues, TrellisAspOptions? options = null)

// Transform overload — map domain type to DTO inline
IResult ToCreatedAtRouteHttpResult<TValue, TOut>(this Result<TValue> result,
    string routeName, Func<TValue, RouteValueDictionary> routeValues, Func<TValue, TOut> map,
    TrellisAspOptions? options = null)
// + async variants

// Error direct conversion
IResult ToHttpResult(this Error error, TrellisAspOptions? options = null)
```

## PartialObjectResult — HTTP 206 Partial Content

HTTP 206 Partial Content response for paginated results. Automatically sets `Content-Range` headers per RFC 9110.

```csharp
PartialObjectResult(long rangeStart, long rangeEnd, long totalLength, object? value)
PartialObjectResult(ContentRangeHeaderValue contentRange, object? value)
ContentRangeHeaderValue ContentRange { get; }
```

## Maybe\<T\> Support Types

Registered automatically by `AddScalarValueValidation()`.

| Type | Purpose |
|------|---------|
| `MaybeModelBinder<TValue, TPrimitive>` | Model-binds `Maybe<T>` from query/route |
| `MaybeScalarValueJsonConverter<TValue, TPrimitive>` | JSON serialization for `Maybe<T>` of scalar VOs |
| `MaybeSuppressChildValidationMetadataProvider` | Suppresses child validation on `Maybe<T>` properties |

## Registration

Service collection extension methods: `AddScalarValueValidation()` (on `IMvcBuilder`), `AddScalarValueValidationForMinimalApi()` (on `IServiceCollection`). Middleware: `UseScalarValueValidation()` (on `IApplicationBuilder`).

```csharp
// MVC — registers model binders, JSON converters, validation filters
builder.Services.AddControllers().AddScalarValueValidation();

// Minimal API
builder.Services.AddScalarValueValidationForMinimalApi();
app.UseScalarValueValidation();  // middleware

// Full setup
builder.Services.AddTrellisAsp();
builder.Services.AddTrellisAsp(options => options.MapError<MyCustomError>(418));
```

### WithScalarValueValidation (Minimal API per-endpoint)

For Minimal API endpoints, apply scalar value validation per route:

```csharp
app.MapPost("/api/orders", handler).WithScalarValueValidation();
```

## Source Generator — AOT JSON Converters

The `Trellis.AspSourceGenerator` package provides a source generator that auto-discovers all `IScalarValue<TSelf, TPrimitive>` types and emits AOT-compatible `System.Text.Json` converters. Apply `[GenerateScalarValueConverters]` to a partial `JsonSerializerContext`:

```csharp
using Trellis.Asp;

[GenerateScalarValueConverters]
[JsonSerializable(typeof(MyDto))]
public partial class AppJsonSerializerContext : JsonSerializerContext { }

// Generator auto-adds:
// [JsonSerializable(typeof(CustomerId))]
// [JsonSerializable(typeof(EmailAddress))]
// etc.
```

Benefits: Native AOT compatible, no reflection, trimming-safe, faster startup.

---

# 6. Trellis.Asp.Authorization — Actor Providers

**Namespace: `Trellis.Asp.Authorization`**

## ClaimsActorProvider (Generic OIDC/JWT)

Generic actor provider that maps standard OIDC/JWT claims to an `Actor`. Works with any identity provider (Auth0, Keycloak, Okta, Entra, etc.).

```csharp
// Registration
services.AddClaimsActorProvider();
services.AddClaimsActorProvider(options => {
    options.ActorIdClaim = "sub";           // default: "sub"
    options.PermissionsClaim = "permissions"; // default: "permissions"
});

// ClaimsActorProvider : IActorProvider
// Extracts Actor from HttpContext claims using configurable claim names
```

## EntraActorProvider (Production)

Production actor provider that maps Microsoft Entra ID (Azure AD) JWT claims to an `Actor`. Extends `ClaimsActorProvider` with Entra-specific claim mapping for permissions, forbidden permissions, and ABAC attributes.

```csharp
// Registration
services.AddEntraActorProvider();
services.AddEntraActorProvider(options => {
    options.IdClaimType = "sub";
    options.MapPermissions = claims => /* custom extraction */;
});

// EntraActorProvider : ClaimsActorProvider
// Extracts Actor from HttpContext claims (Entra ID / Azure AD)
```

## DevelopmentActorProvider (Development/Testing)

Development/testing actor provider that reads `Actor` from the `X-Test-Actor` HTTP header (JSON). Falls back to a configurable default actor. **Throws unconditionally in any non-Development environment** to prevent accidental use in Production.

```csharp
// Registration — for development environments
services.AddDevelopmentActorProvider();
services.AddDevelopmentActorProvider(options => {
    options.DefaultActorId = "admin";
    options.DefaultPermissions = new HashSet<string> { "orders:create", "orders:read" };
    options.ThrowOnMalformedHeader = false; // default
});

// DevelopmentActorProvider : IActorProvider
// Throws InvalidOperationException unconditionally in non-Development environments
// Reads Actor from X-Test-Actor HTTP header (JSON) in Development
// Falls back to configurable default Actor when header absent
// Header JSON schema: { "Id": "...", "Permissions": [...], "ForbiddenPermissions": [...], "Attributes": {...} }

// Conditional registration pattern:
if (builder.Environment.IsDevelopment())
    services.AddDevelopmentActorProvider();
else
    services.AddEntraActorProvider();
```

## CachingActorProvider (Decorator)

Caching decorator that wraps any `IActorProvider` and caches the result per-scope. Use with database-backed providers to avoid redundant queries within a single request.

```csharp
// Registration — wraps DatabaseActorProvider with per-request caching
services.AddCachingActorProvider<DatabaseActorProvider>();

// CachingActorProvider : IActorProvider
// Caches the Task<Actor> from the first call; subsequent calls return the same result
```

| Type | Purpose |
|------|---------|
| `ClaimsActorProvider` | Generic OIDC/JWT — maps configurable claims to `Actor` |
| `ClaimsActorOptions` | Configuration for generic claim mapping (`ActorIdClaim`, `PermissionsClaim`) |
| `EntraActorProvider` | Production — maps Entra JWT claims to `Actor` (extends `ClaimsActorProvider`) |
| `EntraActorOptions` | Configuration for Entra claim mapping |
| `DevelopmentActorProvider` | Development/testing — reads `X-Test-Actor` header |
| `DevelopmentActorOptions` | Configuration for default actor and error handling |
| `CachingActorProvider` | Decorator — caches inner provider per-scope |
| `ServiceCollectionExtensions` | `AddClaimsActorProvider()`, `AddEntraActorProvider()`, `AddDevelopmentActorProvider()`, `AddCachingActorProvider<T>()` |

### ClaimsActorOptions

Configures how `ClaimsActorProvider` extracts actor identity from JWT claims.

```csharp
public class ClaimsActorOptions
{
    string ActorIdClaim { get; set; }     // default: "sub"
    string PermissionsClaim { get; set; } // default: "permissions"
}
```

### EntraActorOptions

Configures how `EntraActorProvider` extracts actor identity from JWT claims.

```csharp
public sealed class EntraActorOptions
{
    string IdClaimType { get; set; }  // default: "http://schemas.microsoft.com/identity/claims/objectidentifier"
    Func<IEnumerable<Claim>, IReadOnlySet<string>> MapPermissions { get; set; }  // default: reads "roles" claims
    Func<IEnumerable<Claim>, IReadOnlySet<string>> MapForbiddenPermissions { get; set; }  // default: empty set
    Func<IEnumerable<Claim>, HttpContext, IReadOnlyDictionary<string, string>> MapAttributes { get; set; }  // default: tid, preferred_username, azp, IP, MFA
}

// Usage
services.AddEntraActorProvider(options =>
{
    options.IdClaimType = "sub";
    options.MapPermissions = claims => claims.Where(c => c.Type == "scope").Select(c => c.Value).ToHashSet();
});
```

### DevelopmentActorOptions

Configures the `DevelopmentActorProvider` used during local development. Reads the `X-Test-Actor` header as JSON.

```csharp
public sealed class DevelopmentActorOptions
{
    string DefaultActorId { get; set; }  // default: "development"
    IReadOnlySet<string> DefaultPermissions { get; set; }  // default: empty
    bool ThrowOnMalformedHeader { get; set; }  // default: false
}
```

---

# 7. Trellis.Http — HttpClient → Result Extensions

**Namespace: `Trellis.Http`**

Fluent pipeline for `HttpResponseMessage` → `Result<T>`:

```csharp
// Status handlers (chainable, each returns Result<HttpResponseMessage>)
HandleNotFound(this HttpResponseMessage, NotFoundError)
HandleUnauthorized(this HttpResponseMessage, UnauthorizedError)
HandleForbidden(this HttpResponseMessage, ForbiddenError)
HandleConflict(this HttpResponseMessage, ConflictError)
HandleClientError(this HttpResponseMessage, Func<HttpStatusCode, Error>)
HandleServerError(this HttpResponseMessage, Func<HttpStatusCode, Error>)
EnsureSuccess(this HttpResponseMessage, Func<HttpStatusCode, Error>? errorFactory = null)

// Custom async error handling with context
Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this HttpResponseMessage,
    Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callback, TContext context, CancellationToken cancellationToken)
Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this Task<HttpResponseMessage>,
    Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callback, TContext context, CancellationToken cancellationToken)

// Also chainable on Result<HttpResponseMessage> for fluent error handling
HandleNotFound(this Result<HttpResponseMessage>, NotFoundError)
// ... etc.

// JSON deserialization
Task<Result<T>> ReadResultFromJsonAsync<T>(this HttpResponseMessage, JsonTypeInfo<T>, CancellationToken)
Task<Result<Maybe<T>>> ReadResultMaybeFromJsonAsync<T>(this HttpResponseMessage, JsonTypeInfo<T>, CancellationToken)
// + overloads on Task<HttpResponseMessage>, Result<HttpResponseMessage>, Task<Result<HttpResponseMessage>>
```

### Usage Pattern

```csharp
var result = await httpClient.GetAsync($"/api/orders/{id}")
    .HandleNotFoundAsync(Error.NotFound($"Order {id} not found"))
    .HandleUnauthorizedAsync(Error.Unauthorized("Not authenticated"))
    .EnsureSuccessAsync()
    .ReadResultFromJsonAsync(OrderJsonContext.Default.Order, ct);
```

---

# 8. Trellis.Mediator — CQRS Pipeline Behaviors

**Namespace: `Trellis.Mediator`**

> **Import:** Add `using Trellis.Mediator;` for registration extensions (`AddTrellisBehaviors`, `AddResourceAuthorization`). Commands and queries use `Mediator` namespace (`ICommand<T>`, `IQuery<T>`) from the Mediator library. Authorization interfaces use `using Trellis.Authorization;`.

CQRS pattern: define a command/query record implementing `ICommand<T>`/`IQuery<T>`, implement a handler, register with `AddTrellisBehaviors()`. The mediator dispatches through the pipeline behavior chain.

### Pipeline Order

Exception → Tracing → Logging → Authorization → ResourceAuthorization (actor-only) → Validation

Resource-based authorization with a loaded resource (`IAuthorizeResource<TResource>`) is auto-discovered via `AddResourceAuthorization(Assembly)`, or registered explicitly per-command via `AddResourceAuthorization<TMessage, TResource, TResponse>()` for AOT scenarios.

### Behaviors

Pipeline behaviors execute in order: `ExceptionBehavior` (catch unhandled) → `TracingBehavior` (OpenTelemetry) → `LoggingBehavior` (structured logging) → `AuthorizationBehavior` (permission check) → `ResourceAuthorizationBehavior` (ownership check) → `ValidationBehavior` (IValidate) → Handler.

| Behavior | Constraint on TMessage | Purpose |
|----------|----------------------|---------|
| `ExceptionBehavior` | `IMessage` | Catches unhandled exceptions → `Error.Unexpected` |
| `TracingBehavior` | `IMessage` | OpenTelemetry Activity span (`ActivitySourceName = "Trellis.Mediator"`) |
| `LoggingBehavior` | `IMessage` | Structured logging with duration |
| `AuthorizationBehavior` | `IAuthorize, IMessage` | Checks `HasAllPermissions` → `Error.Forbidden` |
| `ResourceAuthorizationBehavior<,,>` | `IAuthorizeResource<TResource>, IMessage` | Loads resource via `IResourceLoader`, delegates to `message.Authorize(actor, resource)`. Auto-discovered via `AddResourceAuthorization(Assembly)`. |
| `ValidationBehavior` | `IValidate, IMessage` | Calls `message.Validate()`, short-circuits |

### IValidate Interface

Implement on a command/query to add validation before the handler runs. The `ValidationBehavior` calls `Validate()` and short-circuits with `ValidationError` on failure.

```csharp
interface IValidate { IResult Validate(); }
```

### TracingBehavior Constants

OpenTelemetry activity source names used by the mediator tracing behavior for distributed tracing.

```csharp
public const string ActivitySourceName = "Trellis.Mediator";
```

Use `ActivitySourceName` to register the activity source with OpenTelemetry: `builder.AddSource(TracingBehavior<IMessage, IResult>.ActivitySourceName)`.

### Registration

`AddTrellisBehaviors()` registers all pipeline behaviors. `AddResourceAuthorization(params Assembly[])` scans assemblies for `IResourceLoader` implementations.

```csharp
services.AddTrellisBehaviors();

// Recommended: scan-register both IAuthorizeResource<T> behaviors and IResourceLoader<,> implementations
services.AddResourceAuthorization(typeof(CancelOrderCommand).Assembly);

// OR: explicit per-command registration (AOT-compatible)
services.AddResourceAuthorization<CancelOrderCommand, Order, Result<Order>>();
services.AddResourceLoaders(typeof(CancelOrderResourceLoader).Assembly);
```

---

# 9. Trellis.Testing

See **`trellis-api-testing-reference.md`** for the complete Trellis.Testing API reference, including FluentAssertions extensions, test builders, FakeRepository, TestActorProvider, and testing patterns.

---

# 10. Trellis.FluentValidation

**Namespace: `Trellis.FluentValidation`**

```csharp
// Convert ValidationResult to Result<T>
Result<T> ToResult<T>(this ValidationResult validationResult, T value)

// Direct validate-and-return
Result<T> ValidateToResult<T>(this IValidator<T> validator, T value)
Task<Result<T>> ValidateToResultAsync<T>(this IValidator<T> validator, T value, CancellationToken cancellationToken = default)
```

---

# 11. Trellis.Stateless — State Machine Integration

**Namespace: `Trellis.Stateless`**

```csharp
Result<TState> FireResult<TState, TTrigger>(this StateMachine<TState, TTrigger> stateMachine, TTrigger trigger)
// Success → new state | Invalid transition → Error.Domain with code "state.machine.invalid.transition"
```

### LazyStateMachine\<TState, TTrigger\>

Defers state machine construction until first use, solving the ORM materialization problem where `stateAccessor` reads a default or uninitialized value before entity properties are populated.

```csharp
// Constructor — stateAccessor/stateMutator not invoked, configure not called
new LazyStateMachine<TState, TTrigger>(
    Func<TState> stateAccessor,
    Action<TState> stateMutator,
    Action<StateMachine<TState, TTrigger>> configure)

// Properties
StateMachine<TState, TTrigger> Machine { get; }  // Lazily creates and configures on first access

// Methods
Result<TState> FireResult(TTrigger trigger)  // Delegates to Machine.FireResult(trigger)
```

---

# 12. Trellis.EntityFrameworkCore

**Namespace: `Trellis.EntityFrameworkCore`**

### DbContext Extensions

`SaveChangesResultAsync()` and `SaveChangesResultUnitAsync()` wrap EF Core `SaveChanges` in `Result<T>`. Duplicate key violations become `ConflictError`; concurrency exceptions become `ConflictError`.

```csharp
Task<Result<int>> SaveChangesResultAsync(this DbContext context, CancellationToken cancellationToken = default)
Task<Result<int>> SaveChangesResultAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, CancellationToken cancellationToken = default)
Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
// DbUpdateConcurrencyException → Error.Conflict
// Duplicate key → Error.Conflict
// FK violation → Error.Domain
```

### Queryable Extensions

`FirstOrDefaultResultAsync` returns `NotFoundError` if missing; `FirstOrDefaultMaybeAsync` returns `Maybe<T>.None` if missing; `SingleOrDefaultMaybeAsync` for unique-or-none queries.

```csharp
Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken cancellationToken = default)
Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Error notFoundError, CancellationToken cancellationToken = default)
Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, Error notFoundError, CancellationToken cancellationToken = default)
IQueryable<T> Where<T>(this IQueryable<T> query, Specification<T> specification)
```

### Value Converter Registration

`ApplyTrellisConventions()` in `ConfigureConventions` registers value converters for all `IScalarValue` types and `Money`. Also registers `AggregateETagConvention` for optimistic concurrency. Call once — do NOT add manual `HasConversion` for Trellis types.

```csharp
// In ConfigureConventions (NOT OnModelCreating)
configurationBuilder.ApplyTrellisConventions(typeof(Order).Assembly);
// Auto-registers converters for all IScalarValue and RequiredEnum types
// Auto-maps Money properties as owned types (Amount + Currency columns)
// MonetaryAmount maps to a single decimal column (scalar value object convention)
// Auto-marks Aggregate<TId>.ETag as IsConcurrencyToken()
```

### Aggregate ETag Convention and Interceptor

Optimistic concurrency is automatic for all `Aggregate<TId>` entities:

- **`AggregateETagConvention`** (registered by `ApplyTrellisConventions`): marks `ETag` as `IsConcurrencyToken()` on entities implementing `IAggregate`
- **`AggregateETagInterceptor`** (registered by `AddTrellisInterceptors()`): generates a new GUID-based ETag on `EntityState.Modified` aggregate entries before `SaveChanges`

No additional configuration is needed. When two processes modify the same aggregate concurrently, the second `SaveChangesResultAsync` returns `ConflictError`. At the HTTP layer, use `OptionalETag` for `If-Match` validation → `PreconditionFailedError` (412).

### Money Property Convention

`Money` properties on entities are automatically mapped as owned types — no `OwnsOne` configuration needed. This includes `Money` properties declared on owned entity types (e.g., items inside `OwnsMany` collections). Column naming convention:

| Property Name | Amount Column | Currency Column | Amount Type | Currency Type |
|---------------|---------------|-----------------|-------------|---------------|
| `Price` | `Price` | `PriceCurrency` | `decimal(18,3)` | `nvarchar(3)` |
| `ShippingCost` | `ShippingCost` | `ShippingCostCurrency` | `decimal(18,3)` | `nvarchar(3)` |

Explicit `OwnsOne` configuration takes precedence over the convention.

`Maybe<Money>` properties are also supported — `MaybeConvention` creates an optional ownership navigation with nullable Amount/Currency columns. No manual `OwnsOne` needed.

### Maybe\<T\> Property Mapping

`Maybe<T>` is a `readonly struct`. EF Core cannot mark non-nullable struct properties as optional — calling `IsRequired(false)` or setting `IsNullable = true` throws `InvalidOperationException`. Use C# 13 `partial` properties with the `Trellis.EntityFrameworkCore.Generator` source generator:

```csharp
// Entity — just declare partial Maybe<T> properties
public partial class Customer
{
    public CustomerId Id { get; set; } = null!;

    public partial Maybe<PhoneNumber> Phone { get; set; }

    public partial Maybe<DateTime> SubmittedAt { get; set; }
}

// OnModelCreating — no configuration needed for Maybe<T>, convention handles everything
modelBuilder.Entity<Customer>(b =>
{
    b.HasKey(c => c.Id);
});
```

The source generator emits a private `_camelCase` backing field and getter/setter for each `partial Maybe<T>` property. The `MaybeConvention` (registered by `ApplyTrellisConventions`) auto-discovers `Maybe<T>` properties, ignores the struct property, maps the backing field as nullable, and sets the column name to the property name. When `T` is a composite owned type (e.g., `Money`), `MaybeConvention` creates an optional ownership navigation instead of a scalar column.

Backing field naming: `Phone` → `_phone`, `SubmittedAt` → `_submittedAt`, `AlternateEmail` → `_alternateEmail`.

If a `Maybe<T>` property is not declared `partial`, the generator emits diagnostic `TRLSGEN100`.

**Troubleshooting:** If the generator produces no output despite correct `partial` declarations, run a clean build (`dotnet clean` followed by `dotnet build`). Stale incremental build artifacts can prevent the generator from executing.

### Maybe\<T\> Queryable Extensions

> **Recommended approach:** Register `AddTrellisInterceptors()` in your DbContext options — this enables both the `MaybeQueryInterceptor` (for `Maybe<T>` properties) and the `ScalarValueQueryInterceptor` (for natural value object comparisons, string methods, and properties in LINQ). The helper methods below (`WhereNone`, `WhereHasValue`, etc.) are available as alternatives when the interceptor is not registered or for explicit control.

Because `MaybeConvention` ignores the `Maybe<T>` CLR property, EF Core cannot translate direct LINQ references to it. Use these extension methods instead of raw `EF.Property` calls:

```csharp
// WhereNone — WHERE backing_field IS NULL
IQueryable<TEntity> WhereNone<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// WhereHasValue — WHERE backing_field IS NOT NULL
IQueryable<TEntity> WhereHasValue<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// WhereEquals — WHERE backing_field = @value
IQueryable<TEntity> WhereEquals<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
    TInner value)

// WhereLessThan — WHERE backing_field < @value (TInner : IComparable<TInner>)
IQueryable<TEntity> WhereLessThan<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
    TInner value)

// WhereLessThanOrEqual — WHERE backing_field <= @value
IQueryable<TEntity> WhereLessThanOrEqual<TEntity, TInner>(...)

// WhereGreaterThan — WHERE backing_field > @value
IQueryable<TEntity> WhereGreaterThan<TEntity, TInner>(...)

// WhereGreaterThanOrEqual — WHERE backing_field >= @value
IQueryable<TEntity> WhereGreaterThanOrEqual<TEntity, TInner>(...)

// OrderByMaybe — ORDER BY backing_field ASC
IOrderedQueryable<TEntity> OrderByMaybe<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// OrderByMaybeDescending — ORDER BY backing_field DESC
IOrderedQueryable<TEntity> OrderByMaybeDescending<TEntity, TInner>(
    this IQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// ThenByMaybe — THEN BY backing_field ASC
IOrderedQueryable<TEntity> ThenByMaybe<TEntity, TInner>(
    this IOrderedQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// ThenByMaybeDescending — THEN BY backing_field DESC
IOrderedQueryable<TEntity> ThenByMaybeDescending<TEntity, TInner>(
    this IOrderedQueryable<TEntity> source,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// Usage — equality and null checks
var withoutPhone = await context.Customers.WhereNone(c => c.Phone).ToListAsync(ct);
var withPhone    = await context.Customers.WhereHasValue(c => c.Phone).ToListAsync(ct);
var matches      = await context.Customers.WhereEquals(c => c.Phone, phone).ToListAsync(ct);
var ordered      = await context.Customers.WhereHasValue(c => c.Phone).OrderByMaybe(c => c.Phone).ToListAsync(ct);

// Usage — comparison operators (for Maybe<DateTime>, Maybe<int>, etc.)
var cutoff = DateTime.UtcNow.AddDays(-7);
var overdue = await context.Orders
    .Where(o => o.Status == OrderStatus.Submitted)
    .WhereLessThan(o => o.SubmittedAt, cutoff)
    .ToListAsync(ct);
```

### AddTrellisInterceptors

Registers the `MaybeQueryInterceptor` and `ScalarValueQueryInterceptor` as singletons, enabling natural LINQ syntax with `Maybe<T>` properties and natural value object operations (comparisons, string methods, properties) without `.Value`.

```csharp
// Generic overload
DbContextOptionsBuilder<TContext> AddTrellisInterceptors<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)

// Non-generic overload
DbContextOptionsBuilder AddTrellisInterceptors(this DbContextOptionsBuilder optionsBuilder)

// Usage
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString)
           .AddTrellisInterceptors());
```

### ScalarValueQueryInterceptor

Automatically rewrites scalar value object expressions in LINQ so EF Core can translate them. Handles `.Value` property access, string methods (`StartsWith`, `Contains`, `EndsWith`), properties (`Length`), and comparisons — converting them to the provider type via the existing `implicit operator T(ScalarValueObject<TSelf, T>)`.

```csharp
// With interceptor registered, natural value object syntax works in LINQ:

// RequiredString — comparisons and string methods without .Value
context.Customers.Where(c => c.Name == "Alice")                       // → Name = 'Alice'
context.Customers.Where(c => c.Name.StartsWith("Al"))                 // → Name LIKE 'Al%'
context.Customers.Where(c => c.Name.Contains("lic"))                  // → Name LIKE '%lic%'
context.Customers.Where(c => c.Name.Length > 3)                       // → LEN(Name) > 3
context.Customers.OrderBy(c => c.Name)                                // → ORDER BY Name
context.Customers.OrderByDescending(c => c.Name)                      // → ORDER BY Name DESC

// All scalar value objects — comparisons without .Value
context.Orders.Where(o => o.DueDate < cutoffDate)                     // → DueDate < @cutoffDate

// Specifications with natural domain syntax:
public override Expression<Func<TodoItem, bool>> ToExpression() =>
    todo => todo.Status == TodoStatus.Active
         && todo.DueDate < _asOf;                                      // no .Value needed

// .Value still needed for:
// - Select projections to primitives: .Select(c => c.Name.Value)
// - Provider-type methods not exposed on the VO (e.g., string.Substring)
// See the EF Core integration guide for the full LINQ support matrix.
```

### TrellisPersistenceMappingException

Thrown by Trellis value converters when a persisted database value cannot be converted back to a value object (e.g., an invalid string in the database for an `EmailAddress` column). Provides diagnostic context for debugging data corruption.

```csharp
public sealed class TrellisPersistenceMappingException : InvalidOperationException
{
    public Type ValueObjectType { get; }     // e.g., typeof(EmailAddress)
    public object? PersistedValue { get; }   // the raw DB value that failed
    public string FactoryMethod { get; }     // e.g., "TryCreate"
    public string Detail { get; }            // validation failure detail
}
```

### Maybe\<T\> Query Interceptor

Automatically rewrites `Maybe<T>` property accesses in LINQ expression trees to EF Core-translatable storage member references. Enables natural LINQ syntax and `Specification<T>` patterns with `Maybe<T>` properties.

```csharp
// Registration — one call, singleton handled internally
optionsBuilder.UseSqlite(connectionString).AddTrellisInterceptors();

// With interceptor registered, these LINQ expressions work directly:
context.Customers.Where(c => c.Phone.HasValue)                                    // → IS NOT NULL
context.Customers.Where(c => c.Phone.HasNoValue)                                  // → IS NULL
context.Orders.Where(o => o.SubmittedAt.HasValue && o.SubmittedAt.Value < cutoff)  // → column IS NOT NULL AND column < @cutoff

// Specifications with Maybe<T> properties also work:
public override Expression<Func<Order, bool>> ToExpression() =>
    order => order.Status == OrderStatus.Submitted
          && order.SubmittedAt.HasValue
          && order.SubmittedAt.Value < _cutoff;
```

### Maybe\<T\> Index, Update, and Diagnostics Helpers

`HasTrellisIndex` resolves `Maybe<T>` properties to backing field names for type-safe index creation. `SetMaybeValue`/`SetMaybeNone` for bulk updates via `ExecuteUpdate`. `TRLS021` analyzer warns when `HasIndex` is used with `Maybe<T>` properties.

```csharp
// HasTrellisIndex — resolves Maybe<T> properties to mapped backing fields
IndexBuilder<TEntity> HasTrellisIndex<TEntity>(
    this EntityTypeBuilder<TEntity> entityTypeBuilder,
    Expression<Func<TEntity, object?>> propertySelector)

// Usage — single Maybe<T> property
builder.HasTrellisIndex(o => o.SubmittedAt);

// Usage — composite index mixing regular + Maybe<T> properties
builder.HasTrellisIndex(o => new { o.Status, o.SubmittedAt });
// Resolves to: HasIndex("Status", "_submittedAt") — type-safe, no string typos

// Notes
// - Accepts direct property access on the lambda parameter only
// - Rejects nested selectors such as e => e.Customer.Phone
// - Validates Maybe<T> backing fields exist on the CLR hierarchy or are already mapped
// - Supports inherited Maybe<T> backing fields declared on base entity types

// ExecuteUpdate helpers
UpdateSettersBuilder<TEntity> SetMaybeValue<TEntity, TInner>(
    this UpdateSettersBuilder<TEntity> updateSettersBuilder,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector,
    TInner value)

UpdateSettersBuilder<TEntity> SetMaybeNone<TEntity, TInner>(
    this UpdateSettersBuilder<TEntity> updateSettersBuilder,
    Expression<Func<TEntity, Maybe<TInner>>> propertySelector)

// Note: SetMaybeValue/SetMaybeNone throw InvalidOperationException for composite
// owned types like Money. Use tracked entity updates (load, modify, SaveChangesAsync) instead.

// Diagnostics
IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this IModel model)
IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this DbContext dbContext)
string ToMaybeMappingDebugString(this IModel model)
string ToMaybeMappingDebugString(this DbContext dbContext)
```

`MaybePropertyMapping` describes the entity type, CLR property name, generated backing field, nullable store type, column name, and resolved provider type for each discovered `Maybe<T>` mapping.

### Exception Classification

How `SaveChangesResultAsync` classifies EF Core exceptions: `DbUpdateConcurrencyException` → `ConflictError`, duplicate key → `ConflictError`, FK violation → `DomainError`.

```csharp
bool DbExceptionClassifier.IsDuplicateKey(DbUpdateException ex)       // SQL Server, PostgreSQL, SQLite
bool DbExceptionClassifier.IsForeignKeyViolation(DbUpdateException ex)
string? DbExceptionClassifier.ExtractConstraintDetail(DbUpdateException ex)
```

---

# 13. Trellis.Analyzers — Code Quality Diagnostics

**NuGet: `Trellis.Analyzers`**

Roslyn analyzers and code fixes for correct `Result<T>`, `Maybe<T>`, and ROP pipeline usage.

| ID | Severity | Title |
|----|----------|-------|
| `TRLS001` | Warning | Result return value is not handled |
| `TRLS002` | Info | Use Bind instead of Map when lambda returns Result |
| `TRLS003` | Warning | Unsafe access to `Result.Value` without checking `IsSuccess` |
| `TRLS004` | Warning | Unsafe access to `Result.Error` without checking `IsFailure` |
| `TRLS005` | Info | Consider using MatchError for error type discrimination |
| `TRLS006` | Warning | Unsafe access to `Maybe.Value` without checking `HasValue` |
| `TRLS007` | Warning | Use `Create()` instead of `TryCreate().Value` |
| `TRLS008` | Warning | Result is double-wrapped as `Result<Result<T>>` |
| `TRLS009` | Warning | Blocking on `Task<Result<T>>` — use `await` |
| `TRLS010` | Info | Use specific error type instead of base `Error` class |
| `TRLS011` | Warning | Maybe is double-wrapped as `Maybe<Maybe<T>>` |
| `TRLS012` | Info | Consider using `Result.Combine` for multiple Result checks |
| `TRLS013` | Info | Consider `GetValueOrDefault` or `Match` instead of ternary |
| `TRLS014` | Warning | Use async method variant (`MapAsync`, `BindAsync`, etc.) for async lambda |
| `TRLS015` | Warning | Don't throw exceptions in Result chains — return failure |
| `TRLS016` | Warning | Error message should not be empty |
| `TRLS017` | Warning | Don't compare `Result` or `Maybe` to null (they are structs) |
| `TRLS018` | Warning | Unsafe access to `.Value` in LINQ without filtering by success state |
| `TRLS019` | Error | Combine chain exceeds maximum supported tuple size (9) |
| `TRLS020` | Warning | Use `SaveChangesResultAsync` instead of `SaveChangesAsync` |
| `TRLS021` | Warning | `HasIndex` references a `Maybe<T>` property — prefer `HasTrellisIndex` or use the backing field name |

Source generator diagnostics use a separate `TRLSGEN` prefix (see §3 and §12).

---

# Known Issues & Workarounds

## Trellis.Unit vs Mediator.Unit

Projects referencing both `Trellis.Results` and `Mediator` will encounter ambiguous `Unit` references. Both libraries define a `Unit` type.

```csharp
// Preferred: Use parameterless Result.Success() — avoids referencing Unit entirely
return Result.Success();  // instead of Result.Success(Unit.Value)

// Alternative: Using alias (if you need to reference Unit directly)
using Unit = Trellis.Unit;
```

---

# Usage Patterns & Recipes

## Full Program.cs Setup

Complete example showing MVC + Mediator + Auth + EF Core registration:

```csharp
using TodoSample.AntiCorruptionLayer;
using TodoSample.Api;
using TodoSample.Api.Middleware;
using TodoSample.Application;
using Scalar.AspNetCore;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddPresentation(builder.Environment)   // MVC, versioning, OpenTelemetry, SLI, DevelopmentActorProvider
    .AddApplication()                        // Mediator + TrellisBehaviors
    .AddAntiCorruptionLayer(connectionString); // DbContext + repositories + ResourceAuthorization

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithDocumentPerVersion();
    app.MapScalarApiReference(...);
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.UseScalarValueValidation();             // Middleware for scalar value validation
app.UseMiddleware<ErrorHandlingMiddleware>();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
```

## Create a Custom Value Object (RequiredGuid ID)

Complete example showing how to define a strongly-typed GUID identifier using `RequiredGuid<TSelf>`.

```csharp
using Trellis;

public partial class OrderId : RequiredGuid<OrderId> { }

// Usage
var id = OrderId.NewUniqueV7();
var parsed = OrderId.TryCreate("550e8400-e29b-41d4-a716-446655440000");
```

## Create a Custom Value Object (RequiredString)

Complete example showing how to define a validated string value object with optional length constraints and custom validation.

```csharp
using Trellis;

public partial class FirstName : RequiredString<FirstName> { }

// Usage
var name = FirstName.Create("Alice");
var result = FirstName.TryCreate(userInput);
```

With length constraints:

```csharp
[StringLength(50)]
public partial class ProductName : RequiredString<ProductName> { }

[StringLength(500, MinimumLength = 10)]
public partial class Description : RequiredString<Description> { }
```

With custom validation (regex, format checks):

```csharp
[StringLength(10)]
public partial class Sku : RequiredString<Sku>
{
    static partial void ValidateAdditional(string value, string fieldName, ref string? errorMessage)
    {
        if (!Regex.IsMatch(value, @"^SKU-\d{6}$"))
            errorMessage = "Sku must match pattern SKU-XXXXXX.";
    }
}
```

## Create a Custom Value Object (RequiredEnum — Smart Enum)

Complete example showing how to define a smart enum with static members and case-insensitive parsing.

```csharp
using Trellis;

public partial class OrderStatus : RequiredEnum<OrderStatus>
{
    public static readonly OrderStatus Draft = new();
    public static readonly OrderStatus Pending = new();
    public static readonly OrderStatus Confirmed = new();
    public static readonly OrderStatus Shipped = new();
    public static readonly OrderStatus Delivered = new();
    public static readonly OrderStatus Cancelled = new();
}

// Usage
var status = OrderStatus.Draft;
var all = OrderStatus.GetAll();
var parsed = OrderStatus.TryFromName("Pending");
if (status.Is(OrderStatus.Draft, OrderStatus.Pending)) { /* ... */ }
```

## Create a Custom ScalarValueObject with Custom Validation

Complete example showing how to create a value object with fully custom validation logic by implementing `IScalarValue<TSelf, TPrimitive>` directly.

```csharp
using Trellis;

public class Temperature : ScalarValueObject<Temperature, decimal>,
    IScalarValue<Temperature, decimal>
{
    private Temperature(decimal value) : base(value) { }

    public static Result<Temperature> TryCreate(decimal value, string? fieldName = null) =>
        value.ToResult()
            .Ensure(v => v >= -273.15m, Error.Validation("Below absolute zero", fieldName ?? "temperature"))
            .Map(v => new Temperature(v));

    // Create is inherited automatically from ScalarValueObject
}
```

## Define an Aggregate

Complete example showing how to define a DDD aggregate with domain methods, invariant enforcement, and domain event publishing.

```csharp
using Trellis;

public class Order : Aggregate<OrderId>
{
    private readonly List<OrderLine> _lines = [];
    public CustomerId CustomerId { get; }
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
    public Money Total { get; private set; }

    private Order(CustomerId customerId) : base(OrderId.NewUniqueV7())
    {
        CustomerId = customerId;
        Total = Money.Create(0m, "USD");
        DomainEvents.Add(new OrderCreated(Id, customerId, DateTime.UtcNow));
    }

    public static Result<Order> TryCreate(CustomerId customerId) =>
        Result.Success(new Order(customerId));

    public Result<Order> AddLine(ProductId productId, string name, Money price, int quantity) =>
        this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft, Error.Conflict("Cannot modify non-draft order"))
            .Bind(_ => OrderLine.TryCreate(productId, name, price, quantity))
            .Tap(line => _lines.Add(line))
            .Bind(_ => RecalculateTotal())
            .Map(_ => this);

    public Result<Order> Submit() =>
        this.ToResult()
            .Ensure(_ => Status == OrderStatus.Draft, Error.Conflict($"Cannot submit order in {Status} status"))
            .Ensure(_ => _lines.Count > 0, Error.Domain("Cannot submit empty order"))
            .Tap(_ =>
            {
                Status = OrderStatus.Pending;
                DomainEvents.Add(new OrderSubmitted(Id, DateTime.UtcNow));
            })
            .Map(_ => this);

    private Result<Unit> RecalculateTotal() =>
        _lines.Select(l => l.LineTotal)
            .Aggregate(Money.Zero("USD"), (acc, next) => acc.Bind(a => a.Add(next)))
            .Tap(total => Total = total)
            .Map(_ => Unit.Value);
}
```

## Build an ROP Pipeline

Complete example showing how to compose validation, transformation, and side effects using the Railway Oriented Programming pipeline.

```csharp
// Validation + transformation
var result = EmailAddress.TryCreate(dto.Email)
    .Combine(FirstName.TryCreate(dto.FirstName))
    .Combine(LastName.TryCreate(dto.LastName))
    .Bind((email, first, last) => CreateUser(email, first, last));

// Async pipeline with side effects
var result = await OrderId.TryCreate(request.OrderId)
    .BindAsync(id => _repository.GetByIdAsync(id, ct))
    .EnsureAsync(order => order.Status == OrderStatus.Draft, Error.Conflict("Order already submitted"))
    .BindAsync(order => order.Submit())
    .CheckAsync(order => _repository.SaveAsync(order, ct))
    .TapAsync(order => _eventBus.PublishAsync(order.UncommittedEvents(), ct));

// Recovery
var result = await ProcessPayment(order, paymentInfo)
    .RecoverOnFailureAsync(
        predicate: err => err is ServiceUnavailableError,
        funcAsync: () => RetryPaymentAsync(order, paymentInfo));
```

## Use Maybe\<T\> for Optional Fields

Complete example showing how to model optional fields with `Maybe<T>` in requests, validation, and EF Core persistence.

```csharp
public record CreateProfileRequest(
    string Email,
    string FirstName,
    string? MiddleName,    // optional
    string LastName,
    Url? Website           // optional value object
);

// Validation with Optional
var result = EmailAddress.TryCreate(dto.Email)
    .Combine(Maybe.Optional(dto.MiddleName, MiddleName.TryCreate))
    .Bind((email, middleName) => CreateProfile(email, middleName));

// EF Core persistence — use partial Maybe<T> property (see §12 Maybe<T> Property Mapping)
public partial class Profile
{
    public partial Maybe<Url> Website { get; set; }
}
// MaybeConvention auto-configures the backing field — no OnModelCreating needed
```

## Convert Result to HTTP Response

Complete example showing how to map `Result<T>` to HTTP responses in both MVC controllers and Minimal API endpoints.

```csharp
// MVC Controller
[HttpGet("{id}")]
public async Task<ActionResult<OrderDto>> GetOrder(OrderId id)
{
    return await _service.GetOrderAsync(id)
        .ToActionResultAsync(this, OrderDto.From);
}

[HttpPost]
public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderRequest request)
{
    return await _service.CreateOrderAsync(request)
        .ToCreatedAtActionResultAsync(this, nameof(GetOrder), dto => new { id = dto.Id }, OrderDto.From);
}

// Minimal API
app.MapGet("/orders/{id}", async (OrderId id, IOrderService service) =>
    await service.GetOrderAsync(id)
        .MapAsync(order => order.ToDto())
        .ToHttpResultAsync());
```

## HTTP Client → Result Pipeline

Complete example showing how to chain HTTP status handling and JSON deserialization into a `Result<T>` pipeline.

```csharp
var result = await _httpClient.GetAsync($"/api/orders/{id}", ct)
    .HandleNotFoundAsync(Error.NotFound($"Order {id} not found"))
    .HandleUnauthorizedAsync(Error.Unauthorized("Authentication required"))
    .EnsureSuccessAsync()
    .ReadResultFromJsonAsync(JsonContext.Default.OrderDto, ct);
```

## EF Core Integration

Complete example showing how to configure `DbContext` with Trellis conventions and implement a repository using Result-returning queries.

```csharp
// DbContext configuration
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.ApplyTrellisConventions(typeof(Order).Assembly);
}

// Repository
public async Task<Result<Order>> GetByIdAsync(OrderId id, CancellationToken cancellationToken) =>
    await _dbContext.Orders
        .FirstOrDefaultResultAsync(o => o.Id == id, Error.NotFound($"Order {id} not found"), ct);

public async Task<Maybe<Order>> FindByIdAsync(OrderId id, CancellationToken cancellationToken) =>
    await _dbContext.Orders.FirstOrDefaultMaybeAsync(o => o.Id == id, ct);

public async Task<Result<Unit>> SaveAsync(Order order, CancellationToken cancellationToken)
{
    _dbContext.Orders.Update(order);
    return await _dbContext.SaveChangesResultUnitAsync(ct);
}

// Specification queries
var highValueOrders = await _dbContext.Orders
    .Where(new HighValueOrderSpec(1000m).And(new OrderStatusSpec(OrderStatus.Confirmed)))
    .ToListAsync(ct);
```

#### Value Object LINQ Comparisons

In LINQ queries, compare value objects to value objects — the value converter registered by `ApplyTrellisConventions` handles SQL translation automatically.

```csharp
// ✅ Correct — value object to value object
var customer = await _dbContext.Customers
    .FirstOrDefaultResultAsync(c => c.Email == EmailAddress.Create("alice@example.com"), notFoundError, ct);

// ✅ Also correct (with ScalarValueQueryInterceptor registered via AddTrellisInterceptors)
// The interceptor rewrites .Value access to the primitive for SQL translation
var customer = await _dbContext.Customers
    .FirstOrDefaultResultAsync(c => c.Email.Value == "alice@example.com", notFoundError, ct);
```

## CQRS Command with Authorization

Complete example showing how to define a CQRS command with permission-based authorization, self-validation, and a handler using the ROP pipeline.

```csharp
using Mediator;
using Trellis;
using Trellis.Authorization;
using Trellis.Mediator;

public sealed record CreateOrderCommand(CustomerId CustomerId, List<OrderLineDto> Items)
    : ICommand<Result<Order>>, IAuthorize, IValidate
{
    // Permission-based authorization
    public IReadOnlyList<string> RequiredPermissions => ["Orders.Create"];

    // Self-validation
    public IResult Validate() =>
        Result.Ensure(Items.Count > 0, Error.Validation("At least one item required", "items"));
}

public sealed class CreateOrderHandler(IOrderRepository repo)
    : ICommandHandler<CreateOrderCommand, Result<Order>>
{
    public async ValueTask<Result<Order>> Handle(CreateOrderCommand command, CancellationToken cancellationToken) =>
        await Order.TryCreate(command.CustomerId)
            .BindAsync(order => AddItemsAsync(order, command.Items, ct))
            .BindAsync(order => order.Submit())
            .CheckAsync(order => repo.SaveAsync(order, ct));
}
```

