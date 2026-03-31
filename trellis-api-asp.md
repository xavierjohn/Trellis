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
