---
package: Trellis.Analyzers
namespaces: [Trellis, Trellis.Analyzers]
types: [TrellisDiagnosticIds, DiagnosticDescriptors, ResultNotHandledAnalyzer, UseBindInsteadOfMapAnalyzer, UnsafeValueAccessAnalyzer, ResultDoubleWrappingAnalyzer, AsyncResultMisuseAnalyzer, MaybeDoubleWrappingAnalyzer, UseResultCombineAnalyzer, AsyncLambdaWithSyncMethodAnalyzer, ThrowInResultChainAnalyzer, UnsafeValueInLinqAnalyzer, CombineLimitAnalyzer, UseSaveChangesResultAnalyzer, HasIndexMaybePropertyAnalyzer, UnsafeResultDeconstructionAnalyzer, DefaultResultOrMaybeAnalyzer, CompositeValueObjectDtoConverterAnalyzer, RedundantEfConfigurationAnalyzer, OwnedEntityInitOnlyPropertyAnalyzer, AddResultGuardCodeFixProvider, UseBindInsteadOfMapCodeFixProvider, UseAsyncMethodVariantCodeFixProvider, UseSaveChangesResultCodeFixProvider, TRLS043, TRLS044, TRLS045, TRLS054, TRLS055, TRLS057, TRLS058]
version: v3
last_verified: 2026-06-03
audience: [llm]
---
# Trellis.Analyzers — API Reference

- **Package:** `Trellis.Analyzers`
- **Namespace:** `Trellis.Analyzers`
- **Purpose:** Roslyn analyzers and code fixes that enforce correct Trellis `Result<T>`, `Maybe<T>`, EF Core, and value-object usage.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#recipe-11--anti-pattern--fix-gallery-the-analyzers-in-action) — recipes using this package.

## Installation — the analyzers are opt-in

The analyzers ship as a **separate NuGet package, `Trellis.Analyzers`**. Installing `Trellis.Core` (or any other Trellis package) does **not** include them — every `TRLS###` diagnostic stays silent until you add this package. Enable it on each project that uses Trellis `Result<T>`, `Maybe<T>`, value objects, or the EF Core integration:

```xml
<PackageReference Include="Trellis.Analyzers" Version="...">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

- `PrivateAssets="all"` keeps the package a build-time-only concern so the analyzers do not flow to downstream consumers of your library.
- Tune any rule's severity per project through `.editorconfig`, e.g. `dotnet_diagnostic.TRLS001.severity = error`. Default severities are in the [Diagnostics](#diagnostics) table.
- In this repository's own source tree, examples reference the analyzer project directly with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` (see `Examples/Showcase/src/Showcase.Application`); published-package consumers use the `PackageReference` above.

## Use this file when

- A build emits a `TRLS###` diagnostic and you need the exact meaning, likely fix, or suppression constant.
- You are writing docs, templates, or examples and want analyzer-backed anti-pattern guidance.
- You need to map source-generator diagnostics (`TRLS031`+) to the owning generator.

## Patterns Index

| Symptom | Canonical fix | Diagnostic |
|---|---|---|
| Result return value ignored | Return, await, match, bind, or assign the result | `TRLS001` |
| Lambda returns `Result<T>` inside `Map` | Use `Bind` / `BindAsync` | `TRLS002` |
| `Maybe<T>.Value` access can throw | Gate with `HasValue`, use `TryGetValue`, convert to `Result`, or use EF helpers in queries | `TRLS003`, `TRLS013` |
| `Result<Result<T>>` or `Maybe<Maybe<T>>` appears | Use `Bind` / flatten the operation | `TRLS004`, `TRLS007` |
| Sync ROP method receives async lambda | Use the `*Async` variant | `TRLS009` |
| EF query over `Maybe<T>` uses unsafe value/sentinel access | Use `MaybeQueryableExtensions.WhereXxx` or register `AddTrellisInterceptors()` | `TRLS013` |
| EF query compares `Maybe<T>` via `.Equals(...)` / `object.Equals(...)` | Use `==` / `!=` operators or `MaybeQueryableExtensions.WhereEquals` | `TRLS054` |
| EF query calls `HasValueWhere` with a captured delegate or method group | Inline the lambda or materialize before applying the delegate | `TRLS055` |
| Direct `SaveChangesAsync` in non-UoW repository code | Use `SaveChangesResultAsync` / `SaveChangesResultUnitAsync`, or let `AddTrellisUnitOfWork<TContext>()` own commits | `TRLS015` |
| EF index points at a `Maybe<T>` CLR property | Use `HasTrellisIndex(...)` | `TRLS016` |
| `[OwnedEntity]` has init-only properties | Use `{ get; private set; }` for EF-owned value objects | `TRLS022` |

## Suppression guidance

Prefer fixing the code over suppressing diagnostics. When a suppression is genuinely intentional, use `TrellisDiagnosticIds` constants instead of string literals and include a justification.

## Diagnostics

| ID | Severity | Title | Description |
|----|----------|-------|-------------|
| `TRLS001` | Warning | Result return value is not handled | Result<T> return values should be handled to ensure errors are not silently ignored. Use Bind, Map, Match, or assign to a variable. |
| `TRLS002` | Info | Use Bind instead of Map when lambda returns Result | When the transformation function returns a Result<T>, use Bind (flatMap) instead of Map. Map will produce Result<Result<T>> which is likely not intended. |
| `TRLS003` | Error | Unsafe access to Maybe.Value | Maybe.Value throws an InvalidOperationException if the Maybe has no value. Check HasValue first, use TryGetValue, GetValueOrDefault, or convert to Result with ToResult. `Maybe<T>.Value` is hidden from IntelliSense as polish; this analyzer is the enforcement mechanism. |
| `TRLS004` | Warning | Result is double-wrapped | Result should not be wrapped inside another Result. This creates Result<Result<T>> which is almost always unintended. If combining Results, use Bind instead of Map. If wrapping a value, ensure it's not already a Result. |
| `TRLS005` | Warning | Incorrect async Result usage | Task<Result<T>> should be awaited, not blocked with .Result or .Wait(). Blocking can cause deadlocks and prevents proper async execution. Use await instead. |
| `TRLS007` | Warning | Maybe is double-wrapped | Maybe should not be wrapped inside another Maybe. This creates Maybe<Maybe<T>> which is almost always unintended. Avoid using Map when the transformation function returns a Maybe, as this creates double wrapping. Consider converting to Result with ToResult() for better composability. |
| `TRLS008` | Info | Consider using Result.Combine | When combining multiple Result<T> values, Result.Combine() or .Combine() chaining provides a cleaner and more maintainable approach than manually checking IsSuccess on each result. |
| `TRLS009` | Warning | Use async method variant for async lambda | When using async work with Map, Bind, Tap, or Ensure, use the async variant (MapAsync, BindAsync, etc.) and await the returned async pipeline. The code fix is only offered for simple, locally safe await insertions; convert synchronous void, value-returning, chained, nested, or delegate-changing scopes manually before applying it. |
| `TRLS010` | Warning | Don't throw exceptions in Result chains | Throwing exceptions inside Bind, Map, Tap, or Ensure lambdas defeats the purpose of Railway Oriented Programming. Return Result.Fail<T>() to signal errors and keep the error on the failure track. |
| `TRLS013` | Warning | Unsafe access to Maybe.Value in LINQ projection | `.Value` on `Maybe<T>` inside Select-family LINQ projections (`Select`/`SelectMany`/`OrderBy*`/`ThenBy*`/`GroupBy`/`ToDictionary`/`ToLookup`) throws for None elements unless an earlier `.Where(...)` lambda mentions `HasValue`. Suppression is **keyword-presence based**: predicate-shape verification (e.g., distinguishing `.Where(x => x.HasValue)` from `.Where(x => !x.HasValue)`) is a known limitation. For EF Core IQueryable predicates over a `Maybe<T>` property, either register `AddTrellisInterceptors()` (which rewrites `.HasValue`/`.Value`/`GetValueOrDefault(d)` into `EF.Property`/null-checks/`COALESCE`) or use `Trellis.EntityFrameworkCore.MaybeQueryableExtensions` (`WhereHasValue`/`WhereNone`/`WhereEquals`/`WhereLessThan`/`WhereLessThanOrEqual`/`WhereGreaterThan`/`WhereGreaterThanOrEqual`) explicitly. |
| `TRLS014` | Error | Combine chain exceeds maximum supported tuple size | Combine supports up to 9 elements. Downstream methods (Bind, Map, Tap, Match) also only support tuples up to 9 elements. Group related fields into intermediate value objects or sub-results, then combine those groups. |
| `TRLS015` | Warning | Use SaveChangesResultAsync instead of SaveChangesAsync | In non-UoW contexts, direct SaveChanges/SaveChangesAsync calls bypass the Result pipeline and turn database errors into unhandled exceptions; use `SaveChangesResultAsync` (returns `Result<int>`) or `SaveChangesResultUnitAsync` (returns `Result<Unit>`). Under `AddTrellisUnitOfWork<TContext>` the `TransactionalCommandBehavior` owns commit — repositories should stage changes via DbContext APIs (Add/Update/Remove) and not invoke SaveChanges at all. |
| `TRLS016` | Warning | HasIndex references a Maybe<T> property | HasIndex with a Maybe<T> property silently fails to create the index because MaybeConvention maps Maybe<T> via generated storage members, so the CLR property is invisible to EF Core's index builder. Prefer HasTrellisIndex so regular properties stay strongly typed and Maybe<T> properties resolve to their mapped storage automatically. If needed, you can also use string-based HasIndex with the storage member name directly. Examples: builder.HasTrellisIndex(e => new { e.Status, e.SubmittedAt }); or builder.HasIndex("Status", "_submittedAt"). |
| `TRLS018` | Warning | Result<T> deconstruction reads value without success gate | Reading the value position of a `Result<T>` deconstruction (`var (success, value, error) = result;`) without first checking `success`/`error` returns the default value when the result is in failure. Gate the read with the success bool, an `error is null` check, or an early return on failure. |
| `TRLS019` | Warning | Avoid `default(Result)`, `default(Result<T>)`, and `default(Maybe<T>)` | `default(Result)` and `default(Result<T>)` are typed failures carrying the `new Error.Unexpected("default_initialized")` sentinel — never silent successes. `default(Maybe<T>)` equals `Maybe<T>.None` but the explicit literal obscures intent. Construct via `Result.Ok(...)` / `Result.Fail(...)` or `Maybe<T>.None` / `Maybe.From(...)`. Suppress with `[SuppressMessage("Trellis", TrellisDiagnosticIds.DefaultResultOrMaybe)]` or `#pragma warning disable TRLS019` for sanctioned sentinel/test-helper sites. |
| `TRLS020` | Warning | Composite value object DTO property is not safely deserializable | Composite `[OwnedEntity]` value objects exposed through request/response DTO surfaces need a supported transport. Bare composite properties require `[JsonConverter(typeof(CompositeValueObjectJsonConverter<T>))]` on the value-object type; `Maybe<TComposite>` DTO properties are not supported and should use a nullable transport (`TComposite?`) plus `Maybe.From(...)` at the endpoint/API seam. |
| `TRLS021` | Warning | EF configuration duplicates Trellis conventions | Flags `HasConversion`, `OwnsOne`, and `Ignore` calls on `Maybe<T>` or `[OwnedEntity]` properties when `ApplyTrellisConventions(...)` / `ApplyTrellisConventionsFor<TContext>()` is wired. Remove the manual mapping and let Trellis conventions own the property. |
| `TRLS022` | Warning | `[OwnedEntity]` property uses init-only setter | Flags `{ get; init; }` properties on classes annotated with `[OwnedEntity]`. EF Core materializes owned entities through a generator-emitted private parameterless constructor; init-only setters are not covered by Trellis tests today and round-trip behavior is not guaranteed. Use `{ get; private set; }`. |
| `TRLS023` | Warning | Location route is missing the api-version route value | Flags `HttpResponseOptionsBuilder<T>.CreatedAtRoute(...)`, `HttpResponseOptionsBuilder<T>.CreatedAtAction(...)`, and `HttpResponseOptionsBuilder<T>.WithLocation(...)` calls inside `[ApiVersion]`-decorated controllers when the chain is not followed by `.WithVersionedRoute(...)` (or the underlying primitive `.WithRouteValueResolver("api-version", httpContext => ...)` on `HttpResponseOptionsBuilder<T>`, matched case-insensitively) and the route values dictionary literal does not include an `"api-version"` key. Without that key, the generated `Location` header omits the version under query/header API versioning and a follow-up `GET` to the dereferenced URL returns 404. The code fix appends `.WithVersionedRoute()` from `Trellis.Asp.ApiVersioning` and adds `using Trellis.Asp.ApiVersioning;` when missing. The analyzer matches `["api-version"]` keys case-insensitively (matching `RouteValueDictionary`'s runtime semantics) and resolves const-string identifiers via the semantic model. Recognises and warns on the anonymous-object ctor shape (`new RouteValueDictionary(new { id = ... })`) since C# property names cannot contain `"-"`, and on the single-id overloads (`CreatedAtRoute(routeName, idSelector)` / `WithLocation(routeName, idSelector)`) which construct a single-key dictionary internally. Does not walk attribute base-type chains (`[ApiVersion]` is `Inherited = false`). |
| `TRLS054` | Warning | Use operators instead of Maybe.Equals/object.Equals in IQueryable expressions | Flags `Maybe<T>.Equals(...)` and `object.Equals(...)` over `Maybe<T>` values inside `System.Linq.Queryable` LINQ lambdas. `MaybeExpressionRewriter` supports `==` / `!=` operator comparisons but cannot translate these opaque method-call shapes, so use operators or `MaybeQueryableExtensions.WhereEquals(...)`. In-memory `IEnumerable<T>` LINQ is not flagged. |
| `TRLS055` | Warning | Inline `HasValueWhere` predicates in `IQueryable` expressions | Flags `maybe.HasValueWhere(predicate)` inside `System.Linq.Queryable` LINQ lambdas when `predicate` is not an inline lambda expression. Captured `Func<T, bool>` variables, method groups, and member delegates are opaque to `MaybeExpressionRewriter`; inline the lambda (`maybe.HasValueWhere(x => ...)`) or materialize first. In-memory `IEnumerable<T>` LINQ is not flagged. |
| `TRLS031` | Warning | Unsupported base type for `RequiredPartialClassGenerator` | Emitted by the Primitives source generator when a `Required*`-derived value object inherits from an unsupported base. Supported bases: `RequiredGuid`, `RequiredString`, `RequiredInt`, `RequiredDecimal`, `RequiredLong`, `RequiredBool`, `RequiredDateTime`, `RequiredDateTimeOffset`, `RequiredEnum`. *(formerly `TRLSGEN001`)* |
| `TRLS032` | Error | `MinimumLength` exceeds `MaximumLength` | Emitted by the Primitives source generator when a `[StringLength]` attribute has `MinimumLength > MaximumLength`. Adjust the attribute values so the range is non-empty. *(formerly `TRLSGEN002`)* |
| `TRLS033` | Error | `Range` minimum exceeds maximum | Emitted by the Primitives source generator when a `[Range]` attribute on `int`/`long`/`decimal` has `Min > Max`. Adjust the attribute values so the range is non-empty. *(formerly `TRLSGEN003`)* |
| `TRLS034` | Error | Decimal range exceeds `decimal` bounds | Emitted by the Primitives source generator when a `[Range]` attribute on `decimal` exceeds the CLR `decimal` value range. Use a tighter range. *(formerly `TRLSGEN004`)* |
| `TRLS035` | Warning | `Maybe<T>` property should be `partial` | Emitted by the EF Core generator (`MaybePartialPropertyGenerator`) for non-partial auto-properties of type `Maybe<T>` whose containing type is `partial`. Declare the property `partial` so the generator can emit the backing field and storage member. *(formerly `TRLSGEN100`)* |
| `TRLS036` | Error | `[OwnedEntity]` type should be `partial` | Emitted by the EF Core generator (`OwnedEntityGenerator`) when `[OwnedEntity]` is applied to a non-partial type. Declare the type `partial` so the generator can emit the private parameterless constructor. *(formerly `TRLSGEN101`)* |
| `TRLS037` | Warning | `[OwnedEntity]` type already has a parameterless constructor | Emitted by the EF Core generator when `[OwnedEntity]` is applied to a type that already has a parameterless constructor. Remove the existing constructor or remove `[OwnedEntity]`. *(formerly `TRLSGEN102`)* |
| `TRLS038` | Error | `[OwnedEntity]` type must inherit from `ValueObject` | Emitted by the EF Core generator when `[OwnedEntity]` is applied to a type that does not inherit from `Trellis.ValueObject`. *(formerly `TRLSGEN103`)* |
| `TRLS039` | Warning | Unsupported scalar value primitive for AOT-safe JSON converter | Emitted by `ScalarValueJsonConverterGenerator` (Trellis.AspSourceGenerator) when a value object inherits from `ScalarValueObject<TSelf, TPrimitive>` with a `TPrimitive` outside the AOT-safe set (`string`, `int`, `long`, `short`, `byte`, `bool`, `float`, `double`, `decimal`, `Guid`, `DateTime`, `DateTimeOffset`). The generator skips the converter for that type to avoid emitting reflection-based `JsonSerializer.Deserialize`/`Serialize` calls (IL2026/IL3050 under `PublishAot=true`); provide a custom `JsonConverter<TSelf>` or pick a supported primitive. |
| `TRLS043` | Error | Numeric convenience attribute on non-numeric Required base | Emitted by `RequiredPartialClassGenerator` when `[Positive]`, `[NonNegative]`, `[Negative]`, or `[NonPositive]` is applied to a Required base other than `RequiredInt`, `RequiredLong`, or `RequiredDecimal`. |
| `TRLS044` | Error | Conflicting numeric convenience attributes | Emitted by `RequiredPartialClassGenerator` when a class carries more than one of `[Positive]`, `[NonNegative]`, `[Negative]`, and `[NonPositive]`. Pick exactly one sign constraint. |
| `TRLS045` | Error | Numeric convenience attribute combined with explicit `[Range]` | Emitted by `RequiredPartialClassGenerator` when a numeric convenience sign attribute is combined with an explicit `[Range]` on a numeric Required base. Use `[Range]` alone for bounded values, or the convenience attribute alone for an unbounded sign constraint. |
| `TRLS056` | Error | Required generated member conflicts with user declaration | Emitted by `RequiredPartialClassGenerator` when a `Required*<TSelf>` partial class declares a member that Trellis would generate (`TryCreate`, `Create`, `Parse`, `TryParse`, GUID factories, conversion operator, constructor, or validation hook). The generator skips the conflicting member so the user declaration wins and reports this diagnostic at the user member declaration. Remove the redundant member, or change the base class if custom semantics are required. |
| `TRLS057` | Error | `[Trim]` on a non-string Required base | Emitted by `RequiredPartialClassGenerator` when `[Trim]` is applied to a Required base other than `RequiredString`. `[Trim]` only affects string trimming; on any other base it is silently ignored, so the generator refuses to emit code. Remove the attribute. |
| `TRLS058` | Error | `[NotDefault]` on a sentinel-less Required base | Emitted by `RequiredPartialClassGenerator` when `[NotDefault]` is applied to `RequiredBool` or `RequiredEnum`. Those bases have no meaningful default sentinel to reject (every value is valid), so the attribute is a no-op. Remove the attribute. |

## Constants — `TrellisDiagnosticIds`

The public static class `Trellis.TrellisDiagnosticIds` (in the `Trellis.Analyzers` assembly) exposes every diagnostic ID above as a `public const string`. Use it from `[SuppressMessage]` attributes and rule sets to avoid magic strings:

```csharp
[SuppressMessage("Trellis", TrellisDiagnosticIds.UnsafeMaybeValueAccess,
    Justification = "guarded by HasValue check earlier in the pipeline")]
public string GetCity(Maybe<Address> address) => address.Value.City;
```

Generator IDs (`TRLS031`–`TRLS045`, `TRLS056`–`TRLS058`) and the LINQ-analyzer IDs (`TRLS054`–`TRLS055`) are also exposed as constants on the same class so consumers have a single canonical reference for the unified namespace.

### Constant → diagnostic ID → emitter

Every `public const string` field on `TrellisDiagnosticIds`, the diagnostic ID it carries, and the analyzer (or generator) that emits it. Use the constant name in `[SuppressMessage]` and the diagnostic ID in `#pragma warning disable`.

| C# constant | Diagnostic ID | Emitted by |
| --- | --- | --- |
| `ResultNotHandled` | `TRLS001` | `ResultNotHandledAnalyzer` |
| `UseBindInsteadOfMap` | `TRLS002` | `UseBindInsteadOfMapAnalyzer` |
| `UnsafeMaybeValueAccess` | `TRLS003` | `UnsafeValueAccessAnalyzer` |
| `ResultDoubleWrapping` | `TRLS004` | `ResultDoubleWrappingAnalyzer` |
| `AsyncResultMisuse` | `TRLS005` | `AsyncResultMisuseAnalyzer` |
| `MaybeDoubleWrapping` | `TRLS007` | `MaybeDoubleWrappingAnalyzer` |
| `UseResultCombine` | `TRLS008` | `UseResultCombineAnalyzer` |
| `UseAsyncMethodVariant` | `TRLS009` | `AsyncLambdaWithSyncMethodAnalyzer` |
| `ThrowInResultChain` | `TRLS010` | `ThrowInResultChainAnalyzer` |
| `UnsafeMaybeValueInLinq` | `TRLS013` | `UnsafeValueInLinqAnalyzer` |
| `CombineChainTooLong` | `TRLS014` | `CombineLimitAnalyzer` |
| `UseSaveChangesResult` | `TRLS015` | `UseSaveChangesResultAnalyzer` |
| `HasIndexMaybeProperty` | `TRLS016` | `HasIndexMaybePropertyAnalyzer` |
| `UnsafeResultDeconstruction` | `TRLS018` | `UnsafeResultDeconstructionAnalyzer` |
| `DefaultResultOrMaybe` | `TRLS019` | `DefaultResultOrMaybeAnalyzer` |
| `CompositeValueObjectDtoMissingJsonConverter` | `TRLS020` | `CompositeValueObjectDtoConverterAnalyzer` |
| `RedundantEfConfiguration` | `TRLS021` | `RedundantEfConfigurationAnalyzer` |
| `OwnedEntityInitOnlyProperty` | `TRLS022` | `OwnedEntityInitOnlyPropertyAnalyzer` |
| `MissingApiVersionRouteValue` | `TRLS023` | `CreatedAtRouteMissingApiVersionAnalyzer` |
| `MaybeEqualsInQueryable` | `TRLS054` | `UnsafeValueInLinqAnalyzer` |
| `NonInlineHasValueWhereInQueryable` | `TRLS055` | `UnsafeValueInLinqAnalyzer` |
| `UnsupportedRequiredBaseType` | `TRLS031` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `InvalidStringLengthRange` | `TRLS032` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `InvalidRangeMinExceedsMax` | `TRLS033` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `DecimalRangeExceedsDecimalRange` | `TRLS034` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `MaybePropertyShouldBePartial` | `TRLS035` | `MaybePartialPropertyGenerator` (Trellis.EntityFrameworkCore.Generator) |
| `OwnedEntityShouldBePartial` | `TRLS036` | `OwnedEntityGenerator` (Trellis.EntityFrameworkCore.Generator) |
| `OwnedEntityAlreadyHasParameterlessCtor` | `TRLS037` | `OwnedEntityGenerator` (Trellis.EntityFrameworkCore.Generator) |
| `OwnedEntityMustInheritValueObject` | `TRLS038` | `OwnedEntityGenerator` (Trellis.EntityFrameworkCore.Generator) |
| `UnsupportedScalarValuePrimitiveForAotJson` | `TRLS039` | `ScalarValueJsonConverterGenerator` (Trellis.AspSourceGenerator) |
| `NumericConvenienceOnNonNumeric` | `TRLS043` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `NumericConvenienceConflict` | `TRLS044` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `NumericConvenienceWithExplicitRange` | `TRLS045` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `GeneratedRequiredMemberCollision` | `TRLS056` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `TrimOnNonStringBase` | `TRLS057` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `NotDefaultOnSentinellessBase` | `TRLS058` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |

## Descriptors — `DiagnosticDescriptors`

The public static class `Trellis.Analyzers.DiagnosticDescriptors` exposes one `public static readonly DiagnosticDescriptor` field per analyzer-emitted diagnostic. Analyzer implementations register these via `SupportedDiagnostics`; consumers normally don't reference them directly, but the field names are stable API and can be used in tests or in custom Roslyn tooling that re-exports the rules.

Every descriptor uses the single shared category `Trellis` (defined as `private const string Category = "Trellis";` in `DiagnosticDescriptors`). Configure rule severities in `.editorconfig` or rule sets via the diagnostic ID (`dotnet_diagnostic.TRLS013.severity = warning`) rather than by category, since the category does not differentiate between Result, Maybe, EF Core, and ASP.NET Core rules today.

| Field | Backing ID | Default severity |
| --- | --- | --- |
| `ResultNotHandled` | `TRLS001` | Warning |
| `UseBindInsteadOfMap` | `TRLS002` | Info |
| `UnsafeMaybeValueAccess` | `TRLS003` | Error |
| `ResultDoubleWrapping` | `TRLS004` | Warning |
| `AsyncResultMisuse` | `TRLS005` | Warning |
| `MaybeDoubleWrapping` | `TRLS007` | Warning |
| `UseResultCombine` | `TRLS008` | Info |
| `UseAsyncMethodVariant` | `TRLS009` | Warning |
| `ThrowInResultChain` | `TRLS010` | Warning |
| `UnsafeMaybeValueInLinq` | `TRLS013` | Warning |
| `CombineChainTooLong` | `TRLS014` | Error |
| `UseSaveChangesResult` | `TRLS015` | Warning |
| `HasIndexMaybeProperty` | `TRLS016` | Warning |
| `UnsafeResultDeconstruction` | `TRLS018` | Warning |
| `DefaultResultOrMaybe` | `TRLS019` | Warning |
| `CompositeValueObjectDtoMissingJsonConverter` | `TRLS020` | Warning |
| `RedundantEfConfiguration` | `TRLS021` | Warning |
| `OwnedEntityInitOnlyProperty` | `TRLS022` | Warning |
| `MissingApiVersionRouteValue` | `TRLS023` | Warning |
| `MaybeEqualsInQueryable` | `TRLS054` | Warning |
| `NonInlineHasValueWhereInQueryable` | `TRLS055` | Warning |

> **Note:** The TRLS013 descriptor was originally exposed as `UnsafeValueInLinq`. The current canonical name is `UnsafeMaybeValueInLinq` (matching the `TrellisDiagnosticIds.UnsafeMaybeValueInLinq` constant); the old name is retained as an `[Obsolete]` alias pointing at the same `DiagnosticDescriptor` instance for backward compatibility. New code should reference `UnsafeMaybeValueInLinq`.

> **Note:** Generator-emitted diagnostics (`TRLS031`–`TRLS045` and `TRLS056`–`TRLS058`) are constructed inline by the source generators and are *not* exposed as fields on `DiagnosticDescriptors`. Use the `TrellisDiagnosticIds` constants instead for those IDs.

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
- **Inside `Expression<Func<...>>` lambdas (EF Core, Specifications, FluentValidation):** the rule is *not* relaxed. The analyzer recognizes the immediate short-circuit shape `e.SubmittedAt.HasValue && e.SubmittedAt.Value < cutoff`; when the `Maybe<T>` check is part of a longer predicate, keep that pair parenthesized or prefer an analyzer-clean sentinel form such as `e.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) < cutoff`. For ad-hoc EF `IQueryable<T>` queries, prefer the `MaybeQueryableExtensions.WhereXxx` helpers when one matches the predicate.
- Code fix: `AddResultGuardCodeFixProvider`.

> **Result accessors:** The `UnsafeValueAccessAnalyzer` previously also covered `Result<T>.Value` and `Result<T>.Error`. Both branches were deleted because (a) `Result<T>.Value` no longer exists, and (b) `Result<T>.Error` is now `Error?`, so unsafe access is caught natively by C# nullable-reference-type analysis.

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
- Code fix: `UseAsyncMethodVariantCodeFixProvider` renames the method, awaits the rewritten call, and adds `async` to Task/ValueTask-returning methods when it can do so without changing surrounding expressions or other returns. No fix is offered for chained invocations, nested wrapper expressions, non-async lambdas, used `var` locals, synchronous `void` methods, synchronous value-returning methods, direct returns from already-async scopes, methods/local functions with `ref`/`out`/`in` parameters, or Task/ValueTask-returning scopes with other task-returning `return` statements because those scopes must be converted manually first.

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

#### `UnsafeValueInLinqAnalyzer` — `TRLS013`, `TRLS054`, `TRLS055`
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
- Reports only when `.Value` is accessed on a `Maybe<T>` lambda parameter. The Result-side branch was removed along with `Result<T>.Value`.
- Suppresses the diagnostic when an earlier `.Where(...)` clause **mentions** `HasValue` anywhere in its lambda body. This is **keyword-presence detection**, not predicate-shape verification: `.Where(x => !x.HasValue).Select(x => x.Value)` (filtering down to None elements before reading their value) silences the diagnostic but still throws at runtime. Tightening the suppression to only honor `Where(x => x.HasValue)`-shaped predicates is a known limitation tracked separately.
- For EF Core IQueryable predicates over a `Maybe<T>` property, either register `AddTrellisInterceptors()` (which rewrites `.HasValue`/`.Value`/`GetValueOrDefault(d)` into `EF.Property`/null-checks/`COALESCE`) or use `Trellis.EntityFrameworkCore.MaybeQueryableExtensions` (`WhereHasValue`/`WhereNone`/`WhereEquals`/`WhereLessThan`/`WhereLessThanOrEqual`/`WhereGreaterThan`/`WhereGreaterThanOrEqual`) explicitly. Note: this analyzer only fires on Select-family methods today; coverage of `.Where`/`.Any`/`.First` etc. is tracked as a follow-up.
- `TRLS054`: inside `System.Linq.Queryable` lambdas, flags `Maybe<T>.Equals(...)` and `object.Equals(...)` when a `Maybe<T>` operand is involved. Use the natural `==` / `!=` operators (which `MaybeExpressionRewriter` understands) or `MaybeQueryableExtensions.WhereEquals(...)`.
- `TRLS055`: inside `System.Linq.Queryable` lambdas, flags `HasValueWhere(...)` when the predicate argument is not an inline lambda. Captured `Func<T, bool>` variables, method groups, and member delegates cannot be inspected by the EF Core rewriter.
- No code fix.

#### `CombineLimitAnalyzer` — `TRLS014`
- Flags the outermost `.Combine(...)` or `.CombineAsync(...)` chain when the resulting tuple would exceed 9 elements.
- Counts tuple width semantically, so chains continued through intermediate variables are still measured correctly.
- No code fix.

### Error, EF Core, and value-object rules

#### `UseSaveChangesResultAnalyzer` — `TRLS015`
- Activates only when the compilation references `Trellis.EntityFrameworkCore.DbContextExtensions`.
- Flags direct `DbContext.SaveChangesAsync(...)` and `DbContext.SaveChanges(...)` calls, including unqualified calls inside a `DbContext` subclass.
- Recommends (non-UoW contexts):
  - `SaveChangesResultAsync` when the return value is used
  - `SaveChangesResultUnitAsync` when the value is discarded
- Under `AddTrellisUnitOfWork<TContext>` the `TransactionalCommandBehavior` owns commit; repositories should stage changes via DbContext APIs (`Add`/`Update`/`Remove`) and not invoke `SaveChanges`/`SaveChangesAsync` at all.
- Code fix: `UseSaveChangesResultCodeFixProvider`.

#### `HasIndexMaybePropertyAnalyzer` — `TRLS016`
- Activates only when the compilation references `Trellis.EntityFrameworkCore.MaybeConvention`.
- Flags `EntityTypeBuilder.HasIndex(...)` lambda members that reference `Maybe<T>` properties.
- Reports both the CLR property name and the generated storage-member fallback name (for example `_submittedAt`).
- No code fix.

#### `UnsafeResultDeconstructionAnalyzer` — `TRLS018`
- Flags reads of the value position of a `Result<T>` deconstruction (`var (success, value, error) = result;`) when the read is not guarded by:
  - an `if`/`while`/conditional on the success bool,
  - an early-return on failure (`if (!success) return ...`),
  - a check that the error is `null`, or
  - the value being assigned to `_` (discard).
- Skips deconstructions where the value identifier is never read.
- For the **assignment-form** deconstruction `(success, value, error) = result;` (which writes into existing locals rather than declaring fresh ones), only structural guards and early-returns whose **condition is authored after** the deconstruction assignment are accepted as proof of safety. A pre-existing `if (!success) return;` authored before the assignment is rejected because the locals' pre-assignment values may be stale and unrelated to the freshly produced result triple.
- No code fix.

#### `DefaultResultOrMaybeAnalyzer` — `TRLS019`
- Flags explicit `default(Result)`, `default(Result<T>)`, and `default(Maybe<T>)` expressions at use sites.
- Uses `IDefaultValueOperation` (operation-based, not syntax-based) so it covers all surface forms equivalently:
  - `default(T)` typeof-style: `return default(Result<int>);`
  - Target-typed `default`: `return default;` in a `Result<T>`-returning method, parameter defaults, etc.
  - Null-suppressed `default!`: `return default!;` is treated identically — the null-suppressing operator does not change the underlying value.
- `default(Result)`/`default(Result<T>)` represent typed failures carrying the shared `new Error.Unexpected("default_initialized")` sentinel — *never* silent successes. `default(Maybe<T>)` equals `Maybe<T>.None` (semantically correct) but the explicit literal obscures intent.
- Suggested replacements:
  - `Result` → `Result.Ok()` or `Result.Fail(error)`
  - `Result<T>` → `Result.Ok(value)` or `Result.Fail<T>(error)`
  - `Maybe<T>` → `Maybe<T>.None` or `Maybe.From(value)`
- For sanctioned sentinel/test-helper sites, suppress with `[SuppressMessage("Trellis", "TRLS019", Justification = "...")]` on the enclosing member or `#pragma warning disable TRLS019` around the offending span.
- No code fix (the appropriate replacement depends on intent — success vs. failure for `Result`, value vs. None for `Maybe`).

#### `CompositeValueObjectDtoConverterAnalyzer` — `TRLS020`
- Flags ASP.NET controller request/response DTOs, Minimal API handler request DTOs, and Mediator `IRequest<T>`/`ICommand<T>`/`IQuery<T>` message DTOs with properties whose type is an `[OwnedEntity]` Trellis `ValueObject` missing `[JsonConverter(typeof(CompositeValueObjectJsonConverter<T>))]`.
- Also flags properties typed `Maybe<TComposite>` where `TComposite` is an `[OwnedEntity]` `ValueObject`. **`Maybe<TComposite>` is always flagged**, even when the inner `TComposite` carries `[JsonConverter(typeof(CompositeValueObjectJsonConverter<TComposite>))]`: that converter operates on `TComposite`, not on `Maybe<TComposite>`. Trellis ships no `MaybeCompositeValueObjectJsonConverterFactory`, so STJ falls back to default construction of the inner type and wraps it in `Maybe.From`, silently bypassing `TryCreate`. The supported DTO transport per cookbook Recipe 14 is `TComposite?` plus `Maybe.From(...)` at the endpoint/API seam — applicable to controller actions, Minimal API handlers, and Mediator message-construction sites.
- This catches the silent JSON-binding failure where System.Text.Json can default-construct the composite value object and bypass `TryCreate` validation.
- Does not flag domain model properties that are not exposed through DTO surfaces.
- Does not flag bare composite value-object types that carry the matching `CompositeValueObjectJsonConverter<T>` attribute.
- Scope is bounded to owned composite value objects. `Maybe<TScalarValueObject>` (where `TScalarValueObject : IScalarValue<,>`) is handled by `MaybeScalarValueJsonConverterFactory` and is out of scope for this analyzer. `Maybe<int>` / `Maybe<string>` / `Maybe<Guid>` / `Maybe<DateTime>` (primitive inner types in the closed allowed list) are handled by `MaybePrimitiveJsonConverterFactory` since Trellis #506 (`AddScalarValueValidation()` — or the convenience `AddTrellisAspWithScalarValidation()` — registers it); also out of scope for this analyzer because the JSON round-trip is correct by construction. Primitive inner types outside the allowed list (`DateOnly`, `TimeOnly`, unsigned numerics) remain unsupported and use the wire-shape DTO seam per Recipe 14 — also out of scope here for the same correctness-bug-class boundary.
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

#### `OwnedEntityInitOnlyPropertyAnalyzer` — `TRLS022`
- Activates only when the compilation references `Trellis.EntityFrameworkCore.OwnedEntityAttribute`.
- Flags property declarations with an `init` accessor whose containing class is annotated with `[OwnedEntity]`.
- Diagnostic anchors at the `init` keyword and includes the property name and class name.
- Recommends `{ get; private set; }`, the supported and tested shape for owned-entity properties materialized through the generator-emitted parameterless constructor.
- No code fix.

#### `RequiredPartialClassGenerator` Required-attribute diagnostics — `TRLS043`–`TRLS045`
- `TRLS043` (Error): numeric convenience attribute (`[Positive]`, `[NonNegative]`, `[Negative]`, `[NonPositive]`) applied to a non-numeric Required base.
- `TRLS044` (Error): more than one numeric convenience attribute is present on the same Required type.
- `TRLS045` (Error): a numeric convenience attribute is combined with an explicit `[Range]`; pick one shape.
- No code fix.

#### `RequiredPartialClassGenerator` member collision — `TRLS056`
- Flags user-declared members on `Required*<TSelf>` partial classes that collide with members generated by `RequiredPartialClassGenerator`.
- Severity: Error. The generator suppresses only the conflicting generated member and reports at the user member location, replacing generic `CS0111` / `CS0102` duplicate-member failures with a Trellis-specific message.
- Fix: delete the redundant declaration and rely on the generated member, or stop deriving from the Required base if the type needs fully custom semantics.
- No code fix.

#### `RequiredPartialClassGenerator` opt-in attribute placement — `TRLS057`–`TRLS058`
- `TRLS057` (Error): `[Trim]` applied to a Required base other than `RequiredString`. `[Trim]` only drives string trimming, so on any other base it is silently ignored — remove it.
- `TRLS058` (Error): `[NotDefault]` applied to `RequiredBool` or `RequiredEnum`. Those bases have no meaningful default sentinel to reject (every value is valid), so the attribute is a no-op — remove it.
- The generator refuses to emit code for the type until the misplaced attribute is removed, surfacing the mistake at build time even when the analyzer is disabled.
- No code fix.

#### `CreatedAtRouteMissingApiVersionAnalyzer` — `TRLS023`
- Activates only inside controllers/types annotated with `[ApiVersion]` (and not `[ApiVersionNeutral]`). `[ApiVersion]` is declared with `Inherited = false`, so the analyzer inspects the immediate type only — derived controllers without their own `[ApiVersion]` are ignored.
- Triggered by `HttpResponseOptionsBuilder<T>.CreatedAtRoute(routeName, routeValues)`, `CreatedAtAction(actionName, routeValues, controllerName)`, and `WithLocation(routeName, routeValues)` invocations whose route-values dictionary does not include an `"api-version"` key.
- Recognised dictionary shapes:
  - `c => new RouteValueDictionary { ["id"] = c.Id, ["api-version"] = "..." }` — initializer block.
  - `c => new RouteValueDictionary { ["id"] = c.Id, [ApiVersionKey] = "..." }` — initializer block with const-string key (resolved via the semantic model).
  - `c => { return new RouteValueDictionary { ... }; }` — block-bodied lambda with a return statement.
  - `c => new RouteValueDictionary(new { id = c.Id })` — anonymous-object ctor shape; **always** flagged because C# property names cannot contain `"-"`, so the api-version key is necessarily missing.
- Key matching is case-insensitive (matches `RouteValueDictionary`'s runtime semantics): `"API-VERSION"`, `"Api-Version"`, etc., are all accepted.
- Suppression also applies when the fluent chain calls the underlying primitive directly: `HttpResponseOptionsBuilder<T>.WithRouteValueResolver("api-version", httpContext => ...)`. The key match is case-insensitive (so `"API-Version"` also suppresses) and the symbol is gated to `Trellis.Asp.HttpResponseOptionsBuilder<T>` — a same-named extension on an unrelated type does not silence the diagnostic.
- Code fix: appends `.WithVersionedRoute()` to the flagged `CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)` call (the `Trellis.Asp.ApiVersioning` extension) and adds `using Trellis.Asp.ApiVersioning;` when missing. The new `using` is inserted in the same scope as existing usings (file-scoped namespace, block-scoped namespace, or top-level).

## Code fix providers

| Code fix provider | Fixes | Behavior |
|---|---|---|
| `AddResultGuardCodeFixProvider` | `TRLS003` | Wraps the current statement block in `if (maybe.HasValue)` and tracks consecutive statements that keep using the guarded value. |
| `UseBindInsteadOfMapCodeFixProvider` | `TRLS002` | Replaces `Map` with `Bind` and `MapAsync` with `BindAsync`. |
| `UseAsyncMethodVariantCodeFixProvider` | `TRLS009` | Replaces sync method names with async variants, awaits the rewritten call, and adds `async` to Task/ValueTask-returning methods when that is locally safe. It withholds the fix for chained/nested calls and scopes that require manual delegate, parameter, or return-flow changes. |
| `UseSaveChangesResultCodeFixProvider` | `TRLS015` | Replaces `SaveChangesAsync` / `SaveChanges` with `SaveChangesResultAsync` or `SaveChangesResultUnitAsync`, adds `await`/`async` for sync `SaveChanges`, and adds `using Trellis.EntityFrameworkCore;` when needed. |
| `CreatedAtRouteMissingApiVersionCodeFixProvider` | `TRLS023` | Appends `.WithVersionedRoute()` to the flagged `CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)` call (so the chain becomes `<original>.WithVersionedRoute()`) and inserts `using Trellis.Asp.ApiVersioning;` in the same scope as existing usings (file-scoped namespace, block-scoped namespace, or top-level) when missing. |

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

- [trellis-api-core.md](trellis-api-core.md#result-pipeline-extension-families) — `Result<T>`, `Maybe<T>`, `Bind`, `Map`, `Match`, `Combine`
- [trellis-api-efcore.md](trellis-api-efcore.md#extension-methods) — `SaveChangesResultAsync`, `SaveChangesResultUnitAsync`, `HasTrellisIndex`
- [trellis-api-primitives.md](trellis-api-primitives.md#trellis-validation-attributes-vs-systemcomponentmodeldataannotations) — Trellis `[StringLength]` and `[Range]`
- [trellis-api-testing-reference.md](trellis-api-testing-reference.md#common-traps) — testing helpers that intentionally work with analyzer rules
