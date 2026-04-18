# Trellis.Results API Reference

**Package:** `Trellis.Results`  
**Namespace:** `Trellis`  
**Purpose:** Provides Trellis result, maybe, scalar-value, and HTTP-oriented error primitives for railway-oriented application flows.

See also: [trellis-api-patterns.md](trellis-api-patterns.md), [trellis-api-asp.md](trellis-api-asp.md), [trellis-api-primitives.md](trellis-api-primitives.md).

---

## Types

### `public interface IResult`

Base success/failure contract.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `IsSuccess` | `bool` | `true` for success results |
| `IsFailure` | `bool` | `true` for failure results |
| `Error` | `Error` | Throws when the result is successful |

#### Methods

None.

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

Static factory and helper surface for `Result<TValue>` and `Result<Unit>`.

#### Properties

None.

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Result<TValue> Success<TValue>(TValue value)` | Success factory |
| `public static Result<TValue> Success<TValue>(Func<TValue> funcOk)` | Deferred success factory |
| `public static Result<Unit> Success()` | Success without payload |
| `public static Result<TValue> Failure<TValue>(Error error)` | Failure factory |
| `public static Result<TValue> Failure<TValue>(Func<Error> error)` | Deferred failure factory |
| `public static Result<Unit> Failure(Error error)` | Failure without payload |
| `public static Result<TValue> SuccessIf<TValue>(bool isSuccess, in TValue value, Error error)` | Conditional success |
| `public static Result<(T1, T2)> SuccessIf<T1, T2>(bool isSuccess, in T1 t1, in T2 t2, Error error)` | Conditional 2-tuple success |
| `public static Result<TValue> FailureIf<TValue>(bool isFailure, TValue value, Error error)` | Conditional failure |
| `public static Result<TValue> FailureIf<TValue>(Func<bool> failurePredicate, in TValue value, Error error)` | Deferred predicate version |
| `public static Task<Result<TValue>> SuccessIfAsync<TValue>(Func<Task<bool>> predicate, TValue value, Error error)` | Async conditional success |
| `public static Task<Result<TValue>> FailureIfAsync<TValue>(Func<Task<bool>> failurePredicate, TValue value, Error error)` | Async conditional failure |
| `public static Result<Unit> Ensure(bool flag, Error error)` | Converts a boolean to `Result<Unit>` |
| `public static Result<Unit> Ensure(Func<bool> predicate, Error error)` | Deferred predicate version |
| `public static Task<Result<Unit>> EnsureAsync(Func<Task<bool>> predicate, Error error)` | Async predicate version |
| `public static Result<T> Try<T>(Func<T> func, Func<Exception, Error>? map = null)` | Converts thrown exceptions to failures |
| `public static Task<Result<T>> TryAsync<T>(Func<Task<T>> func, Func<Exception, Error>? map = null)` | Async exception capture |
| `public static Result<Unit> FromException(Exception ex, Func<Exception, Error>? map = null)` | Failure from exception |
| `public static Result<T> FromException<T>(Exception ex, Func<Exception, Error>? map = null)` | Typed failure from exception |
| `public static Result<(T1, T2)> Combine<T1, T2>(Result<T1> r1, Result<T2> r2)` | Combines two results |
| `public static Result<(T1, ..., T9)> Combine<...>(...)` | Additional generated arities up to 9 |
| `public static (Task<Result<T1>>, ..., Task<Result<T9>>) ParallelAsync<...>(...)` | Starts async result-producing operations in parallel, arities 2-9 |

#### Factory Methods

`Success`, `Failure`, `SuccessIf`, `FailureIf`, `Ensure`, `Try`, `FromException`, `Combine`, and `ParallelAsync`.

---

### `public readonly partial struct Result<TValue> : IResult<TValue>, IEquatable<Result<TValue>>, IFailureFactory<Result<TValue>>`

Represents either a successful `TValue` or a failure `Error`.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `Value` | `TValue` | Throws when `IsFailure` is `true` |
| `Error` | `Error` | Throws when `IsSuccess` is `true` |
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

| Signature | Notes |
| --- | --- |
| `public static implicit operator Result<TValue>(TValue value)` | Success conversion |
| `public static implicit operator Result<TValue>(Error error)` | Failure conversion |
| `public static bool operator ==(Result<TValue> left, Result<TValue> right)` | Equality |
| `public static bool operator !=(Result<TValue> left, Result<TValue> right)` | Inequality |

#### Factory Methods

Use the static `Result` type.

---

### `public record struct Unit`

Represents “no value” for `Result<Unit>`.

#### Properties

None.

#### Methods

None.

#### Factory Methods

Use `Result.Success()`.

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

Multi-field validation helpers for `Maybe<T>` values. Each method returns `Result<Unit>` — success when the invariant holds, or a `ValidationError` with field-level details when it does not. All error codes use `"validation.error"`.

#### Methods

| Signature | Notes |
| --- | --- |
| `public static Result<Unit> AllOrNone<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstName, string secondName)` | All fields present or all absent. Arities 2, 3, 4. |
| `public static Result<Unit> Requires<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstName, string secondName)` | If `first` is present, `second` must be too. Arity 2. |
| `public static Result<Unit> MutuallyExclusive<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstName, string secondName)` | At most one field may be present. Arities 2, 3. |
| `public static Result<Unit> ExactlyOne<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstName, string secondName)` | Exactly one field must be present. Arities 2, 3. |
| `public static Result<Unit> AtLeastOne<T1, T2>(Maybe<T1> first, Maybe<T2> second, string firstName, string secondName)` | At least one field must be present. Arities 2, 3. |

#### Usage

```csharp
// All-or-none: street + city must both be provided or both omitted
MaybeInvariant.AllOrNone(command.Street, command.City, "street", "city")

// Requires: if discount is given, reason is required
MaybeInvariant.Requires(command.Discount, command.DiscountReason, "discount", "discountReason")

// ExactlyOne: must provide either email or phone
MaybeInvariant.ExactlyOne(command.Email, command.Phone, "email", "phone")
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

### `public class Error : IEquatable<Error>`

Base error type for all Trellis failure flows.

#### Properties

| Name | Type | Notes |
| --- | --- | --- |
| `Code` | `string` | Machine-readable error code |
| `Detail` | `string` | Human-readable detail |
| `Instance` | `string?` | Optional instance identifier |

#### Methods

| Signature | Notes |
| --- | --- |
| `public Error(string detail, string code)` | Constructor |
| `public Error(string detail, string code, string? instance)` | Constructor |
| `public bool Equals(Error? other)` | **Compares only `Code`** |
| `public override bool Equals(object? obj)` | Object equality |
| `public override int GetHashCode()` | **Hashes only `Code`** |
| `public override string ToString()` | Debug string |

#### Factory Methods

`Validation`, `BadRequest`, `Conflict`, `PreconditionFailed`, `PreconditionRequired`, `NotFound`, `Unauthorized`, `Forbidden`, `Unexpected`, `Domain`, `RateLimit`, `ServiceUnavailable`, `Gone`, `MethodNotAllowed`, `NotAcceptable`, `UnsupportedMediaType`, `ContentTooLarge`, and `RangeNotSatisfiable`.

Factory families exist in these forms where applicable:

- default-code overloads: `(string detail, string? instance = null)`
- `IFormattable` instance overloads: `(string detail, TInstance instance) where TInstance : IFormattable`
- custom-code overloads: `(string detail, string code, string? instance)`
- special metadata overloads for `RateLimit`, `ServiceUnavailable`, `MethodNotAllowed`, `ContentTooLarge`, and `RangeNotSatisfiable`

---

### Concrete error types

Each concrete type mainly exposes constructors plus additional metadata where noted.

| Type Declaration | Additional Public Members |
| --- | --- |
| `public sealed class ValidationError : Error, IEquatable<ValidationError>` | `ImmutableArray<ValidationError.FieldError> FieldErrors`, `static ValidationError For(string fieldName, string message, string code = "validation.error", string? detail = null, string? instance = null)`, `ValidationError And(string fieldName, string message)`, `ValidationError And(string fieldName, params string[] messages)`, `ValidationError Merge(ValidationError other)` |
| `public readonly record struct ValidationError.FieldError` | `string FieldName`, `ImmutableArray<string> Details`, constructor from `IEnumerable<string>`, `override string ToString()` |
| `public sealed class BadRequestError : Error` | constructor only |
| `public sealed class ConflictError : Error` | constructor only |
| `public sealed class PreconditionFailedError : Error` | constructor only |
| `public sealed class PreconditionRequiredError : Error` | constructor only |
| `public sealed class NotFoundError : Error` | constructor only |
| `public sealed class UnauthorizedError : Error` | constructor only |
| `public sealed class ForbiddenError : Error` | constructor only |
| `public sealed class UnexpectedError : Error` | constructor only |
| `public sealed class DomainError : Error` | constructor only |
| `public sealed class GoneError : Error` | constructor only |
| `public sealed class NotAcceptableError : Error` | constructor only |
| `public sealed class UnsupportedMediaTypeError : Error` | constructor only |
| `public sealed class RateLimitError : Error` | `RetryAfterValue? RetryAfter` |
| `public sealed class ServiceUnavailableError : Error` | `RetryAfterValue? RetryAfter` |
| `public sealed class MethodNotAllowedError : Error` | `IReadOnlyList<string> AllowedMethods` |
| `public sealed class ContentTooLargeError : Error` | `RetryAfterValue? RetryAfter` |
| `public sealed class RangeNotSatisfiableError : Error` | `long CompleteLength`, `string Unit`, `string ContentRangeHeaderValue` |
| `public sealed class AggregateError : Error` | `IReadOnlyList<Error> Errors`, `ValidationError? FlattenValidationErrors()` |

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
| `CheckExtensions`, `CheckExtensionsAsync` | Runs side-effect validations that return `IResult`/`Result<Unit>` while preserving original success value |
| `CheckIfExtensions`, `CheckIfExtensionsAsync` | Conditional `Check` variants |
| `CombineExtensions`, `CombineExtensionsAsync`, `CombineErrorExtensions` | Combines results, including tuple and enumerable forms |
| `DiscardExtensions`, `DiscardTaskExtensions`, `DiscardValueTaskExtensions` | Converts `Result<T>` to `Result<Unit>` |
| `EnsureExtensions`, `EnsureExtensionsAsync`, `EnsureAllExtensions`, `EnsureAllExtensionsAsync` | Predicate-based validation on successful values; includes collection-wide validation |
| `FlattenValidationErrorsExtensions` | Flattens nested validation failures from aggregate results |
| `GetValueOrDefaultExtensions` | Non-throwing value fallback helpers |
| `ResultLinqExtensions` | LINQ query syntax support via `Select`/`SelectMany` |
| `MapExtensions`, `MapExtensionsAsync`, `MapIfExtensions`, `MapOnFailureExtensions` | Success-path mapping, conditional mapping, and failure remapping; tuple overloads generated for arities 2-9 |
| `MatchExtensions`, `MatchExtensionsAsync`, `MatchTupleExtensions`, `MatchTupleExtensionsAsync` | Terminal branching for normal and tuple results |
| `MatchErrorExtensions`, `MatchErrorExtensionsAsync` | Terminal branching by concrete error subtype |
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

## Error Types

| Type | Factory Method | Default Code | HTTP Status |
| --- | --- | --- | --- |
| `ValidationError` | `Error.Validation(...)` | `validation.error` | `400 Bad Request` |
| `BadRequestError` | `Error.BadRequest(...)` | `bad.request.error` | `400 Bad Request` |
| `ConflictError` | `Error.Conflict(...)` | `conflict.error` | `409 Conflict` |
| `PreconditionFailedError` | `Error.PreconditionFailed(...)` | `precondition.failed.error` | `412 Precondition Failed` |
| `PreconditionRequiredError` | `Error.PreconditionRequired(...)` | `precondition.required.error` | `428 Precondition Required` |
| `NotFoundError` | `Error.NotFound(...)` | `not.found.error` | `404 Not Found` |
| `UnauthorizedError` | `Error.Unauthorized(...)` | `unauthorized.error` | `401 Unauthorized` |
| `ForbiddenError` | `Error.Forbidden(...)` | `forbidden.error` | `403 Forbidden` |
| `UnexpectedError` | `Error.Unexpected(...)` | `unexpected.error` | `500 Internal Server Error` |
| `DomainError` | `Error.Domain(...)` | `domain.error` | framework-specific; commonly `422 Unprocessable Content` or mapped application-specific |
| `RateLimitError` | `Error.RateLimit(...)` | `rate.limit.error` | `429 Too Many Requests` |
| `ServiceUnavailableError` | `Error.ServiceUnavailable(...)` | `service.unavailable.error` | `503 Service Unavailable` |
| `GoneError` | `Error.Gone(...)` | `gone.error` | `410 Gone` |
| `MethodNotAllowedError` | `Error.MethodNotAllowed(...)` | `method.not.allowed.error` | `405 Method Not Allowed` |
| `NotAcceptableError` | `Error.NotAcceptable(...)` | `not.acceptable.error` | `406 Not Acceptable` |
| `UnsupportedMediaTypeError` | `Error.UnsupportedMediaType(...)` | `unsupported.media.type.error` | `415 Unsupported Media Type` |
| `ContentTooLargeError` | `Error.ContentTooLarge(...)` | `content.too.large.error` | `413 Content Too Large` |
| `RangeNotSatisfiableError` | `Error.RangeNotSatisfiable(...)` | `range.not.satisfiable.error` | `416 Range Not Satisfiable` |
| `AggregateError` | constructor | `aggregate.error` | depends on contained errors |

---

## Examples

### Result flow

```csharp
using Trellis;

Result<int> Divide(int left, int right) =>
    Result.Ensure(right != 0, Error.BadRequest("Right operand must not be zero"))
        .Map(_ => left / right);
```

### Maybe to Result

```csharp
using Trellis;

Maybe<string> maybeEmail = Maybe.From("user@example.com");

Result<string> emailResult = maybeEmail.ToResult(
    Error.Validation("Email is required", "email"));
```

