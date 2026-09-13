# Trellis.Http

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Http.svg)](https://www.nuget.org/packages/Trellis.Http)

`HttpClient` extensions that bridge `HttpResponseMessage` into `Result<T>` / `Result<Maybe<T>>` pipelines.

## Installation

```bash
dotnet add package Trellis.Http
```

## Quick Example

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

## Key Features

- `ToResultAsync` maps HTTP status codes into typed Trellis results.
- `HandleNotFoundAsync`, `HandleConflictAsync`, and `HandleUnauthorizedAsync` cover common expected failures.
- `ReadJsonAsync<T>` and `ReadJsonMaybeAsync<T>` deserialize required and optional bodies.
- `ReadJsonOrNoneOn404Async<T>` maps a missing upstream resource to `Maybe.None`.
- Terminal `ReadJson*` operations own response disposal; pass-through operations leave it with the caller.

## Disposal contract

The library owns `HttpResponseMessage` disposal on terminal/transformative paths: `ToResultAsync` and `Handle*Async` dispose on the `Fail` path; `ReadJson*` always dispose after reading. Pass-through paths leave disposal to the caller. Programmer-error null-argument paths (e.g. `client.GetAsync(...).HandleNotFoundAsync(null!)`) await first, then dispose before throwing `ArgumentNullException`.

## Strict-default behavior

`ToResultAsync()` without a `statusMap` produces typed errors with HTTP-specific cases wrapped in `Error.TransportFault(new HttpError.*(...))`. The strict default preserves `Allow` on `405` and `Content-Range` on `416`. `401` does not carry parsed `WWW-Authenticate` challenges, and `429` / `503` do not preserve `Retry-After` into the error payload. Missing or unusable header values for `405` and `416` fall through to `Error.Unexpected` rather than fabricating misleading wire headers. 3xx responses fall through; redirect-aware callers should pass a `statusMap`.

## Exception propagation

`HttpRequestException` and `OperationCanceledException` / `TaskCanceledException` propagate through the chain rather than being mapped to `Result.Fail`. `JsonException` does **not**: `ReadJsonAsync<T>`, `ReadJsonMaybeAsync<T>`, and `ReadJsonOrNoneOn404Async<T>` all catch it and return `Fail<Error.Unexpected>` (`Code = FaultCodes.HttpResponseInvalidBody`) with structured position diagnostics only (no response body, no `JsonException.Path`).

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-http.html)
- [HTTP integration guide](https://xavierjohn.github.io/Trellis/articles/integration-http.html)

## Part of Trellis

This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
