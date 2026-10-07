---
package: Trellis.Analyzers
namespaces: [Trellis, Trellis.Analyzers]
types: [TrellisDiagnosticIds, EmptyReasonCodeOverride, ValidateAdditionalOverloadConflict, UnnamedValidateAdditionalFailure, MustWithoutErrorCode, DiagnosticDescriptors, ResultNotHandledAnalyzer, UseBindInsteadOfMapAnalyzer, UnsafeValueAccessAnalyzer, ResultDoubleWrappingAnalyzer, AsyncResultMisuseAnalyzer, MaybeDoubleWrappingAnalyzer, UseResultCombineAnalyzer, AsyncLambdaWithSyncMethodAnalyzer, ThrowInResultChainAnalyzer, UnsafeValueInLinqAnalyzer, CombineLimitAnalyzer, UseSaveChangesResultAnalyzer, HasIndexMaybePropertyAnalyzer, UnsafeResultDeconstructionAnalyzer, DefaultResultOrMaybeAnalyzer, CompositeValueObjectDtoConverterAnalyzer, RedundantEfConfigurationAnalyzer, OwnedEntityInitOnlyPropertyAnalyzer, MustWithoutErrorCodeAnalyzer, AddResultGuardCodeFixProvider, UseBindInsteadOfMapCodeFixProvider, UseAsyncMethodVariantCodeFixProvider, UseSaveChangesResultCodeFixProvider, TRLS043, TRLS044, TRLS045, TRLS054, TRLS055, TRLS057, TRLS058, TRLS062, TRLS063, TRLS064, ReasonCodeVocabulary, ReasonCodeVocabularyAnalyzer, ReasonCodeVocabularyCodeFixProvider, TRLS065, ProducesClobbersProblemDetails, ProducesClobbersProblemDetailsAnalyzer, TRLS066, UseEnsureNotNullForNullable, UseEnsureNotNullForNullableAnalyzer, UseEnsureNotNullForNullableCodeFixProvider]
version: v3
last_verified: 2026-10-07
audience: [llm]
agent_usage: onDemand
agent_description: "Open when a TRLS diagnostic appears or when checking which analyzer rules apply: rule ids, severities and the Trellis.Analyzers opt-in."
---
# Trellis.Analyzers — API Reference

> **Analyzer rules require `Trellis.Analyzers`; bundled generator diagnostics do not.** Add a `PackageReference` to `Trellis.Analyzers` for the analyzer-emitted rules below. Source-generator diagnostics ship with their hosting packages (`Trellis.Core`, `Trellis.EntityFrameworkCore`, and `Trellis.Asp`) and can fire without that reference. This file is delivered by `Trellis.Core`, so its presence in `.github/` does **not** mean the standalone analyzers are active — check the `.csproj` and the emitter table below.

- **Package:** `Trellis.Analyzers`
- **Namespace:** `Trellis.Analyzers`
- **Purpose:** Roslyn analyzers and code fixes that enforce correct Trellis `Result<T>`, `Maybe<T>`, EF Core, and value-object usage.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#recipe-11--anti-pattern--fix-gallery-the-analyzers-in-action) — recipes using this package.

## Installation — the analyzers are opt-in

The standalone analyzers ship as a **separate NuGet package, `Trellis.Analyzers`**. Installing `Trellis.Core` (or any other Trellis package) does **not** include that package. This is separate from the source generators bundled with Core, EF Core, and ASP: their `TRLS###` diagnostics are active with the hosting package alone. Enable the standalone analyzers on each project that uses Trellis `Result<T>`, `Maybe<T>`, value objects, or the EF Core integration:

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

Prefer fixing the code over suppressing diagnostics. When a suppression is genuinely intentional, use the literal diagnostic ID (for example, `"TRLS003"`) and include a justification. The analyzer-only NuGet package does not expose `TrellisDiagnosticIds` as a compile-time type to application code.

In test projects, `TRLS001` and `TRLS015` are the rules most likely to need tuning — intentional fire-and-forget calls in *arrange*, and `DbContext.SaveChangesAsync()` used for seeding. Idiomatic assertions and explicit discards (`_ = ...`) already satisfy `TRLS001`. To relax a rule across scattered test projects, apply a shared global analyzer config (`is_global = true`) to every `*.Tests` project from the root `Directory.Build.props` rather than a project-wide `<NoWarn>`. See [test-context guidance](trellis-api-testing-reference.md#common-traps).

## Diagnostics

Use this index to locate the rule. Follow **Details** for its exact scope, safe shapes,
limitations and code-fix behavior; the short summary is not the complete specification.

| ID | Severity | Symptom / fix | Details |
|----|----------|---------------|---------|
| `TRLS001` | Warning | Handle discarded results. | [Result flow](#resultnothandledanalyzer--trls001) |
| `TRLS002` | Info | Use `Bind` when a `Map` callback returns a result. | [Map versus Bind](#usebindinsteadofmapanalyzer--trls002) |
| `TRLS003` | Error | Guard `Maybe.Value` access. | [Presence checks](#unsafevalueaccessanalyzer--trls003) |
| `TRLS004` | Warning | Avoid `Result<Result<T>>`. | [Result wrapping](#resultdoublewrappinganalyzer--trls004) |
| `TRLS005` | Warning | Await async results; do not block. | [Async misuse](#asyncresultmisuseanalyzer--trls005) |
| `TRLS007` | Warning | Avoid `Maybe<Maybe<T>>`. | [Maybe wrapping](#maybedoublewrappinganalyzer--trls007) |
| `TRLS008` | Info | Combine independent results. | [Combine](#useresultcombineanalyzer--trls008) |
| `TRLS009` | Warning | Use async ROP variants for async work. | [Async callbacks](#asynclambdawithsyncmethodanalyzer--trls009) |
| `TRLS010` | Warning | Return failures instead of throwing in ROP callbacks. | [Exception boundary](#throwinresultchainanalyzer--trls010) |
| `TRLS013` | Warning | Avoid unsafe `Maybe.Value` in LINQ. | [LINQ scope and limitations](#unsafevalueinlinqanalyzer--trls013-trls054-trls055) |
| `TRLS014` | Error | Keep combined tuples within 9 elements. | [Tuple limit](#combinelimitanalyzer--trls014) |
| `TRLS015` | Warning | Use Result save helpers or let the unit of work commit. | [Save ownership](#usesavechangesresultanalyzer--trls015) |
| `TRLS016` | Warning | Use `HasTrellisIndex` for `Maybe<T>` properties. | [Index mapping](#hasindexmaybepropertyanalyzer--trls016) |
| `TRLS018` | Warning | Gate deconstructed result values on success. | [Deconstruction](#unsaferesultdeconstructionanalyzer--trls018) |
| `TRLS019` | Warning | Construct results and optional values explicitly. | [Default-state semantics](#defaultresultormaybeanalyzer--trls019) |
| `TRLS020` | Warning | Use supported composite DTO transports. | [JSON boundary](#compositevalueobjectdtoconverteranalyzer--trls020) |
| `TRLS021` | Warning | Remove convention-owned EF mappings. | [Mapping ownership](#redundantefconfigurationanalyzer--trls021) |
| `TRLS022` | Warning | Use private setters on owned value objects. | [Owned setters](#ownedentityinitonlypropertyanalyzer--trls022) |
| `TRLS023` | Warning | Preserve the destination API version in Location URLs. | [Versioned destinations](#createdatroutemissingapiversionanalyzer--trls023) |
| `TRLS054` | Warning | Use `Maybe` operators or EF helpers, not `.Equals`. | [Queryable comparisons](#unsafevalueinlinqanalyzer--trls013-trls054-trls055) |
| `TRLS055` | Warning | Inline `HasValueWhere` predicates in EF queries. | [Queryable predicates](#unsafevalueinlinqanalyzer--trls013-trls054-trls055) |
| `TRLS031` | Warning | Use a supported Required base type. | [TRLS031](#trls031) |
| `TRLS032` | Error | Keep string minimum length within maximum length. | [TRLS032](#trls032) |
| `TRLS033` | Error | Keep range minimum within maximum. | [TRLS033](#trls033) |
| `TRLS034` | Error | Keep decimal constraints within CLR bounds. | [TRLS034](#trls034) |
| `TRLS035` | Warning | Declare generated `Maybe<T>` properties `partial`. | [TRLS035](#trls035) |
| `TRLS036` | Error | Declare owned generated types `partial`. | [TRLS036](#trls036) |
| `TRLS037` | Warning | Do not duplicate the generated owned constructor. | [TRLS037](#trls037) |
| `TRLS038` | Error | Derive owned value objects from `ValueObject`. | [TRLS038](#trls038) |
| `TRLS039` | Warning | Use a supported AOT JSON primitive or custom converter. | [TRLS039](#trls039) |
| `TRLS043` | Error | Put numeric convenience attributes on numeric bases. | [Numeric attributes](#requiredpartialclassgenerator-required-attribute-diagnostics--trls043trls045) |
| `TRLS044` | Error | Choose one numeric sign constraint. | [Numeric attributes](#requiredpartialclassgenerator-required-attribute-diagnostics--trls043trls045) |
| `TRLS045` | Error | Choose a sign constraint or explicit range, not both. | [Numeric attributes](#requiredpartialclassgenerator-required-attribute-diagnostics--trls043trls045) |
| `TRLS056` | Error | Remove declarations that collide with generated members. | [Member collisions](#requiredpartialclassgenerator-member-collision--trls056) |
| `TRLS057` | Error | Use `[Trim]` only on `RequiredString`. | [Attribute placement](#requiredpartialclassgenerator-opt-in-attribute-placement--trls057trls058) |
| `TRLS058` | Error | Do not apply `[NotDefault]` to sentinel-less bases. | [Attribute placement](#requiredpartialclassgenerator-opt-in-attribute-placement--trls057trls058) |
| `TRLS059` | Warning | Add a `[JsonSerializable]` to the marked JSON context. | [TRLS059](#trls059) |
| `TRLS060` | Error | Supply a nonblank reason-code override. | [TRLS060](#trls060) |
| `TRLS061` | Error | Declare only one `ValidateAdditional` overload. | [TRLS061](#trls061) |
| `TRLS062` | Info | Name custom validation failures with the coded overload. | [TRLS062](#trls062) |
| `TRLS063` | Info | Give FluentValidation `Must` rules a code. | [Custom rule codes](#mustwithouterrorcodeanalyzer--trls063) |
| `TRLS064` | Info | Use vocabulary constants; do not claim framework namespaces. | [Vocabulary scope](#reasoncodevocabularyanalyzer--trls064) |
| `TRLS065` | Warning | Do not use `[Produces]` to force JSON media types. | [Formatter behavior](#producesclobbersproblemdetailsanalyzer--trls065) |
| `TRLS066` | Info | Use value-returning nullable guards. | [Nullable guards](#useensurenotnullfornullableanalyzer--trls066) |

## Constants — `TrellisDiagnosticIds`

The public static class `Trellis.TrellisDiagnosticIds` (in the `Trellis.Analyzers` assembly) exposes every diagnostic ID above as a `public const string` for tooling that explicitly references that assembly for compilation. The NuGet package ships the assembly as an analyzer asset, not a `lib`/`ref` compile asset, so ordinary package consumers must use literal IDs:

```csharp
[SuppressMessage("Trellis", "TRLS003",
    Justification = "guarded by HasValue check earlier in the pipeline")]
public string GetCity(Maybe<Address> address) => address.Value.City;
```

Import `System.Diagnostics.CodeAnalysis` for `SuppressMessageAttribute`. Generator IDs (`TRLS031`–`TRLS045`, `TRLS056`–`TRLS058`, `TRLS060`–`TRLS062`), the LINQ-analyzer IDs (`TRLS054`–`TRLS055`), and `TRLS063`–`TRLS066` are also exposed as constants on the same class for tooling authors.

### Constant → diagnostic ID → emitter

Every `public const string` field on `TrellisDiagnosticIds`, the diagnostic ID it carries, and the analyzer (or generator) that emits it. Ordinary consumers use the diagnostic ID in `[SuppressMessage]`, `.editorconfig`, and `#pragma warning disable`; constants require an explicit compile reference to the analyzer assembly.

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
| `SerializerContextHasNoJsonSerializable` | `TRLS059` | `ScalarValueJsonConverterGenerator` (Trellis.AspSourceGenerator) |
| `EmptyReasonCodeOverride` | `TRLS060` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `ValidateAdditionalOverloadConflict` | `TRLS061` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `UnnamedValidateAdditionalFailure` | `TRLS062` | `RequiredPartialClassGenerator` (Trellis.Core.Generator) |
| `MustWithoutErrorCode` | `TRLS063` | `MustWithoutErrorCodeAnalyzer` |
| `ReasonCodeVocabulary` | `TRLS064` | `ReasonCodeVocabularyAnalyzer` |
| `ProducesClobbersProblemDetails` | `TRLS065` | `ProducesClobbersProblemDetailsAnalyzer` |
| `UseEnsureNotNullForNullable` | `TRLS066` | `UseEnsureNotNullForNullableAnalyzer` |

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
| `MustWithoutErrorCode` | `TRLS063` | Info |
| `ReasonCodeVocabulary` | `TRLS064` | Info |
| `ProducesClobbersProblemDetails` | `TRLS065` | Warning |
| `UseEnsureNotNullForNullable` | `TRLS066` | Info |

> **Note:** The TRLS013 descriptor was originally exposed as `UnsafeValueInLinq`. The current canonical name is `UnsafeMaybeValueInLinq` (matching the `TrellisDiagnosticIds.UnsafeMaybeValueInLinq` constant); the old name is retained as an `[Obsolete]` alias pointing at the same `DiagnosticDescriptor` instance for backward compatibility. New code should reference `UnsafeMaybeValueInLinq`.

> **Note:** Generator-emitted diagnostics (`TRLS031`–`TRLS045`, `TRLS056`–`TRLS058` and `TRLS060`–`TRLS062`) are constructed inline by the source generators and are *not* exposed as fields on `DiagnosticDescriptors`. Tooling with a compile reference can use `TrellisDiagnosticIds` for those IDs; ordinary consumers use literal IDs.

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
- `Maybe<T>.Value` is hidden from IntelliSense as polish; the analyzer enforces safe access.
- Recognized safe patterns include:
  - `if` / ternary checks on `HasValue` / `HasNoValue`
  - `TryGetValue` branches, including negated forms
  - `maybe.HasValue && maybe.Value ...` short-circuit
  - early-return / guard-clause exits — `if (!maybe.HasValue) return;` (also `throw` / `break` / `continue`, and the `maybe.HasNoValue`, `maybe.HasValue == false`, and `maybe.HasValue != true` forms, with the literal on either side) followed by `maybe.Value` in a later statement, when the receiver is not reassigned between the guard and the access
  - property patterns over a single subpattern — `maybe is { HasValue: true }`, `maybe is { HasNoValue: false }`, and their negations `maybe is not { HasValue: true }` — in `if` branches, ternary arms, early-return guards, and `&&` short-circuits. Multi-subpattern forms such as `maybe is { HasValue: true, Value.Length: > 0 }` are deliberately not recognized, because negating a conjunction does not prove absence and both branches must be exact.
  - safe lambda parameters inside Trellis Maybe APIs such as `Bind`, `Map`, `Tap`, `Ensure`, `Match`
  - a prior assignment statement such as `x = Maybe<int>.From(n);` when `T` is a non-nullable value type and the variable is not reassigned; a declaration initializer such as `var x = Maybe<int>.From(n);` is not recognized by this exemption
- **Inside `Expression<Func<...>>` lambdas (EF Core, Specifications, FluentValidation):** the rule is *not* relaxed. The analyzer recognizes the immediate short-circuit shape `e.SubmittedAt.HasValue && e.SubmittedAt.Value < cutoff`; when the `Maybe<T>` check is part of a longer predicate, keep that pair parenthesized or prefer an analyzer-clean sentinel form such as `e.SubmittedAt.GetValueOrDefault(DateTime.MaxValue) < cutoff`. For ad-hoc EF `IQueryable<T>` queries, prefer the `MaybeQueryableExtensions.WhereXxx` helpers when one matches the predicate.
- Code fix: `AddResultGuardCodeFixProvider`.

##### Synthesized fallback return

When the guarded statements end the method with a `return`, the wrapped code no longer returns on every path, so the fix appends a fallback:

| Declared return type | Fix emits | Rationale |
|---|---|---|
| Value type (`int`, `DateTime`, …) | `return default;` | `default` is a real value. |
| `Result<T>` | `return default;` | `default(Result<T>)` is a failure carrying the `default-initialized` sentinel. |
| `Maybe<T>` | `return default;` | `default(Maybe<T>)` is `None`. |
| Unconstrained generic `T` | `return default;` | `default(T)` is valid for any substitution. |
| Nullable reference (`string?`) | `return default;` | `null` is in the type's domain. |
| **Non-nullable reference (`string`, `Task<T>`)** | **nothing** | `default` would be `null`. Omitting the return raises **CS0161** ("not all code paths return a value"), which cannot be ignored, instead of CS8603 (a warning that may be suppressed) or a silent `null` where the type system says one is impossible. Replace the missing branch with a real value or a `throw`. |

`async` methods are judged on the awaited type, so `async Task<int>` gets `return default;` while `async Task<string>` does not. A non-`async` method declared to return `Task<T>` is treated as a non-nullable reference type, because `default` there is a `null` task that would throw on `await`.

`default!` is deliberately not emitted: it silences the compiler while planting a `null` the type system claims cannot exist.

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
- Flags `.Value` inside System.Linq `Enumerable` / `Queryable` projection/order/grouping lambdas for:
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
- That suppression applies only to TRLS013. TRLS003 does not infer safety from a separate `Where` lambda; use `.Where(x => x.HasValue).Select(x => x.GetValueOrDefault(0))` for an in-memory `Maybe<int>` sequence to satisfy both rules without including `None` elements.
- For EF Core IQueryable predicates over a `Maybe<T>` property, either register `AddTrellisInterceptors()` (which rewrites `.HasValue`/`.Value`/`GetValueOrDefault(d)` into `EF.Property`/null-checks/`COALESCE`) or use `Trellis.EntityFrameworkCore.MaybeQueryableExtensions` (`WhereHasValue`, including its typed-predicate overload, / `WhereNone` / `WhereEquals`) explicitly. Note: this analyzer only fires on Select-family methods today; coverage of `.Where`/`.Any`/`.First` etc. is tracked as a follow-up.
- `TRLS054`: inside `System.Linq.Queryable` lambdas, flags `Maybe<T>.Equals(...)` and `object.Equals(...)` when a `Maybe<T>` operand is involved. Use the natural `==` / `!=` operators (which `MaybeExpressionRewriter` understands) or `MaybeQueryableExtensions.WhereEquals(...)`.
- `TRLS055`: inside `System.Linq.Queryable` lambdas, flags `HasValueWhere(...)` when the predicate argument is not an inline lambda. Captured `Func<T, bool>` variables, method groups, and member delegates cannot be inspected by the EF Core rewriter. For reusable EF predicates, use `WhereHasValue(propertySelector, predicate)` with an `Expression<Func<TInner, bool>>` instead; this explicit helper inlines the expression tree.
- User-defined methods named `Select`, `GroupBy`, `OrderBy`, etc. are ignored unless the invocation resolves to `System.Linq.Enumerable` or `System.Linq.Queryable`.
- No code fix.

#### `CombineLimitAnalyzer` — `TRLS014`
- Flags the outermost Trellis `.Combine(...)` or `.CombineAsync(...)` chain when the resulting tuple would exceed 9 elements.
- Downstream `Bind`, `Map`, `Tap` and `Match` also support at most 9 tuple elements. Group related fields into value objects or sub-results before combining larger inputs.
- Counts tuple width semantically, so chains continued through intermediate variables are still measured correctly.
- User-defined methods or extension methods named `Combine` / `CombineAsync` are ignored unless they resolve to the Trellis combine extension containers.
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
- Prefer `builder.HasTrellisIndex(e => new { e.Status, e.SubmittedAt })`: regular properties remain strongly typed and `Maybe<T>` properties resolve to their mapped storage. String-based `builder.HasIndex("Status", "_submittedAt")` is the fallback.
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
- Flags explicit `default(Result<T>)` (including `default(Result<Unit>)`) and `default(Maybe<T>)` expressions at use sites.
- Uses `IDefaultValueOperation` (operation-based, not syntax-based) so it covers all surface forms equivalently:
  - `default(T)` typeof-style: `return default(Result<int>);`
  - Target-typed `default`: `return default;` in a `Result<T>`-returning method, parameter defaults, etc.
  - Null-suppressed `default!`: `return default!;` is treated identically — the null-suppressing operator does not change the underlying value.
- `default(Result<Unit>)`/`default(Result<T>)` represent typed failures carrying the shared `new Error.Unexpected("default-initialized")` sentinel — *never* silent successes. `Result` itself is a static factory class, not a value type. `default(Maybe<T>)` equals `Maybe<T>.None` (semantically correct) but the explicit literal obscures intent.
- Suggested replacements:
  - `Result<Unit>` → `Result.Ok()` or `Result.Fail(error)`
  - `Result<T>` → `Result.Ok(value)` or `Result.Fail<T>(error)`
  - `Maybe<T>` → `Maybe<T>.None` or `Maybe.From(value)`
- For sanctioned sentinel/test-helper sites, suppress with `[SuppressMessage("Trellis", "TRLS019", Justification = "...")]` on the enclosing member or `#pragma warning disable TRLS019` around the offending span.
- No code fix (the appropriate replacement depends on intent — success vs. failure for `Result`, value vs. None for `Maybe`).

#### `CompositeValueObjectDtoConverterAnalyzer` — `TRLS020`
- Flags ASP.NET controller request/response DTOs and Minimal API handler request DTOs with properties whose type is an `[OwnedEntity]` Trellis `ValueObject` missing `[JsonConverter(typeof(CompositeValueObjectJsonConverter<T>))]`. A Mediator `ICommand<T>`/`IRequest<T>`/`IQuery<T>` is flagged only when it is itself bound as the request body at one of those seams (e.g. a Minimal API handler parameter); a command constructed server-side and never deserialized from JSON is not flagged.
- Also flags properties typed `Maybe<TComposite>` where `TComposite` is an `[OwnedEntity]` `ValueObject`. **`Maybe<TComposite>` is always flagged**, even when the inner `TComposite` carries `[JsonConverter(typeof(CompositeValueObjectJsonConverter<TComposite>))]`: that converter operates on `TComposite`, not on `Maybe<TComposite>`. Trellis ships no `MaybeCompositeValueObjectJsonConverterFactory`, so STJ falls back to default construction of the inner type and wraps it in `Maybe.From`, silently bypassing `TryCreate`. The supported DTO transport per cookbook Recipe 14 is `TComposite?` plus `Maybe.From(...)` at the endpoint/API seam — applicable to controller actions, Minimal API handlers, and Mediator message-construction sites.
- This catches the silent JSON-binding failure where System.Text.Json can default-construct the composite value object and bypass `TryCreate` validation.
- Does not flag domain model properties that are not exposed through DTO surfaces.
- Does not flag bare composite value-object types that carry the matching `CompositeValueObjectJsonConverter<T>` attribute.
- Scope is bounded to owned composite value objects. `Maybe<TScalarValueObject>` (where `TScalarValueObject : IScalarValue<,>`) is handled by `MaybeScalarValueJsonConverterFactory` and is out of scope for this analyzer. `Maybe<int>` / `Maybe<string>` / `Maybe<Guid>` / `Maybe<DateTime>` (primitive inner types in the closed allowed list) are handled by `MaybePrimitiveJsonConverterFactory` since Trellis #506 (`AddScalarValueValidation()` — or the convenience `AddTrellisAspWithScalarValidation()` — registers it); also out of scope for this analyzer because the JSON round-trip is correct by construction. Primitive inner types outside the allowed list (`DateOnly`, `TimeOnly`, unsigned numerics) remain unsupported and use the wire-shape DTO seam per Recipe 14 — also out of scope here for the same correctness-bug-class boundary.
- No code fix.

#### `RedundantEfConfigurationAnalyzer` — `TRLS021`
- Activates only when the compilation references `Trellis.EntityFrameworkCore.MaybeConvention`.
- Reports only when the relevant `DbContext` also wires Trellis conventions via `ApplyTrellisConventions(...)` or generated `ApplyTrellisConventionsFor<TContext>()`.
- Manual configuration inside a `DbContext` subclass is correlated with that specific context, so a second context in the same compilation can keep manual mappings without being flagged. Standalone `IEntityTypeConfiguration<TEntity>` classes are attributed to contexts through `ApplyConfiguration(new TConfig())`, `ApplyConfigurationsFromAssembly(...)`, or matching `DbSet<TEntity>` properties. Configuration classes that cannot be associated with any context are not reported.
- Flags manual EF configuration for convention-owned properties:
  - `builder.Property(e => e.MaybeProperty).HasConversion(...)`
  - `builder.OwnsOne(e => e.OwnedEntityValueObject)`
  - `builder.Ignore(e => e.MaybeOrOwnedEntityProperty)`
- Targets `Maybe<T>` and types annotated with `Trellis.EntityFrameworkCore.OwnedEntityAttribute`.
- No code fix.

#### `OwnedEntityInitOnlyPropertyAnalyzer` — `TRLS022`
- Activates only when the compilation references `Trellis.EntityFrameworkCore.OwnedEntityAttribute`.
- Walks each `[OwnedEntity]` class and its base types, flagging non-private instance properties with an `init` accessor even when the declaring base is not annotated.
- Ignores static properties, indexers, explicit interface implementations, and private properties. A nearer eligible property of the same name hides the inherited property, even when the nearer property's setter is not init-only.
- Diagnostic anchors at the `init` keyword when available (otherwise the property/type location) and includes the property name and owned class name.
- Recommends `{ get; private set; }`, the supported and tested shape for owned-entity properties materialized through the generator-emitted parameterless constructor.
- No code fix.

### Bundled generator diagnostics

These diagnostics come from the hosting package's generator, not the standalone
analyzer opt-in. See the [constant/emitter table](#constant--diagnostic-id--emitter)
for the owning assembly.

#### TRLS031

`RequiredPartialClassGenerator` reports an unsupported Required base. Supported bases
are `RequiredGuid`, `RequiredString`, `RequiredInt`, `RequiredDecimal`, `RequiredLong`,
`RequiredBool`, `RequiredDateTime`, `RequiredDateTimeOffset` and `RequiredEnum`.
The previous diagnostic ID was `TRLSGEN001`.

#### TRLS032

`RequiredPartialClassGenerator` reports `[StringLength]` with
`MinimumLength > MaximumLength`. Adjust the values to form a non-empty range.
The previous diagnostic ID was `TRLSGEN002`.

#### TRLS033

`RequiredPartialClassGenerator` reports `[Range]` on `int`, `long` or `decimal`
with `Min > Max`. Adjust the values to form a non-empty range.
The previous diagnostic ID was `TRLSGEN003`.

#### TRLS034

`RequiredPartialClassGenerator` reports a decimal `[Range]` outside CLR `decimal`
bounds. Use a tighter range. The previous diagnostic ID was `TRLSGEN004`.

#### TRLS035

`MaybePartialPropertyGenerator` reports non-partial `Maybe<T>` auto-properties in a
partial containing type. Declare the property `partial` so it can emit the backing
field and storage member. The previous diagnostic ID was `TRLSGEN100`.

#### TRLS036

`OwnedEntityGenerator` reports `[OwnedEntity]` on a non-partial type. Declare the type
`partial` so it can emit the private parameterless constructor.
The previous diagnostic ID was `TRLSGEN101`.

#### TRLS037

`OwnedEntityGenerator` reports an owned type that already has a parameterless
constructor. Remove that constructor or remove `[OwnedEntity]`.
The previous diagnostic ID was `TRLSGEN102`.

#### TRLS038

`OwnedEntityGenerator` reports an owned type that does not derive from `Trellis.ValueObject`.
The previous diagnostic ID was `TRLSGEN103`.

#### TRLS039

`ScalarValueJsonConverterGenerator` skips an unsupported `ScalarValueObject<TSelf, TPrimitive>`
JSON converter. The AOT-safe primitives are `string`, `int`, `long`, `short`, `byte`,
`bool`, `float`, `double`, `decimal`, `Guid`, `DateTime` and `DateTimeOffset`. A
reflection-based converter would raise IL2026/IL3050 under `PublishAot=true`;
provide a custom `JsonConverter<TSelf>` or choose a supported primitive.

#### TRLS059

`ScalarValueJsonConverterGenerator` reports a `[GenerateScalarValueConverters]`
context with no `[JsonSerializable]` of its own. System.Text.Json observes only the
original compilation's attributes, skips that context and leaves its abstract members
unimplemented (two CS0534 errors). Generators cannot observe one another's output;
add at least one `[JsonSerializable(typeof(...))]`.

#### TRLS060

`RequiredPartialClassGenerator` reports an empty/whitespace `Code` on `[StringLength]`,
`[Range]`, `[NotDefault]` or a numeric sign attribute. Supply a nonblank application
code or omit `Code` to keep the framework default.

#### TRLS061

`RequiredPartialClassGenerator` reports both the three- and four-argument
`ValidateAdditional` overloads on one type. It emits one defining declaration,
so the other implementation cannot bind. Keep one overload.

#### TRLS062

`RequiredPartialClassGenerator` reports the three-argument `ValidateAdditional`.
It can reject a value but cannot name the failure, which reaches the client as
`error.unspecified`. Add and set `ref string? errorCode`. The old overload remains
legal; raise `dotnet_diagnostic.TRLS062.severity` once failures are named.

#### `RequiredPartialClassGenerator` Required-attribute diagnostics — `TRLS043`–`TRLS045`
- `TRLS043` (Error): numeric convenience attribute (`[Positive]`, `[NonNegative]`, `[Negative]`, `[NonPositive]`) applied to a base other than `RequiredInt`, `RequiredLong` or `RequiredDecimal`.
- `TRLS044` (Error): more than one numeric convenience attribute is present on the same Required type.
- `TRLS045` (Error): a numeric convenience attribute is combined with an explicit `[Range]`; pick one shape.
- No code fix.

#### `RequiredPartialClassGenerator` member collision — `TRLS056`
- Flags user-declared members on `Required*<TSelf>` partial classes that collide with members generated by `RequiredPartialClassGenerator`.
- Covered members include `TryCreate`, `Create`, `Parse`, `TryParse`, GUID factories, conversion operators, constructors and validation hooks.
- Severity: Error. The generator suppresses only the conflicting generated member and reports at the user member location, replacing generic `CS0111` / `CS0102` duplicate-member failures with a Trellis-specific message.
- Fix: delete the redundant declaration and rely on the generated member, or stop deriving from the Required base if the type needs fully custom semantics.
- No code fix.

#### `RequiredPartialClassGenerator` opt-in attribute placement — `TRLS057`–`TRLS058`
- `TRLS057` (Error): `[Trim]` applied to a Required base other than `RequiredString`. `[Trim]` only drives string trimming, so on any other base it is silently ignored — remove it.
- `TRLS058` (Error): `[NotDefault]` applied to `RequiredBool` or `RequiredEnum`. Those bases have no meaningful default sentinel to reject (every value is valid), so the attribute is a no-op — remove it.
- The generator refuses to emit code for the type until the misplaced attribute is removed, surfacing the mistake at build time even when the analyzer is disabled.
- No code fix.

### Versioned Location rules

#### `CreatedAtRouteMissingApiVersionAnalyzer` — `TRLS023`
- Activates only inside controllers/types annotated with `[ApiVersion]` (and not `[ApiVersionNeutral]`). `[ApiVersion]` is declared with `Inherited = false`, so the analyzer inspects the immediate type only — derived controllers without their own `[ApiVersion]` are ignored.
- Triggered by `HttpResponseOptionsBuilder<T>.CreatedAtRoute(routeName, routeValues)`, `CreatedAtAction(actionName, routeValues, controllerName)`, and `WithLocation(routeName, routeValues)` invocations whose route-values dictionary does not include an `"api-version"` key.
- Also checks the single-id `CreatedAtRoute(routeName, idSelector)` and `WithLocation(routeName, idSelector)` overloads, which build a single-key dictionary internally.
- Recognised dictionary shapes:
  - `c => new RouteValueDictionary { ["id"] = c.Id, ["api-version"] = "..." }` — initializer block.
  - `c => new RouteValueDictionary { ["id"] = c.Id, [ApiVersionKey] = "..." }` — initializer block with const-string key (resolved via the semantic model).
  - `c => { return new RouteValueDictionary { ... }; }` — block-bodied lambda with a return statement.
  - `c => new RouteValueDictionary(new { id = c.Id })` — anonymous-object ctor shape; **always** flagged because C# property names cannot contain `"-"`, so the api-version key is necessarily missing.
- Key matching is case-insensitive (matches `RouteValueDictionary`'s runtime semantics): `"API-VERSION"`, `"Api-Version"`, etc., are all accepted.
- Suppression also applies when the fluent chain calls the underlying primitive directly: `HttpResponseOptionsBuilder<T>.WithRouteValueResolver("api-version", httpContext => ...)`. The key match is case-insensitive (so `"API-Version"` also suppresses) and the symbol is gated to `Trellis.Asp.HttpResponseOptionsBuilder<T>` — a same-named extension on an unrelated type does not silence the diagnostic.
- Code fix: appends `.WithVersionedRoute()` to the flagged `CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)` call (the `Trellis.Asp.ApiVersioning` extension) and adds `using Trellis.Asp.ApiVersioning;` when missing. The new `using` is inserted in the same scope as existing usings (file-scoped namespace, block-scoped namespace, or top-level).
- Runtime fix guidance: `WithVersionedRoute` now resolves the **final destination**, not the current endpoint. Missing/ambiguous destinations or unsupported pins throw; use a uniquely named destination route where necessary. It validates actual destination mappings: action-level `[MapToApiVersion]` narrows the versions accepted from controller declarations. It writes resolved/pinned versions into the target's actual `:apiVersion` segment or conventional query value. Neutral/missing-metadata targets remove supplied `api-version`; missing-metadata warnings identify the destination. Existing detection/code-fix syntax is unchanged, with no new diagnostic. Manual dictionary/resolver suppression does **not** provide these runtime checks. See [versioning migration guidance](trellis-api-asp-apiversioning.md#migration-existing-syntax-stricter-destination-checks); `PageUrl` retains different segment-pin behavior.

### Validation rules

#### `MustWithoutErrorCodeAnalyzer` — `TRLS063`
- Activates only when the compilation references FluentValidation (probed via the `FluentValidation.IRuleBuilder` interface), so the overwhelming majority of consumers never pay for it.
- Flags a `Must(...)` or `MustAsync(...)` rule component that no `WithErrorCode(...)` applies to. Both report as `PredicateValidator`/`AsyncPredicateValidator`, which `Trellis.FluentValidation` projects to the `error.unspecified` sentinel — see [trellis-api-fluentvalidation.md](trellis-api-fluentvalidation.md#behavioral-notes).
- The call must resolve to FluentValidation's **own** `Must`: declared in a `FluentValidation` namespace, on the built-in validator container `DefaultValidatorExtensions`, with a **receiver implementing `IRuleBuilder<T, TProperty>`**. All three matter — the name alone matches anything, a namespace test alone would also match an unrelated third-party namespace merely prefixed with the same word, and an application's own `Must` overload declared in `namespace FluentValidation` (a common convention) may name the failure itself.
- **Reports only what it can prove.** From the `Must`, the analyzer walks the calls chained after it and stops at the first one that is not a known component modifier. `WithMessage`, `WithErrorCode`, `WithSeverity`, `WithName`, `WithState`, `When`, `Unless`, `WhenAsync`, `UnlessAsync`, `OverridePropertyName`, and `DependentRules` refine the component already on the chain. One of FluentValidation's own built-in validators there (`NotEmpty`, `Matches`, another `Must` — recognised by their declaring type, `DefaultValidatorExtensions`) starts a new component, so the earlier one is provably unnamed — which is why `Must(a).Must(b).WithErrorCode("x")` still reports the first `Must`.
- It stays **silent** wherever the code could be attached out of sight:
  - the chain's value escapes the statement (assigned to a local, returned, passed as an argument), so a later `rule.WithErrorCode(...)` is possible;
  - the chain calls `Configure(...)`, which hands out the raw rule and can set `ErrorCode` directly;
  - the chain passes through any helper the application declared — including one declared in `namespace FluentValidation`, a common convention that spares callers an extra `using` — since such a helper may itself wrap `WithErrorCode`.

  Parentheses are climbed, so `(RuleFor(x).Must(p)).WithErrorCode("c")` reads as one chain. Only fluent syntax is analyzed; calling the extensions in static form is not recognised, which costs a diagnostic rather than producing a wrong one.
- Default severity is Info, not Warning: an uncoded `Must` is legal and pre-existing code is full of them. Raise it with `dotnet_diagnostic.TRLS063.severity = warning` once a codebase has caught up.
- No code fix — only the author knows what the rule's failure should be called, and a placeholder code on the wire is worse than the sentinel because it looks deliberate.

#### `ReasonCodeVocabularyAnalyzer` — `TRLS064`
- Activates only when the compilation can see `Trellis.ValidationCodes`. The frozen vocabulary is read **out of the compilation** — the analyzer enumerates the `public const string` fields on `ValidationCodes` and `FaultCodes` rather than carrying a table of its own, so a code added to the vocabulary is covered the day it is added. A hard-coded copy would be a second source of truth, and duplicated reason codes drifting is the problem this rule exists to catch.
- Matches an argument by **parameter or property name**, never by a list of members. Four positions carry a reason code to the wire, and all four are inspected:

  | Position | Matched by | Note |
  | --- | --- | --- |
  | Any Trellis method or constructor | a parameter named `reasonCode` or `code`, compared case-insensitively | Covers case-scoped `ForField`/`ForRule`/`ForReason`/`ForPolicy`/`For` factories through their consistent leading `code` parameter, including `Forbidden`, plus positional records (`FieldViolation.ReasonCode`, `RuleViolation.ReasonCode`, and the mandatory-reason `Error` cases whose parameter is `Code`). Matching follows the bound parameter, not arity or argument position. |
  | `Code` in an object initializer on a Trellis type | the assigned property name, on a type declared in a Trellis assembly | `new Error.NotFound(resource) { Code = "…" }` — the optional-reason cases reach `Error.Code` this way rather than through a constructor, so matching only parameters would have left `NotFound`, `Gone`, `RateLimited`, `AuthenticationRequired`, and `Unavailable` uncovered. Attribute named arguments are excluded here because the row below already models them |
  | FluentValidation `WithErrorCode(...)` | the method name plus its declaring namespace | Its argument becomes a Trellis reason code verbatim through `Trellis.FluentValidation`'s projection. The namespace is required so an unrelated API of the same method name is not analyzed; an application's own `WithErrorCode` declared in `namespace FluentValidation` *is* matched, because whatever wraps it, the argument is still an error code. FluentValidation's **test-helper** overload of the same name is excluded, because it names its parameter `expectedErrorCode` rather than `errorCode` |
  | `Code` on a Trellis primitive attribute | the property name, on a Trellis-declared attribute | `[StringLength]`, `[Range]`, `[NotDefault]` and the sign-convenience attributes. A `Code` property on a non-Trellis attribute is ignored |

- Reports three things, all Info:

  | Shape | Message | Fixable |
  | --- | --- | --- |
  | Literal equals a frozen code (`"value.not-null"`) | Names the constant to use | Yes — replaces with `ValidationCodes.ValueNotNull`, with fix-all |
  | Literal is the pre-vocabulary placeholder (`"validation.error"`) | Says to emit a real code | No — the guidance is not "use the constant" but "do not emit this" |
  | Literal claims `error.*`, or a namespace the framework publishes (`value.*`, `format.*`, `page-size.*`, …) | Names the namespace and why it misleads | No — only the author knows where the code belongs |

- **It does not check membership.** The freeze [constrains Trellis, not the application](trellis-api-core.md#validationcodes--the-reason-code-vocabulary), and [trellis-api-primitives.md](trellis-api-primitives.md#overriding-the-reason-code--code) promises that no analyzer pressures the choice to override or keep a framework code. A novel, well-formed application code such as `order.cancel-after-ship` — or a bare `required` — is silent. What is reported is narrower: restating a code that already has a constant, or claiming a namespace whose meaning Trellis has published, which makes an application failure read as a framework one to a client using the documented prefix fallback.
- The `validation.*` namespace is **not** reserved against applications, even though `validation.error` itself is reported. That namespace is a pre-vocabulary artifact rather than one the framework gave a meaning to.
- **Assertions are not reported.** Because the match requires the parameter to be named `reasonCode`, `Code`, or `errorCode`, a test asserting a literal wire value — `ShouldHaveValidationErrorFor(...).WithErrorCode("value.not-null")`, whose parameter is `expectedErrorCode` — stays silent. That is deliberate: pinning the exact published string in a test is what catches a renamed constant, and a test that compares the constant to itself stays green through precisely that break.
- **Only literal syntax is reported.** A constant reference carries a constant *value* too, so testing the value alone would flag `ForField(f, ValidationCodes.ValueNotNull)` — precisely the shape this rule tells authors to write. The consequence is that a code reached through an application's own `const string` indirection is invisible here; that is the accepted trade, not an oversight.

### Nullable guard rules

#### `UseEnsureNotNullForNullableAnalyzer` — `TRLS066`

- Reports `Result.Ensure(<expr> is not null, <error>)` where `<expr>` is a nullable reference type (judged by its declared annotation, so a non-nullable `string` is not reported; a `string?` already proven non-null still is, because the compiler treats the operand of a null test as maybe-null) or `Nullable<T>`. Three equivalent null tests are recognised — `is not null`, `!= null` / `null !=`, and the empty property pattern `is { }` — and a parenthesised condition is read through. `Ensure(bool, Error)` and `Ensure(bool, Func<Error>)` are both covered, because `Result.EnsureNotNull` has both overloads. Public tooling names match the guard: `UseEnsureNotNullForNullableAnalyzer`, `UseEnsureNotNullForNullableCodeFixProvider`, and `UseEnsureNotNullForNullable` for the descriptor and ID constant. The diagnostic ID remains `TRLS066`; old-name aliases are not retained.
- **Silent on anything that is not a pure null test.** `x is not null && x.Length > 0`, `x is { Length: > 0 }`, `x is { } y` (which declares a variable), explicit type patterns such as `x is string { }`, `x is null`, and `x == null` are not reported: the replacement would drop a clause or invert the meaning. A user-defined `!=` on a reference type (other than `string`'s) is skipped, as is a null comparison requiring an implicit user-defined operand conversion: rewriting the original operand would lose that conversion's null semantics and side effects. A lifted operator on a `Nullable<T>` such as `DateTime? != null` is reported, since `EnsureNotNull` performs a plain null check. Conditional error selection (`c ? e1 : e2`, switch expressions, and null coalescing) is skipped, including through an explicit cast. Named arguments are still reported, but get no fix.
- **The two forms differ in payload type.** `Result.Ensure` returns `Result<Unit>`; `Result.EnsureNotNull` returns `Result<T>` carrying the non-null value. A standalone replacement can therefore change the type a caller observes (`Result<Unit>` to `Result<string>`), so the analyzer reports every occurrence but the code fix does not offer a rewrite everywhere.
- **Code fix:** `UseEnsureNotNullForNullableCodeFixProvider` rewrites to `Result.EnsureNotNull(<expr>, <error>)` only when the call is an operand of a Trellis `Combine` chain (resolved semantically) whose result is passed straight to Trellis's own `Map` or `Bind` (matched by symbol, with the callback found by its bound delegate parameter, not its position) with a lambda that ignores that operand's tuple slot — a real discard, or an untyped `_` parameter that is never read (a lone `_` is an ordinary parameter in C# and can be referenced). The slot is taken from the parameter the operand binds to, so named, reordered and static-call forms are placed correctly. `Tap`, `Match`, and application extensions get no fix. `Combine`, `EnsureNotNull`, `Map` and `Bind` are matched against Trellis's own declaring types. The original `Result` qualification or alias is preserved; a using-static call becomes `global::Trellis.Result.EnsureNotNull`, so no new namespace import is needed. An application's `ToResult` instance method cannot shadow this static guard. Comments beside the checked value and the error are kept. A lambda that names or types the slot, a chain that is returned or assigned, named arguments, and a call that would not bind get no fix. The checked value is found semantically, so `title != Nil` with `const string? Nil = null` rewrites `title`. A low-precedence value stays an argument: `(a ?? b) is not null` becomes `Result.EnsureNotNull(a ?? b, ...)`. A standalone `Result.Ensure(...)` keeps the diagnostic with no fix. The fix does not touch a `title!` in a later lambda; once the lambda takes the tuple elements (`.Map((title, due) => ...)`) the suppression is unnecessary and can be deleted by hand.
- **Invocations containing preprocessor directives get no fix.** Semantic binding examines only the active configuration; rewriting a call containing `#if` / `#else` can discard inactive validation and change another configuration's behavior. The analyzer still reports an active pure null check, but both individual fixes and Fix All leave the directive-bearing call untouched.
- **Nested tuples are tracked through the chain.** A non-flattening `Combine` (for example one selected by the named `t2` argument) nests the preceding tuple in its first slot. The consumer must discard that entire containing tuple, not an unrelated later slot. Subsequent flattening calls preserve the containing slot; further nesting moves it into the new first slot.
- **The enclosing pipeline is rebound before offering a fix.** Every affected `Combine` and the final `Map`/`Bind` must still resolve to the same method definition, and the consumer's return type must remain unchanged. A rewrite that selects a tuple-flattening overload, selects an application extension, or makes explicit generic arguments incompatible gets no fix even if the replacement guard call alone would compile.
- **Fix All applies replacements sequentially within each document**, rechecking pipeline binding after each edit. It can leave a diagnostic unfixed when combining otherwise-safe edits would select a different overload.
- **Error accumulation is unchanged.** `Combine` aggregates every failure whether its operands are `Result<Unit>` guards or `Result<T>` values, so the multi-field form still reports all missing fields at once. See the WRONG/FIX shape in [trellis-api-anti-patterns.md](trellis-api-anti-patterns.md#trls066--resultensurex-is-not-null-error-instead-of-ensurenotnull).
- Default severity is Info: both shapes are correct, so this teaches an idiom rather than gating a defect.

### ASP.NET Core rules

#### `ProducesClobbersProblemDetailsAnalyzer` — `TRLS065`

- Activates only when the compilation can see `Trellis.Asp.TrellisAspOptions`. `[Produces]` is a stock MVC attribute, so the rule has no standing in a project that has not opted into Trellis problem-details responses.
- **Reports on a JSON-family media type, not on the absence of `problem+json`.** A media type is JSON-family when `SystemTextJsonOutputFormatter` advertises it, which is `application/json`, `text/json`, and `application/*+json` — and no wider. The top-level type matters: `text/vnd.contoso+json` is *not* a subset of `application/*+json`, so the JSON formatter declines it and the rule stays silent, while `application/vnd.contoso+json` is matched and reported. Media-type parameters are ignored, so `application/json; charset=utf-8` is reported. Named constructor arguments (`[Produces(contentType: "…")]`) and an explicit array for the `params` parameter are read; arguments are read in **parameter** order rather than source order, since named arguments may be written in either.
- **Any JSON-family entry is reported, in any position.** It is tempting to assume the content-type list is searched in order, and that a list is therefore safe if `application/problem+json` comes early enough. It is not: `ObjectResultExecutor` selects via `SelectFormatterUsingAnyAcceptableContentType`, which loops over **formatters** in the outer loop and acceptable media types in the inner one. Registration order therefore beats list order, and the JSON formatter — which writes *any* type — claims whichever of its media types appears in the list, however late. The message splits on which JSON-family entry comes first:

  | Shape | Reported as |
  | --- | --- |
  | `[Produces("application/json")]` | problem responses are written as `application/json` |
  | `[Produces("application/json", "application/problem+json")]` | same — the trailing `problem+json` is **inert**, so a rule keyed on "omits `problem+json`" would go green on a broken app |
  | `[Produces("application/problem+json", "application/json")]` | successful `ObjectResult` responses are written as `application/problem+json` |
  | `[Produces("text/csv", "application/problem+json")]` | same — the earlier `text/csv` does **not** protect the success response, because the JSON formatter is consulted before an appended CSV one |

  No ordering of the list is reliably correct: for a list of JSON-family types alone there is no working order at all, and for a mixed list the outcome turns on formatter registration order that the analyzer cannot see — the same `[Produces("text/csv", "application/problem+json")]` yields `application/problem+json` with an appended CSV formatter and `text/csv` with one inserted first. That is why the guidance is to trim `MvcOptions.OutputFormatters` rather than edit the list. `Examples/Showcase/src/Showcase.Mvc/Program.cs` shows the sanctioned shape.
- **A list with no JSON-family type is silent, and this is a behavioural fact rather than a false-positive concession.** Clobbering requires a registered formatter that can write a `ProblemDetails` *as a listed media type*. A `text/csv` or `application/pdf` formatter declines `ProblemDetails` in `CanWriteType`, so MVC falls back and the failure keeps `application/problem+json` — `[Produces("text/csv")]` on its own is safe. `FileResult` is not an `ObjectResult` at all, so it is unaffected in both directions. XML is deliberately **not** offered as an example here, for a different reason than `text/csv`: Trellis failure responses consult no output formatter at all, so registering either XML formatter cannot affect them and `[Produces]` cannot repair anything about them — see the JSON-only note in [trellis-api-asp.md](trellis-api-asp.md#behavioral-notes).
- **`[Produces(typeof(T))]` is never reported.** That overload declares a response type and leaves `ContentTypes` empty, so it cannot rewrite anything.
- Trellis's own responses are already immune: `AsActionResult<T>()` wraps an `IResult` in a plain `ActionResult` rather than an `ObjectResult`, and once `ScalarValueValidationFilter` is registered it owns every invalid `ModelState`. The seam the rule protects is the one Trellis does **not** own — `ObjectResult`s the application builds itself, chiefly `Problem(...)` and `ValidationProblem(...)`, plus MVC's automatic model-validation response in an app without the filter.
- No code fix. Both mechanical edits — appending and prepending `problem+json` — are the two shapes this rule exists to reject, and the correct remedy is a change to composition-root formatter registration that the analyzer cannot see.

## Code fix providers

| Code fix provider | Fixes | Behavior |
|---|---|---|
| `AddResultGuardCodeFixProvider` | `TRLS003` | Wraps the current statement block in `if (maybe.HasValue)` and tracks consecutive statements that keep using the guarded value. When the wrapped statements end the method with a `return`, it appends `return default;` only if `default` is a usable value for the declared return type; see [synthesized fallback return](#synthesized-fallback-return). |
| `UseBindInsteadOfMapCodeFixProvider` | `TRLS002` | Replaces `Map` with `Bind` and `MapAsync` with `BindAsync`. |
| `UseAsyncMethodVariantCodeFixProvider` | `TRLS009` | Replaces sync method names with async variants, awaits the rewritten call, and adds `async` to Task/ValueTask-returning methods when that is locally safe. It withholds the fix for chained/nested calls and scopes that require manual delegate, parameter, or return-flow changes. |
| `UseSaveChangesResultCodeFixProvider` | `TRLS015` | Replaces `SaveChangesAsync` / `SaveChanges` with `SaveChangesResultAsync` or `SaveChangesResultUnitAsync`, adds `await`/`async` for sync `SaveChanges`, and adds `using Trellis.EntityFrameworkCore;` when needed. When the returned row count is *used*, the fix is offered only where the consuming context can rebind to `Result<int>` — an implicitly-typed (`var`) local. It is withheld for explicit `int` locals, assignments, conditions, arguments, and `return` statements, because the mechanical rename would produce `CS0029`/`CS0019`; restructure those call sites into the railway by hand. |
| `ReasonCodeVocabularyCodeFixProvider` | `TRLS064` | Replaces a literal that restates a frozen code with its constant — `"value.not-null"` becomes `ValidationCodes.ValueNotNull`. Fix-all is supported, because the motivating case is one literal repeated across dozens of call sites. Only that shape is fixed: the analyzer attaches the constant to the diagnostic, and a namespace-squatting diagnostic carries none, so no fix is offered where no mechanical replacement exists. |
| `UseEnsureNotNullForNullableCodeFixProvider` | `TRLS066` | Rewrites `Result.Ensure(x is not null, error)` to `Result.EnsureNotNull(x, error)` when the call is an operand of a Trellis `Combine` chain whose consuming lambda discards that slot, so the changed payload type is not observable. Preserves qualification and aliases; fix-all is supported. No fix is offered for a standalone `Result.Ensure`, whose `Result<Unit>` type a caller may depend on. |
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
