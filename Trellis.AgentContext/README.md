# Trellis.AgentContext

Optional repository-pinned .NET tool for AI coding assistants. Trellis packages carry API
references, but assistants may not discover them inside the NuGet cache. This tool installs
references matched to the restored packages under `.trellis/` and adds a small pointer to
applicable `AGENTS.md` files, helping assistants use real APIs instead of guessing.
Trellis works without it; restore and build never edit your instructions. Package discovery
uses the read-only `Trellis.Guidance.Reader`, not project-authored MSBuild items.

## Installation

Pin the tool to the version of `Trellis.Core` resolved by the selected project or
solution. Choose where `.trellis/` should live before running the commands.

### At the Git root

Run from the **Git root**. If the solution is in a subfolder, pass its path relative
to the Git root:

```text
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentContext --version <resolved-Core-version> --tool-manifest .config/dotnet-tools.json
dotnet restore <solution-or-project>
dotnet tool run trellis agent init <solution-or-project>
```

This creates `.trellis/` at the Git root, even if the solution is in a subfolder.

### In a subfolder

Run from the subfolder that should own `.trellis/` (for example, `backend`), using
solution or project paths relative to that subfolder:

```text
cd <subfolder>
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentContext --version <resolved-Core-version> --tool-manifest .config/dotnet-tools.json
dotnet restore <solution-or-project>
dotnet tool run trellis agent init <solution-or-project> --scope .
```

This creates `<subfolder>/.trellis/`, with its own local tool manifest. Keep
`--scope .` on later `sync`, `check`, and `remove` commands from that subfolder.
Passing only a subfolder solution path from the Git root does **not** select a
subfolder context.

For either setup, skip `dotnet new` if `.config/dotnet-tools.json` already exists
in the chosen directory. If plain `dotnet new tool-manifest` created a
`dotnet-tools.json` alongside that directory instead, move it into `.config/`
rather than creating a second manifest. The tool requires a scoped manifest
advertising `trellis` with `"isRoot": true`. When Core is absent, pin a tool
that supports the contributor's guidance schema.

## Quick Example

After cloning a repository with an installed context and checked-in tool manifest,
run from the same directory used at installation and restore the tool **and**
project graph separately:

```text
dotnet tool restore
dotnet restore App.slnx
dotnet tool run trellis agent sync
dotnet tool run trellis agent check
```

For a subfolder context, append `--scope .` to `sync` and `check`.

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
