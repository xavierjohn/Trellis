# Trellis.AgentContext

Repository-pinned .NET tool that installs versioned package guidance under `.trellis/` and links it from applicable `AGENTS.md` files. Package discovery uses the read-only `Trellis.Guidance.Reader`, not project-authored MSBuild items.

## Installation

From the directory owning the scope's `.config/dotnet-tools.json`, create a local tool manifest if necessary and pin the version matching its resolved `Trellis.Core`:

```text
dotnet new tool-manifest
dotnet tool install Trellis.AgentContext --version <resolved-Core-version>
dotnet restore App.slnx
dotnet tool run trellis agent init App.slnx
```

The scoped manifest must declare `trellis` and set `"isRoot": true`. When Core is absent, pin a tool that supports the contributor's guidance schema. For an independent nested scope, run every command from that directory and add `--scope .`; otherwise the context root is the Git root.

## Quick Example

After cloning a repository with an installed context and checked-in tool manifest, restore the tool **and** project graph separately:

```text
dotnet tool restore
dotnet restore App.slnx
dotnet tool run trellis agent sync
dotnet tool run trellis agent check
```

Run `dotnet tool run trellis agent remove` to remove recorded ownership without a usable package graph. `--dry-run` previews changes, `--force` enables reviewed adoption or replacement of conflicts, and `init`/`sync --restore` explicitly permit restore side effects in `obj/`, NuGet's cache, and configured lock files. Use `init --source-root PATH` (repeatable) for linked source directories outside selected project directories but inside the selected context and Git working tree. Recorded roots continue to apply during `sync` and `check`.

## Key Features

- Generates a small `.trellis/README.md` index from package-declared entry points and a manifest recording package, framework, document, and NuGet content-hash provenance.
- Projects Trellis's flat reference filenames, deduplicates identical canonical text with all contributor identities, and namespaces unrelated package documents without breaking nested links.
- Merges keyed instruction entries into existing `AGENTS.md` files while retaining customer bytes outside the marker, original encoding/BOM, and line endings.
- Checks owned-file hashes and complete destination conflicts, and probes atomic replacement in each affected existing destination parent before mutation; `check` and dry-run never restore or take the worktree writer lock.
- Never reads or writes legacy `.github/` documents. Update customer-authored legacy links manually.

Avoid external edits to affected files during mutation: an edit after the last snapshot check can still be lost. A partial multi-file update has no recovery journal; inspect conflicts and preserve customer changes before selective restoration or reviewed `--force`. Source roots default to selected project directories. Current read-only MSBuild evaluation does not fully reproduce NuGet's restore specification for conditional multi-target imports or lock-file changes.

## Documentation

- [Agent-context API and CLI reference](../docs/docfx_project/api_reference/trellis-api-agent-context.md)

## Development

Run the targeted tests from the repository root:

```powershell
dotnet test Trellis.AgentContext\tests\Trellis.AgentContext.Tests.csproj -c Debug
```

Normal restore and build never install agent context. The shared reader is a project reference packed into the tool, not a separate user-installed command.
