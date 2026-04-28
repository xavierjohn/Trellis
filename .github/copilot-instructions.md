# GitHub Copilot Instructions for Trellis

## Project overview

Trellis is an AI-native framework that helps create consistent, reliable enterprise software with railway-oriented programming, DDD primitives, ASP.NET integration, EF Core integration, and value objects.

These instructions are for repository workflow and contribution conventions only. They are not the source of truth for how to use Trellis APIs.

## API usage source of truth

Before writing or changing code that uses Trellis APIs, read the relevant files in `docs/docfx_project/api_reference/`.

Start with `docs/docfx_project/api_reference/trellis-api-cookbook.md`. Use its task lookup table to find the right recipe, then read the package reference files for exact signatures, overloads, namespaces, and examples. Do not infer Trellis API behavior from these Copilot instructions.

| When touching... | Read first |
|---|---|
| Result, Maybe, Error, ROP operations, aggregates, entities, specifications | `docs/docfx_project/api_reference/trellis-api-core.md` |
| Ready-to-use value objects and primitive attributes | `docs/docfx_project/api_reference/trellis-api-primitives.md` |
| ASP.NET Core response mapping, validation, ETags, Prefer, range handling | `docs/docfx_project/api_reference/trellis-api-asp.md` |
| EF Core integration | `docs/docfx_project/api_reference/trellis-api-efcore.md` |
| Authorization | `docs/docfx_project/api_reference/trellis-api-authorization.md` |
| FluentValidation integration | `docs/docfx_project/api_reference/trellis-api-fluentvalidation.md` |
| HttpClient extensions | `docs/docfx_project/api_reference/trellis-api-http.md` |
| Mediator pipeline behaviors | `docs/docfx_project/api_reference/trellis-api-mediator.md` |
| State machine integration | `docs/docfx_project/api_reference/trellis-api-statemachine.md` |
| Service defaults and composition root setup | `docs/docfx_project/api_reference/trellis-api-servicedefaults.md` |
| Testing helpers | `docs/docfx_project/api_reference/trellis-api-testing-reference.md` |
| ASP.NET Core integration-test helpers | `docs/docfx_project/api_reference/trellis-api-testing-aspnetcore.md` |
| Analyzer rules and diagnostic IDs | `docs/docfx_project/api_reference/trellis-api-analyzers.md` |

If an API reference contradicts these instructions, treat the API reference as authoritative for API usage.

## Code style

- Omit braces for single-line `if`/`return` statements when consistent with nearby code.
- Use `char` overloads for single-character operations, for example `value.Contains('-')`.
- Use collection expressions in tests where appropriate, for example `.Should().Equal([1, 2, 3])`.
- Use `ConfigureAwait(false)` in library source code; do not add it in test code.
- Prefer `ValueTask<T>` for high-frequency operations that may complete synchronously; prefer `Task<T>` for I/O-bound work.
- Avoid broad `try`/`catch` blocks and silent fallbacks. Surface or propagate errors using the existing repository patterns documented in the API references.
- Keep public APIs documented with XML comments.

## Test-driven development

Follow TDD when fixing bugs or adding features:

1. Add or update a failing test that proves the bug or specifies the new behavior.
2. Implement the smallest correct change.
3. Refactor while keeping tests green.

Do not skip the red step for bug fixes or new behavior.

## Test organization

Tests are organized by source area:

| Area | Source | Tests |
|---|---|---|
| Core ROP and DDD | `Trellis.Core/src/` | `Trellis.Core/tests/` |
| Value objects | `Trellis.Primitives/src/` | `Trellis.Primitives/tests/` |
| Authorization | `Trellis.Authorization/src/` | `Trellis.Authorization/tests/` |
| Mediator | `Trellis.Mediator/src/` | `Trellis.Mediator/tests/` |
| ASP.NET Core | `Trellis.Asp/src/` | `Trellis.Asp/tests/` |
| HTTP | `Trellis.Http/src/` | `Trellis.Http/tests/` |
| EF Core | `Trellis.EntityFrameworkCore/src/` | `Trellis.EntityFrameworkCore/tests/` |
| State machine | `Trellis.StateMachine/src/` | `Trellis.StateMachine/tests/` |
| Testing helpers | `Trellis.Testing*/src/` | `Trellis.Testing*/tests/` |

Async extension tests use this file naming convention:

| Pattern | File name |
|---|---|
| Async receiver and async delegates | `[Method]Tests.[Type].cs` |
| Async receiver and sync delegates | `[Method]Tests.[Type].Left.cs` |
| Sync receiver and async delegates | `[Method]Tests.[Type].Right.cs` |

Test method names should follow `[Method]_[Variant]_[Scenario]_[Expectation]`.

For T4-generated tuple overloads, test the 2-tuple case comprehensively and validate larger tuple arities with minimal representative tests. Do not chase 100% coverage on generated tuple code.

## Documentation standards

When adding or changing public API surface, update the relevant API reference file in `docs/docfx_project/api_reference/`. Update package `README.md`, `NUGET_README.md`, DocFX articles, and `docs/docfx_project/docfx.json` metadata when those artifacts are directly affected.

Keep framework usage guidance in the API reference and cookbook files, not in this Copilot instruction file.

DocFX artifact checklist for package or public API changes:

| Artifact | Location |
|---|---|
| DocFX metadata | `docs/docfx_project/docfx.json` |
| DocFX articles | `docs/docfx_project/articles/` |
| Article TOC | `docs/docfx_project/articles/toc.yml` |
| Package README | `Trellis.{Package}/README.md` |
| NuGet README | `Trellis.{Package}/NUGET_README.md` |
| AI API reference | `docs/docfx_project/api_reference/trellis-api-{library}.md` |

## File encoding and PowerShell

All repository files must be UTF-8 with BOM.

When using PowerShell for file writes, preserve the BOM:

```powershell
$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($path, $content, $utf8Bom)
```

Avoid `Set-Content` for repository files because it can change encoding.

## Validation before handoff

Before considering code work complete:

1. Run `dotnet build` from the repository root.
2. Run `dotnet test` from the repository root.
3. Confirm public API changes are reflected in the API references and related package docs.
4. For changed code, use a code-review agent with `model: gpt-5.4` before committing.

Documentation-only changes do not require a build or test run unless they affect generated docs, examples that are compiled, or documented public API behavior.

## Git and PR rules

- Do not commit without explicit user approval.
- Do not push branches.
- Do not create or merge pull requests.
- Do not amend commits, rebase pushed history, or force-push unless the user explicitly asks and confirms the history is safe to rewrite.
- If asked for a PR summary, output this copy-paste-ready format:

````markdown
**Title:** <short PR title>

```markdown
<full PR body>
```
````

## Pre-commit checklist

Before committing any changes after explicit approval:

1. Confirm required validation has passed.
2. Confirm the diff contains only intended changes.
3. Run a code-review agent with `model: gpt-5.4` for changed code and address substantive findings.
4. Present the final summary to the user.
