---
package: Trellis.Asp
namespaces: [Trellis.Asp, Trellis.Asp.Authorization, Trellis.Asp.ModelBinding, Trellis.Asp.Routing, Trellis.Asp.Validation]
types: [TrellisHttpResult, ToHttpResponse, AsActionResult, HttpResponseOptionsBuilder<T>, ClaimsActorProvider, EntraActorProvider, DevelopmentActorProvider, CachingActorProvider]
version: v3
last_verified: 2026-05-01
audience: [llm]
---
# Trellis.Asp — API Reference

**Package:** `Trellis.Asp` (bundles the AOT-friendly `Trellis.AspSourceGenerator.dll` at `analyzers/dotnet/cs/` — installing `Trellis.Asp` attaches the generator automatically — and contains the ASP.NET actor providers formerly published as `Trellis.Asp.Authorization`).
**Namespaces:** `Trellis.Asp`, `Trellis.Asp.Authorization`, `Trellis.Asp.ModelBinding`, `Trellis.Asp.Routing`, `Trellis.Asp.Validation`
**Purpose:** ASP.NET Core integration for mapping Trellis `Result`/`Result<T>`/`WriteOutcome<T>`/`Page<T>` values to HTTP responses, evaluating HTTP preconditions / ranges / `Prefer` preferences, hydrating actors from JWT claims, validating scalar value objects in MVC and Minimal APIs, and emitting AOT-friendly `JsonConverter`s for Trellis scalar values.

The single supported response verb is `result.ToHttpResponse(...)`. It returns `Microsoft.AspNetCore.Http.IResult` and works in both Minimal API and MVC hosts (.NET 7+ executes `IResult` natively in MVC). For typed `ActionResult<T>` signatures, chain `.AsActionResult<T>()`. Configure protocol semantics via the fluent `HttpResponseOptionsBuilder<T>` (`WithETag`, `WithLastModified`, `Vary`, `Created`/`CreatedAtRoute`/`CreatedAtAction`, `EvaluatePreconditions`, `HonorPrefer`, `WithRange`, `WithErrorMapping`, …).

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package.

## Use this file when

- You are wiring ASP.NET Core endpoints/controllers that return Trellis `Result`, `Result<T>`, `WriteOutcome<T>`, or `Page<T>`.
- You need the exact response-mapping verb, status-code behavior, Problem Details mapping, ETag/range/preference handling, actor-provider setup, scalar value-object binding, or route constraints.
- You are implementing API surface polish: failure response metadata, versioned `Location` headers, or tests proving `Error.UnprocessableContent` maps to 422.

## Patterns Index

| Goal | Canonical API / action | See |
|---|---|---|
| Enable Trellis Result-to-HTTP mapping | Call `builder.Services.AddTrellisAsp()` or `services.AddTrellis(o => o.UseAsp())` in the composition root. Exception middleware is only a 500 fallback; it does not map `Result` failures. | [`ServiceCollectionExtensions`](#servicecollectionextensions), [ServiceDefaults](trellis-api-servicedefaults.md) |
| Return a Minimal API result | `return result.ToHttpResponse(...)` | [`HttpResponseExtensions`](#httpresponseextensions) |
| Return an MVC typed action result | Convert first, then adapt: `return result.ToHttpResponse(...).AsActionResult<T>()` or `return await result.ToHttpResponseAsync(...).AsActionResultAsync<T>()` | [`ActionResultAdapterExtensions`](#actionresultadapterextensions) |
| Configure 201 Created | `.ToHttpResponse(o => o.Created(...))`, `.CreatedAtRoute(...)`, or `.CreatedAtAction(...)` | [`HttpResponseOptionsBuilder<TDomain>`](#httpresponseoptionsbuildertdomain) |
| Generate versioned `Location` headers | **Required when query-string API versioning is enabled.** Include the API version in `CreatedAtRoute` route values: `["api-version"] = ApiVersion`. Omitting it produces `Location` headers that 404 on dereference. | [`CreatedAtRoute`](#httpresponseoptionsbuildertdomain) |
| Map failure codes globally | Configure `TrellisAspOptions.ErrorStatusCodeMap` through `AddTrellisAsp(...)` | [`TrellisAspOptions`](#trellisaspoptions) |
| Override failure mapping for one endpoint | `.WithErrorMapping(...)` / `.WithErrorMapping<TError>(statusCode)` | [`HttpResponseOptionsBuilder<TDomain>`](#httpresponseoptionsbuildertdomain) |
| Document endpoint failure codes | Add ASP.NET response metadata for every spec-listed failure status (`422`, `409`, `403`, `404`, etc.) in addition to happy-path metadata. | [Code examples](#code-examples) |
| Add ETag / conditional GET | `.WithETag(...)`, `.WithLastModified(...)`, `.EvaluatePreconditions()` | [`HttpResponseOptionsBuilder<TDomain>`](#httpresponseoptionsbuildertdomain), [`ETagHelper`](#etaghelper) |
| Honor `Prefer: return=minimal` | `.HonorPrefer()` on write responses | [`HttpResponseOptionsBuilder<TDomain>`](#httpresponseoptionsbuildertdomain) |
| Return paginated list responses | `Result<Page<T>>.ToHttpResponse(nextUrlBuilder, bodySelector, ...)` | [`PagedResponse<TResponse>`](#pagedresponsetresponse) |
| Resolve actors from requests | `AddClaimsActorProvider`, `AddEntraActorProvider`, or `AddDevelopmentActorProvider` | [`Trellis.Asp.Authorization`](#namespace-trellisaspauthorization) |
| Bind scalar value objects from routes/query/body | `AddTrellisAsp()` plus route constraints / validation middleware as needed | [`Trellis.Asp.ModelBinding`](#namespace-trellisaspmodelbinding), [`Trellis.Asp.Validation`](#namespace-trellisaspvalidation) |

## Endpoint checklist for generated APIs

- Composition root calls `AddTrellisAsp()` or `UseAsp()`.
- Every endpoint that returns a Trellis `Result` ultimately calls `ToHttpResponse` / `AsActionResult`.
- OpenAPI metadata includes the success code and every failure code listed by the product spec.
- `201 Created` endpoints include a usable `Location` header. **Under query-string API versioning, include `["api-version"] = ApiVersion` in `CreatedAtRoute` route values** (or use a literal `Location` that already contains `?api-version=...`). Forgetting this is a silent `Location`-404 bug — tests pass and OpenAPI looks fine, but clients following the `Location` header get 404.
- `[Consumes("application/json")]` is **not** safe at the controller level when the controller has trigger-style POSTs without bodies (e.g., `POST /orders/{id}/submission`). ASP.NET Core returns `415 Unsupported Media Type` for any request without a `Content-Type` header. Apply `[Consumes]` per-action on body-bearing endpoints only, or scope it to a route convention.
- Integration tests include at least one business-validation failure that asserts `422` Problem Details; do not rely on exception middleware to prove Result mapping.

### Cross-package preflight for endpoint changes

| If the endpoint change includes... | Also read | Why |
|---|---|---|
| Sending commands or queries through Mediator | [`trellis-api-mediator.md`](trellis-api-mediator.md) | ASP maps the response, but validation/authorization/logging/commit behavior belongs to the Mediator pipeline. |
| EF-backed writes | [`trellis-api-efcore.md`](trellis-api-efcore.md), [`trellis-api-servicedefaults.md`](trellis-api-servicedefaults.md) | Handlers stage changes; `TransactionalCommandBehavior` commits only when registered in the correct order. |
| Actor resolution or authorization failures | [`trellis-api-authorization.md`](trellis-api-authorization.md), [`trellis-api-mediator.md`](trellis-api-mediator.md) | ASP provides actor providers; authorization contracts and behaviors live outside the response mapper. |
| Integration tests or `.http` examples | [`trellis-api-testing-aspnetcore.md`](trellis-api-testing-aspnetcore.md) | Failure-path status/header expectations should be executable, not only documented in OpenAPI. |

## Types

### Namespace `Trellis.Asp`

### `HttpResponseExtensions`

**Declaration**

```csharp
public static class HttpResponseExtensions
```

The single Trellis verb for converting `Result` / `Result<T>` / `Result<WriteOutcome<T>>` / `Result<Page<T>>` to ASP.NET Core HTTP responses.

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IResult ToHttpResponse(this Error error, Action<HttpResponseOptionsBuilder>? configure = null)` | `IResult` | Maps a standalone `Error` to a Problem Details response (for endpoints that produce a deterministic error). |
| `public static IResult ToHttpResponse<T>(this Result<T> result, Action<HttpResponseOptionsBuilder<T>>? configure = null)` | `IResult` | Maps `Result<T>` to `200 OK` with the value as body, or `201 Created` + `Location` when `Created` / `CreatedAtRoute` / `CreatedAtAction` is configured. For `Result<Unit>` (the no-payload result returned by `Result.Ok()` / `Result.Fail(error)`), success emits `204 No Content`. Failures go through Problem Details. |
| `public static IResult ToHttpResponse<TDomain, TBody>(this Result<TDomain> result, Func<TDomain, TBody> body, Action<HttpResponseOptionsBuilder<TDomain>>? configure = null)` | `IResult` | Same as the `Result<T>` overload, but projects the response body via `body`. Selectors in the options builder still run against the domain value. |
| `public static IResult ToHttpResponse<T>(this Result<WriteOutcome<T>> result, Action<HttpResponseOptionsBuilder<T>>? configure = null)` | `IResult` | Maps `Result<WriteOutcome<T>>` per RFC 9110: `Created → 201 + Location`, `Updated → 200` (or `204` with `Prefer: return=minimal` **when `HonorPrefer()` is configured**), `UpdatedNoContent → 204`, `Accepted → 202` + `Retry-After`, `AcceptedNoContent → 202`. |
| `public static IResult ToHttpResponse<TDomain, TBody>(this Result<WriteOutcome<TDomain>> result, Func<TDomain, TBody> body, Action<HttpResponseOptionsBuilder<TDomain>>? configure = null)` | `IResult` | `WriteOutcome` overload with body projection. |
| `public static IResult ToHttpResponse<T, TBody>(this Result<Page<T>> result, Func<Cursor, int, string> nextUrlBuilder, Func<T, TBody> body, Action<HttpResponseOptionsBuilder<Page<T>>>? configure = null)` | `IResult` | Maps `Result<Page<T>>` to a paginated JSON envelope (`PagedResponse<TBody>`) plus an RFC 8288 `Link` header. `nextUrlBuilder(cursor, appliedLimit)` builds the absolute URL for next/previous links. |

Each overload also exposes `Task<...>` and `ValueTask<...>` async variants named `ToHttpResponseAsync` with identical signatures.

### `HttpResponseOptionsBuilder<TDomain>`

**Declaration**

```csharp
public sealed class HttpResponseOptionsBuilder<TDomain>
```

Fluent options builder used by every generic `ToHttpResponse` overload. Selectors run against the `TDomain` value (not the projected response body). All methods return `this` for chaining.

| Signature | Returns | Description |
| --- | --- | --- |
| `WithETag(Func<TDomain, string> selector)` | `HttpResponseOptionsBuilder<TDomain>` | Sets a strong ETag (wraps the string in `EntityTagValue.Strong`). |
| `WithETag(Func<TDomain, EntityTagValue> selector)` | `HttpResponseOptionsBuilder<TDomain>` | Sets a strong or weak ETag from a caller-built `EntityTagValue`. |
| `WithLastModified(Func<TDomain, DateTimeOffset> selector)` | `HttpResponseOptionsBuilder<TDomain>` | Emits `Last-Modified` header in RFC 1123 format. |
| `Vary(params string[] headers)` | `HttpResponseOptionsBuilder<TDomain>` | Appends headers to the response `Vary` header (existing values preserved; duplicates suppressed). |
| `WithContentLanguage(params string[] languages)` | `HttpResponseOptionsBuilder<TDomain>` | Joins values into `Content-Language`. |
| `WithContentLocation(Func<TDomain, string> selector)` | `HttpResponseOptionsBuilder<TDomain>` | Sets the `Content-Location` header. |
| `WithAcceptRanges(string acceptRanges)` | `HttpResponseOptionsBuilder<TDomain>` | Sets `Accept-Ranges` (e.g. `"bytes"` or `"none"`). |
| `Created(string locationLiteral)` | `HttpResponseOptionsBuilder<TDomain>` | Returns `201 Created` with a literal `Location` header. |
| `Created(Func<TDomain, string> selector)` | `HttpResponseOptionsBuilder<TDomain>` | Returns `201 Created` with a `Location` derived from the value. |
| `CreatedAtRoute(string routeName, Func<TDomain, RouteValueDictionary> routeValues)` | `HttpResponseOptionsBuilder<TDomain>` | Returns `201 Created` with a `Location` generated via `LinkGenerator.GetUriByName` (resolved from `HttpContext.RequestServices` at execute time). AOT-safe. **Under query-string API versioning, the route values dictionary MUST include `["api-version"] = ApiVersion`** — otherwise `Location` headers omit the version and 404 on dereference. Example: `o.CreatedAtRoute("Orders_GetById", o => new RouteValueDictionary { ["id"] = o.Id.Value, ["api-version"] = ApiVersion })`. |
| `[RequiresUnreferencedCode] [RequiresDynamicCode] CreatedAtAction(string actionName, Func<TDomain, RouteValueDictionary> routeValues, string? controllerName = null)` | `HttpResponseOptionsBuilder<TDomain>` | MVC equivalent of `CreatedAtAction` — uses `LinkGenerator.GetUriByAction`. **Not trim/AOT-safe**; use `CreatedAtRoute` for AOT scenarios. |
| `EvaluatePreconditions()` | `HttpResponseOptionsBuilder<TDomain>` | On `GET`/`HEAD`, evaluates RFC 9110 conditional headers (`If-Match`, `If-Unmodified-Since`, `If-None-Match`, `If-Modified-Since`) using the configured ETag/LastModified selectors and writes `304 Not Modified` or `412 Precondition Failed` accordingly. On unsafe methods the precondition must be evaluated *before* the mutation. |
| `HonorPrefer()` | `HttpResponseOptionsBuilder<TDomain>` | Opt in to RFC 7240 `Prefer: return=minimal` / `return=representation` handling on `WriteOutcome.Updated`. When **not** called, the `Prefer` request header is completely ignored: the writer never emits `Vary: Prefer` or `Preference-Applied`, and `return=minimal` does **not** short-circuit the body. When called, always emits `Vary: Prefer`; emits `Preference-Applied` only when an honored preference was sent. |
| `WithRange(Func<TDomain, ContentRangeHeaderValue> selector)` | `HttpResponseOptionsBuilder<TDomain>` | Returns `206 Partial Content` with a `Content-Range` header from the selector (returns `200` when the range covers the whole representation). |
| `WithRange(long from, long to, long totalLength)` | `HttpResponseOptionsBuilder<TDomain>` | Static range variant. Clamps `to` to `totalLength - 1`; returns `200` when the range covers the whole resource. |
| `WithErrorMapping(Func<Error, int> mapper)` | `HttpResponseOptionsBuilder<TDomain>` | Per-call mapper for failure responses. Highest precedence. |
| `WithErrorMapping<TError>(int statusCode) where TError : Error` | `HttpResponseOptionsBuilder<TDomain>` | Per-call override for a single error type. Higher precedence than `TrellisAspOptions`. |

### `HttpResponseOptionsBuilder`

**Declaration**

```csharp
public sealed class HttpResponseOptionsBuilder
```

Non-generic builder used for the value-less `Result` overload.

| Signature | Returns | Description |
| --- | --- | --- |
| `Vary(params string[] headers)` | `HttpResponseOptionsBuilder` | Appends headers to `Vary`. |
| `HonorPrefer()` | `HttpResponseOptionsBuilder` | Always emits `Vary: Prefer`. |
| `WithErrorMapping(Func<Error, int> mapper)` | `HttpResponseOptionsBuilder` | Per-call mapper for failure responses. |
| `WithErrorMapping<TError>(int statusCode) where TError : Error` | `HttpResponseOptionsBuilder` | Per-call override for a single error type. |

### `ActionResultAdapterExtensions`

**Declaration**

```csharp
public static class ActionResultAdapterExtensions
```

MVC adapter that wraps an `IResult` in an `ActionResult<T>` so MVC controllers can declare typed return signatures (e.g. `Task<ActionResult<TodoResponse>>`) for OpenAPI/ApiExplorer inference and `[ProducesResponseType<T>]` compatibility. Implementation forwards `ActionResult.ExecuteResultAsync` to `IResult.ExecuteAsync(HttpContext)` via an internal `TrellisActionResult<T>` (which also implements `IConvertToActionResult`).

| Signature | Returns | Description |
| --- | --- | --- |
| `public static ActionResult<T> AsActionResult<T>(this IResult result)` | `ActionResult<T>` | Wraps an `IResult` in a typed `ActionResult<T>`. |
| `public static Task<ActionResult<T>> AsActionResultAsync<T>(this Task<IResult> resultTask)` | `Task<ActionResult<T>>` | Async `Task` overload. |
| `public static ValueTask<ActionResult<T>> AsActionResultAsync<T>(this ValueTask<IResult> resultTask)` | `ValueTask<ActionResult<T>>` | Async `ValueTask` overload. |

### `TrellisAspOptions`

**Declaration**

```csharp
public sealed class TrellisAspOptions
```

Configuration registered via `AddTrellisAsp(...)` that maps domain `Error` types to HTTP status codes.

| Name | Type | Description |
| --- | --- | --- |
| `SystemDefault` | `static TrellisAspOptions` (internal) | Read-only default instance used when DI cannot resolve a configured `TrellisAspOptions` (e.g. the host did not call `AddTrellisAsp`). Internal — not callable from user code. Hosts customize the mappings by passing a configure delegate to `AddTrellisAsp(o => o.MapError<...>(...))`; raw `AddSingleton(new TrellisAspOptions())` is unsupported and will be replaced by the bridge factory the next time `AddTrellisAsp` runs. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public TrellisAspOptions MapError<TError>(int statusCode) where TError : Error` | `TrellisAspOptions` | Overrides or adds an error-type-to-status-code mapping. |
| `internal int GetStatusCode(Error error)` | `int` | Walks the error type hierarchy looking for a mapping; falls back to `500`. Invoked by the response writer. |

Default mappings (closed-ADT): `Error.BadRequest=400`, `Error.Unauthorized=401`, `Error.Forbidden=403`, `Error.NotFound=404`, `Error.MethodNotAllowed=405`, `Error.NotAcceptable=406`, `Error.Conflict=409`, `Error.Gone=410`, `Error.PreconditionFailed=412`, `Error.ContentTooLarge=413`, `Error.UnsupportedMediaType=415`, `Error.RangeNotSatisfiable=416`, `Error.UnprocessableContent=422`, `Error.PreconditionRequired=428`, `Error.TooManyRequests=429`, `Error.InternalServerError=500`, `Error.Unexpected=500`, `Error.NotImplemented=501`, `Error.ServiceUnavailable=503`.

### `RuleViolationProblemDetail`

**Declaration**

```csharp
public sealed record RuleViolationProblemDetail(string Code, string? Detail, string[] Fields);
```

AOT-friendly JSON payload used inside Problem Details `extensions["rules"]` for `Error.UnprocessableContent` rule violations. Application code should treat this as response shape metadata, not as a domain model.

### `AggregateRepresentationValidator<T>`

**Declaration**

```csharp
public sealed class AggregateRepresentationValidator<T> : IRepresentationValidator<T> where T : IAggregate
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public EntityTagValue GenerateETag(T value, string? variantKey = null)` | `EntityTagValue` | Returns `EntityTagValue.Strong(value.ETag)` when `variantKey` is null/empty; otherwise SHA-256 hashes `$"{value.ETag}:{variantKey}"` and returns the first 16 lowercase hex characters as a strong ETag. |

### `IRepresentationValidator<in T>`

**Declaration**

```csharp
public interface IRepresentationValidator<in T>
```

| Signature | Returns | Description |
| --- | --- | --- |
| `EntityTagValue GenerateETag(T value, string? variantKey = null)` | `EntityTagValue` | Generates a representation-specific validator for a domain value and optional variant key (typically the negotiated content type or language). |

### `ETagHelper`

**Declaration**

```csharp
public static class ETagHelper
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static bool IfNoneMatchMatches(IList<EntityTagHeaderValue> ifNoneMatchHeader, string currentETag)` | `bool` | Weak-comparison helper for `If-None-Match`; returns `true` for `*` or any matching opaque tag. |
| `public static bool IfMatchSatisfied(IList<EntityTagHeaderValue> ifMatchHeader, string currentETag)` | `bool` | Strong-comparison helper for `If-Match`; returns `true` for `*` or a matching strong tag. |
| `public static EntityTagValue[]? ParseIfNoneMatch(HttpRequest request)` | `EntityTagValue[]?` | `null` when absent; `[]` when present but unparseable/empty; wildcard for `*`; otherwise the parsed strong/weak tags. |
| `public static DateTimeOffset? ParseIfModifiedSince(HttpRequest request)` | `DateTimeOffset?` | Returns the typed `If-Modified-Since` value. |
| `public static DateTimeOffset? ParseIfUnmodifiedSince(HttpRequest request)` | `DateTimeOffset?` | Returns the typed `If-Unmodified-Since` value. |
| `public static EntityTagValue[]? ParseIfMatch(HttpRequest request)` | `EntityTagValue[]?` | `null` when absent; `[]` when present but empty/only weak; wildcard for `*`; otherwise strong tags only. |

### `IfNoneMatchExtensions`

**Declaration**

```csharp
public static class IfNoneMatchExtensions
```

Create-if-absent guard for unsafe methods (`PUT` / `POST`).

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<T> EnforceIfNoneMatchPrecondition<T>(this Result<T> result, EntityTagValue[]? ifNoneMatchETags)` | `Result<T>` | When `ifNoneMatchETags` contains `*`, replaces a successful result with `Error.PreconditionFailed` (`PreconditionKind.IfNoneMatch`). No-op when the header is absent or the result is already a failure. |
| `public static Task<Result<T>> EnforceIfNoneMatchPreconditionAsync<T>(this Task<Result<T>> resultTask, EntityTagValue[]? ifNoneMatchETags)` | `Task<Result<T>>` | Async `Task` overload. |
| `public static ValueTask<Result<T>> EnforceIfNoneMatchPreconditionAsync<T>(this ValueTask<Result<T>> resultTask, EntityTagValue[]? ifNoneMatchETags)` | `ValueTask<Result<T>>` | Async `ValueTask` overload. |

### `PreferHeader`

**Declaration**

```csharp
public sealed class PreferHeader
```

Parses the RFC 7240 `Prefer` request header. Per RFC 7240 §2 unrecognized or malformed tokens are ignored; duplicate recognized preferences use first-wins behavior.

| Name | Type | Description |
| --- | --- | --- |
| `ReturnRepresentation` | `bool` | `true` for `return=representation`. |
| `ReturnMinimal` | `bool` | `true` for `return=minimal`. |
| `RespondAsync` | `bool` | `true` for `respond-async`. |
| `Wait` | `int?` | Parsed `wait=N` value; `null` when absent or unparseable. |
| `HandlingStrict` | `bool` | `true` for `handling=strict`. |
| `HandlingLenient` | `bool` | `true` for `handling=lenient`. |
| `HasPreferences` | `bool` | `true` when at least one recognized preference was parsed. Unknown preferences do not set this. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static PreferHeader Parse(HttpRequest request)` | `PreferHeader` | Parses the header from the request. |

### `RangeOutcome`

**Declaration**

```csharp
public abstract record RangeOutcome
{
    public sealed record FullRepresentation : RangeOutcome;
    public sealed record PartialContent(long From, long To, long CompleteLength) : RangeOutcome;
    public sealed record NotSatisfiable(long CompleteLength) : RangeOutcome;
}
```

Result of evaluating an RFC 9110 `Range` request.

| Variant | Description |
| --- | --- |
| `FullRepresentation` | No Range header, non-`bytes` unit, multi-range request, malformed range, or non-`GET` method — serve the full representation (`200 OK`). |
| `PartialContent(From, To, CompleteLength)` | Satisfiable single byte range — serve `206 Partial Content` with `Content-Range`. |
| `NotSatisfiable(CompleteLength)` | `from >= completeLength` or `from > to` — serve `416 Range Not Satisfiable` with `Content-Range: bytes */{CompleteLength}`. |

### `RangeRequestEvaluator`

**Declaration**

```csharp
public static class RangeRequestEvaluator
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static RangeOutcome Evaluate(HttpRequest request, long completeLength)` | `RangeOutcome` | Evaluates the `Range` header per RFC 9110 §14. Only supports `bytes`. Non-`GET`, missing header, multi-range, or malformed → `FullRepresentation`. Suffix ranges (`-N`) supported. Throws `ArgumentOutOfRangeException` for negative `completeLength`. |

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
| `public PartialContentHttpResult(long rangeStart, long rangeEnd, long? totalLength, IResult inner)` | — | Builds a `206 Partial Content` result using unit `"items"`, delegating body serialization to `inner`. |
| `public PartialContentHttpResult(ContentRangeHeaderValue contentRangeHeaderValue, IResult inner)` | — | Variant with a caller-built `Content-Range`. |
| `public Task ExecuteAsync(HttpContext httpContext)` | `Task` | Writes `Content-Range`, forces status `206`, then executes `inner`. |

### `PartialContentResult`

**Declaration**

```csharp
public class PartialContentResult : ObjectResult
```

MVC `ObjectResult` companion to `PartialContentHttpResult`.

| Name | Type | Description |
| --- | --- | --- |
| `ContentRangeHeaderValue` | `ContentRangeHeaderValue` | The `Content-Range` header written during formatting. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public PartialContentResult(long rangeStart, long rangeEnd, long? totalLength, object? value)` | — | `206 Partial Content` MVC result using unit `"items"`. |
| `public PartialContentResult(ContentRangeHeaderValue contentRangeHeaderValue, object? value)` | — | Variant with a caller-built `Content-Range`. |
| `public override void OnFormatting(ActionContext context)` | `void` | Writes the `Content-Range` response header before object body formatting. |

### `PagedResponse<TResponse>`

**Declaration**

```csharp
public sealed record PagedResponse<TResponse>(
    IReadOnlyList<TResponse> Items,
    PageLink? Next,
    PageLink? Previous,
    int RequestedLimit,
    int AppliedLimit,
    int DeliveredCount,
    bool WasCapped);
```

JSON envelope returned by the `Result<Page<T>>` overload of `ToHttpResponse`.

### `PageLink`

**Declaration**

```csharp
public sealed record PageLink(string Cursor, string Href);
```

A cursor + the absolute URL the client should follow. Also rendered as `<{Href}>; rel="next"` / `rel="prev"` entries in the response `Link` header.

### `ServiceCollectionExtensions`

**Declaration**

```csharp
public static class ServiceCollectionExtensions
```

The main DI surface for `Trellis.Asp` (in folder `Extensions/`).

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IMvcBuilder AddScalarValueValidation(this IMvcBuilder builder)` | `IMvcBuilder` | Configures MVC JSON options + the `ScalarValueValidationFilter` + a `ScalarValueModelBinderProvider`. Suppresses MVC validation recursion into `Maybe<T>`. |
| `public static IServiceCollection AddScalarValueValidation(this IServiceCollection services)` | `IServiceCollection` | Configures both MVC (`MvcJsonOptions`) and Minimal API (`HttpJsonOptions`) JSON pipelines for scalar-value/`Maybe<T>` support. Idempotent. |
| `public static IApplicationBuilder UseScalarValueValidation(this IApplicationBuilder app)` | `IApplicationBuilder` | Adds `ScalarValueValidationMiddleware` so `ValidatingJsonConverter<TValue,TPrimitive>` can collect errors per request. |
| `public static IServiceCollection AddScalarValueValidationForMinimalApi(this IServiceCollection services)` | `IServiceCollection` | Configures only the Minimal API JSON pipeline. |
| `public static RouteHandlerBuilder WithScalarValueValidation(this RouteHandlerBuilder builder)` | `RouteHandlerBuilder` | Adds `ScalarValueValidationEndpointFilter` to the route handler. |
| `public static IServiceCollection AddTrellisAsp(this IServiceCollection services)` | `IServiceCollection` | Registers `TrellisAspOptions` with default error mappings, then calls `AddScalarValueValidation()`. |
| `public static IServiceCollection AddTrellisAsp(this IServiceCollection services, Action<TrellisAspOptions> configure)` | `IServiceCollection` | Same as above, with a `MapError<TError>(...)` callback for overrides. **Calls compose** — when `AddTrellisAsp(o => ...)` is invoked more than once (e.g. by a library and the application), every `configure` delegate runs in registration order against the same `TrellisAspOptions` instance built lazily by `OptionsFactory<TrellisAspOptions>`. Same-`TError` mappings still follow last-wins, but mappings for different error types from earlier calls are preserved. |

### Namespace `Trellis.Asp.Authorization`

The actor-provider DI surface absorbed from the former `Trellis.Asp.Authorization` package. Domain primitives (`Actor`, `IActorProvider`, etc.) live in `Trellis.Authorization` — see [`trellis-api-authorization.md`](trellis-api-authorization.md).

### `ServiceCollectionExtensions`

**Declaration**

```csharp
public static class ServiceCollectionExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddClaimsActorProvider(this IServiceCollection services, Action<ClaimsActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor`, configures `ClaimsActorOptions`, and **replaces** the `IActorProvider` registration with a scoped `ClaimsActorProvider`. |
| `public static IServiceCollection AddEntraActorProvider(this IServiceCollection services, Action<EntraActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor`, configures `EntraActorOptions`, and **replaces** the `IActorProvider` registration with a scoped `EntraActorProvider`. |
| `public static IServiceCollection AddDevelopmentActorProvider(this IServiceCollection services, Action<DevelopmentActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor` + logging, configures `DevelopmentActorOptions`, and **replaces** the `IActorProvider` registration with a scoped `DevelopmentActorProvider`. The provider itself throws outside the Development environment. |
| `public static IServiceCollection AddCachingActorProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services) where T : class, IActorProvider` | `IServiceCollection` | Registers concrete provider `T` as scoped, then **replaces** the `IActorProvider` registration with a scoped `CachingActorProvider` wrapping `T`. |

> **Replacement semantics.** Each `AddXxxActorProvider` helper calls
> `services.Replace(...)` for the `IActorProvider` slot. Calling more than one
> helper leaves exactly one `IActorProvider` descriptor — the last one wins —
> and `GetServices<IActorProvider>()` returns a single provider. Without
> `Replace`, two helpers would leave two scoped descriptors with surprising
> resolution semantics (single resolve picks the last; enumeration exposes
> both).

### `ClaimsActorOptions`

**Declaration**

```csharp
public class ClaimsActorOptions
```

| Name | Type | Description |
| --- | --- | --- |
| `ActorIdClaim` | `string` | Claim type used for `Actor.Id`. Default: `"sub"`. Matched verbatim against `Claim.Type`; no dotted/JSON-path traversal. |
| `PermissionsClaim` | `string` | Claim type used for permissions. Default: `"permissions"`. Multi-valued JWT claims arrive as repeated `Claim` instances and are aggregated via `FindAll`. |

### `ClaimsActorProvider`

**Declaration**

```csharp
public class ClaimsActorProvider(
    IHttpContextAccessor httpContextAccessor,
    IOptions<ClaimsActorOptions> options) : IActorProvider
```

Hydrates an `Actor` from the current `HttpContext.User` using flat JWT/OIDC claims. Subclass and override `GetCurrentActorAsync` for nested-claim or computed-permission scenarios; `EntraActorProvider` is a worked example.

| Name | Type | Description |
| --- | --- | --- |
| `HttpContextAccessor` | `IHttpContextAccessor` (protected) | Exposed to derived providers. |
| `Options` | `ClaimsActorOptions` (protected) | Mapped options value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public virtual Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Actor>` | Throws `InvalidOperationException` when `HttpContext` is missing, no authenticated identity exists, or the configured `ActorIdClaim` is missing. Permissions come from `FindAll(PermissionsClaim)` snapshotted into a `FrozenSet<string>`; `Actor.Create(actorId, permissions)` is used so forbidden permissions and attributes default to empty. |

### `EntraActorOptions`

**Declaration**

```csharp
public sealed class EntraActorOptions
```

| Name | Type | Description |
| --- | --- | --- |
| `IdClaimType` | `string` | Claim type used for actor ID. Default: `"http://schemas.microsoft.com/identity/claims/objectidentifier"`. |
| `MapPermissions` | `Func<IEnumerable<Claim>, IReadOnlySet<string>>` | Default returns the values of every `roles` / `ClaimTypes.Role` claim (case-insensitive type match). |
| `MapForbiddenPermissions` | `Func<IEnumerable<Claim>, IReadOnlySet<string>>` | Default returns an empty `HashSet<string>`. |
| `MapAttributes` | `Func<IEnumerable<Claim>, HttpContext, IReadOnlyDictionary<string, string>>` | Default extracts `tid`, `preferred_username`, `azp`, `azpacr`, `acrs`, plus `ip_address` from `Connection.RemoteIpAddress` and `mfa = "true"|"false"` from the `amr` claim. |

### `EntraActorProvider`

**Declaration**

```csharp
public sealed class EntraActorProvider : ClaimsActorProvider
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public EntraActorProvider(IHttpContextAccessor httpContextAccessor, IOptions<EntraActorOptions> options)` | — | Builds the Entra-specific provider; passes `ActorIdClaim = options.Value.IdClaimType` and `PermissionsClaim = "roles"` to the base. |
| `public override Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Actor>` | Throws the same missing-context / missing-identity failures as `ClaimsActorProvider`. When `IdClaimType` is the long objectidentifier claim, falls back to the short `"oid"` claim before failing. Wraps any exception from `MapPermissions`, `MapForbiddenPermissions`, or `MapAttributes` in `InvalidOperationException`. |

### `DevelopmentActorOptions`

**Declaration**

```csharp
public sealed class DevelopmentActorOptions
```

| Name | Type | Description |
| --- | --- | --- |
| `DefaultActorId` | `string` | Default fallback actor ID. Default: `"development"`. |
| `DefaultPermissions` | `IReadOnlySet<string>` | Default fallback permissions when no header is supplied. Default: empty `HashSet<string>`. |
| `ThrowOnMalformedHeader` | `bool` | When `true`, malformed `X-Test-Actor` JSON throws instead of falling back to the default actor. Default: `false`. |

### `DevelopmentActorProvider`

**Declaration**

```csharp
public sealed partial class DevelopmentActorProvider(
    IHttpContextAccessor httpContextAccessor,
    IHostEnvironment hostEnvironment,
    IOptions<DevelopmentActorOptions> options,
    ILogger<DevelopmentActorProvider> logger) : IActorProvider
```

Reads the `X-Test-Actor` header (JSON: `{ "Id": ..., "Permissions": [...], "ForbiddenPermissions": [...], "Attributes": {...} }`, case-insensitive property matching).

| Signature | Returns | Description |
| --- | --- | --- |
| `public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Actor>` | Throws `InvalidOperationException` whenever `!hostEnvironment.IsDevelopment()`, regardless of header presence. In Development, returns `Actor.Create(DefaultActorId, DefaultPermissions)` when `HttpContext` is null or the header is missing/empty. Malformed JSON logs a warning and falls back unless `ThrowOnMalformedHeader` is `true`. |

### `CachingActorProvider`

**Declaration**

```csharp
public sealed class CachingActorProvider : IActorProvider
```

Decorator that caches the inner provider's resolution task per request scope using `LazyInitializer.EnsureInitialized`. The shared task uses `HttpContext.RequestAborted` so expensive work (DB lookups) is canceled with the request, but individual callers' tokens only cancel their own awaits.

| Signature | Returns | Description |
| --- | --- | --- |
| `public CachingActorProvider(IActorProvider inner, IHttpContextAccessor httpContextAccessor)` | — | `inner` cannot be null. |
| `public Task<Actor> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Actor>` | Returns the cached task; if `cancellationToken` differs from `RequestAborted`, applies it via `Task.WaitAsync`. |

### Namespace `Trellis.Asp.ModelBinding`

### `ScalarValueModelBinderBase<TResult, TValue, TPrimitive>`

**Declaration**

```csharp
public abstract class ScalarValueModelBinderBase<TResult, TValue, TPrimitive> : IModelBinder
    where TValue : IScalarValue<TValue, TPrimitive>
    where TPrimitive : IComparable
```

| Signature | Returns | Description |
| --- | --- | --- |
| `protected abstract ModelBindingResult OnMissingValue()` | `ModelBindingResult` | Called when no raw value is present in the value provider. |
| `protected virtual ModelBindingResult? OnEmptyValue() => null` | `ModelBindingResult?` | Called when the raw value is an empty string; return `null` to fall through to normal conversion. For string-typed scalar VOs (`TPrimitive == string`), the empty string is forwarded to `TValue.TryCreate("")` so the value object decides whether empty input is valid. For non-string primitives, empty strings continue to fail with the standard "value is required" message before reaching `TryCreate`. |
| `protected abstract ModelBindingResult OnSuccess(TValue value)` | `ModelBindingResult` | Wraps a validated scalar value into the final binding result. |
| `public Task BindModelAsync(ModelBindingContext bindingContext)` | `Task` | Reads the raw value, converts to `TPrimitive`, calls `TValue.TryCreate`, and populates `ModelState` on failure. |

### `ScalarValueModelBinder<TValue, TPrimitive>`

**Declaration**

```csharp
public class ScalarValueModelBinder<TValue, TPrimitive>
    : ScalarValueModelBinderBase<TValue, TValue, TPrimitive>
```

| Signature | Returns | Description |
| --- | --- | --- |
| `protected override ModelBindingResult OnMissingValue()` | `ModelBindingResult` | Leaves the binding result unset (`default`). |
| `protected override ModelBindingResult OnSuccess(TValue value)` | `ModelBindingResult` | Returns `ModelBindingResult.Success(value)`. |

### `MaybeModelBinder<TValue, TPrimitive>`

**Declaration**

```csharp
public class MaybeModelBinder<TValue, TPrimitive>
    : ScalarValueModelBinderBase<Maybe<TValue>, TValue, TPrimitive>
```

| Signature | Returns | Description |
| --- | --- | --- |
| `protected override ModelBindingResult OnMissingValue()` | `ModelBindingResult` | Returns `ModelBindingResult.Success(Maybe<TValue>.None)`. |
| `protected override ModelBindingResult? OnEmptyValue()` | `ModelBindingResult?` | Returns `ModelBindingResult.Success(Maybe<TValue>.None)`. |
| `protected override ModelBindingResult OnSuccess(TValue value)` | `ModelBindingResult` | Returns `ModelBindingResult.Success(Maybe.From(value))`. |

### `ScalarValueModelBinderProvider`

**Declaration**

```csharp
public class ScalarValueModelBinderProvider : IModelBinderProvider
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public IModelBinder? GetBinder(ModelBinderProviderContext context)` | `IModelBinder?` | Returns a `MaybeModelBinder<,>` for `Maybe<TScalar>`, a `ScalarValueModelBinder<,>` for direct scalar values, or `null` otherwise. Annotated `[UnconditionalSuppressMessage]` for IL2070/IL2072/IL2075 and IL3050 — model binding is not Native AOT compatible. |

### Namespace `Trellis.Asp.Routing`

### `TrellisValueObjectRouteConstraint<T>`

**Declaration**

```csharp
public sealed class TrellisValueObjectRouteConstraint<T> : IRouteConstraint
    where T : IParsable<T>
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public bool Match(HttpContext?, IRouter?, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)` | `bool` | Delegates to `T.TryParse(..., CultureInfo.InvariantCulture, out _)`. Returns `false` when the route value is missing, null, or fails to parse. |

### `RouteConstraintRegistrationExtensions`

**Declaration**

```csharp
public static class RouteConstraintRegistrationExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddTrellisRouteConstraints(this IServiceCollection services, params Assembly[] assemblies)` | `IServiceCollection` | Scans the supplied assemblies (or the calling assembly + the assembly containing `IScalarValue<,>` from `Trellis.Core` if none are supplied) for value objects implementing both `IScalarValue<TSelf, TPrimitive>` and `IParsable<TSelf>`, then registers a `TrellisValueObjectRouteConstraint<T>` under the type's simple name. Existing entries in `RouteOptions.ConstraintMap` are preserved. Reflection-based — not Native AOT compatible. |
| `public static IServiceCollection AddTrellisRouteConstraint<T>(this IServiceCollection services, string? constraintName = null) where T : IParsable<T>` | `IServiceCollection` | Registers a single value-object route constraint without reflection. AOT-safe. |

Once registered, route templates such as `"/products/{id:ProductId}"` parse and bind the segment via the value object's `IParsable<T>.TryParse` implementation.

### Namespace `Trellis.Asp.Validation`

### `ScalarValueJsonConverterBase<TResult, TValue, TPrimitive>`

**Declaration**

```csharp
public abstract class ScalarValueJsonConverterBase<TResult, TValue, TPrimitive>
    : JsonConverter<TResult>
    where TValue : class, IScalarValue<TValue, TPrimitive>
    where TPrimitive : IComparable
```

| Name | Type | Description |
| --- | --- | --- |
| `HandleNull` | `bool` (override) | Always `true`; forces `System.Text.Json` to call `Read(...)` for JSON `null` tokens. |

| Signature | Returns | Description |
| --- | --- | --- |
| `protected abstract TResult OnNullToken(string fieldName)` | `TResult` | Returns the deserialization result for a JSON `null` token. |
| `protected abstract TResult WrapSuccess(TValue value)` | `TResult` | Wraps a validated scalar value into the final converter result. |
| `protected abstract TResult OnValidationFailure()` | `TResult` | Returns the failure result after a validation error has been collected into `ValidationErrorsContext`. |
| `public override TResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)` | `TResult` | Reads the primitive JSON value, calls `TValue.TryCreate`, collects errors into `ValidationErrorsContext`, and returns the derived-type wrapper. |
| `protected static string GetDefaultFieldName()` | `string` | Returns the camel-cased scalar type name used when no property name is available. |

### `ValidatingJsonConverter<TValue, TPrimitive>`

**Declaration**

```csharp
public sealed class ValidatingJsonConverter<TValue, TPrimitive>
    : ScalarValueJsonConverterBase<TValue?, TValue, TPrimitive>
```

| Signature | Returns | Description |
| --- | --- | --- |
| `protected override TValue? OnNullToken(string fieldName)` | `TValue?` | Adds `"{TypeName} cannot be null."` to `ValidationErrorsContext` and returns `null`. |
| `protected override TValue? WrapSuccess(TValue value)` | `TValue?` | Returns the validated scalar value. |
| `protected override TValue? OnValidationFailure()` | `TValue?` | Returns `null`. |
| `public override void Write(Utf8JsonWriter writer, TValue? value, JsonSerializerOptions options)` | `void` | Writes JSON `null` for `null`; otherwise writes the underlying primitive `value.Value`. |

### `ValidatingJsonConverterFactory`

**Declaration**

```csharp
public sealed class ValidatingJsonConverterFactory : JsonConverterFactory
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public override bool CanConvert(Type typeToConvert)` | `bool` | `true` when `typeToConvert` is a scalar value type. |
| `public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)` | `JsonConverter?` | Builds a `ValidatingJsonConverter<TValue, TPrimitive>` for supported scalar value types. Annotated `[RequiresDynamicCode]` — `JsonConverterFactory` is not Native AOT compatible. |

### `MaybeScalarValueJsonConverter<TValue, TPrimitive>`

**Declaration**

```csharp
public sealed class MaybeScalarValueJsonConverter<TValue, TPrimitive>
    : ScalarValueJsonConverterBase<Maybe<TValue>, TValue, TPrimitive>
```

| Signature | Returns | Description |
| --- | --- | --- |
| `protected override Maybe<TValue> OnNullToken(string fieldName)` | `Maybe<TValue>` | Returns `Maybe<TValue>.None`; JSON `null` is valid for optional scalar values. |
| `protected override Maybe<TValue> WrapSuccess(TValue value)` | `Maybe<TValue>` | Returns `Maybe.From(value)`. |
| `protected override Maybe<TValue> OnValidationFailure()` | `Maybe<TValue>` | Returns `Maybe<TValue>.None`. |
| `public override void Write(Utf8JsonWriter writer, Maybe<TValue> value, JsonSerializerOptions options)` | `void` | Writes JSON `null` for `Maybe.None`; otherwise writes the wrapped primitive `value.Value.Value`. |

### `MaybeScalarValueJsonConverterFactory`

**Declaration**

```csharp
public sealed class MaybeScalarValueJsonConverterFactory : JsonConverterFactory
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public override bool CanConvert(Type typeToConvert)` | `bool` | `true` when `typeToConvert` is `Maybe<T>` and `T` is a scalar value type. |
| `public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)` | `JsonConverter?` | Builds `MaybeScalarValueJsonConverter<TValue, TPrimitive>` for supported `Maybe<TScalar>` types. |

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
| `public void OnActionExecuted(ActionExecutedContext context)` | `void` | No-op. |

### `ScalarValueValidationEndpointFilter`

**Declaration**

```csharp
public sealed class ScalarValueValidationEndpointFilter : IEndpointFilter
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)` | `ValueTask<object?>` | For Minimal APIs, returns `Results.ValidationProblem(validationError.ToDictionary())` when `ValidationErrorsContext` contains errors; otherwise invokes `next`. |

### `ScalarValueValidationMiddleware`

**Declaration**

```csharp
public sealed class ScalarValueValidationMiddleware
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public ScalarValueValidationMiddleware(RequestDelegate next)` | — | Wraps each request in `ValidationErrorsContext.BeginScope()`. |
| `public Task InvokeAsync(HttpContext context)` | `Task` | Begins a validation scope, invokes the next middleware, and converts scalar-value `BadHttpRequestException` binding failures into validation problem responses using endpoint parameter metadata plus route/query raw values. |

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
| `public static Error.UnprocessableContent? GetUnprocessableContent()` | `Error.UnprocessableContent?` | Returns the aggregated `Error.UnprocessableContent` for the current scope (with `Fields` / `Rules` populated from collected `FieldViolation`s) or `null` when no errors were collected. |

## Behavioral notes

- **One verb, every shape.** `ToHttpResponse` is the only supported response mapper. The internal result types it constructs (`TrellisHttpResult<TDomain, TBody>`, `TrellisWriteOutcomeResult<TDomain, TBody>`, `TrellisErrorOnlyResult`, `TrellisEmptyResult`) implement `IResult`, the `IStatusCodeHttpResult` / `IValueHttpResult<T>` / `IContentTypeHttpResult` metadata interfaces, and `IEndpointMetadataProvider` so OpenAPI/ApiExplorer surfaces the success status, body type, and the union of error envelopes the writer can emit (`200`, `201`, `206`, `304`, `400`, `404`, `412`, `500`). Layer your own `[ProducesResponseType]` / `Produces<T>` on top.
- **Failures use Problem Details.** A failure runs through `ResponseFailureWriter` (internal). It calls `Results.ValidationProblem(...)` for `Error.UnprocessableContent` with field violations (the `errors` dictionary keys are the violation `Field.Path` translated from RFC 6901 JSON Pointer to ASP.NET Core MVC dot+bracket convention — `/items/0/name` becomes `items[0].name`, RFC 6901 escapes `~1`/`~0` are decoded, empty reference tokens become `[""]` so `""` (root) and `/` stay distinct on the wire; values are `Detail ?? ReasonCode`), and `Results.Problem(...)` for everything else. Every Trellis.Asp `ValidationProblem` emitter — `ResponseFailureWriter`, `ModelStateExtensions.AddResultErrors`, `ScalarValueValidationFilter` (MVC), `ScalarValueValidationEndpointFilter` (Minimal API), `ScalarValueTypeHelper.GetValidationErrors`, and `ScalarValueValidationMiddleware` (which translates `JsonException.Path` from JSON Path notation `$.items[0].name` to MVC `items[0].name` and uses an empty-string key for root or generic 400 fallbacks) — emits errors keyed in the same MVC dot+bracket shape, so JSON-deserialization 400s and ROP 422s share a single key shape on the wire. The translation is intentionally aligned with MVC's de facto convention and is therefore lossy for two edge cases: a numeric reference token always becomes `[N]` (matching what `[ApiController]` emits for both `List<T>` and `Dictionary<int,T>` access — the schema is not available at translation time); and segment names containing `.`/`[`/`]` collapse with structurally distinct pointers. C# property names reflected by FluentValidation cannot contain these characters, but `InputPointer.ForProperty` only escapes `~` and `/` per RFC 6901, so any caller that passes a `propertyName` containing `.`/`[`/`]` (for example a custom adapter mapping a dotted path to a flat field) will produce a single-segment pointer that collides with structurally distinct multi-segment pointers under this translation. Raw JSON Pointer values are preserved per-rule under `extensions["rules"][n].fields[]` only when the producer also supplied `RuleViolation`s; an `Error.UnprocessableContent` containing only `FieldViolation`s carries the translated MVC key as its sole on-wire identifier and has no escape hatch, so producers needing path fidelity for a flat field violation must also emit a corresponding `RuleViolation`. Companion headers are emitted automatically: `Allow` for `Error.MethodNotAllowed`, `Retry-After` for `Error.TooManyRequests` / `Error.ServiceUnavailable` (when a delay is configured), and `Content-Range: {Unit} */{CompleteLength}` for `Error.RangeNotSatisfiable`. Extensions always carry `code` and `kind`; `Error.InternalServerError` adds `faultId`; rule violations are surfaced under `rules`. For `5xx` responses the `Detail` is always replaced with `"An internal error occurred."` so internal diagnostics never leak to clients.
- **Status code resolution precedence.** `WithErrorMapping(Func<Error, int>)` (per call) → `WithErrorMapping<TError>(int)` (per call, walks the type hierarchy) → `TrellisAspOptions` resolved from `HttpContext.RequestServices` (or `TrellisAspOptions.SystemDefault` if none registered) → `500 Internal Server Error`.
- **Conditional requests.** `EvaluatePreconditions()` runs only on `GET` / `HEAD` and only when at least one of `WithETag` / `WithLastModified` is configured. The internal `ConditionalRequestEvaluator` evaluates RFC 9110 preconditions in this order: `If-Match` (strong); else `If-Unmodified-Since`; then `If-None-Match` (weak); else `If-Modified-Since` for safe methods. Failed `If-Match` / `If-Unmodified-Since` → `412`; failed `If-None-Match` / `If-Modified-Since` on `GET`/`HEAD` → `304`.
- **`Vary` is append-only.** Both the `HonorPrefer()` switch and `Vary(...)` use `AppendVaryUnique` — they preserve any pre-existing `Vary` values added by other middleware and skip duplicates (case-insensitive).
- **`HonorPrefer()` semantics on `WriteOutcome.Updated`.** `HonorPrefer()` is opt-in. Without it, `Prefer` request headers are ignored entirely: no `Vary: Prefer`, no `Preference-Applied`, and `return=minimal` does **not** suppress the body. When `HonorPrefer()` is configured, `Prefer: return=minimal` short-circuits to `204 No Content` and emits `Preference-Applied: return=minimal`; `return=representation` returns `200 OK` with the body and emits `Preference-Applied: return=representation`. `Vary: Prefer` is always emitted under `HonorPrefer()`, regardless of which preference was sent.
- **Range mapping.** `WithRange` returns `200 OK` (full body) when the configured range covers the whole representation; otherwise `206 Partial Content` with `Content-Range`. The static-range overload clamps `to` to `totalLength - 1`; the selector overload trusts the provided `ContentRangeHeaderValue`.
- **`CreatedAtAction` is not AOT-safe.** It depends on MVC's `ControllerLinkGeneratorExtensions`. The builder method, the writer's `ResolveActionLocation` private, and the `LocationKind.Action` branch are annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Use `CreatedAtRoute` with a named route for trim/AOT scenarios; `ResolveActionLocation` throws `NotSupportedException` when `RuntimeFeature.IsDynamicCodeSupported` is `false`.
- **Pagination.** The `Result<Page<T>>` overload always emits the `PagedResponse<TBody>` envelope; the RFC 8288 `Link` header is added only when `Page.Next` and/or `Page.Previous` cursors are present. Failure on the page result short-circuits through the standard error pipeline.
- **Validation collection scope.** `ScalarValueValidationMiddleware` opens a `ValidationErrorsContext` scope per request. Both `ValidatingJsonConverter<,>` and `MaybeScalarValueJsonConverter<,>` collect errors into this scope; `ScalarValueValidationFilter` (MVC) and `ScalarValueValidationEndpointFilter` (Minimal API) short-circuit with a validation problem when the scope is non-empty at action/handler entry.
- **Minimal API scalar binding failures are metadata-driven.** When ASP.NET Core throws a 400 while binding route/query parameters, `ScalarValueValidationMiddleware` no longer parses `BadHttpRequestException.Message` to discover a field name or invalid value. It inspects `IParameterBindingMetadata`, reads the matching route/query raw value, and re-runs Trellis scalar validation for `IScalarValue<,>` / `Maybe<TScalar>` parameters. Non-scalar endpoint binding failures are rethrown to ASP.NET Core.
- **`AddTrellisAsp` is the one-call setup.** It registers `TrellisAspOptions` and chains `AddScalarValueValidation()`, configuring both the MVC and Minimal API JSON pipelines for scalar-value/`Maybe<T>` deserialization. You still need `UseScalarValueValidation()` middleware in the request pipeline and `WithScalarValueValidation()` on each Minimal API endpoint that should short-circuit on validation errors.
- **Composite value objects in request/response DTOs.** `AddTrellisAsp`/`AddScalarValueValidation` only wires the **scalar** VO converters. Composite VOs (multi-field `[OwnedEntity]` types like `ShippingAddress`, `Money`) bind through `CompositeValueObjectJsonConverter<T>` (in `Trellis.Primitives`), which is **opt-in per type** via `[JsonConverter(typeof(CompositeValueObjectJsonConverter<MyVo>))]` on the value object class itself. Without that attribute, model binding falls back to default construction and **silently bypasses `TryCreate`** — the inner-field validation never runs and an invalid payload propagates into the domain layer. See [Cookbook Recipe 13](trellis-api-cookbook.md#recipe-13--composite-value-object-end-to-end-domain--api-json-binding--ef-core-ownership) for the full Domain + API JSON + EF pattern.

## Code examples

### Basic `Result<T>` → 200 / Problem Details

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrellisAsp();

var app = builder.Build();
app.UseScalarValueValidation();

app.MapGet("/widgets/{id}", (string id) =>
{
    Result<Widget> result = WidgetService.Get(id);
    return result.ToHttpResponse(opts => opts
        .WithETag(w => w.ETag)
        .WithLastModified(w => w.UpdatedAt)
        .EvaluatePreconditions());
}).WithScalarValueValidation();

app.Run();
```

### `WriteOutcome<T>` with Prefer / Created

```csharp
app.MapPost("/widgets", async (CreateWidget cmd, IWidgetWriter writer, CancellationToken ct) =>
{
    Result<WriteOutcome<Widget>> result = await writer.CreateAsync(cmd, ct);
    return result.ToHttpResponse(
        body: w => new WidgetResponse(w.Id, w.Name),
        configure: opts => opts
            .CreatedAtRoute("widgets.get", w => new RouteValueDictionary { ["id"] = w.Id })
            .WithETag(w => w.ETag)
            .HonorPrefer());
});
```

### Paginated `Result<Page<T>>`

```csharp
app.MapGet("/widgets", async (string? cursor, int? limit, IWidgetReader reader, HttpContext ctx) =>
{
    Result<Page<Widget>> page = await reader.ListAsync(cursor, limit ?? 50, ctx.RequestAborted);

    return page.ToHttpResponse(
        nextUrlBuilder: (c, applied) =>
            $"{ctx.Request.Scheme}://{ctx.Request.Host}/widgets?cursor={c.Token}&limit={applied}",
        body: w => new WidgetResponse(w.Id, w.Name));
});
```

### MVC controller using `AsActionResult<T>` for typed signatures

```csharp
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;

[ApiController]
[Route("widgets")]
public sealed class WidgetsController(IWidgetReader reader) : ControllerBase
{
    [HttpGet("{id}", Name = "widgets.get")]
    [ProducesResponseType<WidgetResponse>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<WidgetResponse>> Get(string id, CancellationToken ct)
    {
        Result<Widget> result = await reader.GetAsync(id, ct);
        return await result
            .ToHttpResponseAsync(w => new WidgetResponse(w.Id, w.Name))
            .AsActionResultAsync<WidgetResponse>();
    }
}
```

### Per-call error mapping override

```csharp
return result.ToHttpResponse(opts => opts
    .WithErrorMapping<DomainConflict>(StatusCodes.Status409Conflict)
    .WithErrorMapping(err => err is OutOfStockError ? 410 : default));
```

### Actor providers

`AddXxxActorProvider` helpers all `Replace` the `IActorProvider` slot — only the **last** call wins. The two clean composition patterns:

**Pattern A — select one provider per environment:**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Trellis.Asp.Authorization;

var services = new ServiceCollection();

if (env.IsDevelopment())
{
    services.AddDevelopmentActorProvider(opts =>
    {
        opts.DefaultActorId = "development";
        opts.DefaultPermissions = new HashSet<string> { "orders:read", "orders:create" };
    });
}
else
{
    services.AddEntraActorProvider(opts =>
    {
        opts.MapPermissions = claims => claims
            .Where(c => string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value)
            .ToHashSet();
    });
}
```

**Pattern B — wrap the chosen inner provider with caching:**

```csharp
// First register the inner provider, then wrap with caching.
// Each AddCachingActorProvider<T>() call replaces the IActorProvider slot
// with CachingActorProvider over T. The inner T is registered idempotently
// via TryAddScoped<T>() so library + application calls do not duplicate.
services.AddEntraActorProvider(opts => { /* ... */ });
services.AddCachingActorProvider<EntraActorProvider>();
```

Do **not** chain multiple `AddXxxActorProvider` calls expecting them to coexist or fall back — the last one always wins. If you need different providers in different environments, branch the registration code as in Pattern A.

### Route constraints for scalar value objects

```csharp
// AOT-safe — explicit registration
services.AddTrellisRouteConstraint<ProductId>("ProductId");

// Reflection-based — scans the calling assembly
services.AddTrellisRouteConstraints();

app.MapGet("/products/{id:ProductId}", (ProductId id) => Results.Ok(id));
```

## Cross-references

- [trellis-api-core.md](trellis-api-core.md) — `Result`, `Result<T>`, `Error`, `WriteOutcome<T>`, `Page<T>`, `RepresentationMetadata`, `EntityTagValue`, `Cursor`, `PreconditionKind`, `ResourceRef`.
- [trellis-api-authorization.md](trellis-api-authorization.md) — `Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<TResource>`, resource loaders.
- [trellis-api-primitives.md](trellis-api-primitives.md) — `IScalarValue<TSelf, TPrimitive>`, `Maybe<T>`.
- [trellis-api-http.md](trellis-api-http.md) — Pure HTTP value objects shared between hosts.
- [trellis-api-testing-aspnetcore.md](trellis-api-testing-aspnetcore.md) — `WebApplicationFactoryExtensions.CreateClientWithActor` (writes the `X-Test-Actor` header consumed by `DevelopmentActorProvider`).
