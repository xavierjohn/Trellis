---
name: recipe-04-minimal-api-endpoint
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Wires Result to a Minimal API endpoint with HttpResponseOptionsBuilder and ToHttpResponse. Use when adding endpoints, 201 Created with Location, or versioned links."
---
# Recipe 4 — Minimal-API endpoint wiring `Result<T>` → `HttpResponseOptionsBuilder` → `ToHttpResponse`

**Requires:** `Trellis.Core`, plus `Trellis.Asp`, `Trellis.Http.Abstractions`, `Trellis.Mediator`.

**Problem.** Map a `Result<Order>` to a fully-conformant HTTP response: `200` with strong ETag and `Last-Modified`, `404` Problem Details when the order is missing, `304` on an `If-None-Match` match.

```csharp
using Mediator;
using Microsoft.AspNetCore.Builder;
using Trellis;
using Trellis.Asp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrellisAspWithScalarValidation();  // error → status mapping + scalar-value validation
builder.Services.AddOrdersFeature();       // behaviors, validation, unit of work: see recipe-02-command-handler for what stays application-owned

var app = builder.Build();

app.MapGet("/orders/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
{
    if (!OrderId.TryCreate(id, nameof(id)).TryGetValue(out var orderId, out var idError))
        return idError.ToHttpResponse();

    Result<Order> result = await mediator.Send(new GetOrderQuery(orderId), ct);

    return result.ToHttpResponse(opts => opts
        .WithETag(o => o.ETag)                         // strong ETag from aggregate
        .WithLastModified(o => o.LastModified)         // RFC 1123
        .Vary("Accept", "Accept-Language")
        .EvaluatePreconditions());                     // 304 / 412 handling on GET and HEAD
});

app.Run();
```

**What it shows.** `ToHttpResponse` returns `Microsoft.AspNetCore.Http.IResult` and is the **only** supported response verb. The fluent `HttpResponseOptionsBuilder<TDomain>` configures protocol semantics (`WithETag`, `WithLastModified`, `Vary`, `EvaluatePreconditions`) without leaking HTTP into the handler. Failures (`Error.NotFound`, `Error.InvalidInput`, …) round-trip through Problem Details using the `TrellisAspOptions` mapping registered by `AddTrellisAsp`.

`GetOrderQuery` is an application-owned `IQuery<Result<Order>>` that takes an `OrderId`. `ETag`, `CreatedAt` and `LastModified` are stamped on save by the Trellis EF interceptors, so the `DbContext` options must call `AddTrellisInterceptors()`; without it every aggregate has an empty `ETag` and a default `LastModified`, `.WithETag(o => o.ETag)` emits `ETag: ""`, and every `If-None-Match: ""` request gets `304`.

**PUT upserts.** The application returns `Result.Ok(WriteOutcome.Created(order))` when it adds
the resource, or `Result.Ok(WriteOutcome.Updated(order))` when it updates one. No URL is needed
in the application layer. In this endpoint, `IOrderWriter.UpsertAsync` is an application-owned
service returning `Task<Result<WriteOutcome<Order>>>`:

```csharp
app.MapPut("/orders/{id:guid}", async (
    Guid id, PutOrderRequest request, IOrderWriter writer, CancellationToken ct) =>
{
    Result<WriteOutcome<Order>> result = await writer.UpsertAsync(id, request, ct);
    return result.ToHttpResponse(
        body: order => new { Id = order.Id.Value },
        configure: options => options.WithETag(order => order.ETag));
});
```

For `Result<WriteOutcome<T>>`, pass `body:` and `configure:` together, with a body lambda that uses a member of `T`; a configure-only call does not compile (`CS0121`, ambiguous between the `Result<T>` and `Result<WriteOutcome<T>>` overloads).

Created returns 201 without a Location when the new resource is at the PUT request URL;
Updated returns 200. To generate a Location at the endpoint instead, configure `Created`,
`CreatedAtRoute`, `CreatedAtAction`, or `WithLocation`. Null, empty, or whitespace outcome
locations use that fallback; a nonblank outcome Location wins. The outcome controls status,
so `WithLocation` cannot turn Created into 200 or Updated into 201. Updated and Accepted
do not use these fallbacks. An unresolved configured fallback returns
`response.location-unresolved` (500 by default); callback exceptions propagate.

**Versioned Location links.** For `CreatedAtRoute` / `CreatedAtAction` (201) or `WithLocation` (normal 2xx), load [target-aware API versioning](../trellis-api-asp-apiversioning.md#behavioral-notes) before chaining the existing `.WithVersionedRoute()` / `.WithVersionedRoute(ApiVersion)` APIs. They resolve the version against the final Location destination: they honor action-level version mappings and URL-segment pins, and throw for a missing or ambiguous destination or an unsupported pin. The optional ASP [`WithLocationRouteResolver`](../trellis-api-asp.md#locationroutecontext) hook runs after all `WithRouteValueResolver` callbacks on a cloned route-value dictionary; the last registration wins. Literal and selector `Created(...)` locations, nonblank `WriteOutcome` locations and Accepted monitor URIs never run it. `PageUrl` keeps implicit ambient routing and rejects explicit segment pins, so do not transfer Location segment behavior to it.
