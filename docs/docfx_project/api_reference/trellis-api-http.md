---
package: Trellis.Http
namespaces: [Trellis.Http]
types: [HttpResponseMessageExtensions, HttpClientResultExtensions]
version: v3
last_verified: 2026-05-01
audience: [llm, developer]
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
| `ToResultAsync(this Task<HttpResponseMessage> response, Func<HttpStatusCode, Error?>? statusMap = null)` | `Task<Result<HttpResponseMessage>>` | When `statusMap` is `null`, 2xx statuses pass through as `Ok(response)` and non-2xx statuses map to typed Trellis errors. When supplied, a `null` return passes through; a non-null `Error` becomes `Fail` and the underlying response is disposed. |
| `ToResultAsync(this Task<HttpResponseMessage> response, Func<HttpResponseMessage, CancellationToken, Task<Error?>> mapper, CancellationToken ct = default)` | `Task<Result<HttpResponseMessage>>` | Body-aware bridge. The mapper is invoked **only** when `IsSuccessStatusCode == false`. `null` return -> `Ok(response)`; non-null -> `Fail` (response disposed). |
| `HandleNotFoundAsync(this Task<HttpResponseMessage> response, Error.NotFound error)` | `Task<Result<HttpResponseMessage>>` | Maps `404` to `Fail(error)` (response disposed); any other status passes through as `Ok(response)`. |
| `HandleConflictAsync(this Task<HttpResponseMessage> response, Error.Conflict error)` | `Task<Result<HttpResponseMessage>>` | Maps `409` to `Fail(error)` (response disposed); pass through otherwise. |
| `HandleUnauthorizedAsync(this Task<HttpResponseMessage> response, Error.Unauthorized error)` | `Task<Result<HttpResponseMessage>>` | Maps `401` to `Fail(error)` (response disposed); pass through otherwise. |
| `ReadJsonAsync<T>(this Task<Result<HttpResponseMessage>> response, JsonTypeInfo<T> jsonTypeInfo, CancellationToken ct = default) where T : notnull` | `Task<Result<T>>` | Already-failed input short-circuits with the upstream error. Otherwise reads the body and deserializes; non-success status, `204`, `205`, empty body, null payload, or `JsonException` (caught) all map to `Fail<InternalServerError>`. **Always disposes** the response after reading. |
| `ReadJsonMaybeAsync<T>(this Task<Result<HttpResponseMessage>> response, JsonTypeInfo<T> jsonTypeInfo, CancellationToken ct = default) where T : notnull` | `Task<Result<Maybe<T>>>` | Already-failed input short-circuits. Non-success status -> `Fail<InternalServerError>`. `204`, `205`, empty body, JSON `null` -> `Ok(Maybe.None)`. Invalid JSON throws `JsonException` (intentional). **Always disposes** the response. |
| `ReadJsonOrNoneOn404Async<T>(this Task<HttpResponseMessage> response, JsonTypeInfo<T> jsonTypeInfo, CancellationToken ct = default) where T : notnull` | `Task<Result<Maybe<T>>>` | Terminal optional-resource helper. `404` -> `Ok(Maybe.None)`; other non-2xx statuses use strict status mapping; `204`, `205`, empty body, and JSON `null` keep `ReadJsonMaybeAsync` semantics. **Always disposes** the response. |

> **Business API default.** Bare `ToResultAsync()` is now the safe default for domain-facing HTTP clients. Use `HandleNotFoundAsync`, `HandleConflictAsync`, `HandleUnauthorizedAsync`, or an explicit `statusMap` only when the endpoint needs domain-specific error payloads.

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
