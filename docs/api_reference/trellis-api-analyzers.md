# Trellis.Analyzers — API Reference

- **Package:** `Trellis.Analyzers`
- **Namespace:** `Trellis.Analyzers`
- **Purpose:** Roslyn analyzers and code fixes that enforce correct Trellis `Result<T>`, `Maybe<T>`, EF Core, and value-object usage.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package.

## Diagnostics

| ID | Severity | Title | Description |
|----|----------|-------|-------------|
| `TRLS001` | Warning | Result return value is not handled | Result<T> return values should be handled to ensure errors are not silently ignored. Use Bind, Map, Match, or assign to a variable. |
| `TRLS002` | Info | Use Bind instead of Map when lambda returns Result | When the transformation function returns a Result<T>, use Bind (flatMap) instead of Map. Map will produce Result<Result<T>> which is likely not intended. |
| `TRLS003` | Warning | Unsafe access to Maybe.Value | Maybe.Value throws an InvalidOperationException if the Maybe has no value. Check HasValue first, use TryGetValue, GetValueOrDefault, or convert to Result with ToResult. |
| `TRLS004` | Warning | Result is double-wrapped | Result should not be wrapped inside another Result. This creates Result<Result<T>> which is almost always unintended. If combining Results, use Bind instead of Map. If wrapping a value, ensure it's not already a Result. |
| `TRLS005` | Warning | Incorrect async Result usage | Task<Result<T>> should be awaited, not blocked with .Result or .Wait(). Blocking can cause deadlocks and prevents proper async execution. Use await instead. |
| `TRLS006` | Info | Use specific error type instead of base Error class | `Error` is an abstract closed ADT. Construct one of the nested cases — `new Error.NotFound(...)`, `new Error.UnprocessableContent(...)`, etc. The base record cannot be instantiated directly. |
| `TRLS007` | Warning | Maybe is double-wrapped | Maybe should not be wrapped inside another Maybe. This creates Maybe<Maybe<T>> which is almost always unintended. Avoid using Map when the transformation function returns a Maybe, as this creates double wrapping. Consider converting to Result with ToResult() for better composability. |
| `TRLS008` | Info | Consider using Result.Combine | When combining multiple Result<T> values, Result.Combine() or .Combine() chaining provides a cleaner and more maintainable approach than manually checking IsSuccess on each result. |
| `TRLS009` | Warning | Use async method variant for async lambda | When using an async lambda with Map, Bind, Tap, or Ensure, use the async variant (MapAsync, BindAsync, etc.) to properly handle the async operation. Using sync methods with async lambdas causes the Task to not be awaited. |
| `TRLS010` | Warning | Don't throw exceptions in Result chains | Throwing exceptions inside Bind, Map, Tap, or Ensure lambdas defeats the purpose of Railway Oriented Programming. Return Result.Fail<T>() to signal errors and keep the error on the failure track. |
| `TRLS011` | Warning | Error message should not be empty | Error messages should provide context for debugging and user feedback. Empty error messages make it difficult to diagnose issues. |
| `TRLS012` | Warning | Don't compare Result or Maybe to null | Result<T> and Maybe<T> are structs and cannot be null. Use IsSuccess/IsFailure for Result, or HasValue/HasNoValue for Maybe. |
| `TRLS013` | Warning | Unsafe access to Maybe.Value in LINQ expression | When using LINQ on collections of Maybe<T>, filter by HasValue first, or use Select with Match to safely extract values. |
| `TRLS014` | Error | Combine chain exceeds maximum supported tuple size | Combine supports up to 9 elements. Downstream methods (Bind, Map, Tap, Match) also only support tuples up to 9 elements. Group related fields into intermediate value objects or sub-results, then combine those groups. |
| `TRLS015` | Warning | Use SaveChangesResultAsync instead of SaveChangesAsync | Direct SaveChanges/SaveChangesAsync calls bypass the Result pipeline and turn database errors into unhandled exceptions. Use SaveChangesResultAsync (returns Result<int>) or SaveChangesResultUnitAsync (returns the non-generic `Result`) instead. |
| `TRLS016` | Warning | HasIndex references a Maybe<T> property | HasIndex with a Maybe<T> property silently fails to create the index because MaybeConvention maps Maybe<T> via generated storage members, so the CLR property is invisible to EF Core's index builder. Prefer HasTrellisIndex so regular properties stay strongly typed and Maybe<T> properties resolve to their mapped storage automatically. If needed, you can also use string-based HasIndex with the storage member name directly. Examples: builder.HasTrellisIndex(e => new { e.Status, e.SubmittedAt }); or builder.HasIndex("Status", "_submittedAt"). |
| `TRLS017` | Warning | Wrong [StringLength] or [Range] attribute namespace | Trellis [StringLength] and [Range] attributes share names with System.ComponentModel.DataAnnotations versions. Using the wrong namespace compiles silently but the Trellis source generator ignores them, resulting in value objects without the expected validation constraints. Use the Trellis versions (namespace Trellis) instead. |
| `TRLS018` | Warning | Result<T> deconstruction reads value without success gate | Reading the value position of a `Result<T>` deconstruction (`var (success, value, error) = result;`) without first checking `success`/`error` returns the default value when the result is in failure. Gate the read with the success bool, an `error is null` check, or an early return on failure. |
| `TRLS019` | Warning | Avoid `default(Result)`, `default(Result<T>)`, and `default(Maybe<T>)` | Per ADR-002 §3.5.1, `default(Result)` and `default(Result<T>)` are typed failures carrying the `new Error.Unexpected("default_initialized")` sentinel — never silent successes. `default(Maybe<T>)` equals `Maybe<T>.None` but the explicit literal obscures intent. Construct via `Result.Ok(...)` / `Result.Fail(...)` or `Maybe<T>.None` / `Maybe.From(...)`. Suppress with `[SuppressMessage("Trellis", TrellisDiagnosticIds.DefaultResultOrMaybe)]` or `#pragma warning disable TRLS019` for sanctioned sentinel/test-helper sites. |
| `TRLS020` | Warning | Composite value object DTO property is missing JSON converter | Composite `[OwnedEntity]` value objects exposed through request/response DTO surfaces must carry `[JsonConverter(typeof(CompositeValueObjectJsonConverter<T>))]` so JSON binding round-trips through `TryCreate`. |
| `TRLS021` | Warning | EF configuration duplicates Trellis conventions | Flags `HasConversion`, `OwnsOne`, and `Ignore` calls on `Maybe<T>` or `[OwnedEntity]` properties when `ApplyTrellisConventions(...)` / `ApplyTrellisConventionsFor<TContext>()` is wired. Remove the manual mapping and let Trellis conventions own the property. |
| `TRLS031` | Warning | Unsupported base type for `RequiredPartialClassGenerator` | Emitted by the Primitives source generator when a `Required*`-derived value object inherits from an unsupported base. Supported bases: `RequiredGuid`, `RequiredString`, `RequiredInt`, `RequiredDecimal`, `RequiredLong`, `RequiredBool`, `RequiredDateTime`, `RequiredEnum`. *(formerly `TRLSGEN001`)* |
| `TRLS032` | Error | `MinimumLength` exceeds `MaximumLength` | Emitted by the Primitives source generator when a `[StringLength]` attribute has `MinimumLength > MaximumLength`. Adjust the attribute values so the range is non-empty. *(formerly `TRLSGEN002`)* |
| `TRLS033` | Error | `Range` minimum exceeds maximum | Emitted by the Primitives source generator when a `[Range]` attribute on `int`/`long`/`decimal` has `Min > Max`. Adjust the attribute values so the range is non-empty. *(formerly `TRLSGEN003`)* |
| `TRLS034` | Error | Decimal range exceeds `decimal` bounds | Emitted by the Primitives source generator when a `[Range]` attribute on `decimal` exceeds the CLR `decimal` value range. Use a tighter range. *(formerly `TRLSGEN004`)* |
| `TRLS035` | Warning | `Maybe<T>` property should be `partial` | Emitted by the EF Core generator (`MaybePartialPropertyGenerator`) for non-partial auto-properties of type `Maybe<T>` whose containing type is `partial`. Declare the property `partial` so the generator can emit the backing field and storage member. *(formerly `TRLSGEN100`)* |
| `TRLS036` | Error | `[OwnedEntity]` type should be `partial` | Emitted by the EF Core generator (`OwnedEntityGenerator`) when `[OwnedEntity]` is applied to a non-partial type. Declare the type `partial` so the generator can emit the private parameterless constructor. *(formerly `TRLSGEN101`)* |
| `TRLS037` | Warning | `[OwnedEntity]` type already has a parameterless constructor | Emitted by the EF Core generator when `[OwnedEntity]` is applied to a type that already has a parameterless constructor. Remove the existing constructor or remove `[OwnedEntity]`. *(formerly `TRLSGEN102`)* |
| `TRLS038` | Error | `[OwnedEntity]` type must inherit from `ValueObject` | Emitted by the EF Core generator when `[OwnedEntity]` is applied to a type that does not inherit from `Trellis.ValueObject`. *(formerly `TRLSGEN103`)* |
| `TRLS039` | Warning | Unsupported scalar value primitive for AOT-safe JSON converter | Emitted by `ScalarValueJsonConverterGenerator` (Trellis.AspSourceGenerator) when a value object inherits from `ScalarValueObject<TSelf, TPrimitive>` with a `TPrimitive` outside the AOT-safe set (`string`, `int`, `long`, `short`, `byte`, `bool`, `float`, `double`, `decimal`, `Guid`, `DateTime`, `DateTimeOffset`). The generator skips the converter for that type to avoid emitting reflection-based `JsonSerializer.Deserialize`/`Serialize` calls (IL2026/IL3050 under `PublishAot=true`); provide a custom `JsonConverter<TSelf>` or pick a supported primitive. |

## Constants — `TrellisDiagnosticIds`

The public static class `Trellis.TrellisDiagnosticIds` (in the `Trellis.Analyzers` assembly) exposes every diagnostic ID above as a `public const string`. Use it from `[SuppressMessage]` attributes and rule sets to avoid magic strings:

```csharp
[SuppressMessage("Trellis", TrellisDiagnosticIds.UnsafeMaybeValueAccess,
    Justification = "guarded by HasValue check earlier in the pipeline")]
public string GetCity(Maybe<Address> address) => address.Value.City;
```

Generator IDs (`TRLS031`–`TRLS039`) are also exposed as constants on the same class so consumers have a single canonical reference for the unified namespace.

### Constant → diagnostic ID → emitter

Every `public const string` field on `TrellisDiagnosticIds`, the diagnostic ID it carries, and the analyzer (or generator) that emits it. Use the constant name in `[SuppressMessage]` and the diagnostic ID in `#pragma warning disable`.

| C# constant | Diagnostic ID | Emitted by |
| --- | --- | --- |
| `ResultNotHandled` | `TRLS001` | `ResultNotHandledAnalyzer` |
| `UseBindInsteadOfMap` | `TRLS002` | `UseBindInsteadOfMapAnalyzer` |
| `UnsafeMaybeValueAccess` | `TRLS003` | `UnsafeValueAccessAnalyzer` |
| `ResultDoubleWrapping` | `TRLS004` | `ResultDoubleWrappingAnalyzer` |
| `AsyncResultMisuse` | `TRLS005` | `AsyncResultMisuseAnalyzer` |
| `UseSpecificErrorType` | `TRLS006` | `ErrorBaseClassAnalyzer` |
| `MaybeDoubleWrapping` | `TRLS007` | `MaybeDoubleWrappingAnalyzer` |
| `UseResultCombine` | `TRLS008` | `UseResultCombineAnalyzer` |
| `UseAsyncMethodVariant` | `TRLS009` | `AsyncLambdaWithSyncMethodAnalyzer` |
| `ThrowInResultChain` | `TRLS010` | `ThrowInResultChainAnalyzer` |
| `EmptyErrorMessage` | `TRLS011` | `EmptyErrorMessageAnalyzer` |
| `ComparingToNull` | `TRLS012` | `ComparingToNullAnalyzer` |
| `UnsafeMaybeValueInLinq` | `TRLS013` | `UnsafeValueInLinqAnalyzer` |
| `CombineChainTooLong` | `TRLS014` | `CombineLimitAnalyzer` |
| `UseSaveChangesResult` | `TRLS015` | `UseSaveChangesResultAnalyzer` |
| `HasIndexMaybeProperty` | `TRLS016` | `HasIndexMaybePropertyAnalyzer` |
| `WrongAttributeNamespace` | `TRLS017` | `WrongAttributeNamespaceAnalyzer` |
| `UnsafeResultDeconstruction` | `TRLS018` | `UnsafeResultDeconstructionAnalyzer` |
| `DefaultResultOrMaybe` | `TRLS019` | `DefaultResultOrMaybeAnalyzer` |
| `CompositeValueObjectDtoMissingJsonConverter` | `TRLS020` | `CompositeValueObjectDtoConverterAnalyzer` |
| `RedundantEfConfiguration` | `TRLS021` | `RedundantEfConfigurationAnalyzer` |
| `UnsupportedRequiredBaseType` | `TRLS031` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `InvalidStringLengthRange` | `TRLS032` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `InvalidRangeMinExceedsMax` | `TRLS033` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `DecimalRangeExceedsDecimalRange` | `TRLS034` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `MaybePropertyShouldBePartial` | `TRLS035` | `MaybePartialPropertyGenerator` (Trellis.EntityFrameworkCore.Generator) |
| `OwnedEntityShouldBePartial` | `TRLS036` | `OwnedEntityGenerator` (Trellis.EntityFrameworkCore.Generator) |
| `OwnedEntityAlreadyHasParameterlessCtor` | `TRLS037` | `OwnedEntityGenerator` (Trellis.EntityFrameworkCore.Generator) |
| `OwnedEntityMustInheritValueObject` | `TRLS038` | `OwnedEntityGenerator` (Trellis.EntityFrameworkCore.Generator) |
| `UnsupportedScalarValuePrimitiveForAotJson` | `TRLS039` | `ScalarValueJsonConverterGenerator` (Trellis.AspSourceGenerator) |

## Descriptors — `DiagnosticDescriptors`

The public static class `Trellis.Analyzers.DiagnosticDescriptors` exposes one `public static readonly DiagnosticDescriptor` field per analyzer-emitted diagnostic. Analyzer implementations register these via `SupportedDiagnostics`; consumers normally don't reference them directly, but the field names are stable API and can be used in tests or in custom Roslyn tooling that re-exports the rules.

| Field | Backing ID | Default severity | Category |
| --- | --- | --- | --- |
| `ResultNotHandled` | `TRLS001` | Warning | Trellis.Result |
| `UseBindInsteadOfMap` | `TRLS002` | Info | Trellis.Result |
| `UnsafeMaybeValueAccess` | `TRLS003` | Warning | Trellis.Maybe |
| `ResultDoubleWrapping` | `TRLS004` | Warning | Trellis.Result |
| `AsyncResultMisuse` | `TRLS005` | Warning | Trellis.Result |
| `UseSpecificErrorType` | `TRLS006` | Info | Trellis.Error |
| `MaybeDoubleWrapping` | `TRLS007` | Warning | Trellis.Maybe |
| `UseResultCombine` | `TRLS008` | Info | Trellis.Result |
| `UseAsyncMethodVariant` | `TRLS009` | Warning | Trellis.Result |
| `ThrowInResultChain` | `TRLS010` | Warning | Trellis.Result |
| `EmptyErrorMessage` | `TRLS011` | Warning | Trellis.Error |
| `ComparingToNull` | `TRLS012` | Warning | Trellis.Result |
| `UnsafeValueInLinq` | `TRLS013` | Warning | Trellis.Maybe |
| `CombineChainTooLong` | `TRLS014` | Error | Trellis.Result |
| `UseSaveChangesResult` | `TRLS015` | Warning | Trellis.EntityFrameworkCore |
| `HasIndexMaybeProperty` | `TRLS016` | Warning | Trellis.EntityFrameworkCore |
| `WrongAttributeNamespace` | `TRLS017` | Warning | Trellis.Primitives |
| `UnsafeResultDeconstruction` | `TRLS018` | Warning | Trellis.Result |
| `DefaultResultOrMaybe` | `TRLS019` | Warning | Trellis.Result |
| `CompositeValueObjectDtoMissingJsonConverter` | `TRLS020` | Warning | Trellis.Asp |
| `RedundantEfConfiguration` | `TRLS021` | Warning | Trellis.EntityFrameworkCore |

> **Note:** Generator-emitted diagnostics (`TRLS031`–`TRLS039`) are constructed inline by the source generators and are *not* exposed as fields on `DiagnosticDescriptors`. Use the `TrellisDiagnosticIds` constants instead for those IDs.

```csharp
// Re-exporting an analyzer rule in a custom analyzer:
public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    ImmutableArray.Create(Trellis.Analyzers.DiagnosticDescriptors.ResultNotHandled);
```

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

#### `UnsafeValueAccessAnalyzer` — `TRLS003`
- `TRLS003`: flags `maybe.Value` when the analyzer cannot prove the access is guarded by presence checks.
- Recognized safe patterns include:
  - `if` / ternary checks on `HasValue` / `HasNoValue`
  - `TryGetValue` branches, including negated forms
  - `maybe.HasValue && maybe.Value ...` short-circuit
  - safe lambda parameters inside Trellis Maybe APIs such as `Bind`, `Map`, `Tap`, `Ensure`, `Match`
  - prior assignment from `Maybe.From(...)` when `T` is a non-nullable value type and the variable is not reassigned
- **Inside `Expression<Func<...>>` lambdas (EF Core, Specifications, FluentValidation):** the rule is *not* relaxed. Use the `HasValue && Value` short-circuit idiom — e.g. `e => e.SubmittedAt.HasValue && e.SubmittedAt.Value < cutoff`. EF Core needs the `HasValue` predicate to translate to `IS NOT NULL`, and the short-circuit form keeps the analyzer satisfied without `#pragma` suppressions.
- Code fix: `AddResultGuardCodeFixProvider`.

> **TRLS003, TRLS004 (removed in v2):** The `UnsafeValueAccessAnalyzer` previously also covered `Result<T>.Value` (TRLS003) and `Result<T>.Error` (TRLS004). Both branches were deleted because (a) `Result<T>.Value` no longer exists, and (b) `Result<T>.Error` is now `Error?`, so unsafe access is caught natively by C# nullable-reference-type analysis.

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

#### `TryCreateValueAccessAnalyzer` — `TRLS007` *(removed in v2)*

This analyzer was deleted in v2. The pattern `TryCreate(...).Value` no longer compiles because `Result<T>.Value` was removed (see TRLS003). Call `Create(...)` directly when the input is known-good, or handle the `Result` returned by `TryCreate(...)` explicitly via `TryGetValue` / `Match` / `Bind`.

#### `ResultDoubleWrappingAnalyzer` — `TRLS004`
- Flags declared or inferred `Result<Result<T>>` in:
  - variable declarations
  - properties
  - method return types
  - parameters
- Also flags `Result.Ok(existingResult)` and `Result.Fail(existingResult)` when the argument is already a `Result<T>`.
- No code fix.

#### `AsyncResultMisuseAnalyzer` — `TRLS005`
- Flags blocking access on `Task<Result<T>>` and `ValueTask<Result<T>>`:
  - `.Result`
  - `.Wait()`
  - `.GetAwaiter().GetResult()`
- Handles both `Task` and `ValueTask`.
- No code fix.

#### `MaybeDoubleWrappingAnalyzer` — `TRLS007`
- Flags declared `Maybe<Maybe<T>>` in variable declarations, properties, method return types, and parameters.
- No code fix.

#### `UseResultCombineAnalyzer` — `TRLS008`
- Flags conditional logic that manually combines two or more Result-state checks:
  - `&&` chains over `.IsSuccess`
  - `||` chains over `.IsFailure`
- Uses operation analysis, so it looks at semantic property access rather than raw text.
- No code fix.

#### `TernaryValueOrDefaultAnalyzer` — `TRLS013` *(removed in v2)*

This analyzer was deleted in v2. The `result.IsSuccess ? result.Value : fallback` shape no longer compiles because `Result<T>.Value` was removed. Use `result.GetValueOrDefault(fallback)` or `result.Match(onSuccess: v => v, onFailure: _ => fallback)`.

#### `AsyncLambdaWithSyncMethodAnalyzer` — `TRLS009`
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

#### `ThrowInResultChainAnalyzer` — `TRLS010`
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

#### `EmptyErrorMessageAnalyzer` — `TRLS011`
- Flags empty or whitespace-only `Detail` values supplied to `Error` cases. With v2's closed ADT there are no static factory methods; the analyzer inspects the `Detail` initializer on `new Error.X(...) { Detail = ... }` constructions.
- Recognizes `""`, whitespace string literals, interpolated strings containing only whitespace text, and `string.Empty`.
- No code fix.

> **Note:** The analyzer source still lists v1 factory names (`Validation`, `NotFound`, `Unauthorized`, `Forbidden`, `Conflict`, `Unexpected`); a follow-up pass updates the analyzer itself to inspect the `Detail` initializer on the closed-ADT cases.

#### `ComparingToNullAnalyzer` — `TRLS012`
- Flags `== null`, `!= null`, `is null`, and `is not null` when the non-null side is a Trellis `Result<T>` or `Maybe<T>`.
- Suggests `IsSuccess` / `IsFailure` for `Result<T>` and `HasValue` / `HasNoValue` for `Maybe<T>`.
- No code fix.

#### `UnsafeValueInLinqAnalyzer` — `TRLS013`
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
- Reports only when `.Value` is accessed on a `Maybe<T>` lambda parameter. The Result-side branch was removed in v2 along with `Result<T>.Value`.
- Suppresses the diagnostic when an earlier `.Where(maybe => maybe.HasValue)` in the same chain proves the access is safe.
- No code fix.

#### `CombineLimitAnalyzer` — `TRLS014`
- Flags the outermost `.Combine(...)` or `.CombineAsync(...)` chain when the resulting tuple would exceed 9 elements.
- Counts tuple width semantically, so chains continued through intermediate variables are still measured correctly.
- No code fix.

### Error, EF Core, and value-object rules

#### `ErrorBaseClassAnalyzer` — `TRLS006`
- Flags any attempt to construct the abstract base `Error` directly (which won't compile but is sometimes attempted via `new Error(...)` or implicit `new(...)` inference).
- Construct one of the nested cases instead: `new Error.NotFound(resource)`, `new Error.UnprocessableContent(fields)`, etc. See `docs/api_reference/trellis-api-core.md` for the full case catalog.
- No code fix.

#### `UseSaveChangesResultAnalyzer` — `TRLS015`
- Activates only when the compilation references `Trellis.EntityFrameworkCore.DbContextExtensions`.
- Flags direct `DbContext.SaveChangesAsync(...)` and `DbContext.SaveChanges(...)` calls, including unqualified calls inside a `DbContext` subclass.
- Recommends:
  - `SaveChangesResultAsync` when the return value is used
  - `SaveChangesResultUnitAsync` when the value is discarded
- Code fix: `UseSaveChangesResultCodeFixProvider`.

#### `HasIndexMaybePropertyAnalyzer` — `TRLS016`
- Activates only when the compilation references `Trellis.EntityFrameworkCore.MaybeConvention`.
- Flags `EntityTypeBuilder.HasIndex(...)` lambda members that reference `Maybe<T>` properties.
- Reports both the CLR property name and the generated storage-member fallback name (for example `_submittedAt`).
- No code fix.

#### `WrongAttributeNamespaceAnalyzer` — `TRLS017`
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

#### `UnsafeResultDeconstructionAnalyzer` — `TRLS018`
- Flags reads of the value position of a `Result<T>` deconstruction (`var (success, value, error) = result;`) when the read is not guarded by:
  - an `if`/`while`/conditional on the success bool,
  - an early-return on failure (`if (!success) return ...`),
  - a check that the error is `null`, or
  - the value being assigned to `_` (discard).
- Skips deconstructions where the value identifier is never read.
- No code fix.

#### `DefaultResultOrMaybeAnalyzer` — `TRLS019`
- Flags explicit `default(Result)`, `default(Result<T>)`, and `default(Maybe<T>)` expressions at use sites.
- Uses `IDefaultValueOperation` (operation-based, not syntax-based) so it covers all surface forms equivalently:
  - `default(T)` typeof-style: `return default(Result<int>);`
  - Target-typed `default`: `return default;` in a `Result<T>`-returning method, parameter defaults, etc.
  - Null-suppressed `default!`: `return default!;` is treated identically — the null-suppressing operator does not change the underlying value.
- Per ADR-002 §3.5.1, `default(Result)`/`default(Result<T>)` represent typed failures carrying the shared `new Error.Unexpected("default_initialized")` sentinel — *never* silent successes. `default(Maybe<T>)` equals `Maybe<T>.None` (semantically correct) but the explicit literal obscures intent.
- Suggested replacements:
  - `Result` → `Result.Ok()` or `Result.Fail(error)`
  - `Result<T>` → `Result.Ok(value)` or `Result.Fail<T>(error)`
  - `Maybe<T>` → `Maybe<T>.None` or `Maybe.From(value)`
- For sanctioned sentinel/test-helper sites, suppress with `[SuppressMessage("Trellis", "TRLS019", Justification = "...")]` on the enclosing member or `#pragma warning disable TRLS019` around the offending span.
- No code fix (the appropriate replacement depends on intent — success vs. failure for `Result`, value vs. None for `Maybe`).

#### `CompositeValueObjectDtoConverterAnalyzer` — `TRLS020`
- Flags ASP.NET controller request/response DTOs, Minimal API handler request DTOs, and Mediator `IRequest<T>`/`ICommand<T>`/`IQuery<T>` message DTOs with properties whose type is an `[OwnedEntity]` Trellis `ValueObject` missing `[JsonConverter(typeof(CompositeValueObjectJsonConverter<T>))]`.
- This catches the silent JSON-binding failure where System.Text.Json can default-construct the composite value object and bypass `TryCreate` validation.
- Does not flag domain model properties that are not exposed through DTO surfaces.
- Does not flag composite value-object types that carry the matching `CompositeValueObjectJsonConverter<T>` attribute.
- No code fix.

#### `RedundantEfConfigurationAnalyzer` — `TRLS021`
- Activates only when the compilation references `Trellis.EntityFrameworkCore.MaybeConvention`.
- Reports only when the source also wires Trellis conventions via `ApplyTrellisConventions(...)` or generated `ApplyTrellisConventionsFor<TContext>()`.
- Flags manual EF configuration for convention-owned properties:
  - `builder.Property(e => e.MaybeProperty).HasConversion(...)`
  - `builder.OwnsOne(e => e.OwnedEntityValueObject)`
  - `builder.Ignore(e => e.MaybeOrOwnedEntityProperty)`
- Targets `Maybe<T>` and types annotated with `Trellis.EntityFrameworkCore.OwnedEntityAttribute`.
- No code fix.

## Code fix providers

| Code fix provider | Fixes | Behavior |
|---|---|---|
| `AddResultGuardCodeFixProvider` | `TRLS003` | Wraps the current statement block in `if (maybe.HasValue)` and tracks consecutive statements that keep using the guarded value. |
| `UseBindInsteadOfMapCodeFixProvider` | `TRLS002` | Replaces `Map` with `Bind` and `MapAsync` with `BindAsync`. |
| `UseAsyncMethodVariantCodeFixProvider` | `TRLS009` | Replaces sync method names with async variants: `MapAsync`, `BindAsync`, `TapAsync`, `EnsureAsync`, `TapOnFailureAsync`. |
| `UseSaveChangesResultCodeFixProvider` | `TRLS015` | Replaces `SaveChangesAsync` / `SaveChanges` with `SaveChangesResultAsync` or `SaveChangesResultUnitAsync`, adds `await`/`async` for sync `SaveChanges`, and adds `using Trellis.EntityFrameworkCore;` when needed. |

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
        await dbContext.SaveChangesAsync(); // TRLS015
    }
}
```

## Cross-references

- [trellis-api-core.md](trellis-api-core.md) — `Result<T>`, `Maybe<T>`, `Bind`, `Map`, `Match`, `Combine`
- [trellis-api-efcore.md](trellis-api-efcore.md) — `SaveChangesResultAsync`, `SaveChangesResultUnitAsync`, `HasTrellisIndex`
- [trellis-api-primitives.md](trellis-api-primitives.md) — Trellis `[StringLength]` and `[Range]`
- [trellis-api-testing-reference.md](trellis-api-testing-reference.md) — testing helpers that intentionally work with analyzer rules
