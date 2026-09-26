# ADR-008 — Deliver Versioned Trellis Agent Context Without Build-Time Repository Mutation

> **Status:** Accepted. The five open questions and the transaction-scope proportionality question
> raised in review are resolved in place below; see "Decisions resolved in review" for the summary.
>
> **Context:** Trellis NuGet packages ship an LLM-optimized API reference set and currently copy it
> into a consuming repository's nearest `.github/` directory during build. After adopting root
> `AGENTS.md` as the cross-agent instruction surface, the files are still delivered but no portable
> mechanism tells an agent that they exist. Automatically editing a customer's `AGENTS.md` from a
> `buildTransitive` target would solve discovery by violating repository ownership, build
> reproducibility, and the trust boundary around agent instructions. This ADR proposes an explicit,
> manifest-backed agent-context lifecycle instead.

## Context

Trellis maintains API reference files specifically for coding agents. They contain exact signatures,
cross-package recipes, analyzer fix shapes, package preflight requirements, and a small
`trellis-start-here.md` router. The reference set exists because an agent without exact signatures
tends to invent plausible Trellis APIs.

The current delivery mechanism has several deliberate properties:

1. `Trellis.Core` packs the complete lockstep-versioned first-party reference set under `trellis/`.
2. Independently versioned satellite packages ship their own references.
3. `build/Trellis.ApiReference.targets`, imported through `buildTransitive`, copies every contributed
   `TrellisApiReference` item into the nearest `.github/` directory before build.
4. The directory search is bounded by the Git repository root, supports an explicit root override,
   and can be disabled.
5. Packaging gates fail when a shipping package has no path to a reference payload.
6. The complete first-party set is intentional: it prevents stale references after a first-party
   package is removed and lets an agent discover optional Trellis modules that are not installed yet.

Those are valuable invariants and should not be discarded. The problem is the final discovery hop.
Arbitrary Markdown under `.github/` is not a cross-agent instruction mechanism. Once a repository uses
`AGENTS.md`, nothing automatically routes an agent from that file to
`.github/trellis-start-here.md`.

There is no package-manager-to-agent discovery standard. An agent reliably learns about dependency
documentation only when the repository's effective instructions point to it, or when a human tells
the agent in a prompt.

## Decisions resolved in review

Six items were flagged during design review as needing an explicit call rather than further drafting.
Resolutions:

1. **Command distribution** — a local, repo-pinned .NET tool via a checked-in tool manifest
   (`.config/dotnet-tools.json`), not a global tool and not an MSBuild task. The tool version bumps in
   the same PR as the `Trellis.Core` package version bump for the scope it governs, is reviewable in the
   diff, and `check` fails when the tool version resolved for a scope disagrees with that scope's
   resolved `Trellis.Core` version. Independent scopes (see "Scope and monorepos") carry independent,
   nested tool manifests rather than one repository-wide manifest forcing one tool version on every
   scope. See "Explicit lifecycle command" below.
2. **Insertion point** — the managed block is inserted after optional frontmatter and the file's first
   top-level heading, as originally drafted. Unchanged.
3. **CI enforcement** — first-party templates enable `trellis agent check` in CI by default, rather than
   documenting it as opt-in.
4. **Legacy `.github/` cleanup** — out of scope. `init`, `sync`, and `remove` never read, write, or
   delete anything under `.github/`, and there is no `migrate` command. Consumers delete legacy files
   manually once `.trellis/` looks correct. See "Migration from `.github/`" below.
5. **Tool bootstrap** — a low-`Importance` MSBuild `<Message/>` (never a warning) on restore/build,
   shown when no `.trellis/` is detected. It distinguishes a first install (no tool manifest yet: create
   one and install the pinned tool) from a subsequent clone (manifest already committed: just restore
   it) — see "Explicit lifecycle command" for the exact commands, since `dotnet tool restore` alone only
   restores a tool already registered in an existing manifest and does not, by itself, bootstrap a fresh
   repository. This borrows the same technique `_CopyTrellisApiReference` already uses for its own
   "`.git` not found" notice — an `Importance="Low"` `<Message/>`, invisible by default and never
   participating in `TreatWarningsAsErrors` — but ships as its own independent target, not an addition to
   `_CopyTrellisApiReference` itself, which Phase 3 deletes entirely. The bootstrap message must outlive
   that removal.
6. **Transaction scope for Phase 1/2** — ship the exclusive writer lock, atomic per-file replacement,
   and full conflict preflight now; defer the durable recovery journal and the persistent
   generation-record read-consistency protocol described in earlier drafts. An operation interrupted
   mid-write may require reviewed repair/adoption or selective restoration of affected paths before
   rerunning `init`/`sync` with current package assets — a bare re-run is not guaranteed to complete on
   its own, and there is no automatic multi-step recovery. Revisit once
   concurrent multi-scope usage is an actual, not hypothetical, scenario. See "Shared writers and
   interrupted updates" and the acceptance criteria below for what changed.

## Problem statement

Trellis needs a delivery and discovery design that simultaneously provides:

- exact, package-version-aligned API references in a fresh clone;
- one portable pointer from the repository's effective `AGENTS.md`;
- no replacement or uncontrolled rewriting of customer-authored instructions;
- deterministic refresh and pruning after package upgrades or removals;
- support for first-party and independently versioned satellite references;
- a reviewable source-control diff rather than hidden instruction mutation;
- no restore/build races in solutions containing multiple projects; and
- no requirement to load the full reference set into every agent session.

The current build-time copy solves only the first half of this list. Extending the same target to
append to `AGENTS.md` would make the second half worse.

## Existing invariants to preserve

This proposal changes consumer materialization and activation, not reference ownership or authoring.

| Invariant | Preserve |
|---|---|
| `Trellis.Core` ships the complete first-party set | Yes |
| Satellite packages ship their independently versioned references | Yes |
| `TrellisApiRefName` identifies the package that owns a reference | Yes |
| `trellis-start-here.md` routes the complete Core reference set to the cookbook | Yes; a tool-owned index also supports graphs without Core |
| API completeness, freshness, lint, and payload gates | Yes |
| References remain available without network access | Yes |
| Presence of a reference does not imply that package is installed | Yes |

The repository's full contributor `AGENTS.md` is **not** part of the consumer payload. It contains
framework-maintainer workflow and must never be copied into an application. Consumers receive only a
small Trellis-specific pointer in their own instructions.

## Proposed decision

Adopt an explicit agent-context installation:

1. Resolve references from a package-side manifest in the selected NuGet assets graph.
2. Materialize package-owned references and a tool-owned discovery index under a tool-neutral
   `.trellis/` directory.
3. Add a minimal, marker-delimited pointer to the consumer's `AGENTS.md` only through an explicit
   user-invoked command.

Normal restore and build perform none of these installation steps. They compile the application but
do not modify repository instructions or generated agent-context files.

### 1. Consumer repository layout

The default layout is:

```text
AGENTS.md
.trellis/
  README.md
  agent-context.json
  api-reference/
    trellis-start-here.md
    trellis-api-cookbook.md
    trellis-api-core.md
    ...
```

The `trellis/` directory inside each `.nupkg` remains the package payload location. The change is only
the materialized destination in the consuming repository:

```text
.github/*.md                 -> current consumer destination
.trellis/api-reference/*.md -> proposed consumer destination
```

`.trellis/` is preferred because it is:

- independent of GitHub, Copilot, or any other agent vendor;
- clearly owned by Trellis rather than by the consuming application's documentation system;
- an exact path an instruction can name without relying on directory scanning; and
- isolated from GitHub workflows, issue templates, and repository governance files.

A nested `.trellis/AGENTS.md` is not used. Hierarchical agent files govern work in their own subtree;
an instruction under `.trellis/` would not reliably apply when an agent edits application code.

The tool always generates `.trellis/README.md` as the discovery entry point:

- With Core present, it directs agents to `api-reference/trellis-start-here.md`, preserving the
  packaged router's mandatory cookbook routing-head guidance.
- It lists contributed satellite references with their source package IDs and resolved versions.
- Without Core, including analyzer-only and satellite-only graphs, it links directly to the available
  references. It must not require a missing router or cookbook, or imply that the full set is present.

The index uses tool-authored instructions and validated manifest filenames and package identities,
not package-supplied prose. It is generated deterministically, recorded as a tool-owned file in the
repository manifest, and subject to the same modification and removal safeguards as references.
It sits outside `api-reference/`, so no package document can replace it. Packages remain responsible
for reference content; the tool only supplies discovery and does not synthesize API guidance.

### 2. Package-side input contract

The command does **not** enumerate the current global `TrellisApiReference` MSBuild item. That item is
an unconstrained local path: a project or imported package can add an arbitrary file, and it carries
no authoritative package identity, version, package-relative path, or content hash. Copying it into a
committed context could disclose a developer-local file or assign false provenance.

Every compatible package that contributes references instead ships a machine-readable manifest at a
fixed package path, provisionally:

```text
trellis/reference-manifest.json
```

The package-side manifest contains only package-relative document paths, output document filenames,
and SHA-256 hashes. Package ID, resolved version, and package root are derived from the NuGet
resolved-assets graph, never trusted from values asserted by the manifest itself.

The package does not choose an arbitrary repository-relative output path. Each output is one
normalized filename ending in `.md`, with no directory separator, rooted path, `.`/`..` segment, or
reserved tool filename. The command always constructs the destination as:

```text
.trellis/api-reference/<document-filename>
```

This keeps package content out of `.trellis/README.md`, `.trellis/agent-context.json`, `AGENTS.md`, and
every other tool-owned or customer-owned location.

The command:

1. starts from explicit project or solution entry points;
2. consumes already-restored `project.assets.json` graphs;
3. resolves package roots through NuGet's declared package folders;
4. reads only fixed-path package manifests;
5. canonicalizes every document beneath that exact package root;
6. validates the output-filename grammar;
7. rejects rooted paths, traversal, links/reparse points, and hash mismatches; and
8. materializes only manifest-listed package documents, using the text contract in section 6.

It does not run package build targets to discover documents. A package can influence its own reference
content—as any dependency can influence its shipped assets—but it cannot redirect the tool to an
unrelated project or machine file.

The manifest is generated and verified during packing. Payload gates expand to require it and verify
that every listed document exists at the declared package-relative path with the declared hash.

### 3. Managed `AGENTS.md` pointer

The installer adds this small block:

```markdown
<!-- trellis-agent-context:start -->
## Trellis API usage

Before writing or changing code that uses Trellis, read
`.trellis/README.md` and follow its routing instructions.
Use those versioned references for exact Trellis signatures; repository instructions remain
authoritative for project conventions.
<!-- trellis-agent-context:end -->
```

The wording is deliberately narrow:

- Trellis controls API-usage guidance, not the application's architecture or workflow.
- The agent loads the small index first, follows the Core router when present, and reads area
  references on demand.
- No tool-specific import syntax such as `@file` is used.
- The full reference set is not injected into every context window.

The block is placed near the beginning of `AGENTS.md`, after optional frontmatter and the initial
top-level heading when those can be identified safely. Appending at the end is not sufficient:
instruction loaders may cap the number of bytes they read, so a pointer at the bottom of a large file
can be truncated.

### 4. Existing `AGENTS.md` behavior

The customer owns `AGENTS.md`. The tool follows these rules:

| State | Behavior |
|---|---|
| Selected source has no governing `AGENTS.md` up to the Git root | Create a minimal root file for that uncovered source, even if other subtrees have instruction files |
| An ancestor, scope-local, or descendant `AGENTS.md` governs selected source, with no Trellis block | Insert into every applicable boundary identified in section 8; preserve all existing content |
| One complete Trellis block | Update it in place only when the canonical block changes |
| Duplicate or incomplete markers | Stop without editing and explain the manual repair |
| Unsupported encoding, unsafe symlink, or path outside the permitted context/instruction boundaries | Stop without editing and print the block for manual insertion |

The tool never creates a new child `AGENTS.md` when an ancestor instruction file already applies. Some
agents concatenate ancestor instructions while others prefer the nearest file; creating a minimal
child could therefore hide the customer's root policies. If a nested `AGENTS.md` already exists, it is
the correct file to update for that subtree. If none exists below the root, the root file receives a
path-qualified pointer.

Edits preserve the existing BOM, encoding, newline style, frontmatter, and bytes outside the managed
block. Re-running the command without a package or block change produces no diff.

The tool offers a dry-run mode that prints the proposed changes before writing them.

### 5. Explicit lifecycle command

Introduce a small cross-platform .NET command, distributed as a **local, repo-pinned .NET tool**:
a checked-in tool manifest (`.config/dotnet-tools.json`), restored with `dotnet tool restore`, not a
machine-global tool and not an MSBuild task. The tool's exact version is therefore a file in the
repository, bumped in the same commit as the `Trellis.Core` `PackageReference` version it governs,
reviewable in the same diff — see the "expected package-add workflow" in the Source-control contract
section. A global tool was rejected because its version lives outside the repository entirely, which
reintroduces exactly the machine-specific-drift problem Alternative 6 rejects for the NuGet
global-packages cache, just one layer up: nothing would force two engineers, or a laptop and a CI
runner, to run the same tool version against the same package graph.

A single repository-wide manifest cannot pin one tool version that is simultaneously correct for two
independent scopes on different `Trellis.Core` versions (a real, required configuration — see "Scope and
monorepos" and acceptance criterion 16). The tool relies on .NET's native nested tool-manifest
resolution instead: `dotnet tool run` finds the nearest `.config/dotnet-tools.json` walking up from the
current directory, so each independent scope (`services/orders/`, `services/billing/`) carries its own
manifest pinning the tool version matching *that scope's* resolved `Trellis.Core` version. A
single-version repository still needs only the one manifest at its root. `check` compares the tool
version resolved for a given invocation against the `Trellis.Core` version resolved for that
invocation's own scope, never against a different scope's version, and never against a fixed
repository-wide expectation. When a scope's resolved graph has no `Trellis.Core` at all — an
analyzer-only or satellite-only graph, per "Compatible package floor" — there is no Core version to
compare against and this check does not apply; only that scope's own contributor schema/version
validation applies.

The working command shape:

```text
trellis agent init
trellis agent sync
trellis agent check
trellis agent remove
```

There is no `migrate` command and no automated legacy-`.github/` cleanup path — see "Migration from
`.github/`" below. The verb names above are logical; the actual invocation follows standard .NET local
tool conventions, which have two distinct flows the documentation must not conflate:

- **First install in a repository with no tool manifest yet:**
  `dotnet new tool-manifest` (only if `.config/dotnet-tools.json` does not already exist),
  `dotnet tool install <pinned-tool-package>`, then
  `dotnet tool run trellis agent init <explicit-entry-point>`. `dotnet tool restore` has nothing to
  restore until a manifest exists and names the tool, so it is not part of this flow.
- **Subsequent clone, with a committed tool manifest:** `dotnet tool restore` (installs the pinned
  version locally), then `dotnet tool run trellis agent sync` or `check`.

A local tool is not placed on `PATH` as a bare `trellis` executable — invocation is
`dotnet tool run trellis agent <verb>` (or the project's chosen command name). There is no supported
global-tool path: "Decisions resolved in review" item 1 rejects global distribution outright, not merely
as a non-default option, because the version-drift rationale (nothing forces two engineers, or a laptop
and a CI runner, to run the same tool version) applies whether or not global use is the default choice.
`init` also requires an explicit project or solution entry point argument in every flow above; a
bootstrap hint or template example that omits it does not run. The final package name and exact flag
syntax are implementation details to settle during implementation, but must stay consistent with this
invocation shape. The required semantics are:

- **`init`** — require explicit project or solution entry points, resolve contributed Trellis
  references from their restored assets graphs, write `.trellis/`, and create or merge the managed
  `AGENTS.md` block.
- **`sync`** — refresh references after package changes and prune only files owned by the previous
  manifest, using the graph entry points recorded by `init`.
- **`check`** — strictly read-only; fail when committed context differs from the resolved package
  graph, when the managed pointer is absent, or when the manifest is invalid.
- **`remove`** — use only the repository manifest to remove its owned instruction entries and files;
  it does not require a usable project, assets graph, package cache, or compatible installed package.

This must be a repository- or solution-scoped operation, not a `BeforeTargets=Build` action. A
`buildTransitive` target runs once per project and may run concurrently; it has no safe installation
or uninstallation boundary for merging one shared Markdown file.

Templates run the equivalent of `init` before publication and therefore contain a working
`AGENTS.md` pointer and committed `.trellis/` references from the first clone.

`init` and `sync` require current `project.assets.json` files by default. Their explicit `--restore`
mode delegates to NuGet and may update `obj/`, the global package cache, and configured lock files;
documentation and CI examples keep restore as a separate visible step.

`check` does not accept `--restore`, invoke restore/build targets, write `obj/` or `bin/`, change lock
files, or populate the NuGet package cache. It reads existing assets, packages, repository context,
and transaction state only. `remove` needs only `.trellis/agent-context.json`, the owned output files,
the recorded instruction files, and local transaction state; it does not evaluate the package graph.

All mutating commands use the overwrite protections and repository-wide transaction protocol in
section 6. Modified owned content and unowned destination collisions stop the operation before any
managed-file write. An explicit force or adoption mode may resolve a conflict only after displaying
the exact files and keyed instruction entries it will replace, delete, or claim. It never bypasses
path boundaries, package integrity checks, or malformed-marker checks.

### 6. Manifest and ownership

`.trellis/agent-context.json` records enough information to verify and safely update generated files:

```json
{
  "schemaVersion": 1,
  "scope": ".",
  "sourceRoots": ["src/App/"],
  "textHashFormat": "utf8-lf-no-bom-v1",
  "generatedBy": {
    "package": "Trellis.AgentContext",
    "version": "<tool-version>"
  },
  "graph": {
    "entryPoints": [
      {
        "path": "Trellis.slnx",
        "restoreSpecSha256": "<canonical-restore-spec-hash>"
      }
    ],
    "projects": [
      {
        "project": "src/App/App.csproj",
        "targetFramework": "net10.0",
        "runtimeIdentifier": null,
        "packages": [
          {
            "id": "Trellis.Core",
            "version": "<resolved-version>",
            "contentHash": "<nuget-content-hash>"
          }
        ]
      }
    ]
  },
  "instructionEntries": [
    {
      "key": "<stable-scope-key>",
      "instructionFile": "AGENTS.md",
      "scope": "src/App/",
      "indexPath": ".trellis/README.md",
      "canonicalSha256": "<previous-entry-canonical-text-hash>"
    }
  ],
  "toolOwnedFiles": [
    {
      "path": "README.md",
      "canonicalSha256": "<index-canonical-text-hash>"
    }
  ],
  "references": [
    {
      "path": "api-reference/trellis-api-core.md",
      "canonicalSha256": "<output-canonical-text-hash>",
      "sources": [
        {
          "package": "Trellis.Core",
          "packageVersion": "<resolved-version>",
          "packagePath": "trellis/trellis-api-core.md",
          "sha256": "<exact-package-file-byte-hash>"
        }
      ]
    }
  ]
}
```

The exact schema may change during implementation, but it must support:

- deterministic no-op synchronization;
- reproducible project/solution graph selection without machine-specific absolute paths;
- recorded source roots and stable keyed ownership of every path-qualified instruction entry;
- separate integrity hashes for exact package bytes and canonical materialized text;
- previous-content hashes for owned documents, the tool-owned index, and individual instruction entries;
- mandatory attribution of every source package, resolved version, package path, and content hash;
- collision detection;
- stale-file detection;
- pruning files previously written by Trellis without deleting unrelated files; and
- a no-write CI check.

#### Text integrity and checkout portability

Package manifest SHA-256 values verify **exact package-file bytes**, before decoding or normalization.
Materialized document and index hashes instead use strict UTF-8 text, remove an optional leading
UTF-8 BOM, and normalize CRLF and lone CR to LF. The hash input is UTF-8 without BOM. Invalid UTF-8
is rejected; spaces, final-newline presence, Unicode content, and Markdown are otherwise unchanged.
The versioned `textHashFormat` makes this distinction explicit rather than silently changing hashes.

New or changed generated Markdown is written as UTF-8 with BOM and LF. A canonical-content match is
a no-op even if Git checked the file out with CRLF or a different BOM convention. `check`, overwrite
preflight, and removal use the same canonical hashes, so ordinary Git text conversion does not look
like a user edit. No consumer `.gitattributes` edit is required. Instruction-entry hashes use this
canonical text representation too, but instruction-file edits preserve existing encoding, BOM,
newlines, and all bytes outside the managed entries.

When two packages contribute the same path with identical canonical text, the tool deduplicates the
output and records every contributor in a deterministically sorted `sources` array. This is required, not
optional: `Trellis.Core` and `Trellis.Analyzers` intentionally both contribute
`trellis-api-analyzers.md`. Each source still passes its own exact-byte hash check. When the canonical
contents differ, synchronization fails and identifies both packages; last-writer-wins would make the
resulting API reference dependent on restore order.

#### Overwrite and prune protection

`init`, `sync`, and `remove` preflight the complete proposed change set before modifying any managed
file:

- An existing document or index destination not owned by the previous repository manifest is a
  conflict, even if its content matches. The tool does not silently adopt files or claim an existing
  unowned marker entry. An existing customer-owned `AGENTS.md` is not itself a collision: insertion
  of a new entry follows section 4 without claiming ownership of the whole file.
- Before replacing or pruning an owned document or index, compare its current canonical hash with
  the previous manifest's hash. A mismatch requires explicit resolution rather than discarding edits.
- Before changing an instruction entry, compare that scope's entry with its recorded previous hash.
  Do not compare the whole shared block with a scope-local snapshot: another scope may legitimately
  have changed its own entry. Preserve other scopes' entries and customer-authored content.
- Validate ownership and conflicts for the whole operation, including files being pruned, before
  writing. Dry-run reports the same conflicts and never stages or writes a transaction.

Hashes establish change detection, not permission to modify arbitrary paths. All ownership records
remain subject to the context and instruction boundaries; force/adoption cannot expand them.

#### Shared writers and interrupted updates

All mutating commands acquire one exclusive writer lock for the Git working tree, regardless of
context scope. They re-read and validate shared instruction state under that lock before staging any
changes. This prevents two independent scopes from overwriting each other's root instruction entries.
The lock is process-held and released on process exit; a leftover lock file is not by itself proof of
a live lock, so the tool must be able to detect and clear a stale lock left by a crashed process.

Phase 1/2 scope here is deliberately smaller than a fully recoverable multi-file transaction: a durable
recovery journal and a persistent generation record for strict read-consistency across `check`/dry-run
are deferred (see "Decisions resolved in review" item 6 and "Out of scope") until concurrent
multi-scope usage is an observed need rather than a hypothetical one. What ships now:

- After preflight, each managed file — a reference document, the index, the manifest, or one
  instruction entry — is replaced with atomic per-file replacement where the filesystem supports it.
  Files are written independently, not staged as one cross-file transaction.
- Preflight reads each affected file **once** and derives both hashes from that same buffer: the
  **canonical hash**, compared against the manifest's recorded `canonicalSha256` to decide ownership and
  staleness (the same comparison "Overwrite and prune protection" already specifies — canonical hashing,
  with BOM stripped and CRLF/CR normalized to LF, is required here because generated files are written
  as UTF-8 with BOM and Git may rewrite line endings on checkout, so comparing raw bytes against a
  canonical hash would misreport an untouched, correctly-checked-out file as modified on every run); and
  an **exact-byte snapshot hash** of the identical read, kept only for the pre-write recheck below.
  Reading twice — once to decide ownership, again afterward to snapshot — would let a customer edit
  landing between the two reads become the accepted snapshot, so the later pre-write comparison would
  pass and the edit would be silently overwritten; one read producing both hashes closes that gap. For an
  instruction file such as `AGENTS.md`, the snapshot covers the **entire file being replaced**, not just
  the managed entry's text — the write reconstructs the whole file from this preflight read, so an
  unrelated customer edit elsewhere in the file between preflight and write must also be caught, not only
  a change to the entry itself. For a destination that does not yet exist at preflight time, record its
  **absence** as the snapshot state, so a file unexpectedly created before the write (by another process,
  or a customer) is also treated as a conflict rather than silently overwritten.
- Immediately before writing, the command recomputes the current exact bytes (or confirms continued
  absence, for a new destination) and compares against that same-run snapshot — never against the
  manifest's canonical hash, a different representation entirely. This is a narrow, same-run race check:
  it catches an edit made in the gap between this run's preflight and its write. It is not a substitute
  for the canonical-hash ownership check above, which is representation-correct but too coarse-grained
  (and too early) to catch a last-second edit.
- If a mutating command is interrupted partway through writing several files, the repository is left
  with whichever individual per-file replacements had already completed atomically, plus whichever had
  not yet started. Per-file atomicity guarantees no single file is left torn — but it does **not**
  guarantee the interrupted operation can be cleanly resumed: if a document was replaced but the manifest
  update that would record its new canonical hash did not complete before the crash, the file's new
  content now disagrees with the manifest's still-old recorded hash. Writing the manifest first instead
  of last only relocates the same problem to a different file. Deferring the recovery journal (Decisions
  resolved in review, item 6) means the tool cannot distinguish "this file's content came from my own
  interrupted write" from "a customer edited this file" — both look identical to preflight.
  So there is **no automatic clean-retry guarantee** in this phase: a bare re-run of `init`/`sync` after
  an interruption may report the affected files as conflicts requiring `--force`/adoption. The documented
  recovery is not a single blanket restore command — `git checkout -- .trellis/ AGENTS.md` would replace
  entire tracked files, discarding any unrelated uncommitted customer edits inside them; without a
  revision argument it restores from the index rather than a specific committed state; it does nothing
  for untracked files an interrupted `init` may have created; and it does not cover nested instruction
  files in other scopes. Instead: inspect the reported conflicts, preserve any customer changes found
  among them, then restore only the specific affected paths from an explicitly chosen revision (not a
  blanket path restore with no revision named), and handle untracked leftover outputs through reviewed
  adoption or deletion rather than a `git` command that cannot see them. Re-run afterward with current
  package assets. When there is no previously committed context to restore from — for example, an
  interrupted first-ever `init` — force/adoption is the only path, since there is nothing to fall back
  to. This applies to a single user working alone, not only to concurrent multi-scope usage — it is a
  consequence of deferring the journal, not of concurrency.
- `check` and dry-run never write and never take the writer lock. Because there is no generation
  record in this phase, a `check` that races an in-progress `init`/`sync` is not guaranteed to observe
  a consistent before-or-after snapshot — it may see a partially updated repository. The writer lock
  still prevents two *mutating* commands from running concurrently, so this window is `check`-only.
  Add the generation record if and when that window becomes a real problem, rather than building it
  against a usage pattern that does not exist yet.

#### Graph identity and freshness

Raw `project.assets.json` bytes are not hashed into the committed manifest because they contain
checkout paths, package-cache roots, and output paths. The tool instead canonicalizes a semantic graph
of relative project identity, target framework/runtime identifier, package ID, resolved version, and
NuGet content hash.

To detect stale assets, `init`, `sync`, and `check` evaluate the current NuGet/MSBuild restore
specification in memory without running package build targets or writing restore output, normalize
away machine paths, and compare it with the restore specification embedded in the existing assets
file. A mismatch fails with an instruction to run restore; it is never treated as current merely
because the old assets file still exists. Cross-directory and cross-OS fixtures must produce the same
semantic graph digest.

### 7. Source-control contract

The generated `.trellis/` directory and managed `AGENTS.md` block are intended to be committed.

This is necessary because cloud agents commonly begin from a fresh clone before restore. References
that exist only in the NuGet global-packages cache, `obj/`, or a developer's previous build are not a
portable agent context.

The expected package-add workflow is therefore:

```text
add/update the Trellis PackageReference
run the explicit agent-context init or sync command
review the AGENTS.md and .trellis/ diff
commit both with the package change
```

Templates and documentation should describe the reference refresh as part of changing Trellis package
versions. Repositories that want enforcement add `trellis agent check` to CI.

### 8. Scope and monorepos

The Git repository root is the hard outer boundary. The design distinguishes two write boundaries
inside it:

- **Context root:** the selected repository or subtree scope. The manifest, tool-owned index, and
  every `.trellis/api-reference/` file must remain beneath it.
- **Instruction boundary:** the existing `AGENTS.md` files governing selected source, discovered
  above and within its source roots as described below. If selected source has no governing file,
  creating root `AGENTS.md` is allowed. Previously recorded instruction files remain eligible only
  for verified removal of the selected scope's old entry after a boundary change.

No context file may escape the context root, and no instruction edit may target a file outside that
explicit governing set. Managed outputs never escape the Git root; only the tool-owned transaction
area described in section 6 may live in worktree metadata outside it.

The default context root is the selected solution's Git repository root.

A repository may contain independent subtrees using different Trellis versions. One flat reference
set cannot truthfully describe both. When the resolved inputs contain incompatible versions, the tool
must not pick one silently. It requires explicit independent scopes and materializes:

```text
services/orders/.trellis/...
services/billing/.trellis/...
```

Source roots default to the selected projects' directories within the context root and are recorded
in the manifest. For each source root, discover the nearest existing ancestor or root-local
`AGENTS.md`, and every existing descendant `AGENTS.md` within the selected source subtree. Descendant
discovery is not limited to directories containing a project file: a policy under
`src/App/Features/` can govern source belonging to `src/App/App.csproj`.

Exclude Git metadata, `.trellis/`, `bin/`, `obj/`, and evaluated generated/intermediate/output
directories from source discovery; do not follow links/reparse points or cross nested Git repository
boundaries. Do not blanket-exclude Git-ignored directories that may contain selected source. Phase 1
must define how evaluated source membership and explicit source-root selection handle these cases
without running build targets. Linked source outside a project's directory must be covered by an
explicit recorded source root. Source outside the selected context or Git root fails with a diagnostic
requiring scope correction; it is not silently treated as covered or used to widen instruction writes.

Group source subtrees by their effective instruction boundary and install a correctly path-qualified
entry in every applicable file. For example, `AGENTS.md`, `src/App/App.csproj`,
`src/App/Features/AGENTS.md`, and `src/App/Features/Feature.cs` require entries in both instruction
files when the root file also governs selected source outside `Features`. This supports both
ancestor-concatenating and nearest-only loaders.

If any selected source has no governing instruction file, create one at the Git root for that
uncovered source. Existing instructions in a different subtree do not prevent this. Never create an
additional child file merely to carry a Trellis pointer.

If a nested file already governs a subtree, its managed block points to the applicable local
`.trellis/`. Otherwise, the root managed block contains path-qualified entries such as:

```markdown
For files under `services/orders/`, read
`services/orders/.trellis/README.md`.
```

Each pointer is a stable keyed entry recorded in `instructionEntries`. One managed block may contain
entries for several independent scopes. `sync` rediscovers descendant boundaries and adds, moves, or
removes entries when instruction layout changes. It may remove a previously owned entry from a file
that no longer governs selected source, but may not add unrelated content there. `remove` deletes only
the selected scope's entry and removes the marker block only after its final entry is gone.

Paths in a managed block are rendered relative to the file containing that block. Tests cover
ancestor-concatenating and nearest-only loaders, root graphs with shadowing descendant files, multiple
scopes sharing one root block, and single-scope removal from that shared block.

### 9. Normal restore and build behavior

For a graph containing only compatible package versions:

- NuGet packages continue to carry their reference payloads.
- Compatible packages carry the new package-side reference manifest.
- The command reads NuGet assets and package manifests rather than global MSBuild items.
- `_CopyTrellisApiReference` no longer runs automatically before `Build`.
- Restore and build make no repository-source changes.
- Missing or stale agent context does not produce ordinary compiler warnings.
- An explicit `check` command is the enforcement mechanism.

Agent documentation is important, but it does not affect runtime correctness. Making every normal
consumer build warn—or fail under warnings-as-errors—because the repository chose not to install
agent context would be disproportionate.

To address first-run discovery without repository mutation or a build warning, the packed targets emit
a single `Importance="Low"` `<Message/>` when no `.trellis/` directory is detected, pointing at the
"Explicit lifecycle command" setup steps rather than a single copy-pasteable command — the correct
command differs between a repository with no tool manifest yet and one with a committed manifest to
restore, and `init` always needs an explicit entry point the message cannot supply generically. This
borrows the same technique `_CopyTrellisApiReference` already uses for its own "`.git` not found within
11 levels" notice — a low-importance message, invisible in default build output, never participating in
`TreatWarningsAsErrors`, writing nothing — but is emitted by its **own new target**, packed and
maintained independently of `_CopyTrellisApiReference`. It must keep working after Phase 3 deletes
`_CopyTrellisApiReference`, `TrellisSyncApiReference`, and the missing-copy warning entirely; it is not a
modification bolted onto a target scheduled for removal. It is a hint, not the enforcement mechanism —
`check` remains that — and a repository that never runs `init` sees the same message on every build
rather than a one-time nudge, so it costs nothing to ignore.

## Why `AGENTS.md` is not modified automatically

Automatically appending a line from `buildTransitive` was considered and rejected for five independent
reasons:

1. **Ownership:** `AGENTS.md` is customer-authored repository policy, not package output.
2. **Execution model:** package targets run repeatedly and concurrently per project, not once per
   package installation.
3. **No uninstall hook:** removing a PackageReference cannot reliably remove or update the line.
4. **Trust:** changing agent instructions changes future tool behavior and therefore deserves explicit
   review, especially when initiated by a dependency.
5. **Reliability:** append-only merging duplicates content, cannot safely repair conflicts, and can
   place the pointer beyond an instruction loader's size limit.

The explicit command may edit `AGENTS.md` because the user invoked that operation for that purpose and
can review the resulting diff.

## Security and trust boundary

Package-supplied agent documentation is executable only in the social sense—it influences what an
agent writes and which commands it may choose. That makes provenance important even though the files
are Markdown.

The implementation must:

- show every contributing package and version in the manifest;
- derive package identity and version from NuGet assets rather than package-authored fields;
- accept documents only through the fixed package-side manifest contract;
- require every source path to remain beneath its resolved package root;
- reject project-authored `TrellisApiReference` items and other arbitrary local-file inputs;
- refuse context writes outside the context root and instruction writes outside the approved
  governing `AGENTS.md` set;
- avoid following source or destination symlinks/reparse points outside their allowed roots;
- fail on conflicting document paths;
- restrict the managed root instruction to Trellis API usage;
- never place package-provided prose directly into `AGENTS.md`; and
- change only marker-delimited content during update or removal.

The reference payload should remain API documentation. Repository workflow, credentials, command
approval, and unrelated architectural instructions belong to the consumer's own `AGENTS.md`.

## Compatible package floor and immutable legacy targets

Already-published NuGet packages are immutable. Older `Trellis.Analyzers` and Core-independent
satellite versions may carry `Trellis.ApiReference.targets` themselves; other satellites carry
`Trellis.ApiReference.Payload.targets`, whose build warning assumes the old copy target. Removing
those assets from a new `Trellis.Core` cannot change what an older package imports.

The clean cut therefore requires coordinated compatible releases:

- every first-party package in the lockstep release;
- `Trellis.Analyzers`, which ships independently of Core's build assets; and
- every known satellite that contributes a reference payload.

Every first-party package ships `trellis/reference-manifest.json`, even when its `documents` list is
empty because Core carries its reference. The manifest identifies the package as a member of the
first-party lockstep cohort. Core's manifest carries the authoritative cohort package-ID list, derived
and gated from the repository's packable-project set.

For every resolved package whose ID is in that cohort, the command requires:

1. a current-schema manifest;
2. the exact same resolved lockstep version as the Core reference set; and
3. no `_CopyTrellisApiReference`, `TrellisSyncApiReference`, or old
   missing-copy-logic warning target; and
4. a passing package-manifest payload gate.

Without Core, the command validates each contributor's schema, payload, and absence of legacy targets
without requiring a Core version or Core-only files. Analyzer-only and Core-independent satellite
graphs are supported through the generated index. When Core is present, its cohort checks apply to
all resolved cohort members, including Analyzers.

An independently versioned satellite has no lockstep cohort. Its own manifest declares its documents
and contract schema; compatibility is based on that schema and the satellite's separately released
version. The tool also scans every resolved package for the known legacy copy/payload target paths, so
an older or third-party satellite cannot escape detection merely because it is absent from Core's
cohort list.

Before `init`, `sync`, `check`, or any graph-derived `AGENTS.md` edit, the command inspects the
complete resolved graph. If any cohort member has the wrong version or manifest, or any
resolved package still contains a legacy copy/payload target, it stops without writing and lists the
package IDs and versions that must be upgraded. The tool does not try to suppress an old target with a
property supplied by a new package: a Core-independent old package may be the only package in the
graph, and import ordering must not decide whether repository mutation occurs.

`remove` is the exception. It is repository-manifest-driven and remains available after packages are
downgraded or removed, entry points disappear, assets become unusable, or the NuGet cache is cleared.

The no-build-mutation guarantee applies only after this compatibility check passes. Mixed old/new
graphs are unsupported and covered by end-to-end tests proving that the command refuses them before
creating `.trellis/` or changing `AGENTS.md`.

## Migration from `.github/`

There is no automated migration command. `init`, `sync`, and `remove` never read, write, or delete
anything under `.github/` — legacy files there are simply outside every command's context root and
instruction boundary, in the same way any unrelated repository directory is.

Legacy files have no ownership manifest, so automatic deletion would be unsafe regardless: a consumer
may have edited or repurposed one, and the tool has no way to tell a customer edit from an untouched
copy. Rather than build a confirmation/opt-in flow to manage that risk, cleanup is manual: run `init`,
review that `.trellis/` looks correct, then delete the old `.github/trellis-*.md` files yourself. This
is proportionate to the current adoption footprint; an interactive or non-interactive automated
migration path can be added later if Trellis gains enough consumers that manual cleanup becomes a real
support burden.

The rollout does not dual-write. Maintaining copies in both `.github/` and `.trellis/` creates two
apparent sources of truth and recreates the stale-document failure this design is meant to remove, so
documentation should tell consumers to delete the legacy files promptly rather than leave both in place
indefinitely. The release that introduces the explicit agent-context command removes the old automatic
copy target and its configuration surface regardless of whether any given consumer has deleted their
legacy files yet.

Existing configuration maps as follows:

| Current mechanism | Proposed replacement |
|---|---|
| `TrellisApiReferenceRoot` | explicit command scope/root option |
| `TrellisDisableApiReferenceSync` | default behavior is no automatic sync |
| `TrellisSyncApiReference` target | explicit `sync` command |
| nearest `.github/` selection | selected repository or subtree scope |

## Consequences

### Positive

- All agents receive one standard discovery pointer through `AGENTS.md`.
- The references are tool-neutral and available before restore in a fresh clone.
- Customer instructions are preserved and changed only through an explicit operation.
- A manifest makes update, verification, collision handling, and pruning deterministic.
- Normal build becomes free of repository-writing side effects and parallel copy races.
- First-party full-set discovery and independently versioned satellite references continue to work.
- Core-independent graphs receive a valid discovery index without taking a Core dependency.
- Canonical text verification tolerates Git newline conversion while preserving package-byte integrity.

### Negative

- Adding or updating Trellis gains an explicit synchronization step.
- Consumers commit approximately the same documentation payload currently copied into `.github/`.
- A new cross-platform command must be distributed, versioned, documented, and tested.
- The clean cut requires coordinated releases of every first-party package and known satellite that
  carries reference-delivery assets.
- Every first-party package gains a small package-side manifest, even when Core carries its document.
- Repositories that skip initialization still have no universal way for an agent to discover the
  package-supplied references.
- Multi-version monorepos require explicit scopes rather than one convenient shared directory, each with
  its own nested tool manifest.
- An interrupted mutating command has no automatic multi-file rollback, resumption, or clean retry in
  Phase 1/2. Per-file atomic replacement prevents a single torn file, but a file whose content was
  written before a crash and a manifest whose recorded hash was not yet updated to match it look
  identical to a customer edit — the tool cannot tell them apart without the deferred recovery journal.
  The documented recovery is to review and force/adopt the reported conflicts, or to preserve any
  customer changes among them and selectively restore only the affected paths from an explicitly chosen
  revision before re-running with current package assets; a bare re-run is not guaranteed to complete on
  its own, and no single blanket restore command is safe to prescribe (see "Shared writers and
  interrupted updates").
- A `check` run can race an in-progress `init`/`sync` and observe a partially updated repository, since
  the persistent generation record that would guarantee a consistent snapshot is also deferred.

### Neutral

- Package-owned API reference content and the Core router-to-cookbook strategy do not change; the
  generated index adds discovery for whichever references the selected graph actually contributes.
- NuGet package payload size does not materially change.
- This is not a runtime Trellis API and does not affect application binaries.

## Alternatives considered

### 1. Append a pointer to `AGENTS.md` during every build

Rejected. It maximizes first-run discovery but silently changes a customer-owned policy file from a
dependency, races in multi-project builds, has no removal lifecycle, and is difficult to make
idempotent without a real parser and ownership markers.

### 2. Create `AGENTS.md` automatically only when one does not exist

Rejected. It gives new repositories a good experience while leaving established repositories—the
ones most likely to have meaningful instructions—with delivered but undiscoverable references. It
also remains an unexpected build-time source mutation.

### 3. Keep references under `.github/` and add only a root pointer

Rejected as the long-term design. A pointer would solve discovery, but generated dependency
documentation would remain mixed into a hosting-vendor directory even though no GitHub feature
loads those arbitrary files. `.trellis/` states ownership and intent more accurately.

### 4. Put a package-supplied `AGENTS.md` under `.trellis/`

Rejected. Hierarchical instruction files apply by directory scope; this would describe edits inside
`.trellis/`, not application code elsewhere in the repository.

### 5. Use a tool-specific import from root `AGENTS.md`

Rejected. Import syntax is not part of the portable `AGENTS.md` format, and importing the entire set
would spend context on references unrelated to the current task. A plain-language pointer to the
small router is portable and preserves on-demand loading.

### 6. Leave references only in the NuGet global-packages cache

Rejected. The location is machine-specific, commonly outside an agent's workspace, and unavailable
to cloud agents before restore. It also makes the effective documentation impossible to review in the
application's pull request.

### 7. Publish only web-hosted references

Rejected as the primary path. Web references weaken exact package-version alignment, require network
access, and may be unavailable to restricted agents. Versioned web docs may remain a human-facing
convenience, not the source of truth for generated code.

### 8. Continue automatic copying but move the destination to `.trellis/`

Rejected as the final design. It improves naming but retains build-time source mutation, per-project
parallelism, and the absence of a safe repository-level prune.

### 9. Use the existing `TrellisApiReference` MSBuild item as command input

Rejected. The item contains an arbitrary local path and no authenticated package provenance. A project
or unrelated package could point it at a machine-local file, which the command would then copy into a
committed directory. Fixed-path package manifests resolved through NuGet assets constrain input to
files shipped by the identified package.

### 10. Create a scoped child `AGENTS.md` for every independent Trellis graph

Rejected. In tools that use only the nearest instruction file, a generated minimal child would hide
the customer's ancestor policies. Scoped context updates the nearest file that already governs the
subtree and uses a path-qualified root entry when no child file exists.

## Implementation plan

Implementation is split so each layer can be reviewed and mutation-tested independently.

### Phase 1 — Define the context contract

- Finalize the managed `AGENTS.md` block and tool-owned `.trellis/README.md` discovery index,
  including Core-present, analyzer-only, and satellite-only routing.
- Finalize the package-side `trellis/reference-manifest.json` schema.
- Finalize the repository-side `agent-context.json` schema, graph identity, mandatory `sources`
  provenance, and ownership rules.
- Generate and gate Core's authoritative first-party lockstep cohort from the packable-project set.
- Define repository-root, scoped-root, encoding, newline, marker-conflict, and symlink behavior.
- Specify exact package-byte hashes versus canonical text hashes, deterministic output encoding, and
  no-op behavior across Git checkout transformations.
- Define source-root discovery, below-project instruction boundaries, generated-directory exclusions,
  linked-source handling, and explicit ownership of each keyed entry.
- Define full-operation conflict preflight, reviewed force/adoption, and the worktree-wide writer lock.
  The durable recovery journal and generation-record read-consistency mechanism for `check`/dry-run are
  explicitly deferred (see "Decisions resolved in review" item 6) — do not design them in Phase 1.
- Decide the local tool-manifest layout, including nested per-scope manifests for multi-version
  monorepos (relying on .NET's native nearest-manifest resolution), and how the tool's own version
  resolved for a given invocation is validated against that invocation's scope-resolved `Trellis.Core`
  version during `check` — with no check applying when a scope's graph has no Core at all.
- Add golden test fixtures for empty, existing, large, malformed, and nested `AGENTS.md` files,
  including policies below a project directory and shared blocks with independent scope entries.
- Extend pack gates to verify package-relative paths and hashes without evaluating MSBuild items.

### Phase 2 — Build the explicit command

- Require explicit project or solution entry points on `init`.
- Parse existing NuGet assets graphs and fixed-path package manifests without invoking package build
  targets.
- Implement portable semantic graph normalization and read-only restore-spec freshness evaluation.
- Implement `init`, `sync`, `check`, `remove`, and dry-run behavior.
- Generate and own the discovery index separately from package documents; validate its local links.
- Implement source-root and descendant instruction discovery, including rediscovery during `sync`.
- Implement canonical text hashing and exact package-byte verification as separate operations.
- Preflight unowned destinations and modified owned content before overwriting or pruning.
- Serialize writers across scopes with the exclusive writer lock. Use atomic per-file replacement, with
  ownership decided against the manifest's canonical hash at preflight and a separate exact-byte
  snapshot rechecked immediately before each write to catch only same-run races — not the manifest hash
  itself, which uses a different (canonical) representation than raw file bytes. Do not build a
  dedicated recovery journal in this phase (see "Decisions resolved in review" item 6): a bare re-run of
  `init`/`sync` after an interruption may report conflicts requiring `--force`/adoption rather than
  completing automatically, and that is by design, not a defect to work around.
- Keep `check` and dry-run read-only and lock-free. A `check` racing an in-progress `init`/`sync` may
  observe a partially updated repository — that consistency guarantee is deferred, not a Phase 2 bug.
- Package the command as a local .NET tool consumed through a checked-in tool manifest, supporting
  nested per-scope manifests for multi-version monorepos; make `check` fail when the tool version
  resolved for an invocation and the `Trellis.Core` version resolved for that invocation's scope
  disagree, and skip the check entirely when that scope's graph has no Core.
- Emit the low-`Importance` bootstrap `<Message/>` from its own new target when `.trellis/` is absent —
  independent of `_CopyTrellisApiReference`, so it keeps working after Phase 3 removes that target.
- Preserve all customer bytes outside the managed block.
- Implement keyed instruction-entry ownership, graph recording, collision detection, mandatory
  duplicate-source provenance, manifest validation, compatible-cohort checks, and owned-file pruning.

### Phase 3 — Coordinate the clean package cut

- Keep packing reference payloads under `trellis/`.
- Ship the package-side manifest from every first-party package and known satellite; Core's manifest
  carries the authoritative lockstep cohort.
- Remove `_CopyTrellisApiReference`, `TrellisSyncApiReference`, the missing-copy-logic warning, and
  their root/disable configuration properties from every coordinated package release.
- Make the explicit command reject any unresolved legacy contributor before writing.
- Replace the existing scratch-consumer probe with end-to-end command tests.
- Publish the agent-context tool package in lockstep with the first-party release cohort, so a
  tool-manifest version bump and a `Trellis.Core` version bump land together in one PR.

### Phase 4 — Migrate first-party entry points

- Pre-initialize Trellis templates, including a checked-in tool manifest pinned to the matching tool
  version.
- Update package README and NuGet README setup instructions with both flows: first install
  (`dotnet new tool-manifest` + `dotnet tool install` + `dotnet tool run trellis agent init
  <entry-point>`) and subsequent clone (`dotnet tool restore` + `dotnet tool run trellis agent sync`).
- Document the `.trellis/README.md` entry point for all graphs and conflict resolution. Document the
  actual interrupted-run recovery path — re-run `init`/`sync` with current package assets, which may
  report conflicts requiring `--force`/adoption, or preserve customer changes and selectively restore
  only the affected paths from an explicitly chosen revision before re-running — rather than a single
  blanket restore command or an implied guaranteed automatic clean retry, since Phase 1/2 defers the
  recovery journal that would provide one.
- Update `trellis-start-here.md` wording from `.github/` to `.trellis/api-reference/`.
- Update framework contributor documentation without shipping the framework's `AGENTS.md`.
- Add `check` enforcement to first-party templates' CI by default (see "Decisions resolved in review"
  item 3), not as an opt-in.

### Phase 5 — Retire legacy output

- Document the manual cleanup step — delete `.github/trellis-*.md` once `.trellis/` is verified — in
  package README/NUGET_README and framework contributor docs. No automated detection or deletion
  command ships (see "Decisions resolved in review" item 4 and "Migration from `.github/`").
- Remove the old destination's tests and documentation in the same release; do not add a dual-write
  compatibility period.

## Acceptance criteria

The design is complete only when end-to-end tests prove:

1. With a compatible package graph, normal restore and build do not create or modify `AGENTS.md`,
   `.trellis/`, `.github/`, or tracked source; ordinary NuGet cache, `obj/`, and `bin/` writes remain
   normal build outputs.
2. `init` with explicit graph entry points creates `.trellis/README.md`, `.trellis/api-reference/`,
   a manifest, and a minimal root `AGENTS.md` when selected source has no effective file.
3. `init` preserves every existing byte outside the inserted block when an effective `AGENTS.md`
   exists.
4. A second `init` or `sync` produces no diff.
5. A package update refreshes documents and manifest versions deterministically.
6. `check` fails on a missing pointer, stale document, changed generated document, unknown manifest
   schema, graph mismatch, stale assets graph, or package-version mismatch without invoking restore
   or changing the repository, `obj/`, `bin/`, lock files, or the NuGet package cache.
7. Removing a satellite package prunes only its manifest-owned reference.
8. Conflicting contributions fail with both package identities in the error.
9. Identical Core/analyzer contributions produce one file with both contributors in the sorted
   `sources` array; changing or removing one contributor updates provenance deterministically.
10. Package manifests with traversal, rooted paths, missing files, source links/reparse points, hash
    mismatches, directory separators in output names, non-Markdown output names, or reserved-name
    collisions fail before any repository write.
11. Project-authored or imported `TrellisApiReference` items cannot add a document.
12. Incomplete or duplicate managed markers fail without editing `AGENTS.md`.
13. UTF-8 BOM, UTF-8 without BOM, CRLF, and LF instruction files retain their original format.
14. Repository-boundary and destination-link tests prove no context write can escape its context
    root and instruction writes are limited to approved governing files or removal of previously
    owned entries. Only validated worktree-local transaction metadata may live outside the Git root.
15. A committed fresh clone exposes `.trellis/README.md` before restore, with valid local discovery
    links for Core-present, analyzer-only, and satellite-only graphs. Only Core-present graphs require
    the packaged `trellis-start-here.md` and cookbook route.
16. A multi-version monorepo fails the single-scope operation and succeeds with explicit independent
    scopes without creating a child `AGENTS.md` that shadows an existing ancestor policy. Each scope
    resolves and validates its own nested tool manifest against its own `Trellis.Core` version; `check`
    never compares one scope's tool version against a different scope's `Trellis.Core` version.
17. Root-only, nested-existing, ancestor-concatenating, and nearest-only instruction layouts all
    receive the correct path-qualified pointer, including `src/App/Features/AGENTS.md` below
    `src/App/App.csproj`. Adding or removing such a policy is reflected by `sync`; an unrelated
    subtree policy does not prevent root-file creation for uncovered source.
18. `init`, `sync`, and `remove` never read, write, or delete anything under `.github/`; legacy files
    there are left untouched regardless of whether their content matches a known package payload.
19. A mixed old/new package graph fails before changing `AGENTS.md` or `.trellis/` and identifies
    every incompatible package, including new Core plus an old non-payload first-party package; an
    all-compatible graph contains no legacy build copy or warning target.
20. The complete first-party set and independently versioned satellite references still reach the
    consumer.
21. Two scopes sharing one root managed block can initialize, synchronize, and remove either scope
    without changing the other's keyed entry; the marker block disappears only after the last entry.
22. `remove` succeeds after a package downgrade/removal, missing entry point, stale or absent assets,
    and empty NuGet cache; modified owned state requires an explicit reviewed force operation.
23. The semantic graph digest is identical across checkout paths and operating systems, while a
    project, imported props/targets, central-package version, TFM/RID, or resolved-package change is
    detected even when an old assets file still exists.
24. `init/sync --restore` clearly reports and tests its allowed NuGet cache, `obj/`, and lock-file
    side effects separately from strictly read-only `check`.
25. References and the generated index survive actual Windows-to-Linux and Linux-to-Windows
    commit/clone round trips with Git text conversion: `check` passes after the separate restore step,
    `sync` is a no-op, and `remove` does not misidentify newline/BOM conversion as a user edit.
    Substantive content changes still fail verification. Package-byte hash mismatches fail even when
    canonical text would match; same-text contributors with different line endings retain both sources.
26. `init` and `sync` reject unowned destination files, including byte-identical files, without
    silently adopting them. Modified owned references, the index, and the selected instruction entry
    block replacement or pruning until explicitly resolved. A conflict discovered late in preflight
    leaves all managed files unchanged; force/adoption previews the exact affected paths and entries.
27. Concurrent initialization, synchronization, and removal of independent scopes sharing root
    instructions serialize without losing entries or producing manifests inconsistent with the block.
    Legitimate changes to another scope's entry do not trigger a false modification conflict.
28. Failure injected between individual per-file replacements (including manifest replacement) leaves
    every already-completed file intact and every not-yet-started file untouched — no partial file is
    corrupted, and this holds regardless of write order (files before manifest, or manifest before
    files). Re-running `init`/`sync` afterward, with current package assets available, either completes
    or reports the affected files as conflicts requiring explicit `--force`/adoption; it is not required
    to complete automatically, since the tool cannot distinguish its own interrupted write from a
    customer edit without the deferred recovery journal. Preserving customer changes among the reported
    conflicts and selectively restoring only the affected paths from an explicitly chosen revision, then
    re-running, is a documented, tested recovery path — not a single blanket restore command. This
    criterion does not require automatic multi-file rollback, automatic clean retry, or a recovery
    journal — those are deferred (see
    "Decisions resolved in review" item 6).
29. `check` and dry-run perform no writes and never take the writer lock. Ordinary and linked Git
    worktrees use the correct isolated lock file location. A `check` that races an in-progress
    `init`/`sync` is allowed to observe a partially updated repository in this phase — it is not
    required to detect and reject a transaction that begins and finishes during its read interval, since
    the generation-record consistency guarantee that would provide that is deferred.
30. Source discovery excludes tool/generated output, covers explicitly selected linked source, and
    diagnoses out-of-scope source or repository boundaries without widening writes. Git-ignored
    selected source is not silently omitted, and descendant policies need no colocated project file.

## Open questions for review

None outstanding. The five questions originally listed here — command distribution, default insertion
point, CI adoption, legacy cleanup, and tool bootstrap — are resolved in "Decisions resolved in review"
near the top of this document, with the detail folded into the relevant sections below.

## Out of scope

- Changing the content or structure of the Trellis API references themselves.
- Loading all reference files into every agent session.
- Supporting legacy agent-specific instruction filenames.
- Enforcing application architecture or coding style through package-supplied instructions.
- An automated `migrate` command for legacy `.github/` files; cleanup is manual (see "Migration from
  `.github/`").
- The durable recovery journal and persistent generation-record read-consistency protocol for
  concurrent multi-scope transactions; deferred until concurrent usage is observed (see "Decisions
  resolved in review" item 6 and "Shared writers and interrupted updates").
- Treating the presence of an optional-package reference file as proof that the package is installed.
