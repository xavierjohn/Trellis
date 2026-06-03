# API reference lint

The API reference lint gate blocks documentation regressions in `docs/docfx_project/api_reference/*.md`.

Run it locally with:

```powershell
pwsh docs/lint-api-reference.ps1
```

The solution build runs the same script through `docs\Trellis.DocsLint.csproj`, so failures are emitted as MSBuild errors.

## Rules

- **TRLDOC001**: Bare cross-doc links such as `](trellis-api-core.md)` must point at a specific anchor, for example `](trellis-api-core.md#some-section)`.
- **TRLDOC002**: Filler table rows such as `| — | — | No public properties.` are not allowed.
- **TRLDOC003**: Anchored cross-doc links such as `](trellis-api-core.md#some-section)` and same-file anchors such as `](#some-section)` must resolve to an existing heading in the target API reference file.

## Anchor slug rule

TRLDOC003 uses the DocFX/Markdig slug rule verified against the live Trellis site:

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