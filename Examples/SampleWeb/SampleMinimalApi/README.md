# SampleMinimalApi

Canonical Minimal API sample. Consumes [`SampleUserLibrary`](../SampleUserLibrary/)
and exposes user / product / order endpoints through Trellis.

## What this teaches

| Concept | Where to look |
|---|---|
| Minimal API endpoint groups with VO route binding (`OrderId orderId` from `{orderId}`) | `Endpoints/OrderEndpoints.cs` |
| `Result<T>` → HTTP via `ToHttpResultAsync` / `ToCreatedAtRouteHttpResultAsync` | every endpoint file |
| Workflow boundary — `repo.GetAsync(id, ct).BindAsync(agg => agg.Method().ToTask()).TapAsync(CommitAsync)` | `Workflows/OrderWorkflow.cs` |
| Wire-boundary value objects in request DTOs (axiom A1a/A1b) | `Models/Requests.cs` |
| In-memory repository as a thin `IRepository<TAggregate, TId>` adapter | `Persistence/Repositories.cs` |
| Reusable shared domain library across hosts (axiom A8 — domain purity) | `..\SampleUserLibrary\` |

## Project layout

```
SampleMinimalApi/
├── Endpoints/      User, Product, Order endpoint groups (MapGroup)
├── Workflows/      Application boundary — mutate → publish → accept → persist
├── Persistence/    In-memory IRepository<,> implementations
├── Models/         Request DTOs and response records (VOs at the wire)
└── Program.cs      Host wiring (DI, OpenAPI, route registration)
```

## Run it

```pwsh
cd Examples/SampleWeb/SampleMinimalApi
dotnet run
```

The host listens on `http://localhost:5080`. Use [`api.http`](api.http)
(VS Code REST Client / Visual Studio HTTP file support) to exercise every
endpoint without writing curl by hand.

## Test it

```pwsh
dotnet test ../SampleMinimalApi.Tests/SampleMinimalApi.Tests.csproj
```

## AOT status

`<PublishAot>` is **disabled** today. Two framework gaps must close first:

1. `Trellis.Asp/src/Validation/ScalarValueValidationMiddleware.cs` parses
   exception text — won't survive trimming.
2. Composite VO `JsonConverter` source generation (currently scalar-only).

Tracked in `BACKLOG.md` under "Open — Framework Features". The csproj has a comment
flagging this so the AOT switch can be flipped once the fixes land.

## Contrast with `Showcase`

| Concern | Showcase | SampleMinimalApi |
|---|---|---|
| Hosting | MVC controllers | Minimal API endpoint groups |
| Domain | Banking (state machine + Money) | Users / products / orders |
| Persistence | In-memory repository | In-memory repository |
| Layout | 3-project (Domain / Api / Tests) | Single web project + shared library + tests |

Pick whichever matches the host style you're building. Both samples follow the same
11 axioms documented in [`Examples/README.md`](../../README.md).
