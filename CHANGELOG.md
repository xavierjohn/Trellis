# Changelog

All notable changes to the Trellis project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

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
