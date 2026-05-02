---
title: ToHttpResponse — Unified Response Verb
package: Trellis.Asp
topics: [asp, http-response, builder, etag, prefer, problem-details, write-outcome]
related_api_reference: [trellis-api-asp.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# ToHttpResponse — Unified Response Verb

`ToHttpResponse(...)` is the single Trellis verb for translating `Result`, `Result<T>`, `Result<WriteOutcome<T>>`, and `Result<Page<T>>` into ASP.NET Core responses, returning `Microsoft.AspNetCore.Http.IResult` for both Minimal API and MVC hosts.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Map `Result<T>` to `200 OK` (or mapped Problem Details on failure) | `result.ToHttpResponse()` / `await ...ToHttpResponseAsync()` | [Quick start](#quick-start) |
| Send no body on success | Return `Result<Unit>` (`Result.Ok()`) — emits `204 No Content` | [Basic mapping](#basic-mapping) |
| Project the response body separately from the domain value | `result.ToHttpResponse(body: domain => dto, configure: opts => ...)` | [Body projection](#body-projection) |
| Map `Result<WriteOutcome<T>>` to RFC 9110 write semantics | `result.ToHttpResponse(opts => opts.CreatedAtRoute(...))` | [Created responses](#created-responses), [WriteOutcome variants](#writeoutcomet-variants) |
| Wrap as MVC `ActionResult<T>` | Chain `.AsActionResult<T>()` / `.AsActionResultAsync<T>()` | [MVC adapter](#mvc-adapter) |
| Add ETag / `Last-Modified` and honor conditional `GET`/`HEAD` | `opts.WithETag(...).WithLastModified(...).EvaluatePreconditions()` | [ETag and conditional requests](#etag-and-conditional-requests) |
| Honor `Prefer: return=minimal` / `return=representation` | `opts.HonorPrefer()` | [Prefer header](#prefer-header) |
| Return `206 Partial Content` for byte ranges | `opts.WithRange(from, to, total)` / `opts.WithRange(selector)` | [Range responses](#range-responses) |
| Return paginated JSON with RFC 8288 `Link` header | `result.ToHttpResponse(nextUrlBuilder, body)` on `Result<Page<T>>` | [Pagination](#pagination) |
| Override error → status mapping for one endpoint | `opts.WithErrorMapping<TError>(status)` / `opts.WithErrorMapping(err => ...)` | [Per-call error mapping](#per-call-error-mapping) |
| Render a standalone `Error` (no `Result` pipeline) | `error.ToHttpResponse(...)` | [Standalone error](#standalone-error) |

## Use this guide when

- You are writing endpoint code and need the exact overload, parameter order, or builder method for `ToHttpResponse`.
- You want to confirm what each `HttpResponseOptionsBuilder<T>` method does without scanning the API reference.
- You need to know which HTTP status / headers a given `Result` shape produces.
- You are migrating from a v1 ASP integration that used the legacy result-mapping helpers — see [migration.md](migration.md#aspnet-core-trellisasp).

For broader ASP.NET integration topics (Problem Details rendering, scalar validation, route constraints, actor providers, controller examples), see [`integration-aspnet.md`](integration-aspnet.md).

## Surface at a glance

| Receiver | Overload (simplified) | Success status | Failure |
|---|---|---|---|
| `Error` | `error.ToHttpResponse(configure?)` | n/a | Problem Details |
| `Result<T>` | `result.ToHttpResponse(configure?)` | `200` (or `201` if `Created*` configured); `204` for `Result<Unit>` | Problem Details |
| `Result<T>` | `result.ToHttpResponse(body, configure?)` | Same as above; body is `body(domain)` | Problem Details |
| `Result<WriteOutcome<T>>` | `result.ToHttpResponse(configure?)` | Variant-driven (`Created → 201`, `Updated → 200/204`, `UpdatedNoContent → 204`, `Accepted → 202`, `AcceptedNoContent → 202`) | Problem Details |
| `Result<WriteOutcome<T>>` | `result.ToHttpResponse(body, configure?)` | Same; projected body | Problem Details |
| `Result<Page<T>>` | `result.ToHttpResponse(nextUrlBuilder, body, configure?)` | `200` with `PagedResponse<TBody>` envelope + RFC 8288 `Link` | Problem Details |

Each overload also exposes `Task<...>` and `ValueTask<...>` async variants named `ToHttpResponseAsync` with identical signatures. The `configure` delegate runs against `HttpResponseOptionsBuilder<TDomain>` (or the non-generic `HttpResponseOptionsBuilder` for the `Error` overload).

Full signatures: [trellis-api-asp.md](../api_reference/trellis-api-asp.md).

### `HttpResponseOptionsBuilder<TDomain>` — chainable surface

| Method | Effect |
|---|---|
| `WithETag(Func<T, string>)` / `WithETag(Func<T, EntityTagValue>)` | Emits `ETag`; the string overload wraps as `EntityTagValue.Strong`. |
| `WithLastModified(Func<T, DateTimeOffset>)` | Emits `Last-Modified` in RFC 1123 format. |
| `Vary(params string[])` | Appends to `Vary` (preserves existing values; case-insensitive dedupe). |
| `WithContentLanguage(params string[])` / `WithContentLocation(Func<T, string>)` / `WithAcceptRanges(string)` | Sets the matching response header. |
| `Created(string literal)` / `Created(Func<T, string>)` | `201 Created` with literal or value-derived `Location`. |
| `CreatedAtRoute(name, Func<T, RouteValueDictionary>)` | `201 Created` via `LinkGenerator.GetUriByName`. AOT-safe. |
| `CreatedAtAction(action, Func<T, RouteValueDictionary>, controller?)` | MVC `CreatedAtAction` equivalent. **Not trim/AOT-safe** — `RequiresUnreferencedCode` / `RequiresDynamicCode`. |
| `EvaluatePreconditions()` | On `GET`/`HEAD`, evaluates `If-Match`, `If-Unmodified-Since`, `If-None-Match`, `If-Modified-Since` against the configured ETag/`Last-Modified`. **Not on by default.** |
| `HonorPrefer()` | Honors RFC 7240 `Prefer: return=minimal` / `return=representation`. Always emits `Vary: Prefer`; emits `Preference-Applied` only when honored. **Not on by default.** |
| `WithRange(Func<T, ContentRangeHeaderValue>)` | `206 Partial Content` with selector-driven `Content-Range` (or `200` if range covers the whole resource). |
| `WithRange(long from, long to, long totalLength)` | Static range variant; clamps `to` to `totalLength - 1`. |
| `WithErrorMapping(Func<Error, int>)` | Per-call error → status mapper. Highest precedence. |
| `WithErrorMapping<TError>(int status)` | Per-call override for a single error type. |

The non-generic `HttpResponseOptionsBuilder` (for `error.ToHttpResponse(...)`) only exposes `Vary`, `HonorPrefer`, and the two `WithErrorMapping` methods.

> [!NOTE]
> The options builder has no `body` method. To project the response body, use the `body` **parameter** of the `ToHttpResponse<TDomain, TBody>` overload. Selectors in the options builder always run against the `TDomain` value, not the projected body.

## Installation

```bash
dotnet add package Trellis.Asp
```

Register defaults in `Program.cs`:

```csharp
builder.Services.AddTrellisAsp();
```

## Quick start

A read endpoint, in both API styles. Same `Result` pipeline, same verb.

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Trellis;
using Trellis.Asp;

public sealed record TodoDto(Guid Id, string Title);

// Minimal API
app.MapGet("/todos/{id:guid}", async (Guid id, ITodoService svc, CancellationToken ct) =>
    await svc.GetAsync(id, ct).ToHttpResponseAsync());

// MVC controller
[ApiController]
[Route("todos")]
public sealed class TodosController(ITodoService svc) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public Task<ActionResult<TodoDto>> Get(Guid id, CancellationToken ct) =>
        svc.GetAsync(id, ct)
           .ToHttpResponseAsync()
           .AsActionResultAsync<TodoDto>();
}
```

`ToHttpResponseAsync` returns `Task<IResult>`; `AsActionResultAsync<T>` adapts it to `ActionResult<T>` for MVC signatures.

## Basic mapping

| Receiver shape | Success status | Body |
|---|---|---|
| `Result<T>` (no `Created*`) | `200 OK` | `T` (or `body(domain)` if projecting) |
| `Result<T>` (with `Created*`) | `201 Created` + `Location` | `T` |
| `Result<Unit>` | `204 No Content` | none |
| `Result<WriteOutcome<T>>` | Variant-driven (see [WriteOutcome variants](#writeoutcomet-variants)) | Variant-driven |
| `Result<Page<T>>` (paginated overload) | `200 OK` + `Link` | `PagedResponse<TBody>` |
| Any `Fail(error)` | Mapped via `TrellisAspOptions` (or per-call override) | `application/problem+json` |

Use `Result<Unit>` for no-payload commands:

```csharp
app.MapDelete("/todos/{id:guid}", async (Guid id, ITodoService svc, CancellationToken ct) =>
    await svc.DeleteAsync(id, ct).ToHttpResponseAsync()); // 204 on Ok, Problem Details on Fail
```

## Body projection

Pass `body` as the **second positional argument** to project a DTO without losing access to the domain value for header selectors.

```csharp
public sealed record TodoView(Guid Id, string Title, string ETag);

app.MapGet("/todos/{id:guid}", async (Guid id, ITodoService svc, CancellationToken ct) =>
    await svc.GetAsync(id, ct).ToHttpResponseAsync(
        body: t => new TodoView(t.Id, t.Title, $""{t.Version}""),
        configure: opts => opts.WithETag(t => $""{t.Version}"")));
```

Selectors (`WithETag`, `WithLastModified`, `Created(Func<T, string>)`, `WithRange(selector)`, etc.) always receive the **domain** value, not the projected body.

## ETag and conditional requests

`EvaluatePreconditions()` opts in to RFC 9110 evaluation on safe methods (`GET`, `HEAD`). Pair it with `WithETag` and/or `WithLastModified`:

```csharp
app.MapGet("/todos/{id:guid}", async (Guid id, ITodoService svc, CancellationToken ct) =>
    await svc.GetAsync(id, ct).ToHttpResponseAsync(opts => opts
        .WithETag(t => $""{t.Version}"")
        .WithLastModified(t => t.UpdatedAt)
        .EvaluatePreconditions()));
```

Evaluation order (per `ConditionalRequestEvaluator`): `If-Match` → `If-Unmodified-Since` → `If-None-Match` → `If-Modified-Since`. Failed `If-Match` / `If-Unmodified-Since` → `412 Precondition Failed`; failed `If-None-Match` / `If-Modified-Since` on `GET`/`HEAD` → `304 Not Modified`. On unsafe methods, evaluate the precondition **before** the mutation (see [`integration-aspnet.md`](integration-aspnet.md#conditional-requests)).

## Prefer header

`HonorPrefer()` is meaningful on `Result<WriteOutcome<T>>` updates: `Prefer: return=minimal` short-circuits `Updated → 204 No Content`; `return=representation` returns `200 OK` with the body. `Vary: Prefer` is always emitted when honored; `Preference-Applied` is emitted only when the preference was honored.

```csharp
app.MapPut("/todos/{id:guid}", async (Guid id, UpdateTodo cmd, ITodoService svc, CancellationToken ct) =>
    await svc.UpdateAsync(id, cmd, ct).ToHttpResponseAsync(opts => opts
        .WithETag(t => $""{t.Version}"")
        .HonorPrefer()));
```

## Created responses

| Configuration | Result |
|---|---|
| `opts.Created("/orders/123")` | `201 Created`, literal `Location`. |
| `opts.Created(o => $"/orders/{o.Id}")` | `201 Created`, value-derived `Location`. |
| `opts.CreatedAtRoute("Orders_GetById", o => new RouteValueDictionary { ["id"] = o.Id })` | `201 Created`, link via `LinkGenerator.GetUriByName`. AOT-safe. |
| `opts.CreatedAtAction("GetById", o => new RouteValueDictionary { ["id"] = o.Id })` | `201 Created`, MVC link. **Not trim/AOT-safe.** |

```csharp
app.MapPost("/orders", async (CreateOrder cmd, IOrderService svc, CancellationToken ct) =>
    await svc.CreateAsync(cmd, ct).ToHttpResponseAsync(opts => opts
        .CreatedAtRoute("Orders_GetById", o => new RouteValueDictionary { ["id"] = o.Id })))
   .WithName("Orders_Create");
```

> [!IMPORTANT]
> Under query-string API versioning, the `RouteValueDictionary` MUST include `["api-version"] = ApiVersion`; otherwise the emitted `Location` omits the version segment and `404`s on dereference.

### `WriteOutcome<T>` variants

When the receiver is `Result<WriteOutcome<T>>`, the outcome variant drives status and headers — `Created*` builder methods are **not** required (they are required for `Result<T>` create endpoints). See [`integration-aspnet.md → WriteOutcome<T>`](integration-aspnet.md#writeoutcomet) for the full mapping table and command examples.

## Range responses

| Overload | Behavior |
|---|---|
| `WithRange(Func<T, ContentRangeHeaderValue>)` | Returns `206` with selector-derived `Content-Range`. Returns `200` if the range covers the whole representation. |
| `WithRange(long from, long to, long totalLength)` | Static variant. Clamps `to` to `totalLength - 1`. |

```csharp
app.MapGet("/blobs/{id:guid}", async (Guid id, IBlobService svc, CancellationToken ct) =>
    await svc.GetAsync(id, ct).ToHttpResponseAsync(opts => opts
        .WithAcceptRanges("bytes")
        .WithRange(b => new System.Net.Http.Headers.ContentRangeHeaderValue(0, b.Length - 1, b.Length))));
```

## Pagination

`Result<Page<T>>` has a dedicated overload requiring `nextUrlBuilder` and a per-item `body` projector. The pagination signature is **not** the same as the `Result<T>` overload:

| Parameter | Type | Notes |
|---|---|---|
| `nextUrlBuilder` | `Func<Cursor, int, string>` | Receives the cursor and the applied page limit; returns an absolute URL. Used for both `next` and `prev` links in the RFC 8288 `Link` header. |
| `body` | `Func<T, TBody>` | Per-**item** projector (not per-page). Each `Page<T>` item is mapped to `TBody` for the envelope. |

```csharp
app.MapGet("/todos", async (string? cursor, int? limit, ITodoService svc, HttpRequest req, CancellationToken ct) =>
    await svc.ListAsync(cursor, limit, ct).ToHttpResponseAsync(
        nextUrlBuilder: (c, n) => $"{req.Scheme}://{req.Host}{req.Path}?cursor={c}&limit={n}",
        body: t => new TodoDto(t.Id, t.Title)));
```

## Per-call error mapping

Override the global `TrellisAspOptions` defaults for a single endpoint. Per-call overrides have **higher precedence** than the global registration.

```csharp
// Global default at composition root:
builder.Services.AddTrellisAsp(opts => opts.MapError<Error.Conflict>(StatusCodes.Status409Conflict));

// Per-call override for one endpoint:
app.MapGet("/legacy/{id:guid}", async (Guid id, ITodoService svc, CancellationToken ct) =>
    await svc.GetAsync(id, ct).ToHttpResponseAsync(opts => opts
        .WithErrorMapping<Error.NotFound>(StatusCodes.Status410Gone)));
```

For Problem Details payload rules and the full default mapping table, see [`integration-aspnet.md → Error mapping`](integration-aspnet.md#error-mapping) and [`integration-aspnet.md → Problem Details output`](integration-aspnet.md#problem-details-output).

## Standalone error

For diagnostic / fault-demo endpoints that produce an `Error` without a `Result` pipeline:

```csharp
app.MapGet("/diagnostics/throttle",
    () => new Error.TooManyRequests("rate-limit-policy") { Detail = "10 req/min" }
        .ToHttpResponse(opts => opts.WithErrorMapping<Error.TooManyRequests>(StatusCodes.Status429TooManyRequests)));
```

The non-generic `HttpResponseOptionsBuilder` exposes `Vary`, `HonorPrefer`, and the two `WithErrorMapping` overloads only.

## MVC adapter

`AsActionResult<T>()` / `AsActionResultAsync<T>()` (in `ActionResultAdapterExtensions`) wrap the `IResult` so it satisfies `ActionResult<T>` signatures. The MVC pipeline still executes the same `IResult` via `IConvertToActionResult`.

```csharp
[HttpPost]
public Task<ActionResult<TodoDto>> Create(CreateTodo cmd, CancellationToken ct) =>
    _svc.CreateAsync(cmd, ct)
        .ToHttpResponseAsync(opts => opts.CreatedAtRoute(
            "Todos_GetById",
            t => new RouteValueDictionary { ["id"] = t.Id }))
        .AsActionResultAsync<TodoDto>();
```

> [!NOTE]
> `ToHttpResponse` writes via the Minimal API `Results.*` infrastructure, **bypassing MVC output formatters**. If an endpoint depends on a custom MVC formatter (e.g. XML), return an MVC `ObjectResult` directly for that endpoint and use `ToHttpResponse(...).AsActionResult<T>()` for the rest.

## Composition

`ToHttpResponseAsync` accepts both `Task<Result<T>>` and `ValueTask<Result<T>>` receivers, so it composes naturally at the end of a `Bind`/`Map`/`Ensure` chain.

```csharp
app.MapPut("/todos/{id:guid}/title", async (Guid id, RenameTodo cmd, ITodoService svc, CancellationToken ct) =>
    await svc.GetAsync(id, ct)
             .EnsureAsync(t => t.OwnerId == cmd.OwnerId,
                          new Error.Forbidden("todos.rename"))
             .BindAsync((t, token) => svc.RenameAsync(t.Id, cmd.NewTitle, token), ct)
             .ToHttpResponseAsync(opts => opts
                 .WithETag(t => $""{t.Version}"")
                 .HonorPrefer()));
```

Test the produced `IResult` directly without spinning up the host:

```csharp
var http = (await result).ToHttpResponse(opts => opts.WithETag(t => $""{t.Version}""));
await http.ExecuteAsync(httpContext);
// Assert on httpContext.Response.StatusCode and headers.
```

## Practical guidance

- **Pick the right receiver shape.** Reads → `Result<T>`. Commands with a no-body success → `Result<Unit>` (auto `204`). Writes that need RFC 9110 status semantics (`201`/`202`/`204` with `Location`/`Retry-After`) → `Result<WriteOutcome<T>>`. Lists → `Result<Page<T>>` with the paginated overload.
- **Use `CreatedAtRoute` for AOT.** `CreatedAtAction` carries `RequiresUnreferencedCode` / `RequiresDynamicCode`. Pass `RouteValueDictionary` (not anonymous types) to stay AOT-safe.
- **Opt in to preconditions and `Prefer`.** Both `EvaluatePreconditions()` and `HonorPrefer()` are off by default — call them where you need the behavior.
- **Selectors run on the domain value.** When projecting via `body`, all `With*` selectors still receive the domain `T`. Use this to compute strong ETags from version fields the DTO does not expose.
- **Per-call mappings beat globals.** `opts.WithErrorMapping<TError>(status)` overrides the `TrellisAspOptions` registration for that one call.
- **Don't pre-compute the `IResult`.** Build it inside the endpoint handler so the configured `LinkGenerator` resolves at execute time with the live `HttpContext.RequestServices`.

## Cross-references

- API surface: [trellis-api-asp.md](../api_reference/trellis-api-asp.md)
- `Result<T>`, `WriteOutcome<T>`, `Page<T>`, `Cursor`, `Error` semantics: [trellis-api-core.md](../api_reference/trellis-api-core.md)
- Broader ASP.NET integration (Problem Details rendering, scalar validation, route constraints, actor providers, MVC patterns): [integration-aspnet.md](integration-aspnet.md)
- HTTP client side (`HttpResponseMessage` → `Result<T>`): [integration-http.md](integration-http.md)
