# Trellis.Asp.OpenApi

Optional native ASP.NET Core OpenAPI documentation for Trellis cursor pagination.
It consumes `[Pagination]` / `.WithPagination()` metadata without binding inputs or
adding native SDK dependencies to the base ASP and ServiceDefaults packages.

## Installation

```bash
dotnet add package Microsoft.AspNetCore.OpenApi --version 11.0.0
dotnet add package Trellis.Asp.OpenApi
```

Keep the native SDK as a direct host reference: its XML-comment source generator needs
the SDK's build targets, which do not flow through a transitive package reference.

The adapter uses `Microsoft.AspNetCore.OpenApi` 11.x and `Microsoft.OpenApi` 3.x.
Older Swashbuckle, NSwag, or `Asp.Versioning.OpenApi` integrations requiring model 2.x can
conflict. Those hosts can keep `Trellis.Asp` without this adapter and declare parameters
through their existing generator.

## Quick Example

```csharp
using Trellis.Asp;
using Trellis.Asp.OpenApi;

builder.Services.AddTrellisAsp();
builder.Services.AddOpenApi("v1", options =>
    options.AddOperationTransformer<TrellisPaginationOperationTransformer>());

app.MapGet("/widgets", (HttpRequest request) =>
    request.TryCreatePageRequest().ToHttpResponse())
    .WithPagination();

app.MapOpenApi();
```

The example returns the parsed request; a list endpoint passes it to an application query.
Register the transformer once per participating document. `AddTrellisAsp()` / `UseAsp()`
alone do not enable documentation, and the marker never enables parsing or binding.

## Key Features

- Shared metadata for Minimal APIs, route groups, MVC actions and controllers.
- Optional cursor/limit parameters with names, defaults, caps and clamp/reject behavior.
- Existing query parameters and endpoint overrides are preserved.
- Cursor non-whitespace validation and positive `int32` limit schemas.
- OpenAPI 3.0, 3.1 and .NET 11's default 3.2 support.
- No request-binding, ApiExplorer or input-origin side effects.

The schema default is published only when sending it explicitly equals omission: always under
`Clamp`, and under `Reject` when the default does not exceed the cap.

## Documentation

- [Package API reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-asp-openapi.html)
- [Pagination recipe](https://xavierjohn.github.io/Trellis/api_reference/recipes/recipe-03-paginated-query.html)
- [Base ASP integration](https://www.nuget.org/packages/Trellis.Asp)
