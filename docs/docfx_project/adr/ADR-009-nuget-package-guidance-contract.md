# ADR-009 — Propose a Vendor-Neutral NuGet Package Guidance Contract

> **Status:** Accepted for experimental implementation with
> [ADR-008](ADR-008-agent-context-delivery.md); proposed for external standardization.
> NuGet approval and external adoption are not Trellis implementation or release prerequisites.
>
> **Scope:** An experimental, vendor-neutral contract for discovering versioned usage guidance from
> NuGet packages, with a reusable read-only reader. No NuGet or agent vendor has accepted this proposal.

## Context

ADR-008 addresses a concrete Trellis delivery problem: packages carry exact API guidance, but coding
agents need a discoverable entry point and consumers need an explicit, safe installation lifecycle.
Other package authors face the same discovery problem without sharing Trellis's package topology,
documentation filenames, release cadence, or repository conventions.

Making ADR-008 solve the entire ecosystem problem before Trellis can ship would couple a bounded
product feature to external consensus and adoption. Conversely, presenting all of ADR-008 as a generic
standard would require unrelated publishers to adopt Trellis-specific policies.

The work should proceed on two tracks. Trellis implements this experimental contract now and supplies
the first working publisher and consumer example through ADR-008. External standardization uses that
running implementation as evidence and can proceed independently. "Experimental" means the contract
may evolve through review, not that Trellis builds a different contract to be generalized later.

## Decision for the experimental implementation

Separate generic guidance discovery from publisher policy and repository installation:

| Layer | Responsibility |
|---|---|
| Package contract | Declares guidance documents, explicit entry points, schema version, and integrity information |
| Generic reader | Resolves contributions from a selected NuGet graph and returns validated, package-attributed guidance without repository writes |
| Consumer integration | Chooses how to expose guidance to a human, IDE, or agent; may optionally materialize documents or install an instruction pointer |
| Publisher policy | Supplies usage guidance and any publisher-specific routing, compatibility, or release rules |

The core proposition is:

> A package can publish version-aligned usage guidance that tools can discover without executing
> package targets. Discovery makes documentation available; it does not authorize execution of its
> instructions or let dependency documentation override repository policy.

This is opt-in documentation infrastructure, not a requirement that every NuGet package carry agent
instructions. It can benefit humans, migration tools, IDEs, and coding agents alike.

## Resolved version 1 decisions

These are local engineering decisions, not questions awaiting NuGet or agent-vendor approval.

| Question | Experimental version 1 decision |
|---|---|
| Manifest location and version | `guidance/reference-manifest.json`, with `schemaVersion: 1` and a published JSON Schema; explicitly experimental |
| Discovery mechanism | A conventional package file, with no new NuGet metadata or client support required |
| Minimum content | Package-relative document paths and exact-byte SHA-256 hashes, plus explicit entry points when documents exist; document roles are optional |
| Framework-specific guidance | No TFM/RID-conditioned entry-point selection in version 1; guidance covers the package as a whole |
| Mixed-graph errors | No manifest means no contribution; unsupported or invalid manifests and missing required assets produce explicit diagnostics and prevent strict installation/check success |
| Reader interface | An internal read-only component used by the Trellis CLI, with a documented discovery-result model; no stable public .NET API yet |
| Ownership and artifact location | Trellis maintains version 1 in this repository; specification and schema live beside the reader under its project `docs/`, and conformance fixtures under its test project's `Fixtures/` |

The discovery result still records the project/framework/runtime in which a package was resolved.
That provenance is distinct from selecting different guidance for each framework: a package may
describe framework differences in its prose, but version 1 readers do not interpret routing conditions.

Phase 1 implements and publishes these decisions as a precise schema, specification, and fixtures.
It does not wait for external answers or reopen the chosen location, version, or scope. The reader's
project name and corresponding physical project directory are implementation naming details, not a
reason to delay this work.

## Relationship to ADR-008

ADR-008 governs Trellis publishing, repository installation, routing, and release policy. This ADR
owns the shared experimental package contract and read-only reader that ADR-008 uses from its first
release. There must not be a parallel Trellis-only discovery implementation that bypasses the contract.

Implement the version 1 contract decided here and publish its specification,
schema, fixtures, and working reader together. Trellis packages emit it and the Trellis CLI consumes
it through that reader. These are engineering deliverables, not dependencies on external approval.
No unrelated publisher, NuGet change, second reader, or agent-vendor integration is needed to ship.

Keep Trellis's cookbook routing, complete-set ownership, release cohort, and CLI version checks in its
publisher/installer policy. The reader must handle an unrelated package without these assumptions.
This does not mandate separate assemblies, a public .NET API, or a plug-in framework: an internal
reusable component with a documented read-only discovery output is enough for the first release.

Call the result a reference implementation of the named experimental contract version, not an
official NuGet standard or proof of independent interoperability. Later format changes require
explicit schema-version and compatibility decisions for published packages.

## Version 1 contract requirements

These requirements govern the first experimental implementation. Phase 1 encodes them in the
standalone specification and JSON Schema before packing the first compatible release. The chosen
location is an experimental convention, not a NuGet-reserved name.

### Package declaration

- Use the fixed package-relative location `guidance/reference-manifest.json`; do not require new NuGet
  metadata or claim official NuGet support.
- Require `schemaVersion: 1`, a `documents` array whose entries include `path` and `sha256`, and an
  `entryPoints` array of declared document paths. At least one entry point is required when documents
  exist; both arrays may be empty for a metadata-only publisher contribution.
- Permit explicitly namespaced publisher metadata without making it generic reader policy. Trellis
  uses this for its release cohort; consumers that do not apply that profile still read the same
  document contract. Packages declaring no documents have no required entry point.
- Optionally classify documents by role, such as overview, API reference, recipes, or migration
  guidance. A single small usage guide must be sufficient; no cookbook or prescribed prose format
  is required.
- Do not define framework/runtime routing selectors in version 1. Any later conditional-routing
  contract needs explicit versioning so an older reader cannot silently select the wrong guidance.
- Derive package ID and resolved version from the NuGet graph rather than trusting publisher-asserted
  identity. Retain source attribution even if a consumer deduplicates identical document content.
- Treat logical document identity as package ID, resolved version, and package-relative document path.
  Different publishers can both ship `README.md` or `api.md` without a collision.
- Support safe nested document paths and preserve relative links within each contribution. Do not
  flatten unrelated packages into one global filename namespace.
- Within a package contribution, reject distinct path spellings that alias after separator
  normalization, Unicode NFC normalization, and ordinal case-insensitive comparison, including
  directory-prefix aliases. Preserve accepted spelling rather than rewriting links. Apply this
  portable rule on every host: `Foo.md` and `foo.md` cannot be distinct documents in one contribution.
  Identical filenames in different package/version namespaces remain valid.

### Read-only discovery

- Consume selected, already-restored NuGet assets and package payloads. Do not execute package targets
  to discover guidance or implicitly restore packages.
- Return a stable discovery model containing project/framework/runtime scope, package identity and
  version, entry points, document locations, and provenance. Machine-local paths may be returned to
  a local caller but must not be mistaken for portable committed identifiers.
- Work without a Git checkout, `.trellis/`, or an `AGENTS.md` file. Git boundaries and instruction
  editing belong to a repository installer, not the discovery protocol.
- Report distinct outcomes for no manifest, unsupported schema, invalid manifest, and missing package
  assets. An existing package with no manifest contributes nothing and is not an error. An unsupported
  `schemaVersion`, malformed or invalid manifest, integrity failure, or missing required package asset
  produces an explicit diagnostic identifying the package and problem. Never classify missing package
  assets as absence of an optional manifest.
- The reader may collect diagnostics across the selected graph, but must mark discovery unsuccessful
  when any contribution cannot be validated. Strict installation and `check` fail on that outcome;
  installation performs no managed-file mutation. Valid contributions from the same graph must not be
  presented as a complete successful result after silently skipping a failed contribution.
- Allow one pinned reader to consume independently versioned packages through supported contract
  schemas. Reader versions do not have to equal package versions.
- Do not recognize special package IDs or filenames to infer routing. Trellis's Core/cookbook routing,
  release cohort, legacy-target checks, and tool-version policy are not generic reader requirements.

### Integrity and trust

Retain ADR-008's distinction between exact package-byte verification and canonical text verification
for optional materialized output. The generic specification must define normalization precisely if
it standardizes output hashing; a read-only reader need not materialize anything.

Manifest-listed sources must stay inside the resolved package. Reject traversal, unsupported links,
invalid paths, missing documents, and integrity failures. Discovery must not copy arbitrary
project-authored MSBuild items or interpret documentation as permission to run commands.

Consumer tools retain control over activation and loading. No restore/build target may silently edit
repository instructions as part of this contract. Optional installers define their own consent,
ownership, overwrite/prune protections, and interrupted-update behavior; these are not requirements
to use the read-only reader.

## Workstream and adoption

1. Publish the version 1 specification, JSON Schema, and minimal package examples implementing the
   decisions above as part of ADR-008's Phase 1, without waiting for external review.
2. Implement the shared read-only reader and have Trellis packages publish and the Trellis CLI consume
   that contract. Exercise the same code with minimal unrelated-publisher fixtures, retaining observed
   failure cases. Fixtures prove generic behavior, not independent ecosystem adoption.
3. Ship the experimental contract and Trellis integration once their local acceptance criteria pass.
   Do not wait for the following adoption milestones.
4. Seek package-author and consumer-tool feedback. Measure wrong-version API usage, compilation
   success, invented API calls, context cost, and offline usability against a no-guidance baseline.
5. Discuss the proposal with NuGet maintainers, then submit a focused design proposal using their
   current public process. Acceptance, scheduling, implementation, and adoption are separate outcomes.
6. Add packaging helpers and integrations when the contract is stable enough to justify them. A
   second independent reader should exercise the shared fixtures before claiming interoperability.

The first three steps establish the working sample; the remaining steps support external adoption
and do not gate Trellis development or release.
No issue, PR, package publication, or external outreach is authorized merely by recording this ADR.

## Local acceptance criteria

- A package with no Trellis dependency and only one short guide can participate.
- Unrelated publishers can use identical filenames without losing documents or provenance.
- Nested documents and their relative links remain usable.
- Case-only document and directory-prefix aliases within a contribution fail identically on Windows
  and Linux, even when file contents match; unrelated package namespaces remain independent.
- The reader handles independently versioned packages and multiple selected project scopes without
  a reader/package release-number equality requirement.
- Discovery works offline with restored packages, without Git and without repository writes.
- Absent, unsupported, malformed, and invalid manifests produce the specified distinct outcomes.
- A mixed graph with valid guidance plus an unsupported or invalid contribution fails strict
  installation/check without managed-file writes. A package with no manifest does not cause failure;
  a missing package cache entry does.
- Non-empty document sets require valid explicit entry points. Empty metadata-only contributions
  remain valid, roles are optional, and no framework-specific routing is required to participate.
- Path and integrity failure fixtures are shared between implementations.
- Trellis's packed guidance and its CLI use this reader and contract end to end, rather than a
  Trellis-only parallel path.

These criteria are part of the experimental implementation. The unrelated-package cases may use
local fixture packages; they do not require another publisher's participation.

## Evidence required before claiming independent interoperability

An independently implemented reader must consume the shared fixtures with compatible results.
External publishers and consumer integrations must be described as adopters only when they actually
participate. Neither condition is a prerequisite for shipping the reference implementation.

## Alternatives considered

### Wait for ecosystem standardization before allowing Trellis to ship

Rejected. External consensus and publisher adoption cannot be a prerequisite for solving Trellis's
current delivery problem. Trellis can ship the experimental contract and supply evidence for the
proposal without waiting for external acceptance.

### Build a Trellis-only contract first and extract a generic one later

Rejected. Trellis should demonstrate the proposed mechanism working, not a similar proprietary
mechanism. A minimal generic contract and reader are built with Trellis; broad integrations and
ecosystem adoption remain separate follow-up work.

### Publish ADR-008 unchanged as the generic standard

Rejected. Flat filenames, Core-specific routing, cohort validation, tool/Core version equality, and
the repository installer are not universal NuGet requirements.

### Rely only on repository instructions or web documentation conventions

Insufficient for this problem. Repository instructions provide activation and web indexes can improve
documentation access, but neither alone defines guidance discovery for a project's exact resolved
NuGet package graph. They are complementary integration surfaces, not replacements for the contract.

## Consequences

Trellis supplies the first working example of the experimental contract. There is modest up-front work
to specify the manifest and keep discovery generic, but no waiting for a standards process and no
later extraction from a separate product format. External feedback may still require versioned
changes; an experimental reference implementation is not a promise of permanent wire compatibility.

## External follow-up

No local design question in the former open-question list remains a prerequisite decision for
implementation. Trellis owns the experimental version 1 decisions and maintenance.

- NuGet maintainers decide whether to accept an upstream proposal and whether native metadata,
  tooling, or a different standardized discovery location is appropriate.
- External package authors and consumer-tool maintainers decide whether to adopt or independently
  implement the contract.
- If adoption warrants shared governance or a separate specification repository, Trellis and the
  participating maintainers can agree on that transfer explicitly.

These are follow-up discussions, not pending approvals for building or shipping version 1. External
feedback may motivate a subsequent contract version; it does not retroactively change published
version 1 semantics.

## References

- [ADR-008: Trellis agent-context delivery](ADR-008-agent-context-delivery.md)
- [NuGet proposal process](https://github.com/NuGet/Home/blob/dev/meta/README.md)
- [NuGet proposal template](https://github.com/NuGet/Home/blob/dev/meta/template.md)
- [NuGet/Home#14203: package API and breaking-change documentation locations](https://github.com/NuGet/Home/issues/14203)
- [NuGet/Home#15041: offline package API exploration](https://github.com/NuGet/Home/issues/15041)
- [llms.txt proposal](https://llmstxt.org/)
