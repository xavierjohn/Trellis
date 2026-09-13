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

For paginated responses, pass a route-aware URL builder:

```csharp
return pageResult.ToHttpResponse(
    nextUrlBuilder: HttpContext.PageUrl(
        "Orders_List",
        (cursor, limit) => new RouteValueDictionary
        {
            ["cursor"] = cursor.Token,
            ["limit"] = limit,
        }),
    body: order => OrderResponse.From(order));
```

## Key Features

- Resolves the actual named-route or MVC-action destination instead of copying metadata from the current endpoint.
- Adds a mapped query-string API version or fills the destination's `:apiVersion` segment.
- Supports `CreatedAtRoute`, `CreatedAtAction`, and `WithLocation`.
- Builds version-aware next and previous pagination URLs through `HttpContext.PageUrl(...)`.
- Supports explicit version pins when cross-version links are intentional.
- Requires no additional service registration beyond normal `Asp.Versioning` setup.

## Behavior to Know

- Missing or ambiguous destinations fail explicitly; give link targets unique route names.
- An explicit pin must map to the destination. Automatic resolution prefers a mapped requested version, then one mapped declared version, then a mapped default version.
- Neutral and unversioned destinations skip injection. `WithVersionedRoute` also removes a supplied `api-version` value in those cases.
- Location helpers support URL-segment pins. Explicit `PageUrl` pins intentionally do not.
- `TRLS023` warns when a versioned controller creates a Location without version handling.

The complete resolution order, route-value precedence, migration notes, and diagnostics belong in the package API reference rather than this landing page.

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-asp-apiversioning.md)
- [ASP.NET Core integration guide](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [Trellis.Asp package](../Trellis.Asp/README.md)

## Development

Run the package tests from the repository root:

```powershell
dotnet test Trellis.Asp.ApiVersioning\tests\Trellis.Asp.ApiVersioning.Tests.csproj -c Release
```
