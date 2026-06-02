# Changelog

All notable changes to the Trellis project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed — Round-4 audit cleanup

- <strong>Breaking — `Trellis.Core` `RequiredEnumJsonConverter` rejects JSON `null`</strong> — the converter now overrides `HandleNull => true` and throws `JsonException` when reading a JSON `null` token into a `RequiredEnum<TSelf>`. Previously, deserializing `"null"` into a required enum value object silently returned `null` (or `default(TSelf)`), violating the "required" contract at the JSON boundary. Migration: callers that intentionally accept null must wrap the property in `Maybe<TSelf>` or use a regular nullable enum; otherwise the new exception is the correct rejection at the request boundary. The `Write(...)` path and round-trip of valid values are unchanged.
- **`Trellis.Core` default exception mapper no longer leaks `ex.Message`** — `Result.Try` / `Result.TryAsync` previously stored raw `ex.Message` in `Error.Unexpected.Detail`. While `Trellis.Asp.ResponseFailureWriter` masks 5xx details before ProblemDetails output, non-ASP consumers (gRPC, console hosts, background jobs, test harnesses) saw the raw exception text — which routinely contains connection strings, file paths, SQL fragments, and internal schema names. The default mapper now keeps the stable `("unhandled_exception", faultId)` shape (`FaultId` for correlation) and drops `ex.Message` from the public `Detail`. The caught exception is still available to consumers that pass a custom mapper to `Result.Try(..., mapper)`. Same class of fix as PR #563 round 3 (`ExceptionBehavior`); this closes the underlying `Result.cs` site.
- **`Trellis.Core` `RequiredString<TSelf>` `StringComparison` overloads** — added `StartsWith(string, StringComparison)`, `Contains(string, StringComparison)`, `Contains(char, StringComparison)`, and `EndsWith(string, StringComparison)` overloads to let in-memory callers request ordinal (or ordinal case-insensitive) semantics. The existing single-argument overloads are unchanged — they are intentionally kept for EF Core translation. The new overloads are **not** EF Core translatable and the XML docs say so explicitly; use the single-argument overload in `IQueryable` expressions.
- **`Trellis.Analyzers` TRLS054 and TRLS055 — `Maybe<T>` IQueryable shapes the rewriter cannot translate** — `MaybeExpressionRewriter` only rewrites the binary `==`/`!=` operators. Two reasonable-looking shapes silently fall through to opaque EF translation failures: `Maybe<T>.Equals(other)` / `object.Equals(maybe1, maybe2)` inside `Where`/`OrderBy`/etc. (now TRLS054), and `HasValueWhere(capturedDelegate)` where the delegate is a variable/method group instead of an inline lambda (now TRLS055). Both diagnostics fire for `System.Linq.Queryable` method-syntax lambdas AND for C# query-expression syntax rooted at an `IQueryable<T>` source; in-memory `IEnumerable<T>` calls remain silent. TRLS054 suggests `==`/`!=`; TRLS055 suggests inlining the lambda or materializing the query first.
- **`Trellis.Core.Generator` TRLS056 — generated `Required*<TSelf>` member collisions** — the generator now scans the user's partial-class declarations before emitting members it owns (`TryCreate`, `Create`, `Parse`, `TryParse`, the primitive→value-object explicit conversion operator, `NewUniqueV4()`, `NewUniqueV7()`, `NewUniqueV7(TimeProvider)`, and the generated constructor). If the user redeclared one of those, the generator reports `TRLS056` at the user's declaration with a message naming the offending member and the base class that already provides it, and skips emitting the conflicting member. Collision detection is signature-aware: generic overloads (e.g. user `Create<T>(...)` vs generated non-generic `Create(...)`) and unrelated explicit conversions (e.g. user `operator string(MyId)` vs generated `operator MyId(Guid)`) do NOT trigger false positives. Compilation is still blocked (TRLS056 is reported as an error), but the consumer now sees an attributed Trellis diagnostic instead of an opaque `CS0111` / `CS0102` duplicate-member error from a generated file.

### Fixed — Round-3 audit cleanup

- **`Trellis.Asp` `PrimitiveJsonReader` boxing eliminated** — `TryReadKnownPrimitive<TPrimitive>` no longer routes value-type primitives through an `object? boxed` temp before casting back to `TPrimitive`. The refactor uses typed branches with a `JitCast<TActual>` helper backed by `Unsafe.As`, removing the per-deserialize box on every primitive scalar value object (`Guid`, `int`, `long`, `decimal`, `DateTime`, `DateTimeOffset`, `bool`, etc.). Reference-type primitives (`string`) are unaffected. Public behavior, error shapes, and `Maybe<TPrimitive>` semantics are unchanged.
- **`Trellis.Mediator` `LoggingBehavior` log-level discipline** — expected client / domain failures (`Error.InvalidInput`, `InvariantViolation`, `NotFound`, `Gone`, `Conflict`, `AuthenticationRequired`, `Forbidden`, `RateLimited`, and `Aggregate` instances containing only expected inner errors) now log at `Information`. `Unexpected`, `Unavailable`, `TransportFault`, and unknown / future kinds still log at `Warning`. Operators no longer get warning-level noise from normal validation, authentication, and authorization rejections. The `[LoggerMessage]` source-gen shape is preserved.
- **`Trellis.Mediator` `ExceptionBehavior` reason code is now stable** — unhandled exceptions now return `new Error.Unexpected("unhandled_exception", faultId)` with the fault GUID in the dedicated `FaultId` correlation slot instead of `new Error.Unexpected(Guid.NewGuid().ToString("N"))`, which had been putting the per-incident GUID into the stable `ReasonCode` slot. Consumers can now filter / route on `ReasonCode == "unhandled_exception"`; correlation IDs remain available via `FaultId`. The mapper shape now matches `Result.cs`'s own default exception mapping.
- **`Trellis.EntityFrameworkCore` `AddTrellisUnitOfWork<TContext>()` fail-fast on conflicting closed-generic transactional registrations** — the method's own comments promised that the dedup check recognized both open-generic and closed-generic `TransactionalCommandBehavior<,>` pre-registrations. The actual implementation only matched the open generic; users who pre-registered a closed `IPipelineBehavior<MyCmd, MyRes> → TransactionalCommandBehavior<MyCmd, MyRes>` (with or without a later open-generic registration) ended up with two transactional behaviors firing on matching commands and double commits. The helper now throws `InvalidOperationException` with an actionable message naming both supported resolutions (remove the closed registration and let the helper install the open generic, or call `AddTrellisUnitOfWorkWithoutBehavior<TContext>()` to keep explicit closed registrations and skip open-generic installation). The open-generic-only idempotent path is unchanged.
- **`Trellis.ServiceDefaults` / `Trellis.Asp` nested-JSON-path actor-provider configure is now optional** — `UseNestedJsonPathClaimsActorProvider(...)` and `AddNestedJsonPathClaimsActorProvider(...)` now accept `Action<NestedJsonPathClaimsActorOptions>? configure = null`, matching the sibling claims / Entra / development actor-provider helpers. Default options (flat claims) are applied when `configure` is omitted.
- **`Trellis.Primitives` `RequiredGuid<TSelf>.NewUniqueV7(TimeProvider)`** — new overload accepts an explicit `TimeProvider` so tests can use `FakeTimeProvider` for deterministic v7 GUID timestamps. The previously flaky V7-ordering test in `RequiredGuidTests` (which relied on `Thread.Sleep(2)` and Windows millisecond-clock granularity) now uses the new overload and is deterministic. The generator emits the new overload for every `RequiredGuid<TSelf>` derivation; no migration is required.
- **`Trellis.Core` `Result<T>` raw-JSON example drift** — the `README.md`, `NUGET_README.md`, and `ResultRequiresExplicitHttpMappingConverter` XML doc all showed a stale `{"IsSuccess": true, "Value": ..., "Error": null}` example for raw `Result<T>` serialization. `Result<T>` has no public `Value` property; the example now reflects what raw serialization could actually produce (`{"IsSuccess": true, "IsFailure": false, "Error": null}` — public state only, no success value) and reinforces that consumers should use `Match` / `TryGetValue` / `Deconstruct` or the HTTP-mapping path via `Trellis.Asp`.

### Fixed — Round-2 audit cleanup

- **Input-size caps** — `Trellis.Asp` idempotency keys, `Trellis.Primitives` phone numbers, and `Trellis.Core` cursor tokens now reject oversized inputs before parsing, normalization, or decoding: idempotency header values above 4 KiB, phone-number input above 32 characters, and cursor tokens above 1024 characters. Rejections preserve the existing invalid-key / invalid-input / `cursor.malformed` failure shapes while hardening DoS paths. No migration is required.
- **Diagnostic logging for response-resource synthesis** — `Trellis.Asp` `ResponseFailureWriter` now emits a throttled warning once per exception type when service resolution or `ResourceCollectionNameRegistry.Resolve` throws while synthesizing a 404 / 409 `ProblemDetails.Instance`. The safe fallback to the request URL is unchanged, so response mapping still never turns those failures into 500s; previously silent misconfiguration is now actionable. The logger is optional and no-ops when none is registered.
- **Async-only actor-provider disposal** — `Trellis.Asp` `WorkerComposedActorProvider.Dispose()` no longer blocks on `DisposeAsync()` for inner providers that only implement `IAsyncDisposable`. Sync disposal now skips async-only inners and emits a throttled warning telling consumers to use `DisposeAsync()` / `await using`; the async disposal path remains the correct cleanup path.
- **EF interceptor cancellation** — `Trellis.EntityFrameworkCore` aggregate ETag and timestamp async interceptors now honor pre-canceled tokens before local mutation work, so canceled saves do not assign ETags or timestamps.
- **Behavior change — `Trellis.Testing` `FakeRepository` parity** — duplicate-key failures now use the canonical `"duplicate.key"` reason and not-found details now quote the ID and include a period, matching the EF repository runtime. Tests that asserted the old fake-only strings (`"duplicate.unique.constraint"` or `with ID {id} not found`) should update to the canonical shape they would assert against EF.
- **Log-injection hardening for enum JSON errors** — `Trellis.Core` `RequiredEnumJsonConverter` now sanitizes invalid enum names in `JsonException` messages by truncating long values and escaping control characters, preventing log injection and exception-message bloat when middleware logs parse failures verbatim.

### Fixed — Post-audit bundle: EF interceptor idempotency, scalar JSON generator nesting, `FireResult` guard, FluentValidation diagnostics

- **`Trellis.EntityFrameworkCore` `AddTrellisInterceptors(...)` idempotency** — repeated calls on the same `DbContextOptionsBuilder` now install Trellis' query / save interceptors exactly once, using an internal `IDbContextOptionsExtension` marker to detect prior registration. Library-level and application-level composition can both call the helper without double-firing Maybe-equality rewrites, scalar-value rewrites, or timestamp rewrites per query/save; consumer interceptors registered separately via `optionsBuilder.AddInterceptors(...)` are preserved. No migration is required.
- **`Trellis.Asp` scalar-value JSON converter generator** — nested `JsonSerializerContext` declarations now regenerate the containing-type chain instead of emitting a conflicting top-level partial class, so contexts such as `Outer.InnerContext` compile correctly. Source-generator hint names now include the full namespace and containing-type chain, preventing same-named contexts in different namespaces or owners from colliding. No migration is required.
- **Behavior change — `Trellis.StateMachine` `FireResult(...)`** — `FireResult` now checks `CanFire(trigger)` before calling Stateless `Fire`, returning a typed `Error.InvalidInput` for impermissible transitions without invoking the consumer's `OnUnhandledTrigger` callback. This keeps guarded result-based transitions from running side-effect callbacks and avoids confusing custom `InvalidOperationException` throws with Stateless' default unhandled-trigger exception; guard exceptions still surface. Consumers that intentionally rely on `OnUnhandledTrigger` side effects for rejected transitions should call `Fire` directly instead of `FireResult`.
- **`Trellis.FluentValidation` scanner diagnostics** — assembly scanning now reports `ReflectionTypeLoadException` details when validator discovery has to drop types because transitive dependencies are missing. If an `ILoggerFactory` is registered, the scanner emits one warning per affected assembly with the assembly name, dropped-type count, and a sample of loader-exception messages; otherwise it falls back to `Debug.WriteLine`. The diagnostics are non-breaking and make previously silent partial discovery failures actionable.

### Fixed — Post-audit cleanup: resource-auth idempotency, TRLS009 fix correctness, PhoneNumber.GetCountryCode totality, doc sweep

- **`Trellis.Mediator` resource authorization** — closed `ResourceAuthorizationBehavior<TMessage, TResource, TResponse>` registrations are now idempotent when the existing descriptor has the same `ServiceType` and `ImplementationType`. Repeated typed registration, repeated assembly scanning, and explicit-plus-scanned overlap no longer double-load resources, double-run authorization, or fire the same pipeline behavior twice per request. Distinct closed behaviors, including different response types, continue to coexist. No migration is required; this only removes duplicated registrations that were always a bug.
- **`Trellis.Analyzers` TRLS009 code fix** — the "use async method variant" fix now rewrites the invocation to the async overload, wraps it in `await`, and adds `async` to the enclosing `Task` / `ValueTask` method or local function when that conversion is locally safe. The analyzer still reports unsafe shapes, but the fixer no longer offers a broken fire-and-forget rewrite for synchronous `void` / value-returning methods or non-async lambdas; those cases require manual conversion to an async call chain.
- <strong>Breaking — `Trellis.Primitives` `PhoneNumber.GetCountryCode()`</strong> — now returns `Maybe<string>` instead of `string`. Numbers that satisfy E.164 shape validation but do not start with an assigned ITU-T country calling code now return `Maybe<string>.None` instead of throwing `InvalidOperationException`, restoring the value-object contract that a constructed value is safe for all queries. Migration: replace `var code = phone.GetCountryCode();` with `if (phone.GetCountryCode().TryGetValue(out var code)) { ... }` or use `phone.GetCountryCode().GetValueOrDefault(...)` when a fallback is appropriate.
- **XML docs, examples, and compile-checked snippets** — refreshed stale examples across Core, Authorization, Cookbook snippets, testing-pattern guidance, and the Core API reference so documented shapes compile against the current framework. The sweep updates obsolete `Result.Value` / implicit-failure examples to `TryGetValue` or `Match`, replaces removed `ICommand<Result>` examples with `ICommand<Result<Unit>>`, modernizes scalar-value examples to `RequiredString<TSelf>` / `RequiredGuid<TSelf>` and `IScalarValue<TSelf, TPrimitive>`, and points aggregate event persistence guidance at the tracked-aggregate domain-event dispatch behavior.

### Breaking — Required<T> defaults flipped to strict-by-default with per-type opt-outs

- **`Trellis.Core` / `Trellis.Primitives`** — every `Required*<T>` base now rejects its CLR sentinel by default. `RequiredString<T>` rejects `null`, `""`, and whitespace-only input, trims by default, then runs user constraints. `RequiredGuid<T>` rejects `Guid.Empty`; `RequiredDateTime<T>` and `RequiredDateTimeOffset<T>` reject their `MinValue`; `RequiredInt<T>`, `RequiredLong<T>`, and `RequiredDecimal<T>` reject zero; `RequiredBool<T>` still rejects `null`; `RequiredEnum<T>` rejects `null` and undeclared members.
- **New / now-active opt-out attributes** — use `[AllowEmpty]` for post-trim-empty `RequiredString<T>` values and `Guid.Empty`, `[AllowWhitespace]` for whitespace-only string input, `[NoTrim]` to preserve string input verbatim, `[AllowMinValue]` for date sentinels, and `[AllowZero]` for numeric sentinels. `[AllowWhitespace]` without `[NoTrim]` accepts whitespace-only input but stores `""` after trim.
- **Retired attributes** — `[NotDefault]` and `[Trim]` are vestigial no-ops under the v3 strict defaults; the generator ignores them and reports informational diagnostics. `[AllowDefault]` was deleted before release because the opt-out surface now uses per-type names.
- **New generator diagnostics** — TRLS046 (info: `[NotDefault]` is vestigial), TRLS047 (info: `[Trim]` is vestigial), TRLS048 (error: `[AllowZero]` on a non-numeric Required base), TRLS049 (error: `[AllowEmpty]` on numeric / date Required bases), TRLS050 (error: `[AllowMinValue]` on a non-date Required base), TRLS051 (error: `[AllowWhitespace]` on a non-string Required base), TRLS052 (error: `[NoTrim]` on a non-string Required base), and TRLS053 (error: contradictory combinations such as `[AllowZero]` + `[Positive]`).
- **Migration.** Remove `[NotDefault]` / `[Trim]`, add per-type opt-outs only where sentinel values are legitimate, and audit string fixtures against the validation-order truth table. Full recipe and worked examples: [MIGRATION_v3.md](MIGRATION_v3.md#requiredt-defaults-flip-strict-by-default-with-per-type-opt-outs).

### Changed — `OverloadResolutionPriority` retires CS0121 ambiguity on async Result extensions

- **`Trellis.Core`** — applied `[OverloadResolutionPriority(1)]` to the Task-delegate overloads of `BindAsync` / `MapAsync` / `TapAsync` / `TapOnFailureAsync` / `MapOnFailureAsync` / `CheckAsync` / `CheckIfAsync` / `EnsureAsync` / `MatchAsync` / `BindZipAsync` on sync `Result<T>` receivers (both non-tuple and T4-generated tuple variants). Inline `async` lambdas whose body returns a synchronous `Result<R>` no longer surface the historical `CS0121` ambiguity between the Task and ValueTask delegate overloads — the Task overload now wins by priority. Strongly-typed `Func<T, ValueTask<Result<R>>>` delegates continue to resolve to the ValueTask overload because the Task overload is not applicable (priority only ranks applicable candidates). Doc trap callout and the CS0121 row in `trellis-api-core.md` are retired; replacement note explains the new resolution rule and the one remaining LINQ `SelectMany` cross-class limitation that Roslyn does not currently disambiguate via priority.
- **`Trellis.Core`** — `[OverloadResolutionPriority(1)]` is also applied to the sync-receiver Task-delegate `SelectMany` overload (`Trellis.Core/src/Result/Extensions/Linq.Task.Right.cs`) for forward-compatibility, even though Roslyn does not currently honor priority across distinct extension classes (the Task `SelectMany` is in `ResultLinqExtensionsTaskRightAsync`, the ValueTask `SelectMany` is in `ResultLinqExtensionsValueTaskRightAsync`). LINQ query syntax over async Result composition still benefits from a typed local delegate when the async return type is ambiguous.
- **Test infrastructure** — new `OverloadResolutionPriorityTests` class exercises each affected method with the previously-ambiguous inline-async-lambda shape and asserts both the return type (Task vs ValueTask) and runtime semantics. A reflection-based safety net (`OverloadResolutionPriority_AppliedConsistently_AcrossSyncReceiverTaskDelegateOverloads`) walks every sync-receiver Task-delegate `XxxAsync` method that has a sibling ValueTask overload and fails if any is missing the attribute — so a future contributor who adds a new method (or removes the attribute from an existing one) gets a focused test failure with the offending method name instead of a downstream CS0121 from a random caller.

### Breaking — `AddTrellisAsp()` no longer auto-registers scalar-value validation

- **`Trellis.Asp`** — `AddTrellisAsp()` previously called `AddScalarValueValidation()` unconditionally, silently mutating global `MvcOptions` and `JsonOptions` (model binders, JSON converters, `SuppressModelStateInvalidFilter` flip) without user-facing disclosure. The mutation was invisible from the call site and surprised consumers who had already configured their own converters / naming policies. In v3, `AddTrellisAsp()` registers ONLY `TrellisAspOptions` (error-to-status-code mapping), `ResourceCollectionNameRegistry`, and the composition contract for layered `MapError<TError>` configuration. Scalar-value validation is now an explicit opt-in.
- **`Trellis.Asp`** — new convenience helpers `services.AddTrellisAspWithScalarValidation()` and `services.AddTrellisAspWithScalarValidation(Action<TrellisAspOptions>)` compose `AddTrellisAsp` and `AddScalarValueValidation` in one call. Greenfield controller hosts that bind value-object DTOs from JSON / route / query should use the convenience helper; the behavior matches the v2.x default exactly. Hosts that only need error mapping (no VO DTO binding) call `AddTrellisAsp()` alone and skip the binder / converter mutation.
- **`Trellis.ServiceDefaults`** — `TrellisServiceBuilder.UseAsp(...)` no longer implies scalar-value validation. New slot `TrellisServiceBuilder.UseScalarValueValidation()` (applied after `UseAsp`, before `UseProblemDetails`) opts a host into the scalar-value model binders, JSON converters, and `SuppressModelStateInvalidFilter` toggle for both MVC and Minimal API JSON pipelines. The slot is idempotent and independent of `UseAsp()` so MVC sites that don't bind VO DTOs can stay on `UseAsp()` alone. Minimal API hosts must still call `app.UseScalarValueValidation()` middleware and chain `.WithScalarValueValidation()` per endpoint.
- **Migration.** The two-shape mechanical migration is in [MIGRATION_v3.md](MIGRATION_v3.md#trellisasp-v3--addtrellisasp-no-longer-auto-registers-scalar-value-validation). For behavior-preserving migration: replace every `services.AddTrellisAsp(` with `services.AddTrellisAspWithScalarValidation(` and append `.UseScalarValueValidation()` after every `.UseAsp()` in `AddTrellis(o => ...)` composition roots.

### Documentation — owner-by-id authorization quick-start

- **`docs/docfx_project/api_reference/trellis-api-authorization.md`** — new "Owner check quick-start — copy this" section at the top of the document, before the Patterns Index. Names the canonical owner-on-loaded-resource pattern (`IAuthorizeResource<TResource>` + `IIdentifyResource<TResource, TId>` so the framework reuses `SharedResourceLoaderById<TResource, TId>`) and shows the complete copy-paste setup. The Patterns Index gains a top-of-table row pointing back at the quick-start.
- **`docs/docfx_project/api_reference/trellis-api-cookbook.md`** — Recipe 7 reordered so the canonical owner case appears first (with explanatory comments naming it as "the 90% case"), then the static-permission gate. Recipe 24's decision matrix gains an explicit "Owner-by-id check on the command's resource (the 90% case)" row at the top so consumers debugging an authorization path land on the simplest match first.

### Documentation — `FailAfterCommit` composition anti-pattern

- **`Trellis.Core`** — `Result.FailAfterCommit<T>(error)` XML remarks now explicitly call out that the helper is a *leaf* worker-handler operation and must not be threaded through `Combine` / `TraverseAll` / `SequenceAll` / `WhenAllAsync`. The `PersistOnFailure` flag OR-accumulates onto aggregated failures, which silently commits the staged permanent-failure mutation alongside any other legs' outcomes — almost never what the handler author intended. The new guidance directs authors to restructure such handlers so the aggregating step runs to its terminal outcome first and `FailAfterCommit` is invoked as a terminal step (or in a follow-up command).
- **`docs/docfx_project/api_reference/trellis-api-anti-patterns.md`** — new "Result.FailAfterCommit composed with aggregating operators" entry with a WRONG / FIX gallery example.
- **`docs/docfx_project/api_reference/trellis-api-core.md`** — IPersistOnFailure section gains an "Anti-pattern" subsection pointing at the new anti-pattern entry, plus the existing pipeline-composition table.

### Added — Silent-403 diagnostics and `NestedJsonPathClaimsActorProvider`

- **`Trellis.Asp`** — `ClaimsActorProvider` now emits two startup-diagnostics log entries (each throttled to fire at most once per application lifetime) that surface the silent-403 footgun caused by mis-configured nested-JSON identity-provider claims (Auth0 `app_metadata.roles`, Azure B2C `extension_*`, some Okta token shapes): **EventId 2** (Warning) fires when the configured `PermissionsClaim` resolves to zero entries on an authenticated identity that carries other claims; **EventId 3** (Error) fires when the configured claim resolves to a single value that parses as a JSON object or array. The diagnostics name the configured claim, list a sample of present claim types, and recommend `NestedJsonPathClaimsActorProvider`.
- **`Trellis.Asp`** — new `ClaimsActorOptions.ValidateClaimShapeOnFirstUse` toggle (default `true`) suppresses both diagnostics when set to `false`. Use only when the diagnostics duplicate an existing health-check or claim-validation pipeline.
- **`Trellis.Asp`** — new `NestedJsonPathClaimsActorProvider` and `NestedJsonPathClaimsActorOptions` for identity providers that ship roles/permissions under a nested JSON claim. Configure with a `ContainerClaim` naming the top-level claim that carries the JSON payload plus optional `ActorIdPath` / `PermissionsPath` dotted JSON paths. Terminal elements may be strings (single value), arrays of strings (multiple values), or objects whose property names are emitted as the values (Auth0 roles-as-object shape). Falls back to the inherited flat-claim resolution when the configured paths are empty, when the container claim is absent, or when its value fails to parse as JSON (with a one-off **EventId 4** warning). Register via the new `services.AddNestedJsonPathClaimsActorProvider(opts => ...)` extension. AOT-safe (uses `JsonDocument.Parse`).

### Added — `Trellis.ServiceDefaults` AOT compatibility

- **`Trellis.ServiceDefaults`** — re-enabled `IsAotCompatible=true`, `IsTrimmable=true`, `EnableAotAnalyzer=true`, `EnableTrimAnalyzer=true`. The package is now AOT- and trim-compatible.
- **`Trellis.ServiceDefaults`** — new AOT-safe per-type overloads on `TrellisServiceBuilder`: `UseFluentValidation()` (adapter only) + `UseFluentValidation<TValidator, TMessage>()` per validator; `UseResourceAuthorization()` (pipeline only) + `UseResourceAuthorization<TMessage, TResource, TResponse>()` per command; `UseDomainEvents()` (publisher + behavior only) + `UseDomainEvents<TEvent, THandler>()` per handler; `UseTrackedAggregateDomainEvents()` (publisher + behavior only) + `UseTrackedAggregateDomainEvents<TEvent, THandler>()` per handler. The four existing assembly-scanning overloads are now annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` so the AOT analyzer surfaces the choice between AOT-safe and scanning shapes at the consumer's call site.
- **`Trellis.ServiceDefaults`** — `UseEntityFrameworkUnitOfWork<TContext>()` is now annotated `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]` because the underlying `Trellis.EntityFrameworkCore` package is intentionally non-AOT (EF Core requires reflection over entity types). The AOT analyzer now surfaces this seam at the consumer's call site; AOT consumers should compose their data access layer outside the builder.
- **README** — the unconditional "AOT-friendly" claim is softened to note that the composition-root convenience builder exposes both AOT-safe per-type overloads and assembly-scanning overloads with appropriate analyzer annotations.

### Added — `RequiredDateTimeOffset<TSelf>` and `Required*<T>` convenience attributes

- **`Trellis.Core`** — new `RequiredDateTimeOffset<TSelf>` primitive base class mirroring `RequiredDateTime<TSelf>` for instants whose originating UTC offset is part of the domain contract. Lenient by default (rejects `null` only); opt into `DateTimeOffset.MinValue` rejection via `[NotDefault]`. Generator support: `RequiredPartialClassGenerator` now recognises `RequiredDateTimeOffset` as a valid base, emits the full `TryCreate(DateTimeOffset)` / `TryCreate(DateTimeOffset?, string?)` / `TryCreate(string?, string?)` / `IFormattableScalarValue` factory family, and round-trips the offset through `IParsable<T>` / `ParsableJsonConverter<T>` via ISO 8601 round-trip ("O") format.
- **`Trellis.Core`** — four new numeric convenience attributes for `RequiredInt<TSelf>` / `RequiredLong<TSelf>` / `RequiredDecimal<TSelf>`: `[Positive]` (rejects `<= 0`), `[NonNegative]` (rejects `< 0`), `[Negative]` (rejects `>= 0`), `[NonPositive]` (rejects `> 0`). On `RequiredInt` and `RequiredLong` the generator synthesises the equivalent `[Range]` bounds; on `RequiredDecimal` it emits a direct sign comparison (the full decimal range exceeds what double-backed `[Range]` can express). New generator diagnostics: **TRLS043** — convenience attribute on a non-numeric Required base; **TRLS044** — more than one convenience attribute on the same class (the sign constraints are mutually exclusive); **TRLS045** — convenience attribute combined with an explicit `[Range]` on the same class (the combination would silently disable the convenience sign check). The diagnostic IDs are also mirrored on `Trellis.Analyzers.TrellisDiagnosticIds` for the consumer-facing analyzer surface.
- **`Trellis.Core`** — string opt-out marker attributes (`[AllowEmpty]`, `[AllowWhitespace]`, `[NoTrim]`) were introduced ahead of the strict-default emission flip and are now active as described in the breaking-change entry above. The generic `[AllowDefault]` placeholder was removed before release in favor of per-type opt-out names.

### Fixed — `Maybe<T>` equality silent miss-query

- **`Trellis.EntityFrameworkCore`** — `c.Phone == Maybe.From(value)` previously translated to `_phone IS NULL` because EF Core funcletization extracted both `Maybe<T>.None` and `Maybe.From(value)` to `QueryParameterExpression` *before* `MaybeQueryInterceptor.QueryCompilationStarting` ran, erasing the syntactic difference. The fix is a new `MaybeEvaluatableExpressionFilterPlugin` registered via `MaybeEvaluatableExpressionFilterExtension` (an `IDbContextOptionsExtension`) that returns `false` for the three literal operand shapes — `Maybe<T>.None` static property access, `default(Maybe<T>)`, and `Maybe.From(value)` / `Maybe<T>.From(value)` calls — so they stay un-funcletized. `MaybeExpressionRewriter` now recognises each literal shape: `None` and `default(Maybe<T>)` translate to typed `null`; `Maybe.From(arg)` extracts `arg` and lifts it to the storage type, producing `_field = @p`. The plugin is wired automatically by every `AddTrellisInterceptors(...)` overload; consumers already calling `AddTrellisInterceptors(...)` get the fix with no registration change. Consumers registering the interceptor directly via `optionsBuilder.AddInterceptors(new MaybeQueryInterceptor())` must migrate to `AddTrellisInterceptors(...)` (or add the new options extension themselves) to pick up the equality fix.
- **`Trellis.EntityFrameworkCore`** — `MaybeExpressionRewriter` no longer silently converts unrecognised `Maybe<T>`-typed operands to typed null. A captured local of type `Maybe<T>` (for example `var m = Maybe.From(value); db.Where(c => c.Phone == m);`) now throws `InvalidOperationException` with an actionable message naming the supported alternatives (`Maybe.From(value)` inlined at the comparison site, or `MaybeQueryableExtensions.WhereEquals(...)`). This trades the historic silent miss-query for an explicit failure at translation time.

### Added — IETF `Idempotency-Key` middleware

- **`Trellis.Asp`** — new `Trellis.Asp.Idempotency` namespace shipping opt-in middleware that implements [`draft-ietf-httpapi-idempotency-key-header`](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/) for `POST` / `PATCH` retry safety. Endpoints opt in with `[Idempotent]` (or `.WithMetadata(new IdempotentAttribute())` in Minimal API); the middleware is a no-op on endpoints without it and on methods outside `IdempotencyOptions.Methods`. The middleware reads the configured header (default `Idempotency-Key`), parses it as the [RFC 8941](https://www.rfc-editor.org/rfc/rfc8941) `sf-string` subset, buffers the request body up to `MaxRequestBodyBytes` (default `1 MiB`), computes a SHA-256 fingerprint over `(method, path, normalized headers, body)`, resolves an isolation scope via `IIdempotencyScopeResolver` (default `DefaultIdempotencyScopeResolver` — per-actor when `IActorProvider` is registered, anonymous otherwise), and contracts with `IIdempotencyStore.TryReserveAsync(...)` for one of four outcomes: `Reserved(reservationId)` — the first request, the response is captured by a teeing `IHttpResponseBodyFeature` decorator and snapshotted on success; `AlreadyInFlight(retryAfter)` — `409 Conflict` with `Retry-After`; `Replay(snapshot)` — the captured status code, headers, and body are written verbatim; `BodyHashMismatch(storedFingerprint)` — `422 Unprocessable Entity`. Reservation tokens are opaque `string` GUIDs the store uses for CAS so a stale completer cannot finalise a reservation the sweeper already abandoned. Finalisation runs under a bounded 5-second cancellation token so `AbandonAsync` still executes when the client disconnects. New types: `IdempotentAttribute`, `IdempotencyOptions`, `IIdempotencyStore`, `IdempotencyReservationOutcome`, `IIdempotencyScopeResolver`, `DefaultIdempotencyScopeResolver`, `AnonymousIdempotencyScopeResolver`, `ActorIdempotencyScopeResolver`, `InMemoryIdempotencyStore`, `CapturingResponseBodyFeature`, `IdempotencyKeyParser`, `IdempotencyFingerprint`, `IdempotencyMiddleware`. New extensions: `services.AddTrellisIdempotency(configure?)`, `services.AddInMemoryIdempotencyStore()`, `app.UseTrellisIdempotency()`. The in-memory store is single-process only and intended for tests and dev hosts; production multi-instance hosts need an EF-backed store implementing the same CAS contract.
- **`Trellis.ServiceDefaults`** — new `TrellisServiceBuilder.UseIdempotency(Action<IdempotencyOptions>? configure = null)` slot, applied after `UseProblemDetails()` and before actor-provider registration so opted-in endpoints' scope resolver composes cleanly with `IActorProvider`. Independent of `UseAsp()`. Composition is explicit: the slot wires options + scope resolver + the marker `app.UseTrellisIdempotency()` checks at startup, but does not register a store — callers add `services.AddInMemoryIdempotencyStore()` (dev / tests) or an EF-backed store (production). Repeated calls compose the configure delegates rather than overwriting, mirroring `UseAsp` / `UseMediator`.

### Behavior — idempotency abandons non-replayable outcomes

- **`Trellis.Asp.Idempotency`** — the middleware now abandons the reservation (instead of caching a snapshot) when the handler returns a 5xx status code or when the response wrote trailers. 5xx responses are treated as transient per the IETF Idempotency-Key draft, so the next retry with the same key re-executes the handler instead of replaying a transient failure. Responses that wrote trailers (via `IHttpResponseTrailersFeature`) cannot be reproduced by the snapshot writer, so caching them would silently drop the trailers on replay; those reservations are abandoned and the next retry re-executes. New `ILogger` events at EventId 6 (trailers-abandoned) and EventId 7 (5xx-abandoned) make the abandon path visible to operators.

### Fixed — idempotency

- **`Trellis.Asp.Idempotency`** — `IdempotencyKeyParser` now rejects an unescaped `"` inside a quoted string with a position-aware diagnostic, matching RFC 8941's requirement that embedded quotes are escaped as `\"`. Previously, `"abc"junk"` was accepted as `abcjunk` because the parser appended the closing `"` to the string buffer instead of treating the second `"` as an end-of-string marker.
- **`Trellis.Asp.Idempotency`** — `IIdempotencyStore.TryReserveAsync` XML doc for the `fingerprint` parameter corrected from "hex digest" to "URL-safe base64 (no padding) digest" so store implementers do not size storage columns for the wrong encoding (`IdempotencyFingerprint.Compute` returns 43-character base64url, not 64-character hex).

### Added — idempotent inserts

- **`Trellis.EntityFrameworkCore`** — new `DbContextIdempotencyExtensions.TryInsertUniqueAsync<TEntity>(this DbContext, TEntity, CancellationToken)` helper that adds the entity, persists it, and converts a provider-level unique-constraint violation into `Result.Fail<TEntity>(new Error.Conflict(null, "duplicate.key") { Detail = "A record with the same unique value already exists.", ConstraintName, ConstraintTableName })`. The added entity is detached from the change tracker on the duplicate path so a caller retrying with a freshly-constructed entity does not re-flush the original. Foreign-key violations, `DbUpdateConcurrencyException`, connection-level exceptions, and `OperationCanceledException` propagate normally. The helper throws `InvalidOperationException` when the context already has pending changes on entry so a duplicate-key violation cannot be mis-attributed to the inserted entity.
- **`Trellis.EntityFrameworkCore`** — new `DbExceptionClassifier.ExtractConstraintIdentity(DbUpdateException)` returning `(string? ConstraintName, string? TableName)`. Typed extraction first for PostgreSQL (`Npgsql.PostgresException.ConstraintName` / `TableName` / `SchemaName` via reflection so the package stays provider-agnostic; output is `"schema.table"` when both are present); falls back to regex-based parsing for SQL Server errors 2627 / 2601 / FK 547, SQLite `UNIQUE constraint failed: <Table>.<Column>` / `PRIMARY KEY` (FK form returns null pair because SQLite does not name the constraint), and MySQL `Duplicate entry '...' for key '<table>.<key>'`. Defensive: returns `(null, null)` on any unexpected exception so telemetry extraction never breaks a caller.
- **`Trellis.Core`** — `Error.Conflict` gains two `[JsonIgnore]` init-only telemetry fields `ConstraintName` and `ConstraintTableName` (defaults `null`, preserves equality for existing call sites). The new fields are intended for structured logging and are never serialized to API responses; the safe-for-clients message stays in `Detail`.
- **`Trellis.EntityFrameworkCore`** — `SaveChangesResultAsync` and `SaveChangesWithRetryAsync` now populate `ConstraintName` / `ConstraintTableName` on the `Error.Conflict` they return for the `duplicate.key` and `referential.integrity` reason codes (no change to `retry.exhausted` / `retry.aborted` paths).

### Added — `Trellis.Testing.Worker`

- **`Trellis.Testing.Worker`** — new package shipping `WorkerHarness<TWorker>` for integration-testing `BackgroundService` workers. Builds an `IHost` with a deterministic `FakeTimeProvider`, a configurable `TestActorProvider`, and an open-generic capture handler wired into the mediator's `IDomainEventHandler<>` pipeline so tests can advance time, await the first domain event of a given type (with an optional predicate), or await a named tick signal via `IWorkerTickSignal`. The harness owns hosted-service registration for the worker under test and fails fast if `ConfigureServices` also calls `AddHostedService<TWorker>()`. Wait timeouts run on real time so `Time.Advance(...)` only drives the worker's `Task.Delay` / `PeriodicTimer` continuations. `AutoStart` defaults to `false` so tests can subscribe before the worker emits. `WaitForEventAsync` returns the matching `TEvent`; timeouts raise `WorkerHarnessTimeoutException` with a diagnostic that names the awaited condition, captured-event counts, and reminds the user to register `AddDomainEventDispatch()` if missing. Caller cancellation propagates as plain `OperationCanceledException` so tests can distinguish wait-expired from caller-cancelled.

### Added — retry classification

- **`Trellis.Core`** — transport-neutral retry classification over the closed `Error` catalog: `RetryClassification` enum (`Transient`, `Permanent`, `FailFast`) and the static `ErrorRetryExtensions` class with `error.Classify()`, `error.IsTransient()`, `error.IsPermanent()`, `error.IsFailFast()`, and `error.GetRetryAdvice()`. `Classify` is exhaustive over all 12 `Error` cases. `Error.Aggregate` uses max-severity semantics over its inners; `GetRetryAdvice` returns `null` for `Error.Aggregate` by design. `Error.TransportFault` defaults to `Permanent` because every transport-specific payload shipped today (`HttpError.MethodNotAllowed`, `NotAcceptable`, `PreconditionFailed`, `ContentTooLarge`, `UnsupportedMediaType`, `RangeNotSatisfiable`, `PreconditionRequired`) is a caller-side error; retryable transient transport outcomes (HTTP 429, HTTP 503) are mapped at the boundary to `Error.RateLimited` / `Error.Unavailable` and never reach `Error.TransportFault`. Replaces hand-rolled `gatewayResult.Error is Error.Unavailable or Error.RateLimited` switches in worker loops, message consumers, and outbound-gateway clients.

### Added — pagination ergonomics

- **`Trellis.Core`** — first-class cursor pagination primitives shared by every storage adapter and transport: `PageSize` (with `FromRequested` lenient parser and `TryCreate` strict parser), `Cursor` (opaque `readonly record struct`), `CursorCodec` (URL-safe base64 encode / `TryDecode<TKey>` returning `Result<TKey>` with `Error.InvalidInput.ForField(..., "cursor.malformed", ...)` on bad input), `Page<T>` (`Items`, `Next`, `Previous`, `RequestedLimit`, `AppliedLimit`, `WasCapped`, `DeliveredCount`), `Page<T>.Map<TOut>` (preserves cursors and limits across DTO projection), and `PageBuilder.FromOverFetch` (storage-agnostic over-fetch slicer for single-key and composite `(CreatedAt, Id)` seek). All AOT-friendly — no JSON, no reflection, no `Expression.Compile`.
- **`Trellis.EntityFrameworkCore`** — `PaginationQueryableExtensions.ToPageAsync<T, TKey>(this IQueryable<T>, PageSize, Cursor?, Expression<Func<T, TKey>>, string?, CancellationToken)`. The helper owns the `OrderBy(keySelector)`, the cursor decode (returns `Result.Fail<Page<T>>` with `cursor.malformed` on bad input — never throws), the seek `WHERE` predicate (`Expression.GreaterThan` for numeric / `DateTime` / `DateTimeOffset` keys; `IComparable<TKey>.CompareTo` for `Guid` and `string` keys), the `Take(Applied + 1)` over-fetch, and the slice via `PageBuilder.FromOverFetch`. The canonical EF query-handler shape collapses to one line: `await db.Orders.AsNoTracking().ToPageAsync(pageSize, cursor, o => o.Id.Value, "cursor", ct)`. Value-object Id projections (`c => c.Id.Value`) require `AddTrellisInterceptors()`. Single-key seek requires a stable, unique ascending key; a composite `(CreatedAt, Id)` overload is deferred to a follow-up release.
- **`Trellis.Core`** — `Maybe<T>.HasValueWhere(Func<T, bool>)` for fluent presence-and-predicate checks; `Money.Sum` extension methods over `IEnumerable<Money>` with single-currency validation.

### Added — ProblemDetails `Instance` synthesised from `ResourceRef`

- **`Trellis.Core`** — new `ResourceCollectionNameAttribute(string name)` for marking an aggregate type with a non-default URI collection name (for example `[ResourceCollectionName("people")]` on `Person`). The attribute exposes the value via the `Name` property and validates that it is a non-empty single URL path segment composed only of RFC 3986 `unreserved` characters (ASCII letters and digits plus `-`, `.`, `_`, `~`) at construction time so misconfiguration fails fast. The `IsSafePathSegment(string?)` predicate is exposed as a public static helper (null/empty input returns `false`) so other layers can validate user-provided segment names against the same rule.
- **`Trellis.Asp`** — `ResponseFailureWriter` now synthesises `ProblemDetails.Instance` from the failing `ResourceRef` when the request URL does not already identify the resource. For example, `POST /api/orders` with body `{ customerId: "abc-123" }` that fails with `Error.NotFound(ResourceRef.For("Customer", "abc-123"))` now emits `"instance": "/customers/abc-123"` (the failing resource URI) and preserves the original request URI under `Extensions["request"]`. Applies to every error case that carries a `ResourceRef`: `NotFound`, `Gone`, `Conflict?`, `Forbidden?`, `InvariantViolation?`, and `TransportFault(HttpError.PreconditionFailed)`. Synthesis is suppressed when the URL already identifies the resource (segment- and query-value-aware match against both the raw id and its percent-decoded form; `+` is treated as space in query values to match ASP.NET Core's form-encoding semantics). The aggregate envelope (`Error.Aggregate`) never promotes a child's `ResourceRef` because the envelope itself carries no resource identity.
- **`Trellis.Asp`** — new `TrellisAspOptions.SynthesizeProblemDetailsInstanceFromResourceRef` toggle (default `true`). Set to `false` to preserve the historical request-URL-only `Instance` behavior. The new shape is strictly more informative, so the toggle exists for backward-compat-strict callers only.
- **`Trellis.Asp`** — four new `IServiceCollection` extensions for overriding the default collection name (`{Type.ToLowerInvariant()}s`): `AddResourceCollectionName<T>(string)` and `AddResourceCollectionName(string resourceType, string collectionName)` are AOT- and trim-friendly; `AddResourceCollectionNames(Assembly)` and `AddResourceCollectionNames(params Assembly[])` scan the supplied assemblies for `[ResourceCollectionName]` (both marked `[RequiresUnreferencedCode]`). Lookups are case-insensitive. Registered overrides are emitted verbatim into the synthesised URI (the lowercase guarantee applies only to the naive plural fallback). Conflicting registrations (same type → different collection names) fail fast at registry activation; identical registrations coalesce silently.

### Breaking changes — `Trellis.Core.Error` union DDD realignment

The `Trellis.Core.Error` discriminated union no longer embeds HTTP/RFC transport vocabulary. The domain stays transport-neutral; HTTP-specific error types now live in a new `Trellis.Http.Abstractions` package and flow through `Result<T>` via the `Error.TransportFault(ITransportFault Fault)` envelope.

The closed union now has 12 cases: `InvalidInput`, `InvariantViolation`, `NotFound`, `Forbidden`, `Conflict`, `Gone`, `AuthenticationRequired`, `Unavailable`, `RateLimited`, `Unexpected`, `Aggregate`, `TransportFault`.

#### Migration table

| Old | New |
|---|---|
| `Error.BadRequest("X")` | `Error.InvalidInput.ForRule("X")` |
| `Error.BadRequest("X", pointer)` | `Error.InvalidInput.ForField(pointer, "X")` |
| `Error.BadRequest("X") { Detail = d }` | `Error.InvalidInput.ForRule("X", d)` |
| `Error.UnprocessableContent(fields, rules)` | `new Error.InvalidInput(fields, rules)` |
| `Error.UnprocessableContent.ForField/ForRule(...)` | `Error.InvalidInput.ForField/ForRule(...)` |
| `Error.Unauthorized()` | `Error.AuthenticationRequired()` (optional `Scheme`) |
| `Error.TooManyRequests()` | `Error.RateLimited()` (optional `RetryAdvice`) |
| `Error.ServiceUnavailable()` | `Error.Unavailable()` (optional `ReasonCode`, `RetryAdvice`) |
| `Error.InternalServerError(faultId)` | `new Error.Unexpected(reasonCode, faultId)` |
| `Error.NotImplemented("X")` | `new Error.Unexpected("not_implemented") { Detail = "Feature 'X' is not implemented." }` |
| `Error.MethodNotAllowed`, `NotAcceptable`, `UnsupportedMediaType`, `RangeNotSatisfiable`, `ContentTooLarge`, `PreconditionFailed`, `PreconditionRequired` | Removed from `Trellis.Core`. Use `new Error.TransportFault(new HttpError.X(...))` from `Trellis.Http.Abstractions`. |

#### New cases

- `Error.InvariantViolation(ReasonCode, ResourceRef?)` — global / multi-field business invariant violated outside the inbound-validation pipeline.
- `Error.Aggregate(EquatableArray<Error>)` — first-class envelope for multiple failures.
- `Error.TransportFault(ITransportFault)` — envelope for transport-specific failures (currently `HttpError.*`).

#### New transport-neutral type

- `RetryAdvice(TimeSpan? After = null, DateTimeOffset? At = null)` in `Trellis.Core` — replaces the HTTP-specific `RetryAfterValue` on retry-bearing error cases. `RetryAfterValue` still exists, but now lives in `Trellis.Http.Abstractions` and is used only at the HTTP boundary.

#### Kind-slug changes

Telemetry consumers that aggregate failures by `Error.Kind` need to update their slug sets:

| Old slug | New slug |
|---|---|
| `bad-request` | `invalid-input` (BadRequest folded into InvalidInput) |
| `unprocessable-content` | `invalid-input` |
| `unauthorized` | `authentication-required` |
| `too-many-requests` | `rate-limited` |
| `service-unavailable` | `unavailable` |
| `internal-server-error` | `unexpected` |
| `not-implemented` | `unexpected` (with `ReasonCode == "not_implemented"`) |

#### Wire format unchanged

The HTTP boundary (`Trellis.Asp.ResponseFailureWriter`) preserves the historical problem-details `kind` extension tokens (`unprocessable-content`, `unauthorized`, `too-many-requests`, `service-unavailable`, `internal-server-error`, `not-implemented`) verbatim. External HTTP API consumers parsing problem-details see no wire change. The top-level Problem Details `type` field continues to default to ASP.NET's status-code URL. RFC 9110, 9457, and 6585 compliance is unaffected.

#### New package

`Trellis.Http.Abstractions` — shared by `Trellis.Asp` (server) and `Trellis.Http` (client). Hosts the `HttpError.*` closed union and the HTTP supporting types previously in `Trellis.Core` (`PreconditionKind`, `AuthChallenge`, `RetryAfterValue`, `EntityTagValue`, `AggregateETagExtensions`, `RepresentationMetadata`, `WriteOutcome<T>`). Add `<PackageReference Include="Trellis.Http.Abstractions" .../>` only when boundary code references these types directly; `Trellis.Asp` and `Trellis.Http` bring it in transitively.

See [`MIGRATION_v3.md`](MIGRATION_v3.md#error-union-ddd-realignment) for code-level before/after examples.

### Fixed

- **`Trellis.EntityFrameworkCore`** — `ApplyTrellisConventions(...)` now includes the `Trellis.Authorization` assembly in its default scan set. After the v3 typed-`ActorId` change, an aggregate carrying a `CreatedByActorId : ActorId` audit field silently failed EF mapping because the convention previously only included `Trellis.Core` and `Trellis.Primitives` by default; consumers had to pass `typeof(ActorId).Assembly` explicitly to get the scalar converter. The default scan set now mirrors the `Trellis.Primitives` precedent so `ApplyTrellisConventions(typeof(MyDomainId).Assembly)` is sufficient. `Trellis.EntityFrameworkCore` gains a project reference on `Trellis.Authorization` — a lightweight dependency (its only reference is `Trellis.Core`) that ASP consumers already receive transitively via `Trellis.Asp`.

### Breaking changes — server-side byte-range emission removed

Trellis targets general business web services, not media servers. The server-side byte-range emission surface duplicated `Microsoft.AspNetCore.Http.Results.File(stream, enableRangeProcessing: true)` and added no Trellis-specific value, so it has been removed.

**Removed public API**

- `Trellis.Asp.PartialContentHttpResult` (Minimal API `IResult`)
- `Trellis.Asp.PartialContentResult` (MVC `ObjectResult`)
- `Trellis.Asp.RangeRequestEvaluator` and the `RangeOutcome` closed union (`FullRepresentation` / `PartialContent` / `NotSatisfiable`)
- `HttpResponseOptionsBuilder<T>.WithRange(Func<T, ContentRangeHeaderValue>)`, `WithRange(long, long, long)`, `WithAcceptRanges(string)`
- `RepresentationMetadata.AcceptRanges` and `RepresentationMetadata.Builder.SetAcceptRanges(string)` (every other `RepresentationMetadata` member is unchanged)
- The `Status206PartialContent` `ProducesResponseTypeMetadata` entry from `TrellisHttpResult<TDomain, TBody>.PopulateMetadata` (OpenAPI no longer advertises `206` for Trellis-mapped endpoints)

**Migration**

- For binary downloads with RFC 9110 §14 byte-range semantics, call `Microsoft.AspNetCore.Http.Results.File(stream, enableRangeProcessing: true)` directly — ASP.NET Core implements byte semantics natively.
- For advisory headers such as `Accept-Ranges: none`, write the header on `HttpContext.Response.Headers` from middleware or the endpoint handler.

**Preserved (client-side typed-error round-trip)**

`HttpError.RangeNotSatisfiable(long CompleteLength, string Unit = "bytes")` still exists in `Trellis.Http.Abstractions`. Inbound `416` responses on the HTTP client continue to surface as `Error.TransportFault(new HttpError.RangeNotSatisfiable(...))` with the upstream `Content-Range` length and unit preserved, and `Trellis.Asp.ResponseFailureWriter` still emits `416` plus `Content-Range: {Unit} */{CompleteLength}` when such a fault propagates up through a `Result` chain.

## [3.0.0]

The first GA release under the **Trellis** name. This release supersedes
the `FunctionalDdd` 2.x line; consumers upgrading from `FunctionalDdd 2.1`
should follow the [migration guide](docs/docfx_project/articles/migration.md)
for step-by-step instructions. The summary below describes what changed in
the move from FunctionalDdd 2.1 to Trellis 3.0.

### Project rename and package reorganization (breaking)

- The project is renamed from `FunctionalDdd` to `Trellis`. The root
  namespace, all package ids, and all repository URLs change accordingly
  (e.g., `FunctionalDdd.RailwayOrientedProgramming` becomes `Trellis.Core`).
- The five FunctionalDdd packages are consolidated and expanded into the
  Trellis package family:

  | FunctionalDdd 2.1 package        | Trellis 3.0 package                                  |
  |----------------------------------|------------------------------------------------------|
  | `RailwayOrientedProgramming`     | `Trellis.Core` (folded with DomainDrivenDesign)      |
  | `DomainDrivenDesign`             | `Trellis.Core`                                       |
  | `CommonValueObjects`             | `Trellis.Primitives`                                 |
  | `Asp`                            | `Trellis.Asp`                                        |
  | `FluentValidation`               | `Trellis.FluentValidation`                           |

### New packages

- `Trellis.Mediator` — Result-aware in-process mediator with a canonical
  pipeline (exception → tracing → logging → authorization → resource
  authorization → validation → transactional commit, outermost to innermost).
  Supports both reflection-based and AOT-friendly source-generated dispatch.
- `Trellis.Authorization` — typed `Actor` / `ActorId`, `IAuthorize`,
  `IAuthorizeResource<TResource>`, `IAuthorizeResourceVia<TOwner>`, and the
  ASP integration points (`IActorProvider`, `ClaimsActorProvider`,
  `EntraActorProvider`, `CachingActorProvider`).
- `Trellis.EntityFrameworkCore` — unit-of-work, transactional command
  behavior, `TrellisScalarConverter`, the composite value object EF
  convention, `[OwnedEntity]` attribute, and supporting analyzers.
- `Trellis.StateMachine` — declarative aggregate state machines with
  compile-time transition validation.
- `Trellis.Asp.ApiVersioning` — `WithVersionedRoute()` helper (chained after
  `CreatedAtRoute(...)`, `CreatedAtAction(...)`, or `WithLocation(...)`) and versioned-projection guard
  rails for `Asp.Versioning.Http`.
- `Trellis.ServiceDefaults` — single composition root (`AddTrellis(...)`)
  that wires every framework slot in the right order.
- `Trellis.Http` — typed HTTP-client primitives for outbound calls.
- `Trellis.Testing` and `Trellis.Testing.AspNetCore` — FluentAssertions
  extensions for `Result<T>` / `IResult`, problem-details assertions, and
  WebApplicationFactory helpers.
- `Trellis.Analyzers` — Roslyn analyzers covering Maybe / Result misuse,
  ValueObject derivation, EF / JSON converter wiring, etc. (`TRLS001` …
  `TRLS042+`).

### `Error` redesigned as a closed ADT (breaking)

`Error` is no longer an open class with public constructors and ad-hoc
factories. It is now a closed algebraic data type whose only inhabitants
are the documented kinds: `Validation` / `UnprocessableContent`, `NotFound`,
`Conflict`, `Forbidden`, `Unauthorized`, `InternalServerError`. Each kind
has a typed factory (e.g., `Error.NotFound(...)`, `Error.Conflict(...)`)
that surfaces the metadata the wire mapper needs (resource references,
problem-details fields).

Consumers porting from `FunctionalDdd.Error`'s open constructor / generic
factories should map each call site to one of the kind-specific factories.
The migration guide covers the mechanical replacements.

### `Result<T>` JSON safety net (breaking)

`Result<T>` (and the `IResult` / `IResult<T>` interfaces) now carry a
default `[JsonConverter]` that throws `NotSupportedException` on any direct
JSON serialize / deserialize attempt. Previously, returning a raw
`Result<T>` from a controller silently produced a public-state JSON dump
(for example, `{"IsSuccess": true, "IsFailure": false, "Error": null}` for
a success, with no success value) and still had no HTTP status-code mapping.
The new converter fires on the first request and names the canonical fix:
call `.ToHttpResponse()` (Trellis.Asp) or unwrap
via `Match` / `TryGetValue` before serialization. Option-registered
converters take precedence and let consumers opt back in for logging /
IPC / storage scenarios.

### `RequiredXxx<T>` POLA realignment (breaking)

The `RequiredXxx<T>` family now follows a single rule: **reject only null**.
Per-type "zero value" rejection (`""` for strings, `0` for numerics,
`Guid.Empty`, `DateTime.MinValue`) is opt-in via the new `[NotDefault]`
attribute. String trim is opt-in via `[Trim]`. This makes the family
uniform with `RequiredInt<T>(0)` — which has always succeeded — and matches
the Principle of Least Astonishment.

Validation order in the generated `TryCreate` is `null → [Trim] →
[NotDefault] → [StringLength] / [Range] → ValidateAdditional`. New
compile-time diagnostics `TRLS040` (`[NotDefault]` on `RequiredBool<T>`),
`TRLS041` (`[Trim]` on a non-`RequiredString`), and `TRLS042` (`[NotDefault]`
on `RequiredEnum<T>`) cover degenerate combinations.

The EF Core `TrellisScalarConverter` rehydrates via `TryCreate`, so
lenient-by-default types now accept persisted sentinel values
(`Guid.Empty`, `""`, `DateTime.MinValue`). Add `[NotDefault]` to any
`RequiredGuid` / `RequiredDateTime` used as an aggregate id or EF-mapped
property to preserve strict rehydration.

### Actor model

- `Actor` is an entity (identity-based equality on `ActorId`); equality
  is no longer a record-style structural compare over every field.
- `Actor.Id` is strongly typed as `ActorId : RequiredString<ActorId>`
  (`[Trim, NotDefault]`). The string-accepting constructors and `Create`
  overloads remain for authentication-boundary code (claim → actor) and
  wrap the raw value via `ActorId.Create` internally. JSON serialization
  emits the raw string for wire compatibility.
- `IActorProvider.GetCurrentActorAsync` returns `Task<Maybe<Actor>>`
  (breaking) — anonymous requests are an absence, not a thrown exception.
  `CachingActorProvider` caches both successes and synchronous failures
  from the inner provider.
- `ClaimsActorProvider` understands both short and long claim-name forms
  (including `PermissionsClaim` and `JwtBearer.MapInboundClaims`).

### Mediator, domain events, and resource authorization

- `Trellis.Mediator` introduces a Result-aware pipeline; `AddMediator` and
  the AOT-friendly source-generator path share the same canonical order.
- Domain event dispatch lands inside the unit-of-work commit so events
  publish only when the transaction succeeds.
- Resource authorization (`IAuthorizeResource<TResource>`,
  `IAuthorizeResourceVia<TOwner>`) checks ownership / permissions against
  the loaded resource exactly once per request, slotted immediately before
  the validation behavior.
- Unified validation: a single `ValidationBehavior` runs `IValidate.Validate`
  plus every `IMessageValidator<TMessage>` in DI. FluentValidation plugs in
  as one such validator (`AddTrellisFluentValidation()`) rather than as its
  own pipeline slot.

### EF Core integration

- Composite value object convention: `[OwnedEntity]`-decorated composite
  VOs flow through `TrellisCompositeValueObjectConvention` and a generated
  EF model configuration. The supported shape is `{ get; private set; }`
  on every property — `TRLS022` enforces this.
- `TrellisScalarConverter` round-trips `ScalarValueObject<TSelf, T>` via
  `TryCreate`, surfacing creation failures as
  `TrellisPersistenceMappingException`.
- `CompositeValueObjectJsonConverter<T>` and the matching analyzer
  (`TRLS020`) ensure DTOs exposing `[OwnedEntity]` composites carry a
  `[JsonConverter]` so STJ deserialization goes through `TryCreate`.
- New analyzer `TRLS021` flags redundant manual EF configuration that the
  convention already handles.

### ASP wire contract

- `Error.UnprocessableContent` is the canonical validation failure code;
  domain validation, binder-level value-object validation, and MVC body
  validation all return 422 with a problem-details payload that expands
  composite value-object failures into per-leaf entries.
- `WWW-Authenticate` is emitted on mediator-produced 401 responses.
- `ProblemDetails.Instance` is populated from the request URL (#496).
- `HttpResponseOptionsBuilder<T>` and `HttpResponseOptionsBuilder<Page<T>>`
  support `WithCacheControl(...)` / `CacheControl` presets, `VaryForActor()`
  / `IProvideActorVaryHeaders`, `WithETag` / `WithLastModified` /
  `Vary` / `WithContentLanguage` / `WithContentLocation`, and
  `EvaluatePreconditions()` (`If-None-Match` / `If-Modified-Since` → 304;
  failing `If-Match` / `If-Unmodified-Since` → 412), evaluated once per
  request so non-deterministic selectors do not produce inconsistent
  headers.
- `Maybe<TPrimitive>` is supported directly on DTOs (via
  `MaybePrimitiveJsonConverterFactory`) and on route / query / header
  parameters (via `MaybePrimitiveModelBinder<T>`).

### Documentation

- New cookbook (`docs/docfx_project/api_reference/trellis-api-cookbook.md`)
  with 26+ task-oriented recipes and a per-package API reference under
  `docs/docfx_project/api_reference/`.
- Value-object taxonomy reference
  (`trellis-value-object-taxonomy.md`) covering scalar / composite /
  primitive variants.
- Analyzer surface documented per rule under
  `docs/docfx_project/articles/analyzers/`.
- Migration guide (`docs/docfx_project/articles/migration.md`) covers the
  step-by-step move from FunctionalDdd 2.1 to Trellis 3.0.
- `Trellis.Core` and `Trellis.ServiceDefaults` README files describe the
  composition root and pipeline order.

---

## Previous Releases

Releases prior to 3.0.0 shipped under the FunctionalDdd project name and
are tracked in that repository's history.

[Unreleased]: https://github.com/xavierjohn/Trellis/compare/v3.0.0...HEAD
[3.0.0]: https://github.com/xavierjohn/Trellis/releases/tag/v3.0.0
