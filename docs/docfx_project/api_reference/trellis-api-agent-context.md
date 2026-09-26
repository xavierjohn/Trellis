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
guidance into a consumer repository only when explicitly invoked. Normal restore and build
do not install context or edit instruction files.

## Public command entry points

`Program.Main(string[] args)` is the tool entry point.
`AgentContextCommand.Run(string[] args, TextWriter output,
Func<IEnumerable<string>, GuidanceDiscovery> discover)` performs the lifecycle operation;
the executable passes the shared read-only `GuidanceReader.Discover` adapter.

## Setup

Run from the directory governed by the local tool manifest. For a first installation:

```text
dotnet new tool-manifest
dotnet tool install Trellis.AgentContext --version <matching-Trellis.Core-version>
dotnet restore <solution-or-project>
dotnet tool run trellis agent init <solution-or-project>
```

If a manifest exists already, add the pinned tool to it instead of creating another.
After cloning a repository with a committed tool manifest and context:

```text
dotnet tool restore
dotnet restore <recorded-solution-or-project>
dotnet tool run trellis agent sync
dotnet tool run trellis agent check
```

The two restores are different: restoring the tool does not create a project's
`project.assets.json`. Run project restore separately before `init`, `sync`, or `check`.
`remove` can work without project assets if the pinned tool is already installed.

For an independent nested context, use its own `.config/dotnet-tools.json` with an
explicit pinned `trellis` entry and `"isRoot": true`. Run all the above commands
from that scope's directory and pass `--scope .` to `init`, `sync`, `check`, and
`remove`. The working directory selects the tool version; `--scope` selects the
context root. Passing a nested project path from the repository root does neither.

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
changed owned files, malformed managed markers, incompatible package cohorts,
unsupported guidance schemas, and missing or stale restored assets stop installation
instead of silently adopting or overwriting files. Review every conflict before any
force/adoption operation. Do not run external editors or formatters against affected
files during `init`, `sync`, or `remove`: individual writes are atomic, but an external
save after the final snapshot check can still race a replacement. The tool never
inspects or modifies existing `.github/` instructions or old reference files.
