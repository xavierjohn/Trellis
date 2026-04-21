# Trellis.Http

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Http.svg)](https://www.nuget.org/packages/Trellis.Http)

`HttpClient` extensions that bridge `HttpResponseMessage` into `Result<T>` / `Result<Maybe<T>>` pipelines.

## Installation

```bash
dotnet add package Trellis.Http
```

## What we provide (v2 surface)

A single static class `Trellis.Http.HttpResponseExtensions` with seven methods:

- `ToResultAsync(statusMap?)` &mdash; bridge `Task<HttpResponseMessage>` into `Task<Result<HttpResponseMessage>>`.
- `ToResultAsync(mapper, ct)` &mdash; body-aware bridge invoked only for non-success status codes.
- `HandleNotFoundAsync` / `HandleConflictAsync` / `HandleUnauthorizedAsync` &mdash; single-status convenience entry points on `Task<HttpResponseMessage>`.
- `ReadJsonAsync<T>` / `ReadJsonMaybeAsync<T>` &mdash; deserialize the body of `Task<Result<HttpResponseMessage>>` into `T` or `Maybe<T>`.

## Quick example

```csharp
using System.Text.Json.Serialization;
using Trellis;
using Trellis.Http;

public sealed record ProfileDto(string DisplayName);

[JsonSerializable(typeof(ProfileDto))]
public partial class ProfileJsonContext : JsonSerializerContext { }

var result = await httpClient.GetAsync("/profile", cancellationToken)
    .HandleNotFoundAsync(new Error.NotFound(new ResourceRef("Profile", userId)))
    .ReadJsonAsync(ProfileJsonContext.Default.ProfileDto, cancellationToken);
```

## Disposal contract

The library owns `HttpResponseMessage` disposal on terminal/transformative paths: `ToResultAsync` and `Handle*Async` dispose on the `Fail` path; `ReadJson*` always dispose after reading. Pass-through paths leave disposal to the caller.

## Breaking changes from v1

`Trellis.Http` has collapsed from 60+ overloads to 7 methods. Removed verbs: `HandleForbidden*`, `HandleClientError*`, `HandleServerError*`, `EnsureSuccess`/`EnsureSuccessAsync`, `HandleFailureAsync<TContext>`, and all sync / `Result<HRM>` / `HttpResponseMessage`-receiver overloads. Renamed verbs: `ReadResultFromJsonAsync` -> `ReadJsonAsync`, `ReadResultMaybeFromJsonAsync` -> `ReadJsonMaybeAsync`. See the package README on GitHub for the full migration table.

## Part of Trellis

This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
