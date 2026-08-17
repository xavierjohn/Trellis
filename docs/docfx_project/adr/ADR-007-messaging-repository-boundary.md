# ADR-007 — Keep Outbox, Inbox, and Broker Transports in the Framework Repository

> **Status:** Accepted. **Extends [ADR-006](ADR-006-store-neutral-persistence-contracts.md)** from
> package boundaries to *repository* boundaries; supersedes nothing.
>
> **Context:** Trellis now ships a transactional outbox, a deduplicating inbox, and an Azure Service Bus
> transport. A sibling repository, `xavierjohn/Trellis.Microservices`, ships gateway and cross-service
> identity packages. Because outbox/inbox/transport are most visibly *used* between services, it is
> natural to ask whether they belong in the microservices repository instead. This ADR records why they
> stay, and states the condition under which transports — and only transports — would move.

## Context

The question is not academic. `Trellis.Microservices` exists precisely to hold "the distributed-systems
part" of Trellis, and reliable messaging looks like the distributed-systems part. Three packages are in
scope:

| Package | Role |
|---|---|
| `Trellis.EntityFrameworkCore.Outbox` | capture domain/integration events in the same transaction as the aggregate; relay them |
| `Trellis.EntityFrameworkCore.Inbox` | deduplicate consumed messages per `(ConsumerId, MessageId)` |
| `Trellis.Messaging.AzureServiceBus` | carry an outbox row to an inbox over a broker |

ADR-006 already answered the *package*-level version of this question by asking one thing of each type:
**what is it, actually?** — then giving it the home that answer implies. This ADR applies the same test at
the repository level.

## Evidence

1. **The layering is already correct, and the seam is an abstraction boundary.** The messaging *contracts*
   — `IIntegrationEventPublisher`, `OutboundIntegrationMessage`, `IInboxDispatcher`,
   `IntegrationEventNameMap` — live in `Trellis.Mediator` (ADR-006 put the consume-side ones there). The EF
   realizations live in the EF packages. `Trellis.Messaging.AzureServiceBus` project-references only
   `Trellis.Core` and `Trellis.Mediator`. Ports and adapters already hold; there is no tangle to relieve.

2. **The outbox is a dual-write pattern, not a microservices pattern.** It solves "mutate the database and
   cause an external side effect atomically." That problem exists in a modular monolith that updates a
   search index, sends email, or fires a webhook — no second service required. The outbox is the reliable
   delivery arm of the domain-event mechanism `Trellis.Core` already ships. The inbox is its dual:
   idempotent consumption, which is likewise about persistence, not topology.

3. **Outbox and inbox are welded to the EF commit pipeline, not layered on top of it.**
   `OutboxCaptureInterceptor` is an EF Core `SaveChangesInterceptor`; both packages project-reference
   `Trellis.EntityFrameworkCore`. They are adapters to a specific store, in the same sense as
   `EfUnitOfWork`. ADR-006 deliberately left these implementations adapter-side for exactly this reason.

4. **A cross-repository move would put a version pin through the middle of one feature.**
   `Trellis.Microservices` consumes Trellis as NuGet packages behind a single `TrellisVersion` pin
   (`3.0.0-alpha.432` at time of writing). Moving the outbox downstream makes every change to the
   unit-of-work / SaveChanges seam a three-step release dance: framework release → pin-bump PR → downstream
   release. More seriously, **the framework's own test suite would stop covering whether SaveChanges
   interception captures outbox rows correctly** — the highest-value test for that seam would sit in a
   different repository, behind a pin, and could only fail *after* a release.

5. **The two repositories are split by coupling, not by subject matter.** `Trellis.Microservices` holds what
   is determined by *deployment topology*: a YARP gateway that mints internal JWTs, a consumer-side actor
   provider, JWKS/OIDC discovery. Its three packages have zero persistence dependencies. Outbox and inbox
   are determined by *how you persist*. Filing them by the "distributed systems" theme would sort on a
   different axis than the one the split actually uses, and would introduce EF Core to a repository that
   deliberately has no database.

6. **The vendor-SDK concern is real but points elsewhere.** The framework repository has 23 packable projects
   and now two vendor SDK dependencies — `Azure.Messaging.ServiceBus` and `Microsoft.Azure.Cosmos`. Should
   Kafka, RabbitMQ, and SQS transports follow, every framework contributor's build would restore broker SDKs
   they do not use. This is the one genuine cost, and it applies **only** to transports — not to outbox/inbox,
   which carry no vendor dependency.

## Decision

**Outbox and inbox stay in the framework repository, permanently.** They are persistence adapters over
`Trellis.EntityFrameworkCore`; their home follows their coupling, and their tests must live beside the
commit pipeline they intercept. Revisiting this is out of scope absent a change to the EF integration itself.

**Broker transports stay in the framework repository for now**, under a stated exit condition below.

**`Trellis.Microservices` is not the destination for either**, regardless of how the transport question
resolves. That repository's organizing principle is edge and inter-service *identity*; adding brokers and
EF Core would dissolve that principle rather than reinforce it, leaving two repositories that both mean
"distributed-ish."

**Exit condition for transports.** Split broker transports into a dedicated `Trellis.Messaging` repository
when **both** hold:

- a **second** shipped transport exists (Kafka, RabbitMQ, SQS, …), so the split amortizes over more than one
  package and the shared shape has been validated twice rather than guessed once; **and**
- the transport contracts in `Trellis.Mediator` have been stable across at least one such addition — i.e.
  adding transport #2 required no change to `IIntegrationEventPublisher` / `OutboundIntegrationMessage`.

Until both hold, splitting buys isolation that is not yet needed and immediately imposes the version-pin
lockstep described in Evidence §4. The second condition matters more than the first: a repository boundary
across an unstable contract is the expensive kind of mistake, because each iteration costs a release cycle.

This exit condition governs a split along the **capability** axis, which is the one this ADR expects. A
*different* trigger with a different destination — vendor-driven release cadence or separate ownership,
which would argue for a vendor repository instead — is described in Alternatives §5. Both are stated so a
future reader knows the transport question has two distinct escape hatches and which evidence distinguishes
them; neither is in force today.

## Consequences

**Positive**

- One repository, one build, one test run covers outbox capture → relay → transport → inbox dedup. The
  seam most likely to harbour a bug is the one between these parts, and it stays inside a single CI run.
- Contract changes such as PR #696 (making the wire identity mandatory on the publish seam) remain a single
  atomic commit across contract, producer, and transport. Across repositories that change would have been a
  breaking release plus a coordinated downstream bump.
- `Trellis.Microservices` keeps a sharp, stateless identity: gateway, JWT contract, actor propagation.

**Negative / limits**

- The framework repository carries vendor SDK dependencies (`Azure.Messaging.ServiceBus`,
  `Microsoft.Azure.Cosmos`) that most contributors never exercise, costing restore and build time.
- That cost grows linearly with each new transport, which is precisely what the exit condition is for. This
  ADR accepts the cost at n=1 and commits to re-evaluating at n=2 rather than pretending it is zero.

**Neutral**

- No code moves. This ADR records a decision *not* to act, so that the question is answered once rather than
  re-derived. Per the ADR README, the obvious-looking alternative was examined and rejected for reasons that
  are not obvious from the directory layout.

## Alternatives considered

1. **Move outbox, inbox, and transports to `Trellis.Microservices`.** Rejected: sorts by theme rather than by
   coupling (Evidence §5), forces EF Core into a repository with no persistence story, and puts a NuGet
   version pin through the middle of the EF commit pipeline — relocating the outbox's most valuable tests
   outside the repository that owns the code they protect (Evidence §4).

2. **Move only the Service Bus transport to `Trellis.Microservices`.** Rejected: it is the most *movable*
   package (Core + Mediator only, Evidence §1), so the mechanics would work — but the destination is wrong.
   A gateway/identity repository that also hosts broker adapters has no describable boundary, and the next
   transport would have nowhere obvious to go.

3. **Create a `Trellis.Messaging` repository now.** Rejected as premature, not as wrong. With one transport
   it isolates nothing while immediately imposing release lockstep on contracts that are one PR old — PR #696
   changed `IIntegrationEventPublisher` after the outbox shipped, which is the concrete evidence that this
   surface is still moving. Promoted to the stated exit condition instead of discarded.

4. **Move outbox/inbox but keep the transport.** Rejected as the inverse of the coupling: outbox/inbox are
   the EF-coupled pieces that most need to stay next to `Trellis.EntityFrameworkCore`, while the transport is
   the loosely-coupled piece. This alternative moves exactly the wrong half.

5. **Create a vendor repository (`Trellis.Azure`) holding `Trellis.Messaging.AzureServiceBus` and
   `Trellis.Asp.Idempotency.Cosmos`.** Rejected, though it is a better axis than alternative 1 and is
   genuinely *feasible* — `Trellis.Testing.Idempotency` is a shipped conformance suite depending only on
   `Trellis.Asp`, so an out-of-repo store can prove its own correctness. Three reasons it still loses:

   - **The test-infrastructure argument — its strongest — is empirically false.** Container-dependent
     integration tests are not confined to the Azure packages. `Trellis.EntityFrameworkCore` (2),
     `…Inbox` (2), and `…Outbox` (1) carry five SQL Server integration test files. Extracting the Azure
     packages would *not* leave the framework repository free of vendor-backed tests, because the vendor
     code that most needs a container is the code that cannot leave. (CI already excludes
     `Category=Integration`, so this burden is local-developer-only either way.)
   - **Vendor isolation is a dependency concern, and the package boundary already provides it.** Nobody
     installing `Trellis.Core` restores the Azure SDK today. ADR-006 established that the *package*, not the
     namespace, is the dependency boundary; the same logic continues upward — the *repository* is the
     coordination boundary. A vendor repository buys isolation already in hand and charges coordination
     that is not.
   - **"Vendor" describes the dependency, not the code.** Such a repository would pair an integration-event
     transport (implementing `IIntegrationEventPublisher`, from `Trellis.Mediator`) with an idempotency
     store (implementing `IIdempotencyStore`, from `Trellis.Asp`): different contracts, different layers,
     zero shared code. A contributor working on idempotency would need two repositories; one working on
     "Azure" would face two unrelated features. Capability (`Trellis.Messaging.*` — shared contract, shared
     conformance suite) is the grouping that serves contributors, which is why the exit condition above is
     keyed to a second *transport* rather than a second *Azure service*. .NET Aspire is the precedent at
     scale: `Aspire.Azure.*`, `Aspire.Hosting.AWS`, and the rest ship as separate **packages** from a single
     repository.

   This becomes the right answer if the vendor packages ever need a release cadence driven by the vendor SDK
   rather than by Trellis contracts, or if they gain separate owners. Neither holds while the contracts they
   implement are alpha and still moving.

## Reversibility

This decision is deliberately cheap to reverse, and the mechanism is recorded here because it is the part
that decays from memory. Publishing to nuget.org does **not** close the door on a later repository move.

**What a published package actually pins is the `(package ID, version)` tuple.** nuget.org unlists but never
deletes, so any future repository must publish a strictly higher version under the same ID. The repository
itself is invisible to consumers — **moving a package between repositories while keeping its ID is not a
breaking change**; consumers receive a new version of the same ID. Only `RepositoryUrl` / SourceLink metadata
changes, which affects symbol debugging, not resolution.

**Version continuity is one config field.** The framework versions every package from a single
Nerdbank.GitVersioning stream (`3.0-alpha.{height}` in `version.json`), so a new repository would restart its
commit height at 1 and produce *lower* versions than those already published. `versionHeightOffset` exists
precisely for repository migrations: set it to the height last published and the sequence continues.
`versionHeightOffsetAppliesTo` pins the offset to one base version so it does not inflate a later version line.

**The genuinely irreversible choice is the package ID**, since renaming a published ID orphans consumers.
The current IDs are deliberately **repository-neutral** — `Trellis.Messaging.AzureServiceBus` and
`Trellis.Asp.Idempotency.Cosmos` name a capability and a vendor, never a repository — so either the
`Trellis.Messaging` split in the exit condition above or the rejected vendor repository could be adopted later
with **no rename**. Both IDs also follow .NET convention (`Azure.Messaging.ServiceBus`,
`Aspire.Azure.Messaging.ServiceBus`; `Microsoft.EntityFrameworkCore.Cosmos`, `Microsoft.Azure.Cosmos`).

**The one cost publishing order does not change** is the uniform version pin. Every Trellis package currently
shares a version, letting consumers pin a single `$(TrellisVersion)` across all of them — as
`Trellis.Microservices` does today. Any split gives the extracted packages an independent number and forces a
second pin. That is a cost of splitting whenever it happens, not a cost of publishing first.

The practical consequence: **the decision to publish these packages from the framework repository does not
need to wait on the repository question**, and the repository question should not be settled under a deadline
it does not actually have.

## Follow-ups (not in scope here)

- **Re-evaluate the transport split when a second transport is proposed**, against both stated conditions.
- **End-to-end coverage of outbox → broker → real EF inbox.** The Service Bus integration tests use a
  recording `IInboxDispatcher` fake to keep EF out of the transport test project, so the join between the two
  halves is currently unexercised. This gap is an argument *for* co-location: closing it requires a test that
  references the EF inbox and the transport together, which is straightforward in one repository and awkward
  across two.
