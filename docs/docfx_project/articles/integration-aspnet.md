---
title: ASP.NET Core Integration
package: Trellis.Asp
topics: [asp, minimal-api, controllers, http-result, problem-details, etag, prefer, pagination, idempotency]
related_api_reference: [trellis-api-asp.md, trellis-api-core.md, trellis-api-asp-idempotency-cosmos.md]
last_verified: 2026-05-01
audience: [developer]
---
# ASP.NET Core Integration

`Trellis.Asp` maps `Result`, `Result<T>`, `Result<WriteOutcome<T>>`, and `Result<Page<T>>` to ASP.NET Core HTTP responses (status codes, Problem Details, ETags, `Prefer`, paginated envelopes) using the single verb `ToHttpResponse(...)`.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Map `Result<T>` to a Minimal API response | `result.ToHttpResponse(...)` / `ToHttpResponseAsync(...)` | [Quick start](#quick-start) |
| Project the response body separately from the domain value | `result.ToHttpResponse(body: domain => dto, configure: opts => ...)` | [Body projection](#body-projection) |
| Return `ActionResult<T>` from an MVC controller | `.AsActionResult<T>()` / `.AsActionResultAsync<T>()` | [MVC controllers](#mvc-controllers) |
| Map `Result<Unit>` (no payload) | Return `Result.Ok()` — `ToHttpResponse` emits `204 No Content` | [Result&lt;Unit&gt; → 204](#resultunit--204-no-content) |
| Override an error → status mapping for one endpoint | `opts.WithErrorMapping<TError>(status)` / `opts.WithErrorMapping(err => ...)` | [Error mapping](#error-mapping) |
| Override mappings globally | `AddTrellisAsp(opts => opts.MapError<TError>(status))` | [Error mapping](#error-mapping) |
| Conditional `GET`/`HEAD` (`If-None-Match`, `If-Modified-Since`) | `opts.WithETag(...).EvaluatePreconditions()` | [Conditional requests](#conditional-requests) |
| Honor `Prefer: return=minimal` / `return=representation` | `opts.HonorPrefer()` on a `WriteOutcome` response | [Prefer header](#prefer-header) |
| Emit `201 Created` with a `Location` header | `opts.CreatedAtRoute(name, values)` (AOT-safe) / `Created(...)` / `CreatedAtAction(...)` | [Created responses](#created-responses) |
| Return paginated JSON + RFC 8288 `Link` header | `Result<Page<T>>.ToHttpResponse(nextUrlBuilder, body)` | [Pagination](#pagination) |
| Make `POST` / `PATCH` retry-safe with the IETF `Idempotency-Key` header | `AddTrellisIdempotency` + `AddInMemoryIdempotencyStore` + `UseTrellisIdempotency`; mark endpoints `[Idempotent]` | [Idempotency-Key middleware](#idempotency-key-middleware) |
| Make idempotency survive restarts and span replicas | `AddCosmosIdempotencyStore(...)` (`Trellis.Asp.Idempotency.Cosmos`) | [Choosing a store](#idempotency-key-middleware) |
| Verify a custom `IIdempotencyStore` against the contract | Derive from `IdempotencyStoreConformance` (`Trellis.Testing.Idempotency`) | [Choosing a store](#idempotency-key-middleware) |
| Validate scalar value objects (route, query, JSON body) | `AddScalarValueValidation` + `UseScalarValueValidation` + `WithScalarValueValidation` | [Scalar value validation](#scalar-value-validation) |
| Bind value objects in route segments | `AddTrellisRouteConstraint<T>("Name")` then `"/x/{id:Name}"` | [Route constraints](#route-constraints) |
| Hydrate the current `Actor` from JWT claims | `AddClaimsActorProvider` / `AddEntraActorProvider` / `AddDevelopmentActorProvider` / `AddNestedJsonPathClaimsActorProvider` | [Actor providers](#actor-providers) |

## Use this guide when

- Your application returns `Result<T>` and you need predictable HTTP status, Problem Details, and conditional-request behavior at the boundary.
- You are wiring Minimal API endpoints or MVC controllers and want one verb (`ToHttpResponse`) instead of a `switch`-per-endpoint.
- You need ETag, `If-Match` / `If-None-Match`, `Prefer`, or paginated-list semantics that match RFC 9110 / 7240 / 8288 without hand-rolling header parsing.
- You bind scalar value objects (`IScalarValue<TSelf, TPrimitive>`) from routes, queries, or JSON bodies and want validation collected as `Error.InvalidInput`.
- You hydrate the current `Actor` from JWT/OIDC claims for downstream authorization checks.

## Surface at a glance

| Type | Purpose |
|---|---|
| `HttpResponseExtensions` | `ToHttpResponse` / `ToHttpResponseAsync` for `Error`, `Result<T>`, `Result<WriteOutcome<T>>`, `Result<Page<T>>` (with optional body projector). |
| `HttpResponseOptionsBuilder<TDomain>` | Fluent options for the generic overloads (`WithETag`, `WithLastModified`, `Vary`, `Created`/`CreatedAtRoute`/`CreatedAtAction`, `EvaluatePreconditions`, `HonorPrefer`, `WithErrorMapping`, …). |
| `HttpResponseOptionsBuilder` | Non-generic builder for the `Error` overload (`Vary`, `HonorPrefer`, `WithErrorMapping`). |
| `ActionResultAdapterExtensions` | `AsActionResult<T>` / `AsActionResultAsync<T>` to wrap an `IResult` for MVC. |
| `TrellisAspOptions` | DI-registered error-type → status-code map; configure via `AddTrellisAsp(opts => opts.MapError<TError>(status))`. |
| `ETagHelper` | `ParseIfMatch` / `ParseIfNoneMatch` returning `EntityTagValue[]?`; `IfMatchSatisfied` / `IfNoneMatchMatches` comparison helpers. |
| `IfNoneMatchExtensions` | `EnforceIfNoneMatchPrecondition(EntityTagValue[]?)` — converts a successful result into `Error.TransportFault(new HttpError.PreconditionFailed(...))` when `If-None-Match: *` is sent and the resource exists. |
| `PreferHeader` | `Parse(HttpRequest)` → `ReturnRepresentation`, `ReturnMinimal`, `RespondAsync`, `Wait`, `HandlingStrict`, `HandlingLenient`, `HasPreferences`. |
| `PagedResponse<TResponse>` / `PageLink` | JSON envelope and `Link` header entries returned by the `Result<Page<T>>` overload. |
| `Trellis.Asp.Authorization.*` | `AddClaimsActorProvider` / `AddEntraActorProvider` / `AddDevelopmentActorProvider` / `AddNestedJsonPathClaimsActorProvider` / `AddCachingActorProvider<T>`. |
| `Trellis.Asp.ModelBinding.*` | `ScalarValueModelBinder<,>` / `MaybeModelBinder<,>` / `ScalarValueModelBinderProvider`. |
| `Trellis.Asp.Routing.*` | `TrellisValueObjectRouteConstraint<T>` + `AddTrellisRouteConstraint<T>` / `AddTrellisRouteConstraints`. |
| `Trellis.Asp.Validation.*` | `ValidatingJsonConverter<,>`, `MaybeScalarValueJsonConverter<,>`, `ScalarValueValidationFilter` (MVC), `ScalarValueValidationEndpointFilter` (Minimal API), `ScalarValueValidationMiddleware`, `ValidationErrorsContext`. |

Full signatures: [trellis-api-asp.md](../api_reference/trellis-api-asp.md).

## Installation

```bash
dotnet add package Trellis.Asp
```

`Trellis.Asp` bundles the AOT-friendly `Trellis.AspSourceGenerator.dll` (attached automatically) and contains the actor providers formerly published as `Trellis.Asp.Authorization`.

## Quick start

A composition root that wires Trellis.Asp once, then a Minimal API endpoint that returns `Result<T>`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Trellis;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrellisAspWithScalarValidation();

var app = builder.Build();
app.UseScalarValueValidation();

app.MapGet("/users/{id}", async (string id, IUserService users, CancellationToken ct) =>
        (await users.GetByIdAsync(id, ct))
            .ToHttpResponse(user => new UserResponse(user.Id, user.Email)))
    .WithName("Users_GetById")
    .WithScalarValueValidation();

app.Run();

public sealed record User(string Id, string Email);
public sealed record UserResponse(string Id, string Email);

public interface IUserService
{
    Task<Result<User>> GetByIdAsync(string id, CancellationToken ct);
}
```

Behavior:

- Success → `200 OK` with the projected `UserResponse` body.
- `Error.NotFound` → `404 Not Found` Problem Details.
- `Error.InvalidInput` → `422 Unprocessable Content` validation Problem Details (the default; configurable via `MapError<Error.InvalidInput>(status)`, which applies uniformly to route/query binder, JSON-body, and handler validation failures — syntactically malformed JSON stays `400`).
- Any other failure → status from `TrellisAspOptions` (default `500`).

## `Result<Unit>` → 204 No Content

Side-effecting commands return `Result<Unit>` from `Result.Ok()` / `Result.Fail(error)`. `Trellis.Asp` maps a successful `Result<Unit>` to `204 No Content` automatically — no body, no projector required.

```csharp
app.MapDelete("/users/{id}", async (string id, IUserService users, CancellationToken ct) =>
    (await users.DeleteAsync(id, ct)).ToHttpResponse());

public interface IUserService
{
    Task<Result<Unit>> DeleteAsync(string id, CancellationToken ct);
}
```

## MVC controllers

For MVC, convert with `ToHttpResponse` / `ToHttpResponseAsync`, then adapt with `AsActionResult<T>` / `AsActionResultAsync<T>` so the action signature stays `Task<ActionResult<T>>` for OpenAPI / `[ProducesResponseType<T>]`.

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Trellis;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddControllers()
    .AddScalarValueValidation();
builder.Services.AddTrellisAsp();

var app = builder.Build();
app.UseScalarValueValidation();
app.MapControllers();
app.Run();

[ApiController]
[Route("users")]
public sealed class UsersController(IUserService users) : ControllerBase
{
    [HttpGet("{id}", Name = nameof(GetById))]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ActionResult<UserResponse>> GetById(string id, CancellationToken ct) =>
        users.GetByIdAsync(id, ct)
            .ToHttpResponseAsync(user => new UserResponse(user.Id, user.Email))
            .AsActionResultAsync<UserResponse>();

    [HttpPost]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken ct) =>
        users.CreateAsync(request, ct)
            .ToHttpResponseAsync(
                body: user => new UserResponse(user.Id, user.Email),
                configure: opts => opts.CreatedAtRoute(
                    nameof(GetById),
                    user => new RouteValueDictionary { ["id"] = user.Id }))
            .AsActionResultAsync<UserResponse>();
}

public sealed record User(string Id, string Email);
public sealed record CreateUserRequest(string Email);
public sealed record UserResponse(string Id, string Email);

public interface IUserService
{
    Task<Result<User>> GetByIdAsync(string id, CancellationToken ct);
    Task<Result<User>> CreateAsync(CreateUserRequest request, CancellationToken ct);
}
```

> [!NOTE]
> `AsActionResult<T>` only exists for the generic `ActionResult<T>` shape. For value-less responses, return `IResult` directly from a Minimal API endpoint or use `IActionResult` in MVC.

## Body projection

Every generic `ToHttpResponse` overload accepts an optional `body: Func<TDomain, TBody>` projector. The selectors in the options builder (`WithETag`, `WithLastModified`, `Created(selector)`, `CreatedAtRoute(values)`, `WithContentLocation`) **always run against the domain value**, not the projected body. This keeps response DTOs free of representation concerns:

```csharp
return result.ToHttpResponse(
    body: product => new ProductResponse(product.Id.Value, product.Name.Value, product.Price.Value),
    configure: opts => opts
        .WithETag(product => product.ETag)
        .CreatedAtRoute("Products_GetById", product => new RouteValueDictionary { ["id"] = product.Id.Value }));
```

There is **no** `WithBody(...)` builder method. Pass the projector as the second positional argument to `ToHttpResponse` instead.

## Error mapping

Trellis.Core.Error is transport-neutral. The ASP boundary translates domain failures to HTTP per the table below.

| Domain case | HTTP status | wire `kind` | Headers |
|---|---|---|---|
| `Error.InvalidInput` | `422` | `unprocessable-content` | — |
| `Error.InvariantViolation` | `422` | `unprocessable-content` | — |
| `Error.NotFound` | `404` | `not-found` | — |
| `Error.Forbidden` | `403` | `forbidden` | — |
| `Error.Conflict` + `Code == FaultCodes.ConcurrentModification` and request had `If-Match` | `412` | `precondition-failed` | — |
| `Error.Conflict` | `409` | `conflict` | — |
| `Error.Gone` | `410` | `gone` | — |
| `Error.AuthenticationRequired` | `401` | `unauthorized` | `WWW-Authenticate` |
| `Error.Unavailable` | `503` | `service-unavailable` | `Retry-After` |
| `Error.RateLimited` | `429` | `too-many-requests` | `Retry-After` |
| `Error.Unexpected` (`Code == FaultCodes.NotImplemented`) | `501` | `not-implemented` | — |
| `Error.Unexpected` | `500` | `internal-server-error` | `faultId` when set |
| `Error.Aggregate` | worst child | `multi` | per-child |
| `Error.TransportFault` wrapping `HttpError.*` | `405/406/412/413/415/416/428` | inner wire kind | `Allow` / `Content-Range` / inner-specific |

Domain `Kind` and wire `kind` are intentionally distinct for `Error.InvalidInput` and `Error.InvariantViolation`: the domain slugs remain `invalid-input` / `invariant-violation`, while the on-wire `kind` stays `unprocessable-content` for backward compatibility.

Override globally:

```csharp
builder.Services.AddTrellisAsp(opts =>
{
    opts.MapError<Error.Conflict>(StatusCodes.Status400BadRequest);
});
```

Override per call (highest precedence first):

```csharp
return result.ToHttpResponse(
    body: order => new OrderResponse(order.Id),
    configure: opts => opts
        .WithErrorMapping<Error.Conflict>(StatusCodes.Status409Conflict)
        .WithErrorMapping(err => err is Error.NotFound ? StatusCodes.Status410Gone : default));
```

Resolution order: `WithErrorMapping(Func<Error,int>)` → `WithErrorMapping<TError>(int)` → `TrellisAspOptions.MapError<TError>(int)` → `500`.

## Problem Details output

Failures are emitted as `application/problem+json`. `Error.InvalidInput` with field violations uses `Results.ValidationProblem(...)`; everything else uses `Results.Problem(...)`. Companion headers are synthesized automatically from the error payload: `Allow` for `HttpError.MethodNotAllowed`, `Content-Range` for `HttpError.RangeNotSatisfiable`, `Retry-After` from `RetryAdvice` on `Error.RateLimited` / `Error.Unavailable`, and `WWW-Authenticate` from `Error.AuthenticationRequired.Scheme` or the registered `IAuthenticationSchemeProvider` fallback.

### Wiring the canonical Trellis enrichment

`Trellis.Asp` ships an opinionated ProblemDetails pipeline that adds a `traceId` extension (from `Activity.Current?.Id` falling back to `HttpContext.TraceIdentifier`), rewrites the 500 `detail` to a support-friendly message that does not leak exception internals, and projects the `Allow` header into a structured `extensions["allow"]` array on 405 responses. Wire it once at startup:

```csharp
builder.Services.AddTrellisProblemDetails();

var app = builder.Build();
app.UseTrellisProblemDetails();
```

`AddTrellisProblemDetails()` is idempotent and composes with `services.AddProblemDetails()` — register either or both in any order. Consumers override individual defaults by setting `ProblemDetailsOptions.CustomizeProblemDetails` either before or after `AddTrellisProblemDetails()`; Trellis defaults run first, the consumer's customization runs last. `UseTrellisProblemDetails()` wires the standard `UseExceptionHandler` + `UseStatusCodePages` pipeline so ASP.NET short-circuits (404 / 405 / 415) surface as ProblemDetails bodies.

Hosts that compose Trellis via `Trellis.ServiceDefaults` get the same wiring through the builder slot:

```csharp
builder.Services.AddTrellis(t => t.UseProblemDetails());
```

`UseProblemDetails()` defers to `AddTrellisProblemDetails()` internally, so the same defaults and override hooks apply.

### Wire shape

`Error.Aggregate` renders as one outer Problem Details object with the worst child status and a `problems[]` extension containing one child object per inner error. Each child is a full Problem Details object — `type` (a URI reference: ASP.NET Core's default problem type for the child's own status, omitted when the framework has no default for that status, exactly as the root omits it), `status`, `code`, `kind`, `detail`, plus any child-specific extensions such as a nested validation problem's `errors` map and `rules` array, or an `Unexpected`'s `faultId`. (This extension was named `errors` before v-next; it was renamed because it collided with the flat field-violation `errors` map.) `Error.TransportFault` projects the wrapped `HttpError` payload into `extensions` and uses the inner `code` / `kind`, not the outer `transport-fault` envelope. Every response also carries `instance`.

The top-level `code` is `error.unspecified` whenever the error carries no machine-readable reason of its own; it is the error's own `Code` only when it does. Branch on `kind` or `status` for the HTTP condition, and on `code` only for a genuine domain reason.

The two ProblemDetails-producing paths use different 5xx detail messages:

- **Trellis-`Result<T>`-converted failures** (wire shapes shown below) — `ResponseFailureWriter` replaces the public `detail` on every `5xx` response with `"An internal error occurred."`.
- **ASP.NET Core's ProblemDetails pipeline** (uncaught exceptions surfaced via `UseExceptionHandler`, status-code short-circuits via `UseStatusCodePages`) — when `AddTrellisProblemDetails()` is wired (see recipe above), the 500 `detail` is rewritten to `"An error occurred in our API. Please share the trace id with our support team."` so clients can quote the trace id back to support without leaking exception internals.

`Error.InvalidInput` is routed to `Results.ValidationProblem(...)`:

```http
HTTP/1.1 422 Unprocessable Content
Content-Type: application/problem+json

{
  "title": "One or more validation errors occurred.",
  "status": 422,
  "instance": "/api/customers?api-version=2026-11-12",
  "code": "error.unspecified",
  "kind": "unprocessable-content",
  "errors": {
    "email": ["Email is required"]
  }
}
```

The `errors` dictionary keys are each `FieldViolation.Field.Path` translated from RFC 6901 JSON Pointer (e.g. `/items/0/name`) to the dot+bracket convention used by ASP.NET Core's default `ValidationProblemDetails` (e.g. `items[0].name`). RFC 6901 escapes (`~1` → `/`, `~0` → `~`) are decoded so segments containing literal `/` or `~` appear correctly. Values are `FieldViolation.Detail ?? FieldViolation.ReasonCode`. The wire shape matches what `[ApiController]` emits in its automatic 400 responses, so OpenAPI codegen consumers (axios + react-query, NSwag, etc.) and React form libraries (`react-hook-form`, Formik) can lookup `setError(name, ...)` directly without a slash → dot translation shim. The original JSON Pointer is preserved verbatim alongside that map: per field inside `extensions["fieldViolations"][n].location`, and per rule inside `extensions["ruleViolations"][n].locations[]`. (`extensions["problems"]` is a different thing — it carries the child problems of an aggregate error, not field locations.)

## Created responses

Three options for `201 Created`. Pick by your AOT requirement and the link source.

| Builder | Location source | AOT-safe |
|---|---|---|
| `Created(string locationLiteral)` | Caller-supplied literal | Yes |
| `Created(Func<TDomain, string> selector)` | Selector over the domain value | Yes |
| `CreatedAtRoute(string routeName, Func<TDomain, RouteValueDictionary> routeValues)` | Relative path from `LinkGenerator.GetPathByName` (resolved at execute time) | Yes |
| `CreatedAtAction(string actionName, Func<TDomain, RouteValueDictionary> routeValues, string? controllerName = null)` | Relative path from `LinkGenerator.GetPathByAction` | **No** — `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` |

```csharp
app.MapPost("/products", async (CreateProduct cmd, IProductWriter writer, CancellationToken ct) =>
        (await writer.CreateAsync(cmd, ct)).ToHttpResponse(
            body: product => new ProductResponse(product.Id.Value, product.Name.Value),
            configure: opts => opts.CreatedAtRoute(
                "Products_GetById",
                product => new RouteValueDictionary { ["id"] = product.Id.Value })))
    .WithName("Products_Create");
```

> [!WARNING]
> Under query-string or header API versioning, the route values dictionary **must** include `["api-version"] = ApiVersion`. Without it the emitted `Location` header omits the version and 404s on dereference (the response itself looks correct, so tests pass). The recommended path is to chain `.WithVersionedRoute()` from [`Trellis.Asp.ApiVersioning`](#api-version-aware-location-headers), which injects the version automatically. The [`TRLS023`](analyzers/TRLS023.md) analyzer warns on bare `CreatedAtRoute` / `CreatedAtAction` / `WithLocation` calls inside `[ApiVersion]`-decorated controllers and offers a code fix that appends `.WithVersionedRoute()`.

### API-version-aware `Location` headers

The `Trellis.Asp.ApiVersioning` package adds a `WithVersionedRoute()` extension on `HttpResponseOptionsBuilder<T>` that injects the requested `api-version` into the generated `Location` URL automatically — eliminating the recurring "201 looks correct, GET 404s" bug under query/header versioning. Chain it after any builder method that emits a builder-generated `Location` header — `CreatedAtRoute(...)` / `CreatedAtAction(...)` for 201 Created, `WithLocation(...)` for 2xx state-transition responses on existing resources.

```csharp
using Trellis.Asp.ApiVersioning;

[ApiController]
[ApiVersion("2026-12-01")]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Orders_GetById")]
    public IActionResult Get(int id) => Ok(...);

    [HttpPost]
    public ActionResult<Order> Create([FromBody] CreateOrderRequest req) =>
        _mediator.Send(new CreateOrderCommand(req))
            .ToHttpResponse(opts => opts
                .CreatedAtRoute(
                    "Orders_GetById",
                    o => new RouteValueDictionary { ["id"] = o.Id })
                .WithVersionedRoute())
            .AsActionResult<Order>();
}
```

The resolver runs per request inside the `LinkGenerator` callback. Resolution order:

1. **`HttpContext.RequestedApiVersion`** — primary signal; reflects whatever the configured `IApiVersionReader` parsed (query, header, media-type, composite).
2. **Endpoint metadata** — when (1) is null and exactly one declared version exists, fall back to it.
3. **`ApiVersioningOptions.DefaultApiVersion`** — final fallback. If a multi-version action has no client-supplied version and no default, the resolver throws rather than silently picking one.

The resolver short-circuits to a no-op (no `api-version` injected) for `[ApiVersionNeutral]` endpoints and URL-segment versioning (route template contains `:apiVersion`). Two overloads:

| Overload | Use case |
|---|---|
| `WithVersionedRoute()` | Per-request resolution; the default. |
| `WithVersionedRoute(ApiVersion explicitVersion)` | Pin the `Location` to a specific `ApiVersion` regardless of client request — used for cross-version redirects on deprecated endpoints. |

The underlying `WithRouteValueResolver(string key, Func<HttpContext, string?> resolver)` hook on `HttpResponseOptionsBuilder<T>` is also exposed publicly for any other cross-cutting per-request route-value injection (tenant id, request culture, etc.).

> [!NOTE]
> See [`trellis-api-asp-apiversioning.md`](../api_reference/trellis-api-asp-apiversioning.md) for the full LLM-targeted reference and [`TRLS023`](analyzers/TRLS023.md) for the analyzer that catches missed migrations.

#### Mid-migration diagnostic

When a host removes `services.AddApiVersioning(...)` but leaves `.WithVersionedRoute()` chains in place — e.g. mid-rollback of a versioning experiment, or moving a controller into an unversioned project — the resolver silently drops `api-version` injection. That keeps the helper composable in greenfield unversioned hosts, but in the mid-migration case it hides a real downstream contract regression (clients stop seeing `api-version` in `Location` headers).

The framework logs a single warning per `(endpoint, AppDomain)` pair via the `Microsoft.Extensions.Logging` category `Trellis.Asp.ApiVersioning` the first time each affected endpoint serves a request:

```text
.WithVersionedRoute() ran for endpoint 'MyApp.OrdersController.Create (...)' but the endpoint
has no ApiVersionMetadata attached — the host did not call services.AddApiVersioning(...),
or this endpoint sits outside its surface. The api-version route value is being silently
omitted from the response Location/route URL. If this host is intentionally unversioned,
remove the .WithVersionedRoute() chain. To fail fast on this configuration instead of logging
once per endpoint, configure TrellisAspOptions.FailFastOnSilentVersionInjection = true.
```

To surface the misconfiguration as a hard failure during integration tests or non-production runs, opt into fail-fast on `TrellisAspOptions`:

```csharp
builder.Services.AddTrellisAsp(o =>
    o.FailFastOnSilentVersionInjection = builder.Environment.IsDevelopment());
```

With fail-fast enabled, every request whose `.WithVersionedRoute()` chain would silently drop injection throws `InvalidOperationException` (de-duplication does not apply). Leave it off in Production so a stale chain produces a single observability signal rather than a request-storm of 500s.

The diagnostic only fires for the missing-metadata case. `[ApiVersionNeutral]` endpoints and URL-segment-versioned routes are intentional skips and stay silent.

### `WriteOutcome<T>`

When commands return `Result<WriteOutcome<T>>`, the response is RFC 9110-shaped from the variant:

| `WriteOutcome<T>` | Status |
|---|---|
| `Created(value)` | `201 Created` (plus `Location` from `Created`/`CreatedAtRoute`/`CreatedAtAction`) |
| `Updated(value)` | `200 OK` (or `204 No Content` with `Prefer: return=minimal` and `HonorPrefer()`) |
| `UpdatedNoContent` | `204 No Content` |
| `Accepted(value)` | `202 Accepted` (with `Retry-After` when configured) |
| `AcceptedNoContent` | `202 Accepted` |

```csharp
app.MapPut("/orders/{id:guid}", async (
        Guid id,
        UpdateOrderRequest request,
        IOrderService orders,
        CancellationToken ct) =>
    (await orders.UpdateAsync(id, request, ct)).ToHttpResponse(
        body: order => new OrderResponse(order.Id, order.Total),
        configure: opts => opts
            .WithETag(order => order.ETag)
            .HonorPrefer()));
```

## Conditional requests

`EvaluatePreconditions()` runs only on `GET` / `HEAD` and only when at least one selector (`WithETag` / `WithLastModified`) is configured. Evaluation order (RFC 9110): `If-Match` → `If-Unmodified-Since` → `If-None-Match` → `If-Modified-Since`. Failed `If-Match` / `If-Unmodified-Since` → `412`; failed `If-None-Match` / `If-Modified-Since` on `GET`/`HEAD` → `304`.

```csharp
app.MapGet("/products/{id:guid}", async (Guid id, IProductReader reader, CancellationToken ct) =>
    (await reader.GetAsync(id, ct)).ToHttpResponse(
        body: product => new ProductResponse(product.Id, product.Name, product.Price, product.ETag),
        configure: opts => opts
            .WithETag(product => product.ETag)
            .WithLastModified(product => product.UpdatedAt)
            .EvaluatePreconditions()));
```

For unsafe methods (`PUT`, `POST`), evaluate preconditions **before** the mutation. Use the typed parsers from `ETagHelper`:

```csharp
using Trellis.Asp;

EntityTagValue[]? ifMatch = ETagHelper.ParseIfMatch(httpContext.Request);
EntityTagValue[]? ifNoneMatch = ETagHelper.ParseIfNoneMatch(httpContext.Request);
```

`ParseIfMatch` returns `null` (header absent), `[]` (present but empty / weak-only — strong-only enforcement), the wildcard, or the parsed strong tags. `ParseIfNoneMatch` returns `null`, `[]`, the wildcard, or the parsed strong/weak tags.

The aggregate-side concurrency helpers `OptionalETag(...)` / `RequireETag(...)` consume `EntityTagValue[]?`. They live in `Trellis.Http.Abstractions` — see [`trellis-api-http-abstractions.md`](../api_reference/trellis-api-http-abstractions.md).

For "create only if absent" flows (`PUT` / `POST` with `If-None-Match: *`), use `EnforceIfNoneMatchPrecondition`:

```csharp
using Trellis.Asp;

var guarded = result.EnforceIfNoneMatchPrecondition(
    ETagHelper.ParseIfNoneMatch(httpContext.Request));
```

When the header contains `*` and the result is currently a success, it is replaced with `Error.TransportFault(new HttpError.PreconditionFailed(...))` using `PreconditionKind.IfNoneMatch`. No-op otherwise.

## Prefer header

`HonorPrefer()` honors RFC 7240 `Prefer: return=minimal` / `return=representation` on a `WriteOutcome` response. It always emits `Vary: Prefer`; `Preference-Applied` is emitted only when Trellis honored a preference.

| Sent header | Effect on `WriteOutcome.Updated` |
|---|---|
| `Prefer: return=minimal` | `204 No Content` + `Preference-Applied: return=minimal` |
| `Prefer: return=representation` | `200 OK` with body + `Preference-Applied: return=representation` |
| (none) | `200 OK` with body (no `Preference-Applied`) |

For raw access to the parsed header:

```csharp
using Trellis.Asp;

var prefer = PreferHeader.Parse(httpContext.Request);
if (prefer.ReturnMinimal) { /* … */ }
```

> [!NOTE]
> `PreferHeader.HasPreferences` is `true` only when at least one **recognized** standard preference (`return`, `respond-async`, `wait`, `handling`) was parsed. Unknown tokens do not set it.

## Pagination

The `Result<Page<T>>` overload always emits a `PagedResponse<TBody>` JSON envelope. The RFC 8288 `Link` header is added only when `Page.Next` and/or `Page.Previous` cursors are present.

```csharp
app.MapGet("/products", async (
        string? cursor,
        int? limit,
        IProductReader reader,
        HttpContext ctx,
        CancellationToken ct) =>
    (await reader.ListAsync(cursor, limit ?? 50, ct)).ToHttpResponse(
        nextUrlBuilder: (next, applied) =>
            $"{ctx.Request.Scheme}://{ctx.Request.Host}/products?cursor={next.Token}&limit={applied}",
        body: product => new ProductResponse(product.Id, product.Name)));

public sealed record ProductResponse(string Id, string Name);

public interface IProductReader
{
    Task<Result<Page<Product>>> ListAsync(string? cursor, int limit, CancellationToken ct);
}
```

Failure on the page result short-circuits through the standard error pipeline (Problem Details, default mapping).

## Idempotency-Key middleware

Opt-in middleware that makes `POST` / `PATCH` retry-safe by honouring the IETF `Idempotency-Key` request header (draft `draft-ietf-httpapi-idempotency-key-header`). On the first request the middleware reserves the key, lets the handler run, and captures the response. A retry under the same key with the same request fingerprint replays the captured response verbatim; a retry with a *different* request body under the same key returns `422 Unprocessable Entity` per Stripe / Adyen convention.

```csharp
using Trellis.Asp;
using Trellis.Asp.Idempotency;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrellis(t => t.UseAsp());
builder.Services.AddTrellisIdempotency();        // options: AddTrellisIdempotency(o => { o.Ttl = TimeSpan.FromHours(1); })
builder.Services.AddInMemoryIdempotencyStore();  // dev / single instance; see below for production stores

var app = builder.Build();
app.UseTrellisIdempotency();                     // before MapControllers / endpoint mapping

app.MapPost("/orders", ([FromBody] CreateOrder cmd, IOrderService svc, CancellationToken ct) =>
    svc.PlaceAsync(cmd, ct).ToHttpResponseAsync(opts => opts.CreatedAtRoute("Orders_Get", new { id = "{id}" })))
   .WithMetadata(new IdempotentAttribute());
```

For MVC controllers add `[Idempotent]` to the action method instead of `.WithMetadata(...)`.

**Wire contract**

| Behaviour | Detail |
|---|---|
| Header name | `Idempotency-Key` (configurable via `IdempotencyOptions.HeaderName`). Accepts both bare `tchar` tokens and RFC 8941 quoted strings. |
| Missing header on an opted-in endpoint | `400 idempotency.key_required` Problem Details. Set `RequireKeyOnOptedInEndpoints = false` to let missing-key requests pass through unchanged. |
| First request | Reserves the key, runs the handler, captures the response (status + headers + body), and stores the snapshot under TTL (default 24 h). |
| Retry with same key + same fingerprint | Replays the captured response and adds `Idempotent-Replayed: true` (header name configurable via `ReplayHeaderName`). |
| Retry with same key + different fingerprint | `422 Unprocessable Entity` (status configurable via `MismatchStatusCode`); the fresh request is **not** executed. "Different fingerprint" covers any change to method, `PathBase + Path`, canonical query, `Content-Type`, `Content-Encoding`, configured additional headers, or body bytes. |
| Retry while first is still in flight | While the original reservation is within `ReservationTimeout` (default 30 s), the retry returns `409 Conflict` with `Retry-After` and a Problem Details document carrying `code: idempotency.in_flight` (and, like every Trellis failure, the `type` ASP.NET Core assigns a `409`). Once that window has elapsed, a same-key retry with a matching fingerprint creates a fresh reservation and re-executes the handler; if the original request later finishes, its stale-reservation completion is ignored by the store. A mismatched-fingerprint retry instead surfaces as `422` per the row above. |
| Methods | `POST` and `PATCH` by default; `PUT` / `DELETE` are already idempotent per RFC 9110 and pass through. Override via `IdempotencyOptions.Methods`. |
| Store key | `(scope, idempotency key)` — the scope partitions the keyspace so the same literal key under different actors / tenants never collides. |
| Request fingerprint | `(method, PathBase + Path, canonical query, Content-Type, Content-Encoding, configured AdditionalFingerprintHeaders, body bytes)`. Multi-tenant hosts that mount the same routes under different `PathBase` values therefore never collide even when scope and key match. |
| Scope | Resolved per-request by `IIdempotencyScopeResolver`. Default `DefaultIdempotencyScopeResolver` uses the current `Actor` id when an `IActorProvider` is registered, falling back to anonymous. Replace with a custom implementation for multi-tenant hosts. |
| Response capture | `Set-Cookie`, `Date`, `Server`, and hop-by-hop headers (`Connection`, `Keep-Alive`, `Transfer-Encoding`, `Upgrade`, `Proxy-Authenticate`, `Proxy-Authorization`, `TE`, `Trailer`) are stripped from the snapshot. Cookies stay out by default so a replay does not re-issue rotated session tokens; opt back in via `IncludeSetCookieInSnapshot = true`. |
| Failure paths | 5xx responses, response trailers, `SendFileAsync`, oversized request / response bodies (>1 MiB by default), and unhandled exceptions abandon the reservation so the next retry re-executes. |

**`IIdempotencyStore`** is the persistence seam.

| Store | Package | Use when |
|---|---|---|
| `AddInMemoryIdempotencyStore()` | `Trellis.Asp` | Single-instance dev hosts and tests. Sweeps completed-and-expired snapshots opportunistically so memory stays bounded by `Ttl`. **Not safe across instances or process restarts.** |
| `AddCosmosIdempotencyStore(...)` | `Trellis.Asp.Idempotency.Cosmos` | Production hosts that retry across replicas. Reservations are atomic (`CreateItem` → `409`), completion and abandonment are ETag-conditional, and per-item TTL reclaims storage. |
| Your own | — | Any other backing store. Derive a test class from `IdempotencyStoreConformance` in `Trellis.Testing.Idempotency` to check it against all 17 contract rules. |

Choosing a store is a one-line decision alongside `AddTrellisIdempotency()`; it does not participate in pipeline ordering, so there is no `TrellisServiceBuilder.UseXxx()` slot for it.

> [!NOTE]
> Not every cache can back a conforming store. The contract needs an **atomic** set-if-not-exists, which `IDistributedCache` does not expose. A Redis-backed store must use `SET NX` (or a script) directly, and must not run under an eviction policy such as `allkeys-lru` — evicting a live reservation silently allows a double charge.

> [!IMPORTANT]
> `UseTrellisIdempotency()` must run **after `UseRouting()`** so the middleware can see the endpoint's `[Idempotent]` metadata and buffer the request body before the handler consumes it, and **after `UseAuthentication()` / `UseAuthorization()`** so the default `DefaultIdempotencyScopeResolver` partitions the store by the authenticated `Actor`. Mounting before authentication causes every authenticated request to fall back to the shared `anonymous` scope, which lets different users collide on the same key. In a Minimal API host where `UseRouting` is not called explicitly, the runtime inserts routing implicitly; placing `UseTrellisIdempotency()` after `UseAuthentication()` / `UseAuthorization()` and immediately before `MapControllers()` / `MapGet(...)` is equivalent.

Full surface (options, store, scope resolver, attribute, parser, fingerprint helpers): [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md#namespace-trellisaspidempotency).
Cosmos DB store: [`trellis-api-asp-idempotency-cosmos.md`](../api_reference/trellis-api-asp-idempotency-cosmos.md#quick-start).
Conformance suite for a custom store: [`trellis-api-testing-idempotency.md`](../api_reference/trellis-api-testing-idempotency.md#quick-start).

## Scalar value validation

`Trellis.Asp` validates value objects implementing `IScalarValue<TSelf, TPrimitive>` at every binding site (route, query, JSON body) and surfaces the result as `Error.InvalidInput`.

| Host | Required wiring |
|---|---|
| MVC controllers | `services.AddControllers().AddScalarValueValidation();` + `app.UseScalarValueValidation();` |
| Minimal API | `services.AddScalarValueValidationForMinimalApi();` + `app.UseScalarValueValidation();` + `.WithScalarValueValidation()` per endpoint |
| Either | `services.AddTrellisAspWithScalarValidation();` (registers `TrellisAspOptions` AND chains `AddScalarValueValidation()` for **both** MVC and Minimal API JSON pipelines in one call), or call `AddTrellisAsp()` + `AddScalarValueValidation()` explicitly when the split matters (e.g. a host that only needs error mapping). |

> [!IMPORTANT]
> The `IServiceCollection`-receiver `AddScalarValueValidation()` only configures shared JSON support. For MVC apps you still need `AddControllers().AddScalarValueValidation()` so the `ScalarValueValidationFilter` and `ScalarValueModelBinderProvider` are registered.

`Maybe<T>` rules:

- Omitted or JSON `null` → `Maybe<T>.None`.
- Valid value → `Maybe.From(value)`.
- Invalid value (fails `TValue.TryCreate`) → validation error collected in `ValidationErrorsContext`; the request short-circuits with a validation Problem Details before the handler runs.

```csharp
using Trellis;
using Trellis.Primitives;

public sealed record UpdateCustomerRequest(
    FirstName Name,
    Maybe<PhoneNumber> Phone,
    Maybe<Url> Website);
```

> [!WARNING]
> `AddTrellisAsp` / `AddScalarValueValidation` only wire **scalar** VO converters. **Composite** value objects (multi-field `[OwnedEntity]` types like `ShippingAddress`, `Money`) need an explicit `[JsonConverter(typeof(CompositeValueObjectJsonConverter<MyVo>))]` on the type; otherwise model binding silently bypasses `TryCreate` and an invalid payload reaches the domain layer.

## Route constraints

Bind value objects (any `IParsable<T>`) directly from a route segment.

```csharp
using Trellis.Asp;

// AOT-safe — explicit registration per type
services.AddTrellisRouteConstraint<ProductId>("ProductId");

// Reflection-based — scans the calling assembly + Trellis.Core for IScalarValue<,> + IParsable<>
services.AddTrellisRouteConstraints();

app.MapGet("/products/{id:ProductId}", (ProductId id) => Results.Ok(id));
```

`AddTrellisRouteConstraints` is reflection-based and **not Native AOT compatible**; `AddTrellisRouteConstraint<T>` is AOT-safe.

## Actor providers

`Trellis.Asp.Authorization` hydrates the current `Actor` from JWT/OIDC claims. The `Actor` and `IActorProvider` types themselves live in `Trellis.Authorization`.

Each `AddXxxActorProvider` helper **replaces** the `IActorProvider` slot — chaining multiple helpers does not stack them. Pick one provider per environment, then optionally wrap with caching:

```csharp
using Trellis.Asp.Authorization;

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

    // Wraps EntraActorProvider with per-request caching. The inner provider type is
    // registered idempotently via TryAddScoped<T>(); the outer IActorProvider slot
    // is replaced with the caching decorator.
    services.AddCachingActorProvider<EntraActorProvider>();
}
```

`DevelopmentActorProvider` throws `InvalidOperationException` outside the Development environment regardless of header presence; in Development it reads the `X-Test-Actor` header (JSON: `{ "Id", "Permissions", "ForbiddenPermissions", "Attributes" }`, case-insensitive). See [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md) for `WebApplicationFactory.CreateClientWithActor`.

### Nested-JSON claim shapes (Auth0 `app_metadata.roles`, Azure B2C, Okta)

`NestedJsonPathClaimsActorProvider` handles identity providers that ship permissions under a nested JSON claim. Configure with a `ContainerClaim` naming the top-level claim that carries the JSON payload, and dotted `ActorIdPath` / `PermissionsPath` segments inside it. Terminal path elements may be a string (one value), an array of strings (multiple values), or an object whose property names become the values (the Auth0 roles-as-object shape). When the path misses, the container claim is absent, or the JSON fails to parse, the provider falls back through the inherited flat-claim resolver (with the same short↔long claim-name aware fallback the base uses).

```csharp
// Auth0: { "sub": "auth0|abc", "app_metadata": { "roles": ["orders:read"] } }
services.AddNestedJsonPathClaimsActorProvider(opts =>
{
    opts.ActorIdClaim = "sub";
    opts.ContainerClaim = "app_metadata";
    opts.PermissionsPath = "roles";
});
```

Setting `ActorIdPath` or `PermissionsPath` without a `ContainerClaim` throws `InvalidOperationException` at provider construction so the misconfiguration is caught at the first request rather than silently falling back. See [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md#common-identity-provider-claim-shapes) for a table of canonical configurations across providers.

### Silent-403 diagnostics

When `ClaimsActorProvider` (or the nested derivation) resolves zero permissions on an authenticated identity that carries other claims, it emits one warning per application lifetime (`EventId 2`). When the configured `PermissionsClaim` resolves to a single value that parses as a JSON object or array — the smoking gun for nested-claim shapes — it emits one error per application lifetime (`EventId 3`) recommending `NestedJsonPathClaimsActorProvider`. The diagnostics are throttled to once per application lifetime to avoid log spam; set `ClaimsActorOptions.ValidateClaimShapeOnFirstUse = false` to suppress both.

## Composition

Once the application returns `Result<T>` / `Result<WriteOutcome<T>>`, every cross-cutting concern composes through the same `ToHttpResponse` call — there is no per-endpoint `switch` to keep in sync with the error catalog.

```csharp
app.MapPut("/products/{id:guid}", async (
        Guid id,
        UpdateProductRequest request,
        IProductService products,
        HttpContext httpContext,
        CancellationToken ct) =>
{
    var ifMatch = ETagHelper.ParseIfMatch(httpContext.Request);
    var ifNoneMatch = ETagHelper.ParseIfNoneMatch(httpContext.Request);

    return await products.UpdateAsync(id, request, ifMatch, ct)
        .EnforceIfNoneMatchPreconditionAsync(ifNoneMatch)
        .ToHttpResponseAsync(
            body: product => new ProductResponse(product.Id, product.Name, product.Price, product.ETag),
            configure: opts => opts
                .WithETag(product => product.ETag)
                .HonorPrefer()
                .WithErrorMapping<Error.Conflict>(StatusCodes.Status409Conflict));
});
```

When you genuinely need a custom payload shape (non-Problem-Details body, endpoint-specific JSON, extra cookies), reach for `MatchAsync` from `Trellis.Core` instead of `ToHttpResponse`. Treat that as the exception, not the rule.

## Practical guidance

- **Convert at the API boundary only.** Keep `Result<T>` flowing through your application layer; convert to `IResult` / `ActionResult<T>` exactly once, at the endpoint.
- **`AddTrellisAspWithScalarValidation()` is the one-call setup for controller hosts.** It registers `TrellisAspOptions` and configures both the MVC and Minimal API JSON pipelines for scalar-value / `Maybe<T>` deserialization. (`AddTrellisAsp()` alone registers error mapping and `ResourceCollectionNameRegistry` — the scalar pipeline mutates global `MvcOptions` / `JsonOptions` and is now an explicit opt-in.) You still need `UseScalarValueValidation()` middleware and (for Minimal APIs) `WithScalarValueValidation()` per endpoint.
- **Document failure status codes.** Add `[ProducesResponseType<ProblemDetails>(...)]` for every spec-listed failure status (`422`, `409`, `403`, `404`, …). The `IEndpointMetadataProvider` on Trellis result types already declares the union of statuses the writer can emit (`200`, `201`, `304`, `400`, `404`, `412`, `500`); layer your spec-specific metadata on top.
- **`Result<Unit>` for side-effect commands**. A successful `Result<Unit>` produces `204 No Content` with no body.
- **Use typed ETag parsers.** `ETagHelper.ParseIfMatch` / `ParseIfNoneMatch` return `EntityTagValue[]?`, which feeds `OptionalETag` / `RequireETag` (Http.Abstractions) and `EnforceIfNoneMatchPrecondition` (Asp) directly.
- **Versioned `Location` headers.** Under query-string or header API versioning, every `CreatedAtRoute` / `CreatedAtAction` / `WithLocation` call must include `["api-version"] = ApiVersion` in the route values, otherwise the `Location` 404s on dereference and tests still pass. Prefer chaining `.WithVersionedRoute()` from [`Trellis.Asp.ApiVersioning`](#api-version-aware-location-headers) — it injects the version per request automatically. The [`TRLS023`](analyzers/TRLS023.md) analyzer flags bare `CreatedAtRoute` / `CreatedAtAction` / `WithLocation` inside `[ApiVersion]` controllers and the code fix appends `.WithVersionedRoute()`.
- **Avoid controller-level `[Consumes("application/json")]`.** Trigger-style POSTs without bodies (e.g., `POST /orders/{id}/submission`) return `415` for any request without a `Content-Type`. Apply `[Consumes]` per body-bearing action.
- **Prefer `CreatedAtRoute` over `CreatedAtAction`** for trim/AOT scenarios; `CreatedAtAction` is annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`.
- **Prove `422` mapping in integration tests.** Exception middleware does not map Trellis `Result` failures — assert at least one business-validation failure surfaces as `422` Problem Details end-to-end.

## Cross-references

- API surface: [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md)
- `Result`, `Result<T>`, `Error`, `Page<T>`: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- `WriteOutcome<T>`, `EntityTagValue`, `OptionalETag` / `RequireETag`: [`trellis-api-http-abstractions.md`](../api_reference/trellis-api-http-abstractions.md)
- `Actor`, `IActorProvider`, `IAuthorize`: [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md)
- `IScalarValue<TSelf, TPrimitive>`, `Maybe<T>`, ready-to-use value objects: [`trellis-api-primitives.md`](../api_reference/trellis-api-primitives.md)
- Integration-test helpers (`CreateClientWithActor` for `X-Test-Actor`): [`trellis-api-testing-aspnetcore.md`](../api_reference/trellis-api-testing-aspnetcore.md)
- Composite value object end-to-end pattern (Recipe 13): [`trellis-api-cookbook.md`](../api_reference/trellis-api-cookbook.md#recipe-13--composite-value-object-end-to-end-domain--api-json-binding--ef-core-ownership)
- HTTP client side (consuming results): [`integration-http.md`](integration-http.md)
- FluentValidation pipeline: [`integration-fluentvalidation.md`](integration-fluentvalidation.md)
- EF Core integration (`FirstOrDefaultResultAsync`, `SaveChangesResultUnitAsync`): [`integration-ef.md`](integration-ef.md)
