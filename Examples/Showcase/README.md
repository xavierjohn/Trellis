# Showcase

End-to-end Trellis sample using a banking domain. The **same domain** is hosted by **two
front-ends** — an MVC controller stack and a Minimal API endpoint stack — so you can compare
the two hosting styles side-by-side over a single, identical contract.

## What this teaches

| Concept | Where to look |
|---|---|
| `Error.UnprocessableContent` + `FieldViolation` collected before failing | `Showcase.Domain/Aggregates/BankAccount.cs` (`TryCreate`) |
| `Error.Conflict` for domain rule violations | `Showcase.Domain/Aggregates/BankAccount.cs` (`Deposit`, `Withdraw`) |
| `Error.NotFound` with `ResourceRef` | `Showcase.Application/Persistence/IAccountRepository.cs` |
| `Error.PreconditionFailed` | `ConditionalRequestExample` (sibling sample) |
| `Error.Forbidden` with `policyId` | `Showcase.Application/Services/InMemoryIdentityVerifier.cs` |
| `Error.InternalServerError(faultId)` | `Showcase.Mvc/Controllers/DiagnosticsController.cs` and `Showcase.MinimalApi/Endpoints/DiagnosticsEndpoints.cs` |
| `Error.Unauthorized` from a boundary adapter | `Showcase.Application/Services/InMemoryIdentityVerifier.cs` |
| Plain ROP (`Ensure`/`Bind`/`Tap`/`Map`) | `Showcase.Domain/Aggregates/BankAccount.cs` (money operations) |
| `Trellis.Stateless` lifecycle modeling | `Showcase.Domain/Aggregates/BankAccount.cs` (`Freeze`, `Unfreeze`, `Close`) |
| Invalid state transition → `Error.Conflict` via `FireResult` | `BankAccount.Unfreeze` on an Active account |
| Application/workflow boundary (events → AcceptChanges → persist) | `Showcase.Application/Workflows/BankingWorkflow.cs` |
| `Trellis.Asp.ToActionResult` mapping (MVC) | `Showcase.Mvc/Controllers/*` |
| `Trellis.Asp.ToHttpResultAsync` mapping (Minimal API) | `Showcase.MinimalApi/Endpoints/*` |

## Project layout

```
Examples/Showcase/
├── api.http                                 Single .http file — works against either host
├── src/
│   ├── Showcase.Domain/                     Pure domain — aggregate, value objects, events, lifecycle
│   ├── Showcase.Application/                Hosting-agnostic: workflow, repo, services, DTOs, seed
│   ├── Showcase.Mvc/                        MVC host (controllers + Program.cs)
│   └── Showcase.MinimalApi/                 Minimal API host (endpoint groups + Program.cs)
└── tests/
    ├── Showcase.Tests/                      Domain tests + MVC host integration tests
    └── Showcase.MinimalApi.Tests/           Minimal API host integration tests (mirrors MVC tests)
```

The split into `Domain` / `Application` / `Mvc` + `MinimalApi` makes the architectural boundary
explicit: the Minimal API host adds **zero** new application code — it reuses the same DTOs,
repository, workflow, and seed that the MVC host uses. The only delta is how routes are mapped
and how `Result<T>` is converted to an HTTP response (`ToActionResult` vs `ToHttpResult`).

This is intentionally a teaching shape, not the template's full 4-layer shape (Domain /
Application / Acl / Api). The production template adds an Acl layer, Service Level Indicators,
API versioning, and resource-name conventions on top of the same banking domain.

## What is intentionally omitted

| Concern | Where to look instead |
|---|---|
| Service Level Indicators | `Trellis.ServiceLevelIndicators` (separate repo) and the ASP template |
| API versioning | The ASP template |
| Resource-name conventions | The ASP template |
| 4-layer Application/Acl architecture | The ASP template |
| EF Core mapping of the `BankAccount` aggregate | `EfCoreExample` (sibling sample) — the StateMachine field complicates persistence and isn't worth the lesson cost here |

## How to run

Pick a host:

```pwsh
# MVC host  -> https://localhost:61223
cd Examples/Showcase/src/Showcase.Mvc
dotnet run

# Minimal API host  -> http://localhost:5180
cd Examples/Showcase/src/Showcase.MinimalApi
dotnet run
```

Open `<host>/scalar/v1` for the Scalar API explorer, or use [`api.http`](./api.http)
(VS Code REST Client / Visual Studio HTTP file support) to exercise every endpoint with the seed
data — the file's `@host` toggle switches between the two hosts. The same payloads work
against both.

Seed accounts (created on startup):

| Customer | Account ID                              | Type     | Balance |
|----------|-----------------------------------------|----------|---------|
| Alice    | aaaaaaa1-0000-0000-0000-000000000000    | Checking | $1,000  |
| Alice    | aaaaaaa2-0000-0000-0000-000000000000    | Savings  | $5,000  |
| Bob      | bbbbbbb1-0000-0000-0000-000000000000    | Checking | $250    |

## How to test

```pwsh
dotnet test --project Examples/Showcase/tests/Showcase.Tests
dotnet test --project Examples/Showcase/tests/Showcase.MinimalApi.Tests
```

`Showcase.MinimalApi.Tests` is a near-verbatim mirror of the MVC integration tests against the
Minimal API host — proof that the two hosting styles produce identical HTTP behaviour over the
same domain.
