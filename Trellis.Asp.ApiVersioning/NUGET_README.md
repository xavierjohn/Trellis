# Trellis.Asp.ApiVersioning

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Asp.ApiVersioning.svg)](https://www.nuget.org/packages/Trellis.Asp.ApiVersioning)

Target-aware API-versioning helpers for URLs emitted by `Trellis.Asp`, including `Location` headers and pagination links.

## Installation

```bash
dotnet add package Trellis.Asp.ApiVersioning
```

## Quick Example

```csharp
using Trellis.Asp;
using Trellis.Asp.ApiVersioning;

return result.ToHttpResponse(options => options
    .CreatedAtRoute("Orders_GetById", order => order.Id)
    .WithVersionedRoute());
```

For paginated responses, use `HttpContext.PageUrl(...)` as the `nextUrlBuilder` or direction-aware `urlBuilder`.

## Key Features

- Resolves the final named-route or MVC-action destination and validates its supported versions.
- Emits mapped query-string versions and supports URL-segment destinations.
- Composes with `CreatedAtRoute`, `CreatedAtAction`, and `WithLocation`.
- Builds version-aware next and previous pagination URLs.
- Supports explicit version pins for intentional cross-version links.
- Skips version injection for neutral and unversioned destinations.
- Requires no additional service registration beyond normal `Asp.Versioning` setup.

Location targets must be uniquely addressable. `PageUrl` supports shared names across versioned controllers with matching route templates/defaults: self-pagination uses the active endpoint; cross-route links and pins select a uniquely mapped version. Missing or genuinely ambiguous destinations fail explicitly. Location helpers support URL-segment pins; explicit `PageUrl` pins intentionally do not.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-asp-apiversioning.html)
- [ASP.NET Core integration guide](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [Trellis.Asp package](https://www.nuget.org/packages/Trellis.Asp)
