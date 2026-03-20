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

```csharp
implicit operator Result<TValue>(TValue value)   // auto-wrap success
implicit operator Result<TValue>(Error error)     // auto-wrap failure
```

### Static Factories (on `Result`)

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
bool TryGetValue(out T value)
Maybe<TResult> Map<TResult>(Func<T, TResult> selector) where TResult : notnull
TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none)
implicit operator Maybe<T>(T value)
```

### Maybe Static Members

```csharp
// On Maybe<T> struct:
static Maybe<T> None { get; }           // e.g., Maybe<PhoneNumber>.None

// On Maybe static helper class:
Maybe<T> From<T>(T? value) where T : notnull
Result<Maybe<TOut>> Optional<TIn, TOut>(TIn? value, Func<TIn, Result<TOut>> function) where TIn : class, TOut : notnull
Result<Maybe<TOut>> Optional<TIn, TOut>(TIn? value, Func<TIn, Result<TOut>> function) where TIn : struct, TOut : notnull
```

### Maybe Extension Methods

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

---

## Error Hierarchy

### Error (base class)

```csharp
string Code { get; }
string Detail { get; }
string? Instance { get; }
```

**Equality:** `Equals` and `GetHashCode` are `virtual`. Base `Error` compares `GetType()`, `Code`, `Detail`, and `Instance` (DDD Value Object semantics). `ValidationError` additionally compares `FieldErrors`. `AggregateError` additionally compares `Errors`. Override in custom error types to include additional properties.

### Factory Methods

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

| Type | Default Code |
|------|-------------|
| `ValidationError` | `"validation.error"` |
| `BadRequestError` | `"bad.request"` |
| `ConflictError` | `"conflict.error"` |
| `NotFoundError` | `"not.found"` |
| `UnauthorizedError` | `"unauthorized.access"` |
| `ForbiddenError` | `"forbidden.access"` |
| `UnexpectedError` | `"unexpected.error"` |
| `DomainError` | `"domain.error"` |
| `RateLimitError` | `"rate.limit"` |
| `ServiceUnavailableError` | `"service.unavailable"` |

### ValidationError (extends Error)

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

```csharp
Result<T> When<T>(this Result<T>, Func<T, bool> predicate, Func<T, Result<T>> action)
Result<T> When<T>(this Result<T>, bool condition, Func<T, Result<T>> action)
Result<T> Unless<T>(this Result<T>, Func<T, bool> predicate, Func<T, Result<T>> action)
Result<T> Unless<T>(this Result<T>, bool condition, Func<T, Result<T>> action)
// + async variants, including Task<Result<T>> and ValueTask<Result<T>> boolean-condition overloads
```

### Traverse — Apply to Collection

```csharp
Result<IReadOnlyList<TOut>> Traverse<TIn, TOut>(this IEnumerable<TIn>, Func<TIn, Result<TOut>>)
Task<Result<IReadOnlyList<TOut>>> TraverseAsync<TIn, TOut>(this IEnumerable<TIn>, Func<TIn, Task<Result<TOut>>>)
// + CancellationToken overloads, ValueTask variants
// Returns IReadOnlyList<TOut> (not IEnumerable<TOut>) — materializes eagerly
```

### Nullable → Result

```csharp
Result<T> ToResult<T>(this T? value, Error error) where T : struct
Result<T> ToResult<T>(this T? value, Error error) where T : class
// + Task/ValueTask async variants
```

### ToResult — Wrap as Success

```csharp
Result<T> ToResult<T>(this T value)  // wraps value as Success
```

### LINQ Support (Result)

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

```csharp
Result<T> Debug<T>(this Result<T>, string? label = null)
Result<T> DebugDetailed<T>(this Result<T>, string? label = null)
Result<T> DebugWithStack<T>(this Result<T>, string? label = null, bool includeStack = true)
Result<T> DebugOnSuccess<T>(this Result<T>, Action<T>)
Result<T> DebugOnFailure<T>(this Result<T>, Action<Error>)
// + async variants
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
    public static ActivitySource ActivitySource { get; }   // "Functional DDD PVO"
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

## Aggregate\<TId\> (abstract class, extends Entity\<TId\>, implements IAggregate)

```csharp
protected Aggregate(TId id)
protected List<IDomainEvent> DomainEvents { get; }
bool IsChanged { get; }                    // true if DomainEvents.Count > 0
IReadOnlyList<IDomainEvent> UncommittedEvents()
void AcceptChanges()                       // clears DomainEvents
```

## IDomainEvent (interface)

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

```csharp
static abstract Result<TSelf> TryCreate(TPrimitive value, string? fieldName = null)
static virtual TSelf Create(TPrimitive value)  // default: TryCreate + throw
TPrimitive Value { get; }
```

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

### RequiredInt\<TSelf\>

Inherits `ScalarValueObject<TSelf, int>`. Source generator provides:

```csharp
static Result<Foo> TryCreate(int value, string? fieldName = null)       // rejects zero
static Result<Foo> TryCreate(int? value, string? fieldName = null)
static Result<Foo> TryCreate(string? value, string? fieldName = null)
static new Foo Create(int value)
static Foo Create(string stringValue)
// IParsable<Foo>, explicit operator, JsonConverter
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
| `Money` | multi-value | Amount ≥ 0, valid currency code | See below |

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

```csharp
interface IActorProvider { Actor GetCurrentActor(); }
interface IAsyncActorProvider { Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default); }
interface IAuthorize { IReadOnlyList<string> RequiredPermissions { get; } }
interface IAuthorizeResource<TResource> { IResult Authorize(Actor actor, TResource resource); }
interface IResourceLoader<TMessage, TResource> { Task<Result<TResource>> LoadAsync(TMessage message, CancellationToken ct); }
abstract class ResourceLoaderById<TMessage, TResource, TId> : IResourceLoader<TMessage, TResource>
{
    protected abstract TId GetId(TMessage message);
    protected abstract Task<Result<TResource>> GetByIdAsync(TId id, CancellationToken ct);
}
```

### IAsyncActorProvider

Asynchronous variant of `IActorProvider`. Use when permission resolution requires async operations such as database lookups or external service calls.

```csharp
public interface IAsyncActorProvider
{
    Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default);
}
```

Use `IActorProvider` when the actor can be resolved synchronously (e.g., from in-memory claims). Use `IAsyncActorProvider` when resolution requires I/O (e.g., loading permissions from a database).

## ActorAttributes Constants

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
| `DomainError` | 422 |
| `RateLimitError` | 429 |
| `UnexpectedError` | 500 |
| `ServiceUnavailableError` | 503 |

Customizable via `TrellisAspOptions.MapError<TError>(int statusCode)`.

## MVC Controller Extensions

```csharp
ActionResult<T> ToActionResult<T>(this Result<T> result, ControllerBase controller)
ActionResult<T> ToCreatedAtActionResult<T>(this Result<T> result, ControllerBase controller,
    string actionName, Func<T, object?> routeValues, string? controllerName = null)

// Transform overloads — map domain type to DTO inline
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

# 6. Trellis.Asp.Authorization — Entra ID Actor Provider

**Namespace: `Trellis.Asp.Authorization`**

```csharp
// Registration
services.AddEntraActorProvider();
services.AddEntraActorProvider(options => {
    options.IdClaimType = "sub";
    options.MapPermissions = claims => /* custom extraction */;
});

// EntraActorProvider : IActorProvider
// Extracts Actor from HttpContext claims (Entra ID / Azure AD)
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
    Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callback, TContext context, CancellationToken ct)
Task<Result<HttpResponseMessage>> HandleFailureAsync<TContext>(this Task<HttpResponseMessage>,
    Func<HttpResponseMessage, TContext, CancellationToken, Task<Error>> callback, TContext context, CancellationToken ct)

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

### Pipeline Order

Exception → Tracing → Logging → Authorization → ResourceAuthorization (actor-only) → Validation

Resource-based authorization with a loaded resource (`IAuthorizeResource<TResource>`) is auto-discovered via `AddResourceAuthorization(Assembly)`, or registered explicitly per-command via `AddResourceAuthorization<TMessage, TResource, TResponse>()` for AOT scenarios.

### Behaviors

| Behavior | Constraint on TMessage | Purpose |
|----------|----------------------|---------|
| `ExceptionBehavior` | `IMessage` | Catches unhandled exceptions → `Error.Unexpected` |
| `TracingBehavior` | `IMessage` | OpenTelemetry Activity span (`ActivitySourceName = "Trellis.Mediator"`) |
| `LoggingBehavior` | `IMessage` | Structured logging with duration |
| `AuthorizationBehavior` | `IAuthorize, IMessage` | Checks `HasAllPermissions` → `Error.Forbidden` |
| `ResourceAuthorizationBehavior<,,>` | `IAuthorizeResource<TResource>, IMessage` | Loads resource via `IResourceLoader`, delegates to `message.Authorize(actor, resource)`. Auto-discovered via `AddResourceAuthorization(Assembly)`. |
| `ValidationBehavior` | `IValidate, IMessage` | Calls `message.Validate()`, short-circuits |

### IValidate Interface

```csharp
interface IValidate { IResult Validate(); }
```

### TracingBehavior Constants

```csharp
public const string ActivitySourceName = "Trellis.Mediator";
```

Use `ActivitySourceName` to register the activity source with OpenTelemetry: `builder.AddSource(TracingBehavior<IMessage, IResult>.ActivitySourceName)`.

### Registration

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
Task<Result<T>> ValidateToResultAsync<T>(this IValidator<T> validator, T value, CancellationToken ct = default)
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

```csharp
Task<Result<int>> SaveChangesResultAsync(this DbContext context, CancellationToken ct = default)
Task<Result<int>> SaveChangesResultAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken ct = default)
Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, CancellationToken ct = default)
Task<Result<Unit>> SaveChangesResultUnitAsync(this DbContext context, bool acceptAllChangesOnSuccess, CancellationToken ct = default)
// DbUpdateConcurrencyException → Error.Conflict
// Duplicate key → Error.Conflict
// FK violation → Error.Domain
```

### Queryable Extensions

```csharp
Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken ct = default)
Task<Maybe<T>> FirstOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken ct = default)
Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, CancellationToken ct = default)
Task<Maybe<T>> SingleOrDefaultMaybeAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, CancellationToken ct = default)
Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Error notFoundError, CancellationToken ct = default)
Task<Result<T>> FirstOrDefaultResultAsync<T>(this IQueryable<T> query, Expression<Func<T, bool>> predicate, Error notFoundError, CancellationToken ct = default)
IQueryable<T> Where<T>(this IQueryable<T> query, Specification<T> specification)
```

### Value Converter Registration

```csharp
// In OnModelCreating or ConfigureConventions
configurationBuilder.ApplyTrellisConventions(typeof(Order).Assembly);
// Auto-registers converters for all IScalarValue and RequiredEnum types
// Auto-maps Money properties as owned types (Amount + Currency columns)
```

### Money Property Convention

`Money` properties on entities are automatically mapped as owned types — no `OwnsOne` configuration needed. This includes `Money` properties declared on owned entity types (e.g., items inside `OwnsMany` collections). Column naming convention:

| Property Name | Amount Column | Currency Column | Amount Type | Currency Type |
|---------------|---------------|-----------------|-------------|---------------|
| `Price` | `Price` | `PriceCurrency` | `decimal(18,3)` | `nvarchar(3)` |
| `ShippingCost` | `ShippingCost` | `ShippingCostCurrency` | `decimal(18,3)` | `nvarchar(3)` |

Explicit `OwnsOne` configuration takes precedence over the convention.

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

The source generator emits a private `_camelCase` backing field and getter/setter for each `partial Maybe<T>` property. The `MaybeConvention` (registered by `ApplyTrellisConventions`) auto-discovers `Maybe<T>` properties, ignores the struct property, maps the backing field as nullable, and sets the column name to the property name.

Backing field naming: `Phone` → `_phone`, `SubmittedAt` → `_submittedAt`, `AlternateEmail` → `_alternateEmail`.

If a `Maybe<T>` property is not declared `partial`, the generator emits diagnostic `TRLSGEN100`.

**Troubleshooting:** If the generator produces no output despite correct `partial` declarations, run a clean build (`dotnet clean` followed by `dotnet build`). Stale incremental build artifacts can prevent the generator from executing.

### Maybe\<T\> Queryable Extensions

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

### Maybe\<T\> Query Interceptor

Automatically rewrites `Maybe<T>` property accesses in LINQ expression trees to EF Core-translatable storage member references. Enables natural LINQ syntax and `Specification<T>` patterns with `Maybe<T>` properties.

```csharp
// Registration — add to DbContext options
optionsBuilder.AddInterceptors(new MaybeQueryInterceptor());

// With interceptor registered, these LINQ expressions work directly:
context.Customers.Where(c => c.Phone.HasValue)                                    // → IS NOT NULL
context.Customers.Where(c => c.Phone.HasNoValue)                                  // → IS NULL
context.Orders.Where(o => o.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) < cutoff)  // → COALESCE < cutoff

// Specifications with Maybe<T> properties also work:
public override Expression<Func<Order, bool>> ToExpression() =>
    order => order.Status == OrderStatus.Submitted
          && order.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) < _cutoff;
```

### Maybe\<T\> Index, Update, and Diagnostics Helpers

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

// Diagnostics
IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this IModel model)
IReadOnlyList<MaybePropertyMapping> GetMaybePropertyMappings(this DbContext dbContext)
string ToMaybeMappingDebugString(this IModel model)
string ToMaybeMappingDebugString(this DbContext dbContext)
```

`MaybePropertyMapping` describes the entity type, CLR property name, generated backing field, nullable store type, column name, and resolved provider type for each discovered `Maybe<T>` mapping.

### Exception Classification

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

## Create a Custom Value Object (RequiredGuid ID)

```csharp
using Trellis;

public partial class OrderId : RequiredGuid<OrderId> { }

// Usage
var id = OrderId.NewUniqueV7();
var parsed = OrderId.TryCreate("550e8400-e29b-41d4-a716-446655440000");
```

## Create a Custom Value Object (RequiredString)

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

## Create a Custom Value Object (RequiredEnum — Smart Enum)

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
    .TapAsync(order => _repository.SaveAsync(order, ct))
    .TapAsync(order => _eventBus.PublishAsync(order.UncommittedEvents(), ct));

// Recovery
var result = await ProcessPayment(order, paymentInfo)
    .RecoverOnFailureAsync(
        predicate: err => err is ServiceUnavailableError,
        funcAsync: () => RetryPaymentAsync(order, paymentInfo));
```

## Use Maybe\<T\> for Optional Fields

```csharp
public record CreateProfileRequest(
    string Email,
    string FirstName,
    Maybe<string> MiddleName,    // optional
    string LastName,
    Maybe<Url> Website           // optional value object
);

// Validation with Optional
var result = EmailAddress.TryCreate(dto.Email)
    .Combine(Maybe.Optional(dto.MiddleName.AsNullable(), MiddleName.TryCreate))
    .Bind((email, middleName) => CreateProfile(email, middleName));

// EF Core persistence — use partial Maybe<T> property (see §12 Maybe<T> Property Mapping)
public partial class Profile
{
    public partial Maybe<Url> Website { get; set; }
}
// MaybeConvention auto-configures the backing field — no OnModelCreating needed
```

## Convert Result to HTTP Response

```csharp
// MVC Controller
[HttpGet("{id}")]
public async Task<ActionResult<OrderDto>> GetOrder(string id)
{
    return await OrderId.TryCreate(id)
        .BindAsync(orderId => _service.GetOrderAsync(orderId))
        .MapAsync(order => order.ToDto())
        .ToActionResultAsync(this);
}

[HttpPost]
public async Task<ActionResult<OrderDto>> CreateOrder(CreateOrderRequest request)
{
    return await _service.CreateOrderAsync(request)
        .MapAsync(order => order.ToDto())
        .ToCreatedAtActionResultAsync(this, nameof(GetOrder), dto => new { id = dto.Id });
}

// Minimal API
app.MapGet("/orders/{id}", async (string id, IOrderService service) =>
    await OrderId.TryCreate(id)
        .BindAsync(orderId => service.GetOrderAsync(orderId))
        .MapAsync(order => order.ToDto())
        .ToHttpResultAsync());
```

## HTTP Client → Result Pipeline

```csharp
var result = await _httpClient.GetAsync($"/api/orders/{id}", ct)
    .HandleNotFoundAsync(Error.NotFound($"Order {id} not found"))
    .HandleUnauthorizedAsync(Error.Unauthorized("Authentication required"))
    .EnsureSuccessAsync()
    .ReadResultFromJsonAsync(JsonContext.Default.OrderDto, ct);
```

## EF Core Integration

```csharp
// DbContext configuration
protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    configurationBuilder.ApplyTrellisConventions(typeof(Order).Assembly);
}

// Repository
public async Task<Result<Order>> GetByIdAsync(OrderId id, CancellationToken ct) =>
    await _dbContext.Orders
        .FirstOrDefaultResultAsync(o => o.Id == id, Error.NotFound($"Order {id} not found"), ct);

public async Task<Result<Maybe<Order>>> FindByIdAsync(OrderId id, CancellationToken ct) =>
    Result.Success(await _dbContext.Orders.FirstOrDefaultMaybeAsync(o => o.Id == id, ct));

public async Task<Result<Unit>> SaveAsync(Order order, CancellationToken ct)
{
    _dbContext.Orders.Update(order);
    return await _dbContext.SaveChangesResultUnitAsync(ct);
}

// Specification queries
var highValueOrders = await _dbContext.Orders
    .Where(new HighValueOrderSpec(1000m).And(new OrderStatusSpec(OrderStatus.Confirmed)))
    .ToListAsync(ct);
```

## CQRS Command with Authorization

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
    public async ValueTask<Result<Order>> Handle(CreateOrderCommand command, CancellationToken ct) =>
        await Order.TryCreate(command.CustomerId)
            .BindAsync(order => AddItemsAsync(order, command.Items, ct))
            .BindAsync(order => order.Submit())
            .TapAsync(order => repo.SaveAsync(order, ct));
}
```
