# Showcase

End-to-end Trellis sample using a banking domain. The **same domain** is hosted by **two
front-ends** — an MVC controller stack and a Minimal API endpoint stack — so you can compare
the two hosting styles side-by-side over a single, identical contract.

## What this teaches

| Concept | Where to look |
|---|---|
| `Error.InvalidInput` + `FieldViolation` collected before failing | `Showcase.Domain/Aggregates/BankAccount.cs` (`TryCreate`) |
| `Error.Conflict` for domain rule violations | `Showcase.Domain/Aggregates/BankAccount.cs` (`Deposit`, `Withdraw`) |
| `Error.NotFound` with `ResourceRef` | `Showcase.Application/Persistence/IAccountRepository.cs` |
| `HttpError.PreconditionFailed` via `Error.TransportFault` envelope | `ConditionalRequestExample` (sibling sample) |
| `Error.Forbidden` with `policyId` | `Showcase.Application/Services/InMemoryIdentityVerifier.cs` |
| `Error.Unexpected` with `faultId` | `Showcase.Mvc/Controllers/DiagnosticsController.cs` and `Showcase.MinimalApi/Endpoints/DiagnosticsEndpoints.cs` |
| `Error.AuthenticationRequired` from a boundary adapter | `Showcase.Application/Services/InMemoryIdentityVerifier.cs` |
| Plain ROP (`Ensure`/`Bind`/`Tap`/`Map`) | `Showcase.Domain/Aggregates/BankAccount.cs` (money operations) |
| `Trellis.StateMachine` lifecycle modeling | `Showcase.Domain/Aggregates/BankAccount.cs` (`Freeze`, `Unfreeze`, `Close`) |
| Invalid state transition → `Error.InvariantViolation` via `FireResult` | `BankAccount.Unfreeze` on an Active account |
| Application/workflow boundary (events → AcceptChanges → persist) | `Showcase.Application/Workflows/BankingWorkflow.cs` |
| `Trellis.Asp.ToHttpResponse(...).AsActionResult<T>()` mapping (MVC) | `Showcase.Mvc/Controllers/*` |
| `Trellis.Asp.ToHttpResponseAsync(...)` mapping (Minimal API) | `Showcase.MinimalApi/Endpoints/*` |
| **Mediator pipeline** (`AddMediator` + `AddTrellisBehaviors`) | `Showcase.MinimalApi/Program.cs` |
| **`IValidate` + FluentValidation composition** in one `ValidationBehavior` stage | `Showcase.Application/Features/SubmitBatchTransfers/*` |
| **JSON Pointer normalization** for FluentValidation nested (`/metadata/reference`) and indexer (`/lines/0/memo`) paths (translated to MVC `metadata.reference` / `lines[0].memo` on the wire by `Trellis.Asp`) | `Showcase.Application/Features/SubmitBatchTransfers/SubmitBatchTransfersValidator.cs` |
| AOT-friendly `AddTrellisFluentValidation()` + explicit `AddScoped<IValidator<T>, ...>` | `Showcase.MinimalApi/Program.cs` |
| **IETF Idempotency-Key middleware** (opt-in `[Idempotent]` / `.WithMetadata(new IdempotentAttribute())` on POST — first call executes, retry replays the captured snapshot with `Idempotent-Replayed: true`, same key + mutated body is rejected as 422, missing key on an opted-in endpoint is rejected as 400) | `Showcase.{Mvc,MinimalApi}/Program.cs`, `Showcase.MinimalApi/Endpoints/TransferEndpoints.cs`, `Showcase.Mvc/Controllers/TransfersController.cs` |

> [!NOTE]
> The Showcase intentionally does **not** demonstrate every pipeline surface. The following
> are tracked in the workspace `BACKLOG.md` ("ASP Template — items the Showcase can't
> demonstrate") for the `TrellisAspTemplate`:
>
> - `AddTrellisUnitOfWork<TContext>()` + `TransactionalCommandBehavior` (no `DbContext` here).
> - Resource authorization (`IAuthorizeResource<T>` + `IResourceLoader<,>`).
> - Assembly-scanning `AddTrellisFluentValidation(typeof(...).Assembly)` overload.
> - EF Core-backed `<PublishAot>true</PublishAot>` end-to-end (blocked on EF Core AOT readiness;
>   the Minimal API host already publishes with AOT for non-EF Trellis.Asp/Mediator/FluentValidation paths).

## Project layout

```
Examples/Showcase/
├── api.http                                 Single .http file — works against either host
├── http-client.env.json                     Environments: `mvc` and `minimalapi` (host selector)
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
and how `Result<T>` is converted to an HTTP response (`ToHttpResponse(...).AsActionResult<T>()` vs `ToHttpResponseAsync(...)`).

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
data. Pick the target host from the environment selector — `mvc` or `minimalapi` — defined in
[`http-client.env.json`](./http-client.env.json). The same payloads work against both.

Seed accounts (created on startup):

| Customer | Account ID                              | Type     | Balance |
|----------|-----------------------------------------|----------|---------|
| Alice    | aaaaaaa1-0000-0000-0000-000000000000    | Checking | $1,000  |
| Alice    | aaaaaaa2-0000-0000-0000-000000000000    | Savings  | $5,000  |
| Bob      | bbbbbbb1-0000-0000-0000-000000000000    | Checking | $250    |

### Replaying `api.http`

`api.http` states the status code each request should produce and names the error behind it.
[`replay-api-http.ps1`](./replay-api-http.ps1) executes those claims, so they can be checked
instead of trusted:

```pwsh
cd Examples/Showcase
./replay-api-http.ps1 -Environment mvc -StartHost
```

`-StartHost` starts the host, waits for it to answer, replays, and stops it; omit it to run
against a host you started yourself. Every request is sent in file order and checked against its
`# @expect status:` and `# @expect header:` directives, and the script exits non-zero if any
request no longer does what the file says it does.

The transcript it writes is the more useful half. It records each response in full — status,
headers, and pretty-printed body — so that when the `Error` ADT or the ProblemDetails mapping
changes, a diff of two transcripts shows exactly what a client will see across every error path
the sample exercises, rather than leaving it to be inferred from unit tests:

```pwsh
./replay-api-http.ps1 -Environment mvc        -StartHost -TranscriptPath before.txt
# ... make the change ...
./replay-api-http.ps1 -Environment mvc        -StartHost -TranscriptPath after.txt
git diff --no-index before.txt after.txt
```

The same diff between the two hosts turns the file's parity claim into something falsifiable.
Running it that way today reports the two body differences that `api.http` already marks
`@parity: status-only` on the invalid-`Money` request: the MVC host sends
`application/problem+json; charset=utf-8` and includes `traceId`, and the Minimal API host sends
neither. Transcripts are git-ignored, because each run mints fresh account ids and trace ids.

A replay assumes a freshly started host: the expectations encode the seeded balances and account
statuses, and the idempotent-transfer block expects an empty idempotency store.

## How to test

```pwsh
dotnet test --project Examples/Showcase/tests/Showcase.Tests
dotnet test --project Examples/Showcase/tests/Showcase.MinimalApi.Tests
```

`Showcase.MinimalApi.Tests` is a near-verbatim mirror of the MVC integration tests against the
Minimal API host — proof that the two hosting styles produce identical HTTP behaviour over the
same domain.
