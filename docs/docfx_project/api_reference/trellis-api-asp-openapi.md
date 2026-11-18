---
package: Trellis.Asp.OpenApi
namespaces: [Trellis.Asp.OpenApi]
types: [TrellisPaginationOperationTransformer]
related_docs: [trellis-api-asp.md, trellis-api-core.md, trellis-start-here.md]
version: v3
last_verified: 2026-11-17
audience: [llm]
agent_usage: onDemand
agent_description: "Open when documenting Trellis cursor pagination with native ASP.NET Core OpenAPI: optional adapter setup, per-document transformers, schemas and dependency compatibility."
---
# Trellis.Asp.OpenApi API Reference

**Package:** `Trellis.Asp.OpenApi`

**Namespace:** `Trellis.Asp.OpenApi`

**Purpose:** Optional native ASP.NET Core OpenAPI adapter for the generator-neutral
pagination metadata in `Trellis.Asp`.

## Use this file when

- You want native OpenAPI documents to describe cursor/limit query parameters without binding inputs.
- You need per-document opt-in, schema constraints, or the native SDK compatibility boundary.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Enable pagination documentation for a native OpenAPI document | Register `TrellisPaginationOperationTransformer` inside that document's `AddOpenApi` callback | [Setup](#setup) |
| Declare pagination names, sizes and policy | `[Pagination]` or `.WithPagination()` from `Trellis.Asp` | [ASP metadata](trellis-api-asp.md#paginationattribute) |
| Match cursor and page-size schemas to runtime validation | Cursor non-whitespace pattern; positive `int32` limit with clamp/reject semantics | [Schema behavior](#schema-behavior) |
| Keep native OpenAPI dependencies out of the base integration | Reference this adapter only in hosts using the native SDK | [Dependencies and compatibility](#dependencies-and-compatibility) |

## Dependencies and compatibility

This package references `Trellis.Asp` and `Microsoft.AspNetCore.OpenApi` 11.x, which brings
`Microsoft.OpenApi` 3.x. Neither `Trellis.Asp` nor `Trellis.ServiceDefaults` references this
adapter or those OpenAPI packages. Pagination metadata and request parsing work without it.

Older Swashbuckle, NSwag, or `Asp.Versioning.OpenApi` integrations requiring
`Microsoft.OpenApi` 2.x can conflict with the native SDK's model. Do not install this adapter
in those hosts unless their resolved dependencies are compatible; use generator-specific
parameter declarations instead. See also the
[native versioning compatibility note](trellis-api-asp-apiversioning.md#native-versioned-openapi-on-net-11).

## Setup

The host must directly reference `Microsoft.AspNetCore.OpenApi` 11.x as well as this adapter:

```bash
dotnet add package Microsoft.AspNetCore.OpenApi --version 11.0.0
dotnet add package Trellis.Asp.OpenApi
```

The native SDK's XML-comment source generator needs its build targets, which are not imported
through transitive references. Relying only on the adapter's dependency can produce compiler
error CS9137 when the generator intercepts `AddOpenApi`; the direct SDK reference imports
the required targets. No compiler-property workaround or Trellis build target is needed.

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

Register the transformer once for **each** participating named document. A document without
that registration remains unchanged, even if another document enables the transformer.
`AddTrellisAsp()` / `UseAsp()` do not enable it. No additional Trellis service-registration
helper or ServiceDefaults builder slot is needed.

The endpoint above demonstrates parsing and documentation; a list endpoint binds the
validated request before executing its query. See [Recipe 3](recipes/recipe-03-paginated-query.md).
MVC actions/controllers use `[Pagination]` with the same transformer registration.
The host still owns `MapOpenApi`, document grouping and specification version selection.
The adapter works with OpenAPI 3.0, 3.1 and .NET 11's default 3.2.

## Types

### `TrellisPaginationOperationTransformer`

```csharp
public sealed class TrellisPaginationOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken);
}
```

`TransformAsync` reads the last `PaginationAttribute` in the action's endpoint metadata.
Action/endpoint declarations therefore override controller/route-group declarations.
Unmarked operations, or descriptions without an action descriptor or endpoint metadata,
are left unchanged. Existing query parameters are preserved case-insensitively rather than
duplicated or overwritten. The transformation completes synchronously.

This is documentation only: it does not bind or parse requests, configure the parser,
add ApiExplorer parameter descriptions, or change Trellis input-origin inference.
Keep marker settings aligned with the explicit
[`TryCreatePageRequest`](trellis-api-asp.md#httprequestpaginationextensions) call and keep
downstream query input-origin declarations separate.

## Schema behavior

Both query parameters are optional and described as occurring at most once.

The cursor schema requires at least one non-whitespace character, matching `Cursor.TryCreate`.
Its pattern lists .NET whitespace characters explicitly because ECMAScript `\s` differs
(for example, U+0085 is whitespace to .NET, while U+FEFF is not).

The limit schema is a positive `int32`: under `Reject`, its maximum is `Max`; under `Clamp`,
its maximum is `Int32.MaxValue`, not the applied cap. The raw parser enforces that accepted-input
bound through `Int32` parsing; `Max` limits `PageSize.Applied`, not `PageSize.Requested`.
The description explains the configured cap and policy. Its schema default is the applied
missing-limit size, `min(DefaultSize, Max)`; the description also preserves the requested
`DefaultSize`, including when the missing default is capped under `Reject`.

When `DefaultSize > Max`, a client that sends the schema default explicitly changes
`PageSize.Requested` and `PageSize.WasCapped` compared with omitting the limit; only
`PageSize.Applied` matches. With default 50 and cap 10, omission requests 50/applies 10
and is capped, while `?limit=10` requests/applies 10 and is not capped. The applied schema
default keeps an explicitly filled-in request valid under `Reject`; clients that need
omission's requested-size semantics must leave the parameter absent.
