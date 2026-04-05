# Sample Web

This folder contains three ASP.NET Core apps that expose the same kinds of Trellis-powered behavior through different hosting styles.

## What You'll Learn
- How Trellis works in MVC controllers and Minimal APIs
- How automatic scalar value validation and Problem Details responses behave in web apps
- How the same shared domain model can back multiple API hosts

## Prerequisites
- .NET 10 SDK

## Run It
```bash
dotnet run --project SampleMinimalApi/SampleMinimalApi.csproj
dotnet run --project SampleMinimalApiNoAot/SampleMinimalApiNoAot.csproj
dotnet run --project SampleWebApplication/src/SampleWebApplication.csproj
```

## Projects
| Project | URL | What It Shows |
|------|--------------|--------------|
| `SampleMinimalApi` | `http://localhost:5001` | Minimal API with source-generated JSON metadata |
| `SampleMinimalApiNoAot` | `http://localhost:5002` | Minimal API using reflection fallback |
| `SampleWebApplication` | `http://localhost:5003` | MVC controllers with OpenAPI and Scalar |

## Key Files
| File | What It Shows |
|------|--------------|
| `SampleUserLibrary/Aggregate/User.cs` | Shared user aggregate and FluentValidation rules |
| `SampleDataAccess/AppDbContext.cs` | Shared EF Core model and persistence setup |
| `SampleMinimalApi/Program.cs` | AOT-friendly Minimal API host |
| `SampleMinimalApiNoAot/Program.cs` | Reflection-based Minimal API host |
| `SampleWebApplication/src/Program.cs` | MVC host and controller wiring |
| `OrderApi.http` | Ready-made requests for exercising the sample APIs |

## Related Docs
- [ASP.NET Core Integration](https://xavierjohn.github.io/Trellis/articles/integration-aspnet.html)
- [FluentValidation Integration](https://xavierjohn.github.io/Trellis/articles/integration-fluentvalidation.html)
- [Entity Framework Core Integration](https://xavierjohn.github.io/Trellis/articles/integration-ef.html)
