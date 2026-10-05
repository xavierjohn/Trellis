---
package: Trellis (start here)
namespaces: [Trellis]
types: [orientation, routing]
related_docs: [trellis-api-cookbook.md, trellis-api-core.md, trellis-api-anti-patterns.md, trellis-api-analyzers.md]
version: v3
last_verified: 2026-10-02
audience: [llm]
agent_usage: required
agent_description: "Routing head for every Trellis task: which reference to open, the recipe lookup and how to read the set. Read before writing or changing code that uses Trellis."
---

# Trellis — start here

You are looking at versioned Trellis API reference files, installed from the `Trellis.Core` package by the optional
Trellis.AgentDocs local tool (`agentdocs`; approve `Trellis.Core` in `.agentdocs/policy.json`). This file is the **routing head**
for the whole set: it tells you which reference to open for a task and which recipe to read. It is self-contained on
purpose, so it is the only file you must read before writing Trellis code. The other files sit beside it, so every link
below is relative.

## Read the references yourself — do not delegate them to a sub-agent

A sub-agent hands back a summary, so the exact signatures never reach your context and you end up
writing code against a paraphrase. That is how invented APIs and wrong overloads get produced, and
it is the specific failure these references exist to prevent.

Sub-agents are fine for work whose output is a *verdict* rather than knowledge you must write code
against: running builds and tests, searching for a file, auditing something for accuracy. The rule
is narrow — if the answer determines the code you are about to write, read it yourself.

## Do not guess

Every Trellis API you write should be traceable to a line in one of these files. If you cannot find
the method, overload, or attribute you are about to use, stop and read the reference that owns it
rather than reconstructing the signature from memory. Trellis makes heavy use of source generators,
so a base type such as `RequiredGuid<TSelf>` or `Aggregate<TId>` already supplies members that are a
compile error to redeclare — the cookbook's Recipe 1 lists that inherited surface explicitly.

## How to read these recipes

**Hold this file resident; read recipe bodies on demand.** This file is the routing head for the whole set (~6K tokens)
and routes every task. The 37 recipe bodies in `trellis-api-cookbook.md` are another ~57K, and a typical task needs
one to three of them — so open a body when the [task lookup table](#task---recipe-lookup) sends you to one, rather than
loading all of them up front. Every live recipe is reachable from that table (enforced by the repository's TRLDOC007 lint
gate), so if a task is not listed there, no recipe covers it. Never write code from a recipe's title alone.

Every recipe follows the same shape:

1. **Problem statement** — what the consumer is trying to accomplish.
2. **Solution code** — copy-pasteable C# that compiles against the documented public surface only. No invented APIs.
3. **What it shows** — the cross-cutting concept being demonstrated.
4. **Anti-pattern → fix** *(when applicable)* — the wrong way and which Trellis analyzer catches it.

Conventions used throughout:

- All Trellis types live in the `Trellis` namespace except where called out (`Trellis.Asp`, `Trellis.Asp.Authorization`, `Trellis.EntityFrameworkCore`, `Trellis.Analyzers`).
- Snippets use C# 12+ features (file-scoped namespaces, primary constructors, collection expressions) — Trellis targets `net10.0`.
- `Result.Ok` / `Result.Fail` are *the* construction APIs. `default(Result<T>)` is a typed failure; do not rely on it as success.
- Every async pipeline uses `*Async` extensions; mixing sync chain methods with `Task<Result<T>>` triggers `TRLS009`.
- Examples reference an `OrderId : RequiredGuid<OrderId>` value object and an `Order` aggregate. Substitute your own types without changing the structure.
- A command without a payload returns `Result<Trellis.Unit>` — qualified because a file that imports both `Trellis` and `Mediator` has two `Unit` types in scope (`Trellis.Unit` and `Mediator.Unit`). Add `global using TrellisUnit = Trellis.Unit;` (or `global using Unit = Trellis.Unit;`, since Trellis code never references `Mediator.Unit`) to your `GlobalUsings.cs` to drop the qualification.

## LLM preflight: load the smallest correct reference set

Before writing Trellis code, choose the task in the lookup table below, then load only the package references needed for that task. The cookbook gives the end-to-end recipe; the package references are the source of truth for exact signatures, overloads, ordering, and edge-case behavior.

| If you are changing... | Load these references before coding | Why |
|---|---|---|
| Result, Maybe, errors, value-object bases, aggregates, specifications, pagination | `trellis-api-cookbook.md`, `trellis-api-core.md` | Core owns the ROP primitives and DDD base types used by every package. |
| ASP.NET endpoints, controllers, response mapping, ETags, Prefer, ranges, actor providers | `trellis-api-cookbook.md`, `trellis-api-asp.md`, `trellis-api-core.md`; add `trellis-api-mediator.md` when endpoints send messages | `ToHttpResponse` and scalar validation are ASP-owned, while handlers and result shapes come from Core/Mediator. |
| Mediator handlers, pipeline behaviors, validation, authorization, domain events | `trellis-api-cookbook.md`, `trellis-api-mediator.md`, `trellis-api-core.md`; add `trellis-api-efcore.md` for unit-of-work and `trellis-api-authorization.md` for resource guards | Pipeline ordering and opt-in behaviors are cross-package; missing one reference usually creates a registration-order bug. |
| EF Core persistence, repositories, unit of work, `Maybe<T>` queries, `[OwnedEntity]` | `trellis-api-cookbook.md`, `trellis-api-efcore.md`, `trellis-api-core.md`; add `trellis-api-mediator.md` when commits happen through handlers | EF owns mapping/interceptors; Mediator owns when command commits run. |
| FluentValidation integration | `trellis-api-cookbook.md`, `trellis-api-fluentvalidation.md`, `trellis-api-mediator.md` | FluentValidation plugs into `ValidationBehavior` through `IMessageValidator<TMessage>`; it is not a separate pipeline behavior. |
| Composition-root helpers (`AddTrellis`, `UseXxx`) | `trellis-api-cookbook.md`, `trellis-api-servicedefaults.md`, plus every package reference for selected modules | `TrellisServiceBuilder` preserves canonical order but does not register app-owned services like `DbContext` or Mediator handlers. |
| HTTP client adapters | `trellis-api-cookbook.md`, `trellis-api-http.md`, `trellis-api-core.md` | The HTTP package maps upstream responses into Core `Result<T>` / `Maybe<T>` shapes. |
| Tests | `trellis-api-testing-reference.md`; add `trellis-api-testing-aspnetcore.md` for `WebApplicationFactory` or `.http` replay | Unit/helper assertions and ASP integration helpers live in separate test packages. |
| Analyzer diagnostics | `trellis-api-anti-patterns.md` first for the canonical WRONG/FIX shape to adapt, then `trellis-api-analyzers.md`, then the package reference named by the diagnostic category | Anti-pattern file shows the canonical control-flow shape; analyzer docs explain the warning; the package reference gives the canonical API to use instead. |

Measurable completion check for generated code: every Trellis method call should be traceable to a loaded package reference, every selected integration module should be wired in the documented order, and every public API or behavior change should update the matching package reference plus the matching recipe in `trellis-api-cookbook.md` (and this router's lookup row) when it affects a cross-package recipe.

Known non-APIs and corrected assumptions:

| Do not write | Correct source-backed statement |
|---|---|
| `WithDocumentPerVersion()` | No Trellis API with this name exists. |
| `MapScalarApiReference()` | Sample-app helper only; not a Trellis framework API. |
| Place `UseScalarValueValidation()` anywhere | Add it before routing/endpoints that deserialize request bodies. |
| Mutate `IAuthorize.RequiredPermissions` | `RequiredPermissions` is an `IReadOnlyList<string>`. |
| `IValidate.Validate()` returns `Result` | The declared return type is `IResult`. |

## Patterns Index

### Task -> recipe lookup

Use this table before writing code. If a task matches a row, read that recipe first.

| Task | Start here |
|---|---|
| Create or load an aggregate with value objects | [Recipe 1](trellis-api-cookbook.md#recipe-1--crud-aggregate-ddd-value-objects--entity--repository-contract) |
| Write a command handler that validates and persists | [Recipe 2](trellis-api-cookbook.md#recipe-2--command--handler--fluentvalidation--ef-persistence), then [Recipe 16](trellis-api-cookbook.md#recipe-16--unit-of-work-in-handlers-add-staging-vs-immediate-saveasync) |
| Load multiple independent resources in one handler (HTTP + DB, two upstream services, factory-created `DbContext`s) | [Recipe 21](trellis-api-cookbook.md#recipe-21--parallel-independent-loads-in-handlers-resultparallelasync--whenallasync) |
| Multi-aggregate orchestration: side effect per element of a related-aggregate set | [Recipe 22](trellis-api-cookbook.md#recipe-22--multi-aggregate-orchestration-fail-loud-on-missing-related-aggregates) |
| Apply an operation to every element of a related-aggregate set where per-element validation can fail (reserve stock per line item, etc.) — avoid partial mutation | [Recipe 25](trellis-api-cookbook.md#recipe-25--two-pass-validate-then-mutate-over-a-collection-of-related-aggregates) |
| Concurrency control on mutating endpoints — when to require `If-Match` | [Recipe 23](trellis-api-cookbook.md#recipe-23--concurrency-control-on-aggregate-mutating-endpoints-when-to-require-if-match) |
| Save bandwidth on reads — return `304 Not Modified` when the client's `If-None-Match` still matches | [Recipe 6](trellis-api-cookbook.md#recipe-6--conditional-get-with-entitytagvalue) |
| Add a paginated list query | [Recipe 3](trellis-api-cookbook.md#recipe-3--query-handler-returning-paget-paginated-list-with-cursor) |
| Parse cursor/limit query input in MVC or Minimal APIs without treating `?cursor=` as missing | [`HttpRequestPaginationExtensions`](trellis-api-asp.md#httprequestpaginationextensions), then [Recipe 3](trellis-api-cookbook.md#recipe-3--query-handler-returning-paget-paginated-list-with-cursor) |
| Paginate a translated spherical distance or an application-computed score with validated continuation state bound to query context | [Recipe 40](trellis-api-cookbook.md#recipe-40--computed-pagination-with-validated-query-bound-continuation-state) |
| Validate geographic coordinates, calculate in-memory distance, build conservative bounds, or compose an EF Core radius query | [`GeoCoordinate`](trellis-api-primitives.md#geocoordinate) and [`GeoBounds`](trellis-api-primitives.md#geobounds), then [`GeoCoordinateExpressions` in the EF Core reference](trellis-api-efcore.md#geocoordinateexpressions) for database queries; use Recipe 40 for distance pagination |
| Model weekly availability, overnight periods, or time-zone-aware membership | [`WeeklySchedule` and `WeeklyPeriod` in the Primitives reference](trellis-api-primitives.md#weeklyschedule); use Recipe 13's DTO boundary guidance for JSON/persistence |
| Add Minimal API or MVC endpoints | [Recipe 4](trellis-api-cookbook.md#recipe-4--minimal-api-endpoint-wiring-resultt--httpresponseoptionsbuilder--tohttpresponse), [Recipe 5](trellis-api-cookbook.md#recipe-5--mvc-controller-using-asactionresult) |
| Generate versioned Location links to a named route or MVC action, including cross-route segment pins | [Recipe 4](trellis-api-cookbook.md#recipe-4--minimal-api-endpoint-wiring-resultt--httpresponseoptionsbuilder--tohttpresponse), then [target-aware API versioning](trellis-api-asp-apiversioning.md#behavioral-notes) |
| Map primitive DTO fields to value objects | [Recipe 18](trellis-api-cookbook.md#recipe-18--dto-primitives-to-value-object-command-no-test-only-unwrap) |
| Add resource authorization | [Recipe 7](trellis-api-cookbook.md#recipe-7--authorization-iactorprovider--iauthorize--resource-based-auth) |
| Authorize against a related resource one or more navigation hops away (cricket-style fan-out, owner chains) | [Recipe 24](trellis-api-cookbook.md#recipe-24--indirect-multi-hop-resource-authorization) |
| Enforce tenant isolation on a command (per-command scope check, no base type) | [Recipe 38](trellis-api-cookbook.md#recipe-38--tenant-scoped-resource-authorization-with-a-typed-actor-attribute) |
| Map `Maybe<T>` or composite value objects with EF Core | [Recipe 8](trellis-api-cookbook.md#recipe-8--ef-core-maybepropertymapping-for-nullable-value-objects), [Recipe 13](trellis-api-cookbook.md#recipe-13--composite-value-object-end-to-end-domain--api-json-binding--ef-core-ownership) |
| Add optional request/response fields | [Recipe 14](trellis-api-cookbook.md#recipe-14--optional-fields-in-request-dtos-maybetscalar-vs-nullable-transport) |
| Read optional HTTP resources where 404 means absent | [Recipe 19](trellis-api-cookbook.md#recipe-19--http-client-result-safety-and-optional-reads) |
| Choose between fail-fast and accumulating-error collection ops, including indexed validation | [Recipe 20](trellis-api-cookbook.md#recipe-20--fail-fast-vs-accumulating-sequencetraverse-vs-sequencealltraverseall) |
| Return synchronous `Result` chains from `Task`/`ValueTask` APIs | [Recipe 2](trellis-api-cookbook.md#recipe-2--command--handler--fluentvalidation--ef-persistence), then `AsTask()` / `AsValueTask()` in [trellis-api-core.md](trellis-api-core.md#task-adapter-family--resulttaskadapterextensions) |
| Create HTTP-oriented resource errors | Use `ResourceRef.For<TResource>(id)` from [trellis-api-core.md](trellis-api-core.md#supporting-types) |
| Point `ProblemDetails.Instance` at the resource that failed, instead of leaving it null or hand-formatting a URI | [Recipe 28](trellis-api-cookbook.md#recipe-28--synthesise-problemdetailsinstance-from-a-resourceref) |
| Add a state transition | [Recipe 9](trellis-api-cookbook.md#recipe-9--state-machine-canfire--fire-pattern-with-fireresult) |
| Write handler/domain tests | [Recipe 10](trellis-api-cookbook.md#recipe-10--test-handler-test-using-trellistesting-shouldbe--unwraperror) |
| Write integration tests for a `BackgroundService` worker | [Recipe 26](trellis-api-cookbook.md#recipe-26--test-a-backgroundservice-with-workerharnesstworker) |
| Insert a row idempotently on a unique constraint (de-duplicated worker outbox, "save unless exists") | [Recipe 27](trellis-api-cookbook.md#recipe-27--idempotent-inserts-on-a-unique-constraint-with-tryinsertuniqueasync) |
| Make POST / PATCH safe under client retries with an IETF `Idempotency-Key` header | [Recipe 29](trellis-api-cookbook.md#recipe-29--ietf-idempotency-key-middleware-on-post--patch-with-usetrellisidempotency) |
| Render ASP.NET Core rate-limit rejections as Trellis 429 Problem Details with optional `Retry-After` | [`RateLimiterOptionsExtensions` in the ASP reference](trellis-api-asp.md#ratelimiteroptionsextensions) |
| Define domain events | [Recipe 17](trellis-api-cookbook.md#recipe-17--defining-custom-domain-events-occurredat-is-the-only-timestamp) |
| Make domain events survive a crash (transactional outbox) | [Recipe 35](trellis-api-cookbook.md#recipe-35--transactional-outbox-for-crash-safe-domain-events) |
| Publish a stable external contract (integration events) translated from domain events | [Recipe 36](trellis-api-cookbook.md#recipe-36--translating-a-domain-event-into-an-integration-event) |
| Connect an inbound message, domain outbox row, translated integration event, and W3C trace | [Recipe 36](trellis-api-cookbook.md#recipe-36--translating-a-domain-event-into-an-integration-event), then [`IntegrationMessageContext`](trellis-api-mediator.md#integrationmessagecontext) and [outbox lineage](trellis-api-efcore-outbox.md#outboxmessage) |
| Show a validation failure in the user's language, or render your own message from a 422 instead of showing the server's English | [Recipe 39](trellis-api-cookbook.md#recipe-39--rendering-a-validation-failure-in-the-callers-language-code--args) |
| Fix analyzer warnings | [Recipe 11](trellis-api-cookbook.md#recipe-11--anti-pattern--fix-gallery-the-analyzers-in-action) |
| Wire the composition root | [Recipe 12](trellis-api-cookbook.md#recipe-12--di-wiring-playbook-addtrellis-composition-builder) |
| Rehydrate an entity from a database row (fail-loud vs Result-track) | [Recipe 30](trellis-api-cookbook.md#recipe-30--rehydrating-entities-from-persistence-fail-loud-vs-result-track) |
| Reconstitute an aggregate in a non-EF repository without re-running its factory (no re-validation, no events) | [Recipe 37](trellis-api-cookbook.md#recipe-37--reconstituting-an-aggregate-without-its-factory-non-ef-repositories) |
| Avoid the pipeline-then-handler duplicate load when a command both authorizes and mutates the same resource | [Recipe 31](trellis-api-cookbook.md#recipe-31--avoid-duplicate-load-with-iauthorizedresourcetcommand-tresource) |
| Hide existence of sensitive resources from unauthorized callers — translate `Forbidden`/`AuthenticationRequired` to `NotFound` | [Recipe 32](trellis-api-cookbook.md#recipe-32--hide-existence-with-authfailureexposurepolicyhideasnotfound) |
| Configure the strict `AddJwtBearer` validation profile + key-rotation runbook for a gateway-minted internal JWT | [Moved: xavierjohn/Trellis.Microservices](trellis-api-cookbook.md#recipes-33-34--moved-to-xavierjohntrellismicroservices) (Recipe 1 in the microservices cookbook) |
| Stand up the gateway side of the Path B microservices pattern (YARP transform that mints the internal JWT) | [Moved: xavierjohn/Trellis.Microservices](trellis-api-cookbook.md#recipes-33-34--moved-to-xavierjohntrellismicroservices) (Recipe 2 in the microservices cookbook) |

### Mistake-regression routing

These rows route recurring LLM lab mistakes to the most relevant reference before code is written.

| If the task involves... | Read first | Why |
|---|---|---|
| Loading independent resources before creating a command result | [Recipe 21](trellis-api-cookbook.md#recipe-21--parallel-independent-loads-in-handlers-resultparallelasync--whenallasync) | Sequential awaits over genuinely independent loads (HTTP + DB, two upstream services) serialise latency. `Result.ParallelAsync(...).WhenAllAsync()` is the framework idiom. **Do NOT use against repositories sharing a scoped `DbContext`** — that races EF Core and throws; sequential is correct there. |
| Overdue/date-filter queries over `Maybe<DateTime>` | [Recipe 8](trellis-api-cookbook.md#recipe-8--ef-core-maybepropertymapping-for-nullable-value-objects), then [trellis-api-efcore.md](trellis-api-efcore.md#patterns-index) | Keep a typed specification and use `MaybeQueryableExtensions` in EF queries. |
| State transitions on an aggregate | [Recipe 9](trellis-api-cookbook.md#recipe-9--state-machine-canfire--fire-pattern-with-fireresult), then [trellis-api-statemachine.md](trellis-api-statemachine.md#patterns-index) | Keep transition methods consistent and put domain mutation after `FireResult` succeeds. |
| Cross-aggregate mutation such as cancel/return releasing stock | [Recipe 1](trellis-api-cookbook.md#recipe-1--crud-aggregate-ddd-value-objects--entity--repository-contract), [Recipe 2](trellis-api-cookbook.md#recipe-2--command--handler--fluentvalidation--ef-persistence), and [trellis-api-core.md](trellis-api-core.md#domain-driven-design) | The application handler orchestrates multiple aggregates; an aggregate mutates only itself. |
| Single-loop mutate-as-you-validate over a collection of related aggregates (reserve stock per line item, etc.) | [Recipe 25](trellis-api-cookbook.md#recipe-25--two-pass-validate-then-mutate-over-a-collection-of-related-aggregates) | A later element's validation failure leaves earlier elements partially mutated in memory. Validate every fallible domain check across every participating aggregate before the first state-changing call. |
| Result-returning ASP endpoints | [Recipe 4](trellis-api-cookbook.md#recipe-4--minimal-api-endpoint-wiring-resultt--httpresponseoptionsbuilder--tohttpresponse), [Recipe 5](trellis-api-cookbook.md#recipe-5--mvc-controller-using-asactionresult), then [trellis-api-asp.md](trellis-api-asp.md#patterns-index) | `AddTrellisAsp()` is required for Result-to-HTTP mapping; exception middleware is not the mapper. |
| Pagination endpoints with bound `string? cursor` / `int? limit` parameters | [`HttpRequestPaginationExtensions`](trellis-api-asp.md#httprequestpaginationextensions), then [Recipe 3](trellis-api-cookbook.md#recipe-3--query-handler-returning-paget-paginated-list-with-cursor) | MVC can normalize a present empty cursor to null, and host integer binding can return 400 before Trellis emits a coded 422. Parse `Request.Query` through `TryCreatePageRequest()`, declare query input origin for downstream cursor decoding, and declare OpenAPI parameters separately. |
| Failure-code OpenAPI metadata or `.http` examples | [trellis-api-asp.md](trellis-api-asp.md#endpoint-checklist-for-generated-apis), [trellis-api-testing-aspnetcore.md](trellis-api-testing-aspnetcore.md#api-failure-path-test-checklist) | Generated APIs need failure paths, not happy-path-only docs/tests. |
| Reading operands out of a failure's `detail` text — splitting `"Valid values: "`, parsing bounds out of a sentence | [Recipe 39](trellis-api-cookbook.md#recipe-39--rendering-a-validation-failure-in-the-callers-language-code--args) | Prose belongs to whichever producer noticed the failure, and one failure can have several producers wording it differently. `Code` is stable and `Args` carries the operands as typed values. |
| Sorting by non-unique/computed values, reusing a cursor with different filters, or seeking after `Take` | [Recipe 3](trellis-api-cookbook.md#recipe-3--query-handler-returning-paget-paginated-list-with-cursor), [Recipe 40](trellis-api-cookbook.md#recipe-40--computed-pagination-with-validated-query-bound-continuation-state) | Ordering and seek must share a unique tie-breaker; validate query context and apply the boundary before limiting the candidates. |
| Resource authorization guards | [Recipe 7](trellis-api-cookbook.md#recipe-7--authorization-iactorprovider--iauthorize--resource-based-auth), then [trellis-api-authorization.md](trellis-api-authorization.md#patterns-index) | Use `Result.Ensure` for owner/admin boolean guards. |

## The rest of the set

| File | What it covers |
|---|---|
| [`trellis-api-cookbook.md`](trellis-api-cookbook.md#recipe-1--crud-aggregate-ddd-value-objects--entity--repository-contract) | End-to-end recipes spanning packages. Open a recipe body when the lookup above sends you to one. |
| [`trellis-api-core.md`](trellis-api-core.md#use-this-file-when) | `Result<T>`, `Maybe<T>`, `Error`, aggregates, entities, specifications, pagination. |
| [`trellis-api-analyzers.md`](trellis-api-analyzers.md#use-this-file-when) | The analyzer and generator diagnostics, `TRLS001`-`TRLS066`, and `TrellisDiagnosticIds`. |
| [`trellis-api-anti-patterns.md`](trellis-api-anti-patterns.md#trls001--result-return-value-not-handled) | Ready-to-apply WRONG/FIX shapes for the analyzer diagnostics (`TRLSxxx`). |
| [`trellis-value-object-taxonomy.md`](trellis-value-object-taxonomy.md#patterns-index) | Choosing a value-object category: scalar, symbolic, structured, optional. |

The remaining `trellis-api-*.md` files cover the optional packages — `trellis-api-efcore.md` for
`Trellis.EntityFrameworkCore`, and so on. The lookup tables above name the right one per task, so route
through them rather than opening files speculatively.

**A file being present here does not mean the project you are editing references that package.** The
complete first-party set ships with `Trellis.Core`, so references for packages you have not installed
are present by design — that is how you discover a module worth adopting. The `agentdocs` command
installs the references of the packages you approved in `.agentdocs/policy.json` and removes them when the package
is no longer approved or no longer publishes guidance. Packages published from other repositories (for example
`Trellis.ServiceLevelIndicators`) ship their own reference alongside themselves, so those appear
only once installed.

So before writing code against one of these files, confirm the package is actually referenced by the
project you are editing — check its `.csproj` or `Directory.Packages.props`. If it is not, the
reference still tells you what adopting the package would buy; say that, rather than emitting code
that cannot compile.
