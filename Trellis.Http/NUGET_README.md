# Trellis.Http

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Http.svg)](https://www.nuget.org/packages/Trellis.Http)

`HttpClient` extensions that preserve typed success and failure flows across network boundaries.

## Installation
```bash
dotnet add package Trellis.Http
```

## Quick Example
```csharp
using System.Text.Json.Serialization;
using Trellis.Http;

public sealed record ProfileDto(string DisplayName);

[JsonSerializable(typeof(ProfileDto))]
public partial class ProfileJsonContext : JsonSerializerContext { }

var result = await httpClient.GetAsync("/profile", cancellationToken)
    .EnsureSuccessAsync()
    .ReadResultMaybeFromJsonAsync(ProfileJsonContext.Default.ProfileDto, cancellationToken);
```

## Key Features
- Convert `HttpResponseMessage` outcomes into `Result<T>` and `Result<Maybe<T>>`.
- Keep HTTP status handling and JSON parsing inside the Trellis pipeline.
- Make remote calls easier to compose with the same patterns you use locally.

## Documentation
- [Full documentation](https://xavierjohn.github.io/Trellis/articles/integration-http.html)
- [API Reference](https://xavierjohn.github.io/Trellis/api/index.html)

## Part of Trellis
This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
