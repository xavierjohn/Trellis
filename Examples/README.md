# Trellis Examples

Practical sample projects that show how Trellis fits into console apps, ASP.NET Core APIs, EF Core, authorization, and HTTP workflows.

## What You'll Learn
- How Trellis models validation, workflows, and state changes
- How the same domain ideas map to console apps and web APIs
- Where to start based on the feature you want to explore

## Prerequisites
- .NET 10 SDK

## Run It
```bash
dotnet run --project EcommerceExample/EcommerceExample.csproj
dotnet run --project BankingExample/BankingExample.csproj
dotnet run --project AuthorizationExample/AuthorizationExample.csproj
dotnet run --project SsoExample/SsoExample.csproj --launch-profile Development
dotnet run --project SampleWeb/SampleMinimalApiNoAot/SampleMinimalApiNoAot.csproj
```

## Examples
| Example | What It Shows |
|------|--------------|
| `AuthorizationExample` | Manual authorization versus mediator pipeline authorization |
| `SsoExample` | Mapping JWT or development headers to a Trellis `Actor` |
| `EfCoreExample` | Value objects, EF Core conventions, and an order state machine |
| `EcommerceExample` | Order processing, recovery, domain events, and specifications |
| `BankingExample` | Fraud checks, transfers, limits, recovery, and audit-friendly workflows |
| `SampleWeb` | Shared domain model exposed through MVC and Minimal API hosts |
| `ConditionalRequestExample` | ETag-based conditional GET and optimistic concurrency |

## Key Files
| File | What It Shows |
|------|--------------|
| `AuthorizationExample/Program.cs` | Side-by-side authorization demo entry point |
| `SsoExample/Program.cs` | Development and production authentication setup |
| `EfCoreExample/Program.cs` | Console walkthrough of EF Core plus Trellis primitives |
| `EcommerceExample/Workflows/OrderWorkflow.cs` | End-to-end order orchestration |
| `BankingExample/Workflows/BankingWorkflow.cs` | Secure banking workflow composition |
| `SampleWeb/README.md` | Web sample map and ports |

## Related Docs
- [Examples](https://xavierjohn.github.io/Trellis/articles/examples.html)
- [Introduction](https://xavierjohn.github.io/Trellis/articles/intro.html)
- [Basics](https://xavierjohn.github.io/Trellis/articles/basics.html)
