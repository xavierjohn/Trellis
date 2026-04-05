# Conditional Request Example

This example demonstrates ETag-based conditional GETs and optimistic concurrency with Trellis, Minimal APIs, EF Core, and in-memory SQLite.

## What You'll Learn
- How `If-None-Match` enables `304 Not Modified`
- How optional versus required `If-Match` changes update behavior
- How Trellis maps ETags into response metadata and update guards

## Prerequisites
- .NET 10 SDK

## Run It
```bash
dotnet run
```

The app starts at `https://localhost:62265` and `http://localhost:62266`.

## Key Files
| File | What It Shows |
|------|--------------|
| `Program.cs` | App startup, SQLite setup, and route registration |
| `Api/ProductRoutes.cs` | Optional and required ETag routes |
| `Domain/Product.cs` | Aggregate with built-in `ETag` support |
| `Data/ProductDbContext.cs` | EF Core conventions and Trellis interceptors |
| `api.http` | Request flow for create, read, and conditional update |

## Related Docs
- [ASP.NET Core Integration](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [Entity Framework Core Integration](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [Examples](https://xavierjohn.github.io/Trellis/articles/examples.html)
