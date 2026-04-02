# Changelog

All notable changes to the Trellis project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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

Lightweight authorization primitives with zero dependencies beyond `Trellis.Results`:

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

#### Trellis.Results — IFailureFactory

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
- **SampleWeb apps** updated — `Maybe<Url> Website` on User/RegisterUserDto, `Maybe<FirstName> AssignedTo` on UpdateOrderDto

### Changed

- `Maybe<T>` now requires `where T : notnull` — see [Migration Guide](MIGRATION_v3.md#maybe-notnull-constraint) for details

---

#### Trellis.Analyzers - NEW Package! 🎉

A comprehensive suite of 18 Roslyn analyzers to enforce Railway Oriented Programming best practices at compile time:

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
- **TRLS017**: Don't compare Result/Maybe to null (they're structs)
- **TRLS018**: Unsafe `.Value` access in LINQ without filtering first

**Best Practice Rules (Info):**
- **TRLS002**: Suggest `Bind` instead of `Map` when lambda returns Result
- **TRLS005**: Suggest `MatchError` for type-safe error discrimination
- **TRLS010**: Suggest specific error types instead of base `Error` class
- **TRLS012**: Suggest `Result.Combine()` for multiple Result checks
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
