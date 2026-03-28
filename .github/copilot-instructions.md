# GitHub Copilot Instructions for Trellis

## Project Overview

Functional programming library for .NET 10 implementing Railway Oriented Programming (ROP), Domain-Driven Design (DDD) primitives, and value objects.

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
| Trellis.Results | `Result<T>`, `Maybe<T>`, `Error`, and all extension methods: `Bind`, `Tap`, `Match`, `Combine`, `Ensure`, `Map` |
| Trellis.DomainDrivenDesign | `Aggregate<T>`, `Entity<T>`, `ValueObject`, `Specification<T>` |
| Trellis.Primitives | `RequiredString`, `RequiredGuid`, `RequiredInt`, `RequiredDecimal`, `RequiredEnum` base classes |

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
| `Trellis.Authorization` | Domain/Application layer | `Actor`, `IActorProvider`, `IAuthorize`, `IAuthorizeResource<TResource>`, `IResourceLoader<TMessage, TResource>` |
| `Trellis.Asp.Authorization` | API layer only | `EntraActorProvider` (production), `DevelopmentActorProvider` (dev/testing), `AddEntraActorProvider()`, `AddDevelopmentActorProvider()` |
| `Trellis.Asp` | API layer only | `ToMinimalApiResult()`, `ToActionResult()` |
| `Trellis.Http` | ACL layer only | `HttpClient` → `Result<T>` extensions |
| `Trellis.Stateless` | Domain layer (when needed) | Stateless state machine integration |
| `Trellis.FluentValidation` | Domain layer (when needed) | FluentValidation integration |
| `Trellis.Testing` | Test projects only | FluentAssertions extensions for `Result<T>` |
| `Trellis.Mediator` | Application layer (CQRS only) | Mediator pipeline behaviors (depends on `Trellis.Authorization`) |
| `Trellis.EntityFrameworkCore` | ACL layer only | EF Core integration |

### Namespace Placement Rule

If a type has **zero dependencies** beyond the .NET runtime → `Trellis` namespace.
If it pulls in a **third-party or framework dependency** → its own namespace matching the package name.

### Do NOT

- Do NOT create one namespace per package (e.g., `Trellis.RailwayOrientedProgramming`). Core types share `Trellis`.
- Do NOT put `ToMinimalApiResult` or `ToActionResult` in the `Trellis` namespace. They depend on ASP.NET Core and belong in `Trellis.Asp`.
- Do NOT put ready-to-use value objects like `EmailAddress` in the `Trellis` namespace. They belong in `Trellis.Primitives` to avoid collision.

## Value Object Creation Patterns

All value objects provide two factory methods:

| Method | Returns | Use When |
|--------|---------|----------|
| `TryCreate` | `Result<T>` | Failure is expected (API input, user validation) |
| `Create` | `T` (throws on failure) | Values are known-valid (tests, constants, config) |

```csharp
//  TryCreate — handle errors gracefully
var result = Money.TryCreate(amount, currencyCode);
if (result.IsFailure)
    return result.Error;

//  Create — failure is exceptional
var testMoney = Money.Create(100.00m, "USD");

//  Don't use .Value on TryCreate in production
var money = Money.TryCreate(amount, currency).Value;
```

### Implementation Details

- All scalar value objects inherit `Create(T value)` from `ScalarValueObject<TSelf, T>`, which calls `TryCreate` and throws `InvalidOperationException` on failure
- Source-generated types (`RequiredGuid`, `RequiredString`, `RequiredInt`, `RequiredDecimal`) auto-generate `Create()` overloads mirroring each `TryCreate()` overload
- `RequiredString` supports optional `[StringLength(max)]` or `[StringLength(max, MinimumLength = min)]` — the generator emits `.Ensure()` length checks in `TryCreate`
- The TRLS007 analyzer suggests `Create()` instead of `TryCreate().Value`
- Override `Create` for multi-parameter signatures (e.g., `Money.Create(amount, currency)`)

**Generated Create overloads:**

| Type | Overloads |
|------|-----------|
| `RequiredGuid` | `Create(Guid)`, `Create(string)`, `NewUniqueV4()`, `NewUniqueV7()` |
| `RequiredString` | `Create(string?, string? fieldName)` — add `[StringLength(max, MinimumLength = min)]` for length validation |
| `RequiredInt` / `RequiredDecimal` | `Create(int/decimal)`, `Create(string)` |

**Custom value objects** inherit `Create` automatically:

```csharp
public class Temperature : ScalarValueObject<Temperature, decimal>,
    IScalarValue<Temperature, decimal>
{
    private Temperature(decimal value) : base(value) { }

    public static Result<Temperature> TryCreate(decimal value, string? fieldName = null) =>
        value.ToResult()
            .Ensure(v => v >= -273.15m, Error.Validation("Below absolute zero", fieldName ?? "temperature"))
            .Map(v => new Temperature(v));

    // Create is inherited from base class — no need to implement!
}
```

**Multi-parameter Create** — override explicitly when TryCreate has multiple required parameters:

```csharp
public static Money Create(decimal amount, string currencyCode)
{
    var result = TryCreate(amount, currencyCode);
    if (result.IsFailure)
        throw new InvalidOperationException($"Failed to create Money: {result.Error.Detail}");
    return result.Value;
}
```

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

### Core Types

- **`Result<TValue>`**: Success (with value) or failure (with error)
- **`Maybe<T>`**: Domain-level optionality (`where T : notnull`). Supports `Map<TResult>`, `Match<TResult>`, `GetValueOrDefault`, `TryGetValue`, implicit operator. Use `Maybe<T>` instead of `T?` for optional value objects in DTOs (e.g., `Maybe<Url> Website`). ASP.NET Core integration: `MaybeScalarValueJsonConverter`, `MaybeModelBinder`, `MaybeSuppressChildValidationMetadataProvider` — all registered automatically by `AddScalarValueValidation()`.
- **`Error`**: Base error type (Validation, NotFound, Unauthorized, etc.)

### Key Methods

| Method | Purpose |
|--------|---------|
| **Bind** | Transform value inside Result (flatMap) |
| **Map** | Transform value, preserve Result wrapper |
| **Ensure** | Add validation to a Result |
| **Tap** | Side effects without changing Result |
| **Match** | Pattern match on success/failure |
| **Combine** | Combine multiple Results |

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
2. **Documentation updated:**
   - `trellis-api-reference.md` — if any public API was added or changed
   - `trellis-api-testing-reference.md` — if test helpers were added or changed
   - Package `README.md` — if the package's public surface changed
   - Docfx articles in `docs/docfx_project/articles/` — if relevant articles exist for the feature area
3. **Do NOT commit without explicit approval** — stage changes and present the diff for review
4. **Do NOT push branches** — the repository owner will push when ready
5. **Do NOT create or merge pull requests** — present changes locally for review

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
await ValueTask.FromResult(Result.Success("test"))
    .EnsureAsync(v => ValueTask.FromResult(v.Length > 0), Error.Validation("Empty"));

// Left only → EnsureTests.ValueTask.Left.cs
await ValueTask.FromResult(Result.Success("test"))
    .EnsureAsync(v => v.Length > 0, Error.Validation("Empty"));

// Right only → EnsureTests.ValueTask.Right.cs
await Result.Success("test")
    .EnsureAsync(v => ValueTask.FromResult(v.Length > 0), Error.Validation("Empty"));
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
var result = await Result.Failure<int>(error)
    .EnsureAsync(v => { predicateInvoked = true; return v > 0; }, error);

predicateInvoked.Should().BeFalse("predicate should not be invoked for failed results");
```

## File Location Guidelines

| Area | Source | Tests |
|------|--------|-------|
| Core ROP | `Trellis.Results/src/Result/Extensions/` | `Trellis.Results/tests/Results/Extensions/` |
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
| `trellis-api-reference.md` | `trellis-api-reference.md` (repo root) | Update the AI API reference with any new or changed public types, methods, or extension methods — this document is consumed by AI coding assistants |

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

## Database-Backed Permissions

When permissions need to be stored in the application database instead of AAD app roles, implement `IAsyncActorProvider`. The Mediator `AuthorizationBehavior` prefers `IAsyncActorProvider` over `IActorProvider` when both are registered.

### Pattern

| Layer | Component | Purpose |
|-------|-----------|---------|
| Domain | `AppUser`, `Role`, `Permission` entities | Schema for users → roles → permissions |
| Application | `IPermissionRepository` | Interface for loading permissions by external user ID |
| Acl | `PermissionRepository` | EF Core implementation with LINQ query |
| Api | `DatabaseActorProvider : IAsyncActorProvider` | Resolves `oid` claim → DB permissions → `Actor` |

### DI Registration

```csharp
if (environment.IsDevelopment())
{
    services.AddDevelopmentActorProvider();  // X-Test-Actor header
}
else
{
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options => configuration.Bind("AzureAd", options));
    services.AddEntraActorProvider();  // Required IActorProvider for AuthorizationBehavior
    services.AddMemoryCache();
    services.AddScoped<IPermissionRepository, PermissionRepository>();
    services.AddScoped<IAsyncActorProvider, DatabaseActorProvider>();  // Takes precedence at runtime
}
```

Both `IActorProvider` (via `AddEntraActorProvider()`) and `IAsyncActorProvider` (via `DatabaseActorProvider`) must be registered. `AuthorizationBehavior` requires `IActorProvider` but prefers the async provider at runtime.

See the [Database-Backed Permissions guide](../docs/docfx_project/articles/integration-db-permissions.md) for the complete schema, entities, seed data, and caching strategy.

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
    return Result.Failure<EmailAddress>(Error.Validation("Email address is not valid.", field));
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

## Maybe\<T\> with EF Core

`Maybe<T>` is a `readonly struct`. EF Core refuses to mark non-nullable struct properties as optional — calling `IsRequired(false)` or setting `IsNullable = true` throws `InvalidOperationException`. This is a hard limitation in EF Core's metadata layer.

### Solution: Source Generator + `MaybeConvention`

Use C# 13 `partial` properties with `Maybe<T>`. The `Trellis.EntityFrameworkCore.Generator` source generator emits the private `_camelCase` backing field and getter/setter implementation automatically. The `MaybeConvention` (registered by `ApplyTrellisConventions`) auto-discovers `Maybe<T>` properties, ignores the struct property, and maps the backing field as nullable.

```csharp
// Entity — just declare partial Maybe<T> properties
public partial class Customer
{
    public CustomerId Id { get; set; } = null!;

    public partial Maybe<PhoneNumber> Phone { get; set; }

    public partial Maybe<DateTime> SubmittedAt { get; set; }
}
```

```csharp
// OnModelCreating — no MaybeProperty calls needed, convention handles everything
modelBuilder.Entity<Customer>(b =>
{
    b.HasKey(c => c.Id);
});
```

### What the Source Generator Emits

For each `partial Maybe<T>` property, the generator emits a private nullable backing field and the getter/setter:

```csharp
// Auto-generated
private PhoneNumber? _phone;
public partial Maybe<PhoneNumber> Phone
{
    get => _phone is not null ? Maybe.From(_phone) : Maybe.None<PhoneNumber>();
    set => _phone = value.HasValue ? value.Value : null;
}
```

### What `MaybeConvention` Does

1. Always ignores the `Maybe<T>` CLR property (EF Core can't map structs as nullable)
2. Discovers the private `_camelCase` backing field
3. Maps the backing field as optional (`IsRequired(false)`)
4. Sets the column name to the original property name (`Phone`, not `_phone`)
5. Configures field-only access mode

### Naming Convention

The backing field follows `_camelCase` naming from the property name:

| Property | Backing Field | Column Name |
|----------|---------------|-------------|
| `Phone` | `_phone` | `Phone` |
| `SubmittedAt` | `_submittedAt` | `SubmittedAt` |
| `AlternateEmail` | `_alternateEmail` | `AlternateEmail` |

### TRLSGEN100 Diagnostic

If a `Maybe<T>` property is not declared `partial`, the generator emits diagnostic `TRLSGEN100` prompting the developer to add the `partial` modifier.

### Value Converters

Trellis scalar value objects (e.g., `PhoneNumber`, `EmailAddress`) get their converters automatically from `ApplyTrellisConventions`. No extra `HasConversion` call is needed. For plain CLR types (`DateTime?`, `string?`), EF Core handles them natively.

### Querying Maybe\<T\> Properties

Because `MaybeConvention` ignores the `Maybe<T>` CLR property, EF Core cannot translate direct LINQ references to it. Use the query extensions instead of raw `EF.Property` calls:

```csharp
// WhereNone — WHERE column IS NULL
var withoutPhone = await context.Customers.WhereNone(c => c.Phone).ToListAsync(ct);

// WhereHasValue — WHERE column IS NOT NULL
var withPhone = await context.Customers.WhereHasValue(c => c.Phone).ToListAsync(ct);

// WhereEquals — WHERE column = @value
var matches = await context.Customers.WhereEquals(c => c.Phone, phone).ToListAsync(ct);
```

These methods rewrite the expression tree to target the backing field via `EF.Property<T?>`, so EF Core can translate the query to SQL.

### Maybe\<T\> with Composite Owned Types

`partial Maybe<Money>` is also supported. The conventions automatically configure it as an optional owned type — no `OwnsOne` configuration needed:

```csharp
public partial class Penalty : Aggregate<PenaltyId>
{
    public Money Fine { get; set; } = null!;              // required Money (2 NOT NULL columns)
    public partial Maybe<Money> FinePaid { get; set; }    // optional Money (2 nullable columns)
}
```

Column naming follows `MoneyConvention`: `FinePaid` (amount) and `FinePaidCurrency`.

> **Note:** `ExecuteUpdate` helpers (`SetMaybeValue`/`SetMaybeNone`) do not support `Maybe<Money>`. Use tracked entity updates (load, modify, `SaveChangesAsync`) instead.

## Money with EF Core

`Money` properties are automatically mapped as owned types by `ApplyTrellisConventions` — no `OwnsOne` configuration needed.
This includes `Money` properties declared on owned entity types, including items inside `OwnsMany` collections.

For optional Money properties, use `partial Maybe<Money>` — see the Maybe\<T\> section above.

> **Single-currency alternative:** If your system uses one currency everywhere, use `MonetaryAmount` instead of `Money`. It is a scalar value object (`ScalarValueObject<MonetaryAmount, decimal>`) that maps to a single `decimal` column — no currency column needed. See the Trellis.Primitives README for details.

### How It Works

The `MoneyConvention` (registered by `ApplyTrellisConventions`) uses two EF Core convention interfaces:
- `IModelInitializedConvention` — calls `modelBuilder.Owned(typeof(Money))` to pre-register Money as an owned type before entity discovery runs
- `IModelFinalizingConvention` — sets column names, precision, and max length on the owned Money properties

### Column Naming Convention

| Property Name | Amount Column | Currency Column |
|---------------|---------------|-----------------|
| `Price` | `Price` | `PriceCurrency` |
| `ShippingCost` | `ShippingCost` | `ShippingCostCurrency` |

Amount columns: `decimal(18,3)`. Currency columns: `nvarchar(3)` (ISO 4217). Scale 3 accommodates all ISO 4217 minor units (0 for JPY, 2 for USD/EUR, 3 for BHD/KWD/OMR/TND).

### Entity Declaration

Just declare `Money` properties — no special backing fields or configuration needed:

```csharp
public class Order
{
    public OrderId Id { get; set; } = null!;
    public Money Price { get; set; } = null!;
    public Money ShippingCost { get; set; } = null!;
}
```

### Explicit Override

If you need custom column names, use `OwnsOne` in `OnModelCreating` — explicit configuration takes precedence:

```csharp
modelBuilder.Entity<Order>(b =>
{
    b.OwnsOne(o => o.Price, money =>
    {
        money.Property(m => m.Amount).HasColumnName("UnitPrice").HasPrecision(19, 4);
        money.Property(m => m.Currency).HasColumnName("UnitCurrency");
    });
});
```

## Composite ValueObjects as EF Core Owned Types

When a composite `ValueObject` (like `ShippingAddress`) is persisted via EF Core's `OwnsOne`, it needs specific boilerplate for EF Core materialization:

```csharp
public sealed record ShippingAddress : ValueObject
{
    // EF Core requires a private parameterless constructor for materialization
    private ShippingAddress() { }

    // Properties must use 'private set' (not 'get;' only) and '= null!' for reference types
    public Street Street { get; private set; } = null!;
    public City City { get; private set; } = null!;
    public State State { get; private set; } = null!;
    public PostalCode PostalCode { get; private set; } = null!;
    public Country Country { get; private set; } = null!;

    // Public factory — the only way to create instances
    public static Result<ShippingAddress> TryCreate(
        Street street, City city, State state, PostalCode postalCode, Country country) =>
        new ShippingAddress
        {
            Street = street, City = city, State = state,
            PostalCode = postalCode, Country = country
        };

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
        yield return PostalCode;
        yield return Country;
    }
}
```

**Rules:**
- Always add `private ShippingAddress() { }` — EF Core cannot materialize without it
- Use `{ get; private set; }` not `{ get; }` — EF Core sets properties via reflection
- Add `= null!` to all reference type properties — suppresses nullable warning for the private constructor path
- Value type properties (e.g., `int`, `decimal`, `DateTime`) do not need `= null!`

**Entity configuration:**
```csharp
// In IEntityTypeConfiguration<Customer>
builder.OwnsOne(c => c.ShippingAddress);
```

**Nested OwnsOne with OwnsMany:** When two `OwnsOne` navigations share the same child type that has `OwnsMany` collections, EF Core defaults to the same table name. Use explicit `ToTable()`:
```csharp
builder.OwnsOne(s => s.Inning1, b => b.OwnsMany(i => i.BattingEntries).ToTable("Inning1BattingEntries"));
builder.OwnsOne(s => s.Inning2, b => b.OwnsMany(i => i.BattingEntries).ToTable("Inning2BattingEntries"));
```

## Known Namespace Collisions

### `Trellis.Unit` vs `Mediator.Unit`

Projects referencing both `Trellis.Results` and `Mediator` will encounter ambiguous `Unit` references. Both libraries define a `Unit` type.

**Workarounds:**

```csharp
// Preferred: Use parameterless Result.Success() — avoids referencing Unit entirely
return Result.Success();  // instead of Result.Success(Unit.Value)

// Alternative: Using alias (if you need to reference Unit directly)
using Unit = Trellis.Unit;
```

The parameterless `Result.Success()` is preferred — it avoids the type name entirely.

## Pre-Submission Checklist

Before committing any changes:

1. **All tests pass** — `dotnet test` from the repository root must report zero failures.
2. **Code review by GPT-5.4** — Use a code-review agent with `model: gpt-5.4` to review all changed files before committing. Address any issues it flags as bugs, security vulnerabilities, or logic errors.
