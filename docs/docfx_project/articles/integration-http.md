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
| `ReadJsonMaybeAsync<T>(jsonTypeInfo, ct)` | `Task<Result<HttpResponseMessage>>` | `Task<Result<Maybe<T>>>` | Optional-payload deserialization. `204` / `205` / empty / JSON `null` → `Ok(Maybe.None)`. Invalid JSON → `Fail<Error.Unexpected>` (`http.response-invalid-body`). `T : notnull`. |
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

Bare `ToResultAsync()` uses the built-in mapper from `Trellis.Http/src/HttpResponseExtensions.cs`. Known upstream statuses become typed Trellis failures; the 405/406/412/413/415/416/428 cases stay wrapped in `Error.TransportFault(new HttpError.*(...))`.

| HTTP status | Produced error |
|---|---|
| `400` | `Error.InvalidInput.ForRule("http.bad-request")` |
| `401` | `new Error.AuthenticationRequired()` |
| `403` | `new Error.Forbidden("http.forbidden")` |
| `404` | `new Error.NotFound(ResourceRef.For("HttpResponse"))` |
| `405` with `Allow` | `new Error.TransportFault(new HttpError.MethodNotAllowed(allow))` |
| `405` without `Allow` | `new Error.Unexpected(FaultCodes.HttpResponseFault, faultId)` |
| `406` | `new Error.TransportFault(new HttpError.NotAcceptable(EquatableArray<string>.Empty))` |
| `409` | `new Error.Conflict(null, "http.conflict")` |
| `410` | `new Error.Gone(ResourceRef.For("HttpResponse"))` |
| `412` | `new Error.TransportFault(new HttpError.PreconditionFailed(ResourceRef.For("HttpResponse"), PreconditionKind.IfMatch))` |
| `413` | `new Error.TransportFault(new HttpError.ContentTooLarge())` |
| `415` | `new Error.TransportFault(new HttpError.UnsupportedMediaType(EquatableArray<string>.Empty))` |
| `416` with known `Content-Range` length | `new Error.TransportFault(new HttpError.RangeNotSatisfiable(length, unit))` |
| `422` | `Error.InvalidInput.ForRule("http.unprocessable-content")` |
| `428` | `new Error.TransportFault(new HttpError.PreconditionRequired(PreconditionKind.IfMatch))` |
| `429` | `new Error.RateLimited(retryAdvice)` (parses `Retry-After` into `RetryAdvice`) |
| `501` | `new Error.Unexpected(FaultCodes.NotImplemented)` |
| `503` | `new Error.Unavailable(Retry: retryAdvice)` (parses `Retry-After` into `RetryAdvice`) |
| other / default | `new Error.Unexpected(FaultCodes.HttpResponseFault, faultId)` |

A `405` without `Allow`, a `416` without a known `Content-Range` length, and all unmapped 3xx/5xx statuses fall through to `new Error.Unexpected(FaultCodes.HttpResponseFault, faultId)`, where `faultId` is a freshly generated per-incident identifier. The `Code` is a stable constant so telemetry can aggregate these; the GUID goes to `FaultId`. The strict default parses `Retry-After` into `RetryAdvice` on `Error.RateLimited` and `Error.Unavailable` (delta-seconds → `After`, HTTP-date → `At`), but it does **not** parse `WWW-Authenticate` into `Error.AuthenticationRequired`. Use a custom status map or the body-aware overload when an endpoint-specific contract needs richer header detail.

> [!IMPORTANT]
> **3xx redirects under the strict default fold into `Error.Unexpected`.** `HttpClient` follows redirects automatically by default, so this is mostly relevant only when `AllowAutoRedirect = false` (for example, SSO landing-page detection).

> [!NOTE]
> **Exception propagation.** `Trellis.Http` does not swallow non-Result-shaped exceptions. `HttpRequestException` and `OperationCanceledException` / `TaskCanceledException` propagate. `JsonException` does not: `ReadJsonAsync<T>`, `ReadJsonMaybeAsync<T>`, and `ReadJsonOrNoneOn404Async<T>` all catch it and convert it to `new Error.Unexpected(FaultCodes.HttpResponseInvalidBody, faultId)` with sanitized line/byte detail only, disposing the response either way.

### Single-status handlers

Use these when one specific failure status is part of the contract.

| Handler | HTTP status | Produces |
|---|---|---|
| `HandleNotFoundAsync` | `404` | `Error.NotFound` |
| `HandleUnauthorizedAsync` | `401` | `Error.AuthenticationRequired` |
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
            .HandleUnauthorizedAsync(new Error.AuthenticationRequired() { Detail = "Sign in before creating orders." })
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
                _ when (int)status >= 500 => new Error.Unexpected("products.upstream-fault", Guid.NewGuid().ToString("N")) { Detail = $"upstream {status}" },
                _ when (int)status >= 400 => new Error.Unexpected("products.upstream-rejected", Guid.NewGuid().ToString("N")) { Detail = $"client error {status}" },
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
                    HttpStatusCode.Conflict   => new Error.Conflict(null, "invoices.conflict") { Detail = body },
                    HttpStatusCode.BadRequest => Error.InvalidInput.ForRule("invoices.invalid-request", body),
                    _ => new Error.Unexpected("invoices.upstream-fault", "upstream") { Detail = $"Invoice request failed with {(int)response.StatusCode}: {body}" },
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
| 2xx + invalid JSON | `Fail<Error.Unexpected>` (`http.response-invalid-body`; response disposed first) |
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
                new Error.InvalidInput(EquatableArray.Create(
                    new FieldViolation(InputPointer.ForProperty(nameof(productId)), "stock.insufficient") { Detail = "Out of stock." })))
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
