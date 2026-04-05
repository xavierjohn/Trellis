# Authorization Example

This example compares two ways to enforce the same document rules: direct service methods and mediator pipeline behaviors.

## What You'll Learn
- How `Actor`, `IAuthorize`, and `IAuthorizeResource<T>` fit together
- How to keep authorization inside services or move it into a pipeline
- How validation and authorization can run before handlers execute

## Prerequisites
- .NET 10 SDK

## Run It
```bash
dotnet run
```

## What It Does
- Alice can edit her own document
- Bob is blocked from editing someone else's document
- Charlie can edit because he has elevated permissions
- Publishing requires a separate permission check

## Key Files
| File | What It Shows |
|------|--------------|
| `Program.cs` | Runs the direct-service and mediator versions |
| `DirectServiceExample.cs` | Authorization checks mixed into service methods |
| `MediatorExample.cs` | Authorization and validation enforced by mediator behaviors |
| `Actors.cs` | Sample actors and actor provider setup |
| `Document.cs` | Shared document model and in-memory store |

## Related Docs
- [ASP.NET Core Authorization](https://xavierjohn.github.io/Trellis/articles/integration-asp-authorization.html)
- [Mediator Pipeline](https://xavierjohn.github.io/Trellis/articles/integration-mediator.html)
- [Examples](https://xavierjohn.github.io/Trellis/articles/examples.html)
