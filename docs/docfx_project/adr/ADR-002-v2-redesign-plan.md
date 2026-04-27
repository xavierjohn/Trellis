# ADR-002 — Trellis v2: Forward-Looking Architectural Redesign

> **Status:** Accepted (in execution). Phase 1a shipped in PR #401 (Trellis.Results → Trellis.Core rename). Phase 2 is in flight in PR #403 (DDD merge into Core, Required* base-class moves, all three source generators bundled into their host packages, Trellis.Asp.Authorization project merge).
>
> **Source of truth.** This document is the authoritative v2 plan. Anything in this repo (api_reference docs, csproj layout, package list in `README.md`, `.github/copilot-instructions.md`) that contradicts this doc is the bug. **Read §2 (Proposed Package Map) and §15 (Phasing) before starting any v2 work.**
>
> **Discoverability.** A copy of the original plan also lives at `C:\GitHub\Trellis\v2-redesign-plan.md` (workspace root, outside the framework repo). The in-repo copy here (ADR-002) is the canonical one — keep them in sync if both are edited.

---

# Trellis — Forward-Looking Architectural Redesign (v2)

> **Lens:** AI-first ergonomics. Every choice optimizes for an LLM producing correct code on the first attempt.
> **Posture:** Forward-looking only. No backward-compat constraint. Package map is on the table.
> **Status:** Proposal for review. Per-package implementation plans follow once this is approved.
>
> **Revision note (v2):** Stress-tested against a GPT-5.4 critique. Removed overclaims (notably "compiler-exhaustive `Error` union"), resolved internal contradictions, restored capabilities silently dropped in v1 (resource authorization, tracing, `MaybeInvariant`, protocol-specific errors, EF persistence diagnostics, `Result.Try` boundary adapter), and demoted out-of-scope rewrites (own dispatcher, EF magic) to later phases.

---

## 0. The One-Page Mental Model

An LLM (or human) building enterprise software with Trellis should hold this in working memory:

```
Domain values are constructed via `<Type>.TryCreate(...)`            → Result<T>
Errors are a finite, well-known set; map to HTTP via Trellis.Asp     → Error (closed-by-convention, analyzer-enforced)
Pipelines: 7 core verbs + BindZip + LINQ query syntax + Try (boundary) + Traverse (collections)
            → Map / Bind / Tap / TapOnFailure / Ensure / Match / Recover  (+ BindZip; + Select/SelectMany/Where)
Multi-field validation builds a single ValidationError               → Validate.Field(...).And(...).Build(...)
Cross-Maybe invariants                                                → MaybeInvariant.AllOrNone / Requires / ExactlyOne ...
Async is one rule: returns Task<Result<T>>, awaited normally         → no Left/Right/Both variants visible
Optionality is one type: Maybe<T>; null and Maybe never coexist      → Maybe<T>
Entities have identity; aggregates have ETags + events; VOs equal-by-components
Persistence stages, the pipeline commits                              → Repo.Add(...), CommitAsync handled by host
HTTP: result.ToHttpResponse(opts) (one verb; opts carry write-outcome, ETag, Location, Vary, ranges, etc.)
Authorization: [Authorize(Permission = ...)] on the message; resource auth via IAuthorizeResource<T>
```

That fits in one screen. The redesign collapses today's surface to ~that screen without losing power. **Honest claim:** the *primary pipeline-verb* count drops to 7, but the *total canonical surface* the AI must know is roughly: 7 verbs + `BindZip` (value-preserving chain) + LINQ query syntax (`Select`/`SelectMany`/`Where`) + `Try`/`TryAsync` + `Traverse` + `Validate` builder + `MaybeInvariant` + `Maybe` operations + 1 HTTP verb + 1 authorization mechanism (layered: `[Authorize]` attribute for the simple case, `IAuthorize` + `IAuthorizeResource<T>` + `IIdentifyResource<,>` + `IResourceLoader<,>` for the resource-aware case) + the error catalog. The current `Trellis.Results/src/Result/Extensions/` folder exposes ≈200 public statics across `Map`/`Bind`/`Tap`/`Ensure`/`Match`/`Recover`/`Combine`/`When`/`Check`/`CheckIf`/`Discard`/`BindZip`/`MapIf`/`MapOnFailure`/`RecoverOnFailure`/etc.; §3.3 specifies ≈30–50 overloads in the new design (excluding the T4-generated `BindZip` 2..9-arity family and the LINQ query-syntax surface, which together add another ~30 overloads). That is approximately a **~4–5× reduction** in extension surface (not the "10×" of an earlier draft, and slightly less than the "5–7×" claimed before `BindZip`/LINQ were retained) — still the largest single AI-ergonomics win in this proposal.

---

## 0.5 The Template Anchors the Design (but is not frozen)

Every redesign decision is validated against `TrellisAspTemplate` (`C:\GitHub\Trellis\TrellisAspTemplate`) — the `dotnet new trellis-asp` scaffold that is the AI's actual entry point to a new project. **The template is not frozen** — if a redesign is genuinely better, the template is refactored to match. But the template is the integration test for every API decision: an API that makes the template uglier is a wrong API.

**Template structure (Clean Architecture):**
```
template/
  Domain/        — Aggregates (TodoItem), Value Objects (Title, DueDate, Tag, TodoId),
                   Domain Events, Specifications, State Machines (LazyStateMachine + Stateless)
  Application/   — ICommand/IQuery handlers (Mediator), ITodoRepository abstraction, IAuthorize
  Acl/           — TodoRepository (EF), TodoItemResourceLoader, AppDbContext
  Api/           — Controllers (MVC), api.http, Middleware, Program.cs (ASP.NET Core 10)
  .github/       — copilot-instructions.md + 13 trellis-api-*.md files (ship in template)
```

**Patterns observed in the current template (each is either preserved or migrated explicitly):**

1. **Commands are records implementing `ICommand<Result<T>>` + `IAuthorize`** with `RequiredPermissions` (per `CreateTodoCommand`, `UpdateTodoCommand`). → **Preserved** — confirms `IAuthorize` marker (Decision Q1) and the `IFailureFactory<TSelf>` pipeline-behavior constraint.
2. **Handlers are `ICommandHandler<TCommand, Result<T>>` returning `ValueTask<Result<T>>`** (`martinothamar/Mediator` shape). → **Preserved** — confirms wrapping rather than owning the dispatcher.
3. **Aggregates expose `TryCreate(...) → Result<TAggregate>`** + private constructors + `LazyStateMachine` for lifecycle. → **Preserved** — confirms scalar VO `TryCreate` pattern and thin Stateless wrapper.
4. **Repositories return `Maybe<T>` for `FindByIdAsync` and `Result<Unit>` for `SaveAsync`/`DeleteAsync`.** → **Migrated:** `Maybe<T>` retained; `Result<Unit>` becomes `Result` (no generic). Phase 1a updates `TodoRepository`.
5. **`UpdateTodoCommand.IfMatchETags : EntityTagValue[]?`** — Application layer carries transport-shaped preconditions through Mediator. → **Preserved** — keeps `EntityTagValue` in `Trellis.Core` (decided in Q3 revision; alternative would have forced refactoring command shape — the value type is genuinely framework-agnostic).
6. **Controllers have at least seven response idioms** (verified against `TodosController.cs` in the template):
   - `ETagHelper.ParseIfMatch(Request)` → `EntityTagValue[]?` passed into the command.
   - `result.ToActionResult(this, todo => RepresentationMetadata.WithStrongETag(todo.ETag), TodoResponse.From)` for success-with-metadata.
   - `result.Error.ToActionResult<TodoResponse>(this)` for failure short-circuit, used 5 times in the template's controller alone.
   - `Response.Headers.ETag = EntityTagValue.Strong(todo.ETag).ToHeaderValue()` for explicit header writes (the `Create` action uses this *and* `CreatedAtAction`, which the unified verb must reconcile).
   - `BindAsync(command => _sender.Send(command, ct))` chaining sync command construction to async dispatch.
   - `ToActionResultAsync(this, todos => projection)` — async variant with projection, used on collection-returning queries.
   - Parameterless `ToActionResultAsync(this)` on `Result<Unit>` returning `204 NoContent` — the `Delete` action's pattern, which depends on `Trellis.Unit` being an addressable type.
   → **Migrated** in Phase 3: §6's single `ToHttpResponse` verb subsumes all seven for both MVC and Minimal. The seven-idiom pattern is exactly what the redesign aims to collapse, and the migrated controller is the concrete proof that §6 works (see Appendix A — included with the Phase 3 deliverable).
7. **`[CustomerResourceId] TodoId id` action-parameter attribute** binds + authorizes resource ids. → **Preserved**, namespace migrates to `Trellis.Asp` after Phase 3 absorbs `Trellis.Asp.Authorization`.
8. **`app.UseScalarValueValidation()` in Program.cs.** → **Preserved**, with the auto-registration option from §6.5 making the explicit call optional.
9. **Template ships its own `.github/copilot-instructions.md` + 13 `trellis-api-*.md` files.** → **Preserved as the canonical AI surface.** The repo-level `docs/api_reference/` files are the source; the template's copies are kept in sync by Phase 5 tooling.

**Process commitment — template is rewritten as a post-GA Phase 6 deliverable.**

- Phases 1a–5a do framework-only work. The v1 template stays on v1.x packages and is **not** kept green during the framework rewrite.
- The framework is validated against its own unit/integration test suite plus an internal "v2-canary" sample app (see §15 Phase 5a quality gates) before the v2.0.0 packages are cut.
- Once framework v2.0.0 ships, **Phase 6** rewrites `Trellis.AspTemplate` from a clean slate against the v2 surface, preserving the 4 Clean-Architecture layers (Domain/Application/ACL/API).
- Breaking changes are documented inline in each `trellis-api-*.md` "Breaking changes from v1" section. **No standalone `MIGRATION_v<N>.md` document is published** (see §12.4).

---

## 0.6 Trellis Without ASP.NET — non-web consumers

The template anchors the API design, but Trellis is a general-purpose .NET library, **not an ASP.NET framework**. This section describes what using Trellis looks like when there is no HTTP layer: console apps, worker services / `IHostedService`s, queue consumers, gRPC services, Azure Functions, Hangfire jobs, and pure domain class libraries.

### 0.6.1 Package selection by host type

| Host shape | References | Does NOT reference |
|---|---|---|
| **Pure domain library** (no host) | `Trellis.Core` (includes Result/Maybe/Error + DDD primitives `Aggregate`/`Entity`/`ValueObject`/`Specification` + VO base classes), `Trellis.Primitives` (optional concrete VOs), `Trellis.StateMachine` (if needed) | `Trellis.Asp`, `Trellis.Mediator`, `Trellis.EntityFrameworkCore`, `Trellis.Http`, `Trellis.Authorization` |
| **Console app / CLI tool** | Above + `Trellis.Mediator` (optional), `Trellis.EntityFrameworkCore` (if persisting), `Trellis.Http` (if calling APIs) | `Trellis.Asp`, `Trellis.Asp.Authorization` (gone — merged into Asp anyway), `Trellis.Testing.AspNetCore` |
| **Worker service / queue consumer** | Same as console app + `Trellis.Authorization` if doing actor-based auth on messages | Same exclusions as console app |
| **gRPC service** (no MVC) | Same as worker + `Trellis.Authorization` | `Trellis.Asp` (gRPC has its own status-mapping idioms; see §0.6.4) |
| **Azure Functions / Lambda** | Same as worker | `Trellis.Asp` |
| **Test project** | `Trellis.Testing` (always); `Trellis.Testing.AspNetCore` only if testing through `WebApplicationFactory` | — |

The dependency tree is **`Trellis.Asp`-optional**. A worker service should never need to install `Trellis.Asp` "just for `EntityTagValue`" — the value type is in `Trellis.Core`. Likewise it should never need `Trellis.DomainDrivenDesign` as a separate install — DDD primitives ship inside `Trellis.Core`.

**On the "swap out DDD" capability** (the original reason `Trellis.DomainDrivenDesign` was a separate package): it survives the merge intact — *if you don't inherit from `Trellis.Aggregate` / `Trellis.Entity` / `Trellis.ValueObject`, you don't use them*. The base classes are inert metadata in your assembly; zero runtime cost. The honest constraint: features that presume Trellis's DDD types (EF integration's `IAggregate` interceptors, `IAuthorizeResource<TResource>`, the composite-VO source generator, ETag automation) are unavailable to a custom hierarchy. If you need those features, use Trellis's primitives. If you don't, write your own and ignore them. We chose to merge because preserving an unused capability behind a separate package was less valuable than (a) eliminating the post-Phase-2 backward-dependency between `Composite` and `ValueObject` and (b) presenting a single domain-modeling vocabulary under one `using Trellis;`.

### 0.6.2 Canonical patterns for non-ASP code

The seven pipeline verbs (`Map`, `Bind`, `Tap`, `TapOnFailure`, `Ensure`, `Match`, `Recover`) plus `BindZip`, the LINQ query-syntax surface (`Select`/`SelectMany`/`Where`), `Try`, `TryAsync`, `Traverse`, `Validate.Field(...).Build(...)`, and `MaybeInvariant` are 100% applicable in non-ASP code. They are pure functional combinators with no host dependency. Same for `Result.Combine<T1..T9>`, `Result.ParallelAsync<T1..T9>`, error catalog factories, scalar/composite VO base classes, and `Maybe<T>`.

**Console app — top-of-`Main` pattern (the non-ASP equivalent of `ToHttpResponse`):**

```csharp
// Program.cs (console app)
public static async Task<int> Main(string[] args)
{
    var host = Host.CreateApplicationBuilder(args)
        .Services
        .AddTrellisCore()           // tracing, debug settings
        .AddTrellisMediator()        // pipeline behaviors
        .AddDbContext<AppDbContext>(...)
        .AddTrellisEntityFrameworkCore<AppDbContext>()
        .BuildServiceProvider();

    var mediator = host.GetRequiredService<IMediator>();
    var result = await mediator.Send(new ImportFileCommand(args[0]));

    return result.Match(
        _ => 0,                                                  // success → exit 0
        error => Trellis.ConsoleHost.ExitCode.For(error));       // catalog → POSIX exit code
}
```

`Trellis.ConsoleHost.ExitCode.For(Error)` is the **non-ASP analog of `ErrorHttpMapping.ToStatusCode`** — a mapping function that lives in `Trellis.Core` (it's just a switch over the catalog). Suggested mapping:

| Error case | Exit code | Rationale |
|---|---|---|
| Success | 0 | POSIX convention |
| `ValidationError` / `BadRequestError` | 64 | EX_USAGE (sysexits.h) |
| `NotFoundError` | 66 | EX_NOINPUT |
| `UnauthorizedError` / `ForbiddenError` | 77 | EX_NOPERM |
| `ConflictError` | 75 | EX_TEMPFAIL |
| `DomainError` | 65 | EX_DATAERR |
| `UnexpectedError` | 70 | EX_SOFTWARE |
| HTTP-flavored errors (`RateLimitError`, `ServiceUnavailableError`, etc.) | 75 | EX_TEMPFAIL (treated as transient — see §0.6.3) |
| Anything else | 1 | Generic failure |

**Worker service top-of-loop pattern:**

```csharp
public class ImportWorker(IMediator mediator, ILogger<ImportWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var msg = await queue.DequeueAsync(ct);
            var result = await mediator.Send(new ProcessMessage(msg), ct);

            // Log + continue, don't crash the loop.
            result.Tap(_ => log.LogInformation("Processed {Id}", msg.Id))
                  .TapOnFailure(error => log.LogError("Failed {Id}: {Code} {Detail}", msg.Id, error.Code, error.Detail));
        }
    }
}
```

The `TapOnFailure(Action<Error>)` verb (§3.2) is the canonical "log on failure, continue the pipeline" verb — same in worker as in HTTP code. The success-side analog is `Tap(Action<T>)`. Keeping the two paths under distinct names (rather than overloading `Tap` on the parameter type) makes the call site read as what it does — `result.TapOnFailure(log)` is unambiguously the failure path, no squinting at the lambda parameter type required.

### 0.6.3 The error catalog in non-HTTP contexts

Non-ASP consumers see all 17 cases of the error catalog, but in practice use only the 8 transport-agnostic ones (`ValidationError`, `NotFoundError`, `ConflictError`, `DomainError`, `UnauthorizedError`, `ForbiddenError`, `BadRequestError`, `UnexpectedError`). The 9 HTTP-flavored cases (`PreconditionFailedError`, `RateLimitError`, …) are **inert records** — they sit in the assembly with zero behavior and zero startup cost. They appear in `Match` only if the consumer chooses to pattern-match on them.

**Why we don't split them into a separate package:**
- They are records; they cost nothing.
- ACL layers in non-ASP apps (queue consumers, gRPC clients, outbound HttpClient calls) **do** produce them when translating upstream HTTP responses. A worker service that calls a downstream HTTP API needs `RateLimitError` and `RetryAfterValue` to implement retry logic.
- Splitting would create an awkward middle layer: pure-domain library doesn't need them, ACL does — and almost every real worker has an ACL layer.
- `EntityTagValue` and `RetryAfterValue` are **framework-agnostic value types**. They serialize to ETag/Retry-After headers when an HTTP layer exists, but they're equally valid as cache-key components or queue-message metadata. Keeping them in `Trellis.Core` lets non-HTTP code use them without an apologetic dependency.

**Analyzer note:** `TRLS027` (missing-case in `Match`) only fires when the user opts into exhaustive matching. A console app that writes `result.Match(success, _ => 1)` is unaffected by HTTP-flavored cases.

### 0.6.4 Mapping for other transports

The `ToHttpResponse` verb lives only in `Trellis.Asp`. Other transports follow the same pattern but in their own optional integration package:

| Transport | Mapping verb | Package | Status |
|---|---|---|---|
| ASP.NET Core HTTP | `result.ToHttpResponse(opts)` | `Trellis.Asp` | In v2 plan, §6 |
| CLI exit code | `Trellis.ConsoleHost.ExitCode.For(error)` | `Trellis.Core` (just a switch — no host dependency) | Add in Phase 1b |
| gRPC status | `result.ToGrpcStatus()` | `Trellis.Grpc` (future, post-2.0) | Not in current scope |
| Worker logging | `result.Tap(success).TapOnFailure(failure)` (no special verb) | `Trellis.Core` | Already covered by §3.2 verbs |

**Future-package note:** `Trellis.Grpc` is not in the v2 redesign scope and is not counted in the 12 packages. It is mentioned only to show the redesign is open to additional transport adapters following the same shape as `Trellis.Asp`.

### 0.6.5 What this implies for §3 (`Trellis.Core`)

- The error catalog stays at **17 cases in `Trellis.Core`** (decision confirmed in this section's open question — no split into a `Trellis.Web` package).
- `EntityTagValue` and `RetryAfterValue` stay in `Trellis.Core` (already decided in §12.2 for layering reasons; reconfirmed here for non-ASP usability reasons).
- `Trellis.DomainDrivenDesign` merges into `Trellis.Core` in Phase 2 (see §15). Final `Trellis.Core` contents: Result/Maybe/Error + tracing + `EntityTagValue`/`RetryAfterValue` + scalar/composite VO base classes + `Aggregate`/`Entity`/`ValueObject`/`Specification` + domain events + `MaybeInvariant` + `Validate` builder. All within the `Trellis` namespace.
- Add `Trellis.ConsoleHost.ExitCode.For(Error) → int` in Phase 1b alongside the error catalog rewrite. It is a single static method with no dependencies; it lives in `Trellis.Core` so console apps don't need a separate "console adapter" package.
- Non-ASP migration guidance lives inline in `trellis-api-core.md` "Breaking changes from v1" — no standalone migration document is published (see §12.4).

### 0.6.6 Implication for §0.5 (template anchor)

The ASP template remains the **primary** anchor for the redesign because it exercises the most surface area. **A second anchor is added** in Phase 5 (after the framework is stable): a minimal **worker-service sample** in `TrellisAspTemplate` (or a sibling `TrellisWorkerTemplate` repo if scoping demands it) that proves the non-ASP path is first-class. This sample is not a deliverable for Phases 1–4, but it is the acceptance gate for "the redesign works for non-ASP consumers" and is added to the v2.0.0 release criteria.

---

## 0.7 Scope Boundary with ASP.NET Core

> **Trellis owns the *response shape* and the *error taxonomy*.
> ASP.NET Core owns the *protocol plumbing*.**

This boundary settles every recurring "should Trellis ship X?" question. When a
proposed feature falls on the ASP.NET Core side of the line, Trellis composes
with it but does NOT reimplement it. When a feature is about how Trellis types
(`Result<T>`, `WriteOutcome<T>`, `Error`, `RepresentationMetadata`,
`AuthChallenge`, `EntityTagValue`, `Prefer`) project onto an HTTP response,
Trellis owns it.

### 0.7.1 ASP.NET Core's territory — Trellis MUST NOT reimplement

The following are first-class in `Microsoft.AspNetCore.*` and Trellis adds zero
value by reinventing them. Trellis MAY ship documentation/examples that show
how to wire them, but no parallel implementation:

| Concern | ASP.NET Core surface |
|---|---|
| JWT / Bearer / OIDC / OAuth | `Microsoft.AspNetCore.Authentication.{JwtBearer,OpenIdConnect,OAuth}` |
| `/.well-known/openid-configuration`, JWKS fetch | OIDC handler (auto-exposed) |
| CORS | `services.AddCors()` + `app.UseCors()` |
| Rate-limit enforcement (429 + `Retry-After`) | `Microsoft.AspNetCore.RateLimiting` (.NET 7+) |
| JSON Patch (RFC 6902) | `Microsoft.AspNetCore.JsonPatch` |
| Output caching / response caching | `app.UseOutputCache()` + `[OutputCache]` |
| OpenAPI document generation | `Microsoft.AspNetCore.OpenApi` (.NET 9+) or Swashbuckle |
| Antiforgery, data protection, session, cookies | built-in middleware |
| Routing, model binding, content negotiation primitives | MVC / Minimal APIs |
| Kestrel-level concerns: HTTP/2, HTTP/3, TLS, compression | Kestrel |

### 0.7.2 Trellis's territory — composition layer on top

Trellis owns response-shape and error-taxonomy concerns that ASP.NET Core
does NOT have an opinion on, and that benefit from being expressed in Trellis's
own type system:

| Concern | Trellis surface |
|---|---|
| Result/Maybe railway, error catalog | `Trellis.Core` |
| `WriteOutcome<T>` → HTTP status mapping (per §6.2) | `Trellis.Asp.WriteOutcomeExtensions` |
| ETag / Last-Modified / Vary / Content-Language emission | `RepresentationMetadata` + `EntityTagValue` |
| Conditional request evaluation (If-Match / If-None-Match / If-Modified-Since) | `ConditionalRequestEvaluator` |
| Prefer header parsing + `return=minimal` honoring + `Preference-Applied` + `Vary: Prefer` | `PreferHeader` + write mappers |
| Range request evaluation (RFC 9110 §14, byte units) | `RangeRequestEvaluator` |
| Problem Details (`application/problem+json`) emission with extension members | `Error.ToHttpResult` + `HttpResultExtensions` |
| `WWW-Authenticate` emission when an `Error.Unauthorized` short-circuits the auth pipeline | `AuthChallenge` (wiring is a Phase 2 gap) |
| OpenAPI schema *contributions* (transformers/operation filters) for Trellis types | `Trellis.Asp.OpenApi` (proposed) |
| Problem-type URI registry (canonical `type` URIs per error kind) | `Trellis.Core` error catalog |
| `Cache-Control` / `immutable` / `Age` directives paired with ETag | `RepresentationMetadata` (Phase 2) |
| `Link`-header pagination + cursor model | `Trellis.Asp` (Phase 2) |
| `Idempotency-Key` middleware + write-dedup primitives | `Trellis.Asp` (Phase 3) |
| `RateLimit-Limit/Remaining/Reset` draft headers (reading from middleware state) | `Trellis.Asp` filter (Phase 3) |
| JSON Merge Patch (RFC 7396) glue | `Trellis.Asp` (Phase 3) |
| Repository / aggregate / WriteOutcome plumbing | `Trellis.EntityFrameworkCore` |
| Mediator pipeline behaviors that surface `Result<T>` to the boundary | `Trellis.Mediator` |

### 0.7.3 The decision rule

When evaluating any future feature request, ask:

1. **Is this protocol plumbing already in `Microsoft.AspNetCore.*`?** → out of scope. Document the wiring; do not reimplement.
2. **Does this feature interpret a Trellis type (`Result<T>`, `Error`, `WriteOutcome<T>`, `RepresentationMetadata`, `AuthChallenge`) as an HTTP construct?** → in scope.
3. **Is this an opinion ASP.NET Core deliberately leaves to the application (cursor pagination shape, idempotency-key semantics, problem-type URIs, response-shape cache directives)?** → in scope IF Trellis's type system makes it cleaner; out of scope otherwise.

This rule keeps the framework small, makes "is it Trellis or ASP.NET Core?"
unambiguous for AI completions, and prevents scope creep into auth flows,
edge caching, and protocol-stack work that the BCL already does well.

### 0.7.4 Pagination is a first-class Trellis concern (business APIs ≠ media APIs)

> **Spike status (PR #400, merged):** the design below has been validated end-to-end in `Trellis.Results` + `Trellis.Asp` + Showcase. `Page<T>`, `Cursor`, the wire envelope, the `Link` header co-emission, and **parity across Minimal API + MVC** are shipped. The full PR-PAGE deliverable (rename to `Trellis.Core`, `[Obsolete]` the byte-range overloads, OpenAPI transformer, recipe doc) is still open — see "Remaining work" below.

Trellis serves business APIs, not media-transfer APIs. **Bytes / Range / 206 Partial Content** are the wrong primitives for paging collections of business records — RFC 9110 §14 was designed for partial transfer of an octet stream (resume a download, request a video segment), and the only universally-understood range unit is `bytes`. Custom range units (`items`, `pages`) are *syntactically* allowed by the RFC but have zero ecosystem interoperability: CDNs, proxies, HTTP clients, and OpenAPI tooling do not understand them.

The decision:

| Use case | Primitive | RFC |
|---|---|---|
| Partial transfer of a binary resource (file download, blob, video segment) | `Range` request → `206 Partial Content` + `Content-Range` (bytes only) | RFC 9110 §14 |
| Server-driven pagination of a business collection | `200 OK` + body envelope (`Page<T>`) + `Link` header (RFC 8288) | RFC 8288 |

**Trellis adopts a body-envelope-with-cursor model as primary, co-emits `Link` header for free.** Rationale:

- **AI-first**: the cursor sits in the JSON the LLM already parsed — no header inspection, no `Link: <…>; rel="next"` regex. Follow-up calls trivially compose: `nextRequest.cursor = lastResponse.next.cursor`.
- **.NET / OData alignment**: Microsoft Graph and OData v4 both use body-envelope `nextLink`. Generated clients and existing .NET tooling cooperate naturally.
- **`Link` header is ~10 LOC of co-emission**: serves crawlers, hypermedia clients, gateways, and RFC-8288-aware tooling at near-zero cost.
- **Industry split is ~70% envelope / ~30% Link-only / ~0% Range/206** — the envelope is the dominant idiom; co-emitting Link covers the minority.

Concrete shapes (shipped in `Trellis.Results` as a spike; will move to `Trellis.Core` post-rename):

```csharp
public readonly record struct Page<T>
{
    public IReadOnlyList<T> Items { get; }
    public Cursor? Next { get; }
    public Cursor? Previous { get; }
    public int RequestedLimit { get; }   // what the client asked for
    public int AppliedLimit { get; }     // server cap actually used (<= RequestedLimit)

    public int DeliveredCount => Items?.Count ?? 0;
    public bool WasCapped => AppliedLimit < RequestedLimit;

    public Page(IReadOnlyList<T> items, Cursor? next, Cursor? previous,
                int requestedLimit, int appliedLimit) { /* validates invariants */ }
}

public readonly record struct Cursor(string Token);   // opaque, base64url
```

Wire format:

```http
HTTP/1.1 200 OK
Content-Type: application/json
Link: <https://api/widgets?limit=5&cursor=eyJpZCI6IjQyIn0%3D>; rel="next"
Vary: Prefer
Preference-Applied: handling=lenient

{
  "items": [ ... ],
  "next": { "cursor": "eyJpZCI6IjQyIn0=", "href": "https://api/widgets?limit=5&cursor=eyJpZCI6IjQyIn0%3D" },
  "previous": null,
  "requestedLimit": 10,
  "appliedLimit": 5,
  "deliveredCount": 5,
  "wasCapped": true
}
```

Notes:

- `requestedLimit` vs `appliedLimit` makes server-side caps observable to the client; `wasCapped` is a derived convenience flag. RFC 7240 `Preference-Applied: handling=lenient` may co-emit when the server clamped the request.
- Cursor stays opaque — server-internal serialization (signed/encrypted JSON, base64url). Clients MUST treat it as a black box.
- `Page<T>` is a struct (allocation-cheap) but `Items` is a heap list — that is fine; the envelope is allocated once per response.
- The existing `PartialContentResult` / `PartialContentHttpResult` is **demoted to byte-range only** in PR-PAGE; the collection-pagination overloads will receive `[Obsolete]` pointing to `Page<T>`. RFC 7233/9110 byte semantics remain fully supported for media endpoints.

Phase 2 deliverable PR (`PR-PAGE`) — status:

1. ✅ `Page<T>`, `Cursor` shipped (in `Trellis.Results`; move to `Trellis.Core` lands with PR-A rename).
2. ✅ `Trellis.Asp.PageHttpResultExtensions.ToPagedHttpResult` (Minimal API) and `Trellis.Asp.PageActionResultExtensions.ToPagedActionResult` (MVC) — both delegate to a single internal `PagedResponseBuilder` for byte-identical envelope + `Link` header across hosting styles. Verb names follow §3.3 / Axiom B.
3. ⬜ Demote `PartialContentResult` collection overloads to `[Obsolete]`.
4. ✅ Showcase paginates `GET /api/accounts/` end-to-end on both Minimal API and MVC hosts (server cap of 5; client requests 10; follow `Link rel="next"` / body cursor to drain). Integration tests cover both hosts.
5. ⬜ OpenAPI schema transformer that surfaces the `Link` response header for any operation returning `Page<T>`.
6. ⬜ Recipe doc: `docs/docfx_project/articles/pagination.md`.

### 0.7.5 Implication for §0.6 (non-web consumers)

The boundary in §0.7 applies *only* to `Trellis.Asp` and `Trellis.Asp.*`. Core
and downstream packages remain framework-agnostic per §0.6 — they have no
ASP.NET Core dependency and no opinion about HTTP at all. `Page<T>` and
`Cursor` live in `Trellis.Core` because they are pure data; the HTTP
projection (Link header, envelope) lives in `Trellis.Asp`.

---

## 1. Design Principles (AI-First Axioms)

These are the rules every package must obey. When a current API violates one, it gets redesigned.

### A. **One verb per concept. Aliases are bugs.**
If two methods do the same thing under different names (`SuccessIf` ≈ `Ensure`; `Recover` ≈ `RecoverOnFailure`; `Check` ≈ `Tap` returning `Result`), the LLM has to gamble. We pick one and delete the rest.

### B. **Async methods carry the `Async` suffix — BCL convention wins.**
**Decision (2026-04-20):** every async-returning extension is named `…Async` (`MapAsync`, `BindAsync`, `TapAsync`, `TapOnFailureAsync`, `EnsureAsync`, `MatchAsync`, `RecoverAsync`). This follows the BCL guideline for `Task`/`ValueTask`-returning methods and matches every async API the user already reads (`Task.WhenAllAsync`, `HttpClient.SendAsync`, `DbContext.SaveChangesAsync`). The earlier "async invisible" framing has been **rescinded** because it created two avoidable problems: (1) overload ambiguity between sync `Map` and `Task<Result<T>>.Map`, and (2) no visible signal at the call site that an `await` is required. The §3.3 overload matrix is updated accordingly: `Map` is one overload (sync × sync), `MapAsync` covers the five async permutations.

### C. **No tuple-arity ceilings in user-facing validation APIs.**
A builder pattern (`Validate.Field(a).And(b).And(c)…`) replaces fixed-arity `Combine` for **validation aggregation** (no ceiling). For heterogeneous value combination (different value types, first-failure-wins semantics), `Result.Combine<T1..T9>` stays — it's already T4-generated, costs nothing, and is genuinely cleaner than chained `Bind`s. `TRLS014` (max 9 hard-error) stays for `Combine` with a clearer message: "Group into a record or use `Validate.Field(...).Build()` if these are validation results."

### D. **Errors are a closed-by-convention catalog, not an open hierarchy.**
We **do not** claim compiler exhaustiveness — C# does not provide it for record hierarchies. Instead:
- The catalog is fixed by Trellis. Adding a case is a Trellis-level breaking change.
- An analyzer (`TRLS026`, new) flags any user code that derives from `Error` outside the Trellis assembly.
- `Match`/`MatchError` accept a typed dictionary of handlers; the analyzer warns when a known case is missing. This is "closed by convention + diagnostics," which is what C# can actually deliver.

### E. **One way to construct a value object.**
`TryCreate(...) → Result<T>` is the only validating path. `Create(...)` (throwing) is also generated by default — but `TRLS007` already steers users to the right one. We are honest: both exist and are documented.

### F. **No `IConvertible`. No `ToInt32(IFormatProvider?)` on an `EmailAddress`.**
Scalar VOs expose `Value`, `ToString()`, and (for numerics/dates only) `ToString(format, provider)`. Removing `IConvertible` is a real simplification.

### G. **The package you install names the API you write.**
If `Trellis.Asp` is installed, HTTP mapping is `Trellis.Asp`-prefixed. Each package's surface is self-describing.

### H. **The doc is the API.**
The per-package `docs/api_reference/trellis-api-*.md` is the canonical surface; it ships in every NuGet package and is generated from source where possible.

### I. **Diagnostics complement — not replace — boundary adapters.**
We keep `Result.Try(Func<T>, Func<Exception, Error>?)` and `Result.TryAsync` as explicit boundary adapters for foreign code (EF, HttpClient, BCL parsing, third-party). Analyzers (`TRLS015`) catch unguarded `throw` *inside* a chain — but they cannot catch exceptions from foreign code, which is exactly what `Try` exists for. Removing `Try` would force the AI to reinvent it badly.

### J. **AOT-clean where supported. No reflection in hot paths.**
Source generators do the work. The redesign treats AOT as a target, not a bonus.

---

## 2. Proposed Package Map

### Before / After: current packages → proposed packages

There are **17** publishable NuGet packages today in `TrellisFramework`. The redesign produces **12** (11 runtime + 1 tooling). Five existing packages are removed (3 absorbed via NuGet bundling, 2 merged into siblings) and one is renamed.

> **Out of scope: `Trellis.AspTemplate`.** The `dotnet new trellis-asp` template package lives in a **separate repository** (`C:\GitHub\Trellis\TrellisAspTemplate`) and is published independently as `Trellis.AspTemplate`. The redesign does not change its packaging or relocate it. Per §0.5 and §15 Phase 6, the template is **rewritten from scratch on v2 only after framework v2.0.0 ships** — Phases 1a–5a do not touch it.

| # | Current package (17) | Future package | Disposition |
|---|---|---|---|
| 1 | `Trellis.Analyzers` | `Trellis.Analyzers` | ✅ Kept; analyzer rules consolidate; diagnostic IDs renumbered to `TRLS001…` (no `TRLSGEN` range) |
| 2 | `Trellis.Asp` | `Trellis.Asp` | ✅ Kept; absorbs `Trellis.Asp.Authorization` (project merge) and bundles `Trellis.AspSourceGenerator` (inside the `.nupkg`) |
| 3 | `Trellis.Asp.Authorization` | — (merged into `Trellis.Asp`) | ❌ Removed; no separate NuGet — content moves into `Trellis.Asp` `.csproj` |
| 4 | `Trellis.AspSourceGenerator` | — (bundled inside `Trellis.Asp.nupkg`) | ❌ Removed as a standalone package; the `netstandard2.0` `.csproj` stays in the source tree under `Trellis.Asp/generator/` and ships as `analyzers/dotnet/cs/...` inside `Trellis.Asp.nupkg` |
| 5 | `Trellis.Authorization` | `Trellis.Authorization` | ✅ Kept unchanged; **does NOT absorb `Trellis.FluentValidation`** (would force a third-party transitive dep) |
| 6 | `Trellis.DomainDrivenDesign` | — (merged into `Trellis.Core` in Phase 2) | ❌ Removed; all **12 source files** (`Aggregate`, `Entity`, `ValueObject`, **`ScalarValueObject`**, `IAggregate`, `IEntity`, `IDomainEvent`, `Specification`, `AndSpecification`, `OrSpecification`, `NotSpecification`, `AggregateETagExtensions`) move into `Trellis.Core`. All types are already in the `Trellis` namespace, so user code is unaffected. **`ScalarValueObject` reconciliation:** the existing DDD `ScalarValueObject` and the proposed `Scalar<T,V>` (§4.1) are the same concept — Phase 2 unifies them as `Scalar<T,V>` in `Trellis.Core`. The DDD `ScalarValueObject` is **deleted outright** at v2.0.0 (no `[Obsolete]` alias; the template is being rewritten on v2 and there are no external consumers requiring a runway). Resolves the post-Phase-2 backward-dep tension where `Composite : ValueObject` would otherwise force `Trellis.Core` to reference `Trellis.DomainDrivenDesign`. The "swap out Trellis DDD for your own" capability is preserved (just don't inherit from `Trellis.Aggregate`); see §0.6 for the honest trade-off discussion. |
| 7 | `Trellis.EntityFrameworkCore` | `Trellis.EntityFrameworkCore` | ✅ Kept; bundles `Trellis.EntityFrameworkCore.Generator` inside the `.nupkg` |
| 8 | `Trellis.EntityFrameworkCore.Generator` | — (bundled inside `Trellis.EntityFrameworkCore.nupkg`) | ❌ Removed as a standalone package; the `netstandard2.0` `.csproj` stays in the source tree under `Trellis.EntityFrameworkCore/generator/` and ships inside the runtime `.nupkg` |
| 9 | `Trellis.FluentValidation` | `Trellis.FluentValidation` | ✅ **Kept separate and opt-in** — only consumers who want FluentValidation install it |
| 10 | `Trellis.Http` | `Trellis.Http` | ✅ Kept; surface slimmed (see §7) |
| 11 | `Trellis.Mediator` | `Trellis.Mediator` | ✅ Kept; remains a wrapper over `martinothamar/Mediator`; owned dispatcher deferred to optional Phase 7 |
| 12 | `Trellis.Primitives` | `Trellis.Primitives` | ✅ Kept; **scope reduced** — contains only the 13 concrete opinionated VOs (`EmailAddress`, `Money`, `PhoneNumber`, `CountryCode`, `CurrencyCode`, `LanguageCode`, `Hostname`, `IpAddress`, `Url`, `Slug`, `Percentage`, `Age`, `MonetaryAmount`) after Phase 2; base classes move to `Trellis.Core` |
| 13 | `Trellis.Primitives.Generator` | — (bundled inside `Trellis.Core.nupkg`) | ❌ Removed as a standalone package; the `netstandard2.0` `.csproj` stays in the source tree (likely moved under `Trellis.Core/generator/`) and ships inside `Trellis.Core.nupkg`. Rationale: the generator targets the `Scalar<T,V>` / `Composite` / `RequiredString` base classes which live in `Trellis.Core` post-Phase 2; bundling there means every consumer who derives from a base class gets the generator automatically. The generator is inert for code that doesn't use the base classes. |
| 14 | `Trellis.Results` | **`Trellis.Core`** | 🔄 **Renamed** from `Trellis.Results` to `Trellis.Core`. **Scope expanded** — Phase 1a rewrites Result/Maybe/Error in place; Phase 2 absorbs (a) scalar/composite VO base classes from `Trellis.Primitives` and (b) the entire `Trellis.DomainDrivenDesign` package; Phase 2 also bundles the Primitives generator. After Phase 2, `Trellis.Core` contains Result + Maybe + Error + tracing + VO base classes + DDD primitives (`Aggregate`, `Entity`, `ValueObject`, `Specification`, domain events). The new name reflects the package's role as the foundational core; api_reference filename becomes `trellis-api-core.md`. |
| 15 | `Trellis.Stateless` | **`Trellis.StateMachine`** | 🔄 Renamed (vendor-neutral name). Stateless types remain visible; thin wrapper unchanged. `LazyStateMachine` pattern included. |
| 16 | `Trellis.Testing` | `Trellis.Testing` | ✅ Kept unchanged; **does NOT absorb `Trellis.Testing.AspNetCore`** (would force ASP.NET Core test packages on console-app test projects) |
| 17 | `Trellis.Testing.AspNetCore` | `Trellis.Testing.AspNetCore` | ✅ **Kept separate and opt-in** — only ASP test projects install it |

**Summary:**
- **17 → 12 packages** in `TrellisFramework`.
- **3 NuGet packages removed via generator bundling:** `Trellis.AspSourceGenerator`, `Trellis.EntityFrameworkCore.Generator`, `Trellis.Primitives.Generator` (all stay as separate `netstandard2.0` `.csproj` projects in the source tree).
- **2 NuGet packages removed via project merge:** `Trellis.Asp.Authorization` (content moves into `Trellis.Asp`); `Trellis.DomainDrivenDesign` (content moves into `Trellis.Core` in Phase 2).
- **2 packages renamed:** `Trellis.Results` → `Trellis.Core`; `Trellis.Stateless` → `Trellis.StateMachine`.
- **2 packages explicitly NOT merged** to avoid forcing third-party transitive deps: `Trellis.FluentValidation`, `Trellis.Testing.AspNetCore`.
- **0 new packages** added in `TrellisFramework`. (`Trellis.AspTemplate` continues to be published from its own repo; not counted here.)

### Guiding principles

> **Principle: no forced third-party transitive dependencies.** Where a merger would push a third-party package (e.g., FluentValidation, ASP.NET Core test infrastructure) onto consumers who don't want it, the merger is rejected and the package stays separate and opt-in.
>
> **Source-generator packaging convention.** Roslyn source generators must target `netstandard2.0` (Visual Studio's Roslyn host runs on .NET Framework). Where a runtime package "absorbs" a generator, the generator's `.csproj` stays as a separate `netstandard2.0` project but ships **inside** the runtime package's `.nupkg` as an analyzer asset (`analyzers/dotnet/cs/...`). At the source-tree level there are still two `.csproj` files; at the NuGet level the user installs one package. This is the same convention `Microsoft.Extensions.Logging.Abstractions` uses for `LoggerMessageGenerator`.
>
> **Cross-repo coordination.** `TrellisAspTemplate` (separate repo, publishes `Trellis.AspTemplate`) is the AI's day-1 entry point. It is **not** kept green during Phases 1a–5a. The framework v2.0.0 ships against its own unit/integration tests and an internal v2-canary sample app (see §15 Phase 5a). Once framework v2.0.0 GA cuts, Phase 6 rewrites the template from a clean slate; this is a separate cross-repo PR with no shared migration document.

### Final 12 packages in TrellisFramework — purpose summary

| Runtime Package (11) | Purpose |
|---|---|
| `Trellis.Core` | Core ROP types (Result, Maybe, Error) + VO base classes + DDD primitives (`Aggregate`, `Entity`, `ValueObject`, `Specification`, domain events) + tracing. Zero third-party deps. (Was: `Trellis.Results` + `Trellis.DomainDrivenDesign` + Primitives base classes.) |
| `Trellis.Primitives` | 13 concrete opinionated VOs (`EmailAddress`, `Money`, `PhoneNumber`, `CountryCode`, `CurrencyCode`, `LanguageCode`, `Hostname`, `IpAddress`, `Url`, `Slug`, `Percentage`, `Age`, `MonetaryAmount`). |
| `Trellis.Asp` | ASP.NET Core integration (HTTP mapping, model binding, scalar validation, authorization providers, JSON gen). |
| `Trellis.EntityFrameworkCore` | EF Core integration (repository base, conventions, interceptors, generator). |
| `Trellis.Http` | `HttpClient` → `Result` extensions for ACL layer. |
| `Trellis.Mediator` | Mediator pipeline behaviors over `martinothamar/Mediator`. |
| `Trellis.Authorization` | Actor + resource authorization model. |
| `Trellis.FluentValidation` | Opt-in FluentValidation pipeline behavior. |
| `Trellis.StateMachine` | Thin wrapper over Stateless. (Was: `Trellis.Stateless`.) |
| `Trellis.Testing` | Result/Maybe/EF/actor/time/token test helpers. |
| `Trellis.Testing.AspNetCore` | Opt-in ASP.NET Core test client + `WebApplicationFactory` helpers. |

| Tooling Package (1) | Purpose |
|---|---|
| `Trellis.Analyzers` | Diagnostic rules (`TRLS001…099`). Source generators ship inside their respective runtime packages, not here. |

**Out-of-repo (separate repo, separate publishing):**
- `Trellis.AspTemplate` — `dotnet new trellis-asp` template package; published from `TrellisAspTemplate` repo.

---

## 3. Core Package — `Trellis.Core`

> **Naming note:** This package is renamed from `Trellis.Results` to `Trellis.Core` as part of the redesign. Rationale: after Phase 2 absorbs the scalar/composite VO base classes, the package is no longer "just Results" — it's the foundational core (Result + Maybe + Error + base classes + `EntityTagValue` + `RetryAfterValue` + tracing + `MaybeInvariant` + `Validate` builder). The `Trellis.Core` name reflects that role accurately, and avoids the misleading "Results" label. The api_reference filename also becomes `trellis-api-core.md`. The rename happens at the start of Phase 1a (the package contents are being rewritten anyway, so a NuGet-id change is paid in the same breaking-version bump to 2.0.0).
>
> **Scope expansion in Phase 2:** Phase 1a (this section) covers the Result/Maybe/Error rewrite. Phase 2 absorbs (a) the scalar/composite VO **base classes** (`Scalar<T,V>`, `Composite`, `RequiredString`, `RequiredGuid`, `RequiredInt`, `RequiredDecimal`, `RequiredEnum`) from `Trellis.Primitives` and (b) the entire `Trellis.DomainDrivenDesign` package (`Aggregate`, `Entity`, `ValueObject`, `Specification` family, `IDomainEvent`, `IAggregate`, `IEntity`, `AggregateETagExtensions`) into `Trellis.Core`, alongside the source-generator rewrite. Rationale: all absorbed types have zero third-party dependencies (same as Result/Maybe), are already in the `Trellis` namespace, and form a cohesive domain-modeling vocabulary. The DDD merge specifically resolves the otherwise-awkward backward-dependency where `Composite : ValueObject` would force `Trellis.Core` to reference `Trellis.DomainDrivenDesign`. After Phase 2, `Trellis.Primitives` contains only the opt-in concrete VOs, and the `Trellis.DomainDrivenDesign` package no longer exists.

### 3.1 `Result<T>` — minimal terminal API

```csharp
public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    // Non-throwing nullable accessor — retained because it powers clean pattern-match idioms
    // (`if (r.Error is { } e)`, `r.Error switch { Error.NotFound => ..., ... }`) that have no
    // safety downside (it never throws). Removing it for symmetry with the throwing `Value`
    // would have been a usability regression with no safety win.
    public Error? Error { get; }

    public bool TryGetValue([MaybeNullWhen(false)] out T value);
    public bool TryGetError([MaybeNullWhen(false)] out Error error);

    // Deconstruction is the only "extract both" path
    public void Deconstruct(out bool isSuccess, out T? value, out Error? error);
}
```

**Removed from the type itself:**
- `Value` property — it threw `InvalidOperationException` on failure and was the primary cause of `TRLS003`. Removed in v2 (ga-03). Use `TryGetValue`, `Match`, or `Deconstruct`.
- All implicit conversions from `T` and from `Error`. They look magical and AI gets the direction wrong.
- `IFailureFactory<TSelf>` static interface — **retained**. It is necessary for pipeline behaviors to construct typed failure responses without reflection while wrapping `martinothamar/Mediator`. The v1 plan's removal was conditioned on owning the dispatcher (deferred to optional Phase 7); under the wrapper model, the constraint stays. See §5.1 line 612 and the §16 score-table caveat.

**Retained (revised from earlier ADR drafts):**
- `Error` property is **kept**. It is nullable and never throws, so the original "they throw → primary cause of TRLS004" rationale only applies to `Value`. Removing `Error` would force `if (r.TryGetError(out var e))` everywhere a clean `if (r.Error is { } e) ...` pattern-match works today, breaking switch expressions (`r.Error switch { ... }`) and LINQ projections (`xs.Select(r => r.Error?.Code)`). The `TRLS004` analyzer continues to flag genuine misuse of `Error` (e.g., dereferencing without a null check or guard).

**Construction is one path:**
```csharp
Result.Ok(value);             // success: Result<T>
Result.Fail<T>(error);        // failure: Result<T>
Result.Ok();                  // success: Result (unit, no payload) — replaces Result<Unit>
Result.Fail(error);           // failure: Result (unit, no payload)
```

**Non-generic `Result` — surface parity with `Result<T>`.** `Result` (no generic) replaces `Result<Unit>` for "success-or-failure-without-payload". Its surface mirrors `Result<T>` minus the value-bearing pieces:

```csharp
public readonly struct Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    public bool TryGetError([MaybeNullWhen(false)] out Error error);

    public void Deconstruct(out bool isSuccess, out Error? error);
}
```

Pipeline-verb shapes for the non-generic case:
- `Result<U> Map<U>(Func<U> projector)` — unit-success → value-success projection. (`Map(Action)` is **not** provided; that would duplicate `Tap(Action)` and violate "one verb per concept".)
- `Result Bind(Func<Result> next)` — sequence another unit-returning step.
- `Result<U> Bind<U>(Func<Result<U>> next)` — sequence a value-returning step from a unit `Result`.
- `Result Tap(Action onSuccess)` — side-effect on success; no value to pass to the lambda.
- `Result TapOnFailure(Action<Error> onFailure)` — symmetric with `Result<T>`.
- `Result Ensure(Func<bool> predicate, Error error)` — guard with no value to inspect.
- `U Match<U>(Func<U> onSuccess, Func<Error, U> onFailure)` — branch projection without a value parameter on the success arm.
- `Result Recover(Func<Error, Result> next)` — recover from failure into another unit `Result`.
- `Result.Try(Action work, Func<Exception, Error>? map = null)` and `Result.TryAsync(Func<Task> work, ...)`.

Cross-shape `Result<T> → Result` is provided on `Bind` only: `Result<T>.Bind(Func<T, Result> next)` for "I have a value but the next step is unit-returning". The reverse (`Result.Bind<U>(Func<Result<U>>)`) is the unit→value cross-shape listed above.

Implicit conversion `Result<T> → Result` is **not** provided (consistent with §3.1's "no implicit operators" rule); explicit `result.AsUnit()` is provided for the legitimate need ("I called something value-bearing but only care about success/failure"). The `default(Result)` invariant is in §3.5.1.

**Honest cost — overload doubling.** Introducing a distinct non-generic `Result` rather than reusing `Result<Unit>` roughly doubles the verb overload count. The corrected per-verb matrix:

| Verb | `Result<T>` shapes | Unit/cross shapes added | Total shapes |
|---|---|---|---|
| `Map` | 1 (`T → U`) | 1 (`Result → Result<U>`) | 2 |
| `Bind` | 1 (`T → Result<U>`) | 3 (`Result → Result`, `Result → Result<U>`, `Result<T> → Result`) | 4 |
| `Tap` | 1 (`Action<T>`) | 1 (`Action`) | 2 |
| `TapOnFailure` | 1 (`Action<Error>`) | 1 (`Action<Error>` on `Result`) | 2 |
| `Ensure` | 1 (`Func<T,bool>`) | 1 (`Func<bool>`) | 2 |
| `Match` | 1 | 1 | 2 |
| `Recover` | 1 | 1 | 2 |
| **Total** | **7** | **9** | **16** |

Each shape × 6 async forms (§3.3) = **96 verb overloads** (vs ~42 if we kept `Result<Unit>`). `BindZip` (T4-generated 1..9 arity) and the LINQ surface get similar treatment for the unit cross-shapes, adding roughly another 30 overloads. We accept this cost because (a) every additional overload is T4-generated — write-once, regenerate-forever — so maintenance cost is near-zero; (b) at the call site the receiver type (`Result` vs `Result<T>`) filters the visible overloads to ≈12 per verb, so AI/IntelliSense never has to choose between unit and value shapes; (c) trim/AOT removes unused overloads from consumer apps; (d) the call-site ergonomics win (`Tap(() => log("done"))` instead of `Tap(_ => log("done"))`) and the elimination of the `Trellis.Unit` vs `Mediator.Unit` namespace collision are paid every day, while the "two types to learn" cost is paid once. The design's headline "small surface for AI ergonomics" is about *what the AI sees at a call site*, not about the absolute overload count in the assembly — and the call-site count goes *down*, not up.

**Naming decision: rename `Success`/`Failure` → `Ok`/`Fail`.** The current code uses `Result.Success(...)` and `Result.Failure<T>(...)`. v2 renames these to `Result.Ok(...)` and `Result.Fail<T>(...)`. Rationale:

- **Shorter at the call site.** Factory verbs are written and read constantly; the saved characters compound.
- **Matches the dominant modern functional tradition** (Rust `Ok`/`Err`, F# `Ok`/`Error`, `FluentResults` `Ok`/`Fail`). AI models trained on cross-language corpora produce `Ok`/`Fail` more readily, which directly serves the "AI-correctness" lens of this proposal.
- **Cleaner pairing with predicate properties.** `IsSuccess`/`IsFailure` read as questions; `Ok`/`Fail` read as commands. Keeping the *predicate* names long-form (they already exist; not renamed) and the *factory* names short is the right ergonomic split.
- **Cost is paid once.** Every call site in the framework, template, and consumer codebases changes — but the change is mechanical (Find & Replace `Result.Success` → `Result.Ok`, `Result.Failure` → `Result.Fail`), is the kind of thing a codemod does perfectly, and is paid in the same v2.0.0 breaking-version bump that already removes `.Value`/`.Error`, removes implicit operators, and replaces `Result<Unit>` with non-generic `Result`. Adding the rename to that list does not change the upgrade *shape*, only its scope.

The factory-method count drops from ~14 to ~3 *and* the survivors get shorter names. The drop comes from removing variants (`SuccessIf`, `FailureIf`, `SuccessIfAsync`, `FailureIfAsync`, `Success(Func<T>)`, `FromException`, `FromException<T>`, etc.), not from the rename. **No automated migration tool is shipped, and no `MIGRATION_v2.md` document is published** — the template is being rewritten on v2 and there are no external consumers. Rename mappings (`Success` → `Ok`, `Failure` → `Fail`) are documented in the relevant `trellis-api-*.md` "Breaking changes from v1" sections; the renames are mechanical enough that a 30-line `sed`/find-replace handles them.

`Unit` is removed from the public API. `Result` (no generic) represents "success or failure without payload". This eliminates the `Trellis.Unit` vs `Mediator.Unit` collision. **Consumer-visible impact:** every command/query handler returning `Task<Result<Unit>>` becomes `Task<Result>`; every controller action signature `ActionResult<Trellis.Unit>` (e.g., the template's `Delete` action) becomes `ActionResult` (or `IActionResult` where the projection is non-trivial). OpenAPI documents regenerate; client SDKs generated from the OpenAPI doc see a return-shape change. The Phase 6 template rewrite (§15) regenerates the OpenAPI document from the v2 surface — no v1 client-shape compatibility is offered.

**Important — `WriteOutcome<T>` is a first-class wrapper, not metadata.** Repositories that perform writes return `Result<WriteOutcome<T>>` where `WriteOutcome<T>` is a closed `record` hierarchy modeling RFC 9110 §9.3.4 write outcomes. The cases are:

```csharp
public abstract record WriteOutcome<T>
{
    private WriteOutcome() { }
    public sealed record Created(T Value, string Location, RepresentationMetadata? Metadata = null)             : WriteOutcome<T>;
    public sealed record Updated(T Value, RepresentationMetadata? Metadata = null)                              : WriteOutcome<T>;
    public sealed record UpdatedNoContent(RepresentationMetadata? Metadata = null)                              : WriteOutcome<T>;
    public sealed record Unchanged(T Value, RepresentationMetadata? Metadata = null)                            : WriteOutcome<T>; // NEW in v2 — see §6.2 for status mapping
    public sealed record Accepted(T StatusBody, string? MonitorUri = null, RetryAfterValue? RetryAfter = null)  : WriteOutcome<T>;
    public sealed record AcceptedNoContent(string? MonitorUri = null, RetryAfterValue? RetryAfter = null)       : WriteOutcome<T>;
}
```

The ASP layer reads the case to choose 200/201/202/204 (mapping table in §6.2). **`WriteOutcome<T>` moves from `Trellis.Asp` (its current location, see `Trellis.Asp/src/WriteOutcome.cs`) to `Trellis.Core`** as part of Phase 1a, because the Application layer's repository interfaces need to declare it as a return type — and Application cannot reference `Trellis.Asp` under Clean Architecture. The `Unchanged` case is the **only** addition vs v1 (everything else preserves the existing case set including the v1 202/Accepted support); `RepresentationMetadata` is moved alongside `WriteOutcome<T>` to `Trellis.Core` (it is referenced by every value-bearing case). This is a real cross-layer migration: every `IRepository` signature in every consumer codebase that today returns `Result<T>` for a write changes to `Result<WriteOutcome<T>>`. This was an error in v1 — there is nowhere on the bare `Result<T>` to store outcome metadata; we keep `WriteOutcome<T>` explicit *and* we move it to the layer that actually originates it.

### 3.2 The Seven Pipeline Verbs

The pipeline surface is seven verbs plus three retained companion forms (`BindZip` for value-preserving chains, LINQ query syntax for monadic composition, `Try`/`Traverse` for boundaries and collections). Every other current pipeline verb either is a special case of one of these or is moved to a builder/helper.

| Verb | Signature | Replaces |
|---|---|---|
| `Map` | `Result<U> Map<U>(Func<T, U>)` | `Map` |
| `Bind` | `Result<U> Bind<U>(Func<T, Result<U>>)` | `Bind` |
| `Tap` | `Result<T> Tap(Action<T>)` — runs only on success | `Tap`, `Check` (when used for success-side side effects) |
| `TapOnFailure` | `Result<T> TapOnFailure(Action<Error>)` — runs only on failure | `TapError` (FP tradition), `Check` (when used for failure-side side effects) |
| `Ensure` | `Result<T> Ensure(Func<T, bool>, Error)` | `Ensure`, `Check` (validation), `SuccessIf`, `FailureIf`, `EnsureAll` (via Validate builder) |
| `Match` | `U Match<U>(Func<T, U>, Func<Error, U>)` | `Match`, `MatchError`, `Switch`, `SwitchError` |
| `Recover` | `Result<T> Recover(Func<Error, Result<T>>)` | `Recover`, `RecoverOnFailure`, `MapOnFailure` (via `.Recover(e => Fail(map(e)))`) |

**Retained companion verbs (distinct surface, not aliases of `Bind`):**

| Verb | Signature | Why kept (not folded into `Bind`) |
|---|---|---|
| `BindZip` | `Result<(T,U)> BindZip<U>(Func<T, Result<U>>)` (and the T4-generated 2..9-arity overloads `Result<(T1,…,Tn,U)> BindZip(...)`) | Preserves prior pipeline values into a tuple. The "absorbed" alternative — `result.Bind(t => GetU(t).Map(u => (t, u)))` at every step — is genuinely uglier and fights AI generation. The tuple-accumulating chain `r.BindZip(GetCustomer).BindZip((order, customer) => GetInventory(...))` is the most readable shape for "I need all of these values to compute the next step" without inventing a record type per intermediate state. |
| `Select` / `SelectMany` / `Where` | LINQ query-syntax surface (T4-generated). `SelectMany` is the 3-arg projection-collector form `(this Result<T>, Func<T, Result<U>>, Func<T, U, V>) → Result<V>` — the form that actually enables `from x in r1 from y in r2(x) select combine(x,y)`. | Lets users compose `Result`-returning operations in C# query syntax. A real subset of the .NET community prefers query syntax for multi-step monadic composition (`from a in r1 from b in r2(a) from c in r3(a, b) select build(a, b, c)`), and the surface is cheap to maintain (Select is one line over Map; SelectMany is one line over Bind+Map; Where is one line over Ensure). Removing it forces those users back to `.Bind(...).Bind(...)` chains, which is a usability regression for no maintenance saving. |

**`Where` policy — error is generic by construction.** C# query syntax `where p` cannot supply a domain-meaningful `Error` because the language pins the lambda shape to `Func<T, bool>`. `Where` therefore returns a generic `Error.Unexpected("Result filtered out by predicate.")` failure when the predicate is false. This is documented as a deliberate trade-off (LINQ syntax convenience vs domain-meaningful errors), and a new analyzer **`TRLS030`** flags `Where` calls in production code with the message: "`Where` produces a generic `Unexpected` error. Use `.Ensure(predicate, domainError)` for production code; `Where` is intended for ad-hoc query syntax and tests." The analyzer is **info-severity by default** and can be promoted to warning per consumer policy. `Where` is retained because removing it breaks the LINQ query-syntax surface (no `where` clause) — the analyzer is the right tool for "available but discouraged".

**No `Tap`/`TapOnFailure` ambiguity by construction.** The two verbs have distinct names, so `result.Tap(x => ...)` is always success-side and `result.TapOnFailure(e => ...)` is always failure-side — no overload-resolution gamble at the call site, and `Result<Error>` (rare in practice) is unambiguous too. This is a deliberate *split* of what would otherwise be a single overloaded `Tap`: the AI-ergonomics cost of "one name does two things based on lambda parameter type" outweighs the verb-count saving. The naming follows the same logic that drives `Result.Ok`/`Fail` (§3.1): one verb, one effect, no inference required.

**Why not `OnSuccess`/`OnFailure` / `BindAlso` / `Peek` etc.?** A pure English-readability lens would prefer event-style names (`OnSuccess`, `OnFailure`) and self-documenting compounds (`BindAlso` for `BindZip`). We deliberately keep the FP-tradition names because **CSharpFunctionalExtensions migrants are an explicit audience for v2** (§0 — Trellis is a fork of CFE). `Tap`/`Bind`/`BindZip`/`Map`/`Ensure`/`Match`/`Recover` are the names CFE users already read fluently, the names every existing CFE code sample on the internet uses, and the names the FluentResults / F# / Rust ecosystems converge on. The one deliberate departure is `TapOnFailure` (rather than the FP-tradition `TapError`): the `OnFailure` suffix names the *condition* under which the side-effect runs, mirroring `IsFailure`/`IsSuccess` and the analyzer terminology used throughout this document, and removes any doubt at the call site about which side of the rail the lambda fires on. Renaming the rest for English-readability would force every migrating user (and every AI model trained on CFE-flavored corpora) through a translation layer for no functional gain. The readability cost is real but accepted: we pay it once via documentation (the public API docs explicitly gloss `Tap` as "side-effect on the success value", `TapOnFailure` as "side-effect on the error", `BindZip` as "bind and preserve the prior value into a tuple") rather than paying it forever via name churn.

**Recover signature:** the proposal keeps only `Recover(Func<Error, Result<T>>)`. The current code's simpler `Recover(Func<Error, T>)` (re-rail to success) is dropped; equivalent is `result.Recover(e => Result.Ok(map(e)))`. Documented in the migration notes.

**Boundary adapters retained, not collapsed into the seven:**
- `Result.Try(Func<T>, Func<Exception, Error>? map = null)` — exception → failure for foreign code.
- `Result.TryAsync(Func<Task<T>>, Func<Exception, Error>? map = null)` — async variant.

**Collection traversal retained, not collapsed:**
- `IEnumerable<T>.Traverse(Func<T, Result<U>>)` → `Result<IReadOnlyList<U>>`. Stops at first failure or accumulates field errors when the inner result is a `ValidationError` (configurable via overload).

**Deleted entirely:** `When`, `WhenAll`, `If`, `MapIf`, `CheckIf`, `Discard`, `Then`. (`Combine` becomes the `Validate` builder for >2 fields and a tuple helper for 2 fields.)

### 3.3 Async overload model — explicit specification

**Naming (decided 2026-04-20, see Axiom B):** sync verb is `Foo`; every Task/ValueTask-returning overload is `FooAsync`. There is no overload between `Foo` and `FooAsync`; resolution is by suffix, not by argument shape. This mirrors BCL convention, eliminates the sync/async overload-ambiguity trap, and gives the call site a visible `await` cue.

For each pipeline verb V, the user-facing API consists of one sync overload and five async overloads (taking `Map` as the example; others mirror):

```csharp
// Sync source × sync function
public static Result<U> Map<T,U>(this Result<T> r, Func<T,U> f);

// Async source (Task) × sync function
public static Task<Result<U>> MapAsync<T,U>(this Task<Result<T>> r, Func<T,U> f);

// Async source (ValueTask) × sync function
public static ValueTask<Result<U>> MapAsync<T,U>(this ValueTask<Result<T>> r, Func<T,U> f);

// Sync source × async function (Task)
public static Task<Result<U>> MapAsync<T,U>(this Result<T> r, Func<T, Task<U>> f);

// Async source × async function (Task)
public static Task<Result<U>> MapAsync<T,U>(this Task<Result<T>> r, Func<T, Task<U>> f);

// Async source (ValueTask) × async function (ValueTask)
public static ValueTask<Result<U>> MapAsync<T,U>(this ValueTask<Result<T>> r, Func<T, ValueTask<U>> f);
```

That is **1 sync + 5 async = 6 overloads per verb** (`Map`/`MapAsync`, `Bind`/`BindAsync`, `Tap`/`TapAsync`, `TapOnFailure`/`TapOnFailureAsync`, `Ensure`/`EnsureAsync`, `Recover`/`RecoverAsync`) and a slightly different shape for `Match`/`MatchAsync` (2 result-typed funcs). Total async-handling overloads ≈ **30 minimum, likely 40–50 in practice** once any deferred-error-factory variants of `EnsureAsync` (`Func<Error>` overloads) are included. The exact count is fixed in Phase 1a; the upper bound is expected to be ≤ 60 overloads across all seven verbs combined. **Trade-offs we accept:**
- Mixing `Task` and `ValueTask` on the same call (e.g., `ValueTask<Result<T>>` source + `Task<U>` function) requires an explicit conversion. This is a deliberate constraint — the alternative (cross-product = 12+ overloads per verb) creates resolution ambiguity.
- The library does not implicitly lift `Task<T>` (without `Result`) into a Result chain; users use `.MapAsync(_ => fooAsync())` or call `TryAsync` for exception-bearing async work.

Internal file layout follows the same suffix split: `Map.cs` holds the sync overload; `MapAsync.cs` holds the five async overloads. The Left/Right/Both file-naming sub-convention disappears.

### 3.4 Combining results — two distinct operations

Two operations with **different semantics**. Both exist; the choice is semantic, not ergonomic.

#### `Validate.Field(...).And(...).Build(...)` — validation aggregation

```csharp
// FirstName, LastName are user-authored scalars (not shipped by Trellis.Primitives).
Result<User> user = Validate
    .Field("firstName", FirstName.TryCreate(req.FirstName))
    .Field("lastName",  LastName.TryCreate(req.LastName))
    .Field("email",     EmailAddress.TryCreate(req.Email))
    .Field("age",       Age.TryCreate(req.Age))
    .Build((first, last, email, age) => User.Create(first, last, email, age));
```

- All field errors accumulated into a single `ValidationError` with `FieldErrors[]`.
- Tuple arities 2–4 keep lambda destructuring on `Build`.
- For arities ≥ 5, `Build` requires a record type or positional callback.
- **No arity ceiling.**
- Inputs assumed to be validation-shaped (`ValidationError` failures). Mixing in a `NotFoundError` would be a type/semantic mismatch.

#### `Result.Combine<T1..T9>(r1, r2, ...)` — heterogeneous value combination

```csharp
var result = Result.Combine(orderResult, customerResult, productResult)
    .Bind((order, customer, product) => CreateLineItem(order, customer, product));
```

- Combines `Result<T1>..<T9>` of any value types into `Result<(T1,..,T9)>`.
- **First-failure-wins semantics.** No aggregation across heterogeneous error types — a `NotFoundError` and a `ConflictError` have no useful combined HTTP/UX mapping.
- Error position is deterministic (first argument's failure wins).
- Subsequent results not evaluated for error aggregation; their value is observed only for parallel async via `Result.ParallelAsync`.
- T1..T9 surface kept (T4-generated, zero cost).
- `TRLS014` (T10 hard-error) **stays** with a clearer message steering users to record-grouping or `Validate`.

#### `Result.ParallelAsync<T1..T9>` — parallel async fan-in

Retained for parallel async work where you want both branches to execute even if one fails (for OpenTelemetry, logging, or compensating actions). Returns `Result<(T1..T9)>` with first-failure-wins on the *result*, but both/all branches run to completion.

#### `Traverse` — collection traversal

```csharp
Result<IReadOnlyList<Order>> = orders.Traverse(o => Order.TryCreate(o));
```

Stops at first failure (or accumulates field errors when the inner failure is a `ValidationError`, configurable via overload).

**Deleted entirely:** `When`, `WhenAll` (use `ParallelAsync`), `If`, `MapIf`, `CheckIf`, `Discard`. `AggregateError` is removed — `ValidationError` already aggregates field errors; for non-validation cases, first-failure-wins.

### 3.5 `Maybe<T>` — keep, trim, retain `MaybeInvariant`

#### 3.5.0 Why `Maybe<T>` survives alongside nullable reference types

The first question a v2 reader asks is "isn't `T?` plus NRT enough?" Answer: not quite, and the gap is what `Maybe<T>` earns its keep on:

1. **Value-type semantics with chaining.** `Maybe<int>` distinguishes "no value" from `0` cleanly, with `.Bind`/`.Map` chains. `int?` does too, but the moment the chain involves a reference type, `T?`-vs-`Maybe<T>` mixing breaks fluent composition. `Maybe<T>` is uniform across reference and value types.
2. **Explicit `None` in collection elements.** `IReadOnlyList<Maybe<TItem>>` says "every slot is present, but each slot may be empty" — the only alternative is `IReadOnlyList<TItem?>` which conflates "absent" with "default". Repository batch results legitimately need this.
3. **IL-level distinction from `null`-as-absence.** Application-layer repository signatures `Task<Maybe<TodoItem>> FindAsync(TodoId id)` document "this lookup may legitimately find nothing, and that is not an error". `Task<TodoItem?>` says the same thing in C# but doesn't survive reflection / serialization / logging boundaries the way a struct does.
4. **Pattern-match composition with `Result<T>`.** `result.Bind(t => repository.FindAsync(t.OwnerId))` works uniformly when `FindAsync` returns `Maybe<Owner>` and there's a `Result<Maybe<T>>` extension surface. Mixing `T?` into a Result chain forces null-checks back into user code.

`Maybe<T>` is **not** for "I might forget to handle null". That is what NRT + the compiler is for. It is for situations where absence is a first-class domain outcome that participates in further composition.

#### 3.5.1 `default(...)` invariants for Result and Maybe structs

Both `Result<T>` and `Maybe<T>` are `readonly struct`s. C# guarantees `default(SomeStruct)` will appear (uninitialized fields, generic instantiations, deserialization of older payloads). v2 specifies the invariants explicitly so an unintentional `default(...)` is well-defined, not a hidden failure mode:

- **`default(Result<T>)`** equals `Result.Fail<T>(Error.Unexpected("Result was default-initialized; use Result.Ok or Result.Fail."))`. `IsSuccess` is `false`; `IsFailure` is `true`; `TryGetValue` returns `false`; `TryGetError` yields the sentinel `UnexpectedError`. Rationale: silently defaulting to "success" would propagate uninitialized state through pipelines; defaulting to a typed failure makes the bug surface immediately on first `Match`/`TryGet`.
- **`default(Maybe<T>)`** equals `Maybe<T>.None`. `HasValue` is `false`; `TryGetValue` returns `false`; equality with `Maybe<T>.None` is `true`. Rationale: this matches user intuition — an uninitialized optional is empty — and is consistent with how `Nullable<T>` behaves.
- **`default(Result)`** (non-generic) equals `Result.Fail(Error.Unexpected("Result was default-initialized."))`. Same rationale as `Result<T>`.

A new analyzer `TRLS029` flags expressions that rely on `default(Result)`, `default(Result<T>)`, or `default(Maybe<T>)` outside of (a) framework-internal struct initialization paths and (b) test fixtures where defaulting is intentional. The analyzer message is "Did you mean: `Result.Ok(...)`, `Result.Fail(...)`, or `Maybe<T>.None`?".

Keep the type. Remove:
- Equality with raw `T` (currently implemented via `IEquatable<T>` in `Maybe{T}.cs` line 32 — removal breaks `someMaybe.Equals(rawValue)` call sites; documented in the `trellis-api-core.md` "Breaking changes from v1" section) and with `object`.
- Redundant `Or` overloads (collapsed to `Or(T fallback)` and `Or(Func<T> factory)`).
- Implicit `Maybe<T>(T value)`.

Retain (already in current code, no work in Phase 1a beyond confirming the surface):
- `Maybe.From<T>(T?)` — already exists for `T : notnull` with both class and struct constraint coverage; retained as-is.
- `IEquatable<Maybe<T>>` — already implemented.

**Retain** the entire `MaybeInvariant` family (`AllOrNone`, `Requires`, `MutuallyExclusive`, `ExactlyOne`, `AtLeastOne`). These encode cross-field rules that the `Validate` builder does not — they are domain-meaningful and AI-assistive. They live in `Trellis.Core`.

### 3.6 `Error` — closed-by-convention catalog (transport-agnostic)

> [!IMPORTANT]
> This section was speculative when written. The shipped V2 design diverged in several ways. The text below has been **replaced with the locked design**; the original speculative notes are preserved in PR7's checkpoint history (`session-state/checkpoints/015-pr7-error-redesign-locked-star.md`) for archeology.

**Final shipped design (V6):**

```csharp
public abstract record Error
{
    private Error() { }                                              // truly closed: only nested cases

    public abstract string Kind { get; }                             // IANA-aligned slug
    public virtual  string Code => Kind;                             // overridden when payload carries a ReasonCode/FaultId

    public string? Detail { get; init; }                             // free-form override
    public Error?  Cause  { get; init; }                             // structured chain; cycle-checked at init

    // Equality compares Kind + payload + Detail. Cause excluded (mirrors System.Exception precedent).
}

// 18 nested sealed records — full catalog:
//   BadRequest(ReasonCode, At?), Unauthorized(Challenges), Forbidden(PolicyId, Resource?),
//   NotFound(Resource), MethodNotAllowed(Allow), NotAcceptable(Available),
//   Conflict(Resource?, ReasonCode), Gone(Resource), PreconditionFailed(Resource, Condition),
//   ContentTooLarge(MaxBytes?), UnsupportedMediaType(Supported), RangeNotSatisfiable(CompleteLength, Unit),
//   UnprocessableContent(Fields, Rules), PreconditionRequired(Condition), TooManyRequests(RetryAfter?),
//   InternalServerError(FaultId), NotImplemented(Feature), ServiceUnavailable(RetryAfter?),
//   Aggregate(Errors)  // auto-flattens; disallows Cause
```

Key decisions vs the original speculation:

- **Catalog is truly closed at the language level** (private base ctor) — not "closed-by-convention enforced by analyzer". The C# compiler verifies exhaustive `switch` over `Error` for free; no `TRLS026`/`TRLS027` analyzers are needed.
- **No static factory methods.** Every call site writes `new Error.X(payload) { Detail = "..." }`. This eliminates a parallel API surface and forces typed-payload discipline.
- **Naming aligned to RFC 9110 / IANA slugs**: `Error.UnprocessableContent` (not "ValidationError"), `Error.ContentTooLarge` (not "PayloadTooLarge"), `Error.InternalServerError` (not "UnexpectedError"), `Error.TooManyRequests` (not "RateLimitError"). One vocabulary across domain → application → API.
- **`Aggregate` is a first-class case** (the original plan removed it) — auto-flattens nested aggregates at construction; disallows `Cause` (composition node, not a chain link).
- **`UnprocessableContent` carries both `Fields` (per-field violations) and `Rules` (cross-field invariants).** `FieldViolation` and `RuleViolation` use `ImmutableDictionary<string, string>? Args` for parameterized i18n messages — wire-stable strings, no `object?` bags.
- **`ResourceRef`, `InputPointer`, `EquatableArray<T>` ship in `Trellis.Results`.** `EquatableArray<T>` wraps `ImmutableArray<T>` to give records `SequenceEqual` semantics (arrays use reference equality by default in record `Equals`).
- **HTTP status mapping lives in `Trellis.Asp`** as a `TrellisAspOptions` dictionary (`MapError<TError>(int status)`). Core stays transport-agnostic; the wire renderer also synthesizes `ProblemDetails.Instance` from request URL + the typed payload's `ResourceRef`.
- **No live `Exception` on errors.** `Error.InternalServerError(string FaultId)` carries an opaque ID; rich diagnostics live in your log/telemetry indexed by `FaultId`. On 5xx responses, `Detail` is redacted at the wire boundary.
- **`Result.Error` became `public Error?`** (nullable, never throws) and `Result<Unit>` collapsed to non-generic `Result`. Full rationale: [`docs/docfx_project/adr/ADR-001-result-api-surface.md`](./ADR-001-result-api-surface.md).
- **SemVer policy:** adding a new case to the closed catalog is a major-version change (since `switch` is exhaustive at the language level — every consumer would need a new arm). The full catalog-evolution policy is documented in [`docs/docfx_project/api_reference/trellis-api-core.md`](../api_reference/trellis-api-core.md) under "Catalog evolution".


### 3.7 OpenTelemetry / tracing retained

`Trellis.Core` keeps the result-tracing surface intact:
- `ResultDebugSettings.EnableDebugTracing` — kept (note: thread-safe writes, not isolated; tests that need isolation use `AsyncLocal`).
- `ResultsTraceProviderBuilderExtensions.AddResultsInstrumentation` — kept.
- The `Result<T>` constructor's auto-`Activity.Current.SetStatus` behavior — kept.
- `RailwayTrackAttribute` / `TrackBehavior` for marking helpers as success-track or failure-track — kept.
- The pattern `using var activity = ...; result.LogActivityStatus();` for child activities — kept and documented.

`Trellis.Primitives` keeps `PrimitiveValueObjectTrace` (and the `AsyncLocal<ActivitySource>` test-isolation pattern).

---

## 4. `Trellis.Primitives` — opinionated VOs

### 4.1 The base classes

A scalar VO is declared by partial class + attribute; the generator produces everything:

```csharp
// User-authored scalar — `FirstName` is not shipped by Trellis.Primitives;
// it's an example of what a consumer writes against the Trellis.Core base classes.
[Trellis.Primitives.Scalar(Of = typeof(string))]
[Trellis.Primitives.StringLength(50, MinimumLength = 1)]
public partial class FirstName : Scalar<FirstName, string>;
```

The generator emits, **by default and consistently**:
- `static Result<FirstName> TryCreate(string?)`
- `static FirstName Create(string)` (throws on validation failure)
- `string Value { get; }`
- JSON converter
- EF converter
- `ToString()`, `Equals`, `GetHashCode`

The generator emits **opt-in via additional attributes**:
- `IComparable<FirstName>` (`[Comparable]`)
- `IFormattable.ToString(format, provider)` (`[Formattable]`, only valid for numeric/date `T`)

**No `IConvertible` ever. No implicit operator to primitive ever.**

### 4.2 The 13 built-in VOs

`Trellis.Primitives` ships **13 concrete opinionated VOs** covering common standards-based values: `EmailAddress` (RFC 5321), `Money` (composite of `MonetaryAmount` + `CurrencyCode`), `MonetaryAmount`, `CurrencyCode` (ISO 4217), `CountryCode` (ISO 3166-1 alpha-2), `LanguageCode` (BCP 47 / ISO 639), `PhoneNumber` (E.164), `Hostname` (RFC 1123), `IpAddress`, `Url`, `Slug`, `Percentage`, `Age`. Each is a `partial` declaration with attributes — the generator does the work, and these double as documentation for users writing their own primitives against the `Trellis.Core` base classes.

### 4.3 Structured VOs (e.g., `Money`)

`Money` is **not** a scalar — it has `Amount` and `Currency`. Structured VOs use a separate attribute:

```csharp
[Trellis.Primitives.Composite]
public partial class Money : ValueObject
{
    public decimal Amount { get; }
    public Currency Currency { get; }
    public static Result<Money> TryCreate(decimal amount, Currency currency) { ... }
}
```

Generator emits JSON shape, EF owned-type config, equality. No fake `Value` property.

---

## 5. `Trellis.Mediator` and `Trellis.Authorization` (revised scope)

The v1 plan to consolidate Mediator + Authorization + FluentValidation into a single `Trellis.Cqrs` package with an owned source-generated dispatcher is **demoted**. Reasons:

1. Owning a dispatcher is a multi-month project that does not unblock anything else.
2. The `martinothamar/Mediator` library is AOT-friendly and philosophically aligned (per the vision doc).
3. The bigger AI-correctness wins are in `Trellis.Core` and `Trellis.Asp`.

### 5.1 `Trellis.Mediator` — pipeline behaviors only

Keep as a thin package providing pipeline behaviors. The canonical pipeline (outermost → innermost) is **six Mediator-first-party behaviors plus one opt-in EF-Core behavior**:

1. `ExceptionBehavior` — catches unhandled exceptions, returns `UnexpectedError`.
2. `TracingBehavior` — OpenTelemetry spans.
3. `LoggingBehavior` — structured logs with duration and outcome.
4. `AuthorizationBehavior` — checks `[Authorize]` permissions via `IActorProvider`.
5. `ResourceAuthorizationBehavior` — *opt-in*; checks `IAuthorizeResource<T>` with loader caching (see §5.3). Inserted by `AddResourceAuthorization(...)` immediately before `ValidationBehavior` so the loaded resource is checked once per request.
6. `ValidationBehavior` — **unified validation stage**. Runs `IValidate.Validate()` when the message implements it AND every `IMessageValidator<TMessage>` registered in DI for the message, aggregating `Error.UnprocessableContent` failures (both `Fields` and `Rules`) into a single response. **External validation sources plug in here through `IMessageValidator<TMessage>` instead of occupying their own pipeline slot** — in particular, `Trellis.FluentValidation` contributes a `FluentValidationMessageValidatorAdapter<TMessage>` registered by `AddTrellisFluentValidation()`. Empty `UnprocessableContent` failures still short-circuit; calling `AddTrellisFluentValidation()` is idempotent.
7. `TransactionalCommandBehavior` — *opt-in*; lives in `Trellis.EntityFrameworkCore`. Wraps commands in `IUnitOfWork.CommitAsync`. Opt in via `AddTrellisUnitOfWork<TContext>()` after all other behavior registrations so it lands innermost (closest to the handler).

`AddTrellisMediator(...)` (or `MediatorOptions.PipelineBehaviors = ServiceCollectionExtensions.PipelineBehaviors.ToArray()` in the AOT path) registers the always-on behaviors (1–4 + 6) in this fixed canonical order. Users add their own behaviors but cannot reorder the built-ins (most production CQRS bugs come from reordering).

The `IFailureFactory<TSelf>` constraint stays — it is necessary to let pipeline behaviors construct a typed failure response without reflection. (v1 proposed removing it; that removal was conditioned on owning the dispatcher, which we are no longer doing.)

The Trellis-owned dispatcher is a **Phase 7** opt-in (see §15), behind a separate `Trellis.Mediator.Dispatcher` package. We will only build it if AI-eval data shows the foreign mediator's quirks meaningfully hurt code-generation correctness.

### 5.2 `Trellis.Authorization` — actor + resource auth retained explicitly

The current explicit resource-authorization model is load-bearing and AI-correctness-positive. **Keep** the contracts:
- `IActor` / `Actor` record (already good).
- `IActorProvider` — single abstraction users implement.
- `IAuthorize` marker interface on messages — kept (rejecting v1's "delete it; attributes are sufficient"). The marker is what the pipeline behavior dispatches on.
- `IAuthorizeResource<TResource>` — declares "this message authorizes against a resource of type T".
- `IIdentifyResource<TMessage, TId>` — extracts the resource id from the message.
- `IResourceLoader<TId, TResource>` — loads the resource (with caching).
- `[Authorize(Permissions = ...)]` attribute as syntactic sugar over `IAuthorize` for the simple permission-check case.

**Layering is the right framing** (per critique): attributes provide the simple ergonomic path; the explicit interfaces remain for resource-aware auth. We do not delete the explicit model.

**`Trellis.FluentValidation` stays as a separate, opt-in package** (consistent with §2 row 9 and the "no forced third-party transitive dependencies" principle). The `ValidationBehavior` in `Trellis.Mediator` discovers FluentValidation validators *only when* `Trellis.FluentValidation` is referenced — the discovery happens via a typed extension method on `IServiceCollection` that the FluentValidation package contributes (`AddTrellisFluentValidation()`). Consumers who do not install `Trellis.FluentValidation` get the `IValidate.Validate()` path only. The 3 standalone extension methods (`ToResult`, `ValidateToResult`, `ValidateToResultAsync`) remain in `Trellis.FluentValidation` for use outside the pipeline. **No folding.**

---

## 6. `Trellis.Asp` — one entry point, protocol semantics preserved

### 6.1 The verb

```csharp
return result.ToHttpResponse();           // success → 200 with body, failure → status from Error
return result.ToHttpResponse(opts);       // with metadata (ETag, Location, Vary, Last-Modified, ranges, Prefer)
```

Renamed from v1's `ToHttpResult` to `ToHttpResponse` to avoid collision with ASP.NET Core's `IResult` mental model.

This is **the** API for both Minimal and MVC. The MVC adapter wraps the returned `IResult` into an `ActionResult` for Controllers. No more parallel `ActionResultExtensions` / `HttpResultExtensions` / `…Async` quartets.

### 6.2 What's in `opts`

The options carry the protocol semantics — they are not generic metadata. (Correcting a v1 oversimplification.)

```csharp
result.ToHttpResponse(opts => opts
    .Created("/orders/123")              // 201 + Location
    .WithETag(o => o.ETag)               // strong ETag
    .WithLastModified(o => o.UpdatedAt)
    .Vary("Accept", "Accept-Language")
    .EvaluatePreconditions()             // honors If-Match / If-None-Match / If-Modified-Since
    .HonorPrefer());                     // honors Prefer: return=minimal/representation
```

For write operations, `WriteOutcome<T>` (defined in core, §3.1) drives status selection automatically (per RFC 9110, `304 Not Modified` is **only** valid for GET/HEAD; conditional writes use `200`/`204`/`412`):
- `Created` → 201 with `Location` header
- `Updated` → 200 (or 204 if `Prefer: return=minimal`)
- `UpdatedNoContent` → 204 (explicit "I have no body to return") — distinct from `Updated` + `Prefer: return=minimal`
- `Unchanged` → 200 with `Content-Location` (or 204 if `Prefer: return=minimal`) — `304` is **not** used for write methods
- `Accepted` → 202 with status body, `Location: <MonitorUri>` (when present), `Retry-After` (when present)
- `AcceptedNoContent` → 202 with no body, `Location: <MonitorUri>` (when present), `Retry-After` (when present)
- `PreconditionFailed` (returned when `If-Match` evaluation fails before mutation) → 412

`PartialContentHttpResult`, `Page<T>`, and `RepresentationMetadata` **stay as public types** in `Trellis.Asp` (constructed only in controllers per template usage — see §12.2). `EntityTagValue` and `RetryAfterValue` **stay in `Trellis.Core`** because the template's commands carry them as properties (`UpdateTodoCommand.IfMatchETags : EntityTagValue[]?`); `Trellis.Asp` provides only the HTTP-binding extensions (`ETagHelper.ParseIfMatch`, `RetryAfterValue.ToHttpHeaderValue()`).

### 6.3 What disappears

- `ActionResultExtensionsAsync` / `HttpResultExtensionsAsync` — async variants are absorbed via §3.3.
- `ToCreatedHttpResult` / `ToUpdatedHttpResult` — replaced by `opts.Created(...)` / driven by `WriteOutcome<T>`.
- `WriteOutcomeExtensions.ToHttpResult` — folded into `Result<WriteOutcome<T>>.ToHttpResponse()`.

### 6.4 Conditional requests

`ConditionalRequestEvaluator` becomes internal. `ETagHelper` and `If-*` parsing helpers stay public for advanced users who do not use the standard pipeline.

### 6.5 Scalar value validation

`AddTrellisAspValidation()` registers an MVC filter and a Minimal API endpoint filter via an `IStartupFilter`. Users do not call `app.UseScalarValueValidation()` or `.WithScalarValueValidation()` per-route — but those calls remain available for opt-out scenarios.

---

## 7. `Trellis.Http` — slim, but keep useful structure

Today's 60+ overloads collapse, but not as aggressively as v1 proposed:

```csharp
// Bridge: HttpResponseMessage -> Result<HttpResponseMessage>
public static Task<Result<HttpResponseMessage>> ToResult(
    this Task<HttpResponseMessage> response,
    Func<HttpStatusCode, Error>? statusMap = null);

// JSON deserialization
public static Task<Result<T>> ReadJsonAsync<T>(
    this Task<Result<HttpResponseMessage>> response,
    JsonTypeInfo<T> type,
    CancellationToken ct = default);

public static Task<Result<Maybe<T>>> ReadJsonMaybeAsync<T>(...);

// Convenience for the most common single-status checks (kept):
public static Task<Result<HttpResponseMessage>> HandleNotFound(this Task<HttpResponseMessage> r, NotFoundError e);
public static Task<Result<HttpResponseMessage>> HandleConflict(this Task<HttpResponseMessage> r, ConflictError e);
public static Task<Result<HttpResponseMessage>> HandleUnauthorized(this Task<HttpResponseMessage> r, UnauthorizedError e);
```

Reasoning: a *fully* generic `statusMap` function is the right primitive, but the 3-4 most-common single-status helpers stay because they make AI code more readable and less error-prone for the obvious cases.

The `HandleFailureAsync<TContext>(...)` overloads with their `(response, ctx, ct) → Task<Error>` callback are removed (the `TContext` channel is anti-pattern; closures are fine).

---

## 8. `Trellis.EntityFrameworkCore` — pragmatic improvements, no magic

### 8.1 Convention registration

`ApplyTrellisConventions(params Assembly[])` stays as the runtime API (it works, it's well-understood, and EF model building is already reflection-heavy so the AOT cost is unchanged).

A **new** source generator emits `ApplyTrellisConventionsFor<TContext>()` that walks the `DbContext`'s `DbSet<>` properties at compile time and emits explicit converter registrations. Users can pick either:
- `ApplyTrellisConventions(typeof(MyDbContext).Assembly)` — runtime scan (works today; ergonomic; reflection cost at startup only)
- `ApplyTrellisConventionsFor<MyDbContext>()` — generated, reflection-free convention registration; EF Core itself remains outside Trellis' NativeAOT support promise

The v1 promise of "no assembly scanning ever" is dropped — it's a 6-month project to do it well across all real EF scenarios. This dual-path is honest.

### 8.2 Repository

`RepositoryBase<TAggregate, TId>` keeps its current shape — it's good. Changes:
- `SaveChangesResultAsync` and `SaveChangesResultUnitAsync` stay as `DbContext` extensions for non-pipeline scenarios.
- `IUnitOfWork.CommitAsync()` is the canonical commit for pipeline scenarios.
- `TRLS015` (warning today: prefer `SaveChangesResultAsync` over `SaveChangesAsync`) is **kept as warning**, not escalated to error. Some scenarios legitimately call `SaveChangesAsync`.

### 8.3 Maybe properties + indexes

`HasIndex(x => x.MaybeProp)` magic rewriting is **dropped** (per critique: low ROI, high fragility). Instead:
- `TRLS016` (current warning) stays, telling users to use `HasTrellisIndex` or string-based `HasIndex`.
- The `HasTrellisIndex` extension is the recommended path, documented prominently.

### 8.4 Diagnostics retained

`TrellisPersistenceMappingException`, `MaybePropertyMapping` (debug helper), and EF debug-string helpers all stay public. They are the operational story for diagnosing EF mapping issues — dropping them would worsen the AI's ability to help users debug.

### 8.5 ETag

Aggregate ETag generation stays in the EF interceptor (`AggregateETagInterceptor`). `IAggregate.ETag` exposes a `string`. The richer `EntityTagValue` type lives in **`Trellis.Core`** (per §12.2 and §3.6) — the Application layer needs it for command preconditions like `UpdateTodoCommand.IfMatchETags : EntityTagValue[]?` and cannot reference `Trellis.Asp`. `Trellis.Asp` provides only the HTTP-binding extensions (`ETagHelper.ParseIfMatch` for inbound headers; `EntityTagValue.ToHttpHeaderValue()` for outbound).

---

## 9. `Trellis.StateMachine` — thin wrapper, renamed only

Per critique: the v1 plan to "hide Stateless behind `IStateMachine<TState, TTrigger>`" is a trap. We either reimplement Stateless (huge) or ship a crippled abstraction users escape (worse than today).

**Revised plan:**
- Rename `Trellis.Stateless` → `Trellis.StateMachine` for vendor independence in the *name*, not the *implementation*.
- Keep the public surface essentially identical:
  - `StateMachine<TState, TTrigger>.FireResult(...)` extension.
  - `LazyStateMachine<TState, TTrigger>` for aggregate-materialization scenarios.
- The Stateless types remain visible in user code. We document them.
- Future work (post-redesign) may add a thin `IStateMachine<TState, TTrigger>` interface for testing seams, but only after evidence of demand.

---

## 10. `Trellis.Testing` — keep two packages, expand surface

`Trellis.Testing` and `Trellis.Testing.AspNetCore` **stay as two packages** (consistent with §2 rows 16–17 and §0.6.1's test-project guidance). An earlier draft proposed merging them with "conditional compilation activated when the consumer references `Trellis.Asp`"; that mechanism is not actually supported by NuGet/MSBuild — a package's compiled assembly is fixed at pack time and cannot have methods that exist or don't based on the consumer's other package references. Forcing `Microsoft.AspNetCore.Mvc.Testing` onto every test project (including console-app and worker-service tests) is the only way a merged package would compile, and that is precisely the third-party-transitive-dependency problem §2 rules out.

**Retain in full** in `Trellis.Testing`:
- FluentAssertions extensions for `Result<T>` and `Maybe<T>`.
- `FakeRepository<TAggregate, TId>` and in-memory test doubles.
- `ActorScope` / actor-substitution helpers.
- `FakeTimeProvider` integration.
- EF DB-provider replacement helpers.
- Entra / MSAL test-token providers.

**Retain in full** in `Trellis.Testing.AspNetCore`:
- ASP test client helpers.
- `WebApplicationFactory<TEntry>`-based fixtures.

**Add** (to `Trellis.Testing`):
- `result.ShouldBeFailureOf<ValidationError>()`.
- `result.ShouldHaveFieldError("email")`.
- `aggregate.ShouldHaveRaised<OrderPlaced>()`.

---

## 11. `Trellis.Analyzers` + Generators — unified

All compile-time tools ship as a single analyzer package. Diagnostic IDs are renumbered into one range (`TRLS001`–`TRLS099`). The `TRLSGEN` namespace disappears.

**Diagnostic IDs are public constants.** The analyzer package ships a `public static class TrellisDiagnosticIds` with `public const string` fields per rule (e.g., `UnusedResult = "TRLS024"`, `OutOfOrderBehavior = "TRLS025"`, `OpenErrorHierarchy = "TRLS026"`, `MatchMissingCases = "TRLS027"`). User code (and AI codegen) references them in `[SuppressMessage("Trellis", TrellisDiagnosticIds.UnusedResult)]` instead of typo-prone string literals. The `trellis-api-analyzers.md` documents both the constant and the ID side-by-side.

**Analyzer messages are codefix-ready.** Every diagnostic with a fixable cause is phrased as `<problem>. Did you mean: '<corrected snippet>'?` (e.g., `TRLS024`: "`Result<T>` returned by `GetTodoAsync` is unused. Did you mean: `var result = await GetTodoAsync(...); if (result.IsFailure) return result;`?"). This optimizes the diagnostic stream that AI assistants ingest — the model gets the fix in the message, not just a problem statement. Where a Roslyn `CodeFixProvider` is feasible, ship one; the message form applies regardless.

**Renumbering is a real consumer-facing break.** In v1 the `TRLSGEN` namespace contained 8 in-use IDs: `TRLSGEN001`–`TRLSGEN004` (Primitives generator) and `TRLSGEN100`–`TRLSGEN103` (EFCore generator). Phase 5a unified them into the analyzer `TRLS###` range (now done in commit `ga-06`):

1. Reserved ID assignments at v2.0.0: `TRLS001`–`TRLS029` (analyzers, with `TRLS003`/`TRLS004`/`TRLS005`/`TRLS007`/`TRLS013`/`TRLS025` retired as documentation tombstones — see ga-05), `TRLS031`–`TRLS038` (renumbered generator IDs). Final mapping: `TRLSGEN001`→`TRLS031`, `TRLSGEN002`→`TRLS032`, `TRLSGEN003`→`TRLS033`, `TRLSGEN004`→`TRLS034`, `TRLSGEN100`→`TRLS035`, `TRLSGEN101`→`TRLS036`, `TRLSGEN102`→`TRLS037`, `TRLSGEN103`→`TRLS038`. A new public `Trellis.TrellisDiagnosticIds` constants class (in `Trellis.Analyzers`) is the canonical reference; consumers should use `[SuppressMessage("Trellis", TrellisDiagnosticIds.X)]` rather than magic strings.
2. The old→new mapping is documented in `trellis-api-analyzers.md` and `trellis-api-efcore.md`. **No codemod, no suppression-alias retention, no separate `MIGRATION_v2.md`** — the template is being rewritten on v2 and there are no external consumers whose `.editorconfig` / `#pragma warning disable` / `[SuppressMessage]` need to keep working. Clean cut at v2.0.0.

New analyzers the redesign enables:
- `TRLS023`: `Create(value)` is called outside a constructor / static initializer / test (signals likely missed `TryCreate`).
- `TRLS024`: A `Result<T>` is returned by reference (likely to be ignored).
- `TRLS025`: A pipeline behavior is registered out of the canonical order.
- `TRLS026`: A user type derives from `Trellis.Error` outside the Trellis assembly (closed-by-convention enforcement).
- `TRLS027`: A `Match`/`MatchError` call is missing handlers for known error catalog cases.

---

## 12. Cross-Cutting Decisions (resolved)

### 12.1 Namespace strategy
- **`Trellis`** for everything in `Trellis.Core` (which after Phase 2 includes Result/Maybe/Error + scalar/composite VO base classes from former Primitives + DDD primitives `Aggregate`/`Entity`/`ValueObject`/`Specification` from former DomainDrivenDesign).
- **`Trellis.Primitives`** for opt-in concrete VOs only (so users can shadow with their own).
- **One namespace per integration package**, matching the package name.
- Sub-namespaces (`Trellis.Asp.ModelBinding`, `Trellis.Asp.Validation`, `Trellis.Authorization.Resources`) are flattened. Flat is more discoverable for AI.

### 12.2 HTTP-related value types — RESOLVED with layer analysis grounded in `TrellisAspTemplate`

The template (`C:\GitHub\Trellis\TrellisAspTemplate`) demonstrates how these types flow through real Clean Architecture layers. The decision is grounded in observed usage, not abstract preference.

**Observed usage in the template:**
- `UpdateTodoCommand` (Application layer) declares `public EntityTagValue[]? IfMatchETags { get; }` as a *command property*. The command carries the optional preconditions through Mediator from API → Application.
- `UpdateTodoCommandHandler` (Application layer) calls `.OptionalETag(command.IfMatchETags)` to evaluate the precondition — Application owns the comparison, not just the API.
- `TodosController` (API layer) parses headers via `ETagHelper.ParseIfMatch(Request)` (HTTP-specific) and formats output via `EntityTagValue.Strong(todo.ETag).ToHeaderValue()`.
- `RepresentationMetadata.WithStrongETag(todo.ETag)` is only ever constructed in the controller.

This rules out my v2 decision to move `EntityTagValue` to `Trellis.Asp`: the Application layer cannot reference `Trellis.Asp` under Clean Architecture, and the template's command shape is the right pattern (commands declare their preconditions explicitly).

**Revised split by layer affinity:**

| Type | Constructed by | Used by | Layer | Package |
|---|---|---|---|---|
| `RetryAfterValue` | ACL (translating upstream 429/503), Application (quota decisions) | API (response header), Application (retry/fallback policies) | All layers | **`Trellis.Core`** |
| `EntityTagValue` | API (parsing `If-Match` headers; wrapping `IAggregate.ETag` for responses); Application (carried as command/query property for conditional ops) | API (response headers, conditional request evaluation); Application (precondition checks via `OptionalETag(...)`) | API + Application | **`Trellis.Core`** |
| `RepresentationMetadata` | API only (assembling `Vary`, `Last-Modified`, `ETag` for response shape) | API only | API only | **`Trellis.Asp`** |

**Why `EntityTagValue` is in `Trellis.Core`:** Per the template, commands carry `EntityTagValue[]?` properties for conditional updates and handlers compare ETags via `OptionalETag(...)`. Application code therefore needs the type, but Application cannot reference `Trellis.Asp`. The pure value type (parsing/formatting per RFC 9110 weak/strong ETags) is framework-agnostic — only the HTTP `If-Match`/`If-None-Match` *header parsing* and `ETag` *response header writing* are API-only.

```csharp
namespace Trellis;

public readonly record struct EntityTagValue
{
    public string Value { get; }
    public bool IsWeak { get; }
    public static EntityTagValue Strong(string value);
    public static EntityTagValue Weak(string value);
    public static bool TryParse(string headerToken, out EntityTagValue tag); // RFC 9110 token form
    public string ToHeaderValue();   // serialization is framework-agnostic; the *header name* is HTTP-specific
}
```

API-layer extensions in `Trellis.Asp` provide the HTTP-binding helpers:
```csharp
namespace Trellis.Asp;
public static class ETagHelper
{
    public static EntityTagValue[]? ParseIfMatch(HttpRequest request);
    public static EntityTagValue[]? ParseIfNoneMatch(HttpRequest request);
}
```

The Application-layer `OptionalETag(...)` extension ships in `Trellis.Core` (it operates on `Result<T>` where `T : IAggregate`).

**Why `RetryAfterValue` is in `Trellis.Core`:** Same reasoning as v2 — it is a property of `RateLimitError` / `ServiceUnavailableError` / `ContentTooLargeError` constructed by ACL/Application. RFC 7231 header serialization is an extension in `Trellis.Asp`:
```csharp
namespace Trellis.Asp;
public static class RetryAfterValueHttpExtensions
{
    public static string ToHttpHeaderValue(this RetryAfterValue v);
}
```

**Why `RepresentationMetadata` stays in `Trellis.Asp`:** Per the template, it is *only* constructed inside controllers (`RepresentationMetadata.WithStrongETag(...)`) when the API decides what to put in the response. It bundles HTTP-specific concepts (`Vary` headers, `Last-Modified`, conditional-eval policies) — there is no Application-layer use case in the template.

**Layer reference rules** (canonical, documented in template's `.github/copilot-instructions.md`):
- **Domain** → references `Trellis.Core` (Result + Maybe + Error + DDD primitives + VO base classes — single package after Phase 2), `Trellis.Primitives` (optional concrete VOs), `Trellis.StateMachine` (optional). Never references `Trellis.Asp`, `Trellis.Http`, `Trellis.EntityFrameworkCore`, `Trellis.Mediator`.
- **Application** → references everything Domain references + `Trellis.Mediator`, `Trellis.Authorization`. May use `EntityTagValue` and `RetryAfterValue` (both in `Trellis.Core`). Never references `Trellis.Asp` or `Trellis.EntityFrameworkCore`.
- **ACL** → references everything Application references + `Trellis.Http`, `Trellis.EntityFrameworkCore`. Never references `Trellis.Asp`. Constructs `RateLimitError(..., RetryAfter: ...)` from upstream HTTP responses.
- **API** → references everything. Owns `Trellis.Asp` consumption. Parses `If-Match` via `ETagHelper.ParseIfMatch(Request)` into `EntityTagValue[]?` and passes it into commands; writes ETag/RetryAfter response headers via the `Trellis.Asp` extensions.

### 12.3 Documentation contract
- `docs/api_reference/trellis-api-*.md` is generated from source by an analyzer-time tool (Roslyn-driven).
- Each NuGet package ships its own `trellis-api-*.md` file as content (already done via `Trellis.ApiReference.targets` — keep that).
- The `copilot-instructions.md` for the template package references the generated docs by relative path — they always match the installed version.

### 12.4 Versioning, deprecation, target framework, and library defaults
- All packages versioned together (already done via Nerdbank.GitVersioning).
- **No `MIGRATION_v<N>.md` document.** The "major version bumps require a migration doc" CI gate is removed. Breaking changes are documented inline in the affected `trellis-api-*.md` files under a "Breaking changes from v<N-1>" section. Rationale: a single mega-doc rots; per-area docs stay accurate because they live next to the API they describe.
- **Deprecation policy for v1→v2.** The "no backward-compat constraint" framing (line 4) is taken literally: v2.0.0 is a **hard, clean breaking release**. Removed APIs are *not* retained as `[Obsolete]` shims, type-aliases, or suppression-aliases at the v2.0.0 line — they are gone. This includes `ScalarValueObject` (deleted, not aliased to `Scalar<T,V>`), `AggregateError` (deleted), the `TRLSGEN###` analyzer IDs (renumbered with no suppression-alias retention), and the old `Result.Success`/`Failure` factories. The template is being rewritten on v2 in `TrellisAspTemplate`, and there are no external consumers requiring a migration runway.
- **Parallel shipping.** v1.x packages remain available (not deleted) on NuGet for archival/pinning purposes, but are unlisted at the v2.0.0 release with a redirect notice. There is no overlapping support window.
- **NuGet listing fates** at v2.0.0:
  - `Trellis.Results` → unlisted with redirect notice to `Trellis.Core`.
  - `Trellis.Stateless` → unlisted with redirect notice to `Trellis.StateMachine`. **No metapackage redirect** — clean cut, consistent with the rest of the deprecation policy.
  - `Trellis.DomainDrivenDesign` → unlisted with "moved into `Trellis.Core`" notice.
  - `Trellis.Asp.Authorization` → unlisted with "merged into `Trellis.Asp`" notice.
  - `Trellis.AspSourceGenerator`, `Trellis.EntityFrameworkCore.Generator`, `Trellis.Primitives.Generator` → unlisted with "now bundled inside the runtime package" notice.

### 12.5 Target framework and language commitments

Stated once for the whole framework so individual packages don't re-decide:

- **Target framework:** **`.NET 10` only** (latest LTS, GA November 2025). No multi-targeting. Source-generator packages target `netstandard2.0` per Roslyn requirements; everything else is single-TFM.
- **Language version:** `<LangVersion>latest</LangVersion>`. Every Trellis package compiles with the most recent C# the target framework supports. v2 commits to using C# 11+ `required` members, C# 12 collection expressions and primary constructors, C# 13 features as appropriate. AI codegen targeting Trellis can assume modern C# throughout.
- **Project defaults across the framework solution** (enforced via `Directory.Build.props`):
  - `<Nullable>enable</Nullable>`
  - `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  - `<ImplicitUsings>disable</ImplicitUsings>` — `using` directives are explicit; AI codegen and human readers see all dependencies at the top of the file.
  - `<AnalysisLevel>latest-recommended</AnalysisLevel>`
  - `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`

### 12.6 AOT / trim / reflection stance

v2 framework code is **AOT-friendly and trim-safe**. No runtime reflection in hot paths — Mediator pipeline behaviors, the `Result` pipeline, EFCore conventions, and analyzer/generator outputs all rely on source-generators or compile-time wiring. Specifically:

- **No `Activator.CreateInstance`, no `Type.GetMethod(...).Invoke` in request paths.** If a future feature appears to need it, the answer is "ship a source-generator instead", which is also the rationale for the optional Phase 7 `Trellis.Mediator.Dispatcher`.
- **Every non-EF runtime package declares `<IsTrimmable>true</IsTrimmable>` and `<IsAotCompatible>true</IsAotCompatible>`.** `Trellis.EntityFrameworkCore` explicitly opts out because EF Core NativeAOT support is still experimental. CI fails if an AOT-supported package emits IL2026/IL2070/IL3050 trim/AOT warnings.
- **Ban on `dynamic`, `[Obsolete]`-after-v2.0, and unconstrained generic reflection in hot paths.** Tested via a custom analyzer in Phase 5a (`TRLS028`).

This is an AI-correctness positive: the model has fewer "does this method exist at runtime?" mysteries to model. It is also a future-proofing positive: modern .NET hosting (Native AOT, single-file, container slimming) is fully supported without per-package retrofits.

### 12.7 Ecosystem defaults

Three perennial debates closed off so the template (Phase 6) and framework (Phases 1a–5a) don't re-decide:

- **JSON:** `System.Text.Json` only, with source-generation (`JsonSerializerContext`) for any framework type that crosses a serialization boundary. No Newtonsoft. AI codegen and the controllers default to `STJ`-attributed types.
- **Logging:** `Microsoft.Extensions.Logging.ILogger<T>` only. **No owned `ITrellisLogger` abstraction.** Source-generated logger methods (`[LoggerMessage]`) are the convention for hot paths.
- **Configuration:** `Microsoft.Extensions.Options.IOptions<T>` / `IOptionsSnapshot<T>` / `IOptionsMonitor<T>` for strongly-typed config. **No owned `ITrellisConfig`.** Validators use FluentValidation's `IValidator<T>` registered alongside `Configure<T>(...)` (consistent with §5.2's "FluentValidation stays opt-in").

---

## 13. What This Buys an LLM (honest accounting)

| Item | Today | After |
|---|---|---|
| Pipeline verbs to choose from | ~25 (Map/Bind/Tap/Ensure/Check/CheckIf/Match/MatchError/Recover/RecoverOnFailure/MapIf/MapOnFailure/When/WhenAll/SuccessIf/FailureIf/Discard/Combine/BindZip/Traverse/Switch/SwitchError/…) | **7 verbs + BindZip + LINQ query syntax + Try + Traverse + Validate builder** (~12 distinct concepts) |
| Async file naming variants visible to user | `Base / Left / Right` per verb | **none visible** (exact internal overload model: §3.3) |
| `Result.*` factory methods | ~14 | **3** (`Ok`, `Fail<T>`, `Ok()`) + `Try`, `TryAsync` for boundary code |
| Error catalog | 19 types (open hierarchy) | **17 cases** (closed-by-convention; analyzer-enforced; no compiler-exhaustiveness overclaim — `AggregateError` removed, see §3.6) |
| Packages | 17 | **12** (11 runtime + 1 tooling) — `Trellis.AspTemplate` continues to ship from its own repo |
| ASP entry points for happy-path response | `ToActionResult` / `ToHttpResult` / `ToCreatedHttpResult` / `ToUpdatedHttpResult` / `WriteOutcome.ToHttpResult` / `PartialContentHttpResult` × sync/async | **1** (`ToHttpResponse` with options) |
| HTTP-client status helpers | ~16 method names × 4 input shapes (~60 overloads) | **5 named methods**, single `statusMap` parameter on the generic one |
| Combine arity ceiling | 9 (hard error TRLS014) | **`Validate` builder: no ceiling** for validation aggregation; **`Combine<T1..T9>`: T1..T9 retained** for heterogeneous combination (first-failure-wins); TRLS014 stays at T10 with clearer message |
| VO base classes the AI must distinguish | scalar / symbolic / composite / optionality wrapper | **2** (`Scalar`, `Composite`) — symbolic is `Scalar<T,Enum>`; optionality is `Maybe<T>` |
| Tracing / OTel surface | rich, dispersed | **same surface**, documented as canonical (no loss) |
| Resource authorization | `IAuthorize` + `IAuthorizeResource<T>` + `IIdentifyResource<,>` + `IResourceLoader<,>` | **same model** + attribute sugar for the simple case (no loss) |

---

## 14. Decisions (was: Open Questions) — RESOLVED

All six questions in v2 are now decided. The per-package implementation plans build on these answers.

| # | Question | Decision | Rationale |
|---|---|---|---|
| 1 | `Trellis.Mediator`: own the dispatcher now, or wrap `martinothamar/Mediator`? | **Wrap now; defer owned dispatcher to optional Phase 7** | Owning dispatch is 3–6 months of foundational work that buys only ~2 small AI-correctness wins (drop `IFailureFactory<TSelf>` constraint; first-class `Result<T>` handler shape). Both are nice but not transformative. The bigger AI wins are in Core/Error/Asp/Http. Phase 7 revisits this *only if* AI-eval data justifies it. |
| 2 | Remove `Result<T>.Value` and `Result<T>.Error` properties entirely? | **Yes — remove entirely** | They are the single biggest source of `TRLS003`/`TRLS004` warnings. `TryGetValue` / `TryGetError` / `Deconstruct` / `Match` cover every legitimate use. AI generates safer code by default when the unsafe path doesn't exist. |
| 3 | Move HTTP-protocol value types out of core? | **Split by observed usage in `TrellisAspTemplate`:** `RepresentationMetadata` moves to `Trellis.Asp` (constructed only in controllers). **`EntityTagValue` and `RetryAfterValue` stay in `Trellis.Core`** — the template's `UpdateTodoCommand.IfMatchETags : EntityTagValue[]?` and `OptionalETag(...)` handler call show `EntityTagValue` flowing through the Application layer; `RetryAfterValue` is a property of error records the ACL constructs. HTTP-specific *parsing* (`ETagHelper.ParseIfMatch`) and *header formatting* (`RetryAfterValue.ToHttpHeaderValue`) are extensions in `Trellis.Asp`. `IAggregate.ETag` stays as `string` in `Trellis.Core` (after the Phase 2 DDD merge). | The template is the canonical AI starting point — its layer usage is the design constraint. Conditional preconditions are an Application-layer concern (commands declare their own preconditions); response shape is an API concern. See §12.2 for the full layer rules. |
| 4 | `Trellis.StateMachine` — wrap or abstract Stateless? | **Thin wrapper, rename only. No `IStateMachine<TState, TTrigger>` abstraction.** | Reimplementing state machines is a trap. Abstracting Stateless either becomes a giant reimplementation or a crippled abstraction users escape. Document the Stateless types in user code. |
| 5 | Keep `Result.Combine` arity? | **Keep T1..T9 with first-failure-wins semantics. `Validate` builder for validation aggregation (no ceiling). `TRLS014` (T10 hard-error) stays with a clearer message.** | The two operations have **different semantics**: `Validate` accumulates field errors into a single `ValidationError` (validation-shaped failures only); `Combine` combines heterogeneous `Result<T1>..<T9>` with first-failure-wins (any error type). T1..T9 is already T4-generated, costs nothing, and `Combine(a,b,c,d).Bind((a,b,c,d) => ...)` is genuinely cleaner than chained `Bind`s. No `AggregateError` — there is no useful HTTP/UX mapping for "multiple unrelated errors at once". `Result.ParallelAsync` retained for parallel async with first-failure-wins (and OpenTelemetry on both branches). |
| 6 | `MaybeInvariant` location? | **Keep separate from `Validate` builder, in `Trellis.Core`** | Different semantics. `Validate` accumulates field errors per source field. `MaybeInvariant` enforces *cross-field* rules between optionals (`AllOrNone`, `Requires`, `MutuallyExclusive`, `ExactlyOne`, `AtLeastOne`). Folding them together would conflate two distinct concepts and harm AI clarity. |

### Decision implications for the redesign

- **`Result<T>` shape (§3.1):** confirmed — `IsSuccess`, `IsFailure`, `TryGetValue`, `TryGetError`, `Deconstruct`. No `Value`/`Error` properties.
- **`Result.Combine` (§3.4):** confirmed surface = T1..T9, first-failure-wins, no aggregation.
- **`Trellis.Asp` (§6, §12.2):** owns `RepresentationMetadata`, `ErrorHttpMapping`, the `ETagHelper.ParseIfMatch/ParseIfNoneMatch` HTTP parsers, and the `RetryAfterValue.ToHttpHeaderValue()` formatter extension. **`EntityTagValue` and `RetryAfterValue` stay in `Trellis.Core`** so Application/ACL can use them without referencing Asp (per template usage). Core stays transport-agnostic in *concepts* — these two types are framework-agnostic value types, just commonly used at the HTTP boundary.
- **`Trellis.Mediator` (§5):** thin wrapper, retains `IFailureFactory<TSelf>` constraint and the `IAuthorize` marker interface (both are necessary for the wrapper model).
- **`Trellis.StateMachine` (§9):** name change only; Stateless types remain visible.
- **`MaybeInvariant` (§3.5):** stays in `Trellis.Core` as a separate static API.

There are now **no open questions blocking Phase 1a**. The next deliverable is a per-package implementation plan for **Phase 1a — `Trellis.Core` Result/Maybe rewrite**.

---

## 15. Phasing (framework-first; template rewritten post-GA)

Once approved, work splits into **8 phases** (Phases 1a–7) preceded by a small set of cross-cutting commitments. **Phase 1 is much narrower than v1's "core rewrite"** — per critique, a single phase that combines new Result + new Error + new async model + validation DSL + doc generator is too big. Splitting unblocks parallel work. **The v1 template is NOT kept green during Phases 1a–5a** — it stays on v1.x packages until the post-GA Phase 6 rewrite (per §0.5).

**Cross-cutting commitments (apply to every phase, no separate phase number):**
- All framework packages stay green: `dotnet build` + `dotnet test` succeed across the entire framework solution after each merged PR.
- Apply §12.5 target/language and §12.6 AOT/trim flags from day one to every package they touch.
- Apply §15.1 release/quality gates to every package boundary.
- Breaking changes documented inline in the corresponding `trellis-api-*.md` "Breaking changes from v1" section. **No standalone `MIGRATION_v<N>.md` document is published** (§12.4).
- **TDD is mandatory** per `TrellisFramework/.github/copilot-instructions.md`. Every Phase 1a–7 PR follows red→green→refactor: failing test(s) committed first (or in the same PR with a clear "test first" commit ordering), then the implementation that satisfies them, then the refactor. The pre-submission checklist in `copilot-instructions.md` (build clean, all tests green, public API surface reviewed, analyzers clean, docs updated) is enforced on every PR — no "we'll backfill tests later" exceptions during the rewrite.
- **v2-canary grows alongside the rewrite, not after it.** A minimal `samples/v2-canary/` worker app is created in Phase 1a and grows with each later phase (see Phase 1a deliverable below and §15.1). This makes the canary a continuous design feedback loop instead of a post-hoc validation, and surfaces awkward surface decisions while course-correction is still cheap.

1. **Phase 1a — `Trellis.Core` Result/Maybe + `WriteOutcome<T>` rewrite + sourcegen rename + minimal canary.** New `Result<T>` shape (§3.1) including non-generic `Result` (replaces `Result<Unit>`) with full surface parity per §3.1, **factory rename `Success`/`Failure` → `Ok`/`Fail`**, removed `.Value`/`.Error` properties, removed implicit operators, seven pipeline verbs (§3.2), `BindZip` (T4-generated 1..9 arity) and LINQ query-syntax surface (`Select`/`SelectMany`/`Where`) retained, explicit async overload model (§3.3), `Maybe<T>` trim (§3.5) including the `default(...)` invariants of §3.5.1, `WriteOutcome<T>` (§3.1 — full 6-case hierarchy including `Accepted`/`AcceptedNoContent`) moved from `Trellis.Asp` to `Trellis.Core` along with `RepresentationMetadata`. Keep `Try`/`TryAsync`/`Traverse`. Tracing surface unchanged. **The `Error` *type-shape* commitments** (errors are `record`s, fields are `required` or positional, collections are `IReadOnlyList<T>`) are introduced here as the abstract `Error` base record so `Result<T>.TryGetError` and `Match` have a typed return — but **the concrete catalog cases are deferred to Phase 1b**. Phase 1a ships only the `Error` base record + a single `UnexpectedError` case (needed for `default(Result<T>)` per §3.5.1 and for `Where` per §3.2). **Source-generator coupling — explicit dependency:** today's `Trellis.Primitives.Generator`, `Trellis.EntityFrameworkCore.Generator`, and `Trellis.AspSourceGenerator` projects all emit `Result.Success`/`Result.Failure` calls. Phase 1a includes the generator-template update so all three emit `Result.Ok`/`Result.Fail`; the three generator packages cut matching pre-releases in lockstep with Phase 1a's `Trellis.Core` pre-release. **Canary deliverable for Phase 1a:** create `samples/v2-canary/` as a 100–200-line worker app that exercises `Result.Ok`/`Fail`, `Maybe<T>`, the seven pipeline verbs, `BindZip`, `Try`, and `WriteOutcome<T>` end-to-end against the Phase 1a `Trellis.Core` pre-release. (The canary does **not** exercise the error catalog — that is added in Phase 1b's canary growth.) The canary builds and all its tests pass before Phase 1a merges; later phases extend it. **No migration tooling and no template work** — the template is rewritten as a post-GA Phase 6, after the framework is feature-complete.
2. **Phase 1b — Error catalog rewrite + canary growth.** Closed-by-convention catalog (§3.6) — the 17 concrete `Error` subtypes including `EntityTagValue`/`RetryAfterValue` value types in core (§12.2), `Validate` builder (§3.4), `MaybeInvariant` retention. New analyzers `TRLS026` (closed catalog) / `TRLS027` (exhaustive `Match`) / `TRLS030` (`Where`-uses-generic-error). The catalog evolution policy of §3.6 is published as binding from this phase. **Canary growth:** add a validation step that uses the `Validate.Field(...).Build(...)` builder and a `Match` over 3+ catalog cases.
3. **Phase 2 — Primitives + Generators + DDD-merge into `Trellis.Core` + canary growth.** Attribute-driven scalar + composite VO generators (§4); rewrite the 13 built-ins as partials. Drop `IConvertible`. **Move the scalar/composite base classes** (`Scalar<T,V>`, `Composite`, `RequiredString`, `RequiredGuid`, `RequiredInt`, `RequiredDecimal`, `RequiredEnum`) from `Trellis.Primitives` into `Trellis.Core` — `Trellis.Primitives` keeps only the 13 concrete VOs. **Merge `Trellis.DomainDrivenDesign` into `Trellis.Core`** — all 12 source files (`Aggregate`, `Entity`, `ValueObject`, `ScalarValueObject`, `IAggregate`, `IEntity`, `IDomainEvent`, `Specification`, `AndSpecification`, `OrSpecification`, `NotSpecification`, `AggregateETagExtensions`) move into `Trellis.Core`. The DDD `ScalarValueObject` and the new `Scalar<T,V>` are unified as a single `Scalar<T,V>` in `Trellis.Core`; the old `ScalarValueObject` is **deleted outright at v2.0.0** (no `[Obsolete]` alias). Types are already in the `Trellis` namespace, so future-adopter `using` directives remain valid. **Intra-phase sequencing (two PRs):** (i) introduce the new types in `Trellis.Core`, repoint every downstream `.csproj` (Mediator, EFCore, Authorization, Asp, Primitives, Testing) to the new types, and confirm the entire framework solution builds and all tests pass; (ii) only then delete the `Trellis.DomainDrivenDesign` source tree. The `Trellis.DomainDrivenDesign` NuGet package is unlisted at the v2.0.0 release with a "moved into Trellis.Core" notice; `trellis-api-ddd.md` content folds into `trellis-api-core.md`. **Canary growth:** introduce one aggregate, one VO, one repository interface returning `Result<WriteOutcome<T>>`.
4. **Phase 3 — `Trellis.Asp` collapse + canary growth.** Single `ToHttpResponse` verb, options-driven (§6). Add `EntityTagValue.ToHttpHeaderValue()` and `RetryAfterValue.ToHttpHeaderValue()` extensions on the Asp side. Auto-register scalar value validation. Absorb `Trellis.Asp.Authorization` (so `[CustomerResourceId]` lives in `Trellis.Asp` namespace). (`RepresentationMetadata` already lives in `Trellis.Results`/Core alongside `WriteOutcome<T>` per Phase 1a — it stays there because the Application layer references it on `Result<WriteOutcome<T>>` return types.) **Canary growth:** add a one-controller × one-handler ASP slice with one ETag-conditional update, exercising `WriteOutcome<T>` → 200/201/204 mapping and `ToHttpResponse` over 3+ error cases.
5. **Phase 4 — Integration cleanup + canary growth.** `Trellis.Http` slim (§7); `Trellis.EntityFrameworkCore` dual-path conventions + retained diagnostics (§8); `Trellis.StateMachine` rename (§9); `Trellis.Mediator`/`Trellis.Authorization` per §5 (FluentValidation **stays separate**, not absorbed — see §5.2 and §2 row 9). `Trellis.Testing` and `Trellis.Testing.AspNetCore` **stay as two packages** (see §10). `Trellis.Stateless` → `Trellis.StateMachine` with **no metapackage redirect** — clean cut, consistent with §12.4. **Canary growth:** wire the canary's repository to EF Core in-memory, the dispatch through `Trellis.Mediator`, and one `[Authorize]` resource-aware authorization check.
6. **Phase 5a — Tooling consolidation + v2.0.0 cut (mandatory; last framework-side phase).** Unify analyzer rules + renumber diagnostic IDs (§11), including the `TRLSGEN###` → `TRLS031`+ migration. Ship `TrellisDiagnosticIds` constants (§11) and the "Did you mean: …" message convention. Add `TRLS028` enforcing the §12.6 AOT/reflection ban. **v2-canary final gate (per §15.1):** the canary that has been growing since Phase 1a must compile against the assembled v2 surface, exercise `WriteOutcome<T>` end-to-end, and pass `dotnet publish /p:PublishAot=true` with no warnings. The canary is a private sample, not a published template. At the end of Phase 5a, the framework is feature-complete and the v2.0.0 NuGet packages are cut. **No template work happens in Phases 1a–5a** — the v1 template stays on v1.x packages, and the v2 framework ships *without* an accompanying public starter template.
7. **Phase 6 — Template rewrite (cross-repo, post-framework-GA).** Begins **only after Phase 5a ships v2.0.0 of the framework packages**. In `TrellisAspTemplate` (separate repo), rewrite the `Trellis.AspTemplate` package against v2 from a clean slate. **The 4 Clean-Architecture layers are preserved** (Domain / Application / ACL / API), but every file is regenerated against the v2 surface — `Result.Ok`/`Fail`, `Result` (no `Unit`), seven-verb primary pipeline (with `BindZip`/LINQ query syntax available where they read more clearly), `WriteOutcome<TodoItem>` from `Trellis.Core`, attribute-driven primitives, `Trellis.StateMachine` references, FluentValidation as a separately-imported package. Add `dotnet new` parameters (e.g., `--no-state-machine`, `--minimal-api`). Re-sync embedded `.github/copilot-instructions.md` + `trellis-api-*.md` to the v2 surface, lock these in as the canonical AI surface, and tag a 2.0.0 release of `Trellis.AspTemplate`. The OpenAPI document regenerates from the rewrite — no v1→v2 client-shape compatibility is offered. **Implication of this phasing:** there is a window — between framework v2.0.0 GA and template v2.0.0 GA — when a new project trying to adopt v2 has no public starter. This is acceptable because (a) Trellis is not yet broadly externally adopted, (b) the v2-canary sample (Phase 5a) and framework integration tests cover the cross-package flow, and (c) keeping the template out of Phases 1a–5a removes coordination cost from the framework rewrite. Phase 6 also serves as the final shake-down of the v2 framework surface — awkwardness discovered while rewriting the template feeds back as v2.0.1/v2.1 framework adjustments.
8. **Phase 7 — Owned dispatcher (optional, conditional on AI-eval data).** Build the source-generated dispatcher in `Trellis.Mediator.Dispatcher` **only if** Phase 6's template rewrite + AI-eval data shows the foreign mediator's quirks measurably hurt code-generation correctness. Phase 7 is independent of Phases 1a–6 and may be deferred indefinitely.

Each phase delivers a working, tested, documented slice. Because backward compat isn't required, each phase is a whole-package replacement.

### 15.1 Release & quality gates (apply to every Phase 1a–5a NuGet)

Every framework `.nupkg` produced by Phases 1a–5a satisfies the following before merge to `main`:

| Gate | Requirement | Enforcement |
|---|---|---|
| **Determinism** | `<Deterministic>true</Deterministic>` + `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>` set in `Directory.Build.props` | CI verifies bit-identical `.nupkg` across two clean builds of the same SHA |
| **SourceLink** | `Microsoft.SourceLink.GitHub` referenced; `<PublishRepositoryUrl>true</PublishRepositoryUrl>`, `<EmbedUntrackedSources>true</EmbedUntrackedSources>` | CI fails if any package lacks SourceLink metadata |
| **Symbols** | `<IncludeSymbols>true</IncludeSymbols>` + `<SymbolPackageFormat>snupkg</SymbolPackageFormat>` | `.snupkg` published to symbol server alongside `.nupkg` |
| **Strong naming** | All runtime assemblies signed with the `Trellis.snk` key (already in repo for v1) | CI fails if any output assembly is unsigned |
| **AOT / trim** | `<IsAotCompatible>true</IsAotCompatible>` + `<IsTrimmable>true</IsTrimmable>` per §12.6 | CI builds a trimmed/AOT test harness and fails on any IL2026/IL2070/IL3050 warning |
| **Public API tracking** | `Microsoft.CodeAnalysis.PublicApiAnalyzers` with `PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt` per package | CI fails on undocumented public-API additions/removals |
| **Per-package docs** | Each package ships its own `trellis-api-<package>.md` as content (`Trellis.ApiReference.targets` already does this) | CI verifies the file exists and the "Breaking changes from v1" section is non-empty for v2.0.0 |
| **Pre-release feed** | Each merged PR publishes `*-alpha.<sha>` packages to the framework's CI feed | Used by the v2-canary sample (Phase 5a) and any internal early adopters |
| **v2-canary** | A `samples/v2-canary/` sample app compiles against the latest pre-release packages and passes `dotnet publish /p:PublishAot=true` with zero trim warnings | Required gate before the v2.0.0 GA cut at the end of Phase 5a |
| **Pre-release strategy** | v2.0.0 packages publish through `2.0.0-preview.N` → `2.0.0-rc.N` → `2.0.0` per standard NuGet semver | First public `2.0.0-preview.1` cut at end of Phase 1a; `2.0.0-rc.1` at end of Phase 5a; `2.0.0` final after the v2-canary passes |
| **Performance** | Microbench suite (`Trellis.Benchmarks/`) covers Result allocation, pipeline-verb cost, EFCore convention startup; CI fails if any tracked benchmark regresses >15% from the previous main | Phase 1a establishes baselines; subsequent phases must not regress |
| **Security defaults** | `Trellis.Asp` registers no auth scheme by default; the template (Phase 6) opts in to specific schemes. `Trellis.Asp` does not enable HSTS / HTTPS redirect by default (host responsibility) | Documented in `trellis-api-asp.md` "Security defaults" section |
| **Telemetry baseline** | `OpenTelemetry` is an opt-in dependency (already true in v1). Trellis types emit `ActivitySource("Trellis.<Area>")` and `Meter("Trellis.<Area>")` with names documented per package; no `OTel` SDK references in framework runtime packages | CI verifies framework packages have no transitive `OpenTelemetry.*` dependency |

---

## 16. Scoring — honest self-assessment

**Caveat upfront:** scoring my own proposal is biased — I am the one defending it. I'll be deliberately conservative on the proposed-design column to compensate. The current design is *not* in poor shape — it's a thoughtful, layered, well-documented framework with real strengths. The redesign is mostly *AI-correctness* wins, not a rescue from a broken foundation.

### Per-dimension scores (0 = unusable, 10 = best in class for the goal)

| Dimension | Current | Proposed | Δ | Honest note |
|---|---|---|---|---|
| **AI-correctness (first-try generation)** | 6 | 8 | +2 | Biggest single win. 7 primary verbs (+ `BindZip` + LINQ query syntax retained for value-preserving and query-style composition); no `Value`/`Error` properties (TRLS003/004 gone); closed-by-convention catalog; `Tap`/`TapOnFailure` split avoids overload-resolution gambling at the call site. Still capped by analyzer-not-compiler enforcement and async overload combinatorics. |
| **Surface discoverability** | 5 | 8 | +3 | Aliases removed; flat namespaces; per-package API references mirror package map 1:1. |
| **Layering clarity (Clean Arch)** | 7 | 8 | +1 | Already strong (template proves it). New: explicit layer-reference rules canonized in `copilot-instructions.md`. |
| **Type safety** | 7 | 8 | +1 | Removing `.Value`/`.Error` is real. `TryGetValue` out-pattern is safer. Catalog still closed-by-convention, not compiler-exhaustive — honest cap. |
| **Error model** | 6 | 8 | +2 | Concrete-subtype factory returns; protocol-bearing payloads kept; aggregation via `Validate` not `AggregateError`. `RetryAfterValue`/`EntityTagValue` placement now justified by layer usage. |
| **Async ergonomics** | 5 | 7 | +2 | Left/Right/Both file-naming noise gone from user surface; ~6 overloads per verb internal. Cap: `Task`↔`ValueTask` mixing still needs explicit conversion. |
| **ASP integration** | 6 | 8 | +2 | One `ToHttpResponse` verb collapses today's 4 quartets. Protocol semantics (Vary, conditional, Prefer, Range, RetryAfter) preserved via opts. Cap: opts-builder discoverability is unproven. |
| **EF integration** | 7 | 7 | 0 | Already solid. New: reflection-free generated convention path is additive, not replacement. Honest: `RepositoryBase` shape unchanged; magic index rewriting *not* added. |
| **Tooling (analyzers + generators)** | 6 | 8 | +2 | Unified `TRLS001…099`; new `TRLS023/024/025/026/027`; analyzer + generator co-shipped. |
| **Documentation** | 7 | 8 | +1 | Per-package `trellis-api-*.md` shipping in nupkg today (already good). New: template-anchored docs as canonical AI surface; reduces drift. |
| **Testability** | 8 | 8 | 0 | Already strong. New helpers (`ShouldHaveFieldError`, `ShouldHaveRaised<T>`) are additive. |
| **AOT readiness** | 6 | 7 | +1 | Source-gen-first culture for AOT-supported packages; explicit EF opt-out; Mediator wrapping caps gains. |
| **Internal consistency / "one way"** | 5 | 8 | +3 | The most-violated current axis (aliases everywhere). New: aliases banned by analyzer; `Result.Ok`/`Fail`/`Try` is the entire factory surface. |
| **Combinator power** | 6 | 7 | +1 | `Validate` builder removes arity ceiling for validation; `Combine T1..T9` + `ParallelAsync` retained for heterogeneous combination. |
| **Migration cost / risk** | n/a | n/a | — | The redesign is whole-package replacement across 8 phases (1a–7) — real cost. The v2-canary sample in Phase 5a (§15.1) mitigates "no consumer before GA" risk but does not eliminate it. Counted as a risk, not a score. |

### Headline scores

| Design | Score | One-line characterization |
|---|---|---|
| **Current Trellis** | **6.0 / 10** | Solid, layered, well-documented framework. Real strengths in DDD primitives, EF integration, tracing, and per-package docs shipped in NuGet. Drag from accumulated alias surface, hidden `.Value`/`.Error` pitfalls, async file-naming noise, contradictory HTTP-type placement, and a 25-verb pipeline an LLM can't keep in working memory. **Production-grade but AI-noisy.** |
| **Proposed Trellis (paper)** | **7.5 / 10** | Materially better AI ergonomics across the board. Single biggest wins: the 7-verb primary pipeline (with `Tap`/`TapOnFailure` split for unambiguous read-at-call-site semantics), `BindZip` retained for value-preserving chains, LINQ query syntax retained for monadic composition, removed `.Value`/`.Error`, closed-by-convention error catalog, single `ToHttpResponse` verb. Honest caps: (a) "closed by analyzer ≠ closed by compiler", (b) async overload combinatorics still real internally (the BindZip 2..9-arity T4 family and LINQ surface together add ~30 overloads to the count in §3.3), (c) **paper score** — until shipped through Phases 1a–5a and validated against the v2-canary + the Phase 6 template rewrite, the +1.5 delta is theoretical, (d) migration cost across 8 phases is real engineering, (e) Mediator wrapper retention caps some AOT/dispatch wins (deliberately deferred to optional Phase 7). |

### Honest delta interpretation

A **+1.5 paper delta** means: this redesign meaningfully improves the AI-correctness story without breaking the architectural strengths the current design has. It is **not** a "rewrite to fix a broken foundation" — it is a "tighten an already-good foundation for a specific goal (LLM code generation)."

If the proposal scored 9.0+ I would distrust my own analysis. **+1.5 is the realistic upside**; the lower bound (if Phase 1a reveals async-overload usability problems or the closed-catalog analyzer is too noisy) is closer to **+0.5**. The phasing (§15) is designed so that each phase's value can be reassessed before committing to the next.

### What would push the proposed score to 8.5+?

- A working **Trellis-owned dispatcher** (Phase 7 stretch) that lets handlers return `Result<T>` natively without `IFailureFactory<TSelf>` — buys ~+0.3 on AI-correctness.
- **Roslyn-driven generation of `trellis-api-*.md` from source** so docs cannot drift — buys ~+0.3 on documentation.
- An **AI eval harness** (Phase 5) that measures actual first-try generation success rates against real prompts — would *prove* the +1.5 rather than estimate it.
- **F#-style discriminated union** for `Error` once C# ships union types — would convert the closed-by-convention into closed-by-compiler — ~+0.4.

These are explicitly *future work*, not part of the proposal.

---

## End

This v2 proposal is grounded in what C# can actually deliver, retains the load-bearing capabilities the v1 silently dropped (resource auth, tracing, `MaybeInvariant`, protocol-specific errors, EF persistence diagnostics, `Result.Try`, `WriteOutcome<T>`), and right-sizes the foundational work. The aggressive AI-correctness wins (seven verbs, removed `Value`/`Error` properties, deleted `IConvertible`, single `ToHttpResponse`) survive intact.

If approved, the next deliverable is a per-package implementation plan starting with **Phase 1a** (Result/Maybe rewrite), because every other phase depends on it.

---

## Post-Phase 3 cleanup (v3 release)

**Status:** the Phase 3 obsolete-verb cleanup has landed. The seven legacy Trellis.Asp extension classes (`ActionResultExtensions`, `ActionResultExtensionsAsync`, `HttpResultExtensions`, `HttpResultExtensionsAsync`, `PageActionResultExtensions`, `PageHttpResultExtensions`, `WriteOutcomeExtensions`) and their verbs (`ToActionResult`, `ToHttpResult`, `ToCreatedAtActionResult`, `ToCreatedAtRouteHttpResult`, `ToCreatedHttpResult`, `ToUpdatedActionResult`, `ToUpdatedHttpResult`, `ToPagedActionResult`, `ToPagedHttpResult`) have been deleted outright. The single supported verb is `result.ToHttpResponse(...)` / `result.ToHttpResponseAsync(...)`, with `.AsActionResult<T>()` / `.AsActionResultAsync<T>()` as the MVC typed-signature adapter. All in-tree examples (`Showcase.MinimalApi`, `Showcase.Mvc`, `ConditionalRequestExample`) use the new API exclusively. See [`MIGRATION_v3.md`](https://github.com/microsoft/Trellis/blob/main/MIGRATION_v3.md) for the per-verb replacement table and [`asp-tohttpresponse.md`](../articles/asp-tohttpresponse.md) for canonical patterns.

---

## Decision log

Append-only record of design decisions taken during phased v2 implementation that diverged from, refined, or extended the literal text of the sections above. The original sections are preserved unchanged for historical context.

### Phase 4b &mdash; `Trellis.Http` slim (refines §7)

Section 7 sketched the canonical surface but deferred several details to implementation. Phase 4b resolves them as follows:

1. **`Async` suffix on every method.** §7's example used bare `ToResult` / `HandleNotFound` etc. The repo's pervasive convention is that any method whose return type is `Task<...>` carries an `Async` suffix. The implemented surface therefore uses `ToResultAsync`, `HandleNotFoundAsync`, `HandleConflictAsync`, `HandleUnauthorizedAsync`, `ReadJsonAsync`, `ReadJsonMaybeAsync`. Repo convention wins over the ADR's example.

2. **Body-aware `ToResultAsync` overload added.** §7 deletes `HandleFailureAsync<TContext>` ("the `TContext` channel is anti-pattern; closures are fine"). To preserve the load-bearing capability of synthesizing an `Error` from the response body (for example, decoding RFC 9457 problem-details), Phase 4b ships a second `ToResultAsync` overload taking `Func<HttpResponseMessage, CancellationToken, Task<Error?>>`. The mapper is invoked **only** for non-success status codes, returning `null` to pass through or an `Error` to fail. State that the v1 `TContext` channel carried is now captured by closure.

3. **Explicit disposal contract.** §7 did not specify ownership of the underlying `HttpResponseMessage`. Phase 4b commits the library to the following contract, reflected in the type-level XML remarks and tested via a dispose-tracking subclass:
    - `ToResultAsync` (both overloads): dispose on the `Fail` path; pass-through on `Ok`.
    - `HandleNotFoundAsync` / `HandleConflictAsync` / `HandleUnauthorizedAsync`: dispose on the matched-status `Fail` path; pass-through otherwise.
    - `ReadJsonAsync` / `ReadJsonMaybeAsync`: **always** dispose after reading (success, structured failure, or thrown `JsonException` from the `Maybe` overload).
    - Already-failed `Result<HttpResponseMessage>` short-circuits `ReadJson*` with the upstream error preserved (no response to dispose; caller never owned one).

4. **Single canonical shape for the post-bridge chain.** §7's example showed `HandleNotFound(this Task<HttpResponseMessage> r, ...)` (singular). Phase 4b implements `Handle*Async` strictly on `Task<HttpResponseMessage>` &mdash; they are *entry points*, not composable mid-chain operators. Multi-status mapping after the bridge is expressed via `ToResultAsync(statusMap)`. This avoids the v1 trap of having both `Task<HRM>` and `Task<Result<HRM>>` overloads for every verb.

5. **Clean cut, no shims.** Pre-GA, every removed/renamed verb is deleted outright; there are no `[Obsolete]` redirects. Migration is mechanical (per the table in `docs/api_reference/trellis-api-http.md`) and tightly scoped &mdash; the framework had zero in-tree production callers of the removed verbs.

These decisions are implemented in PR for branch `dev/xavier/v2-phase4b-http-slim` and codified in tests under `Trellis.Http/tests/HttpResponseExtensionsTests/`.
