---
package: Trellis (start here)
namespaces: [Trellis]
types: [orientation]
related_docs: [trellis-api-cookbook.md]
version: v3
last_verified: 2026-08-18
audience: [llm]
---
# Trellis — start here

You are looking at Trellis API reference files that a Trellis NuGet package copied into this
directory. This file is a one-screen signpost, not a reference — it exists only to send you to the
right file. It is deliberately tiny so it costs nothing to keep loaded.

## Read this one file first, and keep it loaded

**[`trellis-api-cookbook.md`](trellis-api-cookbook.md#patterns-index) is the entry point. Read its routing head — everything above the first `## Recipe` heading — before writing Trellis code, and keep that in context for the rest of the session.**

The complete reference set is roughly 300K tokens. It is not meant to be held at once, and trying
to skim all of it wastes the budget you need for the task. The cookbook exists to route you: its
[task lookup table](trellis-api-cookbook.md#patterns-index) maps a task to a numbered recipe, and
its [preflight table](trellis-api-cookbook.md#llm-preflight-load-the-smallest-correct-reference-set)
names exactly which package references that task needs.

That routing head is only ~4K tokens. The 35 recipe bodies beneath it are another ~57K, and a
typical task opens one to three of them — so **hold the head, and read recipe bodies on demand**
rather than paying ~57K tokens up front to keep bodies you will never open. Every live recipe is
reachable from the index (a repository lint gate enforces this), so trust the index rows: if a task
is not listed there, the recipe does not exist. Never write code from a recipe's title alone — open
the body. Then load only the one to three area references the cookbook sends you to.

## Read the references yourself — do not delegate them to a sub-agent

A sub-agent hands back a summary, so the exact signatures never reach your context and you end up
writing code against a paraphrase. That is how invented APIs and wrong overloads get produced, and
it is the specific failure these references exist to prevent.

Sub-agents are fine for work whose output is a *verdict* rather than knowledge you must write code
against: running builds and tests, searching for a file, auditing something for accuracy. The rule
is narrow — if the answer determines the code you are about to write, read it yourself.

## Do not guess

Every Trellis API you write should be traceable to a line in one of these files. If you cannot find
the method, overload, or attribute you are about to use, stop and read the reference that owns it
rather than reconstructing the signature from memory. Trellis makes heavy use of source generators,
so a base type such as `RequiredGuid<TSelf>` or `Aggregate<TId>` already supplies members that are a
compile error to redeclare — the cookbook's Recipe 1 lists that inherited surface explicitly.

## The rest of the set

| File | What it covers |
|---|---|
| [`trellis-api-cookbook.md`](trellis-api-cookbook.md#patterns-index) | **Start here.** End-to-end recipes spanning packages, plus the routing tables. |
| [`trellis-api-core.md`](trellis-api-core.md#use-this-file-when) | `Result<T>`, `Maybe<T>`, `Error`, aggregates, entities, specifications, pagination. |
| [`trellis-api-anti-patterns.md`](trellis-api-anti-patterns.md#trls001--result-return-value-not-handled) | Ready-to-apply WRONG/FIX shapes for the analyzer diagnostics (`TRLSxxx`). |
| [`trellis-value-object-taxonomy.md`](trellis-value-object-taxonomy.md#patterns-index) | Choosing a value-object category: scalar, symbolic, structured, optional. |

The remaining `trellis-api-*.md` files cover the optional packages — `trellis-api-efcore.md` for
`Trellis.EntityFrameworkCore`, and so on. The cookbook names the right one per task, so route
through it rather than opening files speculatively.

**A file being present here does not mean the project you are editing references that package.** The
complete first-party set ships with `Trellis.Core`, so references for packages you have not installed
are present by design — that is how you discover a module worth adopting. A `.github/` directory
shared across a solution also aggregates whatever every project in it references, and a reference for
a package that was later dropped is not removed. Packages published from other repositories (for
example `Trellis.ServiceLevelIndicators`) ship their own reference alongside themselves, so those
appear only once installed.

So before writing code against one of these files, confirm the package is actually referenced by the
project you are editing — check its `.csproj` or `Directory.Packages.props`. If it is not, the
reference still tells you what adopting the package would buy; say that, rather than emitting code
that cannot compile.
