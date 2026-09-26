# Trellis.AgentContext

Repository-pinned .NET local tool that explicitly installs versioned Trellis package guidance into `.trellis/` and adds a marker-delimited `AGENTS.md` pointer. It consumes already-restored NuGet assets through the shared read-only guidance reader.

## Installation

From the directory that owns your local tool manifest:

```text
dotnet new tool-manifest
dotnet tool install Trellis.AgentContext --version <resolved-Core-version>
dotnet restore App.slnx
dotnet tool run trellis agent init App.slnx
```

Ensure `.config/dotnet-tools.json` pins the `trellis` command and has `"isRoot": true`. An existing manifest does not need `dotnet new tool-manifest`. For a nested independent context, run from that directory with `--scope .`.

## Quick Example

After a fresh clone with a committed tool manifest and context:

```text
dotnet tool restore
dotnet restore App.slnx
dotnet tool run trellis agent sync
dotnet tool run trellis agent check
```

`remove` works even when project assets or packages are unavailable. `--dry-run` previews changes and `--force` allows reviewed conflict resolution. `init --source-root PATH` selects additional linked-source directories within the selected context; `sync` and `check` reuse them. Do not edit affected files concurrently with a mutating command.

## Key Features

- Validates package guidance through fixed-path manifests and preserves package/version provenance.
- Deduplicates identical Trellis documents and namespaces unrelated nested guidance.
- Preserves customer instructions outside managed marker entries; detects modifications before writing.
- Keeps `check` read-only, and never reads or modifies legacy `.github/` documents.

## Documentation

- [Agent-context API and CLI reference](https://xavierjohn.github.io/Trellis/api_reference/trellis-api-agent-context.html)
