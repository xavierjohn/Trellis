# Trellis.Http.Abstractions

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Http.Abstractions.svg)](https://www.nuget.org/packages/Trellis.Http.Abstractions)

HTTP-aware error and representation primitives shared by `Trellis.Asp` on the server side and `Trellis.Http` on the client side.

`Trellis.Core` keeps its domain error union transport-neutral. HTTP-specific failures live in the closed `HttpError` union and flow through `Result<T>` via `Error.TransportFault(ITransportFault)`. This package also owns the HTTP vocabulary that used to live in Core: entity tags, precondition kinds, authentication challenges, retry-after values, representation metadata, and write outcomes.

Use this package when boundary code needs to construct, inspect, or round-trip HTTP failure or context payloads without reintroducing HTTP/RFC concepts into the domain core.

## Installation

```bash
dotnet add package Trellis.Http.Abstractions
```

`Trellis.Asp` and `Trellis.Http` reference this package transitively; add an explicit reference only when boundary glue code needs to construct or pattern-match the types directly.

## Quick Example

```csharp
using Trellis;

Error error = new Error.TransportFault(
    new HttpError.MethodNotAllowed(EquatableArray.Create("GET", "PUT"))
    {
        Detail = "The target resource does not support PATCH.",
    });
```

The server boundary (`Trellis.Asp.ResponseFailureWriter`) unwraps `Error.TransportFault` for status-code resolution and header synthesis. The client (`Trellis.Http.HttpResponseExtensions`) constructs `HttpError.*` cases from inbound responses (preserving `Allow`, `Content-Range`, etc.) and rewraps them in `Error.TransportFault` so they round-trip through the `Result<T>` pipeline without leaving the domain shape.

## Key Features

| Type | Purpose |
| --- | --- |
| `HttpError` | Closed transport-fault union for HTTP-specific failures such as 405, 412, 416, and 428 |
| `EntityTagValue`, `PreconditionKind` | Typed conditional-request and ETag vocabulary |
| `AuthChallenge`, `RetryAfterValue` | Reusable authentication and retry header values |
| `RepresentationMetadata`, `WriteOutcome<T>` | Response metadata and write-result shapes shared by server and client packages |
| `AggregateETagExtensions` | `OptionalETag` and `RequireETag` operators for aggregate result pipelines |

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-http-abstractions.md)
- [Error handling](https://xavierjohn.github.io/Trellis/articles/error-handling.html)

## Part of Trellis

This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.Http.Abstractions\tests\Trellis.Http.Abstractions.Tests.csproj -c Release
```
