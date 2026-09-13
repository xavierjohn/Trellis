# Trellis.Http.Abstractions

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Http.Abstractions.svg)](https://www.nuget.org/packages/Trellis.Http.Abstractions)

HTTP-aware abstractions for Trellis boundary code.

## Installation

```bash
dotnet add package Trellis.Http.Abstractions
```

`Trellis.Asp` and `Trellis.Http` reference this package transitively; add it directly only when boundary code needs to construct or inspect these types.

## Quick Example

```csharp
using Trellis;

var error = new Error.TransportFault(
    new HttpError.PreconditionRequired(PreconditionKind.IfMatch)
    {
        Detail = "This operation requires an If-Match header.",
    });
```

## Key Features

- `HttpError` — closed union of HTTP transport failures (`405`, `406`, `412`, `413`, `415`, `416`, `428`).
- `AuthChallenge`, `EntityTagValue`, `PreconditionKind`, `RetryAfterValue` — reusable HTTP payload/value types.
- `AggregateETagExtensions`, `RepresentationMetadata`, `WriteOutcome<T>` — conditional-request and response-shaping helpers shared by server/client packages. The static `WriteOutcome` factory (`WriteOutcome.Updated(value, metadata)`, `Created(...)`, …) returns the base `WriteOutcome<T>`, so write results bind the `Result<WriteOutcome<T>>` response overloads without a cast.
- `Error.TransportFault(ITransportFault)` integration via `HttpError : ITransportFault`.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-http-abstractions.html)
- [Error handling](https://xavierjohn.github.io/Trellis/articles/error-handling.html)
