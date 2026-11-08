---
package: Trellis (start here)
namespaces: [Trellis]
types: [orientation, routing]
related_docs: [trellis-cross-cutting-tips.md, trellis-api-core.md, trellis-api-anti-patterns.md, trellis-api-analyzers.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: required
agent_description: "Routing head for every Trellis task: which reference to open, the recipe lookup and how to read the set. Read before writing or changing code that uses Trellis."
---

# Trellis — start here

These versioned references ship with `Trellis.Core`. The optional `agentdocs` local tool installs them
after approving Core in `.agentdocs/policy.json`. This self-contained **routing head** is the only
always-required read; it selects task references and recipes. Sibling-file links are relative.

## Read the references yourself — do not delegate them to a sub-agent

Read code-shaping references yourself; summaries lose exact signatures and invite invented APIs.
Delegate *verdicts* (builds, tests, file searches, accuracy audits), not reference learning.

## Do not guess

Trace every Trellis method, overload and attribute to its owning reference; stop and read it rather
than reconstructing signatures from memory. Bases such as `RequiredGuid<TSelf>` and `Aggregate<TId>`
already supply members that are compile errors to redeclare; Recipe 1 lists the inherited surface.

## How to read these recipes

**Hold this file resident; read recipe bodies on demand.** The 39 recipe documents (`skills/recipe-*.md`)
cover the routed tasks; most need one to three. Follow the [task lookup table](#task---recipe-lookup),
reading bodies, not just titles. TRLDOC007 enforces that every live recipe is reachable.

Every recipe follows the same shape:

1. **Problem statement** — the consumer's task.
2. **Solution code** — copy-pasteable C# using only documented public APIs.
3. **What it shows** — the cross-cutting concept.
4. **Anti-pattern → fix** *(when applicable)* — the mistake and its Trellis analyzer.

Conventions used throughout:

- Types use namespace `Trellis` unless noted (`Trellis.Asp`, `Trellis.Asp.Authorization`, `Trellis.EntityFrameworkCore`, `Trellis.Analyzers`).
- Snippets use C# 12+ (file-scoped namespaces, primary constructors, collection expressions); Trellis targets `net10.0`.
- Construct results with `Result.Ok` / `Result.Fail`. `default(Result<T>)` is a typed failure, not success.
- Async pipelines use `*Async` extensions; sync chain methods on `Task<Result<T>>` trigger `TRLS009`.
- Examples use `OrderId : RequiredGuid<OrderId>` and an `Order` aggregate; substitute your types, not the structure.
- Payload-free commands return `Result<Trellis.Unit>`, qualified to avoid ambiguity with `Mediator.Unit`. In `GlobalUsings.cs`, `global using TrellisUnit = Trellis.Unit;` or `global using Unit = Trellis.Unit;` avoids qualification; Trellis code never uses `Mediator.Unit`.

### Preferred patterns, not just valid overloads

Recipe examples are the defaults. Use alternatives only for a documented trade-off, and state why:

- Prefer `Result.EnsureNotNull(value, fieldName, detail)` (or async) for required nullables: standard errors are lazy. It checks null only, not blanks/domain rules. For nonblank strings, `value.EnsureNotNullOrWhiteSpace(fieldName, detail)` emits `ValueNotNull` for null, `ValueNotEmpty` for empty/whitespace, only on failure, without trimming. Use its `Func<Error>` overload for custom failure.
- Construct custom guard/conversion errors **inside** an available factory: `Result.Ensure(condition, () => new Error.Forbidden(...))`, `Result.EnsureNotNull(value, () => ...)`, or `maybe.ToResult(() => ...)`. Value-threaded `Ensure` uses `value => error`, not a parameterless factory. Eager construction wastes work and may record violations even when a guard passes or an earlier failure skips it.
- Prefer `static` callbacks without captured state. Capturing factories may allocate closures; laziness avoids unused errors, not all allocations. Returning a pre-created error does not undo its allocation. Use eager overloads for existing/reused errors or APIs without factories; do not invent overloads.
- Use `Result.Ensure` for payload-free boolean guards, `EnsureNotNull` for required nullables, and `Result.Ok(value)` for success wrapping. A `Result<Unit>` guard is not payload-bearing success.
- Prefer expression-based EF mappings like `builder.OwnsMany(order => order.LineItems, ...)` when convention binds the backing field. String-based mappings are a fallback; value-object collections need explicit ownership mapping only to override conventions.

## LLM preflight: load the smallest correct reference set

Choose the task and open required references at their **Patterns Index**. Read complete selected APIs,
constraints and linked behavior/preflight sections, not unrelated catalogs. Expand for inherited
surfaces and cross-package ordering; section-first reading waives no package or safety contract.
Recipes cover end-to-end composition; direct API routes suffice for small guards/conversions.

| If you are changing... | Load these references before coding | Why |
|---|---|---|
| Result, Maybe, errors, value-object bases, aggregates, specifications, pagination | [Core index](trellis-api-core.md#patterns-index); selected recipe for end-to-end work | Core owns the ROP primitives and DDD base types used by every package. |
| ASP.NET endpoints, controllers, response mapping, ETags, Prefer, ranges, actor providers | [ASP index](trellis-api-asp.md#patterns-index), [Core index](trellis-api-core.md#patterns-index); add [Mediator preflight](trellis-api-mediator.md#cross-package-preflight-for-pipeline-changes) when endpoints send messages | `ToHttpResponse` and scalar validation are ASP-owned, while handlers and result shapes come from Core/Mediator. |
| Mediator handlers, pipeline behaviors, validation, authorization, domain events | [Mediator preflight](trellis-api-mediator.md#cross-package-preflight-for-pipeline-changes), [Core index](trellis-api-core.md#patterns-index), selected recipe; [EF unit-of-work](trellis-api-efcore.md#transactionalcommandbehaviortmessage-tresponse) and [authorization index](trellis-api-authorization.md#patterns-index) for composed stages | Preserve cross-package pipeline ordering and opt-in behavior contracts. |
| EF Core persistence, repositories, unit of work, `Maybe<T>` queries, `[OwnedEntity]` | [EF index](trellis-api-efcore.md#patterns-index), [Core index](trellis-api-core.md#patterns-index), selected recipe; [Mediator preflight](trellis-api-mediator.md#cross-package-preflight-for-pipeline-changes) when commits happen through handlers | EF owns mapping/interceptors; Mediator owns when command commits run. |
| FluentValidation integration | [FluentValidation index](trellis-api-fluentvalidation.md#patterns-index), [Mediator preflight](trellis-api-mediator.md#cross-package-preflight-for-pipeline-changes), selected recipe | FluentValidation plugs into `ValidationBehavior` through `IMessageValidator<TMessage>`; it is not a separate pipeline behavior. |
| Composition-root helpers (`AddTrellis`, `UseXxx`) | [ServiceDefaults index](trellis-api-servicedefaults.md#patterns-index), selected recipe, plus the selected modules' API and ordering sections | `TrellisServiceBuilder` preserves canonical order but does not register app-owned services like `DbContext` or Mediator handlers. |
| HTTP client adapters | [HTTP index](trellis-api-http.md#patterns-index), [Core index](trellis-api-core.md#patterns-index), selected recipe | The HTTP package maps upstream responses into Core `Result<T>` / `Maybe<T>` shapes. |
| Tests | [Testing index](trellis-api-testing-reference.md#patterns-index); add [ASP testing index](trellis-api-testing-aspnetcore.md#patterns-index) for `WebApplicationFactory` or `.http` replay | Unit/helper assertions and ASP integration helpers live in separate test packages. |
| Analyzer diagnostics | Matching [WRONG/FIX section](trellis-api-anti-patterns.md#trls001--result-return-value-not-handled), then the [diagnostic index](trellis-api-analyzers.md#diagnostics) and its detail link, then the named package API section | Adapt the canonical control-flow shape; check the exact API and code-fix limitations. |

Completion: trace each Trellis call to a loaded reference; wire modules in documented order; update the owning reference for public API/behavior changes, plus the affected recipe document (`skills/recipe-*.md`) and its router row.

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
| Create or load an aggregate with value objects | [Recipe 1](skills/recipe-01-crud-aggregate.md) |
| Write a command handler that validates and persists | [Recipe 2](skills/recipe-02-command-handler.md), then [Recipe 16](skills/recipe-16-unit-of-work.md) |
| Persist business changes and audit records in one commit, or refuse a command that recorded no audit | [Recipe 16: same-commit audit staging](skills/recipe-16-unit-of-work.md#same-commit-audit-staging), then [inside-transaction behaviors](trellis-api-mediator.md#insidetransactionbehaviorservicecollectionextensions) |
| Share handler/audit data without changing an entity response | [Recipe 16: application-owned recorder and checkpoint](skills/recipe-16-unit-of-work.md#same-commit-audit-staging) |
| Load multiple independent resources in one handler (HTTP + DB, two upstream services, factory-created `DbContext`s) | [Recipe 21](skills/recipe-21-parallel-loads.md) |
| Multi-aggregate orchestration: side effect per element of a related-aggregate set | [Recipe 22](skills/recipe-22-multi-aggregate-orchestration.md) |
| Apply an operation to every element of a related-aggregate set where per-element validation can fail (reserve stock per line item, etc.) — avoid partial mutation and grouped-quantity overflow | [Recipe 25](skills/recipe-25-two-pass-validate-mutate.md) |
| Concurrency control on mutating endpoints — require or optionally honor `If-Match` before mutation | [Recipe 23](skills/recipe-23-if-match-concurrency.md) |
| Save bandwidth on reads — return `304 Not Modified` when the client's `If-None-Match` still matches | [Recipe 6](skills/recipe-06-conditional-get.md) |
| Add a paginated list query | [Recipe 3](skills/recipe-03-paginated-query.md) |
| Generate the same named-route pagination links with or without API versioning | [`HttpContext.PageUrl`](trellis-api-asp.md#httpcontextpaginationextensions), then [optional versioning policy](trellis-api-asp-apiversioning.md#trellisaspoptionsapiversioningextensions) for versioned hosts |
| Parse cursor/limit query input in MVC or Minimal APIs without treating `?cursor=` as missing | [`HttpRequestPaginationExtensions`](trellis-api-asp.md#httprequestpaginationextensions), then [Recipe 3](skills/recipe-03-paginated-query.md) |
| Paginate a translated spherical distance or an application-computed score with validated continuation state bound to query context | [Recipe 40](skills/recipe-40-computed-pagination.md) |
| Validate geographic coordinates, calculate in-memory distance, build conservative bounds, or compose an EF Core radius query | [`GeoCoordinate`](trellis-api-primitives.md#geocoordinate) and [`GeoBounds`](trellis-api-primitives.md#geobounds), then [`GeoCoordinateExpressions` in the EF Core reference](trellis-api-efcore.md#geocoordinateexpressions) for database queries; use Recipe 40 for distance pagination |
| Model weekly availability, overnight periods, or time-zone-aware membership | [`WeeklySchedule` and `WeeklyPeriod` in the Primitives reference](trellis-api-primitives.md#weeklyschedule); use [Recipe 13's DTO seam](skills/recipe-13-composite-json-and-collections.md#supported-property-shapes-inside-a-composite-vo--when-to-map-to-a-dto-instead) for JSON |
| Add Minimal API or MVC endpoints | [Recipe 4](skills/recipe-04-minimal-api-endpoint.md), [Recipe 5](skills/recipe-05-mvc-controller.md) |
| Return 201 for a PUT upsert without an application-layer URL, or let the endpoint supply a Created Location | [Recipe 4](skills/recipe-04-minimal-api-endpoint.md), then [write outcomes](trellis-api-http-abstractions.md#writeoutcomet-case-payloads) |
| Generate versioned Location links to a named route or MVC action, including cross-route segment pins | [Recipe 4](skills/recipe-04-minimal-api-endpoint.md), then [target-aware API versioning](trellis-api-asp-apiversioning.md#behavioral-notes) |
| Map primitive DTO fields to value objects | [Recipe 18](skills/recipe-18-dto-to-value-object.md) |
| Require an exact monetary input before rounding or normalized comparison/deduplication | [Recipe 18's monetary boundary](skills/recipe-18-dto-to-value-object.md#exact-input-monetary-boundaries-before-normalization) |
| Require nullable values without `!`, or guard a nullable-returning query | [`Result.EnsureNotNull` / `EnsureNotNullAsync`](trellis-api-core.md#value-returning-null-guards); add [Recipe 18](skills/recipe-18-dto-to-value-object.md) for command-factory composition |
| Require a nonblank string without trimming or constructing unused errors | [`EnsureNotNullOrWhiteSpace`](trellis-api-core.md#required-nonblank-strings); no composite recipe is needed |
| Migrate universal or nullable `ToResult` calls without losing payloads or treating absence as success | [Choosing a Result entry point](trellis-api-core.md#choosing-a-result-entry-point), then [Recipe 18](skills/recipe-18-dto-to-value-object.md) |
| Add resource authorization | [Recipe 7](skills/recipe-07-resource-authorization.md) |
| Obtain checked actor/resource parameters without provider/accessor constructor dependencies | [Recipe 7](skills/recipe-07-resource-authorization.md), [Recipe 24](skills/recipe-24-indirect-authorization.md), [Recipe 31](skills/recipe-31-authorized-resource.md), then [actor-aware handler bases](trellis-api-mediator.md#actor-aware-handler-bases) |
| Authorize against a related resource one or more navigation hops away (cricket-style fan-out, owner chains) | [Recipe 24](skills/recipe-24-indirect-authorization.md) |
| Enforce tenant isolation on a command (per-command scope check, no base type) | [Recipe 38](skills/recipe-38-tenant-authorization.md) |
| Map `Maybe<T>` or composite value objects with EF Core | [Recipe 8](skills/recipe-08-maybe-ef-mapping.md), [Recipe 13](skills/recipe-13-composite-value-object.md) |
| Choose JSON transport for a composite value object with nested/optional fields | [Recipe 13's JSON boundary](skills/recipe-13-composite-json-and-collections.md#composite-value-object-json-wire-shape) and [DTO seam](skills/recipe-13-composite-json-and-collections.md#supported-property-shapes-inside-a-composite-vo--when-to-map-to-a-dto-instead) |
| Map an aggregate's read-only collection with a private backing field | [Recipe 13's collection mapping](skills/recipe-13-composite-json-and-collections.md#owned-collections-with-a-private-backing-field) |
| Add optional request/response fields | [Recipe 14](skills/recipe-14-optional-dto-fields.md) |
| Read optional HTTP resources where 404 means absent | [Recipe 19](skills/recipe-19-http-client.md) |
| Choose between fail-fast and accumulating-error collection ops, including indexed validation | [Recipe 20](skills/recipe-20-fail-fast-vs-accumulating.md) |
| Return synchronous `Result` chains from `Task`/`ValueTask` APIs | [Recipe 2](skills/recipe-02-command-handler.md), then `AsTask()` / `AsValueTask()` in [trellis-api-core.md](trellis-api-core.md#task-adapter-family--resulttaskadapterextensions) |
| Create HTTP-oriented resource errors | Use `ResourceRef.For<TResource>(id)` from [trellis-api-core.md](trellis-api-core.md#supporting-types) |
| Point `ProblemDetails.Instance` at the resource that failed, instead of leaving it null or hand-formatting a URI | [Recipe 28](skills/recipe-28-problem-details-instance.md) |
| Add a state transition | [Recipe 9](skills/recipe-09-state-machine.md) |
| Write handler/domain tests | [Recipe 10](skills/recipe-10-handler-tests.md) |
| Replay service `.http` examples in CI, generate fresh GUIDs, or diagnose unresolved placeholders | [`.http` replay helpers](trellis-api-testing-aspnetcore.md#http-file-replay-helpers), then [replay variables and failure handling](trellis-api-testing-aspnetcore.md#replay-variables-and-failure-handling) |
| Diagnose bare Production 400 responses or missing field pointers from Minimal API value-object binding | [ASP semantic JSON validation](trellis-api-asp.md#minimal-api-semantic-json-validation), then [scalar path tracking](trellis-api-asp.md#scalarvaluepathtracking); compare structured fields, not producer prose |
| Write integration tests for a `BackgroundService` worker | [Recipe 26](skills/recipe-26-worker-tests.md) |
| Insert a row idempotently on a unique constraint (de-duplicated worker outbox, "save unless exists") | [Recipe 27](skills/recipe-27-idempotent-insert.md) |
| Match duplicate-key, foreign-key, or retry abort/exhaustion conflicts without copying wire strings | [`FaultCodes` vocabulary](trellis-api-core.md#validationcodes--the-reason-code-vocabulary), then [EF save helpers](trellis-api-efcore.md#dbcontextextensions) or [Recipe 27](skills/recipe-27-idempotent-insert.md) |
| Make POST / PATCH safe under client retries with a quoted RFC 9651 `Idempotency-Key` String | [Recipe 29](skills/recipe-29-idempotency-key.md) |
| Render ASP.NET Core rate-limit rejections as Trellis 429 Problem Details with optional `Retry-After` | [`RateLimiterOptionsExtensions` in the ASP reference](trellis-api-asp.md#ratelimiteroptionsextensions) |
| Partition ASP.NET rate limits by canonical identity without permission loads | [Recipe 41](skills/recipe-41-actor-rate-limiting.md) |
| Define domain events | [Recipe 17](skills/recipe-17-domain-events.md) |
| Make domain events survive a crash (transactional outbox) | [Recipe 35](skills/recipe-35-outbox.md) |
| Publish stable integration contracts locally or through outbox/broker/Azure Service Bus | [Recipe 36](skills/recipe-36-integration-events.md), [messaging roles](trellis-api-servicedefaults.md#messaging-setup-by-deployment-role), and [Azure registration](trellis-api-messaging-azureservicebus.md#registration) |
| Connect an inbound message, domain outbox row, translated integration event, and W3C trace | [Recipe 36](skills/recipe-36-integration-events.md), then [`IntegrationMessageContext`](trellis-api-mediator.md#integrationmessagecontext) and [outbox lineage](trellis-api-efcore-outbox.md#outboxmessage) |
| Consume broker messages idempotently, or inspect/replay inbox evidence | [Inbox wiring](trellis-api-efcore-inbox.md#wiring-two-required-calls), [Azure settlement](trellis-api-messaging-azureservicebus.md#settlement), and [inbox operations](trellis-api-efcore-inbox.md#operational-requirements-and-transport-boundary) |
| Enable outbox readiness, durable capture-age or application-provided consumer-lag evidence; monitor metadata in a split API/worker host; or tune its explicitly selected profile | [Outbox readiness](trellis-api-efcore-outbox.md#outbox-readiness), including [capture-age rollout](trellis-api-efcore-outbox.md#durable-capture-age-and-rollout), [consumer-lag provider](trellis-api-efcore-outbox.md#iconsumerlagprovider-and-consumerlagevidence), [split-host selection](trellis-api-efcore-outbox.md#split-apiworker-hosts), and [inbox evidence requirements](trellis-api-efcore-inbox.md#consumer-lag-health-requirements); each signal is opt-in and disabled is not observed healthy |
| Show a validation failure in the user's language, or render your own message from a 422 instead of showing the server's English | [Recipe 39](skills/recipe-39-localized-validation.md) |
| Fix analyzer warnings | [Recipe 11](skills/recipe-11-analyzer-fixes.md) |
| Keep fresh GUIDs out of error/violation reason codes while retaining valid incident/resource IDs | [TRLS067 WRONG/FIX](trellis-api-anti-patterns.md#trls067--fresh-guid-derived-reason-code), then [semantic scope](trellis-api-analyzers.md#perincidentreasoncodeanalyzer--trls067) |
| Wire the composition root | [Recipe 12](skills/recipe-12-di-wiring.md) |
| Rehydrate an entity from a database row (fail-loud vs Result-track) | [Recipe 30](skills/recipe-30-rehydration.md) |
| Reconstitute an aggregate in a non-EF repository without re-running its factory (no re-validation, no events) | [Recipe 37](skills/recipe-37-reconstitution.md) |
| Migrate a FunctionalDDD 2.x application to Trellis | [Migration guide](trellis-api-migration.md#use-this-file-when); not needed for current-API work |
| Avoid the pipeline-then-handler duplicate load when a command both authorizes and mutates the same resource | [Recipe 31](skills/recipe-31-authorized-resource.md) |
| Hide existence of sensitive resources — give missing, removed, and withheld resources the same public `NotFound`, optionally with fixed code/detail | [Recipe 32](skills/recipe-32-hide-existence.md) |
| Configure the strict `AddJwtBearer` validation profile + key-rotation runbook for a gateway-minted internal JWT | [Moved: xavierjohn/Trellis.Microservices](https://github.com/xavierjohn/Trellis.Microservices/blob/main/docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md) (Recipe 1 in the microservices cookbook) |
| Stand up the gateway side of the Path B microservices pattern (YARP transform that mints the internal JWT) | [Moved: xavierjohn/Trellis.Microservices](https://github.com/xavierjohn/Trellis.Microservices/blob/main/docs/docfx_project/api_reference/trellis-api-microservices-cookbook.md) (Recipe 2 in the microservices cookbook) |

### Mistake-regression routing

These rows route recurring LLM lab mistakes to the most relevant reference before code is written.

| If the task involves... | Read first | Why |
|---|---|---|
| Loading independent resources before creating a command result | [Recipe 21](skills/recipe-21-parallel-loads.md) | Sequential awaits over genuinely independent loads (HTTP + DB, two upstream services) serialise latency. `Result.ParallelAsync(...).WhenAllAsync()` is the framework idiom. **Do NOT use against repositories sharing a scoped `DbContext`** — that races EF Core and throws; sequential is correct there. |
| Overdue/date-filter queries over `Maybe<DateTime>` | [Recipe 8](skills/recipe-08-maybe-ef-mapping.md), then [trellis-api-efcore.md](trellis-api-efcore.md#patterns-index) | Keep a typed specification and use `MaybeQueryableExtensions` in EF queries. |
| State transitions on an aggregate | [Recipe 9](skills/recipe-09-state-machine.md), then [trellis-api-statemachine.md](trellis-api-statemachine.md#patterns-index) | Keep transition methods consistent and put domain mutation after `FireResult` succeeds. |
| Cross-aggregate mutation such as cancel/return releasing stock | [Recipe 1](skills/recipe-01-crud-aggregate.md), [Recipe 2](skills/recipe-02-command-handler.md), and [trellis-api-core.md](trellis-api-core.md#domain-driven-design) | The application handler orchestrates multiple aggregates; an aggregate mutates only itself. |
| Single-loop mutate-as-you-validate, or discarding results after a cross-aggregate preflight | [Recipe 25](skills/recipe-25-two-pass-validate-mutate.md) | Preflight every fallible domain check before mutation, then prove each guard stays valid until its matching call, including through earlier mutations. A shared clock is not a frozen instant; choose execution-time versus snapshot-time eligibility explicitly. Propagating a late failure or skipping a DB commit does not undo in-memory changes. |
| Result-returning ASP endpoints | [Recipe 4](skills/recipe-04-minimal-api-endpoint.md), [Recipe 5](skills/recipe-05-mvc-controller.md), then [trellis-api-asp.md](trellis-api-asp.md#patterns-index) | `AddTrellisAsp()` is required for Result-to-HTTP mapping; exception middleware is not the mapper. |
| Pagination endpoints with bound `string? cursor` / `int? limit` parameters | [`HttpRequestPaginationExtensions`](trellis-api-asp.md#httprequestpaginationextensions), then [Recipe 3](skills/recipe-03-paginated-query.md) | MVC can normalize a present empty cursor to null, and host integer binding can return 400 before Trellis emits a coded 422. Parse `HttpRequest.Query` through `TryCreatePageRequest()`, declare query input origin for downstream cursor decoding, and declare OpenAPI parameters separately. |
| Failure-code OpenAPI metadata or `.http` examples | [trellis-api-asp.md](trellis-api-asp.md#endpoint-checklist-for-generated-apis), [trellis-api-testing-aspnetcore.md](trellis-api-testing-aspnetcore.md#api-failure-path-test-checklist) | Generated APIs need failure paths, not happy-path-only docs/tests. |
| Reading operands out of a failure's `detail` text — splitting `"Valid values: "`, parsing bounds out of a sentence | [Recipe 39](skills/recipe-39-localized-validation.md) | Prose belongs to whichever producer noticed the failure, and one failure can have several producers wording it differently. `Code` is stable and `Args` carries the operands as typed values. |
| Sorting by non-unique/computed values, reusing a cursor with different filters, or seeking after `Take` | [Recipe 3](skills/recipe-03-paginated-query.md), [Recipe 40](skills/recipe-40-computed-pagination.md) | Ordering and seek must share a unique tie-breaker; validate query context and apply the boundary before limiting the candidates. |
| Resource authorization guards | [Recipe 7](skills/recipe-07-resource-authorization.md), then [trellis-api-authorization.md](trellis-api-authorization.md#patterns-index) | Use `Result.Ensure` for owner/admin boolean guards. |

## The rest of the set

| File | What it covers |
|---|---|
| [`skills/recipe-*.md`](skills/recipe-01-crud-aggregate.md) | One document per end-to-end recipe spanning packages. Open the one the lookup above sends you to. |
| [`trellis-cross-cutting-tips.md`](trellis-cross-cutting-tips.md#cross-cutting-tips) | Habits that apply to every task: analyzers in CI, parallel loads, sync versus async chains, errors. |
| [`trellis-api-core.md`](trellis-api-core.md#use-this-file-when) | `Result<T>`, `Maybe<T>`, `Error`, aggregates, entities, specifications, pagination. |
| [`trellis-api-migration.md`](trellis-api-migration.md#use-this-file-when) | FunctionalDDD-to-Trellis package, namespace and API changes; open only when migrating a FunctionalDDD application. |
| [`trellis-api-analyzers.md`](trellis-api-analyzers.md#use-this-file-when) | The analyzer and generator diagnostics, `TRLS001`-`TRLS067`, and `TrellisDiagnosticIds`. |
| [`trellis-api-anti-patterns.md`](trellis-api-anti-patterns.md#trls001--result-return-value-not-handled) | Ready-to-apply WRONG/FIX shapes for the analyzer diagnostics (`TRLSxxx`). |
| [`trellis-value-object-taxonomy.md`](trellis-value-object-taxonomy.md#patterns-index) | Choosing a value-object category: scalar, symbolic, structured, optional. |

Other `trellis-api-*.md` files cover optional packages; use the lookup tables, not speculative reads.

**File presence does not mean the project references that package.** Core ships all first-party
references for discovery, including uninstalled modules. `agentdocs` installs approved packages'
references and removes them when approval or published guidance ends. Other repositories' packages
(e.g. `Trellis.ServiceLevelIndicators`) ship their own references, visible only once installed.

Before coding, confirm the project's package reference in its `.csproj` or `Directory.Packages.props`.
If absent, explain what adoption would enable; do not emit code that cannot compile.
