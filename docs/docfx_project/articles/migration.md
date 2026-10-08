---
title: Migrating from FunctionalDDD to Trellis
package: Trellis (multiple)
topics: [migration, breaking-changes, functionalddd-to-trellis, package-renames]
related_api_reference: [trellis-api-migration.md, trellis-api-core.md, trellis-api-primitives.md, trellis-api-asp.md]
last_verified: 2026-10-07
audience: [developer]
---
# Migrating from FunctionalDDD to Trellis

This guide takes an application using the released FunctionalDDD 2.x packages directly
to the current Trellis APIs. Trellis is the renamed framework and has not had a stable
release. There is no released "Trellis v1" or "Trellis v2" to upgrade through.

The [migration reference](../api_reference/trellis-api-migration.md#core-and-package-migration)
is the source-backed comparison of FunctionalDDD and Trellis. Intermediate alpha changes
belong in the [changelog](https://github.com/xavierjohn/Trellis/blob/main/CHANGELOG.md),
not this guide.

## Patterns Index

| Task | See |
|---|---|
| Replace FunctionalDDD packages and namespaces | [Package map](#package-map-legacy--current) |
| Rename Result factories and railway operations | [Result and Error renames](#result-and-error-renames-trelliscore) |
| Replace throwing result access and implicit conversions | [Result access](#result-access) |
| Port open error subclasses and validation errors | [Error model](#error-model) |
| Update generated scalar value objects without changing validation accidentally | [Value objects](#value-objects) |
| Map MVC and Minimal API responses | [ASP.NET Core](#aspnet-core-trellisasp) |
| Update tracing subscriptions | [Observability](#observability) |
| Apply the changes in a practical order | [Migration order](#practical-guidance) |

## Package map (legacy → current)

| FunctionalDDD package | Trellis package | Notes |
|---|---|---|
| `FunctionalDdd.RailwayOrientedProgramming` | `Trellis.Core` | Result, Maybe, Error, and railway operations |
| `FunctionalDdd.DomainDrivenDesign` | `Trellis.Core` | DDD primitives are in the same package as the railway operations |
| `FunctionalDdd.CommonValueObjects` | `Trellis.Primitives` | Built-in value objects; custom scalar base classes live in `Trellis.Core` |
| `FunctionalDdd.CommonValueObjectGenerator` | No separate package | The generator is bundled in `Trellis.Core` |
| `FunctionalDdd.Asp` | `Trellis.Asp` | MVC and Minimal API response mapping |
| `FunctionalDdd.FluentValidation` | `Trellis.FluentValidation` | Standalone FluentValidation-to-Result adapters |

Update `PackageReference` items and `Directory.Packages.props` together. Change
`FunctionalDdd` namespace imports, including global usings, to `Trellis`. The old response
extensions lived in that root namespace; add `Trellis.Asp` imports for their replacements.
Trellis targets .NET 10, so update the consuming project's SDK and target framework before restoring.

Trellis's Mediator, authorization, EF Core, HTTP client, state-machine, and testing
packages are optional additions. Do not invent corresponding old FunctionalDDD package
references or add all Trellis modules just to complete the migration.

## Result and Error renames (Trellis.Core)

| FunctionalDDD API | Trellis API |
|---|---|
| `Result.Success(value)` / `Result.Success()` | `Result.Ok(value)` / `Result.Ok()` |
| `Result.Failure<T>(error)` / `Result.Failure(error)` | `Result.Fail<T>(error)` / `Result.Fail(error)` |
| `TapError` / `TapErrorAsync` | `TapOnFailure` / `TapOnFailureAsync` |
| `MapError` / `MapErrorAsync` | `MapOnFailure` / `MapOnFailureAsync` |
| `Compensate` / `CompensateAsync` | `RecoverOnFailure` / `RecoverOnFailureAsync` |
| Two-branch `Finally` / `FinallyAsync` | `Match` / `MatchAsync` |

`IsSuccess` and `IsFailure` retain their names. For a whole-result `Finally` callback,
call the callback explicitly rather than treating it as a one-callback `Match`.

Deferred factories now invoke the supplied function explicitly. Replace conditional
factories with a ternary using `Result.Ok` and `Result.Fail<T>`, awaiting async predicates
before branching. Replace `Result.FromException` with `Result.Try` / `Result.TryAsync`
around an operation, or map an already-caught exception to an appropriate safe failure.
See the [complete factory comparison](../api_reference/trellis-api-migration.md#result-factories-and-access).

**Keep no-payload handlers on `Result<Unit>`.** FunctionalDDD already used that shape;
Trellis's parameterless `Result.Ok()` and `Result.Fail(error)` still return it. There is
no intermediate non-generic instance-result migration.

## Result access

Implicit value/error conversions to `Result<T>` are removed. Wrap successful values
with `Result.Ok(value)` and errors with `Result.Fail<T>(error)`.

Replace nullable `value.ToResult(error)` with `Result.EnsureNotNull(value, error)`,
or `EnsureNotNullAsync(error)` on a nullable Task/ValueTask result. Prefer field/detail
guards for ordinary required fields and lazy factories for custom errors.
`Maybe<T>.ToResult` still converts absence to failure and must not become a nullable guard.
See [choosing a Result entry point](../api_reference/trellis-api-core.md#choosing-a-result-entry-point).

The throwing `result.Value` getter is removed. Extract a payload with `TryGetValue`,
`Match`, or guarded deconstruction; inside a railway chain, use `Map` or `Bind`.
The `Error` getter is now nullable and never throws on success; use a null pattern or
`TryGetError`. Default-initialized results are failures, not successes.

Trellis also rejects direct JSON serialization of result wrappers. HTTP endpoints must
map them to responses; other boundaries should extract the payload explicitly.
See [current Result behavior](../api_reference/trellis-api-core.md#public-readonly-struct-resulttvalue--iresulttvalue-iequatableresulttvalue-ifailurefactoryresulttvalue-ipersistonfailure).

## Error model

FunctionalDDD's open error hierarchy becomes the closed Trellis `Error` record catalog.
Map errors by domain meaning and preserve application codes and safe details:

| FunctionalDDD shape | Trellis shape |
|---|---|
| `ValidationError` / `Error.Validation(...)` | `Error.InvalidInput.ForField(code, field, detail: detail)` / `ForRule(code, detail: detail)` | <!-- v1-stale-ok: historical FunctionalDDD API comparison -->
| `NotFoundError` | `Error.NotFound.For<TResource>(id: id, detail: detail)` |
| `ConflictError` | `Error.Conflict.For<TResource>(code, id: id, detail: detail)` / `ForReason(code, detail: detail)` |
| `UnauthorizedError` | `new Error.AuthenticationRequired() { Code = code, Detail = detail }` |
| `ForbiddenError` | `Error.Forbidden.ForPolicy(code, detail: detail)` |
| `UnexpectedError` | `new Error.Unexpected(code, faultId) { Detail = safeDetail }` |

Do not recreate custom subclasses; choose a case that represents the failure.
Factories are code-first. Validation carries field/rule violations rather than the old
field-name/detail-list shape: keep every violation, its code, and its detail, and build
explicit input pointers for nested paths. `Error.Instance` is not a domain field in
Trellis; the ASP boundary sets `ProblemDetails.Instance`, while `ResourceRef` carries
domain resource identity.

Revisit equality assertions and client error parsing. FunctionalDDD's base error equality
compared codes; Trellis errors use typed value equality. Old JSON fields and HTTP status
mappings are not assumed unchanged. See the
[error comparison](../api_reference/trellis-api-migration.md#error-model) and
[current factories](../api_reference/trellis-api-core.md#construction-and-case-scoped-factories).

## Value objects

Retain partial declarations and change the scalar bases to the self-typed forms:
`RequiredString<TSelf>` and `RequiredGuid<TSelf>`. The generator is bundled with
`Trellis.Core`; do not duplicate inherited value/equality members or generated factories.
Generated `NewUnique()` calls become `NewUniqueV4()` for the same random-GUID choice,
or `NewUniqueV7()` if the application deliberately wants time-ordered IDs.

**Review validation, not just compilation.** FunctionalDDD rejected blank required
strings and `Guid.Empty`. Trellis accepts concrete values by default and rejects only
null. `[NotDefault]` rejects the sentinel; `[Trim]` opts into string trimming.
For strings, `[NotDefault]` alone still accepts whitespace-only text.
`[Trim, NotDefault]` rejects it but also trims valid text, unlike FunctionalDDD's
untrimmed string behavior. Use `ValidateAdditional` to preserve nonblank validation
without changing stored text. See [defaults and opt-ins](../api_reference/trellis-api-primitives.md#required-defaults-and-opt-ins).

## ASP.NET Core (Trellis.Asp)

Register `AddTrellisAsp` and replace `ToHttpResult` / `ToActionResult` and their async
forms with `ToHttpResponse` / `ToHttpResponseAsync`. For typed MVC signatures, chain
`AsActionResult<T>` / `AsActionResultAsync<T>`.

Created responses and failure mappings are configured through the current response
options, not a sequence of intermediate Trellis mapping APIs. Review status codes,
locations, and Problem Details bodies in endpoint tests.
See the [ASP reference](../api_reference/trellis-api-asp.md#patterns-index).

## Observability

| FunctionalDDD subscription | Trellis subscription |
|---|---|
| `"Functional DDD ROP"` | `"Trellis.Results"` |
| `"Functional DDD CVO"` | `"Trellis.Primitives"` |
| `AddFunctionalDddRopInstrumentation` | `AddTrellisResultsInstrumentation` |
| `AddFunctionalDddCvoInstrumentation` | `AddTrellisPrimitivesInstrumentation` |

Update collectors, activity listeners, source filters, and tracing tests. The sources
are renamed from FunctionalDDD; they are not unchanged subscriptions to older Trellis
packages. See [observability setup](integration-observability.md).

## Practical guidance

1. Keep a passing FunctionalDDD build/test baseline.
2. Update the SDK, target framework, package references, and namespace imports.
3. Rename factories and railway operations; make implicit Result construction explicit.
4. Port the Error model and scalar value-object declarations, preserving intended validation.
5. Replace throwing result access and update HTTP mapping and telemetry subscriptions.
6. Build and test business outcomes, value-object edge cases, failure bodies, and tracing.
7. Adopt additional Trellis integration modules separately if the application needs them.

## Cross-references

- [FunctionalDDD-to-Trellis API comparison](../api_reference/trellis-api-migration.md#core-and-package-migration)
- [Core contracts](../api_reference/trellis-api-core.md#patterns-index)
- [Value-object defaults](../api_reference/trellis-api-primitives.md#required-defaults-and-opt-ins)
- [ASP response mapping](../api_reference/trellis-api-asp.md#patterns-index)
- [Current recipe lookup](../api_reference/trellis-start-here.md#patterns-index)
