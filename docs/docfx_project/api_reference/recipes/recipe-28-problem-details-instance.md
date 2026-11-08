---
name: recipe-28-problem-details-instance
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Synthesises ProblemDetails.Instance from a ResourceRef. Use when pointing an error at the resource that failed instead of hand-formatting a URI."
---
# Recipe 28 — Synthesise `ProblemDetails.Instance` from a `ResourceRef`

**Requires:** `Trellis.Core`, plus `Trellis.Asp`.

**Problem.** A `POST /api/orders` endpoint that creates an order against an existing customer fails with `new Error.NotFound(ResourceRef.For<Customer>("abc-123"))` when the customer does not exist. The default RFC 9457 `Instance` is the request URL (`/api/orders`), which does not identify the missing resource — the client has to inspect the body to learn which customer was missing. Worse, telemetry indexed by `Instance` collapses every missing-customer failure on this endpoint into the same `/api/orders` bucket regardless of which customer was missing.

**Solution.** `TrellisAspOptions.SynthesizeProblemDetailsInstanceFromResourceRef` (default `true`) tells `ResponseFailureWriter` to populate `Instance` from the failing `ResourceRef` whenever the request URL does not already identify the resource. The synthesised value is `/{collection}/{escapedId}` (no `/api/` prefix, no api-version segment, no query string). The naive plural fallback lowercases the type name; registered overrides are emitted verbatim, so use lowercase override values if you want to preserve the convention. The original request URI is preserved under `Extensions["request"]` for callers that need both.

```csharp
// Aggregates with default plural names need no extra wiring. The naive default
// is type.ToLowerInvariant() + "s" — fine for "Order" → "orders", "Customer" →
// "customers". Override irregular plurals or domain-specific naming with the
// attribute (preferred — keeps the mapping next to the type):
[ResourceCollectionName("people")]
public sealed class Person { /* ... */ }

[ResourceCollectionName("statuses")]
public sealed class Status { /* ... */ }
```

```csharp
// Composition root. AddTrellisAsp wires the registry; the typed extension
// registers a single override and is AOT/trim-safe. Use the assembly scanner
// when you want every [ResourceCollectionName]-tagged type in an assembly
// picked up at once; mark the call as RequiresUnreferencedCode-aware in
// AOT-published apps.
services.AddTrellisAsp();
services.AddResourceCollectionName<Person>("people");
services.AddResourceCollectionName("LegacyDocument", "legacy-documents");
services.AddResourceCollectionNames(typeof(Person).Assembly);  // alternative
```

**On the wire.** `POST /api/orders` returning `Result.Fail<Order>(new Error.NotFound(ResourceRef.For<Customer>("abc-123")))` emits:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "instance": "/customers/abc-123",
  "request": "/api/orders",
  "code": "error.unspecified",
  "kind": "not-found"
}
```

This `NotFound` was built with `new Error.NotFound(...)`, which names no reason, so `code` is the sentinel `error.unspecified`; the HTTP condition is already carried by `kind` and `status`. Build it as `new Error.NotFound(ResourceRef.For<Customer>(id)) { Code = "customer.not-found" }` and that code reaches the wire instead — worth doing whenever a client would act differently on "no such row" than on "exists, but withheld from you", since both share the 404 surface. `Code` is inherited by every case, so any error given one emits it verbatim; the cases whose reason is required — `Conflict`, `Forbidden`, `InvariantViolation`, `Unexpected` — take it positionally and can never be built without one.

If the same error is raised on `GET /api/customers/abc-123`, the URL already identifies the resource, so synthesis is suppressed and `instance` stays `/api/customers/abc-123` with no `request` extension. Suppression is segment-and-query-value-aware: an id of `"1"` does not match the path segment `v1`, and a percent-encoded path segment `a%2fb` correctly matches the raw id `a/b` (RFC 3986 case-insensitive percent escapes).

**Opting out.** Set `o.SynthesizeProblemDetailsInstanceFromResourceRef = false` in `AddTrellisAsp(o => ...)` to retain the historical request-URL-only `Instance`. The new shape is strictly more informative, so the toggle exists for strict backward compatibility only.

**Defensive synthesis.** The writer never throws while building the synthesised URI. Malformed `ResourceRef` values (empty Type or Id), unsafe collection names, and a missing registry all silently fall back to the request URL — a domain 404/409 can never turn into a 500 because of synthesis. `Error.Aggregate` never promotes a child's `ResourceRef`; the envelope itself carries no resource identity.

**Common-noun guidance.** The naive plural (`Type.ToLowerInvariant() + "s"`) produces poor output for words like `Status` → `statuss`, `Address` → `addresss`, `Person` → `persons`. Override these explicitly via `[ResourceCollectionName(...)]` on the aggregate (preferred) or via `services.AddResourceCollectionName<T>(name)`. The attribute constructor and the DI helper both validate the name as a single safe URL path segment so misconfiguration fails fast at host start.
