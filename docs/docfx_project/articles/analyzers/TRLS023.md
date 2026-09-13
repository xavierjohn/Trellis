# TRLS023 — `CreatedAtRoute` / `CreatedAtAction` / `WithLocation` is missing the `api-version` route value

- **Severity:** Warning
- **Category:** Trellis

## What it detects

Flags `HttpResponseOptionsBuilder<T>.CreatedAtRoute(routeName, routeValues)`, `CreatedAtAction(actionName, routeValues, controllerName)`, and `WithLocation(routeName, routeValues)` invocations that produce `Location` headers without an `api-version` route value, when the enclosing controller is decorated with `[ApiVersion(...)]`.

The analyzer suppresses when the same fluent builder chain calls `.WithVersionedRoute(...)` from `Trellis.Asp.ApiVersioning` — that helper resolves the destination's version per request and removes the need to encode it in the route values literal. The analyzer also suppresses for the manual primitive `.WithRouteValueResolver("api-version", httpContext => ...)` on `HttpResponseOptionsBuilder<T>`. This suppression is not equivalent to destination-aware validation. The key match is case-insensitive (`"API-Version"` also suppresses).

The analyzer runs only inside controllers/types annotated with `[ApiVersion]` and not `[ApiVersionNeutral]`. `[ApiVersion]` is `Inherited = false`, so the analyzer inspects the immediate type — derived controllers without their own `[ApiVersion]` attribute are ignored.

Recognised dictionary shapes (suppress the warning when an `api-version` key is present):

- `c => new RouteValueDictionary { ["id"] = c.Id, ["api-version"] = "..." }` — initializer block.
- `c => new RouteValueDictionary { ["id"] = c.Id, [ApiVersionKey] = "..." }` — initializer block with a const-string identifier (resolved via the semantic model).
- `c => { return new RouteValueDictionary { ... }; }` — block-bodied lambda with a return statement.
- `c => new RouteValueDictionary(new { id = c.Id })` — anonymous-object ctor shape; **always flagged** because C# property names cannot contain `-`, so the api-version key is necessarily missing.

Key matching is case-insensitive (matches `RouteValueDictionary`'s runtime semantics): `"API-VERSION"`, `"Api-Version"`, etc., are all accepted.

The single-id overloads (`CreatedAtRoute(routeName, idSelector)` / `WithLocation(routeName, idSelector)`) construct the dictionary internally with a single non-`"api-version"` key, so they are always flagged unless followed by `.WithVersionedRoute()` (or the manual `.WithRouteValueResolver("api-version", ...)`).

## Why it matters

Under query-string or header API versioning (`Asp.Versioning.QueryStringApiVersionReader`, `HeaderApiVersionReader`, or any composite reader that is not URL-segment versioning), link generation does not auto-include the request's `api-version`. The route values dictionary is the only signal it has. Omitting `["api-version"]` produces a `Location` header like:

```
Location: /api/orders/42
```

…which 404s on dereference because the controller is registered under the versioned route group. The 2xx response itself looks correct in tests; only an integration test that actually `GET`s the `Location` URL surfaces the bug.

This is a recurring source of regressions: every author has to remember to add the key, and code review consistently misses it. The analyzer plus the runtime helper close the loop.

## Bad examples

```csharp
[ApiController]
[ApiVersion("2026-12-01")]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id:int}", Name = "Orders_GetById")]
    public IActionResult Get(int id) => Ok(...);

    [HttpPost]
    public ActionResult<Order> Create(CreateOrderRequest req) =>
        _mediator.Send(new CreateOrderCommand(req))
            .ToHttpResponse(opts => opts.CreatedAtRoute(            // TRLS023
                "Orders_GetById",
                o => new RouteValueDictionary { ["id"] = o.Id }))   // missing ["api-version"], no .WithVersionedRoute()
            .AsActionResult<Order>();
}
```

```csharp
// Anonymous-object ctor shape — C# property names can't contain hyphens, so api-version
// can never be present. Always flagged.
opts.CreatedAtRoute(
    "Orders_GetById",
    o => new RouteValueDictionary(new { id = o.Id }));              // TRLS023
```

## Good examples

The recommended fix is to chain `.WithVersionedRoute()` from the [`Trellis.Asp.ApiVersioning`](../integration-aspnet.md#api-version-aware-location-headers) package. It resolves the **final named-route or MVC-action destination**, not the current endpoint. It accepts a requested version only when mapped to that target, otherwise uses a single mapped declared version, then a mapped host default, otherwise throws. Action-level `[MapToApiVersion]` narrows controller declarations.

```csharp
using Trellis.Asp.ApiVersioning;

[HttpPost]
public ActionResult<Order> Create(CreateOrderRequest req) =>
    _mediator.Send(new CreateOrderCommand(req))
        .ToHttpResponse(opts => opts
            .CreatedAtRoute(
                "Orders_GetById",
                o => new RouteValueDictionary { ["id"] = o.Id })
            .WithVersionedRoute())
        .AsActionResult<Order>();
```

If you can't (or prefer not to) take the package dependency, add the key manually:

```csharp
opts.CreatedAtRoute(
    "Orders_GetById",
    o => new RouteValueDictionary { ["id"] = o.Id, ["api-version"] = "2026-12-01" });
```

Either form silences TRLS023, but manual values do not supply target-aware checks. For manual query values, ensure the destination actually accepts the version.

### Runtime migration considerations

The code-fix syntax is unchanged; its runtime behavior is now stricter:

- Missing or ambiguous destinations throw `InvalidOperationException`; prefer uniquely named routes. Suppressed link-generation endpoints are excluded, and resolution never falls back to the current endpoint.
- Explicit pins must map to the destination. Segment destinations receive the resolved/pinned value in their actual `:apiVersion` parameter (not necessarily `version`), without a duplicate query value.
- Neutral and missing-metadata destinations skip injection and remove supplied `api-version` entries. Missing-metadata warnings identify and deduplicate by **destination endpoint / AppDomain**, not caller; neutral targets stay quiet.
- Version injection runs through `WithLocationRouteResolver` after the domain selector and all legacy route-value callbacks, overriding competing version values on a per-execution clone. One callback slot means the last registration wins, including repeated `WithVersionedRoute` calls; errors propagate.
- Literal/selector `Created(...)`, `WriteOutcome`-owned URIs, existing statuses, and action-link trimming/AOT limitations are unchanged. No new registration or analyzer diagnostic is introduced.

`PageUrl` shares corrected mapping checks, not the new Location segment behavior: it retains ambient segment routing, cross-route validation, consumer overrides, quiet missing-metadata handling, and rejection of explicit segment pins.

## Code fix available

Yes. The code fix:

1. Wraps the flagged `CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)` call so the chain becomes `<original>.WithVersionedRoute()`.
2. Inserts `using Trellis.Asp.ApiVersioning;` if missing. The using is added in the same scope as existing usings (file-scoped namespace, block-scoped namespace, or top-level) and matches the file's existing line-ending style.

The fix does **not** add the `Trellis.Asp.ApiVersioning` package reference. If the package isn't yet referenced you'll see a "type or namespace not found" build error pointing at the new namespace; add the package via:

```bash
dotnet add package Trellis.Asp.ApiVersioning
```

## Configuration

Standard Roslyn configuration applies.

```ini
dotnet_diagnostic.TRLS023.severity = error
```

For a versioned controller linking to a neutral destination, prefer `.WithVersionedRoute()` — it inspects the target and removes the query version automatically. If you deliberately own manual Location generation instead, suppress at the call site with a clear justification:

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Trellis", "TRLS023",
    Justification = "Manual neutral-target link; application validates the destination and omits api-version.")]
public ActionResult<Order> CrossVersionRedirect() => ...;
```

> [!NOTE]
> If your controller is api-version-neutral by design, prefer adding `[ApiVersionNeutral]` to it. The analyzer skips `[ApiVersionNeutral]` controllers, and the change documents the intent.

## Limitations

- The analyzer recognises only the dictionary shapes listed above. Computed dictionaries (`c => myDict`, `c => MakeDict(c)`) on the 2-arg overload are bailed to false-negative — TRLS023 won't fire even if `myDict` is missing the key. Chaining `.WithVersionedRoute()` is still the right answer there.
- The analyzer does not run on Minimal API endpoints (`app.MapPost("/orders", ...)`); it's scoped to `HttpResponseOptionsBuilder<T>` calls inside MVC controllers.
