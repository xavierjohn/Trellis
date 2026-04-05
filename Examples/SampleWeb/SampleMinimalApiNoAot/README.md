# SampleMinimalApiNoAot

This example shows a Trellis Minimal API without source generation or Native AOT requirements.

## What You'll Learn
- How reflection fallback still supports automatic scalar value validation
- How Minimal APIs can combine EF Core, authorization, ETags, and parallel queries
- What the simplest Trellis web host looks like

## Prerequisites
- .NET 10 SDK

## Run It
```bash
dotnet run
```

The app starts at `http://localhost:5002`.

## Key Files
| File | What It Shows |
|------|--------------|
| `Program.cs` | Reflection-based Minimal API setup, EF Core, auth, and telemetry |
| `API/UserRoutes.cs` | Manual versus automatic validation endpoints |
| `API/ProductRoutes.cs` | Product CRUD, filtering, pagination, and ETags |
| `API/NewOrderRoutes.cs` | Order creation and update flows with result chains |
| `API/DashboardRoutes.cs` | `ParallelAsync` and `WhenAllAsync` in an HTTP endpoint |
| `SampleMinimalApiNoAot.csproj` | No source generator and no AOT-specific project setup |

## Related Docs
- [ASP.NET Core Integration](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [Entity Framework Core Integration](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
- [ASP.NET Core Authorization](https://xavierjohn.github.io/Trellis/articles/integration-asp-authorization.html)
