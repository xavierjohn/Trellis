# ToHttpResponse — the unified ASP.NET response verb

`Result<T>.ToHttpResponse(...)` is the single, recommended verb for translating a
Trellis `Result` (or `Result<WriteOutcome<T>>`, or `Result<Page<T>>`) into an
HTTP response in **both MVC and Minimal API** apps. It returns
`Microsoft.AspNetCore.Http.IResult`, so it works directly in Minimal API
endpoints and converts to an MVC `ActionResult<T>` via `.AsActionResult<T>()`.

`ToHttpResponse` collapses the previous family of 16+ verbs
(`ToActionResult`, `ToHttpResult`, `ToActionResultAsync`, <!-- stale-doc-ok: legacy verb list in migration guidance -->
`ToHttpResultAsync`, `ToPagedHttpResult`, …) into one fluent surface that <!-- stale-doc-ok: legacy verb list in migration guidance -->
honors the same RFC 9110 conditional-request, `Prefer`, `Vary`, `Range` and
companion-header semantics they did.

## Read endpoint (success → 200, failure → mapped status)

```csharp
[HttpGet("{id:guid}")]
public Task<ActionResult<TodoItem>> Get(Guid id, CancellationToken ct) =>
    _mediator.SendAsync(new GetTodo(id), ct)
             .ToHttpResponseAsync()       // returns IResult
             .AsActionResultAsync<TodoItem>();
```

In Minimal API:

```csharp
app.MapGet("/todos/{id:guid}", (Guid id, IMediator m, CancellationToken ct) =>
    m.SendAsync(new GetTodo(id), ct).ToHttpResponseAsync());
```

## Write endpoint (`Result<WriteOutcome<T>>`)

`ToHttpResponse` understands every `WriteOutcome` variant
(`Created`, `Updated`, `UpdatedNoContent`, `Accepted`, `AcceptedNoContent`)
and emits the right status, `Location`, `ETag` and `Last-Modified` headers.

```csharp
[HttpPost]
public Task<ActionResult<TodoItem>> Create(CreateTodo cmd, CancellationToken ct) =>
    _mediator.SendAsync(cmd, ct)
             .ToHttpResponseAsync(opts => opts.CreatedAtRoute(
                 routeName: "GetTodo",
                 routeValues: t => new RouteValueDictionary { ["id"] = t.Id }))
             .AsActionResultAsync<TodoItem>();
```

## Conditional requests & `Prefer`

`HonorPreconditions` and `HonorPrefer` are on by default. Pair with
`WithETag` / `WithLastModified` to opt in:

```csharp
result.ToHttpResponse(opts => opts
    .WithETag(t => EntityTagHeaderValue.Parse($"\"{t.Version}\""))
    .WithLastModified(t => t.UpdatedAt));
```

This emits `ETag` / `Last-Modified` on success, evaluates
`If-Match` / `If-None-Match` / `If-Modified-Since` / `If-Unmodified-Since`,
and respects `Prefer: return=minimal|representation`.

## Vary, Range, paginated and projected bodies

```csharp
opts.Vary("Accept-Language", "Accept-Encoding");
opts.WithRange(maxRangeBytes: 4 * 1024 * 1024);
```

Paginated:

```csharp
result.ToHttpResponse(
    nextUrlBuilder: page => $"/todos?cursor={page.NextCursor}",
    body: page => page.Items);
```

Body projection:

```csharp
result.ToHttpResponse<TodoItem, TodoDto>(opts => opts.WithBody(t => TodoDto.From(t)));
```

## Error mapping

Default mapping is configured at startup with `AddTrellisAsp`. Override per call:

```csharp
// Startup — configure global defaults:
builder.Services.AddTrellisAsp(options =>
{
    options.MapError<Error.Conflict>(StatusCodes.Status400BadRequest);
});

// Per call — override for a single endpoint:
opts.WithErrorMapping<Error.NotFound>(StatusCodes.Status410Gone);
```

Companion headers (`Allow`, `Retry-After`, `Content-Range`, …) are emitted
automatically based on the concrete `Error` subtype.

## MVC ↔ Minimal API

Same code, two presentation styles:

| API style       | Wrap with                       |
|-----------------|---------------------------------|
| Minimal API     | `await result.ToHttpResponse()` |
| MVC controller  | `result.ToHttpResponse().AsActionResult<T>()` |

`.AsActionResult<T>()` implements `IConvertToActionResult` and forwards
`ExecuteResultAsync` to `IResult.ExecuteAsync`, so the MVC pipeline executes
the same `IResult` you would use in Minimal API.

## Migration from the legacy verbs

The previous extension classes
(`HttpResultExtensions`, `HttpResultExtensionsAsync`, `ActionResultExtensions`,
`ActionResultExtensionsAsync`, `WriteOutcomeExtensions`,
`PageHttpResultExtensions`, `PageActionResultExtensions`) have been **deleted**
in v3. Migrate as follows:

| Old                                                | New                                                              |
|----------------------------------------------------|------------------------------------------------------------------|
| `result.ToActionResult()`                          | `result.ToHttpResponse().AsActionResult<T>()`                    | <!-- stale-doc-ok: legacy verb in migration table -->
| `result.ToActionResultAsync()`                     | `result.ToHttpResponseAsync().AsActionResultAsync<T>()`          | <!-- stale-doc-ok: legacy verb in migration table -->
| `result.ToHttpResult(httpContext)`                 | `result.ToHttpResponse()` (returns `IResult`)                    | <!-- stale-doc-ok: legacy verb in migration table -->
| `outcome.ToActionResult(routeName, routeValues)`   | `result.ToHttpResponse(o => o.CreatedAtRoute(routeName, rv))`    | <!-- stale-doc-ok: legacy verb in migration table -->
| `outcome.ToHttpResult(httpContext, map)`           | `result.ToHttpResponse(o => o.CreatedAtRoute(...))`              | <!-- stale-doc-ok: legacy verb in migration table -->
| `pageResult.ToPagedHttpResult(nextUrlBuilder, ...)` | `result.ToHttpResponse(nextUrlBuilder, body: p => p.Items)`     | <!-- stale-doc-ok: legacy verb in migration table -->

### Other migration notes

- **VO binding auto-registration.** `services.AddTrellisAsp()` now
  automatically calls `AddScalarValueValidation()`. The standalone
  `UseScalarValueValidation()` / `WithScalarValueValidation()` calls remain
  functional but are redundant when `AddTrellisAsp` is used.
- **Controller-test pattern.** Tests that previously asserted on
  `ActionResult` shapes can now exercise the `IResult` directly:
  `var http = (await result).ToHttpResponse(); await http.ExecuteAsync(httpContext);`
- **MVC formatter bypass.** `ToHttpResponse` writes responses through the
  Minimal API `Results.*` infrastructure, bypassing MVC output formatters.
  If an endpoint depends on a custom MVC formatter (e.g. XML), return an MVC
  `ActionResult` / `ObjectResult` directly from the controller for that
  endpoint and use `ToHttpResponse().AsActionResult<T>()` for the rest.
