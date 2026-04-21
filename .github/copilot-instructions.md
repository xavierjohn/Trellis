# GitHub Copilot Instructions for Trellis

## Project Overview

Functional programming library for .NET 10 implementing Railway Oriented Programming (ROP), Domain-Driven Design (DDD) primitives, and value objects.

## Before Writing Code That Uses Trellis APIs

Always read the relevant API reference files in `docs/api_reference/` **before** writing or generating code that uses Trellis types:

| When using... | Read first |
|--------------|------------|
| Result, Maybe, Bind, Map, Tap, Ensure, Combine, Check | `docs/api_reference/trellis-api-core.md` |
| Aggregate, Entity, ValueObject, Specification | `docs/api_reference/trellis-api-ddd.md` |
| RequiredString, RequiredGuid, Money, EmailAddress, etc. | `docs/api_reference/trellis-api-primitives.md` |
| ToActionResult, ToHttpResult, ETag, Prefer, WriteOutcome | `docs/api_reference/trellis-api-asp.md` |
| EF Core integration | `docs/api_reference/trellis-api-efcore.md` |
| Actor, IActorProvider, IAuthorize | `docs/api_reference/trellis-api-authorization.md` |
| FluentValidation integration | `docs/api_reference/trellis-api-fluentvalidation.md` |
| HttpClient extensions | `docs/api_reference/trellis-api-http.md` |
| Mediator pipeline behaviors | `docs/api_reference/trellis-api-mediator.md` |
| State machine integration | `docs/api_reference/trellis-api-stateless.md` |
| Testing helpers | `docs/api_reference/trellis-api-testing-reference.md` |
| Analyzer rules (TRLS001-TRLS022) | `docs/api_reference/trellis-api-analyzers.md` |

These files document the exact method signatures, overloads, and usage patterns. Do not assume APIs based on naming conventions — read the reference first.

## Naming & Namespace Strategy

This project uses `Trellis` as the package and namespace prefix. Namespace matches the **nature of the type**, not the package boundary.

| Context | Correct |
|---------|---------|
| Using statements | `using Trellis;` |
| Assembly names | `Trellis.*` |
| Core types namespace | `Trellis` (not one namespace per package) |

"Trellis" is used everywhere — packages, namespaces, documentation, and the GitHub repository name.

### `Trellis` — Core Structural Building Blocks

The single namespace for all structural types. These have zero dependencies beyond the .NET runtime. One `using Trellis;` makes the entire structural toolkit available.

| From Package | Types in `Trellis` Namespace |
|-------------|------------------------------|
| Trellis.Core | `Result<T>`, `Maybe<T>`, `Error` |
| Trellis.DomainDrivenDesign | `Aggregate<T>`, `Entity<T>`, `ValueObject`, `Specification<T>` |
| Trellis.Primitives | `RequiredString`, `RequiredGuid`, `RequiredInt`, `RequiredDecimal`, `RequiredEnum` base classes |

For complete API details, see `docs/api_reference/trellis-api-core.md`, `trellis-api-ddd.md`, `trellis-api-primitives.md`.

### `Trellis.Primitives` — Opinionated Ready-to-Use Value Objects

Separate namespace for concrete value objects that encode specific validation rules (e.g., EmailAddress, Money, FirstName). Developers may want to replace these with their own validation. Separate namespace prevents collision if the consumer defines their own type with the same name.

```csharp
using Trellis;
using Trellis.Primitives;  // Only if using built-in VOs like EmailAddress, Money

namespace OrderManagement.Domain.Orders;
```

To replace a built-in VO, just don't import `Trellis.Primitives` — no collision:

```csharp
using Trellis;
// No Trellis.Primitives — writing my own EmailAddress

namespace OrderManagement.Domain.Common;

public sealed class EmailAddress : RequiredString<EmailAddress>
{
    // My own validation rules
}
```

### Integration Namespaces — One Per Package

Each integration package gets its own namespace because it pulls in a third-party or framework dependency. Used in specific layers, not everywhere.

| Namespace | Used In | Purpose |
|-----------|---------|---------|
| `Trellis.Authorization` | Domain/Application layer | Actor-based authorization |
| `Trellis.Asp.Authorization` | API layer only | ASP.NET actor providers (Claims, Entra, Development) |
| `Trellis.Asp` | API layer only | Result-to-HTTP response mapping |
| `Trellis.Http` | ACL layer only | HttpClient → Result extensions |
| `Trellis.Stateless` | Domain layer (when needed) | Stateless state machine integration |
| `Trellis.FluentValidation` | Domain layer (when needed) | FluentValidation integration |
| `Trellis.Testing` | Test projects only | FluentAssertions extensions for Result/Maybe |
| `Trellis.Mediator` | Application layer (CQRS only) | Mediator pipeline behaviors |
| `Trellis.EntityFrameworkCore` | ACL layer only | EF Core integration |

For the complete API surface of each namespace, read the corresponding `docs/api_reference/trellis-api-*.md` file.

### Namespace Placement Rule

If a type has **zero dependencies** beyond the .NET runtime → `Trellis` namespace.
If it pulls in a **third-party or framework dependency** → its own namespace matching the package name.

### Do NOT

- Do NOT create one namespace per package (e.g., `Trellis.RailwayOrientedProgramming`). Core types share `Trellis`.
- Do NOT put `ToMinimalApiResult` or `ToActionResult` in the `Trellis` namespace. They depend on ASP.NET Core and belong in `Trellis.Asp`.
- Do NOT put ready-to-use value objects like `EmailAddress` in the `Trellis` namespace. They belong in `Trellis.Primitives` to avoid collision.

## Value Object and ROP API Usage

For value object creation patterns (`TryCreate`/`Create`), async ROP chain patterns (`BindAsync`, `MapAsync`, `TapAsync`, etc.), and all extension method signatures and overloads, read the API reference files:

- `docs/api_reference/trellis-api-core.md` — Result/Maybe operations, async patterns, mixing sync and async in chains
- `docs/api_reference/trellis-api-primitives.md` — Value object creation, `[StringLength]`, culture-aware parsing
- `docs/api_reference/trellis-api-ddd.md` — Aggregate, Entity, Specification patterns

**Do not assume API signatures.** The API references document the exact overloads available (including sync-on-async variants).

## Value Object Category Review

Before adding or approving a new value-like type, classify it first:

| Category | Identity Shape | Expected Contract |
|---------|----------------|-------------------|
| Scalar value object | One primitive value | Public `Value`, `TryCreate`/`Create`, converter-based JSON/EF |
| Symbolic value object | One symbolic member from a finite set | One canonical symbolic identity, stable wire/storage contract |
| Structured value object | Multiple meaningful components | Explicit JSON shape, explicit persistence strategy, no fake scalar `Value` |
| Optionality wrapper | Presence/absence around another type | Preserve the wrapped type's model; keep storage details hidden |

Review checklist for new value-like types:

1. Does it clearly belong to exactly one category?
2. Is there exactly one canonical semantic identity?
3. Does the creation API align with that identity?
4. Would renaming a field or property accidentally change wire or storage contracts?
5. Can JSON, ASP.NET, analyzers, and EF handle it without type-specific hacks?
6. Are any public APIs exposing infrastructure details rather than domain meaning?

Working rule: do not force structured value objects like `Money` through the scalar pipeline, and do not let symbolic or optional types inherit scalar assumptions by accident.

## Diagnostic ID Conventions

Analyzers (`Trellis.Analyzers`) and source generators use **separate ID prefixes** to avoid collisions:

| Prefix | Owner | Range | Example |
|--------|-------|-------|---------|
| `TRLS` | Trellis.Analyzers | `TRLS001`–`TRLS999` | `TRLS007` — Use `Create()` instead of `TryCreate().Value` |
| `TRLSGEN` | Primitives source generator | `TRLSGEN001`–`TRLSGEN099` | `TRLSGEN002` — `MinimumLength` exceeds `MaximumLength` |
| `TRLSGEN` | EF Core source generator | `TRLSGEN100`–`TRLSGEN199` | `TRLSGEN100` — `Maybe<T>` property should be partial |

**Do NOT** reuse a `TRLS` ID in a source generator or vice versa. Do NOT overlap generator ranges.

## Code Style

### General Rules

- Omit braces for single-line `if`/`return` statements
- Use `char` overloads for single-character `Contains()` (CA1847): `value.Contains('-')` not `value.Contains("-")`
- Use collection expressions for FluentAssertions: `.Should().Equal([1, 2, 3])`
- Use `ConfigureAwait(false)` in library code (source files), never in test code
- Prefer `ValueTask<T>` for high-frequency, potentially synchronous operations; `Task<T>` for I/O-bound
- Avoid allocations in hot paths; consider `readonly struct` for value types

### Avoid Task/ValueTask Ambiguities

When both `Task<T>` and `ValueTask<T>` overloads exist, use explicit constructors:

```csharp
// ❌ Ambiguous
.EnsureAsync(v => Task.FromResult(v > 0), error)

// ✅ Explicit
.EnsureAsync(v => new ValueTask<bool>(v > 0), error)
```

## Railway Oriented Programming (ROP)

For `Result<T>`, `Maybe<T>`, `Error`, and all ROP extension methods (`Bind`, `Map`, `Tap`, `Ensure`, `Combine`, `Check`, `Match`, `RecoverOnFailure`, `ParallelAsync`, etc.), see `docs/api_reference/trellis-api-core.md`.

For `Maybe<T>` usage in ASP.NET DTOs and EF Core, see `docs/api_reference/trellis-api-asp.md` and `docs/api_reference/trellis-api-efcore.md`.

### Error API Discipline (V2 Closed ADT)

`Error` is a **closed discriminated union**: each HTTP-aligned case (`NotFound`, `Conflict`, `UnprocessableContent`, `Forbidden`, `InternalServerError`, …) is a `sealed record` nested inside the `Error` base record. The base record has a `private` constructor — only the cases declared in `Error.cs` may inherit. See `docs/adr/ADR-001-result-api-surface.md` for design rationale.

**Construct errors with `new Error.X(...)`. There are no static factory helpers.**

```csharp
// ✅ Direct construction with typed payload
new Error.NotFound(new ResourceRef("User", id.ToString())) { Detail = "User not found" }
new Error.Forbidden("only-creator-can-edit") { Detail = "Only the creator can edit." }
new Error.Conflict("etag_mismatch") { Detail = "ETag does not match current version." }
new Error.UnprocessableContent(EquatableArray.Create(
    new FieldViolation(InputPointer.ForProperty("dueDate"), "due_date_in_past")
    { Detail = "Due date must be in the future." }))
new Error.InternalServerError(faultId: Guid.NewGuid().ToString("N")) { Detail = "Persisted to log; see faultId." }

// ❌ Old factory style (removed) — do not write
Error.NotFound("User not found")
Error.Validation("Due date must be in the future.", "dueDate")
Error.Forbidden("Only the creator can edit.")
```

Key facts:
- `Detail` and `Cause` live on the base record as `init`-only properties; set them via object-initializer syntax.
- `Cause` chains are validated acyclic at `init` time. Never assign a live `System.Exception` — wrap context as a child `Error`.
- Equality compares discriminator + `Detail` + positional payload. `Cause` is intentionally excluded (mirrors `System.Exception`).
- `switch` over an `Error` reference is exhaustive at the language level — no default branch needed.
- `UnprocessableContent` carries both `Fields` (`EquatableArray<FieldViolation>`) and `Rules` (`EquatableArray<RuleViolation>`); either may be empty.
- The ASP boundary populates `ProblemDetails.Extensions["code"]`, `["kind"]`, `["faultId"]` (for `InternalServerError`), and `["rules"]` (when present). On 5xx responses `Detail` is redacted; the `faultId` extension preserves the link to server-side logs.

### Repository and Unit of Work Pattern

**Critical Rule:** Repositories stage changes. The pipeline commits. Handlers never call `SaveChanges`.

- `RepositoryBase` provides staging methods (`Add`, `Remove`, `RemoveByIdAsync`) and read methods (`FindByIdAsync`, `QueryAsync`, `ExistsAsync`, `CountAsync`). None of these call `SaveChanges`.
- `TransactionalCommandBehavior` is a pipeline behavior that auto-commits after a successful command handler. Queries are skipped (no overhead).
- `IUnitOfWork.CommitAsync()` is the single commit point. In the standard pipeline it's called automatically. Inject `IUnitOfWork` directly only for non-pipeline scenarios (background jobs, tests).
- Register with `services.AddTrellisUnitOfWork<AppDbContext>()` in the ACL layer.

For the full API surface, see `docs/api_reference/trellis-api-efcore.md`.

### Testing Philosophy

1. **Railway track behavior**: Once on failure track, stay there
2. **Early exit**: Don't execute functions if already failed
3. **Value preservation**: Original values preserved through transformations
4. **Error propagation**: Errors flow through pipeline unchanged

### Test-Driven Development (TDD)

Follow TDD when fixing bugs or adding new features:

1. **RED** — Write a failing test that proves the bug exists or specifies the new behavior
2. **GREEN** — Write the minimum code to make the test pass
3. **REFACTOR** — Clean up without changing behavior; all tests must stay green

Do NOT skip the RED step. A fix without a failing test is untested by definition.

### Pre-Submission Checklist

Before considering work complete, verify:

1. **Build succeeds** — `dotnet build` with zero errors and zero warnings
2. **All tests pass** — `dotnet test` with zero failures
3. **Documentation updated:**
   - `docs/api_reference/trellis-api-*.md` — if any public API was added or changed (per-library files: `trellis-api-core.md`, `trellis-api-ddd.md`, `trellis-api-primitives.md`, etc.)
   - `docs/api_reference/trellis-api-testing-reference.md` — if test helpers were added or changed
   - Package `README.md` — if the package's public surface changed
   - Docfx articles in `docs/docfx_project/articles/` — if relevant articles exist for the feature area
4. **PR summary prepared** — when asked for a PR summary, output it in a copy-paste-ready format:
   - First line: `**Title:** <short PR title>` — a concise one-liner suitable for the GitHub PR title field
   - Then a blank line followed by the PR body wrapped in a markdown code block:
     ````
     ```markdown
     <full PR body in GitHub-flavored Markdown>
     ```
     ````
   - The body should use headings, bullet lists, tables, and code blocks as appropriate
   - This format lets the user copy the title directly and paste the body into the GitHub PR description field
5. **Do NOT commit without explicit approval** — stage changes and present the diff for review
6. **Do NOT push branches** — the repository owner will push when ready
7. **Do NOT create or merge pull requests** — present changes locally for review
8. **Do NOT rewrite pushed history** — never use `git commit --amend`, `git rebase`, or `git push --force` on commits that have been pushed. Always create new commits for fixes.

## Test Organization

### Async Extension File Naming

Tests are organized by which parts are async:

- **Left** = input/source (`Task<Result<T>>`, `ValueTask<Result<T>>`)
- **Right** = predicates/functions passed as parameters

| Pattern | File Name | Input | Predicates |
|---------|-----------|-------|------------|
| Both async | `[Method]Tests.[Type].cs` | async | async |
| Left only | `[Method]Tests.[Type].Left.cs` | async | sync |
| Right only | `[Method]Tests.[Type].Right.cs` | sync | async |

Applies to: Ensure, Bind, Map, Tap, Match, Combine, and all other async extensions.

```csharp
// Both async → EnsureTests.ValueTask.cs
await ValueTask.FromResult(Result.Ok("test"))
    .EnsureAsync(v => ValueTask.FromResult(v.Length > 0), new Error.BadRequest("empty"));

// Left only → EnsureTests.ValueTask.Left.cs
await ValueTask.FromResult(Result.Ok("test"))
    .EnsureAsync(v => v.Length > 0, new Error.BadRequest("empty"));

// Right only → EnsureTests.ValueTask.Right.cs
await Result.Ok("test")
    .EnsureAsync(v => ValueTask.FromResult(v.Length > 0), new Error.BadRequest("empty"));
```

**Quick decision tree:** Is the input async? → Left. Are predicates async? → Right. Both? → Base (no suffix).

### Test Class Structure

- **One variant per file** — don't mix Left/Right/Both in the same file
- Organize with `#region` blocks by overload variant
- Place helper types (records, classes) at the bottom

```csharp
/// <summary>
/// Tests for Ensure.ValueTask.cs where BOTH input and predicates are async.
/// </summary>
public class Ensure_ValueTask_Tests
{
    #region EnsureAsync with ValueTask<bool> predicate and static Error
    // Tests...
    #endregion

    #region Edge Cases and Integration Tests
    // Tests...
    #endregion

    private record TestData(string Name, int Value);
}
```

### Test Method Naming

Format: `[Method]_[Variant]_[Scenario]_[Expectation]`

Example: `EnsureAsync_ValueTask_Bool_StaticError_SuccessResult_PredicateTrue_ReturnsSuccess`

### Required Test Coverage

- Success path + valid predicate → returns success
- Success path + failing predicate → returns failure
- Failure path → predicate not invoked, original failure returned
- Error factories (sync and async where applicable)
- Result-returning predicates (where applicable)
- Edge cases: nullable types, complex types, empty/null values
- Chained operations, early exit verification, exception propagation

```csharp
// ✅ Always verify early exit
var predicateInvoked = false;
var result = await Result.Fail<int>(error)
    .EnsureAsync(v => { predicateInvoked = true; return v > 0; }, error);

predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
```

## File Location Guidelines

| Area | Source | Tests |
|------|--------|-------|
| Core ROP | `Trellis.Core/src/Result/Extensions/` | `Trellis.Core/tests/Results/Extensions/` |
| Value Objects | `Trellis.Primitives/src/` | `Trellis.Primitives/tests/` |
| DDD | `Trellis.DomainDrivenDesign/src/` | `Trellis.DomainDrivenDesign/tests/` |
| Authorization | `Trellis.Authorization/src/` | `Trellis.Authorization/tests/` |
| Mediator | `Trellis.Mediator/src/` | `Trellis.Mediator/tests/` |
| ASP.NET | `Trellis.Asp/src/` | `Trellis.Asp/tests/` |
| HTTP | `Trellis.Http/src/` | `Trellis.Http/tests/` |
| EF Core | `Trellis.EntityFrameworkCore/src/` | `Trellis.EntityFrameworkCore/tests/` |

## Documentation Standards

- All public APIs must have XML doc comments (`<summary>`, `<param>`, `<returns>`)
- Test classes should have `<summary>` explaining what source file and async variant they cover

### DocFX Checklist

When adding or modifying a package, verify these documentation artifacts:

| Artifact | Location | Action |
|----------|----------|--------|
| `docfx.json` metadata | `docs/docfx_project/docfx.json` | Add the new project's `.csproj` to the `metadata[0].src[0].files` array |
| DocFX article | `docs/docfx_project/articles/` | Create or update the relevant `integration-*.md` article |
| Article TOC | `docs/docfx_project/articles/toc.yml` | Add entry under the appropriate section (e.g., Integration Guides) |
| `NUGET_README.md` | `Trellis.{Package}/NUGET_README.md` | Create or update — this is the NuGet.org package description |
| `README.md` | `Trellis.{Package}/README.md` | Create or update — this is the GitHub-facing documentation |
| `trellis-api-*.md` | `docs/api_reference/trellis-api-{library}.md` | Update the per-library AI API reference file (e.g., `trellis-api-core.md`, `trellis-api-efcore.md`) with any new or changed public types, methods, or extension methods — these documents are consumed by AI coding assistants |

```csharp
/// <summary>
/// Returns a new failure result if the predicate is false.
/// </summary>
/// <param name="result">The source result.</param>
/// <param name="predicate">The predicate to evaluate.</param>
/// <param name="error">The error to return if the predicate fails.</param>
/// <returns>The original result if successful and predicate passes; otherwise a failure.</returns>
public static async ValueTask<Result<TOk>> EnsureAsync<TOk>(
    this ValueTask<Result<TOk>> result,
    Func<TOk, ValueTask<bool>> predicate,
    Error error)
```

## T4 Template Testing Strategy

T4 templates generate 2-tuple through 9-tuple overloads with identical logic. **Test the 2-tuple comprehensively; validate other sizes with minimal tests.**

### T4-Generated Files

| Template | Generated Source | Purpose |
|----------|-----------------|---------|
| `TapTs.g.tt` | `TapTs.g.cs` | Tap for tuple Results |
| `TapOnFailureTs.g.tt` | `TapOnFailureTs.g.cs` | TapOnFailure for tuple Results |
| `BindTs.g.tt` | `BindTs.g.cs` | Bind for tuple Results |
| `MatchTupleTs.g.tt` | `MatchTupleTs.g.cs` | Match for tuple Results |
| `CombineTs.g.tt` | `CombineTs.g.cs` | Combine for multiple Results |
| `MapTs.g.tt` | `MapTs.g.cs` | Map for tuple Results |
| `WhenAllTs.g.tt` | `WhenAllTs.g.cs` | WhenAll for parallel Results |
| `ParallelAsyncs.g.tt` | `ParallelAsyncs.g.cs` | Parallel async operations |

### Test File Naming for T4 Code

| Source File | Test File |
|-------------|-----------|
| `TapTs.g.cs` | `TapTupleTests.cs` |
| `TapOnFailureTs.g.cs` | `TapOnFailureTupleTests.cs` |
| `BindTs.g.cs` | `BindTsTests.cs` |
| `MapTs.g.cs` | `MapTsTests.cs` |
| `MatchTupleTs.g.cs` | (tracing tests) |
| `ParallelAsyncs.g.cs` | `ParallelAsyncTests.cs` |

### What to Test

| Scope | Coverage |
|-------|----------|
| **2-tuple** | Comprehensive: success/failure paths, destructuring, chaining, async variants, different types, real-world scenarios |
| **3-tuple, 9-tuple** | Validation only: one success test, one failure test for largest size |
| **Other sizes** | None — template guarantees consistency |

**Expected coverage: ~12–35%.** This is intentional. Don't aim for 100% on T4-generated code.

### Modifying T4 Templates

1. Run the template to regenerate `.g.cs`
2. Update 2-tuple tests if the pattern changed
3. Verify one larger tuple still works (5-tuple or 9-tuple)

## Activity Tracing and OpenTelemetry

### Core Rules

| Rule | Correct Pattern | Why |
|------|----------------|-----|
| Setting activity status | `activity?.SetStatus(...)` (local variable) | `Activity.Current` has race conditions in concurrent scenarios |
| Test isolation | `AsyncLocal<ActivitySource?>` with inject/reset | Per-context isolation, parallel-safe, no `[Collection]` needed |
| Test helpers | Unique `ActivitySource` per test + `ActivityListener` | Isolated activity capture per test instance |

### Activity Status: TryCreate vs ROP Methods

| Context | Manual status needed? | Reason |
|---------|----------------------|--------|
| Value object `TryCreate` | **No** | Activity is root → becomes `Activity.Current` → Result constructor sets it automatically |
| ROP extensions (Bind, Tap, Map) | **Yes** — call `result.LogActivityStatus()` | Creates child activity ≠ `Activity.Current`; Result constructor sets parent, not child |

The `Result<T>` constructor automatically sets `Activity.Current` status:

```csharp
internal Result(bool isFailure, TValue? ok, Error? error)
{
    // ... validation ...
    Activity.Current?.SetStatus(IsFailure ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
    if (IsFailure && Activity.Current is { } act && error is not null)
        act.SetTag("result.error.code", error.Code);
}
```

**TryCreate** — no manual status needed (activity IS `Activity.Current`):

```csharp
public static Result<EmailAddress> TryCreate(string? value, string? fieldName = null)
{
    using var activity = PrimitiveValueObjectTrace.ActivitySource.StartActivity("EmailAddress.TryCreate");
    if (value is not null && EmailRegEx().IsMatch(value))
        return new EmailAddress(value);  // Result constructor sets Activity.Current (== activity)
    return Result.Fail<EmailAddress>(
        new Error.UnprocessableContent(
            EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty(field ?? string.Empty), "invalid_email")
                { Detail = "Email address is not valid." })));
}
```

**ROP extensions** — must explicitly set child activity status:

```csharp
public static Result<TValue> Tap<TValue>(this Result<TValue> result, Action<TValue> action)
{
    using var activity = RopTrace.ActivitySource.StartActivity();  // Child activity
    if (result.IsSuccess)
        action(result.Value);
    result.LogActivityStatus();  // ✅ Must set explicitly — child ≠ Activity.Current
    return result;
}
```

### Test Isolation Pattern

Use `AsyncLocal<ActivitySource?>` for parallel-safe test isolation without `[Collection]` attributes:

```csharp
public static class PrimitiveValueObjectTrace
{
    private static readonly ActivitySource _defaultActivitySource = new("Trellis.Primitives", "1.0.0");
    private static readonly AsyncLocal<ActivitySource?> _testActivitySource = new();

    public static ActivitySource ActivitySource => _testActivitySource.Value ?? _defaultActivitySource;
    internal static void SetTestActivitySource(ActivitySource s) => _testActivitySource.Value = s;
    internal static void ResetTestActivitySource() => _testActivitySource.Value = null;
}
```

Test helper pattern: create a unique `ActivitySource` per test instance, inject via `SetTestActivitySource`, capture activities via `ActivityListener`, and reset in `Dispose()`. See `PvoActivityTestHelper` for the full implementation.

## File Encoding & PowerShell

All files must be **UTF-8 with BOM**.

```powershell
# ✅ Correct — preserves all characters
$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($path, $content, $utf8Bom)

# ❌ NEVER use Set-Content — corrupts emoji, arrows, special symbols
Set-Content $path -Value $content -NoNewline
```

When running PowerShell commands in the terminal:
- Avoid long or complex scripts — they tend to get stuck or timeout
- Use smaller, targeted file edits with the `replace_string_in_file` tool instead of large PowerShell scripts for file manipulation

## Known Namespace Collisions

### `Trellis.Unit` vs `Mediator.Unit`

Projects referencing both `Trellis.Core` and `Mediator` will encounter ambiguous `Unit` references. Both libraries define a `Unit` type.

**Workarounds:**

```csharp
// Preferred: Use parameterless Result.Ok() — avoids referencing Unit entirely
return Result.Ok();  // instead of Result.Ok(Unit.Value)

// Alternative: Using alias (if you need to reference Unit directly)
using Unit = Trellis.Unit;
```

The parameterless `Result.Ok()` is preferred — it avoids the type name entirely.

## Pre-Submission Checklist

Before committing any changes:

1. **All tests pass** — `dotnet test` from the repository root must report zero failures.
2. **Code review by GPT-5.4** — Use a code-review agent with `model: gpt-5.4` to review all changed files before committing. Address any issues it flags as bugs, security vulnerabilities, or logic errors.
3. **User review** — Present a summary of changes to the user and wait for explicit approval before committing.
