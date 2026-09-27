---
package: Trellis.AgentContext
namespaces: [Trellis.AgentContext]
types: [command-line tool]
related_docs: [trellis-start-here.md]
version: v1
audience: [llm]
---
# Trellis.AgentContext command reference

**Package:** `Trellis.AgentContext` (local .NET tool). The tool installs versioned package
guidance into a consumer repository only when explicitly invoked. Trellis packages include
API references, but an AI coding assistant may not discover them in the NuGet cache. The
tool copies references for the restored packages into `.trellis/` and adds an `AGENTS.md`
pointer so assistants can find the relevant APIs. This is optional and does not change
application behavior; normal restore and build do not install context or edit instruction
files.

## Public command entry points

`Program.Main(string[] args)` is the tool entry point.
`AgentContextCommand.Run(string[] args, TextWriter output,
Func<IEnumerable<string>, GuidanceDiscovery> discover)` performs the lifecycle operation;
the executable passes the shared read-only `GuidanceReader.Discover` adapter.

## Setup

### At the Git root

Run from the **Git root**, passing a relative solution or project path if it is
nested:

```text
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentContext --version <matching-Trellis.Core-version> --tool-manifest .config/dotnet-tools.json
dotnet restore <solution-or-project>
dotnet tool run trellis agent init <solution-or-project>
```

This creates `.trellis/` at the Git root, not beside a nested solution.

### In a subfolder

Run from the subfolder that should own `.trellis/`, using solution or project
paths relative to it:

```text
cd <subfolder>
dotnet new tool-manifest --output .config
dotnet tool install Trellis.AgentContext --version <matching-Trellis.Core-version> --tool-manifest .config/dotnet-tools.json
dotnet restore <solution-or-project>
dotnet tool run trellis agent init <solution-or-project> --scope .
```

This creates a subfolder `.config/dotnet-tools.json` and `.trellis/`. Pass
`--scope .` from this directory to later `sync`, `check`, and `remove` commands.
The working directory selects the tool version; `--scope` selects the context
root. Passing a nested project path from the Git root does neither.

For either setup, skip `dotnet new` if `.config/dotnet-tools.json` already
exists at the selected scope. Plain `dotnet new tool-manifest` can create a
`dotnet-tools.json` alongside `.config/`; move that existing manifest into
`.config/` rather than creating another one. The agent-context command
requires `.config/dotnet-tools.json` with a pinned `trellis.agentcontext`
entry advertising `trellis` and `"isRoot": true`.

### After cloning

Run from the directory that owns the committed tool manifest and context:

```text
dotnet tool restore
dotnet restore <recorded-solution-or-project>
dotnet tool run trellis agent sync
dotnet tool run trellis agent check
```

For a subfolder context, append `--scope .` to `sync` and `check`.
The two restores are different: restoring the tool does not create a project's
`project.assets.json`. Run project restore separately before `init`, `sync`, or `check`.
`remove` can work without project assets if the pinned tool is already installed.

## Commands

| Command | Effect |
|---|---|
| `agent init <solution-or-project>` | Reads already-restored NuGet assets and validated package guidance manifests, generates `.trellis/`, and adds a narrow pointer to applicable `AGENTS.md` files. |
| `agent sync` | Updates owned context using the entry points recorded at initialization and removes stale owned references. |
| `agent check` | Reports missing, modified, or stale context without writing files; suitable for CI. |
| `agent remove` | Removes only the selected context's recorded files and instruction entries, without depending on a usable package cache or project assets. |

`init --source-root PATH` may be repeated for linked source directories outside the selected
project directories but still inside the selected context and Git working tree. The selected
source roots are recorded for subsequent `sync` and `check`; generated directories, `.github/`,
symlinks, and nested Git working trees are excluded. `init` and `sync` accept `--restore` as
an explicit opt-in to updating project assets, NuGet caches, and configured lock files; the
default requires an already-restored graph. `--force` enables reviewed adoption or
replacement of conflicts for mutating commands. `check` remains read-only and accepts
neither `--force` nor `--restore`.
If an owned marker block disappears from an existing instruction file, `sync` and
`remove` require `--force` rather than silently reclaiming it; deleting an entire
descendant `AGENTS.md` while removing that instruction boundary may still be pruned
without force.

The already-restored graph must match evaluated target frameworks, runtime identifier,
package references and per-framework project references. A new or removed project
reference, or a runtime identifier absent from the assets restore specification,
requires `dotnet restore` before installing or checking context.
Source discovery skips evaluated generated, intermediate, and output directories as
well as conventional `bin/` and `obj/`; an explicit source root inside one is rejected.

The tool-owned `.trellis/README.md` is the discovery entry point. With Core installed,
it routes to the packaged cookbook; without Core it links to the installed packages'
own entry points. The repository manifest records which package and version supplied
each document. A file's presence alone does not prove its package is referenced by
the project you are editing: Core intentionally carries the complete first-party
reference set.

`--dry-run` previews a mutation without writes. Existing unowned destinations,
directories at file destinations, files at destination parent paths,
changed owned files, malformed managed markers, incompatible package cohorts,
unsupported guidance schemas, and missing or stale restored assets stop installation
instead of silently adopting or overwriting files. Review every conflict before any
force/adoption operation. Do not run external editors or formatters against affected
files during `init`, `sync`, or `remove`: individual writes are atomic, but an external
save after the final snapshot check can still race a replacement. The tool never
inspects or modifies existing `.github/` instructions or old reference files.

If `init` reports an older Trellis package without a guidance manifest or a legacy
copy target, it does not create `.trellis/`; upgrade that package to a guidance-capable
release or select a narrower compatible project. Mixed versions of any package across
the selected graph also block installation: align those versions or initialize
independent scopes. `--force` does not bypass package compatibility checks.
