---
title: HTTP Client Integration
package: Trellis.Http
topics: [http, httpclient, json, status-mapping, maybe, optional-payload, disposal]
related_api_reference: [trellis-api-http.md, trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# HTTP Client Integration

`Trellis.Http` bridges `Task<HttpResponseMessage>` into `Task<Result<HttpResponseMessage>>` and `Task<Result<T>>` pipelines so calling code stops mixing status-code branching, exception flow, and JSON deserialization.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Bridge a raw `HttpClient` call into a result pipeline (no body inspection) | `ToResultAsync()` or `ToResultAsync(statusMap)` | [Status mapping](#status-mapping) |
| Map one well-known status (404 / 401 / 409) to a typed `Error` | `HandleNotFoundAsync` / `HandleUnauthorizedAsync` / `HandleConflictAsync` | [Single-status handlers](#single-status-handlers) |
| Map *many* statuses with a status switch | `ToResultAsync(Func<HttpStatusCode, Error?>)` | [Multi-status status switch](#multi-status-status-switch) |
| Map statuses by inspecting the response body | `ToResultAsync(Func<HttpResponseMessage, CancellationToken, Task<Error?>>, ct)` | [Body-aware mapping](#body-aware-mapping) |
| Read a *required* JSON body | `ReadJsonAsync<T>(jsonTypeInfo, ct)` | [Reading JSON](#reading-json) |
| Read an *optional* body (allow empty / 204 / 205 / JSON `null`) | `ReadJsonMaybeAsync<T>(jsonTypeInfo, ct)` | [Reading JSON](#reading-json) |
| Treat `404` as "resource absent" (return `Maybe.None`) | `ReadJsonOrNoneOn404Async<T>(jsonTypeInfo, ct)` | [Reading JSON](#reading-json) |

## Use this guide when

- You call another HTTP service from .NET and want results, not exceptions, for expected statuses.
- You need to deserialize JSON into a typed payload while keeping AOT-friendly source-generated metadata.
- You want a single primitive (`ToResultAsync`) instead of `EnsureSuccessStatusCode` + manual `if (response.StatusCode == ...)` branching.

## Surface at a glance

`Trellis.Http` exposes one static class, `HttpResponseExtensions`, with eight extension methods.

| Method | Receiver | Returns | Purpose |
|---|---|---|---|
| `ToResultAsync(statusMap?)` | `Task<HttpResponseMessage>` | `Task<Result<HttpResponseMessage>>` | Bridge into a result pipeline. With no map, 2xx → `Ok`, non-2xx → typed Trellis failure. With a map, return `null` to pass through. (No `CancellationToken` parameter — let the upstream `*Async` call carry it.) |
| `ToResultAsync(mapper, ct)` | `Task<HttpResponseMessage>` | `Task<Result<HttpResponseMessage>>` | Body-aware bridge. Async mapper invoked **only** on non-success status codes. |
| `HandleNotFoundAsync(error)` | `Task<HttpResponseMessage>` | `Task<Result<HttpResponseMessage>>` | Map `404` to a typed `Fail`. |
| `HandleUnauthorizedAsync(error)` | `Task<HttpResponseMessage>` | `Task<Result<HttpResponseMessage>>` | Map `401` to a typed `Fail`. |
| `HandleConflictAsync(error)` | `Task<HttpResponseMessage>` | `Task<Result<HttpResponseMessage>>` | Map `409` to a typed `Fail`. |
| `ReadJsonAsync<T>(jsonTypeInfo, ct)` | `Task<Result<HttpResponseMessage>>` | `Task<Result<T>>` | Required-payload deserialization. Empty / `null` / invalid JSON / `204` / `205` → `Fail`. `T : notnull`. |
| `ReadJsonMaybeAsync<T>(jsonTypeInfo, ct)` | `Task<Result<HttpResponseMessage>>` | `Task<Result<Maybe<T>>>` | Optional-payload deserialization. `204` / `205` / empty / JSON `null` → `Ok(Maybe.None)`. Invalid JSON throws `JsonException` (intentional). `T : notnull`. |
| `ReadJsonOrNoneOn404Async<T>(jsonTypeInfo, ct)` | `Task<HttpResponseMessage>` | `Task<Result<Maybe<T>>>` | Terminal optional-resource read. `404` → `Ok(Maybe.None)`; other non-2xx use strict mapping. `T : notnull`. |

Full signatures: [`trellis-api-http.md`](../api_reference/trellis-api-http.md).

## Installation

```bash
dotnet add package Trellis.Http
```

## Quick start

Call an endpoint, map one expected failure status, read a required payload.

```csharp
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Http;

[JsonSerializable(typeof(UserDto))]
internal partial class ApiJsonContext : JsonSerializerContext { }

public sealed record UserDto(string Id, string DisplayName);

public sealed class UserDirectoryClient(HttpClient httpClient)
{
    public Task<Result<UserDto>> GetUserAsync(string userId, CancellationToken ct) =>
        httpClient.GetAsync($"users/{userId}", ct)
            .HandleNotFoundAsync(new Error.NotFound(ResourceRef.For<UserDto>(userId)) { Detail = $"User {userId} not found" })
            .ReadJsonAsync(ApiJsonContext.Default.UserDto, ct);
}
```

## Status mapping

Handle status codes **before** reading the body. This keeps transport failures separate from payload bugs.

### Strict default with HTTP-header context

Bare `ToResultAsync()` (no `statusMap`) maps non-success status codes to typed Trellis errors. As of v3.x, the strict default also **inspects the upstream response headers** and copies the relevant context into the typed error so downstream rendering (e.g. ASP's `Allow` / `Retry-After` header emission) sees the upstream's intent rather than an empty placeholder.

| HTTP status | Header consulted | Surfaces on |
|---|---|---|
| `401 Unauthorized` | `WWW-Authenticate` (scheme + best-effort parameter parse). **Token68 form** (e.g. `Negotiate <base64-token>`) degrades to scheme-only — `AuthChallenge` has no slot for the bare token; use the body-aware `ToResultAsync(mapper, ct)` overload (the only public API that exposes `HttpResponseMessage.Headers`) if token68 round-trip matters. | `Error.Unauthorized.Challenges` |
| `405 Method Not Allowed` | `Allow` (response content header). When upstream omits it, falls through to `Error.InternalServerError`. | `Error.MethodNotAllowed.Allow` |
| `416 Range Not Satisfiable` | `Content-Range` header presence with a known length (preserves unit and length, including the legitimate `bytes */0` empty-resource case). When upstream omits the header entirely or sends a Length-unspecified form like `bytes 0-99/*`, falls through to `Error.InternalServerError`. | `Error.RangeNotSatisfiable.CompleteLength` + `Error.RangeNotSatisfiable.Unit` |
| `429 Too Many Requests` | `Retry-After` (delay seconds **or** HTTP date; negative deltas treated as absent) | `Error.TooManyRequests.RetryAfter` |
| `503 Service Unavailable` | `Retry-After` | `Error.ServiceUnavailable.RetryAfter` |

Headers that aren't present produce empty arrays / null `RetryAfter` / empty `Challenges` — the mapper never invents values. **Two exceptions:** `405` without `Allow` and `416` without `Content-Range` fall through to `Error.InternalServerError` (per the rows above) rather than producing typed errors with default empty/zero values; rendering those defaults through ASP would fabricate misleading wire headers. `406 Not Acceptable`, `415 Unsupported Media Type`, and other statuses without a single canonical response header still produce typed errors with default empty/zero context.

> [!IMPORTANT]
> **3xx redirects under the strict default fold into `Error.InternalServerError`.** `HttpClient` follows redirects automatically by default, so this is rarely seen — but callers who set `HttpClientHandler.AllowAutoRedirect = false` (e.g. SSO landing-page detection) must use `ToResultAsync(statusMap)` or the body-aware overload to handle 3xx explicitly.

> [!NOTE]
> **Exception propagation.** `Trellis.Http` does not swallow non-Result-shaped exceptions. `HttpRequestException` (network failure, DNS, TLS), `OperationCanceledException` / `TaskCanceledException` (cancellation, timeout), and `JsonException` from **both** `ReadJsonMaybeAsync<T>` and `ReadJsonOrNoneOn404Async<T>` (which delegates to `ReadJsonMaybeAsync<T>` for non-404 statuses) propagate through the chain. Only `ReadJsonAsync<T>` catches `JsonException` and maps it to `Fail<InternalServerError>` with structured position info (line / byte) only — never `JsonException.Message`, never `JsonException.Path` (which can include user-controlled dictionary keys), never the response body content — so user data the upstream may have echoed cannot leak into the failure detail.

### Single-status handlers

Use these when one specific failure status is part of the contract.

| Handler | HTTP status | Produces |
|---|---|---|
| `HandleNotFoundAsync` | `404` | `Error.NotFound` |
| `HandleUnauthorizedAsync` | `401` | `Error.Unauthorized` |
| `HandleConflictAsync` | `409` | `Error.Conflict` |

Each operates on `Task<HttpResponseMessage>` (the entry point of the chain). The next operator is `ReadJsonAsync` / `ReadJsonMaybeAsync`.

```csharp
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Http;

[JsonSerializable(typeof(CreateOrderRequest))]
[JsonSerializable(typeof(OrderDto))]
internal partial class OrdersJsonContext : JsonSerializerContext { }

public sealed record CreateOrderRequest(string CustomerId, decimal Total);
public sealed record OrderDto(string Id, decimal Total);

public sealed class OrdersClient(HttpClient httpClient)
{
    public Task<Result<OrderDto>> CreateAsync(CreateOrderRequest request, CancellationToken ct) =>
        httpClient.PostAsJsonAsync("orders", request, OrdersJsonContext.Default.CreateOrderRequest, ct)
            .HandleUnauthorizedAsync(new Error.Unauthorized() { Detail = "Sign in before creating orders." })
            .ReadJsonAsync(OrdersJsonContext.Default.OrderDto, ct);
}
```

### Multi-status status switch

For more than one mapped status, use `ToResultAsync(Func<HttpStatusCode, Error?>)`. Return `null` to pass through; return an `Error` to short-circuit the chain.

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Http;

[JsonSerializable(typeof(ProductDto))]
internal partial class ProductsJsonContext : JsonSerializerContext { }

public sealed record ProductDto(string Id, string Name);

public sealed class ProductsClient(HttpClient httpClient)
{
    public Task<Result<ProductDto>> GetAsync(string productId, CancellationToken ct) =>
        httpClient.GetAsync($"products/{productId}", ct)
            .ToResultAsync(status => status switch
            {
                HttpStatusCode.NotFound  => new Error.NotFound(ResourceRef.For<ProductDto>(productId)),
                HttpStatusCode.Forbidden => new Error.Forbidden("products.read"),
                _ when (int)status >= 500 => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"upstream {status}" },
                _ when (int)status >= 400 => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"client error {status}" },
                _ => null,
            })
            .ReadJsonAsync(ProductsJsonContext.Default.ProductDto, ct);
}
```

### Body-aware mapping

When status alone is not enough, supply `Func<HttpResponseMessage, CancellationToken, Task<Error?>>`. The mapper is invoked **only** on non-success responses and may read body, headers, or problem-details payload.

```csharp
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Http;

[JsonSerializable(typeof(CreateInvoiceRequest))]
[JsonSerializable(typeof(InvoiceDto))]
internal partial class InvoicesJsonContext : JsonSerializerContext { }

public sealed record CreateInvoiceRequest(string CustomerId, decimal Total);
public sealed record InvoiceDto(string Id, decimal Total);

public sealed class InvoicesClient(HttpClient httpClient)
{
    public Task<Result<InvoiceDto>> CreateAsync(CreateInvoiceRequest request, CancellationToken ct) =>
        httpClient.PostAsJsonAsync("invoices", request, InvoicesJsonContext.Default.CreateInvoiceRequest, ct)
            .ToResultAsync(async (response, token) =>
            {
                var body = await response.Content.ReadAsStringAsync(token);
                return response.StatusCode switch
                {
                    HttpStatusCode.Conflict   => new Error.Conflict(null, "conflict") { Detail = body },
                    HttpStatusCode.BadRequest => new Error.BadRequest("bad-req") { Detail = body },
                    _ => new Error.InternalServerError("upstream") { Detail = $"Invoice request failed with {(int)response.StatusCode}: {body}" },
                };
            }, ct)
            .ReadJsonAsync(InvoicesJsonContext.Default.InvoiceDto, ct);
}
```

Capture caller state via closure — the v1 `TContext` channel is gone (it was redundant).

## Reading JSON

Three read modes, distinguished by what "no payload" means.

### `ReadJsonAsync<T>` — required payload

| Outcome | Result |
|---|---|
| 2xx + valid JSON | `Ok(value)` |
| 2xx + `null` body / empty / invalid JSON / `204` / `205` | `Fail` |
| Non-2xx | `Fail` (passes through prior failure or maps via strict default) |

### `ReadJsonMaybeAsync<T>` — optional payload

| Outcome | Result |
|---|---|
| 2xx + valid JSON | `Ok(Maybe.From(value))` |
| 2xx + `204` / `205` / empty / JSON `null` | `Ok(Maybe.None)` |
| 2xx + invalid JSON | **throws** `JsonException` (response disposed first) |
| Non-2xx | `Fail` |

### `ReadJsonOrNoneOn404Async<T>` — optional resource

| Outcome | Result |
|---|---|
| 2xx + valid JSON | `Ok(Maybe.From(value))` |
| 2xx + `204` / `205` / empty / JSON `null` | `Ok(Maybe.None)` |
| `404` | `Ok(Maybe.None)` |
| Other non-2xx | `Fail` (typed Trellis failure via strict mapping) |

> [!WARNING]
> `ReadJsonMaybeAsync` does NOT catch `JsonException`. Use it when "optional body" is allowed, not when malformed JSON should be silent. The response is still disposed before the exception escapes.

## Disposal contract

`Trellis.Http` owns `HttpResponseMessage` disposal on terminal and transformative paths.

| Path | Disposes? |
|---|---|
| `ToResultAsync` (both overloads) on the `Fail` branch | Yes |
| `Handle*Async` on the matching status (`Fail` branch) | Yes |
| Pass-through (success from bare `ToResultAsync`, non-matching `Handle*Async`, mapper returning `null`) | No — caller owns until the chain reaches `ReadJson*` |
| `ReadJsonAsync` / `ReadJsonMaybeAsync` / `ReadJsonOrNoneOn404Async` | Yes — always, success or failure |

In practice: once you call any `ReadJson*`, you no longer need to dispose the response yourself.

## Composition

Once an HTTP call becomes `Result<T>`, it composes with the rest of Trellis (`Bind`, `Map`, `Ensure`, etc.).

```csharp
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Trellis;
using Trellis.Http;

[JsonSerializable(typeof(InventoryCheckDto))]
[JsonSerializable(typeof(PaymentReceiptDto))]
internal partial class CheckoutJsonContext : JsonSerializerContext { }

public sealed record InventoryCheckDto(bool InStock);
public sealed record PaymentReceiptDto(string Id);

public sealed class CheckoutClient(HttpClient httpClient)
{
    public Task<Result<PaymentReceiptDto>> ChargeAsync(string productId, CancellationToken ct) =>
        httpClient.GetAsync($"inventory/{productId}", ct)
            .ToResultAsync()
            .ReadJsonAsync(CheckoutJsonContext.Default.InventoryCheckDto, ct)
            .EnsureAsync(
                inventory => inventory.InStock,
                new Error.UnprocessableContent(EquatableArray.Create(
                    new FieldViolation(InputPointer.ForProperty(nameof(productId)), "validation.error") { Detail = "Out of stock." })))
            .BindAsync(
                (_, token) => httpClient.PostAsync($"payments/{productId}", null, token)
                    .ToResultAsync()
                    .ReadJsonAsync(CheckoutJsonContext.Default.PaymentReceiptDto, token),
                ct);
}
```

## Practical guidance

- **Use source-generated JSON metadata.** Keeps the chain AOT-friendly and matches the `JsonTypeInfo<T>` overloads.
- **Pick the right read mode.** `404` means absence → `ReadJsonOrNoneOn404Async`. `404` is an error → bare `ToResultAsync()` or `HandleNotFoundAsync`.
- **Always pass `CancellationToken`.** Every helper accepts it.
- **One status mapper, not many.** Prefer `ToResultAsync(statusMap)` over chaining multiple `Handle*Async` calls when you map more than one status.

## Cross-references

- API surface: [`trellis-api-http.md`](../api_reference/trellis-api-http.md)
- `Result<T>` and `Maybe<T>` semantics: [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- Cookbook recipe (HTTP client → result pipelines): [`trellis-api-cookbook.md`](../api_reference/trellis-api-cookbook.md)
- Migration from v1 (full collapsed-verbs table): [`trellis-api-http.md` → Breaking changes](../api_reference/trellis-api-http.md#breaking-changes-from-v1)
