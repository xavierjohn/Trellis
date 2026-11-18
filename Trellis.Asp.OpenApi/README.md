# Trellis.Asp.OpenApi

Optional native ASP.NET Core OpenAPI documentation for Trellis cursor pagination.
The adapter consumes `[Pagination]` / `.WithPagination()` metadata without binding inputs
or adding OpenAPI dependencies to `Trellis.Asp` or `Trellis.ServiceDefaults`.

## Installation

```bash
dotnet add package Microsoft.AspNetCore.OpenApi --version 11.0.0
dotnet add package Trellis.Asp.OpenApi
```

Keep the native SDK as a direct host reference: its XML-comment source generator needs
the SDK's build targets, which do not flow through a transitive package reference.

This adapter references `Microsoft.AspNetCore.OpenApi` 11.x and its `Microsoft.OpenApi` 3.x
model. Install it only in hosts using compatible native OpenAPI tooling. Hosts using older
Swashbuckle, NSwag, or `Asp.Versioning.OpenApi` integrations pinned to model 2.x should keep
the base `Trellis.Asp` package and their generator-specific declarations instead.

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

The example returns the parsed request; a real list endpoint passes it to its application
query. The marker alone does not bind or parse inputs. Register the transformer once for each
participating document; neither `AddTrellisAsp()` nor `UseAsp()` enables it automatically.

## Key Features

- Shares pagination metadata across Minimal API endpoints, route groups, MVC actions and controllers.
- Documents optional cursor/limit parameters, custom names, defaults, caps and clamp/reject policy.
- Preserves existing query parameters and more-specific endpoint declarations.
- Matches the parser's non-whitespace cursor rule and positive `int32` limit range.
- Supports OpenAPI 3.0, 3.1 and .NET 11's default 3.2.
- Leaves request parsing, binding, ApiExplorer parameters and input-origin inference unchanged.
- Keeps native SDK dependencies isolated from the base ASP and ServiceDefaults packages.

When the configured default exceeds the cap, the schema default is the applied size.
Sending it explicitly preserves `Applied`, but changes `Requested` and `WasCapped` compared
with omission; see the schema behavior reference.

## Documentation

- [Package API reference](../docs/docfx_project/api_reference/trellis-api-asp-openapi.md)
- [Pagination recipe](../docs/docfx_project/api_reference/recipes/recipe-03-paginated-query.md)
- [Base ASP integration](../Trellis.Asp/README.md)

## Development

Run the adapter tests from the repository root:

```powershell
dotnet test --project Trellis.Asp.OpenApi\tests\Trellis.Asp.OpenApi.Tests.csproj -c Release
```
