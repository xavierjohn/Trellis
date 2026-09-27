# Trellis.AgentContext

Optional .NET local tool for projects using AI coding assistants. Trellis packages carry
API references, but assistants may not discover them inside the NuGet cache. This tool
installs references matched to your restored packages under `.trellis/` and adds a small
`AGENTS.md` pointer so assistants can find them instead of guessing API signatures.
Trellis works without this tool; restore and build never edit your instructions.

## Installation

### At the Git root

Run from the **Git root**, using a relative path if the solution is in a subfolder:

```text
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentContext --version __TRELLIS_PACKAGE_VERSION__ --tool-manifest .config/dotnet-tools.json
dotnet restore <solution-or-project>
dotnet tool run trellis agent init <solution-or-project>
```

This creates `.trellis/` at the Git root, even for a nested solution.

### In a subfolder

Run from the subfolder that should own `.trellis/` (for example, `backend`):

```text
cd <subfolder>
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentContext --version __TRELLIS_PACKAGE_VERSION__ --tool-manifest .config/dotnet-tools.json
dotnet restore <solution-or-project>
dotnet tool run trellis agent init <solution-or-project> --scope .
```

Use project or solution paths relative to that subfolder. This creates
`<subfolder>/.trellis/` and its own local tool manifest; use `--scope .` for later
`sync`, `check`, and `remove` commands too. A nested solution path alone does not
select a nested context.

In either location, skip `dotnet new` if `.config/dotnet-tools.json` exists there.
If plain `dotnet new tool-manifest` created a root-level `dotnet-tools.json`, move
it into `.config/` instead of creating another manifest. The manifest must pin
`trellis` and have `"isRoot": true`.

## Quick Example

After a fresh clone with a committed tool manifest and context, run from the
directory that owns the manifest:

```text
dotnet tool restore
dotnet restore App.slnx
dotnet tool run trellis agent sync
dotnet tool run trellis agent check
```

For a subfolder context, append `--scope .` to `sync` and `check`.

`remove` works even when project assets or packages are unavailable. `--dry-run` previews changes and `--force` allows reviewed conflict resolution. `init --source-root PATH` selects additional linked-source directories within the selected context; `sync` and `check` reuse them. Do not edit affected files concurrently with a mutating command.

## Key Features

- Validates package guidance through fixed-path manifests and preserves package/version provenance.
- Deduplicates identical Trellis documents and namespaces unrelated nested guidance.
- Preserves customer instructions outside managed marker entries; detects modifications before writing.
- Keeps `check` read-only, and never reads or modifies legacy `.github/` documents.

## Documentation

- [Agent-context API and CLI reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-agent-context.html)
