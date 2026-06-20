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

## What it provides

| Type | Purpose |
| --- | --- |
| `HttpError` (closed union) | Transport faults for `405`, `406`, `412`, `413`, `415`, `416`, `428`. Each case carries the payload that drives the synthesized response header (e.g., `Allow` for `MethodNotAllowed`, `Content-Range` for `RangeNotSatisfiable`). Implements `ITransportFault`. |
| `PreconditionKind` | Discriminates conditional-request preconditions (`IfMatch`, `IfNoneMatch`, `IfModifiedSince`, `IfUnmodifiedSince`). Used by `HttpError.PreconditionFailed` and `HttpError.PreconditionRequired`. |
| `EntityTagValue` | Strongly typed `ETag` value with strong/weak distinction and wildcard support. |
| `AggregateETagExtensions` | `OptionalETag` / `RequireETag` pipeline operators that lift `HttpError.PreconditionFailed` / `PreconditionRequired` into `Error.TransportFault` for `Result<T>` of an `IAggregate`. |
| `AuthChallenge` | Boundary representation of a `WWW-Authenticate` challenge. |
| `RetryAfterValue` | HTTP wire representation of `Retry-After` (delay-seconds or HTTP-date). Use `RetryAdvice` in the domain; the boundary translates one to the other. |
| `RepresentationMetadata`, `WriteOutcome<T>` | Conditional-request and response-shaping helpers shared by server/client packages. The static `WriteOutcome` factory (`Updated<T>`, `Created<T>`, `UpdatedNoContent<T>`, `Accepted<T>`, `AcceptedNoContent<T>`) builds each case but returns the base `WriteOutcome<T>`, so results flow through `Result.Map(...)` / `ToHttpResponse(...)` without a `(WriteOutcome<T>)` cast — e.g. `WriteOutcome.Updated(value, metadata)`. |

## Quick example

```csharp
using Trellis;

Error error = new Error.TransportFault(
    new HttpError.MethodNotAllowed(EquatableArray.Create("GET", "PUT"))
    {
        Detail = "The target resource does not support PATCH.",
    });
```

The server boundary (`Trellis.Asp.ResponseFailureWriter`) unwraps `Error.TransportFault` for status-code resolution and header synthesis. The client (`Trellis.Http.HttpResponseExtensions`) constructs `HttpError.*` cases from inbound responses (preserving `Allow`, `Content-Range`, etc.) and rewraps them in `Error.TransportFault` so they round-trip through the `Result<T>` pipeline without leaving the domain shape.

## Documentation

- [API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-http-abstractions.html)
- [Error handling](https://xavierjohn.github.io/Trellis/articles/error-handling.html)

## Part of Trellis

This package is part of the [Trellis](https://github.com/xavierjohn/Trellis) framework.
