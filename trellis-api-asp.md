# Trellis.Asp — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-results.md, trellis-api-authorization.md, trellis-api-http.md.

**Package:** `Trellis.Asp` | **Namespace:** `Trellis.Asp`

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
| `MethodNotAllowedError` | 405 |
| `NotAcceptableError` | 406 |
| `GoneError` | 410 |
| `ContentTooLargeError` | 413 |
| `UnsupportedMediaTypeError` | 415 |
| `RangeNotSatisfiableError` | 416 |
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

## PartialContentResult — HTTP 206 Partial Content

HTTP 206 Partial Content response for paginated results. Automatically sets `Content-Range` headers per RFC 9110.

```csharp
PartialContentResult(long rangeStart, long rangeEnd, long totalLength, object? value)
PartialContentResult(ContentRangeHeaderValue contentRange, object? value)
ContentRangeHeaderValue ContentRangeHeaderValue { get; }
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

## ConditionalRequestEvaluator

Evaluates RFC 9110 §13.2.2 conditional request preconditions with correct precedence order. Returns a `ConditionalDecision` indicating how the server should respond.

### ConditionalDecision Enum

| Value | Description |
|-------|-------------|
| `PreconditionsSatisfied` | All preconditions passed — serve the representation |
| `NotModified` | `If-None-Match` / `If-Modified-Since` matched — respond 304 |
| `PreconditionFailed` | `If-Match` / `If-Unmodified-Since` failed — respond 412 |

```csharp
public static ConditionalDecision Evaluate(HttpRequest request, RepresentationMetadata metadata)
```

Evaluates in RFC 9110 §13.2.2 precedence: `If-Match` → `If-Unmodified-Since` → `If-None-Match` → `If-Modified-Since`.

---

## ETagHelper

Static helpers for parsing conditional request headers into `EntityTagValue` instances.

```csharp
public static class ETagHelper
{
    // Existing:
    static bool IfNoneMatchMatches(IList<EntityTagHeaderValue> ifNoneMatchHeader, string currentETag)
    static bool IfMatchSatisfied(IList<EntityTagHeaderValue> ifMatchHeader, string currentETag)

    // New — parse headers into EntityTagValue arrays:
    static EntityTagValue[]? ParseIfMatch(HttpRequest request)
    static EntityTagValue[]? ParseIfNoneMatch(HttpRequest request)
    static DateTimeOffset? ParseIfModifiedSince(HttpRequest request)
    static DateTimeOffset? ParseIfUnmodifiedSince(HttpRequest request)
}
```

---

## RangeRequestEvaluator

Evaluates HTTP Range requests per RFC 9110 §14.2. Returns a discriminated union describing the outcome.

### RangeOutcome Union

| Type | Properties | Description |
|------|-----------|-------------|
| `FullRepresentation` | — | No Range header or unsatisfiable → serve full response |
| `PartialContent` | `long From`, `long To`, `long CompleteLength` | Satisfiable range → serve 206 |
| `NotSatisfiable` | `long CompleteLength` | Range syntactically valid but out of bounds → 416 |

```csharp
public static RangeOutcome Evaluate(HttpRequest request, long completeLength)
```

---

## WriteOutcome\<T\>

Discriminated union for write operation results. Each variant maps to the correct HTTP status code and headers.

| Variant | HTTP Status | Properties |
|---------|-------------|------------|
| `Created` | 201 | `T Value`, `string Location`, `RepresentationMetadata? Metadata` |
| `Updated` | 200 | `T Value`, `RepresentationMetadata? Metadata` |
| `UpdatedNoContent` | 204 | `RepresentationMetadata? Metadata` |
| `Accepted` | 202 | `T StatusBody`, `string? MonitorUri`, `RetryAfterValue? RetryAfter` |
| `AcceptedNoContent` | 202 | `string? MonitorUri`, `RetryAfterValue? RetryAfter` |

### WriteOutcomeExtensions

```csharp
public static ActionResult ToActionResult<T, TOut>(
    this WriteOutcome<T> outcome,
    ControllerBase controller,
    Func<T, TOut>? map = null)
```

Maps each variant to the correct HTTP response: `Created` → 201 + `Location`, `Updated` → 200, `UpdatedNoContent` → 204, `Accepted`/`AcceptedNoContent` → 202 + optional `Location` and `Retry-After` headers. Applies `RepresentationMetadata` headers when present.

---

## Metadata-Aware Response Mappers

Overloads of `ToActionResult` that accept `RepresentationMetadata` — emit all metadata headers (`ETag`, `Last-Modified`, `Vary`, `Content-Language`, `Content-Location`, `Accept-Ranges`) and evaluate conditional requests per RFC 9110 §13.2.2.

```csharp
public static ActionResult<TOut> ToActionResult<TIn, TOut>(
    this Result<TIn> result,
    ControllerBase controller,
    RepresentationMetadata metadata,
    Func<TIn, TOut> map)

// Async overloads:
Task<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(
    this Task<Result<TIn>> resultTask, ControllerBase controller,
    RepresentationMetadata metadata, Func<TIn, TOut> map)

ValueTask<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(
    this ValueTask<Result<TIn>> resultTask, ControllerBase controller,
    RepresentationMetadata metadata, Func<TIn, TOut> map)
```

When `ConditionalRequestEvaluator` returns `NotModified` → 304, `PreconditionFailed` → 412.

---

## Companion Header Emission

Error responses automatically emit companion headers based on error type. This is handled internally by `EmitCompanionHeaders`:

| Error Type | Header Emitted |
|-----------|----------------|
| `MethodNotAllowedError` | `Allow: GET, POST, ...` (from `AllowedMethods`) |
| `RateLimitError` (with `RetryAfter`) | `Retry-After: 60` or `Retry-After: <date>` |
| `ServiceUnavailableError` (with `RetryAfter`) | `Retry-After: 60` or `Retry-After: <date>` |
| `ContentTooLargeError` (with `RetryAfter`) | `Retry-After: 60` or `Retry-After: <date>` |
| `RangeNotSatisfiableError` | `Content-Range: bytes */1234` |

---

## IfNoneMatchExtensions

Enforces `If-None-Match` preconditions on `Result<T>` pipeline values.

```csharp
public static Result<T> EnforceIfNoneMatchPrecondition<T>(
    this Result<T> result, string[]? ifNoneMatchETags)

// Async overloads:
Task<Result<T>> EnforceIfNoneMatchPreconditionAsync<T>(
    this Task<Result<T>> resultTask, string[]? ifNoneMatchETags)
ValueTask<Result<T>> EnforceIfNoneMatchPreconditionAsync<T>(
    this ValueTask<Result<T>> resultTask, string[]? ifNoneMatchETags)
```

---

## RedirectResultExtensions

Extension methods for producing redirect responses.

```csharp
public static ActionResult ToMovedPermanently(this ControllerBase controller, string uri)      // 301
public static ActionResult ToFound(this ControllerBase controller, string uri)                  // 302
public static ActionResult ToSeeOther<TValue>(this Result<TValue> result,
    ControllerBase controller, Func<TValue, string> uriSelector)                                // 303
public static ActionResult ToTemporaryRedirect(this ControllerBase controller, string uri)      // 307
public static ActionResult ToPermanentRedirect(this ControllerBase controller, string uri)      // 308
```

---

## IRepresentationValidator\<T\>

Interface for generating entity tags from domain objects. Used to produce ETags for conditional request evaluation.

```csharp
public interface IRepresentationValidator<in T>
{
    EntityTagValue GenerateETag(T value, string? variantKey = null);
}
```

### AggregateRepresentationValidator\<T\>

Default implementation for aggregates — uses the aggregate's built-in `ETag` property. When a `variantKey` is provided, combines it with the ETag using a SHA-256 hash.

```csharp
public sealed class AggregateRepresentationValidator<T> : IRepresentationValidator<T>
    where T : IAggregate
```

---
