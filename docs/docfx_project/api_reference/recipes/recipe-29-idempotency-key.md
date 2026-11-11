---
name: recipe-29-idempotency-key
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows the IETF Idempotency-Key middleware with UseTrellisIdempotency. Use when making POST or PATCH safe under client retries."
---
# Recipe 29 — IETF `Idempotency-Key` middleware on POST / PATCH with `UseTrellisIdempotency`

**Requires:** `Trellis.Core`, plus `Trellis.Asp`, `Trellis.ServiceDefaults`.

**Problem.** A client retries a `POST /payments` after a network hiccup. The original request already created the payment; the retry must return the same `201 Created` payload — not a second `201` (double-charge), and not a `400 Bad Request` because the second attempt now violates a uniqueness rule. The IETF [`draft-ietf-httpapi-idempotency-key-header`](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/) names the header (`Idempotency-Key`) and the contract: Trellis stores the first response under `(scope, parsed key)` with its request fingerprint, replays it on a matching-fingerprint retry, rejects fingerprint mismatches with `MismatchStatusCode` (default `422 Unprocessable Entity`), and serialises matching concurrent in-flight retries with `409 Conflict` + `Retry-After`. Doing this by hand per endpoint scatters request buffering, response capture, scope/tenant isolation, and store-side CAS across the codebase.

**Solution.** `Trellis.Asp.Idempotency.IdempotencyMiddleware` (opt-in via the `[Idempotent]` endpoint attribute) does the contract end-to-end. The middleware is a no-op on endpoints that do not carry the attribute and on methods outside the configured set (`POST` and `PATCH` by default). On an opted-in endpoint and method, a request without the header gets `400` with code `idempotency.key_required`; set `RequireKeyOnOptedInEndpoints = false` to let such requests run without idempotency processing.

```csharp
// Program.cs
builder.Services.AddControllers();
builder.Services.AddTrellis(t => t
    .UseAsp()
    .UseProblemDetails()
    .UseIdempotency(opt =>
    {
        opt.Ttl = TimeSpan.FromHours(24);
        opt.MaxRequestBodyBytes = 256 * 1024;
    }));
builder.Services.AddInMemoryIdempotencyStore(); // dev / single-instance; swap for a durable shared store in production (`Trellis.Asp.Idempotency.Cosmos` ships one)

var app = builder.Build();
app.UseTrellisIdempotency();
app.MapControllers();
```

This sample has no authentication, so every caller shares the `anonymous` scope and two clients that pick the same key collide. In a real host register an actor provider (for example `AddClaimsActorProvider()`) and mount `UseTrellisIdempotency()` after `UseRouting()`, `UseAuthentication()` and `UseAuthorization()` so the default resolver partitions the store by actor. Replace `IIdempotencyScopeResolver` for a different boundary such as tenant plus actor.

```csharp
// PaymentsController.cs
[ApiController]
[Route("payments")]
public sealed class PaymentsController : ControllerBase
{
    [HttpPost]
    [Idempotent]
    public Task<IActionResult> CreateAsync([FromBody] CreatePaymentRequest body, CancellationToken ct)
        => /* handler returns 201 Created with the payment representation */;
}
```

```csharp
// Minimal API equivalent — attach the attribute as endpoint metadata.
app.MapPost("/payments", CreatePaymentAsync).WithMetadata(new IdempotentAttribute());
```

**What it shows.** The middleware reads the configured header (default `Idempotency-Key`), rejects raw header values above the parser's 4,096-character defensive cap, parses an RFC 9651 String-valued Item, buffers the request body up to `MaxRequestBodyBytes`, computes a SHA-256 fingerprint over method, `PathBase + Path`, canonicalized query, `Content-Type`, `Content-Encoding`, configured `AdditionalFingerprintHeaders`, and body bytes, resolves a scope through `IIdempotencyScopeResolver`, and calls `IIdempotencyStore.TryReserveAsync(scope, key, fingerprint, ct)`.

**Key syntax.** Send an [RFC 9651 String-valued Item](https://www.rfc-editor.org/rfc/rfc9651#section-3.3.3), including the double quotes: `Idempotency-Key: "payment-1"`. Trellis retains the Idempotency-Key draft's String-valued key contract and uses the current Structured Fields standard; the header itself is not a finalized RFC. Bare values such as `payment-1` are invalid. Strings contain zero or more printable-ASCII characters (`0x20`-`0x7E`) with only quote/backslash escapes (`\"` and `\\`). An empty quoted String (`""`) is a real key: matching retries replay it, and a different request fingerprint in the same scope is rejected. Leading/trailing SP outside the Item and valid Item parameters are accepted, including Date and UTF-8 percent-encoded Display String values; parameters do not contribute to the parsed key. Dates and Display Strings are not valid as the primary key. Invalid syntax and duplicate headers return `400 Bad Request`; the middleware also returns `400` when the decoded String exceeds `MaxKeyLength`, which excludes quotes, escape overhead, SP outside the Item, and parameters. See [`IdempotencyKeyParser`](../trellis-api-asp.md#idempotencykeyparser) for the exact surface.

**Scope is not the route.** The default resolver uses the current actor's id via `IActorProvider`, falling back to the shared anonymous scope when no provider is registered or no actor resolves. A custom resolver can supply a different isolation boundary, such as tenant plus actor. All of an actor's opted-in endpoints sharing the store share one client key namespace. The fingerprint is compared against the entry at `(scope, key)`; it is not a third key component. While that reservation or unexpired snapshot exists, the same actor, key, and body sent to `POST /restaurants/A/import` and then `POST /restaurants/B/import` produce a path mismatch and `MismatchStatusCode` (default `422`), not a replay or a second independent operation. Use distinct keys for distinct operations and reuse a key for retries of the same request. See [request identity and fingerprint](../trellis-api-asp.md#namespace-trellisaspidempotency).

The store either issues a `Reserved` outcome (carrying an opaque CAS reservation token), reports `AlreadyInFlight` (matching fingerprint, concurrent reservation still active — the middleware responds `409 Conflict` with `Retry-After`), `Replay` (a matching-fingerprint, unexpired completed snapshot — the middleware writes the captured status code, body and headers, except `Set-Cookie` (unless `IncludeSetCookieInSnapshot` is true) and hop-by-hop headers such as `Date`, `Server`, `Transfer-Encoding` and `Connection`, and adds `Idempotent-Replayed: true`; 4xx responses are stored and replayed, 5xx responses are abandoned), or `BodyHashMismatch` (same scope and key, different request fingerprint — the middleware responds with `MismatchStatusCode`, default `422 Unprocessable Entity`). Despite its name, `BodyHashMismatch` covers changes to any fingerprinted component, not just the body. When the reservation is `Reserved`, the middleware decorates `IHttpResponseBodyFeature` with `CapturingResponseBodyFeature` (a tee — bytes still flow to the client while a bounded copy is captured) and registers an `OnStarting` callback that snapshots the final status code and response headers before the first byte flushes. On a successful flush within `MaxResponseBodyBytes`, the middleware calls `IIdempotencyStore.CompleteAsync(scope, key, reservationId, snapshot, ct)` against a bounded 5-second cancellation token (NOT `HttpContext.RequestAborted`, so finalisation still runs if the client disconnected). On any failure path — exception, response-too-large, `SendFileAsync` (uncapturable), middleware abort, **5xx response status** (treated as transient per the IETF Idempotency-Key draft), or **response trailers** (cannot be replayed by the snapshot writer) — the middleware calls `AbandonAsync(scope, key, reservationId, ct)` so the next retry can re-reserve. Reservation tokens are opaque `string` GUIDs the store uses for CAS so a stale completer cannot finalise a reservation the store already took over after the reservation timeout elapsed (a later same-key request re-reserves; there is no background sweeper).

**Composition rules.** `services.AddTrellisIdempotency(...)` (or the builder slot `t.UseIdempotency(...)`) registers options + scope resolver + an internal marker; `services.AddInMemoryIdempotencyStore()` is a separate, explicit call so a dev-only in-memory store is never silently inherited into production. `app.UseTrellisIdempotency()` throws at startup if `AddTrellisIdempotency(...)` was not called. The `IIdempotencyStore` registration is also validated at startup when the container exposes `IServiceProviderIsService` (the default Microsoft.Extensions.DependencyInjection container does); on containers that do not expose it the missing-store failure surfaces as a per-request resolution error on the first opted-in request. The in-memory store is single-process only; multi-instance hosts need a durable shared store: `Trellis.Asp.Idempotency.Cosmos` ships one, and a store you write yourself (for example over EF Core, with `Scope` as a discriminator column) must implement the same CAS contract and should pass `IdempotencyStoreConformance` from `Trellis.Testing.Idempotency`. `MaxRequestBodyBytes` and `MaxResponseBodyBytes` are hard caps: exceeding the request cap returns `413 Payload Too Large` before any handler runs; exceeding the response cap aborts capture and records no snapshot (the next retry re-executes), so the cap should be set high enough to envelop the largest legitimate response from any opted-in endpoint. Endpoints that stream via `SendFileAsync` cannot be captured and are equivalent to exceeding the response cap — model those as non-idempotent or convert them to a buffered response.

**Tests.** Use `Microsoft.AspNetCore.TestHost.TestServer` (`WebApplication` with `UseTestServer()`, or `WebApplicationFactory`) plus an `IIdempotencyStore` registered as a singleton (`InMemoryIdempotencyStore`) plus `TimeProvider` swapped for `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (from the `Microsoft.Extensions.TimeProvider.Testing` NuGet package). Within one scope, send the same key and complete request twice — assert the second call has the same status code and body, the `Idempotent-Replayed: true` header, and the captured application headers. Defaults to know: `ReservationTimeout` 30 s, `MaxKeyLength` 200 (a longer key gets `400 idempotency.key_too_long`), `Ttl` 24 h; the `Retry-After` on a `409` is the whole seconds left on the reservation. Reuse the key with a changed body, path, or configured method on an opted-in endpoint — assert `MismatchStatusCode` (default `422`) and that the original snapshot is still replayable. Advance `FakeTimeProvider` past `ReservationTimeout` to exercise the reservation-timeout takeover path (a later same-scope, same-key, matching-fingerprint request re-reserves the stale entry). The NuGet package is `Microsoft.Extensions.TimeProvider.Testing` (the namespace containing `FakeTimeProvider` is `Microsoft.Extensions.Time.Testing`); the test project should reference the package the same way `Trellis.Testing.Worker`'s harness does.
