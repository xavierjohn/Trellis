---
name: recipe-11-analyzer-fixes
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Pairs each analyzer warning with a WRONG and a FIX example. Use when fixing a Trellis analyzer warning (TRLS###)."
---
# Recipe 11 — Anti-pattern → fix gallery (the analyzers in action)

The anti-pattern catalog is its own file, so that AI sessions and human readers can load it independently when debugging an analyzer warning. See **[`trellis-api-anti-patterns.md`](../trellis-api-anti-patterns.md)** <!-- trellis-doc-lint: allow-bare-cross-doc-link --> for each common analyzer trigger and its idiomatic Trellis fix (TRLS001, TRLS003, TRLS010, TRLS016, TRLS018, TRLS019).

If you are looking up a specific analyzer by ID, the standalone file is faster than scanning this cookbook. The cookbook recipes still link to the relevant sections of that file where they apply.

For a fresh GUID used as an error/violation reason code, follow [TRLS067's WRONG/FIX shape](../trellis-api-anti-patterns.md#trls067--fresh-guid-derived-reason-code): retain a stable category and move the incident identifier to metadata. Other dynamic application codes remain legal.

> **Enabling these analyzers.** Standalone analyzer rules are **opt-in**: they ship in a separate `Trellis.Analyzers` package that `Trellis.Core` does not pull in. Add `<PackageReference Include="Trellis.Analyzers" PrivateAssets="all" />` to every project that uses Trellis `Result`/`Maybe`/value objects to enable those analyzer-emitted diagnostics. The `TRLS###` prefix also includes source-generator diagnostics bundled with `Trellis.Core`, `Trellis.EntityFrameworkCore`, and `Trellis.Asp`; those do not require `Trellis.Analyzers`. See [trellis-api-analyzers.md](../trellis-api-analyzers.md#installation--the-analyzers-are-opt-in) for the emitter table, install snippet, and `.editorconfig` severity control.
