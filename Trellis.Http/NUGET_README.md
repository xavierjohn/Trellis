# Trellis.Http

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Http.svg)](https://www.nuget.org/packages/Trellis.Http)

`HttpClient` extensions that bridge `HttpResponseMessage` into `Result<T>` / `Result<Maybe<T>>` pipelines.

## Installation

```bash
dotnet add package Trellis.Http
```

## What we provide (v3 surface)

A single static class `Trellis.Http.HttpResponseExtensions` with the canonical HTTP result methods:

- `ToResultAsync(statusMap?)` &mdash; bridge `Task<HttpResponseMessage>` into `Task<Result<HttpResponseMessage>>`; without a map, non-2xx statuses become typed failures.
- `ToResultAsync(mapper, ct)` &mdash; body-aware bridge invoked only for non-success status codes.
- `HandleNotFoundAsync` / `HandleConflictAsync` / `HandleUnauthorizedAsync` &mdash; single-status convenience entry points on `Task<HttpResponseMessage>`.
- `ReadJsonAsync<T>` / `ReadJsonMaybeAsync<T>` &mdash; deserialize the body of `Task<Result<HttpResponseMessage>>` into `T` or `Maybe<T>`.
- `ReadJsonOrNoneOn404Async<T>` &mdash; terminal optional-resource read where `404` maps to `Ok(Maybe.None)`.

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

The library owns `HttpResponseMessage` disposal on terminal/transformative paths: `ToResultAsync` and `Handle*Async` dispose on the `Fail` path; `ReadJson*` always dispose after reading. Pass-through paths leave disposal to the caller. Programmer-error null-argument paths (e.g. `client.GetAsync(...).HandleNotFoundAsync(null!)`) await first, then dispose before throwing `ArgumentNullException`.

## Strict-default behavior

`ToResultAsync()` without a `statusMap` produces typed errors that preserve key upstream response-header context — `WWW-Authenticate` schemes + best-effort auth-param parse on `401` (token68 or unparseable parameter strings degrade to scheme-only), `Allow` on `405`, `Content-Range` unit + length on `416`, `Retry-After` on `429` / `503` — so downstream `Trellis.Asp` rendering can faithfully forward the original wire shape. Missing or unusable header values for `405` and `416` fall through to `Error.InternalServerError` rather than fabricating misleading wire headers. 3xx responses fall through; redirect-aware callers should pass a `statusMap`.

## Exception propagation

`HttpRequestException`, `OperationCanceledException` / `TaskCanceledException`, and `JsonException` (from `ReadJsonMaybeAsync<T>` / `ReadJsonOrNoneOn404Async<T>` on a 2xx invalid body) propagate through the chain rather than being mapped to `Result.Fail`. `ReadJsonAsync<T>` catches `JsonException` and returns `Fail<Error.InternalServerError>` with structured position diagnostics only (no response body, no `JsonException.Path`).

## Breaking changes from v1

`Trellis.Http` has collapsed from 60+ overloads to a small canonical method set. Removed verbs: `HandleForbidden*`, `HandleClientError*`, `HandleServerError*`, `EnsureSuccess`/`EnsureSuccessAsync`, `HandleFailureAsync<TContext>`, and all sync / `Result<HRM>` / `HttpResponseMessage`-receiver overloads. Renamed verbs: `ReadResultFromJsonAsync` -> `ReadJsonAsync`, `ReadResultMaybeFromJsonAsync` -> `ReadJsonMaybeAsync`. See the package README on GitHub for the full migration table.

## Part of Trellis

This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
