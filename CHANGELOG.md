# Changelog

All notable changes to the Trellis project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

#### Trellis.Analyzers — inspection findings (m-A-1, m-A-2, m-A-3, m-A-5, i-A-1, i-A-2 + GPT-5.5 N-A-1, N-A-2)

Closes the formal Trellis.Analyzers inspection backlog from `files/analyzers-inspection-report.md` after a Phase-2 GPT-5.5 meta-review amended my self-inspection counts (19 analyzers + 4 code-fix providers + 2 type-system classes = 25 public types, not 20), refuted m-A-4 once I revalidated against the test infrastructure (the `EndsWith(".Microsoft.AspNetCore.Builder")` clause is required for analyzer tests because `AnalyzerTestHelper.WrapInNamespace` produces nested namespaces like `TestNamespace.Microsoft.AspNetCore.Builder`), and surfaced two NEW findings: 1 Major (N-A-2) and 1 Minor (N-A-1).

- **(Major) N-A-2 — `UnsafeResultDeconstructionAnalyzer` no longer accepts pre-deconstruction guards as proof of safety for the assignment form.** The analyzer's `IsGuardedRead` and `HasEarlyReturnGuard` walks now reject structural guards and early-return statements whose condition is authored *before* the deconstruction assignment. Previously, an `if (!success) return;` authored before the assignment-form `(success, value, error) = result;` (i.e., where `success` is an existing local) was accepted as a guard, which is a false negative because the pre-assignment value of `success` is unrelated to the freshly produced result triple. Threaded the deconstruction's `assignment.Span.End` into the guard walks so guards are only honored when their condition's `SpanStart` is at or after the assignment. The fresh-declaration form (`var (success, value, error) = result;`) is unaffected because there is no pre-existing local to gate on. Added a regression test (`ValueReadAfterStaleEarlyReturnGuard_BeforeAssignmentDeconstruction_ReportsDiagnostic`).

- **(Minor) N-A-1 — `DiagnosticDescriptors` field for TRLS013 renamed from `UnsafeValueInLinq` to `UnsafeMaybeValueInLinq` for parity with `TrellisDiagnosticIds.UnsafeMaybeValueInLinq`.** The api ref's "Constants" table referenced the constant name (`UnsafeMaybeValueInLinq`) while the "Descriptors" table referenced the field name (`UnsafeValueInLinq`); each name only existed on its respective surface. Now both surfaces use the canonical `UnsafeMaybeValueInLinq`. The old `UnsafeValueInLinq` field is retained as an `[Obsolete]` alias pointing at the same `DiagnosticDescriptor` instance — existing references still resolve to the same descriptor so `SupportedDiagnostics` registration and rule-set tooling keep working, with CS0618 nudging external consumers toward the canonical name. Added a regression test (`UnsafeValueInLinq_DescriptorAlias_PointsToSameInstance`).

- **(Minor) m-A-1 — api ref frontmatter `types:` corrected.** Previously `[TRLS001..TRLS022 diagnostic rules, TrellisDiagnosticIds]` — the first entry was descriptive prose, not a type name, breaking downstream tooling that expects `types:` to be a flat list. Updated to a flat list of 25 actual public types: `TrellisDiagnosticIds`, `DiagnosticDescriptors`, 19 analyzer classes, and 4 code-fix providers. Also updated `namespaces:` from `[Trellis.Analyzers]` to `[Trellis, Trellis.Analyzers]` because `TrellisDiagnosticIds` lives in the `Trellis` namespace.

- **(Minor) m-A-2 — api ref Descriptors table category column corrected.** Previously claimed sub-categories (`Trellis.Result`, `Trellis.Maybe`, `Trellis.EntityFrameworkCore`, `Trellis.Asp`, `Trellis.Primitives`) but `DiagnosticDescriptors.cs:10` defines a single shared `Category = "Trellis"` and every descriptor uses it. Users filtering by category in `.editorconfig` (`dotnet_diagnostic.category-Trellis.Result.severity = ...`) would silently no-op. Removed the misleading column; added an explicit note that all rules share the `Trellis` category and that severity should be configured by diagnostic ID.

- **(Minor) m-A-3 — `UnsafeValueInLinqAnalyzer` description softened in BOTH api ref and `DiagnosticDescriptors.UnsafeMaybeValueInLinq.Description`.** Previous wording said an earlier `.Where(x => x.HasValue)` "proves the access is safe", but the implementation in `ContainsPropertyCheck` is keyword-presence-only: any `.Where(...)` lambda mentioning `HasValue` anywhere in its body suppresses TRLS013, including `.Where(x => !x.HasValue).Select(x => x.Value)` (which still throws at runtime). Reworded both surfaces to state the actual behavior and call out the predicate-shape verification limitation.

- **(Minor) m-A-5 — `Trellis.Analyzers` README.md and NUGET_README.md "Key Features" lists expanded.** Previously listed 5 features and named only TRLS003, TRLS020, TRLS021, and SaveChanges. Added explicit mention of TRLS001/004/005/007/009/010 (ROP anti-patterns), TRLS018/019/022 (deconstruction, default literals, owned-entity init-only), and added a final bullet linking the api ref so the README doesn't need to enumerate every diagnostic.

- **(Info) i-A-1 — Test stub `Error` is non-abstract while production `Error` is an abstract record.** Documented in the inspection report. None of the existing analyzers gate on Error abstractness, so no current test masks a production-only bug. Out of scope for this PR; a follow-up could replace the stub with `abstract` + a derived `TestError` to harden against future analyzers that DO depend on Error abstractness.

- **(Info) i-A-2 — `AnalyzerReleases.Shipped.md` is empty; all 22 diagnostic IDs sit in `AnalyzerReleases.Unshipped.md`.** Roslyn release-tracking accepts diagnostics in either file, so RS2007/RS2008 do not fire. This is operational drift (release-time entries should move from Unshipped to Shipped), not a code bug. Out of scope for this PR.

- **(Info) i-A-3 — `MaybeQueryableExtensions.WhereXxx` cookbook reference asymmetry — REFUTED.** GPT-5.5 verified the analyzer api ref's shorthand and EF Core api ref's enumeration align (both reference `WhereHasValue/WhereNone/WhereEquals/WhereLessThan/WhereLessThanOrEqual/WhereGreaterThan/WhereGreaterThanOrEqual`). No actionable drift.

Refuted findings: m-A-4 (`CompositeValueObjectDtoConverterAnalyzer.IsAspNetCoreBuilderNamespace`'s `EndsWith(".Microsoft.AspNetCore.Builder")` clause is NOT dead code — it is REQUIRED by the analyzer test infrastructure: `AnalyzerTestHelper.WrapInNamespace` wraps every test source in `namespace TestNamespace { ... }`, so test fixtures declaring an asp-builder shim via `namespace Microsoft.AspNetCore.Builder { ... }` resolve to the nested namespace `TestNamespace.Microsoft.AspNetCore.Builder`. Without the suffix branch, the existing `MinimalApiRequestDto_WithOwnedValueObjectMissingConverter_ReportsDiagnostic` and `MinimalApiMethodGroupRequestDto_WithOwnedValueObjectMissingConverter_ReportsDiagnostic` tests stop firing. Added an explanatory comment in source so future inspectors don't repeat the mistake.). Same shape as the m-TA-2 refutation in the previous inspection: appearance-only "dead code" that is actually deliberate test-support accommodation.

Tests: **+2** new tests in `Trellis.Analyzers.Tests`:
- `ValueReadAfterStaleEarlyReturnGuard_BeforeAssignmentDeconstruction_ReportsDiagnostic` (N-A-2; confirmed RED before fix).
- `UnsafeValueInLinq_DescriptorAlias_PointsToSameInstance` (N-A-1; pins the obsolete alias still resolves to the same descriptor).

Standing two-step → single-step audit (per the PR-466 inspection-checklist memory): 0 actionable opportunities. Most candidate patterns iterate over short fixed arrays (2–9 elements) or already use `ImmutableHashSet<string>` / `ImmutableDictionary<string, string>` for fast lookup.

Pre-commit GPT-5.5 review confirmed all 7 fix points: N-A-2 `assignmentEnd` threading covers structural guards (if/while/ternary) AND early-return walks while leaving fresh-declaration form unaffected; N-A-1 alias points to the same `DiagnosticDescriptor` instance with no RS-rule duplicate-instance risk and no analyzer-release-tracking impact (release tracking is by ID `TRLS013`, not descriptor field name); m-A-4 refutation supported by existing Minimal-API tests that fail if the suffix branch is removed; doc surfaces aligned (api ref Descriptors table, Constants table, analyzer-class section, descriptor `description` field, README/NUGET_README all reference `UnsafeMaybeValueInLinq`). Surfaced **zero** additional blocking findings; one non-blocking note about CS0618 warning surfacing for external consumers under `TreatWarningsAsErrors`, addressed via tightened CHANGELOG wording (alias keeps source compatible while nudging migration through CS0618).

#### Trellis.Testing.AspNetCore — inspection findings (m-TA-1, m-TA-3..m-TA-7, i-TA-1, i-TA-2 + GPT-5.5 N-TA-1)

Closes the formal Trellis.Testing.AspNetCore inspection backlog from `files/testing-aspnetcore-inspection-report.md` after a Phase-2 GPT-5.5 meta-review validated 5 of 6 self-inspection findings, refuted m-TA-2 (the case-insensitive fallback in `ScenarioContext.TryResolve` is required because `Record` accepts public callers' arbitrary `IReadOnlyDictionary<string, string>` headers), promoted my refutation of `MsalTestTokenProvider.AcquireTokenAsync` testUserName null-check to a real Minor (m-TA-7), and surfaced 1 NEW Major (N-TA-1: UTF-8 BOM mishandling in `HttpFileParser`).

- **(Major) N-TA-1 — `HttpFileParser` now strips a leading UTF-8 BOM (U+FEFF) before line dispatch.** Many editors save `.http` files with a UTF-8 BOM by default; without stripping it, the first line of the file failed all dispatch checks (`line.StartsWith("###")` returned false because line[0] was U+FEFF, `line[0] == '@'` returned false for the same reason) and fell into `ParseRequestLine`, producing a bogus request with method `"\uFEFF@HOST"` and URL `"="`. The variable definition was lost; subsequent `{{host}}` substitutions resolved against an empty bag. The repository has a real BOM-prefixed `.http` file at `Examples/ConditionalRequestExample/api.http` that demonstrates the case. Fix: strip a single leading `'\uFEFF'` from `content` at the top of `Parse(...)` before splitting into lines. Added a regression test (`Parse_strips_leading_UTF8_BOM_so_first_line_variable_is_registered`).

- **(Minor) m-TA-1 — api ref frontmatter `types:` corrected.** Previously listed `[WebApplicationFactory<TEntryPoint>, TestClient, FakeTimeProvider, EntraTokenAcquirer, HttpFileReplayer]` — all 5 entries were wrong (some belong to other packages, others don't exist anywhere). Updated to the complete actual public-type list (16 types). The api ref body already documented the real surface; only the frontmatter was broken.

- **(Minor) m-TA-3 — `WithFakeTimeProvider` `out`-overloads now `ArgumentNullException.ThrowIfNull(factory)` at their entry points.** Previously the null-check happened 2 calls deep in the delegated `(FakeTimeProvider)` overload. Per GPT-5.5: not an observable param-name bug (the delegated check produces `paramName: "factory"` correctly), but inconsistent with the public-API entry-point guard pattern used elsewhere in the package. Added entry-point guards + regression tests.

- **(Minor) m-TA-4 — `CreateClientWithEntraTokenAsync` now null-checks `factory` and `testUserName`.** Previously only `tokenProvider` was null-checked. A null `factory` would NRE on `factory.CreateClient()` AFTER paying the cost of a real Entra token acquisition. A null `testUserName` flowed into `MsalTestTokenProvider.AcquireTokenAsync` which threw `ArgumentNullException(paramName: "key")` from `Dictionary.TryGetValue`, confusingly NOT matching the user's parameter name. Both companion `CreateClientWithActor` overloads already null-checked `factory`; this overload was the inconsistent one.

- **(Minor) m-TA-5 — `HttpFileTheoryData.FromFile` now `ArgumentNullException.ThrowIfNull(path)`.** Companion `HttpFileParser.ParseFile` already had the explicit guard. `File.ReadAllText` would also throw on null `path`, but the explicit guard at the public-API entry point keeps the surface uniform.

- **(Minor) m-TA-6 — `ScenarioContext.Record` now `ArgumentNullException.ThrowIfNull(headers)`.** Previously a null `headers` was stored in the `_named` dictionary and only NRE'd later in `TryResolve` (far from the misconfiguration site). Fails fast at `Record`'s entry now.

- **(Minor) m-TA-7 (GPT-5.5 promotion) — `MsalTestTokenProvider.AcquireTokenAsync` now null-checks `testUserName`.** Same shape as m-TA-4 but at the inner `AcquireTokenAsync` public surface. Previously `Dictionary.TryGetValue(null, ...)` threw `ArgumentNullException(paramName: "key")`. Defensive convention: public-API entry points get an explicit guard surfacing the right parameter name.

- **(Info) i-TA-1 — Two-step → single-step audit (per the PR-466 standing inspection-checklist memory).** Audited every two-step API pattern in the package. Result: only `ScenarioContext.TryResolve`'s case-insensitive fallback looked like a two-step that could collapse to a single TryGetValue, but GPT-5.5 correctly refuted that — the fallback IS required because `Record` accepts public callers' arbitrary `IReadOnlyDictionary<string, string>` (which may or may not be case-insensitive). Other audited locations: `HttpFileRunner.cs` content-header replace (no single-op equivalent in `HttpHeaders`); `ServiceCollectionDbProviderExtensions` Where-then-Remove (list-then-remove avoids in-iteration-modification); `IServiceCollection.RemoveAll<T>() + AddXxx` (canonical idiom; framework's `Replace(...)` has different semantics). Net: **0 actionable two-step → single-step opportunities** in this package.

- **(Info) i-TA-2 — `WebApplicationFactoryExtensions.CreateClientWithActor` JSON-building duplication.** The two overloads share ~10 lines of `JsonObject` construction. Refactoring was deferred — the duplication is harmless and the shapes differ slightly (the simpler overload uses empty `JsonArray` / `JsonObject` for forbidden-permissions / attributes rather than copying from an `Actor`).

Refuted findings: m-TA-2 (`ScenarioContext.TryResolve` case-insensitive fallback is NOT dead code; required by public `Record` accepting arbitrary `IReadOnlyDictionary<string, string>` — existing test `ScenarioContextTests.cs:177-181` records via case-sensitive `Dictionary<string, string>` and resolves `etag` against stored `ETag`); `MsalTestOptions` mutable `Dictionary<string, TestUserCredentials> TestUsers` (intentional configuration POCO); `HttpFileParser.SubstituteStaticVars` ordinal `IndexOf` (correct for `.http`-file byte-level parsing); `HttpFileRunner.RunSingleAsync` `client.BaseAddress` null-check (`HttpClient.SendAsync` documented to throw on relative URL with no BaseAddress); `MsalTestOptions.Scopes`/`TestUsers` getter-setter (configuration POCO).

Tests: **+8** new tests in `Trellis.Testing.AspNetCore.Tests`:
- `Parse_strips_leading_UTF8_BOM_so_first_line_variable_is_registered` (N-TA-1).
- `WithFakeTimeProvider_OutOverload_NullFactory_Throws_ArgumentNullException`, `WithFakeTimeProvider_OutOverloadWithStartInstant_NullFactory_Throws_ArgumentNullException` (m-TA-3).
- `CreateClientWithEntraTokenAsync_NullFactory_Throws_ArgumentNullException`, `CreateClientWithEntraTokenAsync_NullTestUserName_Throws_ArgumentNullException` (m-TA-4).
- `FromFile_NullPath_Throws_ArgumentNullException` (m-TA-5).
- `Record_NullHeaders_Throws_ArgumentNullException` (m-TA-6).
- `AcquireTokenAsync_NullTestUserName_Throws_ArgumentNullException` (m-TA-7, in new `MsalTestTokenProviderTests.cs`).

Pre-commit GPT-5.5 review confirmed all 7 fix points (correctness of BOM-strip, null-check ordering, out-overload guards before allocations, IL2026 suppression scope, test string construction, project test-globbing, no-network-trigger from null-tests, CHANGELOG-bullet/test-count alignment) and surfaced **zero** additional findings.

#### Trellis.Testing — inspection findings (m-T-1, m-T-2, m-T-3, i-T-1 + GPT-5.5 N-T-1..N-T-4)

Closes the formal Trellis.Testing inspection backlog from `files/testing-inspection-report.md` after a Phase-2 GPT-5.5 meta-review validated all 4 self-inspection findings and surfaced 4 additional ones (all Minor).

- **(Minor) m-T-1 — api ref frontmatter `types:` corrected.** Previously listed `[FakeRepository<,>, InMemoryUnitOfWork, ResultAssertions]` — `InMemoryUnitOfWork` doesn't exist anywhere in source, and 13 of 16 public types were missing from the metadata. Updated to the complete public type list, with multi-type-parameter generics quoted per the YAML flow-sequence convention established by PR #465.

- **(Minor) m-T-2 — `FakeRepository.SaveAsync` now `ArgumentNullException.ThrowIfNull(aggregate)`.** Previously a null `aggregate` produced an opaque `NullReferenceException` at `aggregate.Id`. The companion `Add(TAggregate)`, `Remove(TAggregate)`, `WithUniqueConstraint(Func<...>)`, `FindAsync(Func<...>)`, and both `WhereAsync(...)` overloads were already null-guarded; `SaveAsync` was the only public method missing the standard fail-fast guard.

- **(Minor) m-T-3 — `FakeRepository.Remove` and `DeleteAsync` now capture domain events before removal.** Previously both methods removed the aggregate from the in-memory store WITHOUT capturing its `UncommittedEvents()` into `PublishedEvents` or calling `AcceptChanges()`. Deletion-related domain events (e.g., `OrderCancelled`, `CustomerArchived`) raised on an aggregate before removal were silently lost — handlers that relied on the deletion event firing would pass against EF (whose `SaveChanges` flow captures events at commit time) and silently fail against the fake. Both methods now mirror the `Add`/`SaveAsync` pattern: append uncommitted events to `PublishedEvents`, call `AcceptChanges()`, then remove from the store. Both methods use `Dictionary.Remove(key, out value)` — a single dictionary operation that returns the TRACKED instance, so the captured events are always from the change-tracker's tracked aggregate, not from a detached instance the caller might pass in (mirroring EF's `SaveChanges` semantics).

- **(Minor) N-T-1 — `FakeRepository` now mirrors `RepositoryBase` read surface.** Added `QueryAsync(Specification<TAggregate>, CancellationToken)`, `ExistsAsync(TId, CancellationToken)`, `ExistsAsync(Specification<TAggregate>, CancellationToken)`, and `CountAsync(Specification<TAggregate>, CancellationToken)` — the four read-shape methods exposed by `Trellis.EntityFrameworkCore.RepositoryBase<TAggregate, TId>`. The api ref's claim that "the same `IRepository` contract works in both the EF and fake paths" is now actually true for the read surface. The legacy `FindAsync(Func<TAggregate, bool>)` / `WhereAsync(Func<TAggregate, bool>)` / `WhereAsync(Specification<TAggregate>)` helpers remain available for ad-hoc Func-based filtering; the api ref now recommends the `RepositoryBase`-shaped methods for same-contract test adapters.

- **(Minor) N-T-2 — `TestActorProvider` now null-guards `Actor` arguments.** Previously `new TestActorProvider((Actor)null!)` silently stored null as the default actor (`GetCurrentActorAsync` returned a null `Actor` through the `Task<Actor>` result), and `provider.WithActor((Actor)null!)` silently set the `AsyncLocal` slot to null which `GetCurrentActorAsync` then coalesced to the default actor — silently turning "swap to a restricted actor" into "swap to the default actor". Both call sites now `ArgumentNullException.ThrowIfNull(actor)`.

- **(Minor) N-T-3 — `BeFailureOfType<TError>` and `BeOfType<TError>` no longer throw `InvalidCastException` under an active `AssertionScope`.** Previously both methods recorded the type-mismatch failure via `Execute.Assertion.ForCondition(...).FailWith(...)` but then unconditionally cast `(TError)error!` / `(TError)Subject!`. Without an active FluentAssertions `AssertionScope`, `FailWith` aborts before the cast and the bug never fires; with an active scope the failure is collected and execution continues, so the wrong-type cast throws `InvalidCastException` and masks the intended assertion-failure message. Both methods now return `default(TError)!` on the wrong-type branch (mirroring the guarded pattern that `ErrorAssertions.BeOfType<TError>` already used for the null-subject branch). Tests inside an `AssertionScope` get the clean recorded assertion failure instead.

- **(Minor) N-T-4 — `Task<Result<T>>` async assertion extensions now null-guard the receiver task.** Previously `((Task<Result<int>>)null!).BeSuccessAsync()` produced an opaque `NullReferenceException` at the await site. `UnwrapAsync(this Task<Result<T>>)` already had the guard; the assertion extensions did not. Added `ArgumentNullException.ThrowIfNull(resultTask)` to all three `Task<Result<T>>` overloads (`BeSuccessAsync`, `BeFailureAsync`, `BeFailureOfTypeAsync`). The `ValueTask<Result<T>>` overloads don't need the guard (value types).

- **(Info) i-T-1 — `FakeRepository` async-method `CancellationToken` semantics now documented uniformly.** All `*Async` methods accept a `CancellationToken` for source-compat with `RepositoryBase` but don't observe it (the fake completes synchronously). The `RemoveByIdAsync` xmldoc previously called this out; the others didn't. Added an explicit "(accepted for source-compat with `RepositoryBase`; not observed — the fake completes synchronously)" note to the xmldoc on `GetByIdAsync`, `FindByIdAsync`, `SaveAsync`, `DeleteAsync`, and the four new `RepositoryBase`-mirror methods. Added a class-level remark in the api ref under the `FakeRepository<TAggregate, TId>` block.

Refuted findings: `AggregateTestMutator` reflection helpers' AOT story is correct (carry `[RequiresUnreferencedCode]` + DAM/suppression annotations); `TestActorScope.Dispose()` thread-safety concern (intentional single-flow design); `HaveErrorDetail` / `HaveErrorDetailContaining` `error!.Detail!` "double-bang" (the `!` only suppresses the compiler nullable warning; FluentAssertions handles null subjects gracefully); `FakeRepository.GetByIdAsync` / `FindByIdAsync` not null-checking `id` (`TId : notnull` constraint); `WithUniqueConstraint` concurrent mutation (single-threaded test fake by contract).

Pre-commit GPT-5.5 review confirmed all 8 fix points and surfaced 4 cross-cutting follow-ups, all addressed in the same commit:

- **`Remove(TAggregate)` no-op-for-untracked semantics restored.** The m-T-3 fix unconditionally captured events + called `AcceptChanges` even for aggregates not in the store, breaking the documented "Remove of unknown aggregate is a no-op" contract. Wrapped the event-capture block in `if (_store.ContainsKey(aggregate.Id))` and added a regression test (`Remove_Untracked_Aggregate_Is_NoOp_Does_Not_Publish_Events`) pinning the no-op contract.
- **N-T-3 chained-`.Which` behavior verified and documented (not a limitation).** Pre-commit GPT-5.5 raised a concern that chaining `.Which.Foo.Should()...` on a wrong-type `BeFailureOfType<TError>()` under an active `AssertionScope` could throw `NullReferenceException` and mask the recorded assertion failure. Empirically verified the actual behavior: when the chained NRE fires inside the `using var scope` block, the surrounding `scope.Dispose()` runs in the language finally block and raises the recorded assertion failure as `XunitException`, which masks the chained NRE. Net effect: the test still sees the "expected `TError`, found ..." assertion message. Updated the xmldoc on both `BeFailureOfType<TError>` and `BeOfType<TError>` to describe the actual behavior accurately, and added a regression test (`BeFailureOfType_Wrong_Type_With_Which_Chain_Inside_AssertionScope_Still_Surfaces_Recorded_Assertion_Failure`) pinning the observed Dispose-flush-masks-NRE order.
- **Cookbook Recipe 15 `WhereAsync(spec.ToExpression(), ct)` corrected to `QueryAsync(spec, ct)`.** The cookbook recipe-15 example invoked `fake.WhereAsync(spec.ToExpression(), ct)` which doesn't compile — `FakeRepository` has no `WhereAsync(Expression<...>, CancellationToken)` overload. The recipe text recommended single-sourcing predicates via `Specification<T>`, but the snippet did exactly the opposite. Updated to use the new `QueryAsync(Specification<T>, CancellationToken)` (added in N-T-1) which preserves the single-sourcing intent.
- **`AggregateTestMutator` AOT/trim warning surfaced in api ref.** The source carries `[RequiresUnreferencedCode]` on both public methods but the api ref code block didn't show the attribute. Added the attribute to the documented signatures and a "AOT/trim incompatibility" callout explaining that AOT-published consumers will receive IL2026 / IL3050 at the call site.

PR-466 round-1 review surfaced 3 additional follow-ups, all addressed in the same commit:

- **`Remove(TAggregate)` and `DeleteAsync(TId)` now use `Dictionary.Remove(key, out value)`.** The pre-commit `if (_store.ContainsKey(...))` + `_store.Remove(...)` pattern was two dictionary operations and the Remove-side comment misleadingly said the lookup-and-remove was "atomic". Switched to `Dictionary.Remove(key, out value)` — genuinely a single operation. Side benefit: the `out` parameter returns the TRACKED instance, so when a caller passes a different aggregate instance with the same ID (e.g. a re-loaded copy), `Remove` now correctly captures events from and AcceptChanges-on the tracked instance, mirroring EF's `SaveChanges` semantics. Added regression test `Remove_With_Different_Instance_Same_Id_Operates_On_Tracked_Instance_Not_Passed_In`.
- **CHANGELOG "atomic" wording removed.** The previous wording "DeleteAsync looks up the aggregate via TryGetValue first so the lookup-and-remove is atomic" misleadingly implied thread-safety; reworded to describe the actual `Dictionary.Remove(key, out value)` single-op semantics.
- **CHANGELOG duplicate "Tests:" sections consolidated.** A draft-vs-final wording oversight left two "Tests: +N new tests" sections in the same Trellis.Testing entry. Consolidated into a single section reflecting the final +15 count (the original 14 plus the new `Remove_With_Different_Instance_Same_Id_Operates_On_Tracked_Instance_Not_Passed_In` regression test).

Tests: **+15** new tests in `Trellis.Testing.Tests`:
- `SaveAsync_Should_Throw_ArgumentNullException_For_Null` (m-T-2).
- `Remove_Should_Capture_Domain_Events_Before_Removing`, `Remove_Untracked_Aggregate_Is_NoOp_Does_Not_Publish_Events`, `Remove_With_Different_Instance_Same_Id_Operates_On_Tracked_Instance_Not_Passed_In`, `DeleteAsync_Should_Capture_Domain_Events_Before_Removing` (m-T-3 + pre-commit + PR-round-1 follow-ups).
- `QueryAsync_With_Specification_Returns_Matching_Aggregates`, `ExistsAsync_With_Id_Returns_True_When_Aggregate_Exists`, `ExistsAsync_With_Specification_Returns_True_When_Any_Match`, `CountAsync_With_Specification_Returns_Match_Count` (N-T-1).
- `Constructor_WithNullActor_Throws_ArgumentNullException`, `WithActor_NullActor_Throws_ArgumentNullException` (N-T-2).
- `BeFailureOfType_Wrong_Type_Inside_AssertionScope_Reports_Assertion_Failure_Without_InvalidCastException`, `BeFailureOfType_Wrong_Type_With_Which_Chain_Inside_AssertionScope_Still_Surfaces_Recorded_Assertion_Failure`, `BeOfType_Wrong_Type_Inside_AssertionScope_Reports_Assertion_Failure_Without_InvalidCastException` (N-T-3 + pre-commit follow-up).
- `BeSuccessAsync_Task_Receiver_Null_Throws_ArgumentNullException`, `BeFailureAsync_Task_Receiver_Null_Throws_ArgumentNullException`, `BeFailureOfTypeAsync_Task_Receiver_Null_Throws_ArgumentNullException` (N-T-4).

#### Trellis.StateMachine — inspection findings (m-SM-1, m-SM-2, i-SM-1 + GPT-5.5 N-SM-1..N-SM-4)

Closes the formal Trellis.StateMachine inspection backlog from `files/statemachine-inspection-report.md` after a Phase-2 GPT-5.5 meta-review validated all 3 self-inspection findings and surfaced 4 additional ones (3 Minor doc-drift findings and 1 Info scope-boundary documentation). All findings are documentation-only or source-comment-only — no behavioral changes; no new tests.

- **(Minor) m-SM-1 — api ref frontmatter `types:` corrected.** Previously listed `["IStateMachine<,>", "StateBuilder<,>", "Transition<,>"]` — none of which exist in source. Updated to `[StateMachineExtensions, LazyStateMachine<TState, TTrigger>]`.

- **(Minor) m-SM-2 — api ref Cross-references duplicate `trellis-api-core.md` entry replaced.** Previously had two identical entries. Replaced one with a `trellis-api-cookbook.md` cross-reference (Recipe 9) and added a `trellis-api-asp.md` link to clarify how `Error.UnprocessableContent` renders as HTTP 422.

- **(Minor) N-SM-1 — `OnUnhandledTrigger` swallow docs no longer overstate "state unchanged".** The implementation calls `Result.Ok(stateMachine.State)` AFTER the user's callback runs, not on a snapshot. If the callback mutates or reroutes state, the surfaced state reflects the post-callback state. Updated wording in the api ref method-table row, api ref Behavioral notes, integration article surface table, cookbook Recipe 9 side-effects subsection, AND the source-code comment to say "the state read AFTER the callback runs (normally unchanged unless the callback itself mutates or reroutes state)".

- **(Minor) N-SM-2 — Cookbook Recipe 9 now imports `Trellis.StateMachine`.** The recipe prose calls `machine.FireResult(...)` but the `using` block previously had only `using Stateless;` and `using Trellis;`. `FireResult` is an extension method in `Trellis.StateMachine`, so the recipe as shown wouldn't compile when copy-pasted. Added the missing `using Trellis.StateMachine;`. (The compiled snippet at `Examples/CookbookSnippets/Recipe09_StateMachine.cs` already had it.)

- **(Minor) N-SM-3 — Cookbook namespace guidance no longer attributes `StateMachine<TState, TTrigger>` to the wrong namespace.** Line 1578 said `using Trellis.StateMachine;` is needed "for `StateMachine<TState, TTrigger>`" — wrong; that type is from the upstream `Stateless` namespace. Trellis.StateMachine provides the `FireResult` extension and `LazyStateMachine`. Reworded to: `using Stateless;` for `StateMachine<TState, TTrigger>` and `using Trellis.StateMachine;` for `FireResult` / `LazyStateMachine`.

- **(Info) i-SM-1 — `FireResult` invalid-transition Detail dual-write rationale documented.** The implementation populates the same string into both `RuleViolation.Detail` (via `Error.UnprocessableContent.ForRule(detail)`) AND the outer `Error.Detail` (via `with { Detail = detail }`). This dual write is needed because `Trellis.Asp.ResponseFailureWriter` reads from BOTH surfaces — top-level `Problem Details.detail` from `Error.Detail`, per-rule context from `RuleViolation.Detail`. Added an inline source comment explaining the rationale so a future maintainer doesn't "simplify" by removing one and break either the unit test (which asserts on `err.Detail`) or the wire format.

- **(Info) N-SM-4 — async / parameterized-trigger scope boundary documented.** `Trellis.StateMachine` deliberately wraps only the synchronous, parameterless trigger shape `Fire(TTrigger)`. Stateless also supports `FireAsync(...)` (for `OnEntryAsync`/`OnExitAsync` callbacks) and `Trigger<TArg>` parameterized triggers — neither is wrapped today. Added a "Scope boundary — async and parameterized triggers" section to the api ref documenting that consumers needing those shapes must drop to raw Stateless APIs and translate exceptions themselves; if a future Trellis version adds `FireResultAsync` / parameterized overloads, they will follow the same `CanFire` pre-check + `OnUnhandledTrigger`-policy-preserving design and will use library-source `ConfigureAwait(false)` on awaited Stateless operations.

Refuted findings: `FireResult` evaluates guards twice (documented + tested as `at most twice` contract); `LazyStateMachine._machine ??= CreateMachine()` race (documented as not thread-safe — DDD aggregates are single-threaded consistency boundaries); `FireResult` doesn't null-check `stateMachine` receiver (documented behavior); article filename `state-machines.md` doesn't follow `integration-<pkg>.md` convention (intentional — positioned under "Building Blocks", not "Integration Guides").

Tests: **+0** new tests. All findings are doc-only or source-comment-only; existing 38 tests already cover the contract surface comprehensively. Pre-commit GPT-5.5 review confirmed all 7 fix points and surfaced 2 cross-cutting follow-ups, both addressed in the same commit:

- N-SM-4 wording referenced a Stateless API named `Trigger<TArg>` that doesn't exist in Stateless 5.20.1; corrected to `SetTriggerParameters<TArg>(...)` returning `TriggerWithParameters<TArg>`, fired via `Fire(triggerWithParameters, arg)` / `FireAsync(triggerWithParameters, arg)`.
- `docs/docfx_project/adr/ADR-001-result-api-surface.md:371` cited `Trellis.StateMachine.StateMachineExtensions.FireResult` as an example of a 409 conflict case — stale cross-doc contradiction, since the package settled on HTTP 422 (`Error.UnprocessableContent`). Replaced with a clarifying note that state-machine guard failures map to 422.

#### Trellis.FluentValidation — inspection findings (m-FV-1..m-FV-4, i-FV-2 + GPT-5.5 N-FV-1)

Closes the formal Trellis.FluentValidation inspection backlog from `files/fluentvalidation-inspection-report.md` after a Phase-2 GPT-5.5 meta-review validated 5 of 6 self-inspection findings, refuted i-FV-1 (`/`-prefixed-input pass-through is documented escape-hatch + `InputPointer` validates escape sequences), and surfaced 1 additional Minor (N-FV-1: `ToResult<T>` doc/code drift). Pre-commit GPT-5.5 review confirmed all 6 fix points and surfaced 1 stale-wording cleanup (api ref Behavioral notes "Grouping rule" bullet + incomplete `ValidateToResultAsync` cancellation note); both addressed in the same commit. The "stale generated DocFX YAML" finding from the pre-commit review is moot — `*.yml` is gitignored under `docs/docfx_project/api/` and CI regenerates metadata fresh.

- **(Minor) m-FV-1 — api ref frontmatter `types:` corrected.** Previously listed `[TrellisValidator<T>, ValidationResultExtensions]` — neither type exists in source. Updated to `[FluentValidationServiceCollectionExtensions, FluentValidationMessageValidatorAdapter<TMessage>, FluentValidationResultExtensions]`.

- **(Minor) m-FV-2 — `FluentValidationMessageValidatorAdapter<TMessage>` constructor null-guards `validators`.** Previously the public primary-constructor parameter was captured directly; passing `null!` would defer the failure to a `NullReferenceException` at the first `foreach (var validator in validators)`. Converted to a traditional constructor with `ArgumentNullException.ThrowIfNull(validators)` at the entry point. Direct construction is reachable from tests (e.g. `new FluentValidationMessageValidatorAdapter<CreateUserCommand>([])` in the test suite), so this is a real public-API surface, not just a DI internal.

- **(Minor) m-FV-3 — `FluentValidationExtension.cs` renamed to `FluentValidationResultExtensions.cs`.** Pure file rename to match the framework convention "one type per file with matching name". The other three source files in the package already follow this convention.

- **(Minor) m-FV-4 — `AddTrellisFluentValidation()` idempotency check now uses `TryAddEnumerable`.** Replaced the manual `services.Any(d => ...)` linear scan with `services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IMessageValidator<>), typeof(FluentValidationMessageValidatorAdapter<>)))` — the canonical .NET pattern matching `Trellis.Mediator.AddTrellisBehaviors`. Behavior unchanged: deduplicates by `(ServiceType, ImplementationType)` so repeated calls register the open-generic adapter exactly once.

- **(Minor) N-FV-1 — `ToResult<T>` documentation corrected.** The api ref method-table row and the source xmldoc both contained behavior claims that didn't match the implementation:
  - api ref claimed it "groups `validationResult.Errors` by property name" — implementation does NOT group; emits one `FieldViolation` per `ValidationFailure`. Multiple failures on the same property produce multiple violations (no information loss).
  - api ref claimed it uses `FieldViolation(InputPointer.ForProperty(propName), ...)` — implementation uses `new InputPointer(JsonPointerNormalizer.ToJsonPointer(rawName))`.
  - source xmldoc `<returns>` said failure "if validation failed or value is null" — implementation returns `Result.Ok(value)` whenever `validationResult.IsValid`, regardless of `value` being null. The api ref's behavioral notes correctly stated this; only the methods-table row and xmldoc were wrong.
  All three drifts now corrected; api ref + xmldoc reflect the actual contract.

- **(Info) i-FV-2 — `ValidateToResultAsync` now observes cancellation before the null-value short-circuit.** Previously a caller passing a cancelled `CancellationToken` AND a null `value` received a `Failure` result rather than the `OperationCanceledException` the documented "Cancellation token to observe" contract implies. Added `cancellationToken.ThrowIfCancellationRequested()` at the top of the method (after the `validator` null-check) so cancellation always wins over null-value short-circuit.

Refuted findings: i-FV-1 (`JsonPointerNormalizer` `/`-prefixed pass-through is documented escape-hatch behavior; `InputPointer` rejects malformed `~` sequences anyway, so silent corruption is not possible). Refuted candidate findings: PII leakage via `failure.ErrorMessage` (FluentValidation message templates are consumer-controlled — Trellis must not silently rewrite them); open-generic `IMessageValidator<>` registration AOT compatibility (open-generic DI is AOT-safe; the scanning overload correctly carries `[RequiresUnreferencedCode]`).

Tests: **+2** new tests in `Trellis.FluentValidation.Tests`:
- `Constructor_throws_ArgumentNullException_when_validators_is_null` (m-FV-2).
- `ValidateToResultAsync_NullValue_CancelledToken_observes_cancellation` (i-FV-2).

#### Trellis.ServiceDefaults — inspection findings (M-S1, M-S2, m-S1..m-S3, i-S1..i-S4 + GPT-5.5 N-S1..N-S4)

Closes the formal Trellis.ServiceDefaults inspection backlog from `files/servicedefaults-inspection-report.md` after a meta-review by GPT-5.5 validated all 9 self-inspection findings and surfaced 4 additional ones (one Major, two Minor, one Info).

- **(Major) M-S1 — `UseEntityFrameworkUnitOfWork<TContext>()` now throws on duplicate call.** Previously chaining `.UseEntityFrameworkUnitOfWork<DbContextA>().UseEntityFrameworkUnitOfWork<DbContextB>()` silently overwrote the first registration so only `DbContextB`'s UoW was wired. The actor-provider slot already enforces fail-fast; UoW now matches that policy. Same-`TContext` duplicates also throw — the Trellis pipeline supports exactly one transactional `IUnitOfWork` per composition; chaining is always misconfiguration. Read/write context splits should run as separate composition roots or use a multi-tenant `DbContext`.

- **(Major) M-S2 — AOT/trim incompatibility now documented prominently.** The package opts out of AOT/trim analyzers (`<IsAotCompatible>false</IsAotCompatible>`, `<EnableAotAnalyzer>false</EnableAotAnalyzer>`) and the fluent assembly-scanning methods (`UseFluentValidation(asm)`, `UseResourceAuthorization(asm)`, `UseDomainEvents(asm)`) wrap underlying `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` APIs without propagating the attributes — so downstream AOT consumers do NOT receive IL2026/IL3050 warnings at the wrapper call site. New "AOT compatibility" section in api ref + integration article + NUGET_README documents the limitation and recommends per-package direct APIs (with the AOT-friendly mapping table) for AOT/trim consumers. The parameterless `o.UseFluentValidation()` / `o.UseResourceAuthorization()` / `o.UseDomainEvents()` overloads are AOT-compatible.

- **(Major) N-S1 — explicit `AddResourceAuthorization<TMessage, TResource, TResponse>()` calls made BEFORE `AddTrellis(...)` are now order-independent.** `InsertResourceAuthorizationBehavior` (in `Trellis.Mediator`) inserts before `ValidationBehavior<,>` if it exists; otherwise it appended to the end of the descriptor list. When `AddTrellis(...)` then ran, the standard pipeline was registered AFTER the closed-generic resource-auth behaviors, so they ended up at descriptor slot 0 — outside the canonical Exception/Tracing/Logging/Authorization/Validation envelope. `AddTrellisBehaviors()` now performs a relocation pass after registering the standard pipeline: any pre-existing closed-generic `ResourceAuthorizationBehavior<,,>` descriptors are re-inserted immediately before `ValidationBehavior`, mirroring the `AddTrellisUnitOfWork ↔ AddDomainEventDispatch` symmetry.

- **(Info) N-S4 — new `UseCachingActorProvider<T>()` builder slot.** `Trellis.Asp` exposes `AddCachingActorProvider<T>()` for per-request caching of an inner `IActorProvider`, but the builder didn't expose a slot. Now does, with the same fail-fast duplicate-detection as the other actor-provider slots. Chain after the matching `UseXxxActorProvider(...)` so the inner provider's `IOptions<TOptions>` is configured before the wrap replaces the `IActorProvider` slot.

- **(Minor) m-S1 — api ref frontmatter `types:` corrected.** Previously listed `[AddTrellisServiceDefaults, WebApplicationBuilderExtensions]` — neither type exists in source. Updated to `[TrellisServiceCollectionExtensions, TrellisServiceBuilder]`.

- **(Minor) m-S2 — Behavior section step 6 mentions `IDomainEventPublisher`.** The api ref previously said "registers `DomainEventDispatchBehavior<,>` and any scanned handlers", omitting the default `IDomainEventPublisher` registration that the existing test `UseDomainEvents_WithoutAssemblies_RegistersDispatchBehaviorAndPublisher` confirms. Step 6 now explicitly mentions all three.

- **(Minor) m-S3 — `Apply()` else-if branches are explicit about the flag invariant.** The second branch in each `_useFluentValidation`/`_useDomainEvents` pair previously checked only `_xxxAssemblies.Count > 0`, relying on the implicit invariant that those private lists are only mutated by methods that also set the flag. Now the branches are `else if (_useFluentValidation)` / `else if (_useDomainEvents)` — equivalent today, robust against future refactor.

- **(Minor) N-S2 — new integration article `docs/docfx_project/articles/integration-servicedefaults.md`.** ServiceDefaults previously had only an api ref — convention is every package has both a per-LLM api ref and a per-developer integration article. Article added with quick start, canonical order, mutually-exclusive slots, per-request actor caching example, the order-independence section for explicit resource-auth registrations, AOT compatibility note, and layered-config usage. Also added to `articles/toc.yml` under "Integration Guides".

- **(Minor) N-S3 — `Examples/CookbookSnippets/Recipe12_DiPlaybook.cs` rewritten to use the `AddTrellis(...)` builder.** The cookbook prose for Recipe 12 already taught the builder pattern; the compiled snippet still demonstrated direct package-specific registrations, contradicting the recipe. Snippet now uses the builder consistently.

- **(Info) i-S1 — xmldoc `<remarks>` on `UseAsp` and `UseMediator` document the repeated-call combine semantics** (configure delegates compose in call order rather than overwriting, supporting layered library-then-host configuration).

- **(Info) i-S2 — api ref clarifies `UseResourceAuthorization()` no-assembly mechanism.** New "Order-independence" subsection in the Behavior section explains that `AuthorizationBehavior<,>` (static-permission flavor) is registered unconditionally by `AddTrellisBehaviors()` (called via `UseMediator()`), and that the no-assembly path is for AOT consumers who register each `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` explicitly via `services.AddResourceAuthorization<TMessage, TResource, TResponse>()`.

- **(Info) i-S3 — `TrellisServiceBuilder` class summary now documents the `Apply()` lifecycle.** Callers don't invoke `Apply()` directly; `AddTrellis(services, configure)` constructs the builder, hands it to the configure callback, and invokes `Apply()` after the callback returns. `<remarks>` paragraph now states this explicitly.

Refuted findings (kept current behavior intentional): i-S4 (`UseEntityFrameworkUnitOfWork<TContext>` lacks a configure delegate — forward-looking only; no concrete need today). Multiple `AddTrellis(...)` calls within one composition (`AddDomainEventDispatch` and `AddTrellisUnitOfWork` are aware of each other and re-order regardless of registration order). `Apply()` thread safety (builder is constructed/configured/applied/discarded synchronously inside `AddTrellis(...)`).

Tests: **+5** new tests in `Trellis.ServiceDefaults.Tests` covering UoW duplicate-call throw (same context + different context), explicit resource-auth before `AddTrellis` ordering, `UseCachingActorProvider<T>()` wrap behavior, and caching-slot duplicate throw.

#### Trellis.Http — inspection findings (M-H1, M-H2, M-H3, i-H2, i-H3, N-H1, N-H2)

Closes the formal Trellis.Http inspection backlog from `files/http-inspection-report.md` after a meta-review by GPT-5.5 validated, refuted, or adjusted each finding and surfaced 2 additional ones.

- **(Minor) i-H2 — `MapStatusToError` now extracts response headers into the typed errors.** Previously produced typed errors with empty arrays / zero values (`Error.MethodNotAllowed(Allow: empty)`, `Error.TooManyRequests(RetryAfter: null)`, etc.) regardless of what the upstream sent. Since `Trellis.Asp`'s response writer renders these typed payloads on the wire (the `Allow` header is taken from `Error.MethodNotAllowed.Allow`; `Retry-After` from `Error.TooManyRequests.RetryAfter` / `Error.ServiceUnavailable.RetryAfter`; `WWW-Authenticate` from `Error.Unauthorized.Challenges`; `Content-Range` from `Error.RangeNotSatisfiable.Unit` + `CompleteLength`), the empty placeholders were actively wrong UX rather than just incomplete. The mapper now reads:
  - `401` → copies `Headers.WwwAuthenticate` schemes (e.g. `Bearer`, `Basic`) **plus a best-effort RFC 7235 parameter parse** (`realm`, `error`, `error_description`, etc.) into `Error.Unauthorized.Challenges` so the full challenge round-trips through ASP. Parse failures fall back to scheme-only.
  - `405` → copies `Content.Headers.Allow` into `Error.MethodNotAllowed.Allow`.
  - `416` → copies `Content.Headers.ContentRange.Length` into `Error.RangeNotSatisfiable.CompleteLength` **and `Content.Headers.ContentRange.Unit` into `Error.RangeNotSatisfiable.Unit`** so a custom unit (e.g. `items`) round-trips instead of being rewritten as `bytes`.
  - `429` / `503` → copies `Headers.RetryAfter` (delta seconds or HTTP date) into `Error.TooManyRequests.RetryAfter` / `Error.ServiceUnavailable.RetryAfter`. Malformed negative deltas (an adversarial / buggy upstream pattern) are treated as absent rather than crashing the mapper.

  When the upstream omits the header, the typed error keeps its empty/null default — the mapper never invents values. **Two exceptions:** `405` without `Allow` and `416` without `Content-Range` fall through to `Error.InternalServerError` per the round-4/round-5 follow-up below; producing typed errors with default empty/zero values for those two cases would let ASP fabricate misleading wire headers.
- **(Minor) M-H1 — `Handle*Async` methods now `ArgumentNullException.ThrowIfNull(error)`.** Previously `error` was deferred to `Result.Fail<T>(error)` (which itself throws on null), but the throw only happened on the matched-status path. A null `error` on a non-matching status was silently ignored. Aligns with the framework's defensive-coding posture and matches the existing `response` null-guard.
- **(Minor) M-H2 — `ReadJsonAsync` / `ReadJsonMaybeAsync` move the `jsonTypeInfo` null check inside the `try` / `finally` so the awaited `HttpResponseMessage` is disposed even when `jsonTypeInfo` is null.** Previously the ANE thrown by `ArgumentNullException.ThrowIfNull(jsonTypeInfo)` skipped the `finally` block, leaking the response (deterministic disposal violated). The class-level disposal contract now holds on every exception path, including null-jsonTypeInfo.
- **(Info) M-H3 — `ReadJsonAsync`'s caught `JsonException` no longer interpolates `ex.Message` or `ex.Path` into the failure `Detail`.** GPT-5.5 pre-commit review caught that `ex.Message` removal alone wasn't enough: `JsonException.Path` can also contain user-controlled dictionary keys (e.g. `$.customers['alice@example.com']`) for object-key payloads. The detail now uses only `LineNumber` / `BytePositionInLine` — schema-free position diagnostics that don't echo upstream-supplied content.
- **(Info) i-H3 — Network/cancellation/JSON exception propagation** is now documented in `trellis-api-http.md` and `integration-http.md`. `HttpRequestException`, `OperationCanceledException` / `TaskCanceledException`, and `JsonException` from **both** `ReadJsonMaybeAsync<T>` and `ReadJsonOrNoneOn404Async<T>` (which delegates to `ReadJsonMaybeAsync<T>` for non-404 statuses) propagate through the chain rather than being mapped to `Result.Fail`. This was always the case but wasn't documented; readers could reasonably believe `ToResultAsync()` always returned a `Result` and never threw.
- **(Minor) N-H1 — API-reference frontmatter type list corrected.** `trellis-api-http.md` previously listed `[HttpResponseMessageExtensions, HttpClientResultExtensions]` — those names don't exist in source. Updated to `[HttpResponseExtensions]`.
- **(Minor) N-H2 — 3xx-redirect handling under the strict default is now documented.** `HttpClient` follows redirects automatically by default; callers who set `AllowAutoRedirect = false` (e.g. SSO landing-page detection) get `Error.InternalServerError` for the unhandled 3xx because it falls through `MapStatusToError`. The strict-default doc section in both `trellis-api-http.md` and `integration-http.md` now flags this and recommends `ToResultAsync(statusMap)` for redirect-aware callers.

Refuted findings (kept current behavior intentional and documented): i-H1 (`Error` ADT has no inner-exception slot — design intent); body-aware mapper cancellation path is already correct (catch-rethrow with disposal); `ReadJsonOrNoneOn404Async` has no double-dispose; 429 ctor usage is valid (`RetryAfter` is optional); multi-await is caller misuse; `statusMap` turning 2xx into failure is documented; `ReadJsonAsync` non-success fallback is documented; `HandleForbiddenAsync` deletion is deliberate; `ResourceRef.For("HttpResponse")` is semantically valid.

- **(Minor) Copilot review round 2 / round 3 / round 4 / round 5 / round 6 — round-trip fidelity for absent or unusable upstream headers + remaining disposal-contract gaps + Retry-After overflow.** The bot iterated 5 times across these themes. The final converged design:
  - **Round 2** flagged that an upstream 405/416 omitting its required header produces synthetic typed errors (`new Error.MethodNotAllowed(empty)` / `new Error.RangeNotSatisfiable(0)`) which then render as misleading wire headers. **Round 3** correctly pushed back on a renderer-side fix that would have overloaded the public type semantics. **Round 4** escalated the concern to the mapper side. **Round 5** caught a regression in the round-4 fix: keying the 416 typed mapping on `Length > 0` conflated "Content-Range absent" with "legitimately `bytes */0` (empty resource)". The accepted resolution: in `MapStatusToError`, **fall through to `Error.InternalServerError`** when the relevant header is absent, empty, or carries no usable typed context (RFC 9110 §15.5.6 says 405 MUST include `Allow`; §15.5.17 says 416 SHOULD include `Content-Range`). Specifically: 405 falls through when `Allow` is missing or empty; 416 falls through when `Content-Range` is missing or its complete-length is unspecified (e.g. `bytes 0-99/*`). When the header is present with a non-empty list / known length, the typed error is produced — including `RangeNotSatisfiable(0, "bytes")` for the legitimate empty-resource 416 case.
  - **Round 4 also flagged a real resource leak** in the round-1 `Handle*Async` `null`-`error` guards: `client.GetAsync(...).HandleNotFoundAsync(null!)` would throw before awaiting the in-flight response Task, leaving the eventual `HttpResponseMessage` unowned and unreleased. Reordered all three `Handle*Async` methods (`HandleNotFoundAsync` / `HandleConflictAsync` / `HandleUnauthorizedAsync`) so they `await` first, then null-check `error` and dispose the message before throwing. The trade-off: a programmer's `null!` bug is delayed by the full HTTP round-trip, but the disposal contract is honored.
  - **Round 6 closed two more disposal-contract gaps** that the round-1/round-4 fixes had missed: the body-aware `ToResultAsync(mapper, ct)` overload also had `ArgumentNullException.ThrowIfNull(mapper)` running BEFORE the response-task await, and `ReadJsonOrNoneOn404Async` had the same shape for its `jsonTypeInfo` guard. Both reordered to await-then-dispose-then-throw.
  - **Round 6 also documented and pinned the `Retry-After` overflow contract.** RFC 9110 §10.2.3 permits arbitrary `delay-seconds` (1*DIGIT, no upper bound), but the typed `RetryAfterValue` uses `int`. The bot flagged that the previous code claimed to "clamp huge deltas to `int.MaxValue`" — implying a wire `Retry-After: 5000000000` would round-trip through ASP as ~68 years. In practice .NET's `HttpResponseHeaders` parser rejects out-of-range `delay-seconds` at the wire-parsing layer (`Headers.RetryAfter` returns `null`), so the clamp branch was unreachable. The mapper's guard was rewritten to treat overflow as absent (`null`) rather than clamp, the misleading xmldoc was corrected, and a new wire-form test (`TryAddWithoutValidation("Retry-After", "9999999999")`) pins the end-to-end contract: regardless of where in the stack the rejection happens, the typed error's `RetryAfter` is `null` and no misleading clamped value round-trips.
  - **Documented limitation: token68 round-trip.** RFC 7235 also defines a `token68` form (`WWW-Authenticate: Negotiate <base64-token>`) used by SPNEGO/Negotiate/NTLM for multi-step authentication. `AuthChallenge` has no slot for the bare token, so when an upstream sends a token68-form challenge `BuildChallenge` captures only the scheme and the token is dropped on round-trip. Documented in `BuildChallenge` xmldoc and the 401 rows of both api ref and article; callers needing token68 fidelity can either pass a `statusMap` that returns `null` for 401 (the original `HttpResponseMessage` flows through as `Result.Ok` and the caller reads the raw `WWW-Authenticate` headers directly) or use the body-aware `ToResultAsync(mapper, ct)` overload, which receives the `HttpResponseMessage` directly. (The round-5 wording incorrectly claimed only the body-aware overload could help — the `statusMap`-returning-null path was overlooked. Round 6 corrected this in all three places: source xmldoc, api reference, integration article.)

Tests: **+31** total on the branch vs `main` (round 1: +18; round 3 coverage: +4; round 4: +2 — null-error disposal consolidated across the three `Handle*Async` methods, 405 without `Allow` falls through, 416 without `Content-Range` falls through; round 5: +4 — 416 with `bytes */0` produces typed `RangeNotSatisfiable(0, "bytes")` for empty resource, 416 with `bytes 0-99/*` (no length) falls through, 405 with literal-empty `Allow:` value falls through, 429 with `Retry-After: 0` preserves zero delta; round 6: +3 — body-aware `ToResultAsync` null-mapper disposal, `ReadJsonOrNoneOn404Async` null-jsonTypeInfo disposal, 429 with `Retry-After` delta-seconds overflowing `int` treated as absent). The round-2 renderer guards and round-3 misleading empty-scheme test were withdrawn.

Package READMEs (round 7): both `Trellis.Http/README.md` and `Trellis.Http/NUGET_README.md` updated to reflect the new strict-default header preservation behavior, the exception propagation rules, and the round-4/round-6 await-then-dispose pattern for null-argument paths. Consumers reading the package docs on GitHub or NuGet now see the same contract surface as the api ref and integration article.

### Changed

#### Trellis.Primitives — inspection findings (M-1..M-5, m-3..m-7, i-6) + GPT-5.5 review (New-1..New-3)

Closes the formal inspection backlog from `files/primitives-inspection-report.md` after a meta-review by GPT-5.5 validated, refuted, or adjusted each finding and surfaced 3 additional ones I missed.

- **(Major) New-1 — `Money.GetDecimalPlaces` minor-unit table was incomplete.** The previous switch only handled JPY/KRW (0 decimals) and BHD/KWD/OMR/TND (3 decimals); the rest defaulted to 2 decimals. ISO 4217 actually assigns 0 minor units to BIF, CLP, DJF, GNF, ISK, KMF, PYG, RWF, UGX, UYI, VND, VUV, XAF, XOF, XPF (in addition to JPY/KRW), 3 minor units to IQD/JOD/LYD (in addition to BHD/KWD/OMR/TND), and 4 minor units to CLF/UYW. `Money.TryCreate` rounds at construction time, so previously-affected currencies silently lost precision (e.g., `Money.TryCreate(100.99m, "ISK")` rounded to `100.99` instead of `101`). Now produces correct rounding per ISO 4217 minor-unit assignments.
- **(Major) Allocate share-arithmetic overflow** — caught by GPT-5.5 pre-commit review. `Money.Allocate`'s inner `amountInMinorUnits * ratios[i] / totalRatio` is `long * int` arithmetic, which is **unchecked** by C# default and silently wraps for extreme inputs (e.g. `Money.Create(50_000_000_000m, "USD").Allocate(1_000_000_000, 1_000_000_000)` would have produced corrupted shares without throwing). Wrapped in `checked(...)` so overflow is caught by the existing `try` / `catch (OverflowException)` block and surfaced as `Result.Fail` like the rest of the arithmetic API.
- **(Major) New-2 — ISO code primitives accepted non-ASCII letters.** `CountryCode`, `CurrencyCode`, and `LanguageCode` validated with `char.IsLetter`, which accepts Unicode letters (German umlauts, Greek/Cyrillic alphabets, etc.) — but ISO 3166-1 alpha-2, ISO 4217, and ISO 639-1 alpha-2 are all ASCII-only. Switched to `char.IsAsciiLetter`. Inputs like `"Ää"` or `"αβ"` now correctly fail with the existing validation error message.
- **(Major) i-6 — `Money.Multiply` / `Divide` / `Allocate` now wrap `OverflowException` in `Result.Fail` for parity with `Add` / `Subtract`.** Previously `Money.Multiply(decimal)`, `Multiply(int)`, `Divide(decimal)`, `Divide(int)`, and the `Allocate` arithmetic could throw `OverflowException` for valid-but-extreme inputs (e.g. `Multiply(decimal.MaxValue / 2, 3m)`), violating the `Result<Money>`-returning contract. All five paths now catch and return `Error.UnprocessableContent` with a "would overflow" detail.
- **(Major) M-3 — `Money.Allocate(int[] ratios)` adds null-guard and overflow handling.** Throws `ArgumentNullException` when `ratios` is null. Wraps the entire ratio-arithmetic block (including `ratios.Sum()`, `Math.Round(... * multiplier)`, and the decimal-to-`long` conversion) in a `try` / `catch (OverflowException)` so the `Result<Money[]>`-returning contract is never violated by extreme but otherwise valid inputs.
- **(Minor) M-1 / M-2 — `Money` and `MonetaryAmount` arithmetic / comparison methods now `ArgumentNullException.ThrowIfNull(other)`.** `Money.Add`, `Subtract`, `IsGreaterThan`, `IsGreaterThanOrEqual`, `IsLessThan`, `IsLessThanOrEqual`, and `MonetaryAmount.Add`, `Subtract` previously NRE'd on a null `other`. Aligns with the framework's defensive-coding posture established by Trellis.Core 2.3-2 / Authorization #458 / Mediator #459 / EFCore #460.
- **(Minor) M-5 — `PhoneNumber.GetCountryCode()` throws `InvalidOperationException` on lookup-table miss.** Previously fell through to `digits[..1]` and silently returned an invalid 1-digit prefix (e.g. `"5"`, `"8"`) when the input passed E.164 *shape* validation but its prefix wasn't in `s_twoDigitCountryCodes` / `s_threeDigitCountryCodes` and didn't start with `1` or `7`. Callers got bad data with no signal. Now throws with a message naming the offending phone number and noting that this may indicate stale lookup tables. (`Result<string>` was considered but rejected as a public-surface break per GPT-5.5's adjustment.)
- **(Minor) m-3 — `PrimitiveValueObjectTraceProviderBuilderExtensions.AddPrimitiveValueObjectInstrumentation` now `ArgumentNullException.ThrowIfNull(builder)`.** Previously NRE'd on a null builder receiver.
- **(Minor) m-4 — `MonetaryAmount` and `Percentage` string-overload `TryCreate` methods no longer open a redundant outer activity span.** Each previously opened its own `Activity` and then delegated to `TryCreate(decimal, ...)` which opened a second one with the same name. Now the leaf decimal overload owns the trace and the string overloads delegate without restarting; telemetry consumers see one span per call.
- **(Minor) New-3 — `Money.Sum` / `MonetaryAmount.Sum` reject null elements with `ArgumentException` (paramName: `values`).** The receiver-null guard was already present (existing tests); element-null was previously NRE'd inside the loop.
- **(Info) m-2 — `CompositeValueObjectJsonConverter<T>.Read` now aggregates all missing required-property names into one error.** Previously threw on the first missing property; multi-field violations required multiple round trips. New format: `Required properties missing: 'amount', 'currency'.` (single-property case unchanged: `Required property 'amount' is missing.`).

Validated, refuted, or adjusted by GPT-5.5 meta-review of the inspection report. Refuted findings (kept current behavior intentional and documented): `Money.IsGreaterThan` etc. returning `false` for cross-currency comparisons (documented + tested); `Read` null-guarding `options`/`typeToConvert` (those parameters aren't dereferenced by the implementation); `Money.Zero("USD")` default (documented opinionated default); `MonetaryAmount` non-finite decimals (decimal has no NaN/Infinity); `Money` private EF parameterless ctor (private + EF-only convention); `Percentage.TryCreate(decimal?)` duplicate range check (refactor noise, not a bug); `PhoneNumber.GetCountryCode()` returning `string` rather than `CountryCode` (different semantic concepts).

Tests: **+30** new tests in `Trellis.Primitives.Tests` covering null guards on `Money` / `MonetaryAmount` arithmetic + comparison methods; `Money.Allocate` null/overflow paths; `Money.Multiply` / `Divide` overflow returning `Result.Fail`; `Money.TryCreate` correct rounding for all 14 added 0-decimal currencies, the 3 added 3-decimal currencies, and 2 4-decimal currencies; `CountryCode` / `CurrencyCode` / `LanguageCode` rejection of Unicode-letter inputs; `PhoneNumber.GetCountryCode` lookup-miss throw; `Money.Sum` / `MonetaryAmount.Sum` element-null rejection; `CompositeValueObjectJsonConverter` aggregate-missing-property error format; trace-builder null guard.

### Changed

#### Trellis.EntityFrameworkCore — GPT-5.5 review fixes (5 findings)

GPT-5.5 thorough review of the package surfaced 3 Major + 1 Minor + 1 Info finding; all addressed in this release.

- **(Major)** `TransactionalCommandBehavior` previously committed every successful command immediately, so a successful inner command (sent via mediator from an outer command's handler) would commit BOTH the outer's staged work and the inner's. If the outer command then failed, the data was already persisted. Fixed by adding `IUnitOfWork.BeginScope()` (a **required** interface member — see breaking change below) and tracking depth in `EfUnitOfWork<TContext>`: `CommitAsync` defers (returns success without persisting) at depth > 1, so only the outermost scope's commit actually runs. The behavior wraps every command in `using var scope = unitOfWork.BeginScope();`. **Caveat documented in xmldoc**: if an inner command returns a failure but the outer handler ignores it and returns success, the outer's commit will persist any changes the inner staged before failing — the unit-of-work is shared with the outer's `DbContext`, so per-scope rollback of staged changes is not supported. Handlers that need to discard inner failures' staged work must detach the affected entities themselves.

  **BREAKING:** `IUnitOfWork.BeginScope()` is required; custom `IUnitOfWork` implementations must implement depth-aware scope tracking. Migration: mirror the `EfUnitOfWork<TContext>` pattern (an `Interlocked.Increment`-counted depth field with a disposable releaser; `CommitAsync` returns `Result.Ok()` at depth > 1, persists otherwise). The `Trellis.Asp` `SAMPLES.md` `UnitOfWork` example has been updated to show the new shape.
- **(Major)** `DbExceptionClassifier.IsDuplicateKey` and `IsForeignKeyViolation` previously had no MySQL/MariaDB branch; classification fell through to message-fragment matching that didn't catch MySQL's "Duplicate entry" / "Cannot add or update a child row" phrasing, so MySQL consumers got raw `DbUpdateException` instead of `Error.Conflict`. Added MySQL detection by reflection (typename `MySqlException`, error number `1062` for duplicate key / `1451`/`1452` for foreign-key, plus message-form fallback for older drivers that don't surface the error number). Works with both `MySql.Data.MySqlClient.MySqlException` and `MySqlConnector.MySqlException`. SQLSTATE `23000` is intentionally **not** trusted on its own because MySQL reuses it for both duplicate-key and foreign-key violations.
- **(Major)** `MaybePartialPropertyGenerator` emitted invalid C# for valid nested user types: it used a stripped-down containing-type emission path that dropped `static` / `sealed` / `abstract` modifiers and conflated `record struct` with `record class`. It also grouped generated output by `Name` rather than `MetadataName`, so generic-arity overloads (`Foo<T>` and `Foo<T1,T2>`) in the same namespace would collapse into one generated partial declaration. Reused `OwnedEntityGenerator.BuildContainingTypeDeclaration` (which preserves all modifiers) and switched `BuildTypePath` to `MetadataName` (which encodes arity). Same `MetadataName`-based `BuildTypePath` change applied to `OwnedEntityGenerator` for consistency. Both generators' `TypeKindKeyword` now correctly emits `record struct` vs `record class`.
- **(Minor)** Public extension methods on `DbContext` / `DbContextOptionsBuilder` / `IQueryable` / `IServiceCollection` / `ModelConfigurationBuilder` now consistently `ArgumentNullException.ThrowIfNull(...)` their receiver and key arguments. Affected: `DbContextExtensions.SaveChangesResultAsync` ×4 overloads, `DbContextOptionsBuilderExtensions.AddTrellisInterceptors` ×4 overloads, `QueryableExtensions.FirstOrDefaultMaybeAsync` ×2 / `SingleOrDefaultMaybeAsync` ×2 / `FirstOrDefaultResultAsync` ×2, `UnitOfWorkServiceCollectionExtensions.AddTrellisUnitOfWork` ×2, `ModelConfigurationBuilderExtensions.ApplyTrellisConventions` (assemblies + null elements), and `EfUnitOfWork<TContext>` / `TransactionalCommandBehavior<TMessage,TResponse>` constructors. Aligns with the framework discipline established by Trellis.Core 2.3-2 / Authorization PR #458 / Mediator PR #459.
- **(Info)** API reference said the convention generator "follows public `DbSet<T>` roots", but the implementation enumerates all instance properties (any accessibility — `public`, `internal`, `private`, etc., as long as the entity type is accessible). Updated `docs/docfx_project/api_reference/trellis-api-efcore.md:117` to match the implementation.

Tests: **+9** new tests in `Trellis.EntityFrameworkCore.Tests` covering the deferred-commit semantics for nested commands (`Handle_nested_inner_success_does_not_commit_until_outermost_scope_exits`, `Handle_nested_outer_failure_after_inner_success_does_not_commit_anything`), MySQL classification (5 tests for duplicate key + foreign-key cases), and the `TransactionalCommandBehavior` constructor null guard.

### Changed

#### Trellis.Mediator — defensive-coding sweep + small cleanups (m-1..m-4, m-7 + i-1..i-3, i-6) + GPT-5.5 review fixes

Closes the entire Trellis.Mediator inspection backlog from `files/mediator-inspection-report.md` plus three additional findings surfaced by a GPT-5.5 review of the library.

- **m-1** — Every behavior, the default publisher, and the shared-loader adapter now throw `ArgumentNullException` with the offending parameter name when constructed with null dependencies. Affects `AuthorizationBehavior`, `ResourceAuthorizationBehavior`, `SharedResourceLoaderAdapter`, `ValidationBehavior`, `LoggingBehavior`, `ExceptionBehavior`, `MediatorDomainEventPublisher`, and `DomainEventDispatchBehavior`. Primary-constructor parameters were converted to regular constructors with explicit guards. Mirrors the Authorization PR #458 / Asp PR #457 i-8 patterns.
- **m-2** — `ServiceCollectionExtensions` public methods (`AddTrellisBehaviors` ×2, `AddResourceAuthorization` ×2, `AddResourceLoaders`, `AddSharedResourceLoader`) now consistently `ArgumentNullException.ThrowIfNull(services)`. The companion `DomainEventDispatchServiceCollectionExtensions` already had this discipline; the behavior-side helpers now match.
- **m-3** — `AuthorizationBehavior` and `ResourceAuthorizationBehavior` previously threw `InvalidOperationException("No authenticated actor available. Ensure an IActorProvider is configured...")` when `IActorProvider.GetCurrentActorAsync` returned null. The check is **kept as defense-in-depth for the documented `ga-11` security guarantee** (the resource loader must not run when the caller is unauthenticated, even under contract violation), but the error message is rewritten to accurately describe what happened: a contract violation by the `IActorProvider` implementation.
- **m-4** — `ResourceAuthorizationBehavior` previously called `loadResult.TryGetError(out var loadError); if (TryGetError) ...; if (!TryGetValue) throw new InvalidOperationException("Result is in an unexpected state.");` — the second branch is impossible because `TryGetError` and `TryGetValue` are mutually exclusive on `Result<T>`. Refactored to use the combined `Result<T>.TryGetValue(out value, out error)` overload (added precisely to support this shape). Removes the dead defensive throw.
- **m-7** — `GetLoadableTypes` (in both `ServiceCollectionExtensions` and `DomainEventDispatchServiceCollectionExtensions`) replaced `ex.Types.Where(t => t is not null).ToArray()!` with `ex.Types.OfType<Type>().ToArray()`. Removes the null-forgiving operator (`!`) that was laundering a `Type?[]` to `Type[]`; `OfType<T>` filters AND narrows the static type in one step.
- **i-1** — `ValidationBehavior` now uses the same `??= []; AddRange(...)` accumulator pattern in both the `IValidate` branch and the external-validator branch (previously the IValidate branch used `[.. upc.Fields.Items]` collection-expression seed). No behavior change; eliminates a maintenance hazard if the branches are reordered.
- **i-2** — `MediatorDomainEventPublisher.CreateInvoker` previously used `nameof(IDomainEventHandler<IDomainEvent>.HandleAsync)` — a quirky closed-generic instantiation just to extract the method name. Replaced with a `private const string HandleAsyncMethodName = nameof(IDomainEventHandler<DummyDomainEvent>.HandleAsync);` field that uses a sentinel record explicitly scoped for this purpose.
- **i-3** — `MediatorDomainEventPublisher.HandlerInvoker.InvokeAsync` previously had `result is ValueTask vt ? vt : ValueTask.CompletedTask;` — the fallback masks contract violations (`HandleAsync` is contractually `ValueTask`-returning). Replaced with a direct cast `(ValueTask)result!;` so a contract violation surfaces as an `InvalidCastException` rather than silently returning `CompletedTask`.
- **i-6** — `LoggingBehavior` and `TracingBehavior` xmldoc on the `options` constructor parameter rewritten to clarify that under `AddTrellisBehaviors()` the singleton is always registered, so the parameter is non-null in production; the optional-null fallback exists only for consumers that instantiate the behavior outside DI (custom test fixtures).

GPT-5.5 review fixes:

- **(Major)** `ResourceAuthorizationBehavior` previously resolved the `IResourceLoader<TMessage, TResource>` from DI **before** checking the actor null-state. Loader DI factories are arbitrary user code (a custom factory may open a `DbContext` or pre-fetch state during construction), so loader **resolution** itself counts as I/O for the documented `ga-11` guarantee ("no I/O when unauthenticated"). Reordered the behavior to check the actor first, then resolve the loader, then invoke the loader. The existing ga-11 test only proved `LoadAsync` wasn't called; a new regression test (`ResourceAuthorization_NullActor_DoesNotInvokeLoaderDIFactory`) registers the loader via a counting factory and asserts the factory is never invoked when the caller is unauthenticated.
- **(Major)** `AddResourceAuthorization(params Assembly[])` previously `continue`d silently when an `IAuthorizeResource<TResource>` command's `TResponse` didn't satisfy `IResult + IFailureFactory<TResponse>` — meaning a security-marked command could ship without resource authorization. (And because `IFailureFactory<TSelf>` is F-bounded, the original `MakeGenericType(tResponse).IsAssignableFrom(tResponse)` shape would actually have thrown `ArgumentException` rather than silently skipping — masking the real diagnostic.) The constraint check now fails fast with an `InvalidOperationException` naming the offending message type, response type, and required interfaces. Validation is extracted to an internal `ValidateResourceAuthorizationResponseType(messageType, resourceType, responseType)` so the assembly scanner's contract is unit-testable without round-tripping through a synthetic assembly.
- **(Minor)** `DomainEventDispatchBehavior.BuildExtractorOrNoop` previously assumed `TResponse` was itself a single-arg generic and used `responseType.GetGenericArguments()[0]` to find the aggregate type — silently no-oping for custom non-generic types implementing `IResult<TAggregate>` (e.g. an envelope class). Rewritten to walk `responseType.GetInterfaces()` looking for `IResult<TValue>` where `TValue : IAggregate`. Multiple aggregate-valued `IResult<>` interfaces with distinct type arguments are now an explicit error rather than a silent picks-one-and-drops-the-other. New regression test (`Dispatch_NonGenericResponseImplementingIResult_DispatchesEvents`) covers the custom-envelope case.

Tests: **+5** new regression tests in `Trellis.Mediator/tests/GptReviewRegressionTests.cs` (loader factory not invoked under null actor; `ValidateResourceAuthorizationResponseType` fail-fast for missing `IResult` and missing `IFailureFactory`; happy-path validation; non-generic envelope event dispatch).

Tests: **+15** new tests in `Trellis.Mediator/tests/ArgumentValidationTests.cs` covering every constructor null-guard (8 tests) and every `IServiceCollection` extension-method null-guard (7 tests).

### Added

#### Trellis.Authorization — `Actor` is now an entity (identity-based equality), no longer a record

`Actor` is converted from `sealed record` to `sealed class` with explicit identity-based equality. The `Id` property (e.g. JWT `sub` claim) is the principal identifier; `Permissions`, `ForbiddenPermissions`, and `Attributes` are point-in-time state about that principal (granted/revoked over time, ABAC attributes change every request). Two `Actor`s with the same `Id` are now equal regardless of their state — mirroring the framework's domain-layer `Trellis.Entity<TId>` pattern without inheriting the full `IAggregate` surface (Actor is an authorization-layer principal, not a domain aggregate root).

`Actor.Equals(Actor?)` / `Actor.Equals(object?)` / `Actor.GetHashCode()` / `==` / `!=` are all overridden to use `Id` only (ordinal comparison). Init-only properties remain unchanged so the type is still immutable after construction. The `with`-expression syntax (a `record`-only feature) is no longer available — use the constructor directly when copy-with-changes is needed. **Behavior change**: as a `record`, equality was synthesised structurally but the collection-typed properties (`Permissions`, `ForbiddenPermissions`, `Attributes`) compared by **reference** (their interface types have no structural comparer). Distinct `Actor` instances built from independent inputs were therefore unequal even when logically identical, because the constructor snapshots inputs into fresh `FrozenSet`/`FrozenDictionary` instances; the only way two distinct `Actor`s could compare equal was if a caller passed the exact same `FrozenSet`/`FrozenDictionary` references to both constructors. After this change they get identity equality based on `Id` regardless of state. No current consumer in the framework was equality-keying actors; the upgrade is otherwise transparent.

Inspection finding **Trellis.Authorization m-1**.

#### Trellis.Core — `ResourceRef.FormatTypeName(Type)` public helper

`ResourceRef.FormatTypeName(Type)` is a new public static helper that returns the simple CLR name of a type with backtick arity-mangling stripped (``List`1`` → `"List"`, ``Dictionary`2`` → `"Dictionary"`). It is used internally by `ResourceRef.For<TResource>()` and exposed publicly so other Trellis components — and consumer code — can sanitize type-derived identifiers without duplicating the algorithm. Non-generic types pass through unchanged.

`ResourceRef.For<TResource>(id)` additionally peels `Maybe<T>` wrappers (recursively) before formatting, so `For<Maybe<Order>>()` produces `"Order"` instead of the previously-mangled ``"Maybe`1"``. This mirrors the documented use case where a result type happens to wrap its domain in `Maybe<>` (e.g. `Result<Maybe<Order>>.ToHttpResponse(...)` for the precondition-fail branch). Non-`Maybe` generics collapse to the outer simple name (e.g. `List<Order>` → `"List"`); when the inner type argument is the meaningful resource identifier, callers should continue to use `ResourceRef.For(string, object?)` with an explicit name. The xmldoc carries the full contract.

#### Trellis.Asp — `WWW-Authenticate` emission for `Error.Unauthorized`

`ResponseFailureWriter` now emits a `WWW-Authenticate` header for every `AuthChallenge` carried on `Error.Unauthorized.Challenges`, completing the round-trip that `AuthChallenge` already documented. Format follows RFC 9110 §11.6.1: scheme alone for parameterless challenges (e.g. `Bearer`), or `<scheme> key1="value1", key2="value2"` for parameterized ones; values are always emitted as quoted-strings with `"` and `\` backslash-escaped per §5.6.4. Multiple challenges produce one `WWW-Authenticate` header per challenge (matching ASP.NET Core authentication handler convention). Emission is gated on the resolved status code being `401` — if `WithErrorMapping` promotes `Error.Unauthorized` to a non-401 status, the header is suppressed, mirroring the m-13 status-aware design used by ValidationProblem detail scrubbing. When `Challenges` is empty (the default `Error.Unauthorized()`), no header is written — the configured authentication handler retains full ownership of that flow.

#### Trellis.Asp — public `ValidationErrorsContext` validation-recording surface

`ValidationErrorsContext.AddError(string fieldName, string errorMessage)`, `ValidationErrorsContext.AddError(Error.UnprocessableContent unprocessableContent)`, and `ValidationErrorsContext.CurrentPropertyName` (get/set) are now `public` (previously `internal`). Promoting these formalizes the contract that AOT-generated `JsonConverter<TValue>`s in consumer assemblies depend on. The reflection-mode `ScalarValueJsonConverterBase<,,>` continues to use the same APIs unchanged. No behavioral change for any existing caller.

### Changed

#### Trellis.Authorization — argument-null guards on the public surface

`Actor` constructor, `Actor.Create`, every `Actor` lookup method (`HasPermission` / `HasPermission(string,string)` / `HasAllPermissions` / `HasAnyPermission` / `IsOwner` / `HasAttribute` / `GetAttribute`), and `ResourceLoaderById<TMessage,TResource,TId>.LoadAsync` now throw `ArgumentNullException` with the offending parameter name when called with a null argument. Previously these calls deferred null-checks to internal helpers (`SnapshotSet` / `FrozenSet.Contains` / `Enumerable.All` over a null `IEnumerable`) which surfaced as confusing `NullReferenceException`s with no parameter name. Aligns with the framework's defensive-coding posture established by Trellis.Core 2.3-2 / 2.3-7. Inspection findings **Trellis.Authorization m-2 / m-3 / i-3**.

#### Trellis.Authorization — xmldoc and API reference clarifications

Inspection findings **m-4 / m-5 / m-6 / i-4 / i-5 / i-6**:

- `Actor` constructor xmldoc and API reference table now enumerate every `ArgumentNullException`-throwing parameter (previously only `id` was documented).
- `Actor.Permissions` xmldoc nudges callers toward the `PermissionScopeSeparator` convention so scoped permissions round-trip through `HasPermission(string, string)` correctly.
- `IAuthorize.RequiredPermissions` xmldoc and API reference clarify that duplicates and order are ignored under AND-semantics.
- `IActorProvider.GetCurrentActorAsync` xmldoc and API reference now name `InvalidOperationException` as the canonical throw on unauthenticated, with subclass-specific guidance for concrete implementations.
- `SharedResourceLoaderById<TResource, TId>` xmldoc and API reference document that `Trellis.Mediator.AddResourceAuthorization(...)` registers it as **scoped** (safe to depend on a `DbContext`).
- The API reference's `HasPermission(string, string)` description previously rendered the composed key in TypeScript template-literal syntax (`${permission}:${scope}`); rewritten as plain prose to avoid misleading LLM-targeted doc consumers.

#### Trellis.Core / Trellis.Asp / Trellis.EntityFrameworkCore / Trellis.Testing — sweep CLR-mangled type names out of resource refs and wire-facing error messages

Across the framework, several wire-facing error messages and `ResourceRef` constructions used `typeof(T).Name` directly. For closed-generic Ts this leaks the CLR-mangled form (``List`1``, ``Maybe`1``) onto the wire — an inspection finding (ASP m-4 / m-7 / m-10, Core 2.4-4) flagged this as both ugly and a "one programming model" violation between modes (the AOT generator already emits friendly names at generation time; only the runtime/reflection paths mangled).

This release routes every such site through one of two new Trellis.Core helpers:

- `ResourceRef.For<TResource>(id)` — peels `Maybe<T>` wrappers (recursively, so `Result<Maybe<Order>>`-backed precondition fails report `"Order"` not ``"Maybe`1"``), then strips backtick mangling.
- `ResourceRef.FormatTypeName(Type)` — strips backtick mangling only (no `Maybe<>` peeling, since that is intentionally scoped to the resource-naming contract on `For<T>`).

Sites updated:

- `Trellis.Asp/src/Response/TrellisHttpResult.cs:105` — `PreconditionFailed` resource ref in the conditional-evaluator path.
- `Trellis.Asp/src/IfNoneMatchExtensions.cs:22` — `EnforceIfNoneMatchPrecondition<T>` resource ref.
- `Trellis.Asp/src/Validation/ScalarValueJsonConverterBase.cs:56,70,104,115,124,129,173` — six fallback message templates plus `GetDefaultFieldName()` (the camel-cased-type-name fallback used when no `CurrentPropertyName` is set).
- `Trellis.Asp/src/Validation/ValidatingJsonConverter.cs:41` — `OnNullToken` "TValue cannot be null." message.
- `Trellis.Asp/src/Validation/PrimitiveJsonReader.cs:31,37` — `FormatException`/`InvalidOperationException` catch and unsupported-primitive fallbacks (the reflection-mode counterparts of the AOT generator's `__TryReadPrimitive` helper, restoring wire-shape parity between modes).
- `Trellis.Core/src/DomainDrivenDesign/AggregateETagExtensions.cs:66,75,80` — three `Error.PreconditionFailed` resource refs.
- `Trellis.EntityFrameworkCore/src/RepositoryBase.cs:223` — `RemoveByIdAsync` not-found resource ref + Detail.
- `Trellis.Testing/src/FakeRepository.cs:82,189,210` — `GetByIdAsync` not-found, `SaveAsync` conflict, `DeleteAsync` not-found resource refs + Details. Also the `Add()` unique-constraint exception message at line 136.

For non-generic CLR types (the typical case) all sweeps are no-ops; no existing test asserted on the mangled form. The fix is materially observable only when a wrapping generic appears in the type position — most commonly `Maybe<T>` for the m-4 path.

#### Trellis.Asp — `ScalarValueModelBinderBase` removes dead `InvalidOperationException` (i-8)

`ScalarValueModelBinderBase.BindModelAsync` previously called `parseResult.TryGetError(...)` followed by an unconditional `parseResult.TryGetValue(out var value)`, with a defensive `throw new InvalidOperationException("Result is in an unexpected state.")` for the impossible `(success, !TryGetValue)` branch. Replaced both calls with the combined `Result<T>.TryGetValue(out value, out error)` overload, which is mutually exclusive on the two outputs and removes the dead branch entirely. No behavior change for any path callers can actually reach.

#### Trellis.Core — null-check consistency, default-uninit defensiveness, tracing perf docs

Self-review of `Trellis.Core` surfaced six findings; all addressed in this release.

- **`Result.Try<T>(Func<T>)` and `Result.TryAsync<T>(Func<Task<T>>)`** now throw `ArgumentNullException` when `func` is null, matching the no-payload `Try(Action)` / `TryAsync(Func<Task>)` overloads. Previously the value-bearing variants caught the resulting `NullReferenceException` and returned `Result.Fail(InternalServerError)`, hiding the programming error. **Behavior change**: callers that relied on the swallowing behavior (test or otherwise) need to handle null up-front. The existing `Try_WithNullFunction_ShouldReturnFailureResult` test was updated to assert `ArgumentNullException`.
- **`Maybe<T>.Map<TResult>(selector)` and `Maybe<T>.Match<TResult>(some, none)`** now throw `ArgumentNullException` when their delegate parameters are null. Previously the failure mode was path-dependent (NRE only when the matching branch fired, particularly bad for `Match` because either delegate could fail depending on `HasValue`). Sibling methods (`Bind`, `Where`, `Tap`, `Or(Func<>)`, etc.) already null-checked.
- **`NullableExtensions.ToResult<T>(Func<Error>)`** struct and class overloads now throw `ArgumentNullException` when `errorFactory` is null. Async variants inherit the fix transitively.
- **`Page<T>.Items`** now returns `Array.Empty<T>()` when accessed on a default-constructed `Page<T>` (previously returned null despite the non-nullable annotation). Mirrors the `EquatableArray<T>.Items` pattern. `DeliveredCount` simplified to `Items.Count` since the property is now always non-null.
- **`Cursor.Token`** now throws `InvalidOperationException` with a diagnostic message when accessed on `default(Cursor)` (previously returned null despite the non-nullable annotation and the doc'd "no empty cursor" invariant). The xmldoc invariant — "There is no empty cursor — a constructed Cursor always carries a non-empty token" — is now enforced at the property accessor.
- **`RequiredDecimal<TSelf>` source generation** now uses invariant culture for the plain `TryCreate(string?, string?)` overload even when `[Range]` is applied. Previously the ranged generated path used the ambient current culture while the unranged path used invariant culture, so the same string could parse differently depending on whether the type had a range constraint.
- **Nested required value-object source generation** now preserves containing-type modifiers such as `static` and `sealed` when emitting nested partial declarations. Previously nested `RequiredString<TSelf>` / `RequiredGuid<TSelf>` / numeric required value objects inside those containers could produce generated partial types that did not match the user's containing type declaration.
- **Global-namespace required value objects** now generate valid source. Previously a `partial class GlobalCode : RequiredString<GlobalCode>` declared outside a namespace caused the generator to emit an invalid namespace declaration, leaving the generated `IScalarValue` interface implementation unavailable.
- **`EntityTagValue.TryParse("*")`** now returns `EntityTagValue.Wildcard()`, so wildcard precondition tokens round-trip through `ToHeaderValue()` and the public parser. Previously only quoted strong and weak ETags parsed successfully.

#### Trellis.Core — tracing performance documentation

Documented the actual performance characteristics of `AddResultsInstrumentation` and the per-extension `using var activity = ActivitySource.StartActivity(...)` pattern, backed by a new BenchmarkDotNet suite (`Trellis.Benchmark/TracingOverheadBenchmarks.cs`). Measured on .NET 10 / x64:

- **No listener registered** (production default): ~14–20 ns per `Bind`/`Map`/`Tap`, **0 bytes allocated**. The framework does not pay for tracing the consumer didn't ask for.
- **`AddResultsInstrumentation` registered with full sampling**: ~200 ns + ~400 B per combinator. At 10k RPS × 10-step pipeline that's ~22 ms/sec CPU + 40 MB/sec GC pressure.

The new docs make the granularity guidance explicit: per-Result-extension spans add limited signal beyond the outer pipeline-behavior or HTTP-request span; for high-throughput services, instrument at the pipeline-behavior altitude (`Trellis.Mediator.TracingBehavior`) and reserve `AddResultsInstrumentation` for development/debugging or low-rate paths. Updated `ResultsTraceProviderBuilderExtensions.cs` xmldoc and the corresponding section in `trellis-api-core.md`.

#### Trellis.Asp — `ValidationProblem` error key shape (breaking)

Every `Trellis.Asp` `ValidationProblem` emitter now produces field keys in the same MVC dot+bracket convention used by ASP.NET Core's built-in `ValidationProblemDetails`, instead of leaking JSON Pointer or JSONPath syntax onto the wire. The on-the-wire `errors` map keys are now consistent regardless of which layer produced the 400 (model binding, scalar-value endpoint filter, FluentValidation adapter, business-rule violations, deserialization failure middleware).

- **Before:** mixed shapes per emitter — JSON Pointer (`/items/0/name`), JSONPath (`$.items[0].amount`, `$['property with space']`), or `"$"` for the root.
- **After:** uniform MVC convention — `items[0].name`, `items[0].amount`, `property with space`, and `""` for the root.
- A new internal translator (`Trellis.Asp.JsonPointerToMvc.Translate`) is wired into every emitter; the `ScalarValueValidationMiddleware` deserialization path additionally translates `System.Text.Json`'s `JsonException.Path` (including bracket-quoted JSONPath segments such as `$['a.b']`, `$['a/b']`, `$.items[0]['weird name']`) to the same shape.
- **Edge-case caveat:** STJ's path serialization is genuinely lossy for dictionary keys containing the literal sequence `'][` (e.g. `a'][`, `a'][b`, `a'.b']['foo`). For those adversarial inputs the middleware translator picks the "multiple segments" interpretation, so the resulting MVC key for these keys may not match `JsonPointerToMvc.Translate` for the equivalent JSON Pointer. Property names with `'][` are not common in real APIs and the trade-off preserves correct handling of the legitimate adjacent-non-identifier-property-names case (e.g. `$['weird name']['another weird']`). Consumers needing lossless field paths should rely on `RuleViolation` payloads carrying raw JSON Pointers in `extensions["rules"][n].fields[]`.
- **Escape hatch:** for `ValidationProblem` payloads carrying `RuleViolation`s, `extensions["rules"][n].fields[]` preserves the raw JSON Pointer values (`/items/0/name`) so consumers needing path fidelity for those payloads still have it. This escape hatch is `RuleViolation`-scoped only; flat field-violation payloads (`Error.UnprocessableContent` from FluentValidation, model binding, deserialization, etc.) are MVC-shape on the wire.

**Migration:** consumers keying off the slash form (`/items/0/name`) or the JSONPath form (`$.items[0].name`, `$['name']`) for `errors` map lookups must migrate to the MVC dot+bracket form (`items[0].name`, `name`). Code generators and form libraries that already target ASP.NET Core's `ValidationProblemDetails` shape (OpenAPI, react-hook-form, Formik) require no change. Producers that emitted `RuleViolation`s and want to keep raw JSON Pointers in their integration tests should assert against `extensions.rules[n].fields[]` rather than `errors`.

#### Trellis.Asp — AOT-generated JSON converters integrate with `ValidationErrorsContext`

The source-generated `JsonConverter<TValue>` emitted by `Trellis.AspSourceGenerator` for each scalar value object now mirrors the reflection-mode `ScalarValueJsonConverterBase<,,>.Read` bit-for-bit. Previously the generated `Read` called `TValue.TryCreate(primitiveValue, null)` and silently coerced any failure to `null`, so under AOT a deserialization that should have produced a 422 ProblemDetails just dropped the value — divergent from the reflection-mode behavior and breaking the framework's "one programming model" promise across the two modes.

After this fix, the generated `Read`:

- Resolves the field name from `ValidationErrorsContext.CurrentPropertyName`, falling back to a baked-in camel-cased type name when the AOT path has no `PropertyNameAwareConverter<T>` setting it. The fallback name is computed at generation time using a port of `JsonNamingPolicy.CamelCase.ConvertName`, so acronym-leading types (`SKU` → `"sku"`, `URLValue` → `"urlValue"`, `IPAddress` → `"ipAddress"`) match reflection mode bit-for-bit instead of the naive `"sKU"`/`"uRLValue"`/`"iPAddress"`. The result is emitted as a string literal, so there is no runtime cost.
- Sets `HandleNull = true` so JSON `null` tokens reach `Read` and get recorded as `"{TypeName} cannot be null."` instead of bypassing the converter.
- Wraps the typed `Utf8JsonReader` getter (`reader.GetGuid()`, `reader.GetInt32()`, etc.) in a `try`/`catch` for `FormatException`/`InvalidOperationException` matching `PrimitiveJsonReader.TryRead`; an invalid token like `"not-a-guid"` for a `Guid`-backed value object is now recorded as `'{fieldName}' is not a valid Guid.` via `ValidationErrorsContext.AddError` instead of escaping as a `JsonException` from the deserializer.
- Calls `TryCreate(primitiveValue, fieldName)` so the failure carries the correct field reference.
- Forwards `Error.UnprocessableContent` failures verbatim via `ValidationErrorsContext.AddError(unprocessableContent)` (preserving `ReasonCode` / `Args` / `Detail`); records other failures with the failure's `Detail` (or `"{TypeName} is invalid."` when `Detail` is blank) keyed under `fieldName`.
- Returns `null` after recording, matching reflection-mode `OnValidationFailure`.

Direct typed `Utf8JsonReader`/`Utf8JsonWriter` calls (`reader.GetGuid()`, `writer.WriteNumberValue(i)`, etc.) are preserved — no boxing or `JsonSerializer.Deserialize` reflection is introduced.

**Migration:** AOT consumers that previously caught the `null` and built their own ProblemDetails should remove that workaround and let `ScalarValueValidationMiddleware` produce the 422 from `ValidationErrorsContext`. Reflection-mode consumers see no change.

### Added

#### Trellis.Mediator — Domain event dispatch

- **`IDomainEventHandler<TEvent>`** (new) — Implement this to handle a domain event. Dispatch matches the event's runtime type **exactly**; base-type and interface-type handlers are not auto-resolved. Handlers must be idempotent — non-cancellation exceptions thrown by a handler are logged at error level and swallowed so other handlers, other events, and the originating command still complete. `OperationCanceledException` matching the request's token is the one exception that propagates.
- **`IDomainEventPublisher`** (new) — Used by the framework to fan out a single event. Inject only when publishing from non-pipeline contexts (background jobs, scheduled tasks). Default implementation (`MediatorDomainEventPublisher`, internal) resolves handlers via DI by runtime type.
- **`DomainEventDispatchBehavior<TMessage, TResponse>`** (new) — Pipeline behavior constrained to `ICommand<TResponse>` (queries pass through). After a successful command whose response is `IResult<TAggregate>` (typically `Result<TAggregate>`) where `TAggregate : IAggregate`, drains `aggregate.UncommittedEvents()` in waves with index tracking. `AcceptChanges()` is called **once at the end** of a fully successful loop; cancellation propagates above the `AcceptChanges()` call so undispatched (and dispatched) events stay on the aggregate, and handlers must be idempotent because a retry will re-publish events that already fired. Wave count is capped at 8; cap-exceeded paths are logged and `AcceptChanges()` is called defensively. Other response shapes (`Result<Unit>`, `Result<TDto>`, `Result<(A,B)>`) pass through untouched in v1; manual dispatch remains the option for those flows.
- **`DomainEventDispatchServiceCollectionExtensions.AddDomainEventDispatch()`** — Idempotent. Registers `DomainEventDispatchBehavior<,>` (open-generic, scoped) and the default `IDomainEventPublisher`. AOT-friendly (no scanning).
- **`AddDomainEventHandler<TEvent, THandler>()`** — Explicit per-handler registration for AOT/trim scenarios. Idempotent.
- **`AddDomainEventDispatch(params Assembly[] assemblies)`** — Assembly-scan overload (annotated `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`) that finds every concrete `IDomainEventHandler<TEvent>` and registers each as scoped.
- **Pipeline placement** — Inserts after `ValidationBehavior` and before `TransactionalCommandBehavior` (when registered), so events fire after the transaction commits and handlers see committed state. When no transactional behavior is in the pipeline (e.g., applications committing directly inside the handler via a repository), dispatch runs immediately after the handler returns success.

> **Failure model**: handlers run as **best-effort side effects**. Email failures, message-bus blips, and DI activation errors are all logged and swallowed; the originating command still succeeds. The one exception that propagates is `OperationCanceledException` matching the request's cancellation token — when the caller cancels, in-flight handlers that observe the token may throw OCE and the dispatcher lets it abort the remaining work. If a non-cancellation side effect must block command completion, do that work inside the command handler — not a domain-event handler.

> **Migration**: applications dispatching events manually (e.g., `foreach (var evt in agg.UncommittedEvents()) await _publisher.PublishAsync(...); agg.AcceptChanges();`) can delete that boilerplate after wiring `AddDomainEventDispatch(...)`. If you must run both during migration, the framework dispatcher is safe **only when the manual path calls `AcceptChanges()` before returning** — typical implementations do, but verify. If the manual code skips `AcceptChanges()`, accepts conditionally, or accepts only some events, the framework dispatcher will see the remaining events and re-publish them. Recommendation: migrate fully or stay manual; don't ship a hybrid.

#### Trellis.Mediator + Trellis.FluentValidation — Unified validation stage with composition

- **`IMessageValidator<TMessage>`** (new, in `Trellis.Mediator`) — Extensibility seam that lets validator packages plug into the single `ValidationBehavior` stage instead of occupying their own pipeline slot. Multiple validators per message are supported; their `Error.UnprocessableContent` failures aggregate into one response.
- **`ValidationBehavior` now runs for every message** (no longer constrained to `IValidate`). It composes `IValidate.Validate()` (when implemented) with every registered `IMessageValidator<TMessage>` and merges all field violations into a single `Error.UnprocessableContent`. Non-UPC failures (`Error.Conflict`, `Error.Forbidden`, …) short-circuit and propagate as-is.
- **`AddTrellisFluentValidation()`** (`Trellis.FluentValidation`) — Parameterless overload registers an open-generic `FluentValidationMessageValidatorAdapter<TMessage>` as `IMessageValidator<>`. AOT-friendly (no assembly scanning, no reflection on the hot path); register each `IValidator<TCommand>` explicitly via `AddScoped<IValidator<...>, ...>()`. Assembly-scanning overload is annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` for non-AOT scenarios.
- **JSON Pointer normalization** — `FluentValidationMessageValidatorAdapter` and `validationResult.ToResult(value)` now translate FluentValidation property names into RFC 6901 JSON Pointers: `Metadata.Reference` → `/Metadata/Reference`, `Lines[0].Memo` → `/Lines/0/Memo`. Special characters are escaped per RFC 6901 (`~` → `~0`, `/` → `~1`).
- **Showcase canonical demo** — `POST /api/transfers/batch/{fromId}` exercises `AddMediator(Scoped)` + `AddTrellisBehaviors()` + `AddTrellisFluentValidation()` end-to-end with nested + indexer FluentValidation rules and an `IValidate` business invariant. See [`Examples/Showcase/README.md`](Examples/Showcase/README.md).

> **Note:** `AddMediator(...)` should be called as `AddMediator(opts => opts.ServiceLifetime = ServiceLifetime.Scoped)` in any host with a request scope. Mediator's default Singleton lifetime conflicts with the scoped Trellis behaviors and fails ASP.NET's root-scope validation. See [Mediator integration docs](docs/docfx_project/articles/integration-mediator.md).

### Removed

#### Trellis.Asp — legacy response verbs removed (Phase 3 cleanup)

The seven extension classes deprecated by Phase 3 of the v2 redesign have been deleted. The single supported response API is now `result.ToHttpResponse(...)` / `result.ToHttpResponseAsync(...)` (returns `IResult`), with `.AsActionResult<T>()` / `.AsActionResultAsync<T>()` adapters for MVC.

Removed types:
- `ActionResultExtensions`, `ActionResultExtensionsAsync` (MVC `ToActionResult`, `ToCreatedAtActionResult`, metadata selector overloads)
- `HttpResultExtensions`, `HttpResultExtensionsAsync` (Minimal API `ToHttpResult`, `ToCreatedAtRouteHttpResult`, `ToCreatedHttpResult`, `ToUpdatedHttpResult`, range overloads)
- `PageActionResultExtensions` (`ToPagedActionResult`)
- `PageHttpResultExtensions` (`ToPagedHttpResult`)
- `WriteOutcomeExtensions` (`WriteOutcome<T>.ToActionResult`, `WriteOutcome<T>.ToHttpResult`, `ToUpdatedActionResult`)

Migration: replace every call with the single fluent builder overload of `ToHttpResponse` / `ToHttpResponseAsync`. See [`docs/docfx_project/articles/asp-tohttpresponse.md`](docs/docfx_project/articles/asp-tohttpresponse.md) and [`MIGRATION_v3.md`](MIGRATION_v3.md) for the full mapping.

### Breaking Changes

#### Trellis.Core — Error redesigned as closed ADT

The `Error` type is now an `abstract record` with **18 nested `sealed record` cases** (`Error.NotFound`, `Error.UnprocessableContent`, `Error.Conflict`, `Error.Forbidden`, …). The base type has a `private` constructor so the catalog is closed at the language level, and every `switch` over an `Error` reference is exhaustive at compile time.

Key changes:
- **No static factory methods.** Replace `Error.Validation("msg", "field")` with `new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("field"), "reason_code") { Detail = "msg" }))`. Same pattern for `Error.NotFound`, `Error.Conflict`, `Error.Forbidden`, `Error.Unexpected`, etc.
- **Typed payloads.** Each case carries a strongly typed payload — `ResourceRef` for `NotFound`/`Gone`/`Conflict`, `EquatableArray<FieldViolation>` for `UnprocessableContent`, `PreconditionKind` for `PreconditionFailed`, etc. No more `object?` bags.
- **`Detail` and `Cause` on the base.** Set them via object initializer; equality compares discriminator + payload + `Detail` (Cause excluded).
- **`Result.Error` is now `public Error?`** (null on success, never throws). `Result<T>.Value` was removed; extract success values with `TryGetValue`, `Match`, `Deconstruct`, or `GetValueOrDefault`. See [ADR-001](docs/docfx_project/adr/ADR-001-result-api-surface.md) for the full design rationale.
- **`Result<Unit>` collapsed to non-generic `Result`.** `Unit` is retained internally for tuple-result interop only.
- **Removed:** `MatchError`, `SwitchError`, `FlattenValidationErrors` extensions; `ValidationError`/`NotFoundError`/`ConflictError`/etc. concrete subclasses; `Error.Instance` field. The ASP wire layer synthesizes `ProblemDetails.Instance` from request URL + `ResourceRef`.
- **Renamed wire identifiers.** Default `Code` values changed from `"validation.error"`/`"not.found.error"`/etc. to the IANA-aligned slugs `"unprocessable-content"`/`"not-found"`/etc.
- **TRLS005 analyzer (`UseMatchErrorAnalyzer`) removed** — the C# compiler now provides exhaustiveness for free.

Migration path: every `Error.X(...)` factory call site must be rewritten. `MatchError(...)` becomes `result.Match(_, e => e switch { Error.X => ..., ... })`. See [Error Handling](docs/docfx_project/articles/error-handling.md) for the full patterns and [api-results.md](docs/docfx_project/api_reference/trellis-api-core.md) for the reference table.

#### Trellis.Testing — Package Restructure

- **Removed `ResultBuilder`** — Use `Result.Ok(value)` and `Result.Fail<T>(new Error.X(...))` directly. `ResultBuilder` was a thin wrapper that added no value over the existing API.
- **Removed `ValidationErrorBuilder`** — Construct an `Error.UnprocessableContent` directly with one `FieldViolation` per failure: `new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), reasonCode) { Detail = "..." }))`. Combine multiple validation results via `Combine`.
- **Removed `Trellis.Testing.Builders` namespace** — All builder types have been removed.
- **Removed `Trellis.Testing.Fakes` namespace** — `FakeRepository`, `FakeSharedResourceLoader`, `TestActorProvider`, and `TestActorScope` now live in the `Trellis.Testing` namespace. Replace `using Trellis.Testing.Fakes;` with `using Trellis.Testing;`.
- **New package: `Trellis.Testing.AspNetCore`** — ASP.NET Core integration test helpers (`WebApplicationFactoryExtensions`, `WebApplicationFactoryTimeExtensions`, `ServiceCollectionExtensions`, `ServiceCollectionDbProviderExtensions`, `MsalTestTokenProvider`, `MsalTestOptions`, `TestUserCredentials`) moved to this new package. Add `dotnet add package Trellis.Testing.AspNetCore` and add `using Trellis.Testing.AspNetCore;` for these types. Projects using both core assertions and ASP.NET helpers will need both packages.
- **`Trellis.Testing` no longer depends on ASP.NET Core, EF Core, or MSAL** — The core package now only depends on `Trellis.Core`, `Trellis.Authorization`, and `FluentAssertions`.

### Added

#### Trellis.Core + Trellis.Asp — Surfaceable JSON validation errors

- **`TrellisJsonValidationException`** (new, in `Trellis.Core`) — A marker subclass of `System.Text.Json.JsonException` that Trellis JSON converters throw when a structured value object's invariants are violated during deserialization (e.g., `MoneyJsonConverter` rejecting a negative amount). The message is treated as curated/client-safe.
- **`ScalarValueValidationMiddleware`** (Minimal API path) now surfaces the message of an inner `TrellisJsonValidationException` in the Problem Details body — using its `JsonException.Path` as the error key when populated. Plain `JsonException`s continue to map to the generic `"The request body contains invalid JSON."` message because their text can include internal type names (audit-respecting).
- **`MoneyJsonConverter`** updated to throw `TrellisJsonValidationException` (was: plain `JsonException`). Callers see `"Amount cannot be negative."` etc. from the framework instead of the generic "invalid JSON" placeholder. This restores DX parity with MVC's model binder, which already includes per-field `JsonException` messages.

#### Trellis.EntityFrameworkCore — Composite Value Object Convention

- **`CompositeValueObjectConvention`** — `ApplyTrellisConventions` now automatically registers all composite `ValueObject` types (types extending `ValueObject` but not implementing `IScalarValue`) as EF Core owned types. No `OwnsOne` configuration needed for types like `Address`, `DateRange`, or `GeoCoordinate`. `Maybe<T>` is also supported — for simple composites, columns are marked nullable in the owner table; for composites with nested owned types (e.g., `Address` containing `Money`), the convention maps the optional dependent to a separate table with NOT NULL columns. `Money` retains its specialized column naming via `MoneyConvention`. Explicit `OwnsOne` configuration takes precedence.

### Fixed

#### Trellis.Analyzers — Ternary Guard Recognition

- **TRLS003, TRLS004, TRLS006** — The unsafe-access analyzers now recognize ternary conditional expressions (`? :`) as valid guards. Previously, `maybe.HasValue ? maybe.Value : fallback` and similar patterns for `Result.Value`/`Result.Error` produced false-positive diagnostics.

### Added

#### Trellis.Testing — ReplaceResourceLoader

- **`ReplaceResourceLoader<TMessage, TResource>`** — New `IServiceCollection` extension method that removes all existing `IResourceLoader<TMessage, TResource>` registrations and re-registers the replacement as scoped (matching the production lifetime of resource loaders). Accepts a `Func<IServiceProvider, IResourceLoader>` factory. Eliminates the need to manually call `RemoveAll` before re-registering when `AddMockAntiCorruptionLayer()` causes duplicate DI registrations.

#### Trellis.Primitives — StringLength Attribute

- **`[StringLength]`** — `RequiredString<TSelf>` derivatives now support `[StringLength(max)]` and `[StringLength(max, MinimumLength = min)]` for declarative length validation at creation time. The source generator emits `.Ensure()` length checks in `TryCreate` with clear validation error messages (e.g., `"First Name must be 50 characters or fewer."`).

#### Trellis.EntityFrameworkCore — Money Convention

- **`MoneyConvention`** — `ApplyTrellisConventions` now automatically maps `Money` properties as owned types with `{PropertyName}` (decimal 18,3) + `{PropertyName}Currency` (nvarchar 3) columns. Scale 3 accommodates all ISO 4217 minor units (BHD, KWD, OMR, TND). No `OwnsOne` configuration needed. Explicit `OwnsOne` takes precedence.

#### Trellis.Primitives — Money EF Core Support

- **`Money`** — Added private parameterless constructor and private setters on `Amount`/`Currency` for EF Core materialization support. No public API changes.

#### Trellis.Authorization — NEW Package!

Lightweight authorization primitives with zero dependencies beyond `Trellis.Core`:

- **`Actor`** — Sealed record representing an authenticated user (`Id` + `Permissions`) with `HasPermission`, `HasAllPermissions`, `HasAnyPermission` helpers
- **`IActorProvider`** — Abstraction for resolving the current actor (implement in API layer)
- **`IAuthorize`** — Marker interface for static permission requirements (AND logic)
- **`IAuthorizeResource<TResource>`** — Resource-based authorization with a loaded resource via `Authorize(Actor, TResource)`
- **`IResourceLoader<TMessage, TResource>`** — Loads the resource required for resource-based authorization
- **`ResourceLoaderById<TMessage, TResource, TId>`** — Convenience base class for ID-based resource loading

Usable with or without CQRS — no Mediator dependency.

#### Trellis.Mediator — NEW Package!

Result-aware pipeline behaviors for [martinothamar/Mediator](https://github.com/martinothamar/Mediator) v3:

- **`ValidationBehavior`** — Short-circuits on `IValidate.Validate()` failure
- **`AuthorizationBehavior`** — Checks `IAuthorize.RequiredPermissions` via `IActorProvider`
- **`ResourceAuthorizationBehavior<TMessage, TResource, TResponse>`** — Loads resource via `IResourceLoader`, delegates to `IAuthorizeResource<TResource>.Authorize(Actor, TResource)`. Auto-discovered via `AddResourceAuthorization(Assembly)` or registered explicitly for AOT.
- **`LoggingBehavior`** — Structured logging with duration and Result outcome
- **`TracingBehavior`** — OpenTelemetry activity span with Result status
- **`ExceptionBehavior`** — Catches unhandled exceptions → `Error.Unexpected`
- **`ServiceCollectionExtensions`** — `PipelineBehaviors` array and `AddTrellisBehaviors()` DI registration

#### Trellis.Core — IFailureFactory

- **`IFailureFactory<TSelf>`** — Static abstract interface for AOT-friendly typed failure creation in generic pipeline behaviors
- **`Result<TValue>`** now implements `IFailureFactory<Result<TValue>>`

#### Specification Pattern — Composable Business Rules

`Specification<T>` is a new DDD building block for encapsulating business rules as composable, storage-agnostic expression trees:

- **`Specification<T>`** — Abstract base class with `ToExpression()`, `IsSatisfiedBy(T)`, and `And`/`Or`/`Not` composition
- **Expression-tree based** — Works with EF Core 8+ for server-side filtering via `IQueryable`
- **Implicit conversion** to `Expression<Func<T, bool>>` for seamless LINQ integration
- **In-memory evaluation** via `IsSatisfiedBy(T)` for domain logic and testing

```csharp
// Define a specification
public class HighValueOrderSpec(decimal threshold) : Specification<Order>
{
    public override Expression<Func<Order, bool>> ToExpression() =>
        order => order.TotalAmount > threshold;
}

// Compose specifications
var spec = new OverdueOrderSpec(now).And(new HighValueOrderSpec(500m));
var orders = await dbContext.Orders.Where(spec).ToListAsync();
```

#### Maybe<T> — First-Class Domain-Level Optionality

`Maybe<T>` now has a `notnull` constraint and new transformation methods, making it a proper domain-level optionality type:

- **`notnull` constraint** — `Maybe<T> where T : notnull` prevents wrapping nullable types
- **`Map<TResult>`** — Transform the inner value: `maybe.Map(url => url.Value)` returns `Maybe<string>`
- **`Match<TResult>`** — Pattern match: `maybe.Match(url => url.Value, () => "none")`
- **Implicit operator** — `Maybe<Url> m = url;` works naturally

#### ASP.NET Core Maybe<T> Integration

Full support for optional value object properties in DTOs:

- **`MaybeScalarValueJsonConverter<TValue,TPrimitive>`** — JSON deserialization: `null` → `Maybe.None`, valid → `Maybe.From(validated)`, invalid → validation error collected
- **`MaybeScalarValueJsonConverterFactory`** — Auto-discovers `Maybe<T>` properties on DTOs
- **`MaybeModelBinder<TValue,TPrimitive>`** — MVC model binding: absent/empty → `Maybe.None`, valid → `Maybe.From(result)`, invalid → ModelState error
- **`MaybeSuppressChildValidationMetadataProvider`** — Prevents MVC from requiring child properties on `Maybe<T>` (fixes MVC crash)
- **`ScalarValueTypeHelper`** additions — `IsMaybeScalarValue()`, `GetMaybeInnerType()`, `GetMaybePrimitiveType()`
- **SampleWeb apps** updated at the time — `Maybe<Url> Website` on User/RegisterUserDto, `Maybe<FirstName> AssignedTo` on UpdateOrderDto. (SampleWeb has since been removed; see _Showcase consolidated; SampleWeb removed_ below.)

### Changed

- `Maybe<T>` now requires `where T : notnull` — see [Migration Guide](MIGRATION_v3.md#maybe-notnull-constraint) for details

#### Examples — Showcase consolidated; SampleWeb removed

The Showcase sample now hosts the **same banking domain** twice — once as MVC controllers and once as Minimal API endpoint groups — so users can compare hosting styles over an identical contract. This replaces the previously incoherent setup where Showcase was banking and `SampleMinimalApi` was a different (users/products/orders) domain with no shared code.

**New project layout:**

```
Examples/Showcase/
├── api.http                                 Single .http file with @host toggle (works on both hosts)
├── src/
│   ├── Showcase.Domain/                     (unchanged) pure domain
│   ├── Showcase.Application/                NEW — workflows, services, persistence, DTOs, seed
│   ├── Showcase.Mvc/                        renamed from Showcase.Api — controllers + Program.cs
│   └── Showcase.MinimalApi/                 NEW — endpoint groups + Program.cs
└── tests/
    ├── Showcase.Tests/                      (unchanged) domain + MVC integration tests
    └── Showcase.MinimalApi.Tests/           NEW — mirror of MVC integration tests against Minimal API host
```

The Minimal API host adds **zero** new application code — same DTOs, repository, `BankingWorkflow`, and seed. The only delta is route mapping and `ToHttpResult*` vs `ToActionResult*` for Result→HTTP conversion. `Showcase.MinimalApi.Tests` runs the same six integration assertions as the MVC tests against the Minimal API factory and proves identical HTTP behaviour.

**Removed:** the entire `Examples/SampleWeb/` folder (`SampleMinimalApi`, `SampleMinimalApi.Tests`, `SampleUserLibrary`, four stale top-level `.http` files). `Trellis.Benchmark` no longer references the deleted `SampleUserLibrary`; the two VOs the benchmarks needed are now inlined in `Trellis.Benchmark/BenchmarkValueObjects.cs`.

#### Examples — Sample-perfection sweep (v2 Phase 1c PR2)

The `Examples/` folder was rewritten end-to-end so every kept sample passes the v2 axiom scorecard (A1–A11). Samples are the source of truth that flows into the ASP template and from there into AI-generated code; imperfections at this layer compound, so the sweep was scored against an explicit set of rules — see [Examples README](Examples/README.md) for the full list.

**Lineup changes:**
- **Removed** as redundant or noisy: `Examples/AuthorizationExample`, `Examples/BankingExample`, `Examples/EcommerceExample`, `Examples/SampleWeb/SampleWebApplication`, `Examples/SampleWeb/SampleMinimalApiNoAot`, `Examples/SampleWeb/SampleDataAccess`. Their teachings are now consolidated in `Showcase` (auth, banking workflows, lifecycle) and the Minimal API sample (data access via in-memory repos).
- **Renamed** `Examples/Xunit` → `Examples/TestingPatterns` (folder name now describes the *intent*, not the runner). The csproj is `TestingPatterns.Tests.csproj` so `IsTestProject` auto-detection still applies.

**Showcase (`Examples/Showcase`):**
- **Architectural fix** — every state-changing use case now crosses `BankingWorkflow`, which centralizes `mutate aggregate → publish events → AcceptChanges → persist`. Previously `AccountsController` mutated aggregates directly for `Open`/`Deposit`/`Withdraw`/`Freeze`/`Unfreeze`/`Close`, so domain events from those flows were never published or accepted (only `SecureWithdraw` and `Transfer` did the right thing). This was the canonical "boundary leak" bug.
- **Wire-boundary alignment** — `AccountResponse` exposes `AccountId`, `CustomerId`, `AccountType`, `Money`, `AccountStatus` directly instead of `Guid`/`string`/`decimal`. The existing `Money` JSON converter emits `{"amount", "currency"}`.
- **`System.TimeProvider`** replaces the ad-hoc `IClock`/`SystemClock` seam (BCL standard since .NET 8). Tests use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.
- **`.Value` purged from production code.** Seed-time invariants are centralized in a `Required<T>()` helper that throws `InvalidOperationException` with a clear message at startup.

**SampleUserLibrary, SampleMinimalApi (`Examples/SampleWeb/*`):**
- The standalone Minimal API sample and the shared `SampleUserLibrary` were folded into `Examples/Showcase/src/Showcase.MinimalApi`, which now hosts the same banking domain as `Showcase.Mvc` over identical DTOs. The shared-VO-library teaching is preserved by Showcase's `Showcase.Domain` / `Showcase.Application` split.
- `ScalarValueValidationMiddleware` no longer parses `BadHttpRequestException.Message` to extract field names or invalid values for Minimal API scalar route/query binding failures. It now uses endpoint parameter metadata plus route/query raw values and re-runs Trellis scalar validation for `IScalarValue<,>` / `Maybe<TScalar>` parameters.

**ConditionalRequestExample:**
- Route templates use `{id:ProductId}` (not `{id:guid}`). Handler signatures bind `ProductId id` directly (generator-emitted `IParsable`).
- `ProductResponse` exposes `ProductId`/`ProductName`/`MonetaryAmount` instead of `Guid`/`string`/`decimal`.
- New `ConditionalRequestExample.Tests` covers all six conditional-request branches (200/304/412/428/etc.).

**SsoExample, EfCoreExample:**
- Re-audited. New minimal `*.Tests` projects added.

---

#### Trellis.Analyzers - NEW Package! 🎉

A comprehensive suite of Roslyn analyzers to enforce Railway Oriented Programming best practices at compile time:

**Safety Rules (Warnings):**
- **TRLS001**: Detect unhandled Result return values
- **TRLS003**: Prevent unsafe `Result.Value` access without `IsSuccess` check
- **TRLS004**: Prevent unsafe `Result.Error` access without `IsFailure` check
- **TRLS006**: Prevent unsafe `Maybe.Value` access without `HasValue` check
- **TRLS007**: Suggest `Create()` instead of `TryCreate().Value` for clearer intent
- **TRLS008**: Detect `Result<Result<T>>` double wrapping
- **TRLS009**: Prevent blocking on `Task<Result<T>>` with `.Result` or `.Wait()`
- **TRLS011**: Detect `Maybe<Maybe<T>>` double wrapping
- **TRLS014**: Detect async lambda used with sync method (Map instead of MapAsync)
- **TRLS015**: Don't throw exceptions in Result chains (defeats ROP purpose)
- **TRLS016**: Empty error messages provide no debugging context
- **TRLS018**: Unsafe `.Value` access in LINQ without filtering first

**Best Practice Rules (Info):**
- **TRLS002**: Suggest `Bind` instead of `Map` when lambda returns Result
- **TRLS005**: *(removed in V2)* — superseded by C# exhaustive `switch` on the closed `Error` ADT
- **TRLS010**: Suggest specific error types instead of base `Error` class
- **TRLS013**: Suggest `GetValueOrDefault`/`Match` instead of ternary operator

**Benefits:**
- ✅ Catch common ROP mistakes at compile time
- ✅ Guide developers toward best practices
- ✅ Improve code quality and maintainability
- ✅ 149 comprehensive tests ensuring accuracy

**Installation:**
```bash
dotnet add package Trellis.Analyzers
```

**Documentation:** [Analyzer Documentation](Analyzers/src/README.md)

---

## Previous Releases


[Unreleased]: https://github.com/xavierjohn/Trellis/compare/v1.0.0...HEAD
