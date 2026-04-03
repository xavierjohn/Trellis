# Trellis.Results — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-ddd.md, trellis-api-asp.md, trellis-api-primitives.md.

**Package:** `Trellis.Results` | **Namespace:** `Trellis`

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
RateLimitError Error.RateLimit(string detail, RetryAfterValue retryAfter, string? instance = null)
ServiceUnavailableError Error.ServiceUnavailable(string detail, string? instance = null)
ServiceUnavailableError Error.ServiceUnavailable(string detail, RetryAfterValue retryAfter, string? instance = null)
GoneError Error.Gone(string detail, string? instance = null)
MethodNotAllowedError Error.MethodNotAllowed(string detail, IReadOnlyList<string> allowedMethods, string? instance = null)
NotAcceptableError Error.NotAcceptable(string detail, string? instance = null)
UnsupportedMediaTypeError Error.UnsupportedMediaType(string detail, string? instance = null)
ContentTooLargeError Error.ContentTooLarge(string detail, RetryAfterValue? retryAfter = null, string? instance = null)
RangeNotSatisfiableError Error.RangeNotSatisfiable(string detail, long completeLength, string? instance = null)

// Custom code factories (same types with additional code parameter)
BadRequestError Error.BadRequest(string detail, string code, string? instance = null)
// ... same pattern for all non-Validation types
```

### Concrete Error Types

Each error type maps to a specific HTTP status code. `ValidationError` → 400, `NotFoundError` → 404, `UnauthorizedError` → 401, `ForbiddenError` → 403, `MethodNotAllowedError` → 405, `NotAcceptableError` → 406, `ConflictError` → 409, `GoneError` → 410, `PreconditionFailedError` → 412, `ContentTooLargeError` → 413, `UnsupportedMediaTypeError` → 415, `RangeNotSatisfiableError` → 416, `DomainError` → 422, `PreconditionRequiredError` → 428, `RateLimitError` → 429, `UnexpectedError` → 500, `ServiceUnavailableError` → 503.

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
| `GoneError` | `"gone.error"` |
| `MethodNotAllowedError` | `"method.not.allowed"` |
| `NotAcceptableError` | `"not.acceptable"` |
| `UnsupportedMediaTypeError` | `"unsupported.media.type"` |
| `ContentTooLargeError` | `"content.too.large"` |
| `RangeNotSatisfiableError` | `"range.not.satisfiable"` |

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
- **Task Left-only**: `MethodAsync(this Task<Result<T>>, sync_func)` → `Task<Result<TOut>>` — sync function on async input (mix sync+async in chains)
- **Task Right-only**: `MethodAsync(this Result<T>, async_func)` → `Task<Result<TOut>>`
- **Task Both**: `MethodAsync(this Task<Result<T>>, async_func)` → `Task<Result<TOut>>`
- **ValueTask**: Same three patterns with `ValueTask<Result<T>>`

The "Task Left-only" variant is key for mixed chains — it lets you call sync domain methods (e.g., `order.Confirm()`) in an async pipeline without `Task.FromResult()` wrappers.

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

**Mixing sync and async in chains:** The "Task Left-only" overload (line 2 above) accepts a sync `Func<TIn, Result<TOut>>` on `Task<Result<TIn>>`, so sync and async operations chain naturally — no `Task.FromResult()` wrapper needed:

```csharp
var result = await GetOrderAsync(orderId)      // Task<Result<Order>>
    .BindAsync(order => order.Confirm())       // sync — uses Task Left-only overload
    .BindAsync(order => ChargeAsync(order));   // async — uses Task Both overload
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

## EntityTagValue (sealed record)

Represents an RFC 9110 §8.8.1 entity tag (ETag) with explicit weak/strong semantics. Validates opaque tags against the `etagc` production — rejects `"`, control characters, and DEL.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `OpaqueTag` | `string` | Raw opaque tag string without quotes or `W/` prefix |
| `IsWeak` | `bool` | `true` if weak entity tag; `false` if strong |
| `IsWildcard` | `bool` | `true` if this is the RFC 9110 wildcard `*` token (semantically distinct from `Strong("*")`) |

### Factory Methods

```csharp
static EntityTagValue Strong(string opaqueTag)
static EntityTagValue Weak(string opaqueTag)
static EntityTagValue Wildcard()
static Result<EntityTagValue> TryParse(string? headerValue)
// Parses "tag" (strong), W/"tag" (weak). Returns BadRequestError on invalid format.
```

### Comparison (RFC 9110 §8.8.3.2)

```csharp
bool StrongEquals(EntityTagValue other)   // both must be strong + opaque tags match
bool WeakEquals(EntityTagValue other)     // opaque tags match regardless of strength
```

### Formatting

```csharp
string ToHeaderValue()  // * for wildcard, "tag" for strong, W/"tag" for weak
string ToString()       // delegates to ToHeaderValue()
```

### Example

```csharp
var strong = EntityTagValue.Strong("abc123");
var weak = EntityTagValue.Weak("abc123");
var parsed = EntityTagValue.TryParse("W/\"abc123\"");

strong.StrongEquals(EntityTagValue.Strong("abc123")); // true
strong.WeakEquals(weak); // true — opaque tags match
```

---

## RetryAfterValue (sealed class)

Represents an RFC 9110 §10.2.3 `Retry-After` value — either a delay in seconds or an absolute HTTP-date. Implements `IEquatable<RetryAfterValue>`.

### Factory Methods

```csharp
static RetryAfterValue FromSeconds(int seconds)      // seconds must be non-negative
static RetryAfterValue FromDate(DateTimeOffset date)
```

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `IsDelaySeconds` | `bool` | `true` when value is a delay in seconds |
| `IsDate` | `bool` | `true` when value is an absolute date |
| `DelaySeconds` | `int` | Delay seconds (throws `InvalidOperationException` if `IsDate`) |
| `Date` | `DateTimeOffset` | Absolute date (throws `InvalidOperationException` if `IsDelaySeconds`) |

### Formatting

```csharp
string ToHeaderValue()  // decimal seconds or IMF-fixdate format
string ToString()       // delegates to ToHeaderValue()
```

### Example

```csharp
var delay = RetryAfterValue.FromSeconds(60);
var date = RetryAfterValue.FromDate(DateTimeOffset.UtcNow.AddMinutes(5));
Error.RateLimit("Too many requests.", retryAfter: delay);
```

---

## RepresentationMetadata (sealed class)

Carries HTTP representation metadata (RFC 9110 §8) through Trellis response mappers. Used to emit `ETag`, `Last-Modified`, `Vary`, `Content-Language`, `Content-Location`, and `Accept-Ranges` response headers consistently across MVC and Minimal API responses (200, 201, 206, 304).

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `ETag` | `EntityTagValue?` | Entity tag validator |
| `LastModified` | `DateTimeOffset?` | Last modification date |
| `Vary` | `IReadOnlyList<string>?` | Vary header field names |
| `ContentLanguage` | `IReadOnlyList<string>?` | Content-Language values |
| `ContentLocation` | `string?` | Content-Location URI |
| `AcceptRanges` | `string?` | Accept-Ranges value (e.g., `"bytes"` or `"none"`) |

### Convenience Factories

```csharp
static RepresentationMetadata WithETag(EntityTagValue eTag)
static RepresentationMetadata WithStrongETag(string opaqueTag)
```

### Builder

```csharp
static Builder Create()

// Builder methods (all return Builder for chaining):
Builder SetETag(EntityTagValue eTag)
Builder SetStrongETag(string opaqueTag)
Builder SetWeakETag(string opaqueTag)
Builder SetLastModified(DateTimeOffset lastModified)
Builder AddVary(params string[] fieldNames)               // deduplicates case-insensitively
Builder AddContentLanguage(params string[] languages)     // deduplicates case-insensitively
Builder SetContentLocation(string uri)
Builder SetAcceptRanges(string value)
RepresentationMetadata Build()
```

### Example

```csharp
var meta = RepresentationMetadata.Create()
    .SetStrongETag(order.ETag)
    .SetLastModified(order.LastModified)
    .AddVary("Accept", "Accept-Language")
    .Build();
```

---

## New Error Types (RFC 9110)

### GoneError (410)

Resource is permanently gone.

```csharp
public sealed class GoneError : Error
// Factory:
static GoneError Error.Gone(string detail, string? instance = null)
static GoneError Error.Gone(string detail, string code, string? instance)
```

### MethodNotAllowedError (405)

Request method not supported. Emits `Allow` response header automatically.

```csharp
public sealed class MethodNotAllowedError : Error
IReadOnlyList<string> AllowedMethods { get; }
// Factory:
static MethodNotAllowedError Error.MethodNotAllowed(string detail, IReadOnlyList<string> allowedMethods, string? instance = null)
static MethodNotAllowedError Error.MethodNotAllowed(string detail, IReadOnlyList<string> allowedMethods, string code, string? instance)
```

### NotAcceptableError (406)

No acceptable representation available.

```csharp
public sealed class NotAcceptableError : Error
// Factory:
static NotAcceptableError Error.NotAcceptable(string detail, string? instance = null)
static NotAcceptableError Error.NotAcceptable(string detail, string code, string? instance)
```

### UnsupportedMediaTypeError (415)

Request payload in unsupported format.

```csharp
public sealed class UnsupportedMediaTypeError : Error
// Factory:
static UnsupportedMediaTypeError Error.UnsupportedMediaType(string detail, string? instance = null)
static UnsupportedMediaTypeError Error.UnsupportedMediaType(string detail, string code, string? instance)
```

### ContentTooLargeError (413)

Request payload exceeds limit. Optional `RetryAfter` emits `Retry-After` header automatically.

```csharp
public sealed class ContentTooLargeError : Error
RetryAfterValue? RetryAfter { get; }
// Factory:
static ContentTooLargeError Error.ContentTooLarge(string detail, RetryAfterValue? retryAfter = null, string? instance = null)
static ContentTooLargeError Error.ContentTooLarge(string detail, string code, RetryAfterValue? retryAfter = null, string? instance = null)
```

### RangeNotSatisfiableError (416)

Requested range cannot be satisfied. Emits `Content-Range: {unit} */{completeLength}` header automatically.

```csharp
public sealed class RangeNotSatisfiableError : Error
long CompleteLength { get; }
string Unit { get; }
// Factory:
static RangeNotSatisfiableError Error.RangeNotSatisfiable(string detail, long completeLength, string? instance = null)
static RangeNotSatisfiableError Error.RangeNotSatisfiable(string detail, long completeLength, string code, string unit = "bytes", string? instance = null)
```

### Updated: RateLimitError (429)

Now supports optional `RetryAfter`. When present, emits `Retry-After` header automatically.

```csharp
public sealed class RateLimitError : Error
RetryAfterValue? RetryAfter { get; }
// Factory (new overload):
static RateLimitError Error.RateLimit(string detail, RetryAfterValue retryAfter, string? instance = null)
```

### Updated: ServiceUnavailableError (503)

Now supports optional `RetryAfter`. When present, emits `Retry-After` header automatically.

```csharp
public sealed class ServiceUnavailableError : Error
RetryAfterValue? RetryAfter { get; }
// Factory (new overload):
static ServiceUnavailableError Error.ServiceUnavailable(string detail, RetryAfterValue retryAfter, string? instance = null)
```

---
