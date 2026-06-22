# ADR-006 — Re-home Persistence and Messaging Contracts out of the EF Core Packages

> **Status:** Accepted. **Supersedes ADR-002 §2 (Proposed Package Map) and §5.1 item 7**
> (which pinned `TransactionalCommandBehavior` to `Trellis.EntityFrameworkCore`) for the persistence and
> inbox messaging contracts named below.
>
> **Context:** Trellis declares its persistence and consume-side messaging contracts (`IUnitOfWork`,
> the inbox/checkpoint store SPIs, the integration-event dispatch seam) but ships them *inside* the
> EF Core packages, in namespace `Trellis.EntityFrameworkCore`. Implementing any of them — or reusing
> the standard commit pipeline — therefore forces a dependency on EF Core. This ADR lifts each
> contract into an honest home: a single new `Trellis.Core`-only abstractions package for the store
> SPIs, while the inbox dispatch seam joins its sibling integration-event contracts already living in
> `Trellis.Mediator`. EF Core becomes one adapter among potential others (Dapper, ADO, Cosmos DB).

## Context

Two guiding principles frame this decision:

- **The core stays independent of concrete persistence/vendor technology.** Trellis ships the
  abstraction/seam (plus an in-memory reference and tests) and the one shipped EF Core adapter;
  concrete adapters for other stores are app/community-owned.
- **The API is AI-first.** An agent should generate correct code from namespaces and examples without
  needing surprising tribal knowledge about where a type lives.

Today both principles are violated by *where the contracts live*, not by their shape. The following
are all declared in namespace `Trellis.EntityFrameworkCore` and shipped from
`Trellis.EntityFrameworkCore` / `Trellis.EntityFrameworkCore.Inbox`:

| Contract | Kind |
|---|---|
| `IUnitOfWork` | persistence — commit boundary |
| `IInboxStore` | persistence — dedup-record store SPI |
| `IConsumerCheckpointStore` | persistence — resume-cursor store SPI |
| `IInboxDispatcher` | messaging — integration-event consume seam |
| `IntegrationEnvelope` | messaging — dispatch input DTO |
| `InboxDispatchOutcome` | messaging — dispatch result |

In addition, `TransactionalCommandBehavior` — the Mediator pipeline behavior that commits a command
via `IUnitOfWork` after a successful handler — lives in `Trellis.EntityFrameworkCore` even though its
implementation uses no EF type (only `IUnitOfWork` + `IPipelineBehavior`).

The net effect: an author who wants to back any of these with a non-EF store, or simply reuse
Trellis's standard commit pipeline, must reference the EF package — and therefore the entire EF Core
relational stack — even though none of the contracts (and not even the commit behavior) names an EF
type.

## Evidence

A spike implemented the three **store** SPIs (`IUnitOfWork`, `IInboxStore`,
`IConsumerCheckpointStore`) in-memory and exercised them through the published contracts under a hard
rule: zero EF types — no `DbContext`, no `SaveChanges`, no `DbUpdateException`.

1. **The store contracts are movable now.** The in-memory realization compiles and passes every
   behavioral assertion using only the BCL and Trellis's own `Result` / `Maybe` / `Unit` types:
   stage a dedup record in the unit of work → commit it atomically → the anti-join reads only
   committed state → dedup keyed per `(ConsumerId, MessageId)` → the checkpoint round-trips an opaque
   cursor → nested-scope commits defer to the outermost scope. No store signature leaks EF. (The
   spike covers the store SPIs only — not the dispatcher; see the Decision for why the dispatcher is
   treated separately.)

2. **The packaging coupling is real.** To *see* those EF-free interfaces the spike had to reference
   the EF packages, which transitively pulled in `Microsoft.EntityFrameworkCore` 10.0.9,
   `…Relational`, `…Abstractions`, and `…Analyzers`. A would-be Dapper or Cosmos adapter installs the
   full EF relational stack purely to implement a dedup record.

3. **`TransactionalCommandBehavior` is EF-free but EF-stranded.** Its implementation depends only on
   `IUnitOfWork` and `IPipelineBehavior` (Mediator abstractions); the EF references in it are
   doc-comment `cref`s. So even a portable `IUnitOfWork` contract does not make the *commit pipeline*
   portable while the behavior is locked in the EF package. (Its **registration**,
   `AddTrellisUnitOfWork<TContext>`, is genuinely EF-specific — it wires `EfUnitOfWork<TContext>` and
   an `ITrackedAggregateSource` forwarder — and must stay adapter-side; see the Decision.)

4. **`IInboxStore` does not need the messaging envelope, but the EF row persists more than the dedup
   key.** The dedup logic reads only `MessageId` from `IntegrationEnvelope`, yet the EF `InboxMessage`
   row also persists `MessageSource`, the event type, `OccurredAt`, `CausationId`, `CorrelationId`,
   and a stamped `ProcessedAt`. So the store can drop its dependency on the messaging DTO, but only if
   the replacement carries that lineage — a bare `(consumerId, messageId)` shape would silently narrow
   what the EF adapter persists.

5. **The sibling messaging contracts already live in `Trellis.Mediator`.** `IIntegrationEventPublisher`,
   `IIntegrationEventHandler<T>`, and `IIntegrationEventCollector` are all in `Trellis.Mediator`. The
   inbox dispatch seam is the *consume* side of the same integration-event story and is intrinsically
   Mediator-coupled (it resolves and invokes `IIntegrationEventHandler<T>` through DI). Its honest home
   is alongside those siblings, not a separate package.

6. **Precedent for the abstractions home is `Trellis.Http.Abstractions`.** That package separates HTTP
   contracts from their realization, and its types are declared in the flat root namespace `Trellis`
   (not `Trellis.Http.Abstractions`). The package is the *dependency* boundary; the namespace stays
   flat.

## Decision

Lift each contract into an honest home, keyed by what it *is*, and relocate the EF-free commit behavior
so the standard pipeline is reusable. EF Core becomes an adapter that references these homes and
implements them.

**Namespace.** Each contract adopts the namespace convention of its destination package, not a single
flat namespace. The store SPIs in the new abstractions package use the flat root namespace **`Trellis`**,
matching `Trellis.Core` and `Trellis.Http.Abstractions` (whose public types are in `namespace Trellis`),
so one `using Trellis;` surfaces them. The dispatch contracts moving into `Trellis.Mediator` adopt its
existing **`Trellis.Mediator`** namespace, consistent with their siblings (`IIntegrationEventPublisher`,
`IIntegrationEventHandler<T>`, `IIntegrationEventCollector`) — and a non-EF inbox consumer already imports
`Trellis.Mediator`. In every case the **package** boundary (not the namespace) is what removes the EF
dependency. (The inbox feature's contracts intentionally span both namespaces, mirroring the
persistence/messaging split; each lands in the namespace its package already uses.)

**Target homes — one new package.**

| Type(s) | Home (package) | Status |
|---|---|---|
| `IUnitOfWork`, `IInboxStore`, `IConsumerCheckpointStore`, `InboxRecord` | **`Trellis.Persistence.Abstractions`** | **new**; depends only on `Trellis.Core` |
| `IInboxDispatcher`, `IntegrationEnvelope`, `InboxDispatchOutcome` | **`Trellis.Mediator`** | existing; joins the sibling integration-event contracts |
| `TransactionalCommandBehavior<,>` + a provider-neutral insertion helper | **`Trellis.Mediator`** | existing; gains a reference to `Trellis.Persistence.Abstractions` |

The inbox dispatch contracts go to the **existing** `Trellis.Mediator` rather than a second new
package: they always travel with the Mediator pipeline (a non-EF inbox consumer needs `Trellis.Mediator`
anyway to invoke handlers), and their siblings are already there. This keeps the change to **one new
package**.

**Decouple the store from the messaging DTO with an explicit `InboxRecord`.** `IInboxStore.TryRecordAsync`
takes the persistence-native record instead of `IntegrationEnvelope`:

```
record InboxRecord(
    Guid MessageId,
    string EventType,
    DateTimeOffset OccurredAt,
    string? MessageSource = null,
    Guid? CausationId = null,
    string? CorrelationId = null);

Task<bool> TryRecordAsync(string consumerId, InboxRecord record, CancellationToken cancellationToken);
```

`consumerId` stays the separate scoping parameter (matching `FilterUnprocessedAsync`); the store stamps
`ProcessedAt` itself. `InboxRecord` lives in `Trellis.Persistence.Abstractions` and uses only primitives,
so the package depends on `Trellis.Core` alone and never on the messaging contracts. The dispatcher maps
the envelope to an `InboxRecord` before calling the store.

**Split the transactional-command registration.** `TransactionalCommandBehavior<,>` and a
provider-neutral helper that inserts the open-generic behavior in canonical pipeline order move to
`Trellis.Mediator`. The EF-specific `AddTrellisUnitOfWork<TContext>` stays in `Trellis.EntityFrameworkCore`,
keeps wiring `EfUnitOfWork<TContext>` + the `ITrackedAggregateSource` forwarder, and **calls** the neutral
helper — so `Trellis.EntityFrameworkCore` gains an intentional reference to `Trellis.Mediator`.
Non-Mediator / behavior-free callers continue to use `AddTrellisUnitOfWorkWithoutBehavior<TContext>`.

`Trellis.Mediator` currently detects and re-orders the transactional behavior by a hard-coded
`Trellis.EntityFrameworkCore.TransactionalCommandBehavior` full-name string constant — its domain-event
and tracked-aggregate dispatch registrations remove and re-append the behavior so dispatch runs outside
the commit. Because the behavior moves into `Trellis.Mediator`, that string lookup becomes a direct
`typeof(...)` reference to the relocated type, and the ordering tests plus the doc comments that locate
the behavior "in `Trellis.EntityFrameworkCore`" must be updated. This is a simplification the move
enables, but it is required for the dispatch-ordering guarantee to survive.

**Stays in the EF adapter** (`Trellis.EntityFrameworkCore` / `…Inbox`), now implementing the relocated
contracts: `EfUnitOfWork`, `RepositoryBase`, `OutboxCaptureInterceptor`, `OutboxRelay`,
`DbExceptionClassifier`, the `InboxMessage` entity + configuration, and `InboxDispatcher<TContext>` (the
EF realization of the relocated `IInboxDispatcher`).

**Contract neutralization is in scope, with acceptance criteria.** Moving the contracts requires
scrubbing EF-specific `cref`s from their XML docs — `IUnitOfWork`'s references to `DbContext` and
`TransactionalCommandBehavior`, `IInboxStore`'s reference to `InboxOptions.ConsumerId` (becomes "the
stable subscriber identifier"), and the moved behavior's references to `DbContext` / `EfUnitOfWork<T>`.
Acceptance:

- `Trellis.Persistence.Abstractions` builds with `GenerateDocumentationFile` + warnings-as-errors while
  referencing only `Trellis.Core`.
- `Trellis.Mediator` builds with `GenerateDocumentationFile` + warnings-as-errors after gaining the
  behavior, with no EF `cref`s in the moved behavior's docs.
- The EF adapter still persists the same inbox columns it persists today
  (`MessageSource`, event type, `OccurredAt`, `CausationId`, `CorrelationId`, `ProcessedAt`).
- `Trellis.Mediator`'s domain-event and tracked-aggregate dispatch registrations detect the relocated
  behavior by type (not by an EF namespace string), and their existing registration-order tests are
  updated and pass.

**Reconcile ADR-002.** On acceptance, ADR-006 supersedes ADR-002 §2 (package map) and §5.1 item 7 (which
pins `TransactionalCommandBehavior` to `Trellis.EntityFrameworkCore`) for these contracts; updating both
ADR-002 passages (and any contradicting `README.md` / `copilot-instructions.md` package lists) is part of
this ADR's implementation, not a follow-up.

**Explicitly deferred — the dispatcher rewrite.** Making the inbox *orchestration* store-agnostic
(committing via `IUnitOfWork` and detecting duplicates via a provider-neutral signal instead of
`SaveChangesAsync` / `DbUpdateException` / `context.Set<InboxMessage>()`) is **not** done here. Re-homing
the dispatcher *interface* is a packaging move and is in scope; rewriting the dispatcher *implementation*
is design work that must wait for a second store provider (see Alternatives §4 and Follow-ups).

## Migration

Trellis is pre-1.0 (alpha) and carries **no backward-compatibility constraint** — ADR-002 states the
posture, and the standing direction is to optimize for the best forward-looking, AI-first design rather
than preserve existing source/binary shapes. Every move here is a clean break: types keep their names and
the flat `Trellis` namespace, only their package changes, and consumers re-reference packages as needed.
No `[TypeForwardedTo]` shims, deprecated re-exports, or dual-namespace aliases are introduced — they would
add exactly the incidental surface an AI-first API should avoid. The `IInboxStore.TryRecordAsync` signature
change and the registration split are deliberate contract improvements, not compatibility concessions.

## Consequences

**Positive**

- A non-EF **persistence** adapter implements `IUnitOfWork` / `IInboxStore` / `IConsumerCheckpointStore`
  by referencing only `Trellis.Persistence.Abstractions` — no EF Core, and no Mediator, dependency.
- The standard commit pipeline (`TransactionalCommandBehavior`) becomes reusable by any adapter that
  registers an `IUnitOfWork`, because it now lives with the Mediator infrastructure.
- The inbox dispatch contracts sit with their sibling integration-event contracts; nothing hides under an
  EF namespace, and the store stops depending on a messaging DTO.
- Only **one** new package; the dependency graph stays acyclic
  (`Trellis.Mediator` → `Trellis.Persistence.Abstractions` → `Trellis.Core`; EF adapter → all three).

**Negative / limits**

- **The inbox orchestration is still EF-only.** A non-EF author gets portable store contracts and a
  portable commit pipeline, but the shipped `InboxDispatcher<TContext>` remains `DbContext`-bound; a non-EF
  inbox consumer must still supply their own dispatcher implementation until the deferred rewrite.
- **`Trellis.EntityFrameworkCore` gains a reference to `Trellis.Mediator`** (to call the neutral insertion
  helper). This is intentional — the package already integrates with the Mediator pipeline — and behavior-
  free / non-Mediator callers retain `AddTrellisUnitOfWorkWithoutBehavior<TContext>`.
- One new package is net surface area, justified because it *removes* a forced dependency rather than adds
  capability.

**Neutral**

- No runtime behaviour change for existing EF consumers beyond the `IInboxStore` signature; the EF adapter
  is otherwise unchanged apart from references and namespaces.

## Alternatives considered

1. **Leave the contracts in the EF packages (status quo).** Rejected: permanently couples every adapter —
   and the EF-free commit pipeline — to EF Core for no benefit; the spike shows the contracts use no EF type.

2. **Put the dispatch contracts under a persistence home (or a new `Trellis.Messaging.Abstractions`).**
   Rejected: `IInboxDispatcher` / `IntegrationEnvelope` / `InboxDispatchOutcome` are messaging concepts whose
   siblings (`IIntegrationEventPublisher`/`Handler`/`Collector`) already live in `Trellis.Mediator`, and the
   dispatcher is intrinsically Mediator-coupled. A persistence home repeats the mis-homing; a separate
   messaging-abstractions package would be three types that always travel with `Trellis.Mediator` anyway —
   pure surface. Co-locating in `Trellis.Mediator` is the honest, lower-surface home.

3. **Merge everything into one package — either `Trellis.Core` or a single `Trellis.Abstractions`.** Rejected:
   `Trellis.Core` is the pure domain library, and a single combined abstractions package would either force
   `Trellis.Mediator` onto persistence-only adapters or pull persistence contracts into Mediator-land. Keeping
   the store SPIs in a `Trellis.Core`-only package, and the dispatch contracts with their Mediator siblings,
   lets a pure persistence adapter avoid Mediator entirely. The flat `Trellis` namespace preserves the
   one-`using` ergonomics regardless of package.

4. **Rewrite the dispatcher to be store-agnostic now.** Rejected for correctness, not effort: a provider-
   neutral duplicate-key/commit seam cannot be designed correctly with only one provider in hand. The EF
   dispatcher's race handling depends on EF-specific semantics — EF attributes a *batched* `SaveChanges`
   failure to every entry in the batch, forcing a ground-truth re-query to distinguish a concurrent winner
   from a handler's own unique-violation. A "neutral" seam shaped against only EF would encode those quirks
   and almost certainly misfit Dapper (single-row `IDbTransaction`) or Cosmos (`TransactionalBatch` + change
   feed). Design it when a second store exists to validate it — which is also when it has a consumer.

5. **Use a `Trellis.Persistence` namespace (not flat `Trellis`).** Rejected: `Trellis.Http.Abstractions`
   sets the precedent that abstraction packages contribute to the flat `Trellis` namespace; a flat namespace
   is the most AI-first (one `using`), and the package — not the namespace — is the dependency boundary that
   solves the actual problem.

6. **Keep `IInboxStore` taking `IntegrationEnvelope` (move the envelope to a shared home).** Rejected: it
   couples persistence to a messaging DTO. An explicit persistence-native `InboxRecord` keeps
   `Trellis.Persistence.Abstractions` dependent on `Trellis.Core` alone while preserving the lineage the EF
   row persists.

## Follow-ups (not in scope here)

- **Store-agnostic dispatcher rewrite**, gated on a second store provider: commit via `IUnitOfWork` and
  detect duplicates via a provider-neutral signal, so the inbox orchestration (not just its interface) is
  reusable.
- **Outbox store/relay seam** (`IOutboxStore`) and an **`IRepository`** abstraction, paired with the
  aggregate ETag-stamp and reconstitution seams a non-EF adapter needs.
- **`InboxOptions` placement** — registration config currently in the EF inbox package; decide whether it
  moves alongside the dispatch contracts.
