# GitHub Copilot Instructions for Trellis

## Project overview

Trellis is an AI-native framework that helps create consistent, reliable enterprise software with railway-oriented programming, DDD primitives, ASP.NET integration, EF Core integration, and value objects.

These instructions are for repository workflow and contribution conventions only. They are not the source of truth for how to use Trellis APIs.

## API usage source of truth

Before writing or changing code that uses Trellis APIs, read the relevant files in `docs/docfx_project/api_reference/`.

### The one file to hold in memory

**`docs/docfx_project/api_reference/trellis-api-cookbook.md` is the start file. Read its routing head first — everything from the top of the file through the end of `## Patterns Index`, which stops at the first `## Recipe` heading — and keep that resident for the whole session.**

The full reference set is ~301K tokens — it does not fit in context and is not meant to. The cookbook is the router: its task-lookup table maps a task to the right recipe, its mistake-regression table maps a recurring error to the reference that prevents it, and its preflight table names exactly which package references a task needs. That routing head is only ~4K tokens. The 36 recipe bodies below it are the other ~57K, and a typical task reads one to three of them (~1.25K tokens each).

**So hold the routing head, and read recipe bodies on demand.** Holding all 36 bodies costs ~57K tokens permanently to keep ~54K of them that you will not open — more than a quarter of a 200K context spent on content the task never needed. Read a body the moment the index routes you to one; do not work from a recipe's title.

This trade is safe only because the index is a complete map, which **TRLDOC007** enforces: every live recipe is reachable from `## Patterns Index`, and rows are phrased as the reader's task or failure mode rather than the recipe's title. If you find yourself guessing whether a recipe exists, re-read the index rows — do not assume its absence.

Then pull in only the 1–3 area-specific references the cookbook points you at. Do not infer Trellis API behavior from these Copilot instructions.

If context is too tight to hold the routing head plus one area reference, you are too tight to write correct Trellis code — say so rather than guessing at API shapes.

`trellis-start-here.md` is **not** a second entry point, and in this repository you can ignore it. It is a ~1 KB router written for *consumers*: the packages copy the reference docs into a consuming repository's `.github/`, where nothing explains what those files are, so that router tells a consumer's agent the same thing this section tells you — read the cookbook first, keep it loaded, don't delegate it. Edit it only when this guidance changes, and keep the two in agreement. There is exactly one start file, the cookbook; `trellis-start-here.md` just points at it for readers who never see this file.

### Do not delegate reference reading to a sub-agent

**Read the cookbook and the package references directly, in your own context. Never send a sub-agent to read them for you.**

A sub-agent returns a summary; the reference content itself never enters your context, so you end up writing code from the sub-agent's paraphrase instead of from the exact signatures. That is precisely the failure mode the references exist to prevent, and it reintroduces invented APIs and wrong overloads.

Sub-agents are still appropriate for work whose *output* is a verdict rather than knowledge you must then write code against — auditing docs for accuracy, running builds and tests, searching for a specific file. The rule is narrow: if the answer determines the code you are about to write, read it yourself.

### Recommended context size

The reference set is the 27 `*.md` files under `docs/docfx_project/api_reference/` — 25 `trellis-api-*.md` files plus `trellis-value-object-taxonomy.md` and the tiny `trellis-start-here.md` router — totalling ~1,184 KB (~303K tokens); the cookbook alone is ~239 KB (~61K tokens), of which only its ~4K-token routing head is held resident. (`completeness-report.md` sits in the same directory but is a generated audit artifact, not a reference — the lint script therefore scans 28 files.) These figures grow as the docs do — treat them as approximate. Together with framework source needed for cross-checking, project source under edit, and accumulated tool output across a typical 30–50 turn session, the working set is **1.5–2.5 MB**.

| Tier | Context | When this is enough |
|---|---|---|
| **Minimum** | 200K | Narrow, single-file tasks. Holds the cookbook routing head, a handful of recipe bodies and one area reference; cross-cutting work is error-prone at this tier. |
| **Recommended** | 400–500K | Most consumer projects. Lets the routing head + 5–6 area-specific references stay resident through a PR-sized session. |
| **Comfortable** | 1M | Framework-internal work and greenfield projects with multiple integration points. Lets all 27 references stay resident from turn 1 without eviction. |

### Mandatory loads at session start

For any non-trivial Trellis work, load these **yourself** (see the sub-agent rule above) **before** writing the first line of code:

1. The `trellis-api-cookbook.md` routing head — always. Everything above the first `## Recipe` heading is the entry point, and it stays resident; recipe bodies are read on demand as the index routes you to them.
2. `trellis-api-servicedefaults.md` — always. Composition-root features have a matching `TrellisServiceBuilder.UseXxx()` slot for their `services.AddXxx()` extension; leaf/store/adapter-author registrations deliberately have **no** slot. See "Adding a new public registration API" below for the rule and the current exception list. Designing or modifying a registration helper without reading this file either silently misses a builder slot that should exist, or wrongly adds one where none belongs.
3. The area-specific reference for the package being modified (from the table below).
4. The reference for **every package whose pipeline this work composes with**. Specifically: anything touching the Mediator pipeline must also load `trellis-api-efcore.md` (transactional behavior) and `trellis-api-authorization.md` (resource-authorization behavior); anything touching ASP must also load `trellis-api-mediator.md`.

| When touching... | Read first |
|---|---|
| Result, Maybe, Error, ROP operations, aggregates, entities, specifications | `docs/docfx_project/api_reference/trellis-api-core.md` |
| Ready-to-use value objects and primitive attributes | `docs/docfx_project/api_reference/trellis-api-primitives.md` |
| Choosing a value-object category (scalar / symbolic / structured / optional) | `docs/docfx_project/api_reference/trellis-value-object-taxonomy.md` |
| ASP.NET Core response mapping, validation, ETags, Prefer handling | `docs/docfx_project/api_reference/trellis-api-asp.md` |
| ASP.NET Core API versioning (versioned `Location`/route + pagination URLs) | `docs/docfx_project/api_reference/trellis-api-asp-apiversioning.md` |
| EF Core integration | `docs/docfx_project/api_reference/trellis-api-efcore.md` |
| Transactional outbox and domain/integration event publishing | `docs/docfx_project/api_reference/trellis-api-efcore-outbox.md` |
| Inbox and idempotent message consumption | `docs/docfx_project/api_reference/trellis-api-efcore-inbox.md` |
| Authorization | `docs/docfx_project/api_reference/trellis-api-authorization.md` |
| FluentValidation integration | `docs/docfx_project/api_reference/trellis-api-fluentvalidation.md` |
| HttpClient extensions | `docs/docfx_project/api_reference/trellis-api-http.md` |
| HTTP transport abstractions (`WriteOutcome`, `HttpError`, ETag/precondition value types) | `docs/docfx_project/api_reference/trellis-api-http-abstractions.md` |
| Azure Service Bus transport for integration events | `docs/docfx_project/api_reference/trellis-api-messaging-azureservicebus.md` |
| Mediator pipeline behaviors | `docs/docfx_project/api_reference/trellis-api-mediator.md` |
| FluentValidation in the Mediator pipeline | `docs/docfx_project/api_reference/trellis-api-mediator-fluentvalidation.md` |
| State machine integration | `docs/docfx_project/api_reference/trellis-api-statemachine.md` |
| Service defaults and composition root setup | `docs/docfx_project/api_reference/trellis-api-servicedefaults.md` |
| Testing helpers | `docs/docfx_project/api_reference/trellis-api-testing-reference.md` |
| ASP.NET Core integration-test helpers | `docs/docfx_project/api_reference/trellis-api-testing-aspnetcore.md` |
| Worker / `BackgroundService` test harness | `docs/docfx_project/api_reference/trellis-api-testing-worker.md` |
| Persistence abstractions (`IUnitOfWork`, provider-neutral contracts) | `docs/docfx_project/api_reference/trellis-api-persistence-abstractions.md` |
| Analyzer rules and diagnostic IDs | `docs/docfx_project/api_reference/trellis-api-anti-patterns.md` for ready-to-apply WRONG/FIX shapes, then `docs/docfx_project/api_reference/trellis-api-analyzers.md` for the formal spec |
| Analyzer implementations and code fixes | `Trellis.Analyzers/src/` + `docs/docfx_project/api_reference/trellis-api-analyzers.md` |
| Source generators (`Trellis.Asp/generator`, `Trellis.EntityFrameworkCore/generator`, `Trellis.Primitives/generator`, `Trellis.StateMachine/generator`) | the area reference for the package that **hosts** the generator, plus the generator's own `generator-tests/` project. There is no separate generator API reference — generated output is part of the hosting package's public surface, so any change to emitted code is an API-reference change. |

### Preflight verification — required before generating non-trivial code

Reading the references is necessary but not sufficient. Before producing any non-trivial Trellis code, **explicitly answer these in your reasoning** (one or two lines is enough, but skipping the step is not allowed):

1. **Which task am I doing?** Name the task in the cookbook's task-lookup table — verbatim if possible.
2. **Which recipe applies?** Cite the recipe number (e.g. *"Recipe 1 — CRUD aggregate"* or *"Recipe 21 — Parallel independent loads"*). If no recipe applies, name the cookbook section or package reference that does.
3. **Which inherited surface does my type already get?** For any type derived from `Aggregate<TId>`, `Entity<TId>`, `RequiredGuid<T>`, `RequiredString<T>`, `RequiredEnum<T>`, the scalar `Required*<T>` primitives (`RequiredInt<T>`, `RequiredLong<T>`, `RequiredDecimal<T>`, `RequiredBool<T>`, `RequiredDateTime<T>`, `RequiredDateTimeOffset<T>`), `ValueObject`, or `ScalarValueObject<TSelf, T>`, list the inherited members you will *not* redeclare. Recipe 1 in the cookbook enumerates the standard set for `RequiredGuid<T>`, `RequiredString<T>`, `ValueObject`, and `Aggregate<TId>`; for `Entity<TId>`, `RequiredEnum<T>`, and the other scalar primitives, consult `trellis-api-primitives.md` and `trellis-api-core.md`. The most common Recipe 1 mistake is redeclaring `Id`, equality methods, or `TryCreate` that the base class already provides.
4. **Am I about to invent an API?** If you cannot point at a specific reference file + line range for the method/extension/attribute you are about to use, stop and load that reference. Do not synthesize the signature from prior knowledge.
5. **What does the analyzer say?** If the change is in a `Result`/`Maybe`/EF-Core/value-object pipeline, list which `TRLSxxx` IDs are relevant. Cite the matching section in `trellis-api-anti-patterns.md` if one exists; otherwise cite `trellis-api-analyzers.md` and the relevant package reference. Preserve the WRONG/FIX control-flow shape from the anti-pattern file, adapting identifiers, types, and error values to the caller — the snippets are pattern examples, not self-contained replacements.

If you cannot answer any of these, stop and load the missing reference before continuing.

### Adding a new public registration API (`AddXxx` / `UseXxx`)

When adding a new `services.AddTrellisXxx()` or `services.AddXxxDispatch()` style extension, first decide whether it needs a builder slot at all:

- **Composition-root features** — anything an application author turns on (pipeline behaviors, dispatchers, actor providers, outbox/inbox) — **must** get a `TrellisServiceBuilder.UseXxx(...)` slot.
- **Leaf, store, and adapter-author extension points** get **no** slot. These are called by another `AddXxx` that *is* surfaced, or by an adapter author wiring a non-shipped provider. Existing examples: `AddInMemoryIdempotencyStore`, `AddTrellisRouteConstraint`/`AddTrellisRouteConstraints`, `AddTransactionalCommandBehavior` (provider-neutral; invoked by `AddTrellisUnitOfWork<TContext>()`, which is surfaced as `UseEntityFrameworkUnitOfWork<TContext>()`), and the **vendor-SDK provider packages**: `AddCosmosIdempotencyStore` (`Trellis.Asp.Idempotency.Cosmos`) and `AddAzureServiceBusIntegrationEventPublisher` / `AddAzureServiceBusIntegrationEventConsumer` (`Trellis.Messaging.AzureServiceBus`). Surfacing a vendor package would force every `Trellis.ServiceDefaults` consumer to take a transitive dependency on a cloud SDK in order to use features unrelated to that vendor; `Trellis.ServiceDefaults` deliberately references no vendor SDK.

If the new helper falls in the first category, the work is **not complete** until:

1. The matching `TrellisServiceBuilder.UseXxx(...)` slot is added in `Trellis.ServiceDefaults/src/TrellisServiceBuilder.cs`, with the call site placed correctly inside `Apply()` so canonical pipeline ordering is preserved.
2. The new helper is order-independent vs the other `AddTrellis*` extensions. If pipeline placement matters (e.g., the new behavior must wrap or be wrapped by `TransactionalCommandBehavior`), the registration must detect existing relevant behaviors and insert/yank-restore correctly — not just `TryAddEnumerable` and hope.
3. Both `trellis-api-mediator.md` (or the relevant area reference) **and** `trellis-api-servicedefaults.md` are updated. The two layers must stay in sync.
4. A test asserts the canonical pipeline order with the new registration both **before** and **after** `AddTrellisUnitOfWork<TContext>()` is called.

If it falls in the second category, say so explicitly in the PR description and add the helper to the "no slot" list above, so the next session does not re-litigate the decision.

### Validating sub-agent findings

Sub-agents (rubber-duck, code-review) are recommendation engines, not ground truth. Before adopting a finding:

- Verify the claim against the relevant API reference, source code, or existing test. Most non-trivial findings are testable in 30 seconds.
- Push back on claims that contradict verified docs/source or existing intentional design. Reference earlier PRs (e.g., via `git log -S 'token'`) when the claim implies undoing prior work.
- Adopt findings that survive verification — and adopt them confidently, because verification means you understand the bug, not just the reviewer's claim about it.

If an API reference contradicts these instructions, treat the API reference as authoritative for API usage.

## Code style

- Omit braces for single-line `if`/`return` statements when consistent with nearby code.
- Use `char` overloads for single-character operations, for example `value.Contains('-')`.
- Use collection expressions in tests where appropriate, for example `.Should().Equal([1, 2, 3])`.
- Use `ConfigureAwait(false)` in library source code; do not add it in test code.
- Prefer `ValueTask<T>` for high-frequency operations that may complete synchronously; prefer `Task<T>` for I/O-bound work.
- Avoid broad `try`/`catch` blocks and silent fallbacks. Surface or propagate errors using the existing repository patterns documented in the API references.
- Prefer self-documenting code over comments: use intention-revealing names and extract small, well-named helpers instead of explanatory comments. Add a comment only where the code genuinely cannot convey the *why* (a non-obvious workaround, invariant, or consequence) — do not narrate *what* the code does. (Per *Clean Code*, a comment compensates for a failure to express intent in code.)
- Keep public APIs documented with XML comments (the sanctioned exception to the guidance above).

## Test-driven development

Follow TDD when fixing bugs or adding features:

1. Add or update a failing test that proves the bug or specifies the new behavior.
2. Implement the smallest correct change.
3. Refactor while keeping tests green.

Do not skip the red step for bug fixes or new behavior.

## Test organization

Tests are organized by source area:

| Area | Source | Tests |
|---|---|---|
| Core ROP and DDD | `Trellis.Core/src/` | `Trellis.Core/tests/` |
| Value objects | `Trellis.Primitives/src/` | `Trellis.Primitives/tests/` |
| Authorization | `Trellis.Authorization/src/` | `Trellis.Authorization/tests/` |
| Mediator | `Trellis.Mediator/src/` | `Trellis.Mediator/tests/` |
| ASP.NET Core | `Trellis.Asp/src/` | `Trellis.Asp/tests/` |
| HTTP | `Trellis.Http/src/` | `Trellis.Http/tests/` |
| EF Core | `Trellis.EntityFrameworkCore/src/` | `Trellis.EntityFrameworkCore/tests/` |
| EF Core outbox | `Trellis.EntityFrameworkCore.Outbox/src/` | `Trellis.EntityFrameworkCore.Outbox/tests/` |
| EF Core inbox | `Trellis.EntityFrameworkCore.Inbox/src/` | `Trellis.EntityFrameworkCore.Inbox/tests/` |
| State machine | `Trellis.StateMachine/src/` | `Trellis.StateMachine/tests/` |
| Testing helpers | `Trellis.Testing*/src/` | `Trellis.Testing*/tests/` |
| FluentValidation (standalone) | `Trellis.FluentValidation/src/` | `Trellis.FluentValidation/tests/` |
| FluentValidation (Mediator) | `Trellis.Mediator.FluentValidation/src/` | `Trellis.Mediator.FluentValidation/tests/` |
| HTTP abstractions | `Trellis.Http.Abstractions/src/` | `Trellis.Http.Abstractions/tests/` |
| Azure Service Bus transport | `Trellis.Messaging.AzureServiceBus/src/` | `Trellis.Messaging.AzureServiceBus/tests/` |
| Persistence abstractions | `Trellis.Persistence.Abstractions/src/` | — |
| API versioning | `Trellis.Asp.ApiVersioning/src/` | `Trellis.Asp.ApiVersioning/tests/` |
| Service defaults / composition root | `Trellis.ServiceDefaults/src/` | `Trellis.ServiceDefaults/tests/` |
| Analyzers | `Trellis.Analyzers/src/` | `Trellis.Analyzers/tests/` |
| Core source generator | `Trellis.Core/generator/` | — (covered indirectly by `Trellis.Primitives/tests/`) |
| ASP source generators | `Trellis.Asp/generator/` | `Trellis.Asp/generator-tests/` |
| EF Core source generators | `Trellis.EntityFrameworkCore/generator/` | `Trellis.EntityFrameworkCore/generator-tests/` |

Async extension tests use this file naming convention:

| Pattern | File name |
|---|---|
| Async receiver and async delegates | `[Method]Tests.[Type].cs` |
| Async receiver and sync delegates | `[Method]Tests.[Type].Left.cs` |
| Sync receiver and async delegates | `[Method]Tests.[Type].Right.cs` |

Test method names should follow `[Method]_[Variant]_[Scenario]_[Expectation]`.

For T4-generated tuple overloads, test the 2-tuple case comprehensively and validate larger tuple arities with minimal representative tests. Do not chase 100% coverage on generated tuple code.

## Documentation standards

When adding or changing public API surface, update the relevant API reference file in `docs/docfx_project/api_reference/`. Update package `README.md`, `NUGET_README.md`, DocFX articles, and `docs/docfx_project/docfx.json` metadata when those artifacts are directly affected.

This is enforced, not advisory. **TRLDOC008** fails the build when any public type or member's name does not appear in its owning package's reference file, and **TRLDOC005** fails it when a doc names a symbol that does not exist. Adding a public member without documenting it will go red in CI. Two things about TRLDOC008 catch people out:

- Matching is **per package**. Documenting a type in a different package's file does not satisfy it, because the owning package's file is the one an agent is routed to.
- Matching is on the **simple name as a substring**. Describing a member conceptually ("the last-modified timestamp") does not count; the doc must contain `LastModified`. A name a reader cannot type is a name they cannot use — and an LLM that cannot find a member in the reference tends to invent a plausible signature rather than conclude it is absent.

When the hit is a static extension class, prefer giving it its own `###` section over name-dropping it in prose: the usual cause is that its methods were documented under a neighbouring class's heading, which is an accuracy defect in its own right.

Keep framework usage guidance in the API reference and cookbook files, not in this Copilot instruction file.

DocFX artifact checklist for package or public API changes:

| Artifact | Location |
|---|---|
| DocFX metadata | `docs/docfx_project/docfx.json` |
| DocFX articles | `docs/docfx_project/articles/` |
| Article TOC | `docs/docfx_project/articles/toc.yml` |
| Package README | `Trellis.{Package}/README.md` |
| NuGet README | `Trellis.{Package}/NUGET_README.md` |
| AI API reference | `docs/docfx_project/api_reference/trellis-api-{library}.md` |

## File encoding and PowerShell

All repository files must be UTF-8 with BOM.

When using PowerShell for file writes, preserve the BOM:

```powershell
$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($path, $content, $utf8Bom)
```

Avoid `Set-Content` for repository files because it can change encoding.

`[System.IO.File]` resolves relative paths against the *process* working directory, not PowerShell's current location, so always pass an absolute path — e.g. `$path = Join-Path (Get-Location) 'Trellis.Core\src\Foo.cs'` or `Resolve-Path`. A bare relative path silently writes somewhere unexpected.

## Validation before handoff

Before considering code work complete:

1. Run `dotnet build` from the repository root (single solution: `Trellis.slnx`).
2. Run `dotnet test` from the repository root.
3. Confirm public API changes are reflected in the API references and related package docs.

Tests run on **Microsoft.Testing.Platform** (xUnit v3), not VSTest. `--nologo` and `--filter` are **not** valid arguments and make the run exit with code 5 and "Zero tests ran" — which looks like a passing no-op. Invoke as `dotnet test <project-or-Trellis.slnx> -c <config>` and narrow output with `Select-String` instead. To iterate quickly, run the single affected test project rather than the solution.

Before committing, also complete the **Pre-commit checklist** below — it adds the code review and the diff check.

Documentation-only changes do not require a build or test run unless they affect generated docs, examples that are compiled, or documented public API behavior.

## Git and PR rules

- Do not commit without explicit user approval.
- Do not push branches unless the user explicitly asks.
- Do not create pull requests unless the user explicitly asks. Do not merge pull requests.
- Do not amend commits, rebase pushed history, or force-push unless the user explicitly asks and confirms the history is safe to rewrite.
- If asked for a PR summary, output this copy-paste-ready format:

````markdown
**Title:** <short PR title>

```markdown
<full PR body>
```
````

## Pre-commit checklist

Before committing any changes after explicit approval:

1. Confirm the **Validation before handoff** steps passed (build, test, docs in sync).
2. Confirm the diff contains only intended changes.
3. Run a code-review agent over the changed code with a high-capability reasoning model (currently `gpt-5.5`; substitute the strongest available if that ID is retired) and address substantive findings. Apply the **Validating sub-agent findings** rule above to each one.
4. Present the final summary to the user.
