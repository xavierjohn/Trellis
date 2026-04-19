# Showcase

End-to-end Trellis sample using a banking domain. Demonstrates idiomatic V6 `Error` ADT usage
across a domain aggregate, an MVC HTTP API, and a unit + integration test suite.

## What this teaches

| Concept | Where to look |
|---|---|
| `Error.UnprocessableContent` + `FieldViolation` collected before failing | `Showcase.Domain/Aggregates/BankAccount.cs` (`TryCreate`) |
| `Error.Conflict` for domain rule violations | `Showcase.Domain/Aggregates/BankAccount.cs` (`Deposit`, `Withdraw`) |
| `Error.NotFound` with `ResourceRef` | `Showcase.Api/Persistence/IAccountRepository.cs` |
| `Error.PreconditionFailed` | `ConditionalRequestExample` (sibling sample) |
| `Error.Forbidden` with `policyId` | `AuthorizationExample` (sibling sample) |
| `Error.InternalServerError(faultId)` | `Showcase.Api/Controllers/DiagnosticsController.cs` |
| `Error.Unauthorized` from a boundary adapter | `Showcase.Api/Services/InMemoryIdentityVerifier.cs` |
| Plain ROP (`Ensure`/`Bind`/`Tap`/`Map`) | `Showcase.Domain/Aggregates/BankAccount.cs` (money operations) |
| `Trellis.Stateless` lifecycle modeling | `Showcase.Domain/Aggregates/BankAccount.cs` (`Freeze`, `Unfreeze`, `Close`) |
| Invalid state transition → `Error.Conflict` via `FireResult` | `BankAccount.Unfreeze` on an Active account |
| `EquatableArray<T>` value-equality | `Showcase.Tests/Domain/EquatableArrayDemoTests.cs` |
| Exhaustive pattern match over the `Error` ADT | `Showcase.Tests/Domain/ErrorMatchTests.cs` |
| `Trellis.Asp.ToActionResult` mapping | every controller |

## Project layout

```
Examples/Showcase/
├── src/
│   ├── Showcase.Domain/    Pure domain — aggregate, value objects, events, lifecycle triggers
│   └── Showcase.Api/       MVC controllers, in-memory repository, workflow orchestration, DI
└── tests/
    └── Showcase.Tests/     Unit tests (Domain) and integration tests (Api)
```

This is intentionally a **3-project shape (Domain / Api / Tests)**, not the template's
4-layer shape (Domain / Application / Acl / Api). Showcase is optimized for a learner
focused on the error-handling lessons; the production template adds Application orchestration,
an Acl layer, Service Level Indicators, API versioning, and resource-name conventions on top of
the same banking domain.

## What is intentionally omitted

| Concern | Where to look instead |
|---|---|
| Service Level Indicators | `Trellis.ServiceLevelIndicators` (separate repo) and the ASP template |
| API versioning | The ASP template |
| Resource-name conventions | The ASP template |
| 4-layer Application/Acl architecture | The ASP template |
| EF Core mapping of the `BankAccount` aggregate | `EfCoreExample` (sibling sample) — the StateMachine field complicates persistence and isn't worth the lesson cost here |

The repository is an in-memory implementation. EF Core integration is taught by the dedicated
`EfCoreExample` sample.

## How to run

```pwsh
cd Examples/Showcase/src/Showcase.Api
dotnet run
```

Open `https://localhost:<port>/scalar/v1` for the Scalar API explorer.

Seed accounts (created on startup):

| Customer | Account ID                              | Type     | Balance |
|----------|-----------------------------------------|----------|---------|
| Alice    | aaaaaaa1-0000-0000-0000-000000000000    | Checking | $1,000  |
| Alice    | aaaaaaa2-0000-0000-0000-000000000000    | Savings  | $5,000  |
| Bob      | bbbbbbb1-0000-0000-0000-000000000000    | Checking | $250    |

## How to test

```pwsh
dotnet test --project Examples/Showcase/tests/Showcase.Tests
```
