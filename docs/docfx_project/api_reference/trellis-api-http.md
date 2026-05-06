---
package: Trellis.Http
namespaces: [Trellis.Http]
types: [HttpResponseExtensions]
version: v3
last_verified: 2026-05-05
audience: [llm]
---
# Trellis.Http &mdash; API Reference

**Package:** `Trellis.Http`
**Namespace:** `Trellis.Http`
**Purpose:** Bridge `Task<HttpResponseMessage>` into `Task<Result<HttpResponseMessage>>` pipelines and deserialize JSON payloads into `Result<T>` / `Result<Maybe<T>>`.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package.

> Bare `ToResultAsync()` is strict by default: non-2xx responses become typed Trellis failures instead of remaining on the success track.

## Use this file when

- You are adapting `HttpClient` calls into Trellis `Result` pipelines.
- You need optional-resource behavior where `404` means `Maybe<T>.None`.
- You need response disposal rules for `HttpResponseMessage` when chaining status mapping and JSON reads.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Strictly fail non-2xx responses | `client.GetAsync(...).ToResultAsync()` | [`HttpResponseExtensions`](#httpresponseextensions) |
| Map one expected status to a domain error | `HandleNotFoundAsync`, `HandleConflictAsync`, `HandleUnauthorizedAsync` | [`HttpResponseExtensions`](#httpresponseextensions) |
| Map several statuses | `ToResultAsync(status => status switch { ... })` | [Multi-status mapping](#multi-status-mapping-with-toresultasyncstatusmap) |
| Inspect an error body before deciding | Body-aware `ToResultAsync((response, ct) => ...)` | [Body-aware mapping](#body-aware-mapping-replaces-handlefailureasynctcontext) |
| Deserialize a required JSON body | `.ReadJsonAsync(jsonTypeInfo, ct)` | [`HttpResponseExtensions`](#httpresponseextensions) |
| Deserialize optional JSON body | `.ReadJsonMaybeAsync(jsonTypeInfo, ct)` | [`HttpResponseExtensions`](#httpresponseextensions) |
| Treat `404` as expected absence | `.ReadJsonOrNoneOn404Async(jsonTypeInfo, ct)` | [`HttpResponseExtensions`](#httpresponseextensions) |

## Common traps

- Once you call any `ReadJson*` terminal helper, the response is disposed by the helper.
- `ReadJsonMaybeAsync` treats `204`, `205`, empty body, and JSON `null` as `Maybe.None`; invalid JSON intentionally throws.
- Use `ReadJsonOrNoneOn404Async` for optional reads. Do not hand-roll a separate 404 branch unless you need custom behavior.

## Type

### `HttpResponseExtensions`

```csharp
public static class HttpResponseExtensions
```

| Signature | Returns | Notes |
| --- | --- | --- |
| `ToResultAsync(this Task<HttpResponseMessage> response, Func<HttpStatusCode, Error?>? statusMap = null)` | `Task<Result<HttpResponseMessage>>` | When `statusMap` is `null`, 2xx statuses pass through as `Ok(response)` and non-2xx statuses map to typed Trellis errors **with response-header context preserved** — see [Strict default with header context](#strict-default-with-header-context). When supplied, a `null` return passes through; a non-null `Error` becomes `Fail` and the underlying response is disposed. |
| `ToResultAsync(this Task<HttpResponseMessage> response, Func<HttpResponseMessage, CancellationToken, Task<Error?>> mapper, CancellationToken ct = default)` | `Task<Result<HttpResponseMessage>>` | Body-aware bridge. The mapper is invoked **only** when `IsSuccessStatusCode == false`. `null` return -> `Ok(response)`; non-null -> `Fail` (response disposed). |
| `HandleNotFoundAsync(this Task<HttpResponseMessage> response, Error.NotFound error)` | `Task<Result<HttpResponseMessage>>` | Maps `404` to `Fail(error)` (response disposed); any other status passes through as `Ok(response)`. Throws `ArgumentNullException` when `error` is null. |
| `HandleConflictAsync(this Task<HttpResponseMessage> response, Error.Conflict error)` | `Task<Result<HttpResponseMessage>>` | Maps `409` to `Fail(error)` (response disposed); pass through otherwise. Throws `ArgumentNullException` when `error` is null. |
| `HandleUnauthorizedAsync(this Task<HttpResponseMessage> response, Error.Unauthorized error)` | `Task<Result<HttpResponseMessage>>` | Maps `401` to `Fail(error)` (response disposed); pass through otherwise. Throws `ArgumentNullException` when `error` is null. |
| `ReadJsonAsync<T>(this Task<Result<HttpResponseMessage>> response, JsonTypeInfo<T> jsonTypeInfo, CancellationToken ct = default) where T : notnull` | `Task<Result<T>>` | Already-failed input short-circuits with the upstream error. Otherwise reads the body and deserializes; non-success status, `204`, `205`, empty body, null payload, or `JsonException` (caught) all map to `Fail<InternalServerError>`. JSON-parse failures use only `JsonException.LineNumber` / `BytePositionInLine` — never `Message`, never `Path` (which can include user-controlled dictionary keys), never the response body — so user data echoed by the upstream cannot leak into the failure detail. **Always disposes** the response after reading, including on the null-`jsonTypeInfo` path. |
| `ReadJsonMaybeAsync<T>(this Task<Result<HttpResponseMessage>> response, JsonTypeInfo<T> jsonTypeInfo, CancellationToken ct = default) where T : notnull` | `Task<Result<Maybe<T>>>` | Already-failed input short-circuits. Non-success status -> `Fail<InternalServerError>`. `204`, `205`, empty body, JSON `null` -> `Ok(Maybe.None)`. Invalid JSON throws `JsonException` (intentional). **Always disposes** the response, including on the null-`jsonTypeInfo` path. |
| `ReadJsonOrNoneOn404Async<T>(this Task<HttpResponseMessage> response, JsonTypeInfo<T> jsonTypeInfo, CancellationToken ct = default) where T : notnull` | `Task<Result<Maybe<T>>>` | Terminal optional-resource helper. `404` -> `Ok(Maybe.None)`; other non-2xx statuses use strict status mapping; `204`, `205`, empty body, and JSON `null` keep `ReadJsonMaybeAsync` semantics. **Always disposes** the response. |

> **Business API default.** Bare `ToResultAsync()` is now the safe default for domain-facing HTTP clients. Use `HandleNotFoundAsync`, `HandleConflictAsync`, `HandleUnauthorizedAsync`, or an explicit `statusMap` only when the endpoint needs domain-specific error payloads.

## Strict default with header context

When `statusMap` is omitted, the default mapper inspects the `HttpResponseMessage` (not just the status code) and copies relevant HTTP headers into the typed `Error` so downstream rendering (e.g. ASP's `Allow` / `Retry-After` / `WWW-Authenticate` header emission) sees the upstream context.

| HTTP status | Header consulted | Surfaces on |
|---|---|---|
| `401 Unauthorized` | `WWW-Authenticate` (scheme + best-effort `realm` / `error` / etc. parameter parse). **Token68 form** (e.g. `Negotiate <base64-token>`) degrades to scheme-only because `AuthChallenge` has no slot for the bare token; use `ToResultAsync(statusMap)` or the body-aware overload when token68 round-trip fidelity matters. | `Error.Unauthorized.Challenges` |
| `405 Method Not Allowed` | `Allow` (response content header) | `Error.MethodNotAllowed.Allow` |
| `416 Range Not Satisfiable` | `Content-Range: <unit> */<total>` (both unit and length preserved) | `Error.RangeNotSatisfiable.CompleteLength` + `Error.RangeNotSatisfiable.Unit` |
| `429 Too Many Requests` | `Retry-After` (delay seconds **or** HTTP date; negative deltas treated as absent) | `Error.TooManyRequests.RetryAfter` |
| `503 Service Unavailable` | `Retry-After` | `Error.ServiceUnavailable.RetryAfter` |

Headers that aren't present produce empty arrays / null `RetryAfter` / zero `CompleteLength` / default `bytes` unit / empty `Challenges` — the mapper never invents values. Status codes not listed above produce typed errors with their default empty/zero context (e.g. `406 Not Acceptable` and `415 Unsupported Media Type` don't have a single canonical response header to extract).

## Exception propagation

The Trellis.Http extensions deliberately do **not** swallow non-Result-shaped exceptions; they propagate as-is so the caller's existing `try` / `catch` strategy (or the host's middleware) handles them:

- **`HttpRequestException`** — propagates from any extension when the underlying `Task<HttpResponseMessage>` faults (DNS failure, connection refused, TLS handshake error, etc.).
- **`OperationCanceledException` / `TaskCanceledException`** — propagates when the `CancellationToken` is signaled or the upstream `HttpClient` times out.
- **`JsonException`** — caught and mapped to `Fail<InternalServerError>` only by `ReadJsonAsync<T>`. Both `ReadJsonMaybeAsync<T>` and `ReadJsonOrNoneOn404Async<T>` (which delegates to `ReadJsonMaybeAsync<T>` after the 404 check) let it propagate (intentional — see method docs).

Any of these cases that surface inside a `try` block where a response has already been awaited still trigger the disposal contract described below before the exception escapes.

## 3xx redirects under strict default

`HttpClient` follows redirects automatically by default. Callers who set `HttpClientHandler.AllowAutoRedirect = false` (e.g. SSO landing-page detection) must use `ToResultAsync(statusMap)` or the body-aware overload to handle 3xx — the strict default folds them into `Error.InternalServerError` along with all other unmapped statuses, which is intentional fail-fast behavior for the "domain-facing client" use case.

## Disposal contract

The library owns the `HttpResponseMessage` lifecycle on terminal or transformative paths:

- `ToResultAsync` (both overloads) dispose the response on the `Fail` path.
- `HandleNotFoundAsync`, `HandleConflictAsync`, `HandleUnauthorizedAsync` dispose on the matched-status `Fail` path.
- `ReadJsonAsync`, `ReadJsonMaybeAsync`, and `ReadJsonOrNoneOn404Async` **always** dispose after reading, success or failure (including when `JsonException` propagates from the `Maybe` overload).
- Pass-through paths (success from bare `ToResultAsync`, non-matching `Handle*`, mapper returning `null`) leave disposal to the caller.

In practice: once you call `ReadJson*`, you no longer need to dispose the response yourself.

## Examples

### Happy path: GET, map 404, deserialize

```csharp
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Http;

[JsonSerializable(typeof(TodoDto))]
internal partial class AppJsonContext : JsonSerializerContext { }

public sealed record TodoDto(Guid Id, string Title);

public Task<Result<TodoDto>> GetTodoAsync(HttpClient client, Guid id, CancellationToken ct) =>
    client.GetAsync($"/todos/{id}", ct)
        .HandleNotFoundAsync(new Error.NotFound(ResourceRef.For<TodoDto>(id)))
        .ReadJsonAsync(AppJsonContext.Default.TodoDto, ct);
```

### Multi-status mapping with `ToResultAsync(statusMap)`

```csharp
public Task<Result<TodoDto>> GetTodoStrictAsync(HttpClient client, Guid id, CancellationToken ct) =>
    client.GetAsync($"/todos/{id}", ct)
        .ToResultAsync(status => status switch
        {
            HttpStatusCode.NotFound => new Error.NotFound(ResourceRef.For<TodoDto>(id)),
            HttpStatusCode.Forbidden => new Error.Forbidden("todo.read"),
            _ when (int)status >= 500 => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = $"upstream {status}" },
            _ => null,
        })
        .ReadJsonAsync(AppJsonContext.Default.TodoDto, ct);
```

### Body-aware mapping (replaces `HandleFailureAsync<TContext>`)

```csharp
public Task<Result<TodoDto>> GetTodoWithProblemDetailsAsync(HttpClient client, Guid id, CancellationToken ct) =>
    client.GetAsync($"/todos/{id}", ct)
        .ToResultAsync(async (response, token) =>
        {
            // Read RFC 9457 problem-details body to synthesize a richer error.
            var problem = await response.Content
                .ReadFromJsonAsync<ProblemDetails>(cancellationToken: token);
            return problem is null
                ? null
                : new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = problem.Detail ?? "upstream error" };
        }, ct)
        .ReadJsonAsync(AppJsonContext.Default.TodoDto, ct);
```

### Optional resource with `ReadJsonOrNoneOn404Async`

```csharp
public Task<Result<Maybe<TodoDto>>> FindTodoAsync(HttpClient client, Guid id, CancellationToken ct) =>
    client.GetAsync($"/todos/{id}", ct)
        .ReadJsonOrNoneOn404Async(AppJsonContext.Default.TodoDto, ct);
```

## Breaking changes from v1

The v1 surface (60+ overloads across two static classes) has been collapsed into a small canonical method set. There are no shims or compatibility redirects: this is a clean cut, taken pre-GA.

| Previous API | Current replacement |
| --- | --- |
| `HandleNotFound`, `HandleNotFoundAsync` (sync, `Result<HRM>`, `Task<Result<HRM>>` overloads) | `HandleNotFoundAsync(this Task<HttpResponseMessage>, Error.NotFound)` |
| `HandleConflict*` | `HandleConflictAsync(this Task<HttpResponseMessage>, Error.Conflict)` |
| `HandleUnauthorized*` | `HandleUnauthorizedAsync(this Task<HttpResponseMessage>, Error.Unauthorized)` |
| `HandleForbidden*` | **Deleted.** Use `ToResultAsync(status => status == HttpStatusCode.Forbidden ? new Error.Forbidden(...) : null)`. |
| `HandleClientError*` (4xx range), `HandleServerError*` (5xx range) | **Deleted.** Use `ToResultAsync(statusMap)` with a `switch` over `HttpStatusCode`. |
| `EnsureSuccess`, `EnsureSuccessAsync` (all shapes) | **Deleted.** Use `ToResultAsync(status => (int)status >= 400 ? error : null)` or the body-aware `ToResultAsync(mapper, ct)`. |
| `HandleFailureAsync<TContext>` (response-shape and `Result<HRM>`-shape) | **Deleted.** Use the body-aware `ToResultAsync(mapper, ct)`; capture additional state via closure. |
| `ReadResultFromJsonAsync<T>` (sync, `Result<HRM>`, `Task<HRM>`, `Task<Result<HRM>>`) | **Renamed** `ReadJsonAsync<T>(this Task<Result<HttpResponseMessage>>, JsonTypeInfo<T>, CancellationToken)`. |
| `ReadResultMaybeFromJsonAsync<T>` (all shapes) | **Renamed** `ReadJsonMaybeAsync<T>(this Task<Result<HttpResponseMessage>>, JsonTypeInfo<T>, CancellationToken)`. |
| Sync receivers (`HttpResponseMessage`, `Result<HRM>`) | **Deleted.** Wrap with `Task.FromResult(...)` if needed; in practice every `HttpClient` call is already async. |
