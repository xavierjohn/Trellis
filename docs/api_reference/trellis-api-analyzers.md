# Trellis.Analyzers — API Reference

- **Package:** `Trellis.Analyzers`
- **Namespace:** `Trellis.Analyzers`
- **Purpose:** Roslyn analyzers and code fixes that enforce correct Trellis `Result<T>`, `Maybe<T>`, EF Core, and value-object usage.

## Diagnostics

| ID | Severity | Title | Description |
|----|----------|-------|-------------|
| `TRLS001` | Warning | Result return value is not handled | Result<T> return values should be handled to ensure errors are not silently ignored. Use Bind, Map, Match, or assign to a variable. |
| `TRLS002` | Info | Use Bind instead of Map when lambda returns Result | When the transformation function returns a Result<T>, use Bind (flatMap) instead of Map. Map will produce Result<Result<T>> which is likely not intended. |
| `TRLS003` | Warning | Unsafe access to Result.Value | Result.Value throws an InvalidOperationException if the Result is in a failure state. Check IsSuccess first, use TryGetValue, or use pattern matching with Match. |
| `TRLS004` | Warning | Unsafe access to Result.Error | Result.Error is `Error?` and **never throws** — it returns `null` on success. Pattern-match on the value (`if (result.Error is { } error)`) instead of asserting non-null with `!`. Or use TryGetError, Match, or an IsFailure guard. |
| `TRLS005` | *(removed)* | *(removed in v2)* | The `UseMatchErrorAnalyzer` was deleted because `Error` is now a closed ADT — `switch` over an `Error` reference is exhaustive at the language level (per `docs/adr/ADR-001-result-api-surface.md`). Replace `MatchError(...)` calls with a `switch` expression on `Error.X` cases. |
| `TRLS006` | Warning | Unsafe access to Maybe.Value | Maybe.Value throws an InvalidOperationException if the Maybe has no value. Check HasValue first, use TryGetValue, GetValueOrDefault, or convert to Result with ToResult. |
| `TRLS007` | Warning | Use Create instead of TryCreate().Value | Using TryCreate().Value is unclear and provides poor error messages when validation fails. Use Create() when you expect success - it throws InvalidOperationException with the validation error details included. TryCreate().Value throws the same exception type but with a generic message, losing the validation error information. Or properly handle the Result returned by TryCreate() to avoid exceptions entirely. |
| `TRLS008` | Warning | Result is double-wrapped | Result should not be wrapped inside another Result. This creates Result<Result<T>> which is almost always unintended. If combining Results, use Bind instead of Map. If wrapping a value, ensure it's not already a Result. |
| `TRLS009` | Warning | Incorrect async Result usage | Task<Result<T>> should be awaited, not blocked with .Result or .Wait(). Blocking can cause deadlocks and prevents proper async execution. Use await instead. |
| `TRLS010` | Info | Use a specific Error case instead of constructing the abstract base | `Error` is an abstract closed ADT. Construct one of the nested cases — `new Error.NotFound(...)`, `new Error.UnprocessableContent(...)`, etc. The base record cannot be instantiated directly. |
| `TRLS011` | Warning | Maybe is double-wrapped | Maybe should not be wrapped inside another Maybe. This creates Maybe<Maybe<T>> which is almost always unintended. Avoid using Map when the transformation function returns a Maybe, as this creates double wrapping. Consider converting to Result with ToResult() for better composability. |
| `TRLS012` | Info | Consider using Result.Combine | When combining multiple Result<T> values, Result.Combine() or .Combine() chaining provides a cleaner and more maintainable approach than manually checking IsSuccess on each result. |
| `TRLS013` | Info | Consider using GetValueOrDefault or Match | The pattern 'result.IsSuccess ? result.Value : default' can be replaced with GetValueOrDefault() or Match() for more idiomatic and safer code. |
| `TRLS014` | Warning | Use async method variant for async lambda | When using an async lambda with Map, Bind, Tap, or Ensure, use the async variant (MapAsync, BindAsync, etc.) to properly handle the async operation. Using sync methods with async lambdas causes the Task to not be awaited. |
| `TRLS015` | Warning | Don't throw exceptions in Result chains | Throwing exceptions inside Bind, Map, Tap, or Ensure lambdas defeats the purpose of Railway Oriented Programming. Return Result.Fail<T>() to signal errors and keep the error on the failure track. |
| `TRLS016` | Warning | Error message should not be empty | Error messages should provide context for debugging and user feedback. Empty error messages make it difficult to diagnose issues. |
| `TRLS017` | Warning | Don't compare Result or Maybe to null | Result<T> and Maybe<T> are structs and cannot be null. Use IsSuccess/IsFailure for Result, or HasValue/HasNoValue for Maybe. |
| `TRLS018` | Warning | Unsafe access to Value in LINQ expression | When using LINQ on collections of Result<T> or Maybe<T>, filter by IsSuccess/HasValue first, or use methods like Select with Match to safely extract values. |
| `TRLS019` | Error | Combine chain exceeds maximum supported tuple size | Combine supports up to 9 elements. Downstream methods (Bind, Map, Tap, Match) also only support tuples up to 9 elements. Group related fields into intermediate value objects or sub-results, then combine those groups. |
| `TRLS020` | Warning | Use SaveChangesResultAsync instead of SaveChangesAsync | Direct SaveChanges/SaveChangesAsync calls bypass the Result pipeline and turn database errors into unhandled exceptions. Use SaveChangesResultAsync (returns Result<int>) or SaveChangesResultUnitAsync (returns the non-generic `Result`) instead. |
| `TRLS021` | Warning | HasIndex references a Maybe<T> property | HasIndex with a Maybe<T> property silently fails to create the index because MaybeConvention maps Maybe<T> via generated storage members, so the CLR property is invisible to EF Core's index builder. Prefer HasTrellisIndex so regular properties stay strongly typed and Maybe<T> properties resolve to their mapped storage automatically. If needed, you can also use string-based HasIndex with the storage member name directly. Examples: builder.HasTrellisIndex(e => new { e.Status, e.SubmittedAt }); or builder.HasIndex("Status", "_submittedAt"). |
| `TRLS022` | Warning | Wrong [StringLength] or [Range] attribute namespace | Trellis [StringLength] and [Range] attributes share names with System.ComponentModel.DataAnnotations versions. Using the wrong namespace compiles silently but the Trellis source generator ignores them, resulting in value objects without the expected validation constraints. Use the Trellis versions (namespace Trellis) instead. |
| `TRLS024` | Warning | Unsafe Result deconstruction | Reading the value position of a `Result<T>` deconstruction (`var (success, value, error) = result;`) without first checking `success`/`error` returns the default value when the result is in failure. Gate the read with the success bool, an `error is null` check, or an early return on failure. |
| `TRLS025` | Warning | Unsafe property pattern over Result.Value | Property patterns such as `result is { Value: ... }` or `result switch { { Value: var v } => ... }` succeed on failure results because `Value` is the default — they bypass the success discriminator. Pattern-match on `IsSuccess`/`IsFailure` first, or use `Match` / explicit guard. |

## Analyzer classes

### Result and Maybe flow

#### `ResultNotHandledAnalyzer` — `TRLS001`
- Flags expression statements that discard a `Result<T>`.
- Also flags discarded `await` expressions when the awaited type is `Task<Result<T>>` or `ValueTask<Result<T>>`.
- Unwraps `await someCall.ConfigureAwait(false)` before checking the awaited type.
- No code fix.

#### `UseBindInsteadOfMapAnalyzer` — `TRLS002`
- Flags Trellis `Map` and `MapAsync` invocations when the first argument returns:
  - `Result<T>`
  - `Task<Result<T>>`
  - `ValueTask<Result<T>>`
- Covers lambda expressions, method groups, and member-access method groups.
- Purpose: prevent `Result<Result<T>>`.
- Code fix: `UseBindInsteadOfMapCodeFixProvider`.

#### `UnsafeValueAccessAnalyzer` — `TRLS003`, `TRLS004`, `TRLS006`
- `TRLS003`: flags `result.Value` when the analyzer cannot prove the access is guarded by success-state checks.
- `TRLS004`: flags `result.Error` when the analyzer cannot prove the access is guarded by failure-state checks.
- `TRLS006`: flags `maybe.Value` when the analyzer cannot prove the access is guarded by presence checks.
- Recognized safe patterns include:
  - `if` / ternary checks on `IsSuccess`, `IsFailure`, `HasValue`, `HasNoValue`
  - `TryGetValue` / `TryGetError` branches, including negated forms
  - early-return guards such as `if (result.IsFailure) return ...;`
  - `maybe.HasValue && maybe.Value ...`
  - safe lambda parameters inside Trellis track-aware APIs such as `Bind`, `Map`, `Tap`, `Ensure`, `Match`, `Switch`, and failure-track variants
  - for `Maybe<T>`, prior assignment from `Maybe.From(...)` when `T` is a non-nullable value type and the variable is not reassigned
- `TRLS003` intentionally skips direct `TryCreate(...).Value`; that is handled by `TRLS007`.
- Code fix: `AddResultGuardCodeFixProvider`.

#### `UseMatchErrorAnalyzer` — `TRLS005` *(removed in v2)*

This analyzer was deleted in v2. With the closed-ADT `Error` (see `docs/adr/ADR-001-result-api-surface.md`), `switch` over an `Error` reference is exhaustive at the language level — the C# compiler verifies that every nested case is handled — so manual error-type discrimination is the recommended pattern. Replace any remaining `result.MatchError(onValidation: ..., onNotFound: ..., ...)` calls with:

```csharp
result.Match(
    onSuccess: value => ...,
    onFailure: error => error switch
    {
        Error.NotFound nf            => ...,
        Error.UnprocessableContent uc => ...,
        Error.Conflict c             => ...,
        _                            => ...,
    });
```

#### `TryCreateValueAccessAnalyzer` — `TRLS007`
- Flags direct `.Value` access on a static `TryCreate(...)` call that returns `Result<T>` when the created type exposes a static `Create(...)` method.
- Reports on the `.Value` identifier, not the whole expression.
- Code fix: `UseCreateInsteadOfTryCreateValueCodeFixProvider`.

#### `ResultDoubleWrappingAnalyzer` — `TRLS008`
- Flags declared or inferred `Result<Result<T>>` in:
  - variable declarations
  - properties
  - method return types
  - parameters
- Also flags `Result.Ok(existingResult)` and `Result.Fail(existingResult)` when the argument is already a `Result<T>`.
- No code fix.

#### `AsyncResultMisuseAnalyzer` — `TRLS009`
- Flags blocking access on `Task<Result<T>>` and `ValueTask<Result<T>>`:
  - `.Result`
  - `.Wait()`
  - `.GetAwaiter().GetResult()`
- Handles both `Task` and `ValueTask`.
- No code fix.

#### `MaybeDoubleWrappingAnalyzer` — `TRLS011`
- Flags declared `Maybe<Maybe<T>>` in variable declarations, properties, method return types, and parameters.
- No code fix.

#### `UseResultCombineAnalyzer` — `TRLS012`
- Flags conditional logic that manually combines two or more Result-state checks:
  - `&&` chains over `.IsSuccess`
  - `||` chains over `.IsFailure`
- Uses operation analysis, so it looks at semantic property access rather than raw text.
- No code fix.

#### `TernaryValueOrDefaultAnalyzer` — `TRLS013`
- Flags the specific pattern `result.IsSuccess ? result.Value : fallback`.
- Only reports when the `IsSuccess` receiver and `.Value` receiver resolve to the same stable expression.
- Ignores repeated invocation receivers to avoid changing semantics.
- Code fix: `UseFunctionalValueOrDefaultCodeFixProvider`.

#### `AsyncLambdaWithSyncMethodAnalyzer` — `TRLS014`
- Flags synchronous Trellis methods called with async work:
  - `Map`
  - `Bind`
  - `Tap`
  - `Ensure`
  - `TapOnFailure`
- Reports when any argument is:
  - an `async` lambda
  - a non-async lambda whose converted return type is `Task` or `ValueTask`
  - a method group returning `Task` or `ValueTask`
- Verifies the receiver is a Trellis `Result`, `Maybe`, or async-result receiver.
- Code fix: `UseAsyncMethodVariantCodeFixProvider`.

#### `ThrowInResultChainAnalyzer` — `TRLS015`
- Flags `throw` statements and `throw` expressions inside lambdas passed to Trellis result-chain APIs:
  - `Bind`, `BindAsync`
  - `Map`, `MapAsync`
  - `Tap`, `TapAsync`
  - `Ensure`, `EnsureAsync`
  - `TapOnFailure`, `TapOnFailureAsync`
  - `MapOnFailure`, `MapOnFailureAsync`
  - `RecoverOnFailure`, `RecoverOnFailureAsync`
  - `DebugOnFailure`, `DebugOnFailureAsync`
- No code fix.

#### `EmptyErrorMessageAnalyzer` — `TRLS016`
- Flags empty or whitespace-only `Detail` values supplied to `Error` cases. With v2's closed ADT there are no static factory methods; the analyzer inspects the `Detail` initializer on `new Error.X(...) { Detail = ... }` constructions.
- Recognizes `""`, whitespace string literals, interpolated strings containing only whitespace text, and `string.Empty`.
- No code fix.

> **Note:** The analyzer source still lists v1 factory names (`Validation`, `NotFound`, `Unauthorized`, `Forbidden`, `Conflict`, `Unexpected`); a follow-up pass updates the analyzer itself to inspect the `Detail` initializer on the closed-ADT cases.

#### `ComparingToNullAnalyzer` — `TRLS017`
- Flags `== null`, `!= null`, `is null`, and `is not null` when the non-null side is a Trellis `Result<T>` or `Maybe<T>`.
- Suggests `IsSuccess` / `IsFailure` for `Result<T>` and `HasValue` / `HasNoValue` for `Maybe<T>`.
- No code fix.

#### `UnsafeValueInLinqAnalyzer` — `TRLS018`
- Flags `.Value` inside LINQ projection/order/grouping lambdas for:
  - `Select`
  - `SelectMany`
  - `ToDictionary`
  - `ToLookup`
  - `GroupBy`
  - `OrderBy`
  - `OrderByDescending`
  - `ThenBy`
  - `ThenByDescending`
- Reports only when `.Value` is accessed on the lambda parameter itself.
- Suppresses the diagnostic when an earlier `.Where(...)` in the same chain checks the required guard property:
  - `IsSuccess` for `Result<T>`
  - `HasValue` for `Maybe<T>`
- No code fix.

#### `CombineLimitAnalyzer` — `TRLS019`
- Flags the outermost `.Combine(...)` or `.CombineAsync(...)` chain when the resulting tuple would exceed 9 elements.
- Counts tuple width semantically, so chains continued through intermediate variables are still measured correctly.
- No code fix.

### Error, EF Core, and value-object rules

#### `ErrorBaseClassAnalyzer` — `TRLS010`
- Flags any attempt to construct the abstract base `Error` directly (which won't compile but is sometimes attempted via `new Error(...)` or implicit `new(...)` inference).
- Construct one of the nested cases instead: `new Error.NotFound(resource)`, `new Error.UnprocessableContent(fields)`, etc. See `docs/api_reference/trellis-api-results.md` for the full case catalog.
- No code fix.

#### `UseSaveChangesResultAnalyzer` — `TRLS020`
- Activates only when the compilation references `Trellis.EntityFrameworkCore.DbContextExtensions`.
- Flags direct `DbContext.SaveChangesAsync(...)` and `DbContext.SaveChanges(...)` calls, including unqualified calls inside a `DbContext` subclass.
- Recommends:
  - `SaveChangesResultAsync` when the return value is used
  - `SaveChangesResultUnitAsync` when the value is discarded
- Code fix: `UseSaveChangesResultCodeFixProvider`.

#### `HasIndexMaybePropertyAnalyzer` — `TRLS021`
- Activates only when the compilation references `Trellis.EntityFrameworkCore.MaybeConvention`.
- Flags `EntityTypeBuilder.HasIndex(...)` lambda members that reference `Maybe<T>` properties.
- Reports both the CLR property name and the generated storage-member fallback name (for example `_submittedAt`).
- No code fix.

#### `WrongAttributeNamespaceAnalyzer` — `TRLS022`
- Flags `System.ComponentModel.DataAnnotations.StringLengthAttribute` and `System.ComponentModel.DataAnnotations.RangeAttribute` applied to types that inherit from Trellis value-object base types:
  - `ScalarValueObject`
  - `RequiredString`
  - `RequiredInt`
  - `RequiredDecimal`
  - `RequiredLong`
  - `RequiredGuid`
  - `RequiredBool`
  - `RequiredDateTime`
  - `RequiredEnum`
- No code fix.

#### `UnsafeResultDeconstructionAnalyzer` — `TRLS024`
- Flags reads of the value position of a `Result<T>` deconstruction (`var (success, value, error) = result;`) when the read is not guarded by:
  - an `if`/`while`/conditional on the success bool,
  - an early-return on failure (`if (!success) return ...`),
  - a check that the error is `null`, or
  - the value being assigned to `_` (discard).
- Skips deconstructions where the value identifier is never read.
- No code fix.

#### `UnsafeResultValuePropertyPatternAnalyzer` — `TRLS025`
- Flags `RecursivePatternSyntax` patterns of the form `{ Value: ... }` applied to a `Result<T>` instance.
- Triggers on `is`-patterns, `switch` statements, and `switch` expressions.
- Property patterns succeed against failure results (`Value` is `default`), masking errors. Use a discriminator (`IsSuccess`/`IsFailure`) before inspecting `Value`.
- No code fix.

## Code fix providers

| Code fix provider | Fixes | Behavior |
|---|---|---|
| `AddResultGuardCodeFixProvider` | `TRLS003`, `TRLS004`, `TRLS006` | Wraps the current statement block in `if (result.IsSuccess)`, `if (result.IsFailure)`, or `if (maybe.HasValue)`. Tracks consecutive statements that keep using the guarded value. |
| `UseBindInsteadOfMapCodeFixProvider` | `TRLS002` | Replaces `Map` with `Bind` and `MapAsync` with `BindAsync`. |
| `UseCreateInsteadOfTryCreateValueCodeFixProvider` | `TRLS007` | Replaces `TryCreate(...).Value` with `Create(...)` when Roslyn can bind the replacement. |
| `UseFunctionalValueOrDefaultCodeFixProvider` | `TRLS013` | Replaces `result.IsSuccess ? result.Value : fallback` with `GetValueOrDefault(...)` or `Match(...)`. |
| `UseAsyncMethodVariantCodeFixProvider` | `TRLS014` | Replaces sync method names with async variants: `MapAsync`, `BindAsync`, `TapAsync`, `EnsureAsync`, `TapOnFailureAsync`. |
| `UseSaveChangesResultCodeFixProvider` | `TRLS020` | Replaces `SaveChangesAsync` / `SaveChanges` with `SaveChangesResultAsync` or `SaveChangesResultUnitAsync`, adds `await`/`async` for sync `SaveChanges`, and adds `using Trellis.EntityFrameworkCore;` when needed. |

## Compilable examples

```csharp
using System.Threading.Tasks;
using Trellis;

public static class AnalyzerExamples
{
    public static Result<int> Parse(string text) => Result.Ok(text.Length);

    public static Result<int> Valid()
    {
        var result = Parse("abc");
        return result.Map(length => Result.Ok(length + 1)); // TRLS002
    }

    public static async Task<Result<int>> ValidAsync()
    {
        Task<Result<int>> task = Task.FromResult(Result.Ok(42));
        var result = await task; // preferred over task.Result / task.Wait() / task.GetAwaiter().GetResult()
        return result;
    }
}
```

```csharp
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext : DbContext
{
}

public static class EfExample
{
    public static async Task SaveAsync(AppDbContext dbContext)
    {
        await dbContext.SaveChangesAsync(); // TRLS020
    }
}
```

## Cross-references

- [trellis-api-results.md](trellis-api-results.md) — `Result<T>`, `Maybe<T>`, `Bind`, `Map`, `Match`, `Combine`
- [trellis-api-efcore.md](trellis-api-efcore.md) — `SaveChangesResultAsync`, `SaveChangesResultUnitAsync`, `HasTrellisIndex`
- [trellis-api-primitives.md](trellis-api-primitives.md) — Trellis `[StringLength]` and `[Range]`
- [trellis-api-testing-reference.md](trellis-api-testing-reference.md) — testing helpers that intentionally work with analyzer rules
