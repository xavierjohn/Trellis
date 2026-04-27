# Trellis.Http

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Http.svg)](https://www.nuget.org/packages/Trellis.Http)

`HttpClient` extensions that bridge `HttpResponseMessage` into `Result<T>` / `Result<Maybe<T>>` pipelines.

## Installation

```bash
dotnet add package Trellis.Http
```

## What we provide (v3 surface)

A single static class `Trellis.Http.HttpResponseExtensions` with the canonical HTTP result methods:

| Method | Purpose |
| --- | --- |
| `ToResultAsync(this Task<HttpResponseMessage>, Func<HttpStatusCode, Error?>? statusMap = null)` | Bridge `Task<HttpResponseMessage>` into `Task<Result<HttpResponseMessage>>`. Without a map, 2xx statuses pass through as `Ok` and non-2xx statuses become typed failures. With a map, a non-null return becomes `Fail`. |
| `ToResultAsync(this Task<HttpResponseMessage>, Func<HttpResponseMessage, CancellationToken, Task<Error?>>, CancellationToken = default)` | Body-aware bridge. The async mapper is invoked only on non-success status codes. |
| `HandleNotFoundAsync(this Task<HttpResponseMessage>, Error.NotFound)` | Map 404 to `Fail`; pass through otherwise. |
| `HandleConflictAsync(this Task<HttpResponseMessage>, Error.Conflict)` | Map 409 to `Fail`; pass through otherwise. |
| `HandleUnauthorizedAsync(this Task<HttpResponseMessage>, Error.Unauthorized)` | Map 401 to `Fail`; pass through otherwise. |
| `ReadJsonAsync<T>(this Task<Result<HttpResponseMessage>>, JsonTypeInfo<T>, CancellationToken = default)` | Read and deserialize the body into `T`. Invalid JSON becomes `Fail<InternalServerError>`. |
| `ReadJsonMaybeAsync<T>(this Task<Result<HttpResponseMessage>>, JsonTypeInfo<T>, CancellationToken = default)` | Read into `Maybe<T>`. `204`, `205`, empty body, JSON `null` map to `Maybe.None`. Invalid JSON throws `JsonException` (intentional). |
| `ReadJsonOrNoneOn404Async<T>(this Task<HttpResponseMessage>, JsonTypeInfo<T>, CancellationToken = default)` | Terminal optional-resource helper: `404` maps to `Ok(Maybe.None)`; other non-2xx statuses use strict status mapping. |

## Quick example

```csharp
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Http;

public sealed record ProfileDto(string DisplayName);

[JsonSerializable(typeof(ProfileDto))]
public partial class ProfileJsonContext : JsonSerializerContext { }

var userId = "current-user";
var result = await httpClient.GetAsync("/profile", cancellationToken)
    .HandleNotFoundAsync(new Error.NotFound(ResourceRef.For("Profile", userId)))
    .ReadJsonAsync(ProfileJsonContext.Default.ProfileDto, cancellationToken);
```

## Disposal contract

The library owns the `HttpResponseMessage` lifecycle on terminal or transformative paths:

- `ToResultAsync` and `Handle*Async` dispose the response on the `Fail` path.
- `ReadJsonAsync`, `ReadJsonMaybeAsync`, and `ReadJsonOrNoneOn404Async` always dispose after reading, success or failure (including when `JsonException` propagates from the `Maybe` overload).
- Pass-through paths (success from bare `ToResultAsync`, non-matching `Handle*Async`) leave the response with the caller until a downstream `ReadJson*` consumes it.

In practice: once you call `ReadJson*`, you no longer need to dispose the response yourself.

## Breaking changes from v1

The v1 surface (60+ overloads) has been collapsed into a small canonical method set. Replacements:

| v1 API | v2 replacement |
| --- | --- |
| `HandleNotFound`, `HandleNotFoundAsync` (sync, `Result<HRM>`, `Task<Result<HRM>>` overloads) | `HandleNotFoundAsync(this Task<HttpResponseMessage>, ...)` |
| `HandleConflict*`, `HandleUnauthorized*` | `HandleConflictAsync` / `HandleUnauthorizedAsync` (single shape) |
| `HandleForbidden*` | **Deleted** &mdash; use `ToResultAsync(status => status == HttpStatusCode.Forbidden ? new Error.Forbidden(...) : null)` |
| `HandleClientError*`, `HandleServerError*` | **Deleted** &mdash; use `ToResultAsync(statusMap)` to map any status range. |
| `EnsureSuccess`, `EnsureSuccessAsync` (all shapes) | **Deleted** &mdash; use `ToResultAsync(statusMap)` or the body-aware `ToResultAsync(mapper, ct)`. |
| `HandleFailureAsync<TContext>` (response-shape and Result-shape) | **Deleted** &mdash; use the body-aware `ToResultAsync(mapper, ct)`; capture context via closure. |
| `ReadResultFromJsonAsync` (sync, `Result<HRM>`, `Task<HRM>`, `Task<Result<HRM>>`) | **Renamed** to `ReadJsonAsync(this Task<Result<HttpResponseMessage>>, ...)`. |
| `ReadResultMaybeFromJsonAsync` (all shapes) | **Renamed** to `ReadJsonMaybeAsync(this Task<Result<HttpResponseMessage>>, ...)`. |

There are no shims or compatibility redirects: v2 is a clean cut. To call the new API on a synchronous response, wrap it: `Task.FromResult(response).ToResultAsync()`.

## Documentation

- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-http.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis

This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
