# Trellis.Asp — API Reference

**Package:** `Trellis.Asp`  
**Namespaces:** `Trellis.Asp`, `Trellis.Asp.ModelBinding`, `Trellis.Asp.Validation`  
**Purpose:** ASP.NET Core integration for mapping Trellis results to HTTP responses, evaluating HTTP preconditions/ranges/preferences, and validating scalar value objects in MVC and Minimal APIs.

## Types

### Namespace `Trellis.Asp`

### `ActionResultExtensions`

**Declaration**

```csharp
public static class ActionResultExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| See [Extension methods](#extension-methods). | — | MVC `Result<T>` / `Error` / metadata / created / partial-content mappers. |

### `ActionResultExtensionsAsync`

**Declaration**

```csharp
public static class ActionResultExtensionsAsync
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| See [Extension methods](#extension-methods). | — | Async MVC `Task<Result<T>>` and `ValueTask<Result<T>>` mappers. |

### `AggregateRepresentationValidator<T>`

**Declaration**

```csharp
public sealed class AggregateRepresentationValidator<T> : IRepresentationValidator<T> where T : IAggregate
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public EntityTagValue GenerateETag(T value, string? variantKey = null)` | `EntityTagValue` | Returns `EntityTagValue.Strong(value.ETag)` when `variantKey` is null/empty; otherwise hashes `$"{value.ETag}:{variantKey}"` with SHA-256 and returns the first 16 lowercase hex characters as a strong ETag. |

### `ConditionalRequestEvaluator`

**Declaration**

```csharp
public static class ConditionalRequestEvaluator
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static ConditionalDecision Evaluate(HttpRequest request, RepresentationMetadata metadata)` | `ConditionalDecision` | Evaluates RFC 9110 preconditions in this order: `If-Match`; else `If-Unmodified-Since`; then `If-None-Match`; else `If-Modified-Since` for `GET`/`HEAD` only. `If-Match` uses strong comparison; `If-None-Match` uses weak comparison. |

### `ETagHelper`

**Declaration**

```csharp
public static class ETagHelper
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static bool IfNoneMatchMatches(IList<EntityTagHeaderValue> ifNoneMatchHeader, string currentETag)` | `bool` | Weak-comparison helper for `If-None-Match`; returns `true` for `*` or a matching opaque tag. |
| `public static bool IfMatchSatisfied(IList<EntityTagHeaderValue> ifMatchHeader, string currentETag)` | `bool` | Strong-comparison helper for `If-Match`; returns `true` for `*` or a matching strong tag. |
| `public static EntityTagValue[]? ParseIfNoneMatch(HttpRequest request)` | `EntityTagValue[]?` | Returns `null` when header absent; `[]` when present but unparseable/empty; wildcard for `*`; otherwise strong and weak `EntityTagValue` instances. |
| `public static DateTimeOffset? ParseIfModifiedSince(HttpRequest request)` | `DateTimeOffset?` | Returns the typed `If-Modified-Since` header value. |
| `public static DateTimeOffset? ParseIfUnmodifiedSince(HttpRequest request)` | `DateTimeOffset?` | Returns the typed `If-Unmodified-Since` header value. |
| `public static EntityTagValue[]? ParseIfMatch(HttpRequest request)` | `EntityTagValue[]?` | Returns `null` when header absent; `[]` when present but empty or only weak tags; wildcard for `*`; otherwise strong `EntityTagValue` instances only. |

### `HttpResultExtensions`

**Declaration**

```csharp
public static class HttpResultExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| See [Extension methods](#extension-methods). | — | Minimal API `Result<T>` / `Error` / metadata / created / partial-content / Prefer-aware update mappers. |

### `HttpResultExtensionsAsync`

**Declaration**

```csharp
public static class HttpResultExtensionsAsync
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| See [Extension methods](#extension-methods). | — | Async Minimal API mappers for `Task<Result<T>>` and `ValueTask<Result<T>>`. |

### `IRepresentationValidator<in T>`

**Declaration**

```csharp
public interface IRepresentationValidator<in T>
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `EntityTagValue GenerateETag(T value, string? variantKey = null);` | `EntityTagValue` | Generates a representation-specific validator for a domain value and optional variant key. |

### `IfNoneMatchExtensions`

**Declaration**

```csharp
public static class IfNoneMatchExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| See [Extension methods](#extension-methods). | — | Create-if-absent `If-None-Match` guard extensions. |

### `PartialContentHttpResult`

**Declaration**

```csharp
public sealed class PartialContentHttpResult : IResult
```

| Name | Type | Description |
| --- | --- | --- |
| `ContentRangeHeaderValue` | `ContentRangeHeaderValue` | The `Content-Range` header written by the result. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public PartialContentHttpResult(long rangeStart, long rangeEnd, long? totalLength, IResult inner)` | `PartialContentHttpResult` | Creates a `206 Partial Content` result using unit `"items"` and an inner result for body serialization. |
| `public PartialContentHttpResult(ContentRangeHeaderValue contentRangeHeaderValue, IResult inner)` | `PartialContentHttpResult` | Uses a caller-provided `ContentRangeHeaderValue`. |
| `public async Task ExecuteAsync(HttpContext httpContext)` | `Task` | Writes the `Content-Range` header, forces status `206`, then executes the inner result. |

### `PartialContentResult`

**Declaration**

```csharp
public class PartialContentResult : ObjectResult
```

| Name | Type | Description |
| --- | --- | --- |
| `ContentRangeHeaderValue` | `ContentRangeHeaderValue` | The `Content-Range` header written during formatting. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public PartialContentResult(long rangeStart, long rangeEnd, long? totalLength, object? value)` | `PartialContentResult` | Creates a `206 Partial Content` MVC result using unit `"items"`. |
| `public PartialContentResult(ContentRangeHeaderValue contentRangeHeaderValue, object? value)` | `PartialContentResult` | Uses a caller-provided `ContentRangeHeaderValue`. |
| `public override void OnFormatting(ActionContext context)` | `void` | Writes the `Content-Range` response header before the object body is formatted. |

### `PreferHeader`

**Declaration**

```csharp
public sealed class PreferHeader
```

| Name | Type | Description |
| --- | --- | --- |
| `ReturnRepresentation` | `bool` | `true` when a recognized `return=representation` preference was parsed. |
| `ReturnMinimal` | `bool` | `true` when a recognized `return=minimal` preference was parsed. |
| `RespondAsync` | `bool` | `true` when `respond-async` was parsed. |
| `Wait` | `int?` | Parsed `wait=N` preference; `null` when absent or unrecognized. |
| `HandlingStrict` | `bool` | `true` when `handling=strict` was parsed. |
| `HandlingLenient` | `bool` | `true` when `handling=lenient` was parsed. |
| `HasPreferences` | `bool` | `true` only when at least one recognized standard preference was parsed. Unrecognized preferences do not set this property. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static PreferHeader Parse(HttpRequest request)` | `PreferHeader` | Parses the RFC 7240 `Prefer` header. Duplicate recognized preferences use first-wins behavior; unknown preferences are ignored. |

### `RangeOutcome`

**Declaration**

```csharp
public abstract record RangeOutcome
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Base union type for range evaluation results. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No public methods. |

### `RangeOutcome.FullRepresentation`

**Declaration**

```csharp
public sealed record FullRepresentation : RangeOutcome;
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No public methods. |

### `RangeOutcome.PartialContent`

**Declaration**

```csharp
public sealed record PartialContent(long From, long To, long CompleteLength) : RangeOutcome;
```

| Name | Type | Description |
| --- | --- | --- |
| `From` | `long` | Inclusive start byte position. |
| `To` | `long` | Inclusive end byte position. |
| `CompleteLength` | `long` | Full representation length in bytes. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No public methods beyond record-generated members. |

### `RangeOutcome.NotSatisfiable`

**Declaration**

```csharp
public sealed record NotSatisfiable(long CompleteLength) : RangeOutcome;
```

| Name | Type | Description |
| --- | --- | --- |
| `CompleteLength` | `long` | Full representation length in bytes. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No public methods beyond record-generated members. |

### `RangeRequestEvaluator`

**Declaration**

```csharp
public static class RangeRequestEvaluator
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static RangeOutcome Evaluate(HttpRequest request, long completeLength)` | `RangeOutcome` | Returns `FullRepresentation` for non-`GET`, missing `Range`, non-`bytes` unit, empty range sets, multi-range requests, or malformed single ranges with neither `From` nor `To`; returns `NotSatisfiable` when computed `from >= completeLength` or `from > to`; otherwise returns `PartialContent`. Throws for negative `completeLength`. |

### `ScalarValueValidationEndpointFilter`

**Declaration**

```csharp
public sealed class ScalarValueValidationEndpointFilter : IEndpointFilter
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)` | `ValueTask<object?>` | For Minimal APIs, returns `Results.ValidationProblem(validationError.ToDictionary())` when `ValidationErrorsContext` contains errors; otherwise invokes `next`. |

### `ScalarValueValidationFilter`

**Declaration**

```csharp
public sealed class ScalarValueValidationFilter : IActionFilter, IOrderedFilter
```

| Name | Type | Description |
| --- | --- | --- |
| `Order` | `int` | Always `-2000`; runs early in the MVC filter pipeline. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public void OnActionExecuting(ActionExecutingContext context)` | `void` | Short-circuits with a validation problem result for collected JSON validation errors or invalid scalar route/query parameters. |
| `public void OnActionExecuted(ActionExecutedContext context)` | `void` | No-op after action execution. |

### `ScalarValueValidationMiddleware`

**Declaration**

```csharp
public sealed partial class ScalarValueValidationMiddleware
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public ScalarValueValidationMiddleware(RequestDelegate next)` | `ScalarValueValidationMiddleware` | Creates middleware that wraps each request in `ValidationErrorsContext.BeginScope()`. |
| `public async Task InvokeAsync(HttpContext context)` | `Task` | Begins a validation scope, invokes the next middleware, and converts scalar-value `BadHttpRequestException` binding failures into validation problem responses. |

### `ServiceCollectionExtensions`

**Declaration**

```csharp
public static class ServiceCollectionExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| See [Extension methods](#extension-methods). | — | Registration helpers for MVC, Minimal APIs, middleware, endpoint filters, and `TrellisAspOptions`. |

### `TrellisAspOptions`

**Declaration**

```csharp
public sealed class TrellisAspOptions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public TrellisAspOptions MapError<TError>(int statusCode) where TError : Error` | `TrellisAspOptions` | Overrides or adds an error-type-to-status-code mapping. Default mappings include `ValidationError=400`, `BadRequestError=400`, `UnauthorizedError=401`, `ForbiddenError=403`, `NotFoundError=404`, `MethodNotAllowedError=405`, `NotAcceptableError=406`, `ConflictError=409`, `GoneError=410`, `PreconditionFailedError=412`, `ContentTooLargeError=413`, `UnsupportedMediaTypeError=415`, `RangeNotSatisfiableError=416`, `DomainError=422`, `PreconditionRequiredError=428`, `RateLimitError=429`, `UnexpectedError=500`, `ServiceUnavailableError=503`. |

### `ValidationErrorsContext`

**Declaration**

```csharp
public static class ValidationErrorsContext
```

| Name | Type | Description |
| --- | --- | --- |
| `HasErrors` | `bool` | `true` when the current async-local scope contains at least one collected validation error. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IDisposable BeginScope()` | `IDisposable` | Starts a new async-local validation collection scope; disposing restores the previous scope and property name. |
| `public static ValidationError? GetValidationError()` | `ValidationError?` | Returns the aggregated `ValidationError` for the current scope, or `null` when no errors were collected. |

### `WriteOutcome<T>`

**Declaration**

```csharp
public abstract record WriteOutcome<T>
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | Base union type for write-operation responses. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No public methods. |

### `WriteOutcome<T>.Created`

**Declaration**

```csharp
public sealed record Created(T Value, string Location, RepresentationMetadata? Metadata = null) : WriteOutcome<T>;
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `T` | Created resource or response body. |
| `Location` | `string` | Absolute or relative location URI for the created resource. |
| `Metadata` | `RepresentationMetadata?` | Optional representation metadata applied to the response. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No public methods beyond record-generated members. |

### `WriteOutcome<T>.Updated`

**Declaration**

```csharp
public sealed record Updated(T Value, RepresentationMetadata? Metadata = null) : WriteOutcome<T>;
```

| Name | Type | Description |
| --- | --- | --- |
| `Value` | `T` | Updated resource or response body. |
| `Metadata` | `RepresentationMetadata?` | Optional representation metadata applied to the response. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No public methods beyond record-generated members. |

### `WriteOutcome<T>.UpdatedNoContent`

**Declaration**

```csharp
public sealed record UpdatedNoContent(RepresentationMetadata? Metadata = null) : WriteOutcome<T>;
```

| Name | Type | Description |
| --- | --- | --- |
| `Metadata` | `RepresentationMetadata?` | Optional representation metadata applied to the `204` response. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No public methods beyond record-generated members. |

### `WriteOutcome<T>.Accepted`

**Declaration**

```csharp
public sealed record Accepted(T StatusBody, string? MonitorUri = null, RetryAfterValue? RetryAfter = null) : WriteOutcome<T>;
```

| Name | Type | Description |
| --- | --- | --- |
| `StatusBody` | `T` | Accepted-operation status payload. |
| `MonitorUri` | `string?` | Optional monitor URI written to `Location` / `Accepted(...)`. |
| `RetryAfter` | `RetryAfterValue?` | Optional `Retry-After` header value. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No public methods beyond record-generated members. |

### `WriteOutcome<T>.AcceptedNoContent`

**Declaration**

```csharp
public sealed record AcceptedNoContent(string? MonitorUri = null, RetryAfterValue? RetryAfter = null) : WriteOutcome<T>;
```

| Name | Type | Description |
| --- | --- | --- |
| `MonitorUri` | `string?` | Optional monitor URI written to `Location` / `Accepted(...)`. |
| `RetryAfter` | `RetryAfterValue?` | Optional `Retry-After` header value. |

| Signature | Returns | Description |
| --- | --- | --- |
| — | — | No public methods beyond record-generated members. |

### `WriteOutcomeExtensions`

**Declaration**

```csharp
public static class WriteOutcomeExtensions
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| See [Extension methods](#extension-methods). | — | MVC and Minimal API mappers for `WriteOutcome<T>` plus Prefer-aware update helpers. |

### Namespace `Trellis.Asp.ModelBinding`

### `MaybeModelBinder<TValue, TPrimitive>`

**Declaration**

```csharp
public class MaybeModelBinder<TValue, TPrimitive> : ScalarValueModelBinderBase<Maybe<TValue>, TValue, TPrimitive> where TValue : IScalarValue<TValue, TPrimitive> where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected override ModelBindingResult OnMissingValue()` | `ModelBindingResult` | Returns `ModelBindingResult.Success(Maybe<TValue>.None)`. |
| `protected override ModelBindingResult? OnEmptyValue()` | `ModelBindingResult?` | Returns `ModelBindingResult.Success(Maybe<TValue>.None)`. |
| `protected override ModelBindingResult OnSuccess(TValue value)` | `ModelBindingResult` | Returns `ModelBindingResult.Success(Maybe.From(value))`. |

### `ScalarValueModelBinder<TValue, TPrimitive>`

**Declaration**

```csharp
public class ScalarValueModelBinder<TValue, TPrimitive> : ScalarValueModelBinderBase<TValue, TValue, TPrimitive> where TValue : IScalarValue<TValue, TPrimitive> where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected override ModelBindingResult OnMissingValue()` | `ModelBindingResult` | Leaves the binding result unset (`default`). |
| `protected override ModelBindingResult OnSuccess(TValue value)` | `ModelBindingResult` | Returns `ModelBindingResult.Success(value)`. |

### `ScalarValueModelBinderBase<TResult, TValue, TPrimitive>`

**Declaration**

```csharp
public abstract class ScalarValueModelBinderBase<TResult, TValue, TPrimitive> : IModelBinder where TValue : IScalarValue<TValue, TPrimitive> where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected abstract ModelBindingResult OnMissingValue();` | `ModelBindingResult` | Called when no raw value is present in the value provider. |
| `protected virtual ModelBindingResult? OnEmptyValue() => null;` | `ModelBindingResult?` | Called when the raw value is an empty string; return `null` to continue normal conversion. |
| `protected abstract ModelBindingResult OnSuccess(TValue value);` | `ModelBindingResult` | Wraps a validated scalar value into the final binding result. |
| `public Task BindModelAsync(ModelBindingContext bindingContext)` | `Task` | Reads the raw value, converts it to `TPrimitive`, calls `TValue.TryCreate`, and populates `ModelState` on failure. |

### `ScalarValueModelBinderProvider`

**Declaration**

```csharp
public class ScalarValueModelBinderProvider : IModelBinderProvider
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Value object types are preserved by model binding infrastructure")]`<br>`[UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Value object types are preserved by model binding infrastructure")]`<br>`[UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Value object types are preserved by model binding infrastructure")]`<br>`[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Model binding is not compatible with Native AOT")]`<br>`public IModelBinder? GetBinder(ModelBinderProviderContext context)` | `IModelBinder?` | Returns a `MaybeModelBinder<,>` for `Maybe<TScalar>` or a `ScalarValueModelBinder<,>` for direct scalar values; otherwise returns `null`. |

### Namespace `Trellis.Asp.Validation`

### `MaybeScalarValueJsonConverter<TValue, TPrimitive>`

**Declaration**

```csharp
public sealed class MaybeScalarValueJsonConverter<TValue, TPrimitive> : ScalarValueJsonConverterBase<Maybe<TValue>, TValue, TPrimitive> where TValue : class, IScalarValue<TValue, TPrimitive> where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `HandleNull` | `bool` | Inherited from `ScalarValueJsonConverterBase`; always `true`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected override Maybe<TValue> OnNullToken(string fieldName)` | `Maybe<TValue>` | Returns `default` / `Maybe.None`; JSON `null` is valid for optional scalar values. |
| `protected override Maybe<TValue> WrapSuccess(TValue value)` | `Maybe<TValue>` | Returns `Maybe.From(value)`. |
| `protected override Maybe<TValue> OnValidationFailure()` | `Maybe<TValue>` | Returns `default` / `Maybe.None`. |
| `[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TPrimitive type parameter is preserved by JSON serialization infrastructure")]`<br>`[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JSON serialization of primitive types is compatible with AOT")]`<br>`public override void Write(Utf8JsonWriter writer, Maybe<TValue> value, JsonSerializerOptions options)` | `void` | Writes JSON `null` for `Maybe.None`; otherwise writes the wrapped primitive `value.Value.Value`. |

### `MaybeScalarValueJsonConverterFactory`

**Declaration**

```csharp
public sealed class MaybeScalarValueJsonConverterFactory : JsonConverterFactory
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `[UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Value object types are preserved by JSON serialization infrastructure")]`<br>`public override bool CanConvert(Type typeToConvert)` | `bool` | Returns `true` when `typeToConvert` is `Maybe<T>` and `T` is a scalar value type. |
| `[UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Value object types are preserved by JSON serialization infrastructure")]`<br>`[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Value object types are preserved by JSON serialization infrastructure")]`<br>`[UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Inner type of Maybe<T> is preserved by JSON serialization infrastructure")]`<br>`[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JsonConverterFactory is not compatible with Native AOT")]`<br>`public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)` | `JsonConverter?` | Creates `MaybeScalarValueJsonConverter<TValue, TPrimitive>` for supported `Maybe<TScalar>` types. |

### `ScalarValueJsonConverterBase<TResult, TValue, TPrimitive>`

**Declaration**

```csharp
public abstract class ScalarValueJsonConverterBase<TResult, TValue, TPrimitive> : JsonConverter<TResult> where TValue : class, IScalarValue<TValue, TPrimitive> where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `HandleNull` | `bool` | Always `true`; forces `System.Text.Json` to call `Read(...)` for JSON `null` tokens. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected abstract TResult OnNullToken(string fieldName);` | `TResult` | Returns the deserialization result for a JSON `null` token. |
| `protected abstract TResult WrapSuccess(TValue value);` | `TResult` | Wraps a validated scalar value into the final converter result. |
| `protected abstract TResult OnValidationFailure();` | `TResult` | Returns the failure result after a validation error has been collected. |
| `[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TPrimitive type parameter is preserved by JSON serialization infrastructure")]`<br>`[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JSON deserialization of primitive types is compatible with AOT")]`<br>`public override TResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)` | `TResult` | Reads the primitive JSON value, calls `TValue.TryCreate`, collects errors into `ValidationErrorsContext`, and returns the derived-type success/failure wrapper. |
| `protected static string GetDefaultFieldName()` | `string` | Returns the camel-cased scalar type name used when no property name is available. |

### `ValidatingJsonConverter<TValue, TPrimitive>`

**Declaration**

```csharp
public sealed class ValidatingJsonConverter<TValue, TPrimitive> : ScalarValueJsonConverterBase<TValue?, TValue, TPrimitive> where TValue : class, IScalarValue<TValue, TPrimitive> where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `HandleNull` | `bool` | Inherited from `ScalarValueJsonConverterBase`; always `true`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected override TValue? OnNullToken(string fieldName)` | `TValue?` | Adds `"{TypeName} cannot be null."` to `ValidationErrorsContext` and returns `null`. |
| `protected override TValue? WrapSuccess(TValue value)` | `TValue?` | Returns the validated scalar value. |
| `protected override TValue? OnValidationFailure()` | `TValue?` | Returns `null`. |
| `[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "TPrimitive type parameter is preserved by JSON serialization infrastructure")]`<br>`[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JSON serialization of primitive types is compatible with AOT")]`<br>`public override void Write(Utf8JsonWriter writer, TValue? value, JsonSerializerOptions options)` | `void` | Writes JSON `null` for `null`; otherwise writes the scalar primitive `value.Value`. |

### `ValidatingJsonConverterFactory`

**Declaration**

```csharp
public sealed class ValidatingJsonConverterFactory : JsonConverterFactory
```

| Name | Type | Description |
| --- | --- | --- |
| — | — | No public properties. |

| Signature | Returns | Description |
| --- | --- | --- |
| `[UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Value object types are preserved by JSON serialization infrastructure")]`<br>`public override bool CanConvert(Type typeToConvert)` | `bool` | Returns `true` when `typeToConvert` is a scalar value type. |
| `[UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Value object types are preserved by JSON serialization infrastructure")]`<br>`[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Value object types are preserved by JSON serialization infrastructure")]`<br>`[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JsonConverterFactory is not compatible with Native AOT")]`<br>`public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)` | `JsonConverter?` | Creates `ValidatingJsonConverter<TValue, TPrimitive>` for supported scalar value types. |

## Extension methods

### `ActionResultExtensions`

```csharp
public static ActionResult<TValue> ToActionResult<TValue>(this Result<TValue> result, ControllerBase controllerBase)
public static ActionResult<TValue> ToActionResult<TValue>(this Error error, ControllerBase controllerBase)
public static ActionResult<TOut> ToActionResult<TIn, TOut>(this Result<TIn> result, ControllerBase controllerBase, Func<TIn, ContentRangeHeaderValue> funcRange, Func<TIn, TOut> funcValue)
public static ActionResult<TValue> ToActionResult<TValue>(this Result<TValue> result, ControllerBase controllerBase, long from, long to, long totalLength)
public static ActionResult<TOut> ToActionResult<TIn, TOut>(this Result<TIn> result, ControllerBase controllerBase, Func<TIn, TOut> map)
public static ActionResult<TValue> ToCreatedAtActionResult<TValue>(this Result<TValue> result, ControllerBase controllerBase, string actionName, Func<TValue, object?> routeValues, string? controllerName = null)
public static ActionResult<TOut> ToCreatedAtActionResult<TValue, TOut>(this Result<TValue> result, ControllerBase controllerBase, string actionName, Func<TValue, object?> routeValues, Func<TValue, TOut> map, string? controllerName = null)
public static ActionResult<TOut> ToCreatedAtActionResult<TIn, TOut>(this Result<TIn> result, ControllerBase controllerBase, string actionName, Func<TIn, object?> routeValues, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, string? controllerName = null)
public static ActionResult<TOut> ToActionResult<TIn, TOut>(this Result<TIn> result, ControllerBase controller, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map)
public static async Task<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, ControllerBase controller, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map)
public static async ValueTask<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, ControllerBase controller, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map)
```

### `ActionResultExtensionsAsync`

```csharp
public static async Task<ActionResult<TValue>> ToActionResultAsync<TValue>(this Task<Result<TValue>> resultTask, ControllerBase controllerBase)
public static async ValueTask<ActionResult<TValue>> ToActionResultAsync<TValue>(this ValueTask<Result<TValue>> resultTask, ControllerBase controllerBase)
public static async Task<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, ControllerBase controllerBase, Func<TIn, ContentRangeHeaderValue> funcRange, Func<TIn, TOut> funcValue)
public static async ValueTask<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, ControllerBase controllerBase, Func<TIn, ContentRangeHeaderValue> funcRange, Func<TIn, TOut> funcValue)
public static async Task<ActionResult<TValue>> ToActionResultAsync<TValue>(this Task<Result<TValue>> resultTask, ControllerBase controllerBase, long from, long to, long totalLength)
public static async ValueTask<ActionResult<TValue>> ToActionResultAsync<TValue>(this ValueTask<Result<TValue>> resultTask, ControllerBase controllerBase, long from, long to, long totalLength)
public static async Task<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, ControllerBase controllerBase, Func<TIn, TOut> map)
public static async ValueTask<ActionResult<TOut>> ToActionResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, ControllerBase controllerBase, Func<TIn, TOut> map)
public static async Task<ActionResult<TValue>> ToCreatedAtActionResultAsync<TValue>(this Task<Result<TValue>> resultTask, ControllerBase controllerBase, string actionName, Func<TValue, object?> routeValues, string? controllerName = null)
public static async ValueTask<ActionResult<TValue>> ToCreatedAtActionResultAsync<TValue>(this ValueTask<Result<TValue>> resultTask, ControllerBase controllerBase, string actionName, Func<TValue, object?> routeValues, string? controllerName = null)
public static async Task<ActionResult<TOut>> ToCreatedAtActionResultAsync<TValue, TOut>(this Task<Result<TValue>> resultTask, ControllerBase controllerBase, string actionName, Func<TValue, object?> routeValues, Func<TValue, TOut> map, string? controllerName = null)
public static async ValueTask<ActionResult<TOut>> ToCreatedAtActionResultAsync<TValue, TOut>(this ValueTask<Result<TValue>> resultTask, ControllerBase controllerBase, string actionName, Func<TValue, object?> routeValues, Func<TValue, TOut> map, string? controllerName = null)
public static async Task<ActionResult<TOut>> ToCreatedAtActionResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, ControllerBase controllerBase, string actionName, Func<TIn, object?> routeValues, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, string? controllerName = null)
public static async ValueTask<ActionResult<TOut>> ToCreatedAtActionResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, ControllerBase controllerBase, string actionName, Func<TIn, object?> routeValues, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, string? controllerName = null)
```

### `HttpResultExtensions`

```csharp
public static Microsoft.AspNetCore.Http.IResult ToHttpResult<TValue>(this Result<TValue> result, TrellisAspOptions? options = null)
public static Microsoft.AspNetCore.Http.IResult ToHttpResult(this Error error, TrellisAspOptions? options = null)
public static Microsoft.AspNetCore.Http.IResult ToHttpResult<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static Microsoft.AspNetCore.Http.IResult ToCreatedAtRouteHttpResult<TValue>(this Result<TValue> result, string routeName, Func<TValue, RouteValueDictionary> routeValues, TrellisAspOptions? options = null)
public static Microsoft.AspNetCore.Http.IResult ToCreatedAtRouteHttpResult<TValue, TOut>(this Result<TValue> result, string routeName, Func<TValue, RouteValueDictionary> routeValues, Func<TValue, TOut> map, TrellisAspOptions? options = null)
public static Microsoft.AspNetCore.Http.IResult ToHttpResult<TIn, TOut>(this Result<TIn> result, HttpContext httpContext, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static Microsoft.AspNetCore.Http.IResult ToCreatedHttpResult<TIn, TOut>(this Result<TIn> result, HttpContext httpContext, Func<TIn, string> uriSelector, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static Microsoft.AspNetCore.Http.IResult ToHttpResult<TValue>(this Result<TValue> result, long from, long to, long totalLength, TrellisAspOptions? options = null)
public static Microsoft.AspNetCore.Http.IResult ToHttpResult<TIn, TOut>(this Result<TIn> result, Func<TIn, System.Net.Http.Headers.ContentRangeHeaderValue> funcRange, Func<TIn, TOut> funcValue, TrellisAspOptions? options = null)
public static Microsoft.AspNetCore.Http.IResult ToUpdatedHttpResult<TIn, TOut>(this Result<TIn> result, HttpContext httpContext, RepresentationMetadata? metadata, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static Microsoft.AspNetCore.Http.IResult ToUpdatedHttpResult<TIn, TOut>(this Result<TIn> result, HttpContext httpContext, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)
```

### `HttpResultExtensionsAsync`

```csharp
public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TValue>(this Task<Result<TValue>> resultTask, TrellisAspOptions? options = null)
public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TValue>(this ValueTask<Result<TValue>> resultTask, TrellisAspOptions? options = null)
public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static async Task<Microsoft.AspNetCore.Http.IResult> ToCreatedAtRouteHttpResultAsync<TValue>(this Task<Result<TValue>> resultTask, string routeName, Func<TValue, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues, TrellisAspOptions? options = null)
public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToCreatedAtRouteHttpResultAsync<TValue>(this ValueTask<Result<TValue>> resultTask, string routeName, Func<TValue, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues, TrellisAspOptions? options = null)
public static async Task<Microsoft.AspNetCore.Http.IResult> ToCreatedAtRouteHttpResultAsync<TValue, TOut>(this Task<Result<TValue>> resultTask, string routeName, Func<TValue, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues, Func<TValue, TOut> map, TrellisAspOptions? options = null)
public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToCreatedAtRouteHttpResultAsync<TValue, TOut>(this ValueTask<Result<TValue>> resultTask, string routeName, Func<TValue, Microsoft.AspNetCore.Routing.RouteValueDictionary> routeValues, Func<TValue, TOut> map, TrellisAspOptions? options = null)
public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, HttpContext httpContext, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, HttpContext httpContext, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static async Task<Microsoft.AspNetCore.Http.IResult> ToCreatedHttpResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, HttpContext httpContext, Func<TIn, string> uriSelector, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToCreatedHttpResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, HttpContext httpContext, Func<TIn, string> uriSelector, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TValue>(this Task<Result<TValue>> resultTask, long from, long to, long totalLength, TrellisAspOptions? options = null)
public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TValue>(this ValueTask<Result<TValue>> resultTask, long from, long to, long totalLength, TrellisAspOptions? options = null)
public static async Task<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, Func<TIn, System.Net.Http.Headers.ContentRangeHeaderValue> funcRange, Func<TIn, TOut> funcValue, TrellisAspOptions? options = null)
public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToHttpResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, Func<TIn, System.Net.Http.Headers.ContentRangeHeaderValue> funcRange, Func<TIn, TOut> funcValue, TrellisAspOptions? options = null)
public static async Task<Microsoft.AspNetCore.Http.IResult> ToUpdatedHttpResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, HttpContext httpContext, RepresentationMetadata? metadata, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToUpdatedHttpResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, HttpContext httpContext, RepresentationMetadata? metadata, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static async Task<Microsoft.AspNetCore.Http.IResult> ToUpdatedHttpResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, HttpContext httpContext, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)
public static async ValueTask<Microsoft.AspNetCore.Http.IResult> ToUpdatedHttpResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, HttpContext httpContext, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map, TrellisAspOptions? options = null)
```

### `IfNoneMatchExtensions`

```csharp
public static Result<T> EnforceIfNoneMatchPrecondition<T>(this Result<T> result, EntityTagValue[]? ifNoneMatchETags)
public static async Task<Result<T>> EnforceIfNoneMatchPreconditionAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? ifNoneMatchETags)
public static async ValueTask<Result<T>> EnforceIfNoneMatchPreconditionAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? ifNoneMatchETags)
```

### `ServiceCollectionExtensions`

```csharp
public static IMvcBuilder AddScalarValueValidation(this IMvcBuilder builder)
public static IServiceCollection AddScalarValueValidation(this IServiceCollection services)
public static IApplicationBuilder UseScalarValueValidation(this IApplicationBuilder app)
public static IServiceCollection AddScalarValueValidationForMinimalApi(this IServiceCollection services)
public static RouteHandlerBuilder WithScalarValueValidation(this RouteHandlerBuilder builder)
public static IServiceCollection AddTrellisAsp(this IServiceCollection services)
public static IServiceCollection AddTrellisAsp(this IServiceCollection services, Action<TrellisAspOptions> configure)
```

### `WriteOutcomeExtensions`

```csharp
public static ActionResult ToActionResult<T, TOut>(this WriteOutcome<T> outcome, ControllerBase controller, Func<T, TOut>? map = null)
public static ActionResult<TOut> ToUpdatedActionResult<TIn, TOut>(this Result<TIn> result, ControllerBase controller, RepresentationMetadata? metadata, Func<TIn, TOut> map)
public static async Task<ActionResult<TOut>> ToUpdatedActionResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, ControllerBase controller, RepresentationMetadata? metadata, Func<TIn, TOut> map)
public static async ValueTask<ActionResult<TOut>> ToUpdatedActionResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, ControllerBase controller, RepresentationMetadata? metadata, Func<TIn, TOut> map)
public static ActionResult<TOut> ToUpdatedActionResult<TIn, TOut>(this Result<TIn> result, ControllerBase controller, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map)
public static async Task<ActionResult<TOut>> ToUpdatedActionResultAsync<TIn, TOut>(this Task<Result<TIn>> resultTask, ControllerBase controller, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map)
public static async ValueTask<ActionResult<TOut>> ToUpdatedActionResultAsync<TIn, TOut>(this ValueTask<Result<TIn>> resultTask, ControllerBase controller, Func<TIn, RepresentationMetadata> metadataSelector, Func<TIn, TOut> map)
public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T>(this WriteOutcome<T> outcome, HttpContext httpContext)
public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T, TOut>(this WriteOutcome<T> outcome, HttpContext httpContext, Func<T, TOut>? map)
```

## Enums

### `ConditionalDecision`

| Name | Numeric value |
| --- | ---: |
| `PreconditionsSatisfied` | `0` |
| `NotModified` | `1` |
| `PreconditionFailed` | `2` |

## Code examples

### MVC setup and controller mapping

```csharp
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;

public sealed record WidgetResponse(string Id);

[ApiController]
[Route("widgets")]
public sealed class WidgetsController : ControllerBase
{
    [HttpGet("{id}")]
    public ActionResult<WidgetResponse> Get(string id)
    {
        Result<string> result = Result.Ok(id);
        return result.ToActionResult(this, value => new WidgetResponse(value));
    }
}
```

### Minimal API setup with scalar-value validation

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddScalarValueValidation();
builder.Services.AddScalarValueValidationForMinimalApi();
builder.Services.AddTrellisAsp();

var app = builder.Build();

app.UseScalarValueValidation();

app.MapGet("/widgets/{id}", (string id) =>
{
    Result<string> result = Result.Ok(id);
    return result.ToHttpResult();
}).WithScalarValueValidation();

app.MapControllers();
app.Run();
```

### Prefer-aware write outcome mapping

```csharp
using Microsoft.AspNetCore.Http;
using Trellis;
using Trellis.Asp;

static IResult UpdateWidget(HttpContext httpContext)
{
    WriteOutcome<string> outcome = new WriteOutcome<string>.Updated("updated-widget");
    return outcome.ToHttpResult<string, string>(httpContext, value => value);
}
```

## Cross-references

- [trellis-api-results.md](trellis-api-results.md)
- [trellis-api-ddd.md](trellis-api-ddd.md)
- [trellis-api-primitives.md](trellis-api-primitives.md)
- [trellis-api-http.md](trellis-api-http.md)
