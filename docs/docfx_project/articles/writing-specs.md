---
title: Writing Specifications for AI-Generated Services
package: Trellis (multiple)
topics: [ai, llm, specification, spec-authoring, status-codes, value-objects, prompting]
related_api_reference: [trellis-api-core.md, trellis-api-asp.md, trellis-api-authorization.md, trellis-api-efcore.md, trellis-api-statemachine.md]
last_verified: 2026-07-04
audience: [developer]
---
# Writing Specifications for AI-Generated Services

A good specification is the highest-leverage input to AI-assisted development. If the spec is precise and consistent with how the framework already behaves, the generated code is predictable and the review is fast. If the spec is vague or fights the framework's defaults, the AI has to guess — and every guess is a place where two runs, or two models, diverge.

This guide is the step **before** [Trellis for AI Code Generation](ai-code-generation.md). Use it when you have a rough idea or a business plan and want an AI to turn it into an implementable spec. Hand the AI your business context **and this guide**; the result is a spec that maps cleanly onto Trellis constructs.

The rules below are not style preferences. Each one comes from a real failure observed while generating services from specs: a status code that contradicted the framework default, a primitive where a value object belonged, a manual `SaveChanges` that broke rollback, an owner modeled as `Guid` that would not bind. A spec that follows these rules removes those failure modes up front.

## The workflow

```
business idea  ->  spec (this guide)  ->  generate (ai-code-generation.md)  ->  human review
```

The spec is where you make the expensive decisions once — status codes, domain vocabulary, persistence shape, authorization — so the AI does not re-decide them (differently) on every run.

## 1. What a Trellis-aligned spec contains

A complete spec has these sections. Missing sections are where an AI silently fills gaps.

1. **Glossary / ubiquitous language.** Define each domain noun and verb. These become your value objects, aggregates, and operations. Name them precisely — the AI will use these names as types.
2. **Domain model.** For each aggregate: its identity, its fields (as value objects, see below), its invariants, and its lifecycle if it has one.
3. **Operations.** For each operation: permission required, input, validation rules, behavior, success result, and **failure cases with their HTTP status** (see §3).
4. **Endpoint table.** Method, path, operation, permission, success status, error statuses — one row per endpoint.
5. **Error-behavior table.** Situation -> expected error -> HTTP status, for the whole service.
6. **State machine / lifecycle** if a workflow exists: the states, the allowed transitions, and what each transition does.
7. **Persistence.** Storage shape, indexes, unique constraints. If a database already exists, treat its schema as a binding contract (see §5).
8. **Eventing** if the service publishes or consumes integration events: the events, the outbox producer, and the idempotent consumer round-trip. Do not leave this implicit — an under-specified eventing section is the section an AI most often skips entirely.
9. **HTTP semantics.** Idempotency, optimistic concurrency (ETag), pagination — stated explicitly (see §6).
10. **Testing requirements.** What must be tested and at what layer (see §7).

## 2. Model domain concepts as value objects, not primitives

The single biggest driver of predictable generated code is naming validated concepts as types.

- Every validated input is a **value object**, not a `string`/`int`/`Guid`. Say "`EmailAddress`", not "a string that must be a valid email". See [`trellis-api-primitives.md`](../api_reference/trellis-api-primitives.md) and the [value-object taxonomy](../api_reference/trellis-value-object-taxonomy.md).
- **Identities are typed.** `OrderId`, `CustomerId` — each a `RequiredGuid<TSelf>`. Never a bare `Guid` parameter that could be transposed with another.
- **Enumerations are `RequiredEnum<TSelf>`**, not a CLR `enum` and not a `string`. This gives you validation, ordering, and a stable wire value. See [RequiredEnum](required-enum.md).
- **Owners and actors are `ActorId`, never `Guid`.** The current principal's identity is an `ActorId` (a validated string); comparing ownership means comparing `ActorId` values. Specifying an owner as `Guid` forces a lossy conversion and breaks the binding contract.
- **Money is a value object** (`MonetaryAmount` / a `Money` composite), never a raw `decimal`.

When you write the domain model, give each concept its type name. The AI then declares the value object once and reuses it everywhere, and the compiler enforces the rules.

## 3. Status codes: match the framework default

This is the most common source of spec-versus-framework friction. Trellis maps failures to HTTP status codes by default. If your spec picks a different code, the AI must either fight the default (extra, non-idiomatic code) or produce something inconsistent with the rest of the service.

Specify status codes this way:

| Situation | HTTP status |
|---|---|
| Input / value-object validation failure (blank field, bad format, malformed typed path or query parameter) | **422** |
| Business-rule / invariant violation, including an invalid state-machine transition | **422** |
| Entity not found | 404 |
| Conflict (duplicate unique value, concurrent state change) | 409 |
| Authorization failure (missing permission, not owner) | 403 |
| Authentication required (no/invalid principal) | 401 |
| Failed precondition (stale `If-Match`) / precondition required (missing `If-Match`) | 412 / 428 |
| Conditional GET matches (`If-None-Match`) | 304 |
| Missing `api-version`, syntactically malformed request body, missing or malformed `Idempotency-Key` header | **400** |

Key rules:

- **Business and input validation is 422, not 400.** Trellis maps `Error.InvalidInput` (value-object validation) and `Error.InvariantViolation` (invalid state transitions) to 422 by default. Reserve **400** for framework-level protocol errors that never reach domain logic — for example a missing API version, an unparseable JSON body, or a missing or malformed idempotency header. Do not tell the AI to return 400 for validation; it will either override the default needlessly or produce a service that disagrees with itself.
- **Use the real error catalog.** Failures are `Error.NotFound`, `Error.Conflict`, `Error.Forbidden`, `Error.AuthenticationRequired`, `Error.InvalidInput`, `Error.InvariantViolation`, and so on. There is no `Error.Unauthorized` — 401 is `AuthenticationRequired`, 403 is `Forbidden`. See [Error Handling](error-handling.md) and [`trellis-api-core.md`](../api_reference/trellis-api-core.md#public-abstract-record-error).
- **Do not mandate exception-based error handling.** Expected failures are returned as `Result` values and mapped to RFC 9457 Problem Details by the framework at the boundary. An exception-handling middleware is only a safety net for *unexpected* faults (500). A spec that says "catch exceptions and return an error response" fights railway-oriented programming.
- **Do not prescribe framework API names you have not verified.** If the spec names a mapping method, use the real one (`ToHttpResponse()` / `ToHttpResponse().AsActionResult<T>()`), not an invented one. When in doubt, describe the behavior and let the implementation pick the API from [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md).

## 4. Keep the spec internally consistent

An AI implements the most specific statement it can find. If three sections disagree, you get three behaviors across runs.

- A given failure must carry the **same status** in the per-operation list, the endpoint table, and the error-behavior table.
- Every endpoint in the table has a matching operation description, and vice versa.
- Every value object named in a contract table is defined in the domain model.
- Every state referenced by a transition exists in the state machine.

## 5. Persistence and the unit of work

- **Repositories stage; they do not save.** A repository exposes `Add`, `Remove`, and `FindById` (returning `Maybe<T>`). It does **not** expose `SaveAsync` / call `SaveChanges`. The unit of work commits **once per command, on success**; a failed command discards staged changes. Specifying a manual save per repository breaks rollback-on-failure and multi-aggregate atomicity. See [Entity Framework Core](integration-ef.md) and [`trellis-api-efcore.md`](../api_reference/trellis-api-efcore.md).
- **Only aggregate roots get repositories.** Child entities are loaded through their aggregate (an `Include`), not through their own repository.
- **If the database already exists, its schema is a binding contract.** The generated model must match the existing columns, types, and nullability exactly, because the framework maps conventionally (typed-ID columns, owned value objects flattened to `Owner_Property`, enums as their wire value). Call out the highest-risk divergences explicitly: owner columns are `ActorId` (string), not `Guid`; identities are GUID primary keys; owned-collection shapes; fixed type conventions.

## 6. State the HTTP semantics you rely on

The framework provides these, but only if the spec asks for them:

- **Idempotency.** Which unsafe writes require an `Idempotency-Key` header, and the replay contract (same key replays the original response; same key in flight -> 409 until the reservation times out; same key with a different body -> 422).
- **Optimistic concurrency.** Which writes require `If-Match` (stale -> 412, missing -> 428) and which reads support conditional GET (`If-None-Match` -> 304).
- **Pagination.** List endpoints page by **forward-only cursor (keyset)** using a stable sort key — not offset/skip. See [Pagination](pagination.md).
- **Lifecycle timestamps.** Domain timestamps (for example `SubmittedAt`, `ApprovedAt`) are aggregate fields. Note that a domain **event**'s timestamp property must be named `OccurredAt` and typed `DateTimeOffset`.

## 7. Require real test coverage

Left unspecified, an AI tends to produce a thin happy-path suite. State the expectation:

- **Domain tests** for each aggregate's invariants and each state transition, with no external dependencies.
- **Application tests** for each operation's success and failure paths, using the in-memory fakes.
- **API integration tests** for the HTTP contract (status codes, headers, problem details).
- **An eventing round-trip test** if the service has an outbox/inbox: produce -> relay -> idempotent consume -> read-model update.

Tell the AI to test failure paths and invariants, not just the happy path. "Cover every failure listed in the operation" is a concrete, checkable instruction.

## 8. Authorization

- State the **permission required** for each operation (an `entity:action` string).
- **Public / anonymous reads must not require authorization.** A query that anyone can call should not declare a required permission.
- State **ownership rules** explicitly: who may act on a resource they own versus an administrator override, compared via `ActorId`. See [ASP.NET Core Authorization](integration-asp-authorization.md) and [`trellis-api-authorization.md`](../api_reference/trellis-api-authorization.md).
- If you use an `entity:*` shorthand for "all actions", expand it into concrete permissions at role-definition time. Permission checks are exact-match by design; there is no check-time wildcard.

## Spec skeleton

Fill this in from your business context. It is the proven shape; keep the section order.

```markdown
# <Service> Specification

## 1. Glossary
<domain nouns and verbs; each becomes a type or operation>

## 2. Domain Model
### <Aggregate>
- Identity: <Name>Id (RequiredGuid)
- Fields: <field: ValueObjectType>, ...
- Invariants: <rules that must always hold>
- Lifecycle: <states + transitions, if any>

## 3. Operations
### <Operation> (Command | Query)
- Permission required: <entity:action>  (omit for public reads)
- Input: <value objects>
- Validation: <rules>
- Behavior: <what it does; which transition it fires>
- Success: <result + status>
- Failure: <case -> status>, ...   (validation -> 422; not found -> 404; conflict -> 409; ...)

## 4. Endpoints
| Method | Path | Operation | Permission | Success | Error Codes |

## 5. Error Behavior
| Situation | Expected Error | HTTP Status |
<plus the status-code convention note from §3>

## 6. State Machine  (if a workflow exists)
<states, allowed transitions, effects>

## 7. Persistence
<tables, indexes, unique constraints; binding-contract note if the DB pre-exists>

## 8. Eventing  (if integration events exist)
<events; outbox producer; idempotent consumer round-trip>

## 9. HTTP Semantics
<idempotency; ETag/concurrency; cursor pagination>

## 10. Testing Requirements
<domain / application / API / eventing coverage expectations>
```

## Final consistency checklist

Before handing the spec to a code-generation step, verify:

- [ ] Every validated concept is named as a value object; identities and enums are typed; owners are `ActorId`.
- [ ] Business/input validation is **422**; **400** is limited to framework-level protocol errors (missing api-version, malformed JSON, missing/malformed `Idempotency-Key`), never domain validation.
- [ ] Each failure's status is identical across the operation list, endpoint table, and error table.
- [ ] Repositories stage only; the unit of work commits per command; only aggregate roots have repositories.
- [ ] Idempotency, ETag/concurrency, and cursor pagination are stated where used.
- [ ] Eventing is fully specified (or explicitly absent), including its round-trip test.
- [ ] Every operation states its permission (or is explicitly public); ownership rules are explicit.
- [ ] Testing requirements name the layers and demand failure-path coverage.
- [ ] No invented framework APIs; error cases use the real `Error` catalog.

## Bottom line

The framework already makes strong default choices about validation, status codes, persistence, and error mapping. A good spec **agrees with those defaults and states the rest explicitly**. That is what turns "generate a service from this spec" into a repeatable, reviewable result instead of a lottery — and it is the front half of the same story told in [Trellis for AI Code Generation](ai-code-generation.md).
