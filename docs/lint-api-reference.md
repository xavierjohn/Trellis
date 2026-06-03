# API reference lint

The API reference lint gate blocks documentation regressions in `docs/docfx_project/api_reference/*.md`.

Run it locally with:

```powershell
pwsh docs/lint-api-reference.ps1
```

The solution build runs the same script through `docs\Trellis.DocsLint.csproj`, so failures are emitted as MSBuild errors.

## Allowlist entries

Bare cross-doc links such as `](trellis-api-core.md)` are rejected because they should point at a specific anchor. Prefer `](trellis-api-core.md#some-section)`. If a bare link is intentional, append this inline marker to that line:

```markdown
<!-- trellis-doc-lint: allow-bare-cross-doc-link -->
```

Filler table rows such as `| — | — | No public properties.` are never allowlisted; remove the row or document real public surface instead.