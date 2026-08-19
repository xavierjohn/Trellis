# API reference lint

The API reference lint gate blocks documentation regressions in `docs/docfx_project/api_reference/*.md`.

Run it locally with:

```powershell
pwsh docs/lint-api-reference.ps1
```

The solution build runs the same script through `docs\Trellis.DocsLint.csproj`, so failures are emitted as MSBuild errors.

## Rules

- **TRLDOC001**: Bare cross-doc links such as `](trellis-api-core.md)` must point at a specific anchor, for example `](trellis-api-core.md#some-section)`. Lines inside fenced code blocks are skipped.
- **TRLDOC002**: Filler table rows such as `| — | — | No public properties.` are not allowed. Lines inside fenced code blocks are skipped.
- **TRLDOC003**: Anchored same-file links such as `](#some-section)` and sibling API-reference Markdown links such as `](trellis-api-core.md#some-section)` or `](completeness-report.md#some-section)` must resolve to an existing heading in the target file. Links may include query strings before anchors, such as `](completeness-report.md?v=2#some-section)`. The gate skips absolute URI links and cross-surface relative paths such as `](../articles/example.md#some-section)`.
- **TRLDOC004**: Every `api_reference/*.md` file must be *owned* — either by a package declaring the matching `<TrellisApiRefName>`, or by being listed under `CrossCuttingDocs` in `docs/api-reference-docs.psd1`. An unowned doc has no source directory to compare against, so `docs/audit-doc-freshness.ps1` cannot tell whether it has gone stale and silently skips it.
- **TRLDOC011**: Every shipping package (`Trellis.*/src/*.csproj`, unless `<IsPackable>false</IsPackable>`) must declare a `<TrellisApiRefName>`. It is the inverse of TRLDOC004: that rule catches a doc with no package, this one catches a package with no doc. It was convention-only until a Trellis package shipped to production with no reference at all, leaving the consuming agent to invent an API against it.

TRLDOC004 used to assert that some package *packs* each file. It no longer can, and the change is deliberate: `Trellis.Core` now ships the whole `api_reference` directory as a glob, so delivery is guaranteed by construction and the old check could never fail again. See [API reference delivery](#api-reference-delivery) below.

Proving docs are *delivered* needs `build/test-apireference-payload.ps1`, which packs real packages, restores them into scratch consumers outside the repository and asserts which `.github` directory the files land in — covering the nearest-`.github` preference, the `.git` boundary that stops the walk escaping into an unrelated parent checkout, the `TrellisApiReferenceRoot` override, the `TrellisDisableApiReferenceSync` opt-out, and a satellite package contributing its own reference. Run it with `pwsh ./build/test-apireference-payload.ps1`; it runs in the Build workflow.

- **TRLDOC010**: The recipe count quoted in `trellis-start-here.md` ("The *n* recipe bodies beneath it") must equal the number of live recipes in the cookbook, excluding `*(retired)*` headings. That file tells agents the Patterns Index is exhaustive and uses the count to justify a token budget, so a stale number quietly undermines both claims.

- **TRLDOC006**: Every `## Recipe N` heading in `trellis-api-cookbook.md` must have a matching `Examples/CookbookSnippets/Recipe<NN>_*.cs` file. Headings marked `*(retired)*` are skipped, because they exist only to keep old anchors and cross-references resolving and carry no code to pin.

TRLDOC006 exists because recipes are the most-copied code in the doc set: a reader lifts a recipe verbatim into their service, so a recipe that no longer compiles is worse than no recipe. The gate itself only proves the snippet *file* exists; the compile check comes from `Examples/CookbookSnippets/CookbookSnippets.csproj` being a member of `Trellis.slnx`, so CI's `dotnet build` compiles every snippet under the repository's full analyzer settings. The two halves are load-bearing together — removing the project from the solution would leave TRLDOC006 asserting nothing but a filename.

Snippets pin the recipe, they do not execute it: the project has no test runner, so `[Fact]` methods in snippets (Recipes 10 and 26) are compile-checked only. Where a recipe legitimately cannot compile in this project, keep the real code in a comment and say why — Recipe 26's `AddMediator(...)` is elided because it is emitted by `Mediator.SourceGenerator`, which the project deliberately does not reference (several recipes show commands with no paired handler, and the generator fails those with `MSG0005`).

- **TRLDOC007**: Every `## Recipe N` heading in `trellis-api-cookbook.md` must be linked from the `## Patterns Index` section. Retired headings are skipped on the same grounds as TRLDOC006. If the `## Patterns Index` heading itself is missing, the rule fails once and suppresses the per-recipe errors, so a renamed section reports the real cause instead of 35 spurious ones.

TRLDOC007 is what makes on-demand recipe loading safe. The cookbook is the largest file in the doc set (~61K tokens), so agents are instructed to hold only its routing head resident and pull recipe bodies as tasks demand them. That trade is sound *only* while the index is a complete map: a recipe the index never links to is one an agent will never discover, because it never reads the body that would have revealed it. Before this gate existed, two recipes (6 and 28) were already unreachable. Keep index rows phrased as the reader's **task or failure mode**, not the recipe's title — routing happens on the row text alone.

## API reference delivery

Two separable concerns, deliberately kept apart in `Directory.Build.targets`:

| Concern | Mechanism | Consumers |
|---|---|---|
| **Ownership** — which package a reference describes | `<TrellisApiRefName>` | `audit-doc-freshness.ps1`, `audit-completeness`, TRLDOC004, TRLDOC011 |
| **Delivery** — which package ships the file to `.github/` | `<TrellisShipsApiReferenceSet>` on `Trellis.Core` | consumers' LLMs |

`Trellis.Core` ships the **complete first-party set**. Every package in this repository carries one version stamp from `version.json`, so scoping delivery per package bought nothing and cost two real failures:

- A reference for a `PackageReference` that was later dropped was never removed from `.github/`. The sync only ever copied, so the file stayed forever, frozen at whatever version last wrote it. That is worse than a missing doc: an absent reference makes an agent say "I don't know", while a stale one makes it write confident code against an API that no longer exists.
- An agent could never learn about a module the project had not already installed — backwards for a framework whose value is largely in its optional modules.

Because Core now ships references for packages the consumer may not have, **file presence no longer implies a package reference**. `trellis-start-here.md` says so explicitly and tells agents to confirm the reference in the `.csproj`; do not reintroduce wording that invites the opposite inference.

`Trellis.Analyzers` is the one first-party exception. It has no dependency on `Trellis.Core`, so it cannot rely on Core to deliver anything and ships its own reference and copy logic. The duplicated `trellis-api-analyzers.md` is harmless — both copies come from the same lockstep version, and the copy skips unchanged destinations.

### Packages published from other repositories

`Trellis.ServiceLevelIndicators`, `Trellis.ResourceNaming.Azure` and similar satellites version **independently**, so they must keep shipping their own reference. `Trellis.Core` deliberately does not embed a copy: it cannot know a satellite's current content, and a stale embedded copy would recreate exactly the failure described above.

A satellite ships two things:

1. Its reference markdown, packed to `trellis/`.
2. `build/Trellis.ApiReference.Payload.targets` from this repository, packed at both `build/<PackageId>.targets` and `buildTransitive/<PackageId>.targets`.

That payload file is three lines of item declaration. It deliberately does **not** carry the copy logic — the ~200-line directory walk lives in `Trellis.ApiReference.targets` and ships with `Trellis.Core`, which every satellite already depends on. Duplicating the walk into each repository would leave satellites silently running stale copy logic with nothing to detect the drift. Scenario 5 of `build/test-apireference-payload.ps1` proves the arrangement works end to end by packing a satellite-shaped package that ships no copy logic and asserting its reference still lands.



## TRLDOC005 — documented symbols must exist

TRLDOC005 and TRLDOC008 are **not** part of this script. Both are emitted by the `audit-completeness` tool, because they need to reflect over built assemblies rather than read Markdown, and they therefore run in the Build workflow after `dotnet build` rather than in the docs workflow. They check the doc↔API relationship in opposite directions, and both must hold: TRLDOC005 that every documented name is real, TRLDOC008 that every real name is documented.

It is the reverse of the completeness audit. Completeness asks "is every public API documented?" and so can only start from symbols that exist; it is structurally blind to a confidently-documented type that was renamed or never existed. TRLDOC005 asks the opposite question — "does every API-shaped name in the docs resolve to a real symbol?" — which is the check that catches, for example, a doc telling readers to derive from a long-deleted `IRepositoryBase`.

The rule scans backticked PascalCase identifiers and ignores things that are not API surface by construction: diagnostic IDs (`TRLS001`, `TRLSGEN102`), generic type parameters (`TSelf`, `TAggregate`), all-caps words (`WHERE`, `DELETE`), and `Xxx` placeholders. Anything left over must either resolve against a loaded assembly or be listed in `audit-completeness/doc-only-symbols.txt`.

**Every dotted segment is checked, not just the head.** A reference like `` `TrellisAspOptions.ErrorStatusCodeMap` `` must resolve on *both* names. Checking only the type would let a real type vouch for a member that no longer exists — and the member name is the part a reader actually types. The known-symbol set contains member names alongside type and namespace names, so a segment resolves if anything in the loaded assemblies declares it. That is deliberately loose: the gate answers "does this name exist anywhere?", not "does this member exist *on that type*". It catches deleted and misspelled members, not members attributed to the wrong owner.

One capture limitation worth knowing: the pattern requires the backticked span to end at the identifier, so `` `Options.MapError` `` is checked but `` `Options.MapError<TError>(statusCode)` `` is not matched at all — the trailing call parentheses end the span. Prefer the bare form in tables and prose when you want the name gated.

Add an entry to that allowlist when the name is genuinely doc-only — a caller-authored example type, a symbol that moved to another repository, an old name kept in a rename table, a generator-emitted member that only exists on consumer types, or **documented negative space** (a name the docs mention precisely to say Trellis does *not* provide it). Every entry needs to be a deliberate decision, so keep the category comments in that file accurate. The tool reports allowlist entries that no doc references any more so the list does not silently rot.

To run it locally:

```powershell
dotnet build Trellis.slnx -c Release
dotnet run -c Release --project docs/docfx_project/api_reference/audit-completeness/audit-completeness.csproj
```

It resolves symbols from the built Trellis assemblies (including internal types, since the docs legitimately explain internal machinery such as EF conventions and interceptors) plus the centrally managed NuGet dependencies listed in `Directory.Packages.props`.

## TRLDOC008 — every public API must be documented

TRLDOC008 is the other half of the same tool, and the direct counterpart to TRLDOC005: it walks each package's public types and members and fails the build when a symbol's simple name never appears in that package's API reference file.

This gate exists because the completeness numbers were printed for a long time without being enforced, and the process exit code came from TRLDOC005 alone. The result was a backlog of 8 undocumented types and 33 undocumented members accumulating across seven packages in a permanently green build — nobody was ignoring a warning, they simply never saw one. A report that nothing reads is not a control.

The bar is deliberately low: the symbol's name must appear *somewhere* in the owning package's doc. That is not a claim the symbol is well explained, only that an agent reading the reference can discover it exists and spell it correctly. The failure this prevents is specific — an LLM that cannot find a member in the reference does not conclude the member is absent, it invents a plausible signature.

Two consequences of the matching rule are worth knowing before chasing a report:

- Matching is per-package. A type documented in a *different* package's file still counts as a gap, because that is the file an agent will be pointed at. `IInboxDispatcher` was flagged for exactly this reason — documented in the inbox reference while living in the `Trellis.Mediator` assembly.
- Matching is substring-based on the simple name. Describing a member conceptually ("the last-modified timestamp", "the delay in seconds") does not satisfy it; the doc has to name `LastModified` and `DelaySeconds`. This is the intended behaviour, since a name a reader cannot type is a name they cannot use.

Static extension classes are a common source of hits, and the honest fix is usually not to name-drop the class but to give it its own `###` section. When `AddTrellisIdempotency` was documented under a generic `ServiceCollectionExtensions` heading rather than its real `IdempotencyServiceCollectionExtensions` owner, the gate was reporting a genuine accuracy defect, not a bookkeeping one.

## Anchor slug rule

TRLDOC003 builds its heading index from real Markdown headings outside CommonMark fenced code blocks (up to three leading literal spaces), then applies the DocFX/Markdig slug rule verified against the live Trellis site. If a file has an unterminated fenced code block, the script emits a warning because subsequent headings may have been skipped during indexing:

1. Strip backticks from heading text.
2. Lowercase the heading text.
3. For each character, keep letters, digits, `-`, and `_` as-is; convert whitespace to `-`; drop everything else without substituting a hyphen.
4. Do not collapse consecutive `-` characters.
5. Left-trim leading `-` characters.
6. For duplicate slugs in the same file, append `-1`, `-2`, and so on.

Examples:

| Heading | Slug |
|---|---|
| `## Recipe 6 — Conditional GET with EntityTagValue and byte-range with RangeOutcome` | `recipe-6--conditional-get-with-entitytagvalue-and-byte-range-with-rangeoutcome` |
| `## Recipe 7 — Authorization: IActorProvider + IAuthorize + resource-based auth` | `recipe-7--authorization-iactorprovider--iauthorize--resource-based-auth` |
| ``### `Aggregate<TId>` `` | `aggregatetid` |
| ``### `MaybeQueryableExtensions` `` | `maybequeryableextensions` |

## Allowlist entries

Bare cross-doc links such as `](trellis-api-core.md)` are rejected because they should point at a specific anchor. Prefer `](trellis-api-core.md#some-section)`. If a bare link is intentional, append this inline marker to that line:

```markdown
<!-- trellis-doc-lint: allow-bare-cross-doc-link -->
```

Broken anchors are rejected by TRLDOC003. If a broken anchor is deliberate, append this inline marker to that line:

```markdown
<!-- trellis-doc-lint: allow-broken-anchor -->
```

Filler table rows such as `| — | — | No public properties.` are never allowlisted; remove the row or document real public surface instead.

## TRLDOC009 — no stray carriage returns

No reference file may contain a carriage return that is not followed by a line feed.

The rule deliberately says nothing about CRLF versus LF. `.gitattributes` marks the repository `* text=auto`, so the blob stores LF and the working tree gets whatever the platform checks out — a bare LF is correct on a Linux runner. An earlier version of this gate required uniform CRLF and failed CI on every reference file for exactly that reason.

A lone CR is the platform-independent defect: git's newline normalisation does not touch it, so it survives in the blob and corrupts the same line on every checkout.

The rule exists because of a defect that reached review. A tool patched one table row of a CRLF working file with LF-terminated replacement text, leaving a lone CR before the row's opening pipe. Nothing looked wrong in an editor, the row still rendered in most viewers, and every other gate passed — a reviewer caught it by reading the raw diff. The failure mode is worse than cosmetic: a stray control character at the start of a Markdown table row can break the row out of its table, silently dropping documented API surface from the rendered page while the completeness gate still counts it as documented.

There is no allowlist marker for this rule. Rewrite the file without the stray byte instead; in PowerShell, preserve the BOM while doing so:

```powershell
$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($path, $content, $utf8Bom)
```