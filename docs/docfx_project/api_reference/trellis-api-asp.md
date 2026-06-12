---
package: Trellis.Asp
namespaces: [Trellis.Asp, Trellis.Asp.Authorization, Trellis.Asp.Idempotency, Trellis.Asp.ModelBinding, Trellis.Asp.Routing, Trellis.Asp.Validation]
types: [TrellisHttpResult, ToHttpResponse, AsActionResult, HttpResponseOptionsBuilder<T>, CacheControl, MaybePrimitiveJsonConverter<T>, MaybePrimitiveJsonConverterFactory, MaybePrimitiveModelBinder<T>, MaybePrimitives, IProvideActorVaryHeaders, ClaimsActorProvider, NestedJsonPathClaimsActorOptions, NestedJsonPathClaimsActorProvider, EntraActorProvider, DevelopmentActorProvider, CachingActorProvider, AddTrellisProblemDetails, UseTrellisProblemDetails, ResourceCollectionNameRegistry, ResourceCollectionNameOverride, AddResourceCollectionName, AddResourceCollectionNames, IdempotentAttribute, IdempotencyOptions, IIdempotencyStore, InMemoryIdempotencyStore, IIdempotencyScopeResolver, DefaultIdempotencyScopeResolver, AnonymousIdempotencyScopeResolver, ActorIdempotencyScopeResolver, IdempotencyReservationOutcome, IdempotencyResponseSnapshot, IdempotencyKeyParser, IdempotencyFingerprint, CapturingResponseBodyFeature, IdempotencyMiddleware, AddTrellisIdempotency, AddInMemoryIdempotencyStore, UseTrellisIdempotency]
version: v3
last_verified: 2026-06-03
audience: [llm]
---
# Trellis.Asp — API Reference

**Package:** `Trellis.Asp` (bundles the AOT-friendly `Trellis.AspSourceGenerator.dll` at `analyzers/dotnet/cs/` — installing `Trellis.Asp` attaches the generator automatically — and contains the ASP.NET actor providers formerly published as `Trellis.Asp.Authorization`).
**Namespaces:** `Trellis.Asp`, `Trellis.Asp.Authorization`, `Trellis.Asp.Idempotency`, `Trellis.Asp.ModelBinding`, `Trellis.Asp.Routing`, `Trellis.Asp.Validation`
**Purpose:** ASP.NET Core integration for mapping Trellis `Result`/`Result<T>`/`WriteOutcome<T>`/`Page<T>` values to HTTP responses, evaluating HTTP preconditions and `Prefer` preferences, hydrating actors from JWT claims, validating scalar value objects in MVC and Minimal APIs, and emitting AOT-friendly `JsonConverter`s for Trellis scalar values.

The single supported response verb is `result.ToHttpResponse(...)`. It returns `Microsoft.AspNetCore.Http.IResult` and works in both Minimal API and MVC hosts (.NET 7+ executes `IResult` natively in MVC). For typed `ActionResult<T>` signatures, chain `.AsActionResult<T>()`. Configure protocol semantics via the fluent `HttpResponseOptionsBuilder<T>` (`WithETag`, `WithLastModified`, `Vary`, `WithCacheControl`, `Created`/`CreatedAtRoute`/`CreatedAtAction`, `EvaluatePreconditions`, `HonorPrefer`, `WithErrorMapping`, …).

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#task---recipe-lookup) — recipes using this package.

## Use this file when

- You are wiring ASP.NET Core endpoints/controllers that return Trellis `Result`, `Result<T>`, `WriteOutcome<T>`, or `Page<T>`.
- You need the exact response-mapping verb, status-code behavior, Problem Details mapping, ETag / preference handling, actor-provider setup, scalar value-object binding, or route constraints.
- You are implementing API surface polish: failure response metadata, versioned `Location` headers, or tests proving `Error.InvalidInput` maps to 422.

## Patterns Index

| Goal | Canonical API / action | See |
|---|---|---|
| Enable Trellis Result-to-HTTP mapping | Call `builder.Services.AddTrellisAsp()` or `services.AddTrellis(o => o.UseAsp())` in the composition root. Exception middleware is only a 500 fallback; it does not map `Result` failures. | [`ServiceCollectionExtensions`](#servicecollectionextensions), [ServiceDefaults](trellis-api-servicedefaults.md#trellisservicebuilder) |
| Return a Minimal API result | `return result.ToHttpResponse(...)` | [`HttpResponseExtensions`](#httpresponseextensions) |
| Return an MVC typed action result | Convert first, then adapt: `return result.ToHttpResponse(...).AsActionResult<T>()` or `return await result.ToHttpResponseAsync(...).AsActionResultAsync<T>()` | [`ActionResultAdapterExtensions`](#actionresultadapterextensions) |
| Configure 201 Created | `.ToHttpResponse(o => o.Created(...))`, `.CreatedAtRoute(...)`, or `.CreatedAtAction(...)` | [`HttpResponseOptionsBuilder<TDomain>`](#httpresponseoptionsbuildertdomain) |
| Generate versioned `Location` headers | **Required when query-string API versioning is enabled.** Include the API version in builder route values (`CreatedAtRoute`, `CreatedAtAction`, `WithLocation`) or chain `.WithVersionedRoute()` from `Trellis.Asp.ApiVersioning`. Omitting it produces `Location` headers that 404 on dereference. | [`HttpResponseOptionsBuilder<TDomain>`](#httpresponseoptionsbuildertdomain) |
| Map failure codes globally | Configure `TrellisAspOptions.ErrorStatusCodeMap` through `AddTrellisAsp(...)` | [`TrellisAspOptions`](#trellisaspoptions) |
| Override failure mapping for one endpoint | `.WithErrorMapping(...)` / `.WithErrorMapping<TError>(statusCode)` | [`HttpResponseOptionsBuilder<TDomain>`](#httpresponseoptionsbuildertdomain) |
| Document endpoint failure codes | Add ASP.NET response metadata for every spec-listed failure status (`422`, `409`, `403`, `404`, etc.) in addition to happy-path metadata. | [Code examples](#code-examples) |
| Add ETag / conditional GET | `.WithETag(...)`, `.WithLastModified(...)`, `.EvaluatePreconditions()` | [`HttpResponseOptionsBuilder<TDomain>`](#httpresponseoptionsbuildertdomain), [`ETagHelper`](#etaghelper) |
| Add `Cache-Control` directive (per endpoint) | `.WithCacheControl(CacheControl.NoStore())` / `.WithCacheControl(CacheControl.Public(TimeSpan.FromMinutes(5)))` / `.WithCacheControl(t => …)` | [`HttpResponseOptionsBuilder<TDomain>`](#httpresponseoptionsbuildertdomain), [`CacheControl`](#cachecontrol) |
| Honor `Prefer: return=minimal` | `.HonorPrefer()` on write responses | [`HttpResponseOptionsBuilder<TDomain>`](#httpresponseoptionsbuildertdomain) |
| Return paginated list responses | `Result<Page<T>>.ToHttpResponse(nextUrlBuilder, bodySelector, ...)` | [`PagedResponse<TResponse>`](#pagedresponsetresponse) |
| Resolve actors from requests | `AddClaimsActorProvider`, `AddNestedJsonPathClaimsActorProvider`, `AddEntraActorProvider`, or `AddDevelopmentActorProvider`. For microservices consuming gateway-minted internal JWTs, see [`Trellis.Microservices.AspNetCore`](https://github.com/xavierjohn/Trellis.Microservices) (the `TrellisInternalJwtActorProvider` types moved out of this repo in v3 cleanup). | [`Trellis.Asp.Authorization`](#namespace-trellisaspauthorization) |
| Compose a system actor for background workers | `AddTrellisWorkerActor` | [`Trellis.Asp.Authorization`](#namespace-trellisaspauthorization) |
| Bind scalar value objects from routes/query/body | `AddTrellisAspWithScalarValidation()` (or `AddTrellisAsp()` + `AddScalarValueValidation()`), plus route constraints / validation middleware as needed | [`Trellis.Asp.ModelBinding`](#namespace-trellisaspmodelbinding), [`Trellis.Asp.Validation`](#namespace-trellisaspvalidation) |
| Add Trellis ProblemDetails recipe (trace id from `Activity.Current`, friendly 500 detail, `allow` extension on 405) | `services.AddTrellisProblemDetails()` plus `app.UseTrellisProblemDetails()` (or `options.UseProblemDetails()` via [`Trellis.ServiceDefaults`](trellis-api-servicedefaults.md#trellisservicebuilder)) | [`ServiceCollectionExtensions`](#servicecollectionextensions), [`ApplicationBuilderExtensions`](#applicationbuilderextensions) |
| Add the IETF `Idempotency-Key` middleware to opted-in `POST` / `PATCH` endpoints | `services.AddTrellisIdempotency(...)` (or `options.UseIdempotency(...)`), `services.AddInMemoryIdempotencyStore()`, `app.UseTrellisIdempotency()`, and mark each opted-in endpoint with `[Idempotent]` | [`Trellis.Asp.Idempotency`](#namespace-trellisaspidempotency), Cookbook [Recipe 29](trellis-api-cookbook.md#recipe-29--ietf-idempotency-key-middleware-on-post--patch-with-usetrellisidempotency) |

## Endpoint checklist for generated APIs

- Composition root calls `AddTrellisAsp()` or `UseAsp()`.
- Every endpoint that returns a Trellis `Result` ultimately calls `ToHttpResponse` / `AsActionResult`.
- OpenAPI metadata includes the success code and every failure code listed by the product spec.
- `201 Created` endpoints include a usable `Location` header. **Under query-string API versioning, include `["api-version"] = ApiVersion` in builder route values** for `CreatedAtRoute` / `CreatedAtAction` (or chain `.WithVersionedRoute()` from `Trellis.Asp.ApiVersioning`). Forgetting this is a silent `Location`-404 bug — tests pass and OpenAPI looks fine, but clients following the `Location` header get 404.
- `[Consumes("application/json")]` is **not** safe at the controller level when the controller has trigger-style POSTs without bodies (e.g., `POST /orders/{id}/submission`). ASP.NET Core returns `415 Unsupported Media Type` for any request without a `Content-Type` header. Apply `[Consumes]` per-action on body-bearing endpoints only, or scope it to a route convention.
- Integration tests include at least one business-validation failure that asserts `422` Problem Details; do not rely on exception middleware to prove Result mapping.

### Cross-package preflight for endpoint changes

| If the endpoint change includes... | Also read | Why |
|---|---|---|
| Sending commands or queries through Mediator | [`trellis-api-mediator.md`](trellis-api-mediator.md#canonical-pipeline-order) | ASP maps the response, but validation/authorization/logging/commit behavior belongs to the Mediator pipeline. |
| EF-backed writes | [`trellis-api-efcore.md`](trellis-api-efcore.md#transactionalcommandbehaviortmessage-tresponse), [`trellis-api-servicedefaults.md`](trellis-api-servicedefaults.md#trellisservicebuilder) | Handlers stage changes; `TransactionalCommandBehavior` commits only when registered in the correct order. |
| Actor resolution or authorization failures | [`trellis-api-authorization.md`](trellis-api-authorization.md#iactorprovider), [`trellis-api-mediator.md`](trellis-api-mediator.md#authorizationbehaviortmessage-tresponse) | ASP provides actor providers; authorization contracts and behaviors live outside the response mapper. |
| Integration tests or `.http` examples | [`trellis-api-testing-aspnetcore.md`](trellis-api-testing-aspnetcore.md#http-file-replay-helpers) | Failure-path status/header expectations should be executable, not only documented in OpenAPI. |

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
| `public static IResult ToHttpResponse<T>(this Result<WriteOutcome<T>> result, Action<HttpResponseOptionsBuilder<T>>? configure = null)` | `IResult` | Maps `Result<WriteOutcome<T>>` per RFC 9110: `Created → 201 + Location`, `Updated → 200` (or `204` with `Prefer: return=minimal` **when `HonorPrefer()` is configured**), `UpdatedNoContent → 204`, `Accepted → 202` (+ `Retry-After` when `RetryAfter != null`, + `Location` when `MonitorUri != null`), `AcceptedNoContent → 202` (+ `Retry-After`/`Location` under the same conditions). |
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
| `VaryForActor()` | `HttpResponseOptionsBuilder<TDomain>` | Appends the request header(s) that contribute to actor identity for the registered `IActorProvider` to the response `Vary` header and returns the builder; See *Behavioral notes: VaryForActor* below. |
| `WithContentLanguage(params string[] languages)` | `HttpResponseOptionsBuilder<TDomain>` | Joins values into `Content-Language`. |
| `WithContentLocation(Func<TDomain, string> selector)` | `HttpResponseOptionsBuilder<TDomain>` | Sets the `Content-Location` header. |
| `WithCacheControl(CacheControlHeaderValue value)` | `HttpResponseOptionsBuilder<TDomain>` | Sets the `Cache-Control` response header to the supplied directive. Applies to success responses (200 / 201 / 204 / 304), `WriteOutcome` (Created / Updated / Accepted), paged responses, AND failure responses — so `WithCacheControl(CacheControl.NoStore())` protects 404 / 403 / 412 / 422 from intermediate-cache leakage just as much as the 200. Throws `ArgumentNullException` on null. Use the [`CacheControl`](#cachecontrol) presets (`NoStore()`, `NoCache()`, `Public(TimeSpan)`, `Private(TimeSpan)`, `Immutable(TimeSpan)`) for common shapes; each call returns a fresh `CacheControlHeaderValue` so mutation cannot leak across responses. |
| `WithCacheControl(Func<TDomain, CacheControlHeaderValue?> selector)` | `HttpResponseOptionsBuilder<TDomain>` | Sets `Cache-Control` from a selector run against the success-path domain value. Applies to the success path only (failures carry no domain value). Returning `null` from the selector omits the header on that response (falls back to the static-value overload if both are configured; otherwise no header is emitted). |
| `Created(string locationLiteral)` | `HttpResponseOptionsBuilder<TDomain>` | Returns `201 Created` with a literal `Location` header. |
| `Created(Func<TDomain, string> selector)` | `HttpResponseOptionsBuilder<TDomain>` | Returns `201 Created` with a `Location` derived from the value. |
| `CreatedAtRoute(string routeName, Func<TDomain, RouteValueDictionary> routeValues)` | `HttpResponseOptionsBuilder<TDomain>` | Returns `201 Created` with a `Location` generated via `LinkGenerator.GetUriByName` (resolved from `HttpContext.RequestServices` at execute time). AOT-safe. **Under query-string / header API versioning**, the route values dictionary MUST include `["api-version"] = ApiVersion` — otherwise `Location` headers omit the version and 404 on dereference. The recommended path is to chain `.WithVersionedRoute()` from [Trellis.Asp.ApiVersioning](trellis-api-asp-apiversioning.md#httpresponseoptionsbuilderapiversioningextensions), which injects the version automatically. The `TRLS023` analyzer warns on bare `CreatedAtRoute` / `CreatedAtAction` / `WithLocation` calls inside `[ApiVersion]` controllers and offers a code fix that appends `.WithVersionedRoute()`. |
| `CreatedAtRoute(string routeName, Func<TDomain, object> idSelector, string idRouteKey = "id")` | `HttpResponseOptionsBuilder<TDomain>` | Convenience overload for the common single-id route. Constructs a `RouteValueDictionary` with `[idRouteKey] = idSelector(value)` and chains the multi-key overload. |
| `WithLocation(string routeName, Func<TDomain, RouteValueDictionary> routeValues)` | `HttpResponseOptionsBuilder<TDomain>` | Adds a `Location` header to the response **without** changing the status code (typically 200 OK). Generated via `LinkGenerator.GetUriByName`. RFC 9110 §10.2.2 permits `Location` on any 2xx response that identifies a related resource — use this on state-transition endpoints that mutate an existing resource (e.g., `POST /orders/{id}/return` returning 200 OK). For new-resource creation use `CreatedAtRoute` / `CreatedAtAction` instead. Applies to `Result<T>` responses (`ToHttpResponse`) only; it has no effect on `Result<WriteOutcome<T>>`, which uses its own outcome-specific Location behavior. Same versioning trap as `CreatedAtRoute` — chain `.WithVersionedRoute()` from `Trellis.Asp.ApiVersioning` under query/header versioning. |
| `WithLocation(string routeName, Func<TDomain, object> idSelector, string idRouteKey = "id")` | `HttpResponseOptionsBuilder<TDomain>` | Single-id convenience overload for `WithLocation`. |
| `[RequiresUnreferencedCode] [RequiresDynamicCode] CreatedAtAction(string actionName, Func<TDomain, RouteValueDictionary> routeValues, string? controllerName = null)` | `HttpResponseOptionsBuilder<TDomain>` | MVC equivalent of `CreatedAtAction` — uses `LinkGenerator.GetUriByAction`. **Not trim/AOT-safe**; use `CreatedAtRoute` for AOT scenarios. Under query/header API versioning, it has the same `api-version` route-value requirement as `CreatedAtRoute`; chain `.WithVersionedRoute()` from `Trellis.Asp.ApiVersioning` to inject it automatically. |
| `WithRouteValueResolver(string key, Func<HttpContext, string?> resolver)` | `HttpResponseOptionsBuilder<TDomain>` | Registers a per-request resolver that injects an additional route value into the `Location`-generation dictionary at execute time, after the `routeValues` selector has run. The resolver is called with the request `HttpContext`; returning `null` skips injection (the existing entry, if any, is preserved). The runtime clones the user-supplied dictionary defensively the first time a resolver writes a non-null value, so selectors that return shared instances cannot leak across requests. The mechanism is the foundation for [Trellis.Asp.ApiVersioning](trellis-api-asp-apiversioning.md#httpresponseoptionsbuilderapiversioningextensions)'s `WithVersionedRoute()` and is also useful for cross-cutting route-value injection (tenant id, request culture, etc.). |
| `EvaluatePreconditions()` | `HttpResponseOptionsBuilder<TDomain>` | On `GET`/`HEAD`, evaluates RFC 9110 conditional headers (`If-Match`, `If-Unmodified-Since`, `If-None-Match`, `If-Modified-Since`) using the configured ETag/LastModified selectors and writes `304 Not Modified` or `412 Precondition Failed` accordingly. On unsafe methods the precondition must be evaluated *before* the mutation. |
| `HonorPrefer()` | `HttpResponseOptionsBuilder<TDomain>` | Opt in to RFC 7240 `Prefer: return=minimal` / `return=representation` handling on `WriteOutcome.Updated`. When **not** called, the `Prefer` request header is completely ignored: the writer never emits `Vary: Prefer` or `Preference-Applied`, and `return=minimal` does **not** short-circuit the body. When called, always emits `Vary: Prefer`; emits `Preference-Applied` only when an honored preference was sent. |
| `WithErrorMapping(Func<Error, int> mapper)` | `HttpResponseOptionsBuilder<TDomain>` | Per-call mapper for failure responses. Highest precedence. |
| `WithErrorMapping<TError>(int statusCode) where TError : Error` | `HttpResponseOptionsBuilder<TDomain>` | Per-call override for a single error type. Higher precedence than `TrellisAspOptions`. |

### Behavioral notes: VaryForActor

The provider must implement `IProvideActorVaryHeaders`; the bundled `ClaimsActorProvider` returns `["Authorization"]`, `DevelopmentActorProvider` returns `[X-Test-Actor]`, and `CachingActorProvider` delegates to its inner provider. `VaryForActor()` throws `InvalidOperationException` at apply time when no provider is registered, the registered provider does not implement the capability, or the implementation returns an empty collection, failing closed against silent cache-poisoning across actors. Decorating providers such as `CachingActorProvider` are unwrapped via the internal `IDecoratingActorProvider` so diagnostics name the underlying provider that needs the implementation. The behavior applies uniformly to success, failure, `WriteOutcome`, and paginated paths.

> **Byte ranges are not in Trellis's scope.** Trellis does not expose `WithRange` / `WithAcceptRanges`. For binary downloads with RFC 9110 §14 byte-range semantics, call `Microsoft.AspNetCore.Http.Results.File(stream, enableRangeProcessing: true)` from ASP.NET Core directly; for custom advisory headers like `Accept-Ranges: none`, write the header on `HttpContext.Response.Headers` from middleware or the endpoint handler. The client-side typed-error vocabulary still surfaces inbound 416 as `Error.TransportFault(new HttpError.RangeNotSatisfiable(...))` with the upstream `Content-Range` companion header preserved.

### `HttpResponseOptionsBuilder`

**Declaration**

```csharp
public sealed class HttpResponseOptionsBuilder
```

Non-generic builder consumed only by `Error.ToHttpResponse(this Error error, Action<HttpResponseOptionsBuilder>?)` — used to shape the ProblemDetails response for a standalone `Error`.

| Signature | Returns | Description |
| --- | --- | --- |
| `Vary(params string[] headers)` | `HttpResponseOptionsBuilder` | Appends headers to `Vary`. |
| `VaryForActor()` | `HttpResponseOptionsBuilder` | Same contract as the generic builder's `VaryForActor()`. Applied by `Error.ToHttpResponse(...)` (the only consumer of the non-generic builder), so standalone error responses emitted via this verb partition by actor too. |
| `HonorPrefer()` | `HttpResponseOptionsBuilder` | Always emits `Vary: Prefer`. |
| `WithCacheControl(CacheControlHeaderValue value)` | `HttpResponseOptionsBuilder` | Same contract as the generic builder's static-value overload. The non-generic builder is consumed only by `Error.ToHttpResponse(...)`, so this overload sets `Cache-Control` on the ProblemDetails failure response — useful for `Error.ToHttpResponse(o => o.WithCacheControl(CacheControl.NoStore()))` to keep deterministic-error responses out of intermediate caches. |
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
| `FailFastOnSilentVersionInjection` | `bool` | When `true`, every `.WithVersionedRoute()` (or pinned overload) call that would silently skip `api-version` injection because the target endpoint has no `ApiVersionMetadata` throws `InvalidOperationException` instead of logging a single warning per endpoint. Defaults to `false` (warn-once-per-(endpoint, AppDomain) via the `Trellis.Asp.ApiVersioning` `ILogger` category). Intended for non-Production environments to surface mid-migration regressions where `AddApiVersioning(...)` was removed but `.WithVersionedRoute()` chains remain. |
| `SynthesizeProblemDetailsInstanceFromResourceRef` | `bool` | When `true` (the default), `ResponseFailureWriter` populates `ProblemDetails.Instance` from the failing `ResourceRef` (`/{collectionName}/{id}`) when the request URL does not already identify the resource, and preserves the original request URL under `Extensions["request"]`. Applies to `NotFound`, `Gone`, `Conflict`, `Forbidden`, `InvariantViolation`, and `TransportFault(HttpError.PreconditionFailed)`. Set to `false` to retain the historical request-URL-only `Instance`. Collection name defaults to `{Type.ToLowerInvariant()}s`; override via `[ResourceCollectionName(name)]` on the aggregate or `services.AddResourceCollectionName<T>(name)`. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public TrellisAspOptions MapError<TError>(int statusCode) where TError : Error` | `TrellisAspOptions` | Overrides or adds an error-type-to-status-code mapping. |
| `internal int GetStatusCode(Error error)` | `int` | Walks the error type hierarchy looking for a mapping; falls back to `500`. Invoked by the response writer. |

Default mappings: `Error.InvalidInput=422`, `Error.InvariantViolation=422`, `Error.AuthenticationRequired=401`, `Error.Forbidden=403`, `Error.NotFound=404`, `Error.Conflict=409`, `Error.Gone=410`, `Error.RateLimited=429`, `Error.Unexpected=500`, and `Error.Unavailable=503`. `Error.Unexpected { ReasonCode: "not_implemented" }` is special-cased to `501`. `Error.TransportFault` unwraps `HttpError.MethodNotAllowed`, `HttpError.NotAcceptable`, `HttpError.PreconditionFailed`, `HttpError.ContentTooLarge`, `HttpError.UnsupportedMediaType`, `HttpError.RangeNotSatisfiable`, and `HttpError.PreconditionRequired` to `405/406/412/413/415/416/428`. Explicit `MapError<Error.TransportFault>(...)` overrides all wrapped transport faults at once.

### Domain → HTTP boundary mapping

Trellis.Core.Error is transport-neutral. The ASP boundary translates domain failures to HTTP per the table below.

| Domain case | Status | Wire `kind` extension slug | Headers |
|---|---|---|---|
| `InvalidInput` | 422 | `unprocessable-content` | — |
| `InvariantViolation` | 422 | `unprocessable-content` | — |
| `NotFound` | 404 | `not-found` | — |
| `Forbidden` | 403 | `forbidden` | — |
| `Conflict` (`ReasonCode=="concurrent_modification"` AND request had `If-Match`) | 412 | `precondition-failed` | — |
| `Conflict` (otherwise) | 409 | `conflict` | — |
| `Gone` | 410 | `gone` | — |
| `AuthenticationRequired` | 401 | `unauthorized` | `WWW-Authenticate` from `Scheme` or `IAuthenticationSchemeProvider` |
| `Unavailable` | 503 | `service-unavailable` | `Retry-After` from `RetryAdvice` |
| `RateLimited` | 429 | `too-many-requests` | `Retry-After` from `RetryAdvice` |
| `Unexpected` (default) | 500 | `internal-server-error` | `faultId` extension when set |
| `Unexpected` (`ReasonCode=="not_implemented"`) | 501 | `not-implemented` | — |
| `Aggregate` | worst-status of children | `multi` | per-child |
| `TransportFault` | per inner `HttpError` (405/406/412/413/415/416/428) | inner wire kind | inner-specific |

The wire token shown above is emitted as the top-level Problem Details extension member `kind` (populated via `ProblemDetails.Extensions["kind"]`, which ASP.NET Core serializes alongside `type`, `title`, `status`, `detail`, and `instance` — RFC 9457 §3.2). The top-level Problem Details `type` field continues to default to the ASP.NET status-code URL (e.g. `https://tools.ietf.org/html/rfc4918#section-11.2` for 422); the top-level `kind` member is the durable identifier consumers should key on. Domain `Kind` and wire `kind` are intentionally distinct for `InvalidInput` and `InvariantViolation`: the domain slugs remain `invalid-input` / `invariant-violation`, while the on-wire `kind` stays `unprocessable-content` for backward compatibility.

### Header synthesis

- `Retry-After` is synthesized from `RetryAdvice` on `Error.RateLimited` and `Error.Unavailable`.
- `WWW-Authenticate` comes from `Error.AuthenticationRequired.Scheme` when set; otherwise the writer asks `IAuthenticationSchemeProvider` for the default challenge/authenticate scheme and emits that scheme name.
- `Allow` comes from `Error.TransportFault(new HttpError.MethodNotAllowed(...))`.
- `Content-Range` comes from `Error.TransportFault(new HttpError.RangeNotSatisfiable(...))`.

### Aggregate rendering

`Error.Aggregate` renders as one outer Problem Details object whose status is the worst status of the children. Child problems are projected into the RFC 9457 `errors[]` extension, one object per inner error with `type`, `status`, `code`, `kind`, and `detail`.

### Concurrent modification override

When `Error.Conflict.ReasonCode == "concurrent_modification"` and the incoming request carried `If-Match`, the boundary emits `412 Precondition Failed` with wire `kind` `precondition-failed` instead of `409 conflict`. The top-level Problem Details `type` continues to default to the ASP.NET status-code URL for 412. The domain `code` stays `"concurrent_modification"`.

### `ProblemDetails.Instance` synthesis from `ResourceRef`

When `TrellisAspOptions.SynthesizeProblemDetailsInstanceFromResourceRef` is `true` (the default), `ResponseFailureWriter` populates `ProblemDetails.Instance` from the failing `ResourceRef` rather than the request URL whenever:

1. The error carries a non-null `ResourceRef` (`NotFound`, `Gone`, `Conflict?`, `Forbidden?`, `InvariantViolation?`, or `TransportFault(HttpError.PreconditionFailed)`).
2. `ResourceRef.Type` and `ResourceRef.Id` are both non-empty and non-whitespace.
3. The request URL does not already identify the same resource (segment-and-query-value-aware exact match against the raw id; percent-encoded path segments are decoded for the comparison, and form-encoded `+` is treated as space in query values).

The synthesised value is `/{collection}/{escapedId}` (no `/api/` prefix, no api-version segment, no query string), where:

- `{collection}` defaults to `ResourceRef.Type.ToLowerInvariant() + "s"`; override via `[ResourceCollectionName(name)]` on the aggregate type or via `services.AddResourceCollectionName<T>(name)` / `services.AddResourceCollectionNames(assembly)`. Overrides are emitted verbatim — the lowercase guarantee applies only to the naive plural fallback, so register lowercase names if you want to preserve the convention.
- `{escapedId}` is `Uri.EscapeDataString(ResourceRef.Id)`.

The original request URI is preserved under `ProblemDetails.Extensions["request"]` so callers needing both have it. When synthesis is suppressed (toggle off, URL already identifies the resource, or `ResourceRef` is malformed), `Instance` falls back to the request URL and no `request` extension is emitted. `Error.Aggregate` never promotes a child's `ResourceRef`; the envelope itself carries no resource identity.

Synthesis is defensive: any failure during URI construction (malformed `ResourceRef`, name not a safe URL path segment, registry not registered) is swallowed silently and the request URL is used. The synthesis path can never turn a domain 404/409 into a 500.

### `ResourceCollectionNameRegistry`

**Declaration**

```csharp
public sealed class ResourceCollectionNameRegistry
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public ResourceCollectionNameRegistry()` | `ResourceCollectionNameRegistry` | Empty registry — every `ResourceRef.Type` resolves to its naive lowercase plural. Used as the static fallback when `HttpContext.RequestServices` is null. |
| `public ResourceCollectionNameRegistry(IEnumerable<ResourceCollectionNameOverride> overrides)` | `ResourceCollectionNameRegistry` | Builds the registry from DI. Validates each override (non-empty type/name; name must pass `ResourceCollectionNameAttribute.IsSafePathSegment`). Throws `InvalidOperationException` if two overrides map the same type name (case-insensitive) to different collection names. Identical duplicates are coalesced silently. Registered as a singleton by `AddTrellisAsp`. |
| `public string Resolve(string resourceType)` | `string` | Case-insensitive override lookup; falls back to `resourceType.ToLowerInvariant() + "s"` if no override is registered. Safe to call from concurrent request threads (the underlying dictionary is built once at construction and read-only thereafter). |

### `ResourceCollectionNameOverride`

**Declaration**

```csharp
public sealed record ResourceCollectionNameOverride(string ResourceType, string CollectionName);
```

DI-friendly carrier record. Register one per type via `services.AddSingleton(new ResourceCollectionNameOverride("Person", "people"))` (or use the `AddResourceCollectionName*` extensions, which do the same). `ResourceCollectionNameRegistry` consumes them via `IEnumerable<ResourceCollectionNameOverride>` in its constructor — Microsoft DI auto-injects an empty enumerable when no overrides are registered.

### `RuleViolationProblemDetail`

**Declaration**

```csharp
public sealed record RuleViolationProblemDetail(string Code, string? Detail, string[] Fields);
```

AOT-friendly JSON payload used inside Problem Details `extensions["rules"]` for `Error.InvalidInput` rule violations. Application code should treat this as response shape metadata, not as a domain model.

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
| `public static bool IfMatchSatisfied(IList<EntityTagHeaderValue> ifMatchHeader, string currentETag)` | `bool` | Strong-comparison helper for `If-Match` (RFC 9110 §13.1.1). Returns `true` when the header is absent/empty (unconditional request), for `*`, or for a matching strong tag; returns `false` when `currentETag` is null/empty or every presented tag is weak or non-matching. |
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
| `public static Result<T> EnforceIfNoneMatchPrecondition<T>(this Result<T> result, EntityTagValue[]? ifNoneMatchETags)` | `Result<T>` | When `ifNoneMatchETags` contains `*`, replaces a successful result with `Error.TransportFault(new HttpError.PreconditionFailed(ResourceRef.For<T>(), PreconditionKind.IfNoneMatch))`. No-op when the header is absent or the result is already a failure. |
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
| `public static IServiceCollection AddTrellisAsp(this IServiceCollection services)` | `IServiceCollection` | Registers `TrellisAspOptions` with default error mappings and `ResourceCollectionNameRegistry`. **Does NOT register scalar-value validation** — see `AddTrellisAspWithScalarValidation` or call `AddScalarValueValidation()` explicitly. The split exists so the global `MvcOptions`/`JsonOptions` mutation that scalar validation performs is opt-in instead of silent. |
| `public static IServiceCollection AddTrellisAsp(this IServiceCollection services, Action<TrellisAspOptions> configure)` | `IServiceCollection` | Same as above, with a `MapError<TError>(...)` callback for overrides. **Calls compose** — when `AddTrellisAsp(o => ...)` is invoked more than once (e.g. by a library and the application), every `configure` delegate runs in registration order against the same `TrellisAspOptions` instance built lazily by `OptionsFactory<TrellisAspOptions>`. Same-`TError` mappings still follow last-wins, but mappings for different error types from earlier calls are preserved. |
| `public static IServiceCollection AddTrellisAspWithScalarValidation(this IServiceCollection services)` | `IServiceCollection` | Convenience composition of `AddTrellisAsp()` and `AddScalarValueValidation()` for greenfield projects that want error mapping plus scalar-value model binding / JSON converters in a single call. |
| `public static IServiceCollection AddTrellisAspWithScalarValidation(this IServiceCollection services, Action<TrellisAspOptions> configure)` | `IServiceCollection` | Convenience composition of `AddTrellisAsp(configure)` and `AddScalarValueValidation()`. |
| `public static IServiceCollection AddTrellisProblemDetails(this IServiceCollection services)` | `IServiceCollection` | Registers Trellis Problem Details defaults and returns the service collection; See *Behavioral notes: AddTrellisProblemDetails* below. |
| `public static IServiceCollection AddResourceCollectionName<T>(this IServiceCollection services, string collectionName)` | `IServiceCollection` | Maps the simple type name of `T` (as produced by `ResourceRef.For<T>()`) to a URL collection segment used when synthesising `ProblemDetails.Instance` from a `ResourceRef`. AOT- and trim-friendly. Registers `ResourceCollectionNameRegistry` via `TryAddSingleton` so callers can use this extension without first calling `AddTrellisAsp`. |
| `public static IServiceCollection AddResourceCollectionName(this IServiceCollection services, string resourceType, string collectionName)` | `IServiceCollection` | Same as the typed overload but takes the `ResourceRef.Type` string directly — for cases where the consumer wants to bind a type name that does not exist as a CLR type, or to keep registration centralised. Validates the `collectionName` is a safe single URL path segment. |
| `public static IServiceCollection AddResourceCollectionNames(this IServiceCollection services, Assembly assembly)` | `IServiceCollection` | Scans the supplied assembly for types decorated with `[ResourceCollectionName]` and registers one `ResourceCollectionNameOverride` per type. Marked `[RequiresUnreferencedCode]` because it uses reflection over the assembly's types; AOT/trim-published apps should prefer the explicit `AddResourceCollectionName<T>(...)` overload. Conflicting registrations (same type name → different collection names) throw at registry activation; identical registrations coalesce silently. |
| `public static IServiceCollection AddResourceCollectionNames(this IServiceCollection services, params Assembly[] assemblies)` | `IServiceCollection` | Convenience overload that scans each supplied assembly in order via the single-`Assembly` overload. Identical overrides across assemblies coalesce silently when the registry is activated; conflicting overrides throw. |
| `public static IServiceCollection AddTrellisIdempotency(this IServiceCollection services, Action<IdempotencyOptions>? configure = null)` | `IServiceCollection` | Registers `IdempotencyOptions` (with the optional `configure` callback and startup validation), the default `IIdempotencyScopeResolver` (per-actor, falling back to anonymous), and an internal marker that `UseTrellisIdempotency()` uses to detect the wiring at startup. **Does not register a store** — composition is explicit; call `AddInMemoryIdempotencyStore()` for dev / tests, or register an EF-backed store for multi-instance production hosts. Composition-root consumers can opt in via the `options.UseIdempotency(...)` slot on [`TrellisServiceBuilder`](trellis-api-servicedefaults.md#trellisservicebuilder). |
| `public static IServiceCollection AddInMemoryIdempotencyStore(this IServiceCollection services)` | `IServiceCollection` | Registers `InMemoryIdempotencyStore` as the singleton `IIdempotencyStore`. Single-process only; multi-instance hosts need a shared store. |

### Behavioral notes: AddTrellisProblemDetails

`AddTrellisProblemDetails()` registers `IProblemDetailsService` via `AddProblemDetails` and applies the Trellis recipe: trace id projected from `Activity.Current?.Id ?? HttpContext.TraceIdentifier`, friendly detail rewrite for `500` responses, and an `allow` extension array on `405` projected from the `Allow` header after splitting on `,` and trimming whitespace. It composes with consumer customizations by running Trellis defaults first, then letting any prior or subsequent `AddProblemDetails(o => o.CustomizeProblemDetails = ...)` callback run last and win on collisions. Additional calls are no-ops. Pair it with `app.UseTrellisProblemDetails()` in the request pipeline. Composition-root consumers can opt in via the `options.UseProblemDetails()` slot on [`TrellisServiceBuilder`](trellis-api-servicedefaults.md#trellisservicebuilder); direct + builder composition is idempotent with one Trellis post-configure layer.

### `ApplicationBuilderExtensions`

**Declaration**

```csharp
public static class ApplicationBuilderExtensions
```

The middleware pipeline surface for `Trellis.Asp` (in folder `Extensions/`).

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IApplicationBuilder UseTrellisProblemDetails(this IApplicationBuilder app)` | `IApplicationBuilder` | Wires the canonical ProblemDetails request pipeline: `UseExceptionHandler()` then `UseStatusCodePages()`. Must be registered **early** in the pipeline — `UseStatusCodePages` only rewrites status-code responses produced by middleware registered after it (routing, authorization, endpoint execution). Pair with `services.AddTrellisProblemDetails()` so the rewritten responses pick up Trellis defaults (trace id, friendly 500 detail, 405 `allow` array). |
| `public static IApplicationBuilder UseTrellisIdempotency(this IApplicationBuilder app)` | `IApplicationBuilder` | Mounts `IdempotencyMiddleware` in the request pipeline. The middleware is a no-op on endpoints that do not carry `IdempotentAttribute` and on methods outside `IdempotencyOptions.Methods` (default `POST` and `PATCH`). Throws `InvalidOperationException` at startup if `services.AddTrellisIdempotency(...)` was not called. The `IIdempotencyStore` registration is validated at startup when the container exposes `IServiceProviderIsService` (the default Microsoft.Extensions.DependencyInjection container does); otherwise the missing-store failure surfaces as a per-request resolution error on the first opted-in request. Mount after `UseRouting()` so opted-in endpoints' metadata is resolvable, and after `UseAuthentication()` / `UseAuthorization()` so the default `DefaultIdempotencyScopeResolver` sees the authenticated `Actor` and partitions the store by it; mounting before authentication causes every authenticated request to fall back to the shared `anonymous` scope, which can let different users collide on the same key. |

### Namespace `Trellis.Asp.Idempotency`

Opt-in IETF `Idempotency-Key` middleware for `POST` / `PATCH` retry safety. See cookbook [Recipe 29](trellis-api-cookbook.md#recipe-29--ietf-idempotency-key-middleware-on-post--patch-with-usetrellisidempotency).

### `IdempotentAttribute`

**Declaration**

```csharp
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IdempotentAttribute : Attribute
```

Endpoint marker. Apply to a controller action (or attach as endpoint metadata in Minimal API: `.WithMetadata(new IdempotentAttribute())`) to opt the endpoint into the idempotency middleware. Endpoints without the attribute pass through.

### `IdempotencyOptions`

**Declaration**

```csharp
public sealed class IdempotencyOptions
```

| Member | Default | Description |
| --- | --- | --- |
| `HeaderName` | `"Idempotency-Key"` | HTTP header carrying the IETF [`sf-string`](https://www.rfc-editor.org/rfc/rfc8941) idempotency key. |
| `ReplayHeaderName` | `"Idempotent-Replayed"` | Response header added to replayed responses so clients can detect that the body came from a cached snapshot rather than a fresh handler invocation. |
| `Ttl` | `24 h` | Time a completed snapshot is retained before it is evicted and the key can be reused. |
| `ReservationTimeout` | `30 s` | Time after which a same-key retry with a matching fingerprint may atomically take over an in-flight reservation (CAS) so a crashed handler does not block retries forever. Stores MUST NOT delete outstanding reservations on this timeout; takeover replaces the entry under a new reservation token, which invalidates the previous token for `CompleteAsync` / `AbandonAsync`. |
| `MaxKeyLength` | `200` | Hard cap on parsed key length; longer keys produce `400 Bad Request`. Raw header values are also rejected before parsing when they exceed the parser's 4 KiB defensive cap, using the existing invalid-key `400 Bad Request` surface. |
| `MaxRequestBodyBytes` | `1 MiB` | Hard cap on the buffered request body that contributes to the fingerprint; larger bodies produce `413 Payload Too Large`. |
| `MaxResponseBodyBytes` | `1 MiB` | Hard cap on the captured response body; exceeding aborts capture and records no snapshot (the next retry re-executes). |
| `MismatchStatusCode` | `422` | Status returned when the same key arrives with a different body fingerprint. |
| `RequireKeyOnOptedInEndpoints` | `true` | When `true`, opted-in endpoints reject requests that omit the header with `400 idempotency.key_required`; when `false`, missing-key requests pass through with no idempotency processing. |
| `IncludeSetCookieInSnapshot` | `false` | When `true`, `Set-Cookie` response headers are captured in snapshots; default excludes them so a replay does not re-issue session or authentication cookies that have since been rotated. |
| `Methods` | `{ POST, PATCH }` | Methods the middleware acts on; other methods (`GET`, `PUT`, `DELETE`) pass through. |
| `AdditionalFingerprintHeaders` | empty | Extra request headers included in the fingerprint (for example a tenant header) when their semantics affect the request identity. |

`AddTrellisIdempotency()` validates options at host startup: `HeaderName`, `ReplayHeaderName`, and `AdditionalFingerprintHeaders` must be valid HTTP header names; `Ttl`, `ReservationTimeout`, `MaxKeyLength`, `MaxRequestBodyBytes`, and `MaxResponseBodyBytes` must be positive; `MismatchStatusCode` must be between `400` and `599`; and `Methods` must contain at least one valid HTTP method token.

### `IIdempotencyStore`

**Declaration**

```csharp
public interface IIdempotencyStore
```

| Signature | Returns | Description |
| --- | --- | --- |
| `ValueTask<IdempotencyReservationOutcome> TryReserveAsync(string scope, string key, string fingerprint, CancellationToken ct)` | one of `Reserved(reservationId)`, `AlreadyInFlight(retryAfter)`, `Replay(snapshot)`, `BodyHashMismatch(storedFingerprint)` | CAS reservation. The reservation token is an opaque `string`; pass it back to `CompleteAsync` / `AbandonAsync`. |
| `ValueTask CompleteAsync(string scope, string key, string reservationId, IdempotencyResponseSnapshot snapshot, CancellationToken ct)` | `ValueTask` | Records the response snapshot under the reservation. Conditional on the reservation token (CAS) so a slow original handler whose reservation has been atomically taken over by a same-key retry cannot finalise the entry under the now-invalid token. |
| `ValueTask AbandonAsync(string scope, string key, string reservationId, CancellationToken ct)` | `ValueTask` | Releases a reservation without a snapshot so the next retry can re-reserve. Called on any failure path (exception, response-too-large, `SendFileAsync`, abort, **5xx response status**, **response trailers**). |

### `InMemoryIdempotencyStore`

**Declaration**

```csharp
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
```

Single-process `ConcurrentDictionary`-backed store. Register with `services.AddInMemoryIdempotencyStore()` for dev / single-instance hosts and tests. **Not safe across multiple instances or process restarts** — production hosts that retry across replicas need an EF-backed store that implements the same CAS contract.

### `IIdempotencyScopeResolver`

**Declaration**

```csharp
public interface IIdempotencyScopeResolver
```

| Signature | Returns | Description |
| --- | --- | --- |
| `ValueTask<string> ResolveAsync(HttpContext context, CancellationToken ct)` | `ValueTask<string>` | Returns an isolation scope (tenant id, actor id, anonymous). Two requests carrying the same key under different scopes never collide. Default registration is `DefaultIdempotencyScopeResolver`, which resolves `IActorProvider` from request services and uses the current actor's id (falling back to anonymous when no provider is registered or no actor is resolved). `ActorIdempotencyScopeResolver` is also shipped for hosts that want a hard dependency on `IActorProvider`. Replace with a custom implementation for multi-tenant hosts. |

### Namespace `Trellis.Asp.Authorization`

The actor-provider DI surface absorbed from the former `Trellis.Asp.Authorization` package. Domain primitives (`Actor`, `IActorProvider`, etc.) live in `Trellis.Authorization` — see [`trellis-api-authorization.md`](trellis-api-authorization.md#types).

### `IProvideActorVaryHeaders`

**Declaration**

```csharp
public interface IProvideActorVaryHeaders
{
    IReadOnlyCollection<string> VaryByHeaders { get; }
}
```

Optional capability that an `IActorProvider` implementation can expose so [`HttpResponseOptionsBuilder<TDomain>.VaryForActor`](#httpresponseoptionsbuildertdomain) can emit the correct request headers as response `Vary` entries for cache partitioning by actor.

The bundled providers implement this:

- `ClaimsActorProvider` (and `EntraActorProvider` via inheritance) — `virtual VaryByHeaders => ["Authorization"]`. Subclass and override for non-bearer auth (cookies, mTLS, forwarded headers); leaving the JWT-bearer default in place against a non-Bearer service allows the same cache-poisoning that `VaryForActor()` exists to prevent.
- `DevelopmentActorProvider` — `[X-Test-Actor]` (the test header).
- `CachingActorProvider` — delegates to the wrapped provider's `VaryByHeaders`; surfaces an empty collection when the wrapped provider does not implement the interface, so `VaryForActor()` throws fail-closed pointing at the inner provider as the remediation site (the writer unwraps caching wrappers via the internal `IDecoratingActorProvider` interface so the diagnostic names the right type).

Custom providers that derive actor identity from request data that cannot be cleanly named by an HTTP header (mTLS, IP-based, etc.) should NOT implement this interface; consumers using such providers must mark cache-eligible endpoints with `Cache-Control: private, no-store` instead of calling `VaryForActor()`.

### `CacheControl`

**Declaration**

```csharp
public static class CacheControl
{
    public static CacheControlHeaderValue NoStore();
    public static CacheControlHeaderValue NoCache();
    public static CacheControlHeaderValue Public(TimeSpan maxAge);
    public static CacheControlHeaderValue Private(TimeSpan maxAge);
    public static CacheControlHeaderValue Immutable(TimeSpan maxAge);
}
```

Preset `System.Net.Http.Headers.CacheControlHeaderValue` builders for the common directives, designed for use with [`HttpResponseOptionsBuilder<TDomain>.WithCacheControl`](#httpresponseoptionsbuildertdomain).

| Preset | Emits | When to use |
|---|---|---|
| `CacheControl.NoStore()` | `Cache-Control: no-store` | Responses that contain personal data, secrets, or per-user state. Use on `Error.ToHttpResponse` and `Result.ToHttpResponse(opts => opts.WithCacheControl(...))` for sensitive endpoints; the static-value overload propagates to failure responses too so 404 / 403 / 422 cannot leak through intermediate caches. |
| `CacheControl.NoCache()` | `Cache-Control: no-cache` | Caches may store the response but must revalidate with the origin before serving. Different from `no-store`: revalidation is allowed, storage is not forbidden. |
| `CacheControl.Public(TimeSpan)` | `Cache-Control: public, max-age={seconds}` | Public, cacheable read endpoints (catalog data, public reference data). Shared caches may store and serve to any consumer for the lifetime. |
| `CacheControl.Private(TimeSpan)` | `Cache-Control: private, max-age={seconds}` | Per-user representations safe to cache in the user agent only. Compose with `VaryForActor()` if any intermediate (CDN, reverse proxy) is in the path. |
| `CacheControl.Immutable(TimeSpan)` | `Cache-Control: public, max-age={seconds}, immutable` | RFC 8246 — the response will not change for the freshness lifetime. Clients should not revalidate. Use for content-addressed or versioned assets. The `immutable` directive is appended via the BCL type's `Extensions` collection because `CacheControlHeaderValue` has no dedicated property for it. |

**Fresh-instance guarantee.** Every preset returns a new `CacheControlHeaderValue` on each call. `CacheControlHeaderValue` is mutable; if the presets returned a shared instance, a single caller mutating one returned value could corrupt every subsequent call. Test pinning: `CacheControl_presets_return_fresh_instances` in `WithCacheControlTests`.

**Directive coverage outside the presets.** Pass a hand-built `CacheControlHeaderValue` directly to `WithCacheControl(...)` when you need a directive not covered by a preset (`s-maxage`, `proxy-revalidate`, `stale-while-revalidate` via `Extensions`, etc.):

```csharp
opts.WithCacheControl(new CacheControlHeaderValue
{
    Public = true,
    MaxAge = TimeSpan.FromMinutes(5),
    SharedMaxAge = TimeSpan.FromMinutes(15),
    MustRevalidate = true,
});
```

**Composition with `VaryForActor()`.** Cache-Control and `Vary` are orthogonal — the former says "is this cacheable, and for how long"; the latter says "by which request dimensions does the cache key vary." For per-user representations served behind a shared cache, combine: `opts.WithCacheControl(CacheControl.Private(TimeSpan.FromMinutes(5))).VaryForActor()`.


### `ServiceCollectionExtensions`

**Declaration**

```csharp
public static class ServiceCollectionExtensions
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddClaimsActorProvider(this IServiceCollection services, Action<ClaimsActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor`, configures `ClaimsActorOptions`, and **replaces** the `IActorProvider` registration with a scoped `ClaimsActorProvider`. |
| `public static IServiceCollection AddNestedJsonPathClaimsActorProvider(this IServiceCollection services, Action<NestedJsonPathClaimsActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor`, configures and startup-validates `NestedJsonPathClaimsActorOptions`, and **replaces** the `IActorProvider` registration with a scoped `NestedJsonPathClaimsActorProvider`. When `configure` is omitted, the default empty JSON paths make the provider behave like `ClaimsActorProvider`. |
| `public static IServiceCollection AddEntraActorProvider(this IServiceCollection services, Action<EntraActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor`, configures `EntraActorOptions`, and **replaces** the `IActorProvider` registration with a scoped `EntraActorProvider`. |
| `public static IServiceCollection AddDevelopmentActorProvider(this IServiceCollection services, Action<DevelopmentActorOptions>? configure = null)` | `IServiceCollection` | Adds `IHttpContextAccessor` + logging, configures `DevelopmentActorOptions`, and **replaces** the `IActorProvider` registration with a scoped `DevelopmentActorProvider`. The provider itself throws outside the Development environment. |
| `public static IServiceCollection AddCachingActorProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(this IServiceCollection services) where T : class, IActorProvider` | `IServiceCollection` | Registers concrete provider `T` as scoped, then **replaces** the `IActorProvider` registration with a scoped `CachingActorProvider` wrapping `T`. |
| `public static IServiceCollection AddTrellisWorkerActor(this IServiceCollection services, Actor systemActor)` | `IServiceCollection` | Captures the existing unkeyed `IActorProvider` registration and **replaces** the slot with a scoped `WorkerComposedActorProvider` that returns `systemActor` when `IHttpContextAccessor.HttpContext` is `null` and delegates to the inner provider otherwise. Throws when there is no prior unkeyed `IActorProvider` registration, when more than one is registered, when the helper has already been called, or when the prior descriptor is singleton-lifetime via implementation type or factory (would silently downgrade to per-wrapper-scope — use `services.AddSingleton<IActorProvider>(instance)` or re-register as scoped) or transient-lifetime (would silently upgrade to scoped-per-wrapper — re-register as scoped). Keyed `IActorProvider` registrations are ignored and remain untouched. Registers an `IHostedLifecycleService` validator that throws in `StartingAsync` (before any `BackgroundService.ExecuteAsync` runs) if a later registration overwrites the wrapper. On synchronous scope disposal, the wrapper disposes inner providers that implement `IDisposable`; async-only `IAsyncDisposable` inners are skipped with a once-per-application-lifetime warning to avoid sync-over-async deadlocks, so consumers with async-only resources must dispose scopes asynchronously (`DisposeAsync` / `await using`). |

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
| `ActorIdClaim` | `string` | Claim type used for `Actor.Id`. Default: `"sub"` (RFC 7519 / OIDC subject claim). Matched against `Claim.Type` literally first; if the configured name is not found, falls back to its counterpart in a curated short↔long mapping table maintained by the provider. Covers the OAuth2 / OIDC / Microsoft identity-platform claims that consumers realistically configure as an actor id: `"sub"`/`"nameid"` ↔ `ClaimTypes.NameIdentifier`, `"oid"` ↔ `http://schemas.microsoft.com/identity/claims/objectidentifier`, `"upn"` ↔ `ClaimTypes.Upn`, `"email"` ↔ `ClaimTypes.Email`, `"role"`/`"roles"` ↔ `ClaimTypes.Role`, `"name"`/`"unique_name"` ↔ `ClaimTypes.Name`, `"tid"` ↔ `http://schemas.microsoft.com/identity/claims/tenantid`, `"idp"`/`"acr"`/`"amr"` ↔ their Microsoft long forms. The bidirectional fallback makes typical configurations just-work against both `JwtBearerOptions.MapInboundClaims = true` (ASP.NET default) and `false`. Emits a debug-level log entry when the fallback fires. No dotted/JSON-path traversal. The curated table is a subset of `JwtSecurityTokenHandler.DefaultInboundClaimTypeMap`; the space-delimited OAuth scope claim `"scp"`, AD FS 1.x legacy aliases, and device / certificate / request-transport / password-policy claims are intentionally not covered (see the `PermissionsClaim` row for the `scp` rationale). |
| `PermissionsClaim` | `string` | Claim type used for permissions. Default: `"permissions"`. Multi-valued JWT claims arrive as repeated `Claim` instances and are aggregated via `FindAll`. Matched against `Claim.Type` literally first; the resolver also queries every counterpart in the provider's curated short↔long mapping table (notably `"role"`/`"roles"` ↔ `ClaimTypes.Role`) and merges all matches into a single deduplicated set. This makes `PermissionsClaim = "roles"` and `PermissionsClaim = ClaimTypes.Role` both just-work against `JwtBearerOptions.MapInboundClaims = true` (ASP.NET default) and `false`. The default `"permissions"` is not in the mapping table, so it resolves by literal match only (regression-safe). The fallback emits a debug-level log entry when the configured claim resolves nothing but a counterpart does. The OAuth scope claim `"scp"` is intentionally NOT covered by the fallback: its value is space-delimited (RFC 6749 §3.3, e.g. `"orders.read orders.write"`) and `ClaimsActorProvider` snapshots claim values verbatim into the permission set, so wiring the fallback would still leave `Actor.HasPermission("orders.read")` returning `false`. OAuth scope-as-permission requires a custom subclass that splits the value. See the `ActorIdClaim` row above for the full covered subset. |

### `NestedJsonPathClaimsActorOptions`

**Declaration**

```csharp
public sealed class NestedJsonPathClaimsActorOptions : ClaimsActorOptions
```

Inherits the flat `ActorIdClaim` / `PermissionsClaim` defaults from `ClaimsActorOptions`. A default `new NestedJsonPathClaimsActorOptions()` is valid: `ContainerClaim`, `ActorIdPath`, and `PermissionsPath` are empty, so the provider delegates to flat-claim resolution.

| Name | Type | Description |
| --- | --- | --- |
| `ContainerClaim` | `string` | Claim type whose value carries the JSON payload. Default: empty. Required only when `ActorIdPath` or `PermissionsPath` is non-empty. |
| `ActorIdPath` | `string` | Dotted JSON path inside `ContainerClaim` used for `Actor.Id`. Default: empty, which falls back to inherited flat `ActorIdClaim` resolution. |
| `PermissionsPath` | `string` | Dotted JSON path inside `ContainerClaim` used for permissions. Default: empty, which falls back to inherited flat `PermissionsClaim` resolution. Terminal values may be a string, an array of strings, or an object whose property names become permissions. |

### `ClaimsActorProvider`

**Declaration**

```csharp
public class ClaimsActorProvider : IActorProvider, IProvideActorVaryHeaders
```

Hydrates an `Actor` from the current `HttpContext.User` using flat JWT/OIDC claims. The optional `logger` parameter receives debug-level diagnostics when the short↔long claim-name fallback resolves a claim the configured literal name did not produce — helpful for diagnosing silent-401/403 issues caused by `JwtBearerOptions.MapInboundClaims = true`. `AddClaimsActorProvider(...)` wires the logger automatically; manual constructions may pass `null`. Subclass and override `GetCurrentActorAsync` for nested-claim or computed-permission scenarios; `EntraActorProvider` is a worked example.

| Name | Type | Description |
| --- | --- | --- |
| `HttpContextAccessor` | `IHttpContextAccessor` (protected) | Exposed to derived providers. |
| `Options` | `ClaimsActorOptions` (protected) | Mapped options value. |

| Signature | Returns | Description |
| --- | --- | --- |
| `public ClaimsActorProvider(IHttpContextAccessor httpContextAccessor, IOptions<ClaimsActorOptions> options, ILogger<ClaimsActorProvider>? logger = null)` | — | Builds the flat-claims provider. The optional logger receives debug-level short↔long claim-name fallback diagnostics. |
| `public virtual Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Maybe<Actor>>` | Returns `Maybe<Actor>.None` when no authenticated identity exists or the configured `ActorIdClaim` is missing from the authenticated identity — the mediator pipeline maps `Maybe.None` to `Error.AuthenticationRequired` (HTTP 401). Throws `InvalidOperationException` only when `HttpContext` is missing (configuration bug, surfaces as HTTP 500). On success, permissions come from `FindAll(PermissionsClaim)` plus every counterpart in the provider's curated short↔long mapping table (see `PermissionsClaim` above), merged and snapshotted into a `FrozenSet<string>`; the result is wrapped via `Maybe.From(Actor.Create(actorId, permissions))` so forbidden permissions and attributes default to empty. |

### `NestedJsonPathClaimsActorProvider`

**Declaration**

```csharp
public class NestedJsonPathClaimsActorProvider : ClaimsActorProvider
```

Maps nested JSON claim payloads to actor ids or permissions via `NestedJsonPathClaimsActorOptions`. If `ActorIdPath` and `PermissionsPath` are both empty, it delegates to `ClaimsActorProvider`, making the no-config registration safe for flat-claim tokens. The DI registration validates at host startup via `IValidateOptions<NestedJsonPathClaimsActorOptions>` + `ValidateOnStart()` that a nested path is not configured without `ContainerClaim` (empty and whitespace-only both count as missing — they would otherwise silently fall back to flat-claim resolution and reintroduce the misconfiguration footgun). The constructor keeps the same `InvalidOperationException` guard as defense-in-depth for manual construction. `ValidateOnStart()` fires regardless of whether a later `AddXxxActorProvider` call replaces `IActorProvider`; see [`trellis-api-authorization.md`](trellis-api-authorization.md#common-identity-provider-claim-shapes) for the "register-then-replace" caveat.

| Signature | Returns | Description |
| --- | --- | --- |
| `public override Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Maybe<Actor>>` | Traverses the configured JSON paths when present, falls back to inherited flat-claim resolution when the container claim is absent, malformed, or a path misses, and emits the same silent-empty-permissions diagnostics as `ClaimsActorProvider`. |

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
| `public override Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Maybe<Actor>>` | Returns `Maybe<Actor>.None` when no authenticated identity exists or the configured ID claim is missing — the mediator pipeline maps that to `Error.AuthenticationRequired` (HTTP 401). When `IdClaimType` is the long objectidentifier claim, falls back to the short `"oid"` claim before returning `None`. Throws `InvalidOperationException` only when `HttpContext` is missing (configuration bug, surfaces as HTTP 500); any exception from `MapPermissions`, `MapForbiddenPermissions`, or `MapAttributes` is rewrapped in `InvalidOperationException` naming the failing delegate. |

### `TrellisInternalJwtActorOptions` / `TrellisInternalJwtActorProvider`

> **Moved.** These types lived under `Trellis.Asp.Authorization` in earlier previews. They moved to the new [`xavierjohn/Trellis.Microservices`](https://github.com/xavierjohn/Trellis.Microservices) repository in v3 as part of the carve-out that consolidates microservice trust-boundary code (gateway minter + consumer hydration). New namespace: `Trellis.Microservices.AspNetCore`. The API reference is now at [`trellis-api-internal-jwt.md`](https://github.com/xavierjohn/Trellis.Microservices/blob/main/docs/docfx_project/api_reference/trellis-api-internal-jwt.md).
>
> **Migration:** replace `using Trellis.Asp.Authorization;` (for the `TrellisInternalJwt*` types) with `using Trellis.Microservices.AspNetCore;`, add a `Trellis.Microservices.AspNetCore` NuGet reference, and replace `services.AddTrellis(b => b.UseTrellisInternalJwtActor(...))` with `services.AddTrellisInternalJwtActorProvider(...)` (the `UseTrellisInternalJwtActor` slot was removed from `TrellisServiceBuilder` in this v3 cleanup).

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
    ILogger<DevelopmentActorProvider> logger) : IActorProvider, IProvideActorVaryHeaders
```

Reads the `X-Test-Actor` header (JSON: `{ "Id": ..., "Permissions": [...], "ForbiddenPermissions": [...], "Attributes": {...} }`, case-insensitive property matching).

| Signature | Returns | Description |
| --- | --- | --- |
| `public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Maybe<Actor>>` | Throws `InvalidOperationException` whenever `!hostEnvironment.IsDevelopment()`, regardless of header presence. In Development, always returns `Maybe.From(actor)` — never `Maybe.None` — so dev workflows are unaffected by the 401 contract: `Maybe.From(Actor.Create(DefaultActorId, DefaultPermissions))` when `HttpContext` is null or the header is missing/empty, otherwise the parsed actor wrapped via `Maybe.From`. Malformed JSON logs a warning and falls back unless `ThrowOnMalformedHeader` is `true`. |

### `CachingActorProvider`

**Declaration**

```csharp
public sealed class CachingActorProvider : IActorProvider, IProvideActorVaryHeaders
```

Decorator that caches the inner provider's resolution task per request scope using `LazyInitializer.EnsureInitialized`. The shared task uses `HttpContext.RequestAborted` so expensive work (DB lookups) is canceled with the request, but individual callers' tokens only cancel their own awaits.

| Signature | Returns | Description |
| --- | --- | --- |
| `public CachingActorProvider(IActorProvider inner, IHttpContextAccessor httpContextAccessor)` | — | `inner` cannot be null. |
| `public Task<Maybe<Actor>> GetCurrentActorAsync(CancellationToken cancellationToken = default)` | `Task<Maybe<Actor>>` | Returns the cached task — including a cached `Maybe<Actor>.None` when the inner provider resolved no authenticated actor — so the inner provider runs once per request scope. If `cancellationToken` differs from `RequestAborted`, applies it via `Task.WaitAsync`. |

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

### `MaybePrimitiveModelBinder<T>`

**Declaration**

```csharp
public sealed class MaybePrimitiveModelBinder<T> : IModelBinder
    where T : notnull
```

Binds `Maybe<T>` parameters where `T` is a primitive in the closed allowed list (`string`, `decimal`, `int`, `long`, `short`, `byte`, `double`, `float`, `bool`, `Guid`, `DateTime`, `DateTimeOffset`) from route / query / form / header sources. Counterpart of `MaybeModelBinder<,>` for the no-scalar-VO case — the new `MaybePrimitiveJsonConverterFactory` handles the JSON body side of the same shape. The allowed list itself is exposed via the non-generic [`MaybePrimitives`](#maybeprimitives) helper so the `FrozenSet<Type>` is allocated once for the framework rather than once per closed generic instantiation.

| Signature | Returns | Description |
| --- | --- | --- |
| `public Task BindModelAsync(ModelBindingContext bindingContext)` | `Task` | Missing or empty value → `ModelBindingResult.Success(Maybe<T>.None)`. Parseable primitive → `ModelBindingResult.Success(Maybe.From(parsed))`. Unparseable → adds a model-state error and returns `ModelBindingResult.Failed()`. Parses using invariant culture and the typed `TryParse` methods on each primitive type. |

<a id="maybeprimitives"></a>

### `MaybePrimitives`

**Declaration**

```csharp
public static class MaybePrimitives
```

Non-generic holder for the closed `Maybe<T>` primitive allowed list shared by `MaybePrimitiveJsonConverterFactory` and `MaybePrimitiveModelBinder<T>`. Exists as a non-generic class so the `FrozenSet<Type>` is shared across all closed generic instantiations of the binder (avoids per-`T` allocation and the CA1000 "no static members on generic types" guidance).

| Signature | Returns | Description |
| --- | --- | --- |
| `public static readonly FrozenSet<Type> SupportedPrimitives` | `FrozenSet<Type>` | The 12-type allowed list: `string`, `decimal`, `int`, `long`, `short`, `byte`, `double`, `float`, `bool`, `Guid`, `DateTime`, `DateTimeOffset`. Used by both the JSON converter factory's `CanConvert` and the model binder provider's `GetBinder`. |

### `ScalarValueModelBinderProvider`

**Declaration**

```csharp
public class ScalarValueModelBinderProvider : IModelBinderProvider
```

| Signature | Returns | Description |
| --- | --- | --- |
| `public IModelBinder? GetBinder(ModelBinderProviderContext context)` | `IModelBinder?` | Returns a `MaybeModelBinder<,>` for `Maybe<TScalar>` where `TScalar : IScalarValue<,>`, a `MaybePrimitiveModelBinder<T>` for `Maybe<T>` where `T` is in `MaybePrimitives.SupportedPrimitives`, a `ScalarValueModelBinder<,>` for direct scalar values, or `null` otherwise. Annotated `[UnconditionalSuppressMessage]` for IL2070/IL2072/IL2075 and IL3050 — model binding is not Native AOT compatible. |

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

### `MaybePrimitiveJsonConverter<T>`

**Declaration**

```csharp
public sealed class MaybePrimitiveJsonConverter<T> : JsonConverter<Maybe<T>>
    where T : notnull
```

JSON converter for `Maybe<T>` where `T` is an STJ-native primitive in the closed allowed list enforced by `MaybePrimitiveJsonConverterFactory`. Reads dispatch on `typeof(T)` to typed `Utf8JsonReader` methods (`GetString` / `GetInt32` / `GetDecimal` / `GetGuid` / `GetDateTime` / etc.) — no reflection, no `JsonSerializer` round-trip, AOT-safe by construction. `null` token → `Maybe<T>.None`; primitive value → `Maybe.From(value)`. `None` writes as JSON `null`.

| Signature | Returns | Description |
| --- | --- | --- |
| `public override Maybe<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)` | `Maybe<T>` | Reads JSON `null` as `Maybe<T>.None`; otherwise dispatches on the closed primitive allowed list. Wrong JSON shape throws the standard `JsonException`. |
| `public override void Write(Utf8JsonWriter writer, Maybe<T> value, JsonSerializerOptions options)` | `void` | `Maybe.None` writes JSON `null`; otherwise switches on the unwrapped value and writes via the matching typed `Utf8JsonWriter` method. |

### `MaybePrimitiveJsonConverterFactory`

**Declaration**

```csharp
public sealed class MaybePrimitiveJsonConverterFactory : JsonConverterFactory
```

Closes the asymmetry where `MaybeScalarValueJsonConverterFactory` shipped support for `Maybe<TScalar>` (typed value objects) but `Maybe<long>` / `Maybe<int>` / `Maybe<string>` / `Maybe<DateTime>` etc. fell through to STJ's default object handling, producing JSON the converter cannot itself parse back. Registered by `AddScalarValueValidation()` (or the convenience helper `AddTrellisAspWithScalarValidation()`) alongside the scalar factory (same `JsonSerializer.IsReflectionEnabledByDefault` gate for AOT). The supported primitive set deliberately mirrors `CompositeValueObjectJsonConverter<T>`'s allowed list: the rule is "`Maybe<T>` works wherever `T` is a primitive Trellis already supports directly".

Supported primitives: `string`, `decimal`, `int`, `long`, `short`, `byte`, `double`, `float`, `bool`, `Guid`, `DateTime`, `DateTimeOffset`. Shapes outside this set (`DateOnly`, `TimeOnly`, unsigned numerics, arrays, collections, nested composites) continue to require the wire-shape DTO + adapter pattern (Cookbook Recipe 14).

| Signature | Returns | Description |
| --- | --- | --- |
| `public override bool CanConvert(Type typeToConvert)` | `bool` | `true` when `typeToConvert` is `Maybe<T>` and `T` is in the closed primitive allowed list. Returns `false` for `Maybe<TScalar>` (handled by `MaybeScalarValueJsonConverterFactory`) and for unsupported primitive shapes (e.g. `Maybe<DateOnly>`) so the two factories don't compete. |
| `public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)` | `JsonConverter?` | Builds `MaybePrimitiveJsonConverter<T>` for the inner primitive type. |

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
| `public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)` | `ValueTask<object?>` | For Minimal APIs, returns `Results.ValidationProblem(...)` with status `422` and MVC dot+bracket field keys when `ValidationErrorsContext` contains errors; otherwise invokes `next`. |

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
| `CurrentPropertyName` | `string?` (get/set) | Async-local property name for the property currently being deserialized. Set by `PropertyNameAwareConverter<T>` (reflection mode) and read by both the reflection-mode `ScalarValueJsonConverterBase<,,>` and the AOT-generated converter (which falls back to a camel-cased type name when this is `null`). |

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IDisposable BeginScope()` | `IDisposable` | Starts a new async-local validation collection scope; disposing restores the previous scope and property name. |
| `public static void AddError(string fieldName, string errorMessage)` | `void` | Appends a single field violation to the current scope. No-op when no scope is active. Called by both reflection-mode (`ScalarValueJsonConverterBase<,,>`) and AOT-generated converters when `TryCreate` returns a non-`Error.InvalidInput` failure. |
| `public static void AddError(Error.InvalidInput unprocessableContent)` | `void` | Merges every `FieldViolation` and `RuleViolation` in the supplied error into the current scope, preserving each violation's `ReasonCode`, `Args`, and `Detail`. No-op when no scope is active. Called by both reflection-mode and AOT-generated converters when `TryCreate` returns an `Error.InvalidInput` failure. |
| `public static Error.InvalidInput? GetUnprocessableContent()` | `Error.InvalidInput?` | Returns the aggregated `Error.InvalidInput` for the current scope (with `Fields` / `Rules` populated from collected `FieldViolation`s) or `null` when no errors were collected. |

## Behavioral notes

- **One verb, every shape.** `ToHttpResponse` is the only supported response mapper. The generic result types it constructs (`TrellisHttpResult<TDomain, TBody>`, `TrellisWriteOutcomeResult<TDomain, TBody>`, and the paged success wrapper) implement `IResult`; the non-paged generic result types also implement the `IStatusCodeHttpResult` / `IValueHttpResult<T>` / `IContentTypeHttpResult` metadata interfaces and `IEndpointMetadataProvider` so OpenAPI/ApiExplorer surfaces the success status, body type, and the union of error envelopes the writer can emit (`200`, `201`, `304`, `400`, `404`, `412`, `500`). Standalone `Error.ToHttpResponse(...)` uses `TrellisErrorOnlyResult`, an `IResult` failure writer. Layer your own `[ProducesResponseType]` / `Produces<T>` on top.
- **Failures use Problem Details.** A failure runs through `ResponseFailureWriter` (internal). `Error.InvalidInput` with field violations uses `Results.ValidationProblem(...)`; everything else uses `Results.Problem(...)`. The `errors` dictionary keys are the violation `Field.Path` translated from RFC 6901 JSON Pointer to ASP.NET Core MVC dot+bracket convention, and raw JSON Pointer values are preserved per rule under the top-level `rules` array's `fields[]` entries (populated via `ProblemDetails.Extensions["rules"]`). Companion headers are emitted automatically: `Allow` for `Error.TransportFault(new HttpError.MethodNotAllowed(...))`, `Content-Range: {Unit} */{CompleteLength}` for `Error.TransportFault(new HttpError.RangeNotSatisfiable(...))`, `Retry-After` from `RetryAdvice` on `Error.RateLimited` / `Error.Unavailable`, and `WWW-Authenticate` from `Error.AuthenticationRequired.Scheme` or the registered `IAuthenticationSchemeProvider` fallback when the resolved status is `401`. For `Error.TransportFault`, the top-level Problem Details extension members `code` and `kind` come from the wrapped `HttpError`, not the outer `transport-fault` envelope. Every failure response carries top-level `code` and `kind` (RFC 9457 §3.2 extension members, populated via `ProblemDetails.Extensions["code"]` / `["kind"]` and serialized at the JSON root, not nested under an `extensions` object); `Error.Unexpected` adds top-level `faultId` when set; rule violations are surfaced under the top-level `rules` array; `Error.Aggregate` adds top-level `errors`; every response also carries top-level `instance`. For `5xx` responses the public `detail` is always `"An internal error occurred."`.
- **Status code resolution precedence.** `WithErrorMapping(Func<Error, int>)` (per call) → `WithErrorMapping<TError>(int)` (per call, walks the type hierarchy) → `TrellisAspOptions` resolved from `HttpContext.RequestServices` (or `TrellisAspOptions.SystemDefault` if none registered) → `500 Internal Server Error`.
- **Conditional requests.** `EvaluatePreconditions()` runs only on `GET` / `HEAD` and only when at least one of `WithETag` / `WithLastModified` is configured. The internal `ConditionalRequestEvaluator` evaluates RFC 9110 preconditions in this order: `If-Match` (strong); else `If-Unmodified-Since`; then `If-None-Match` (weak); else `If-Modified-Since` for safe methods. Failed `If-Match` / `If-Unmodified-Since` → `412`; failed `If-None-Match` / `If-Modified-Since` on `GET`/`HEAD` → `304`.
- **`Vary` is append-only.** Both the `HonorPrefer()` switch and `Vary(...)` use `AppendVaryUnique` — they preserve any pre-existing `Vary` values added by other middleware and skip duplicates (case-insensitive).
- **`HonorPrefer()` semantics on `WriteOutcome.Updated`.** `HonorPrefer()` is opt-in. Without it, `Prefer` request headers are ignored entirely: no `Vary: Prefer`, no `Preference-Applied`, and `return=minimal` does **not** suppress the body. When `HonorPrefer()` is configured, `Prefer: return=minimal` short-circuits to `204 No Content` and emits `Preference-Applied: return=minimal`; `return=representation` returns `200 OK` with the body and emits `Preference-Applied: return=representation`. `Vary: Prefer` is always emitted under `HonorPrefer()`, regardless of which preference was sent.
- **`CreatedAtAction` is not AOT-safe.** It depends on MVC's `ControllerLinkGeneratorExtensions`. The builder method, the writer's `ResolveActionLocation` private, and the `LocationKind.Action` branch are annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Use `CreatedAtRoute` with a named route for trim/AOT scenarios; `ResolveActionLocation` throws `NotSupportedException` when `RuntimeFeature.IsDynamicCodeSupported` is `false`.
- **Pagination.** The `Result<Page<T>>` overload always emits the `PagedResponse<TBody>` envelope; the RFC 8288 `Link` header is added only when `Page.Next` and/or `Page.Previous` cursors are present. Failure on the page result short-circuits through the standard error pipeline.
- **Validation collection scope.** `ScalarValueValidationMiddleware` opens a `ValidationErrorsContext` scope per request. Both `ValidatingJsonConverter<,>` and `MaybeScalarValueJsonConverter<,>` collect errors into this scope; `ScalarValueValidationFilter` (MVC) and `ScalarValueValidationEndpointFilter` (Minimal API) short-circuit with a validation problem when the scope is non-empty at action/handler entry.
- **AOT-generated converters participate in the same scope.** When an assembly opts into the source generator (a partial `JsonSerializerContext` decorated with `[GenerateScalarValueConverters]` or any other `JsonSerializerContext` in a project that references the generator), the emitted `JsonConverter<TValue>`s mirror `ScalarValueJsonConverterBase<,,>` bit-for-bit — they read `ValidationErrorsContext.CurrentPropertyName` for the field name (falling back to the camel-cased type name when the AOT path has no `PropertyNameAwareConverter<T>` setting it), call `TryCreate(primitive, fieldName)`, and on failure call `ValidationErrorsContext.AddError(...)` (forwarding `Error.InvalidInput` directly to preserve `ReasonCode`/`Args`, otherwise recording the `Detail` string under `fieldName`). Without this, AOT consumers got `null` on validation failure while reflection-mode consumers got a 422 — a divergence that broke "one programming model" between the two modes. The factory `Trellis.Generated.GeneratedValueObjectConverterFactory` is emitted by the generator alongside the per-type converters; consumers wire it in by adding it to their `JsonSerializerOptions.Converters` collection (e.g. inside `AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new GeneratedValueObjectConverterFactory()))`). The `[GenerateScalarValueConverters]`-marked partial `JsonSerializerContext` extension only emits `[JsonSerializable]` attributes; it does not auto-register the factory.
- **Minimal API scalar binding failures are metadata-driven.** When ASP.NET Core throws a 400 while binding route/query parameters, `ScalarValueValidationMiddleware` no longer parses `BadHttpRequestException.Message` to discover a field name or invalid value. It inspects `IParameterBindingMetadata`, reads the matching route/query raw value, and re-runs Trellis scalar validation for `IScalarValue<,>` / `Maybe<TScalar>` parameters. Non-scalar endpoint binding failures are rethrown to ASP.NET Core.
- **`AddTrellisAsp` is error-mapping setup only.** It registers `TrellisAspOptions` and `ResourceCollectionNameRegistry`; it does **not** chain scalar-value validation. Call `AddTrellisAspWithScalarValidation()` for the combined setup, or call `AddTrellisAsp()` plus `AddScalarValueValidation()` explicitly. You still need `UseScalarValueValidation()` middleware in the request pipeline and `WithScalarValueValidation()` on each Minimal API endpoint that should short-circuit on validation errors.
- **Composite value objects in request/response DTOs.** `AddTrellisAsp`/`AddScalarValueValidation` only wires the **scalar** VO converters. Composite VOs (multi-field `[OwnedEntity]` types like `ShippingAddress`, `Money`) bind through `CompositeValueObjectJsonConverter<T>` (in `Trellis.Primitives`), which is **opt-in per type** via `[JsonConverter(typeof(CompositeValueObjectJsonConverter<MyVo>))]` on the value object class itself. Without that attribute, model binding falls back to default construction and **silently bypasses `TryCreate`** — the inner-field validation never runs and an invalid payload propagates into the domain layer. See [Cookbook Recipe 13](trellis-api-cookbook.md#recipe-13--composite-value-object-end-to-end-domain--api-json-binding--ef-core-ownership) for the full Domain + API JSON + EF pattern.

## Code examples

### Basic `Result<T>` → 200 / Problem Details

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrellisAspWithScalarValidation();

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

- [trellis-api-core.md](trellis-api-core.md#types) — `Result`, `Result<T>`, `Error`, `WriteOutcome<T>`, `Page<T>`, `RepresentationMetadata`, `EntityTagValue`, `Cursor`, `PreconditionKind`, `ResourceRef`.
- [trellis-api-authorization.md](trellis-api-authorization.md#types) — `Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<TResource>`, resource loaders.
- [trellis-api-primitives.md](trellis-api-primitives.md#types) — `IScalarValue<TSelf, TPrimitive>`, `Maybe<T>`.
- [trellis-api-http.md](trellis-api-http.md#type) — Pure HTTP value objects shared between hosts.
- [trellis-api-testing-aspnetcore.md](trellis-api-testing-aspnetcore.md#webapplicationfactoryextensions) — `WebApplicationFactoryExtensions.CreateClientWithActor` (writes the `X-Test-Actor` header consumed by `DevelopmentActorProvider`).
