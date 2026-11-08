---
name: recipe-23-if-match-concurrency
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Explains when a mutating endpoint requires If-Match and how to honor the precondition. Use when adding concurrency control to an update endpoint."
---
# Recipe 23 — Concurrency control on aggregate-mutating endpoints: when to require `If-Match`

**Requires:** `Trellis.Core`, plus `Trellis.Asp`, `Trellis.EntityFrameworkCore`, `Trellis.Http.Abstractions`.

**Prerequisites.** The `DbContext` must call `ApplyTrellisConventions(...)` in `ConfigureConventions` (value-object converters and the `ETag` concurrency token) and `AddTrellisInterceptors()` in its options, which generates a new `Aggregate.ETag` on every save. Without the interceptors `ETag` stays empty and `If-Match` can never identify a version (an `If-Match: ""` request would succeed). The read endpoint must send the validator for clients to echo, for example `ToHttpResponse(o => o.WithETag(order => order.ETag))`.

**Problem.** RFC 9110 §13.1.1 lets clients send `If-Match: "etag"` on unsafe methods to detect stale-read race conditions: if the resource's current ETag doesn't match, the server returns `412 Precondition Failed` instead of overwriting concurrent changes. For mutating handlers, the framework provides `ETagHelper.ParseIfMatch(request)` to extract the typed `EntityTagValue[]` from the incoming `If-Match` header, plus the `Result<T>.OptionalETag(...)` / `RequireETag(...)` extensions (and their `*Async` overloads) that evaluate the precondition at the read-modify-write boundary inside the handler chain. (`opts.WithETag(...).EvaluatePreconditions()` on the response builder is a different feature — it only runs on `GET` / `HEAD`, where it evaluates `If-Match` / `If-Unmodified-Since` → `412` and `If-None-Match` / `If-Modified-Since` → `304`; it is **not** the mutation hook.) The decision question: which mutating endpoints actually need this?

A blanket "require `If-Match` on every mutation" rule is wrong. **Not requiring a header is different from ignoring a supplied header.** RFC 9110 §§13.1.1 and 13.2.1 require eligible supplied preconditions to be evaluated before the action; a false `If-Match` must not perform the mutation. Domain transition guards do not replace this HTTP check. Use `OptionalETag` when unconditional callers are allowed and `RequireETag` when the endpoint requires a precondition:

| Endpoint shape | Lost-update window? | Precondition policy |
|---|---|---|
| **Body-less state-transition POST** (`POST /orders/{id}/submit`, `.../approve`, `.../cancel`, `.../return`) | Domain guards reject invalid transitions, but cannot determine whether the client saw the current version. | **Optional — `OptionalETag`.** Missing header proceeds to the domain guard; a supplied mismatch returns `412` before mutation. Require the header only when the endpoint contract demands it. |
| **Body-carrying full-update PUT** (`PUT /orders/{id}` with a full replacement body) | **Yes.** The body silently overwrites whatever the concurrent edit wrote. | **Yes — `RequireETag`.** RFC 6585 says `428 Precondition Required` when missing, RFC 9110 says `412 Precondition Failed` when stale. |
| **Body-carrying partial-update PATCH** with a JSON Patch / JSON Merge Patch document | **Yes.** Same overwrite risk as full update. | **Yes — `RequireETag`.** |
| **Destructive `DELETE /resources/{id}`** | **Yes.** A stale client can delete a version it has not seen after another writer changed it. | **Required — `RequireETag`** by default. EF Core concurrency tokens catch writes racing after the handler's read, not stale-client reads before the request. A deliberately unconditional guarded-transition contract can use `OptionalETag`, but must still honor a supplied header. |
| **Additive set operation** (`POST /orders/{id}/line-items`, `POST /products/{id}/stock-additions +5`) | **Maybe.** Depends on commutativity. Two concurrent `+5` calls produce `+10` correctly; "remove the last line item" against a stale read can drop the wrong item. | **Case-by-case.** Commutative operations may admit unconditional callers via `OptionalETag`; non-commutative operations should use `RequireETag`. Both enforce supplied headers. |
| **Resource creation** (`POST /customers`, `POST /products`) | **N/A.** No prior version of the new aggregate to match against. | **Not normally required.** Any supplied precondition concerns the request's target resource, not the newly created aggregate's ETag. |

```csharp
using Trellis;
using Trellis.Asp;
using Trellis.EntityFrameworkCore;

// State-transition POST — no required header, but a supplied If-Match is enforced.
// Missing proceeds; mismatching, weak-only, empty, or malformed returns 412 before Approve.
app.MapPost("/orders/{id:guid}/approve", (OrderId id, OrderDbContext db, HttpContext httpContext, CancellationToken ct) =>
    db.Orders
        .FirstOrDefaultResultAsync(o => o.Id == id, new Error.NotFound(ResourceRef.For<Order>(id)), ct)
        .OptionalETagAsync(ETagHelper.ParseIfMatch(httpContext.Request))
        .BindAsync(o => o.Approve())
        .CheckAsync(_ => db.SaveChangesResultUnitAsync(ct))
        .ToHttpResponseAsync(OrderResponse.From));

// Full-update PUT — RequireETag at the read-modify-write boundary.
// Missing If-Match → 428; stale → 412; current → proceeds.
app.MapPut("/orders/{id:guid}", (OrderId id, ReplaceOrderRequest request, OrderDbContext db, HttpContext httpContext, CancellationToken ct) =>
    db.Orders
        .FirstOrDefaultResultAsync(o => o.Id == id, new Error.NotFound(ResourceRef.For<Order>(id)), ct)
        .RequireETagAsync(ETagHelper.ParseIfMatch(httpContext.Request))
        .BindAsync(o => o.Replace(request))
        .CheckAsync(_ => db.SaveChangesResultUnitAsync(ct))
        .ToHttpResponseAsync(OrderResponse.From, opts => opts.HonorPrefer()));
```

> **Direct `DbContext` in the Minimal API lambda vs command handler via Mediator.** Both shapes are canonical Trellis. This recipe shows the direct-`DbContext` shape because the precondition belongs at the *read-modify-write* boundary. With Mediator, parse `If-Match` at the HTTP boundary, carry `EntityTagValue[]? IfMatchETags` on the command, and move the same chain into the handler. Keep `OptionalETagAsync(command.IfMatchETags)` or `RequireETagAsync(command.IfMatchETags)` after the not-found projection and before mutation. When `TransactionalCommandBehavior` is registered in the Mediator pipeline it owns the commit, so drop the explicit save step; otherwise keep `SaveChangesResultUnitAsync`. Preserve permission/resource authorization before the precondition check, and retain persistence-level concurrency protection for writes racing after the read.

**Rationale.** Header requirement is an endpoint policy; honoring a supplied precondition is not optional. State machines validate domain transitions, ETag checks enforce the caller's observed version, and persistence concurrency protection handles writes racing after the read. These mechanisms are complementary. A failed supplied precondition must leave state and representation metadata unchanged; no header still permits a guarded transition without a `428`.

**Eager-overload exception.** `FirstOrDefaultResultAsync` accepts an `Error`, not an
error factory, so these query examples supply it eagerly. Do not invent a factory
overload. If lazy not-found construction is required, use the documented
`FirstOrDefaultMaybeAsync` plus `ToResultAsync(() => new Error.NotFound(...))` composition.
