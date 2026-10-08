---
package: Trellis (cross-package migration)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [migration]
related_docs: [trellis-api-core.md, trellis-api-mediator.md, trellis-start-here.md]
version: v3
last_verified: 2026-10-07
audience: [llm]
agent_usage: onDemand
agent_description: "Open when upgrading previous Result/Error APIs, actor-handler contracts or merged/renamed Trellis packages; not needed for current-API work."
---
# Trellis migration reference

## Use this file when

- You are replacing previous Result factories, implicit conversions or accessors.
- You are upgrading the Error model or removing packages merged into Core, ASP or EF Core.
- You are upgrading actor-aware handlers from an alpha that exposed a public business-test seam.

This is historical upgrade guidance, not the current API catalog. Read the destination
package's reference for complete signatures and behavior. For current-API work, start
at the [router](trellis-start-here.md#patterns-index) instead. The [developer migration
article](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/articles/migration.md#patterns-index)
covers the wider upgrade sequence.

## Actor-aware handler migration

After alpha.553, all six `Trellis.Mediator` actor/resource handler bases have only one
public entry: `Handle(message, CancellationToken)`. Rename the previous protected business
hook to `Handle(message, actor[, resource/leaf], CancellationToken)` with the same parameters,
response type, business body, and cancellation forwarding. Rebuild derived handlers:
the hook rename and removal of the public explicit-argument overloads are deliberate
source- and binary-breaking changes delivered together.

Replace direct actor/resource test calls with Mediator sends using `TestActorProvider`
and fake loaders/business dependencies. Keep the normal pipeline registrations and
provide fake commit/event dependencies when those stages are enabled. There is no
replacement bypass seam. Calling the public entry without an authorized dispatch throws
before business logic. See [current handler contracts](trellis-api-mediator.md#actor-aware-handler-bases)
and [Recipe 10](trellis-api-cookbook.md#recipe-10--test-handler-test-using-trellistesting-shouldbe--unwraperror).

## Core and package migration

The previous development surface is labelled v1 below; current APIs target v3.

| Change | Previous API | Current API | Migration |
|---|---|---|---|
| Result success factory | `Result.Success(value)` / `Result.Success<T>(...)` / `Result.Success()` | `Result.Ok(value)` / `Result.Ok<T>(...)` / `Result.Ok()` | Mechanical find-and-replace of `Result.Success` → `Result.Ok` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Result failure factory | `Result.Failure<T>(error)` / `Result.Failure(error)` | `Result.Fail<T>(error)` / `Result.Fail(error)` | Mechanical find-and-replace of `Result.Failure` → `Result.Fail` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Deferred success factory | `Result.Success(Func<T> funcOk)` | *(removed)* | Inline the factory: `Result.Ok(funcOk())` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Deferred failure factory | `Result.Failure<T>(Func<Error> errorFactory)` | *(removed)* | Inline the factory: `Result.Fail<T>(errorFactory())` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Conditional factory | `Result.SuccessIf(cond, value, error)` / `Result.SuccessIf(cond, t1, t2, error)` | *(removed)* | Use a ternary: `cond ? Result.Ok(value) : Result.Fail<T>(error)` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Inverse-conditional factory | `Result.FailureIf(cond, value, error)` / `Result.FailureIf(predicate, value, error)` | *(removed)* | Use a ternary: `cond ? Result.Fail<T>(error) : Result.Ok(value)` | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Async-conditional factories | `Result.SuccessIfAsync(predicate, value, error)` / `Result.FailureIfAsync(predicate, value, error)` | *(removed)* | `(await predicate()) ? Result.Ok(value) : Result.Fail<T>(error)` (invert as needed; parens required because `await` binds tighter than `?:`) | <!-- stale-doc-ok: migration-comparison row intentionally cites removed v1 factory -->
| Exception → result helpers | `Result.FromException(ex)` / `Result.FromException<T>(ex)` | *(removed)* | Use `Result.Try` / `Result.TryAsync` for inline exception capture, or log the exception and return `Result.Fail(new Error.Unexpected("unhandled-exception", faultId) { Detail = "An unexpected error occurred while processing the request." })`. Do not copy `ex.Message` into public `Detail`. |
| Implicit operators on `Result<T>` | `Result<T> r = value;` and `Result<T> r = error;` | *(removed)* | Use the explicit factory: `Result.Ok(value)` / `Result.Fail<T>(error)`. The compiler flags every site with CS0029. |
| Non-generic `Result` for void flows | `Result` was a separate `readonly struct` for success/failure with no payload, distinct from `Result<T>`. | The non-generic `Result` instance type was removed. `Result` is now a `public static partial class` factory only; for no-payload success/failure use `Result<Unit>` (returned by parameterless `Result.Ok()` / `Result.Fail(error)` / `Result.Ensure(...)` / `Result.Try(...)` factories). The `Trellis.Unit` type is a public `readonly record struct` with a single value (`Unit.Default`). | Replace `Result` parameter/return types with `Result<Unit>`; replace `Task<Result>` with `Task<Result<Unit>>`; in lambdas after `.Bind(...)` / `BindAsync(...)` accept the `Unit` argument explicitly (`_ =>` or `(Unit _) =>`). |
| `Error` as open class hierarchy | `Error` was a `class` with 18 hand-written subclasses (`ValidationError`, `NotFoundError`, …) and static factory helpers (`Error.Validation(...)`, `Error.NotFound(...)`, …). | `Error` is an `abstract record` with **12 nested `sealed record` cases** (`Error.NotFound`, `Error.InvalidInput`, …). Closed via `private` constructor; static factories live on individual cases, not the base. | Replace not-found factories with `Error.NotFound.For<TResource>(id: id, detail: "...")`. Replace validation factories with `Error.InvalidInput.ForField(code, field, detail: detail)` or `Error.InvalidInput.ForRule(code, detail: detail)`. Replace concrete subclass type names (`ValidationError`, `NotFoundError`) with `Error.InvalidInput`, `Error.NotFound`. See [current error cases](trellis-api-core.md#concrete-error-cases). | <!-- v1-stale-ok: migration-comparison row intentionally cites removed v1 factories -->
| `MatchErrorExtensions` | `result.MatchError(onValidation: ..., onNotFound: ..., onUnexpected: ...)` | *(removed)* | Use a `switch` expression on the closed ADT: `result.Match(_ => ..., e => e switch { Error.NotFound nf => ..., Error.InvalidInput uc => ..., _ => ... })`. C# verifies exhaustiveness against the closed catalog. |
| `FlattenValidationErrorsExtensions` | `result.FlattenValidationErrors()` | *(removed)* | `Combine` over multiple `Result<T>` automatically merges `Error.InvalidInput.Fields` and `.Rules`. |
| `Error.Instance` field | `error.Instance` (string-shaped HTTP vocabulary) | *(removed)* | The ASP wire layer populates `ProblemDetails.Instance` from the server-relative request path+query (RFC 9457 §3.1). Typed payloads expose `ResourceRef` directly via fields like `Error.NotFound.Resource` for callers that need to assert on the resource identity. |
| Public `Value` / `Error` accessors on `Result<T>` | Both threw on the wrong branch. | `result.Error` is `public Error?` and **never throws** (null on success). The throwing `result.Value` getter was removed entirely because it was the primary cause of unsafe value access. | Read errors with `if (result.Error is { } error) { ... }` or `result.TryGetError(out var error)`. Extract success values with `result.TryGetValue(out var v)`, `result.TryGetValue(out var v, out var err)`, `result.Match(...)`, or `var (ok, v, err) = result;` (Deconstruct). | <!-- stale-doc-ok: migration-comparison row intentionally cites removed value accessor -->
| HTTP transport abstractions package | `Trellis.Core` | `Trellis.Http.Abstractions` | Add a PackageReference to `Trellis.Http.Abstractions` for code that names `WriteOutcome<T>`, `RepresentationMetadata`, `EntityTagValue`, `RetryAfterValue`, `PreconditionKind`, `AuthChallenge`, or `AggregateETagExtensions`. The CLR namespace stays `Trellis`, so most source files only need the package-reference change. |
| Package id | `Trellis.Results` | `Trellis.Core` | Replace `<PackageReference Include="Trellis.Results" ... />` with `<PackageReference Include="Trellis.Core" ... />`. The CLR namespace stays `Trellis` — no `using` changes are needed. The legacy `Trellis.Results` package is unlisted with a redirect notice; there is no metapackage shim. | <!-- stale-doc-ok: migration-comparison row intentionally cites previous package id -->
| OpenTelemetry `ActivitySource` name | `"Trellis.Results"` | `"Trellis.Results"` (unchanged) | No change needed. The source is named for the operations it traces, not for the package that ships it, so the v1 name carried over. The `ResultsTraceProviderBuilderExtensions.ActivitySourceName` constant exposes it programmatically. | <!-- stale-doc-ok: migration-comparison row intentionally cites the v1 activity source name -->
| Test helper namespace | `Trellis.Results.Tests.*` | `Trellis.Core.Tests.*` | Internal change only — affects users who took an InternalsVisibleTo dependency on the test assembly (none expected). | <!-- stale-doc-ok: migration-comparison row intentionally cites previous test namespace -->
| Package merge: DDD | <PackageReference Include="Trellis.DomainDrivenDesign" .../> | *(removed)* | All DDD types (`Aggregate<T>`, `Entity<T>`, `ValueObject`, `Specification<T>`, etc.) moved into `Trellis.Core`. Drop the `Trellis.DomainDrivenDesign` PackageReference; the types are still in `namespace Trellis;` so no using changes are needed. | <!-- stale-doc-ok: migration-comparison row intentionally cites previous package id -->
| Package merge: Primitives generator | <PackageReference Include="Trellis.Primitives.Generator" .../> | *(removed)* | The Required* source generator is now bundled inside `Trellis.Core.nupkg` (`analyzers/dotnet/cs/Trellis.Core.Generator.dll`). Installing `Trellis.Core` (or any package depending on it) attaches the analyzer automatically. Drop the standalone PackageReference. |
| `Required*` base classes | `Trellis.Primitives` | `Trellis.Core` | Source-tree consumers may need to ensure they reference `Trellis.Core`. Namespace is unchanged (`Trellis`), so no using edits are required. |
| Package merge: Asp generator | `<PackageReference Include="Trellis.AspSourceGenerator" .../>` | *(removed)* | The ASP source generator is now bundled inside `Trellis.Asp.nupkg` (`analyzers/dotnet/cs/Trellis.AspSourceGenerator.dll`). Installing `Trellis.Asp` attaches the analyzer automatically. Drop the standalone PackageReference. |
| Package merge: EF Core generator | `<PackageReference Include="Trellis.EntityFrameworkCore.Generator" .../>` | *(removed)* | The EF Core source generator (Maybe&lt;T&gt; partial properties + owned value-object helpers) is now bundled inside `Trellis.EntityFrameworkCore.nupkg` (`analyzers/dotnet/cs/Trellis.EntityFrameworkCore.Generator.dll`). Installing `Trellis.EntityFrameworkCore` attaches the analyzer automatically. Drop the standalone PackageReference. |
| Package merge: Asp authorization | `<PackageReference Include="Trellis.Asp.Authorization" .../>` | *(removed)* | The ASP.NET actor providers (`ClaimsActorProvider`, `EntraActorProvider`, `DevelopmentActorProvider`, `CachingActorProvider`) are now part of `Trellis.Asp.nupkg`. The CLR namespace stays `Trellis.Asp.Authorization` — no `using` changes needed. Drop the standalone PackageReference. `Trellis.Asp` now transitively brings in `Trellis.Authorization`. |

The renames bring the factory names in line with Rust (`Ok`/`Err`), F# (`Ok`), and FluentResults (`Ok`/`Fail`). The `IsSuccess`/`IsFailure` predicate properties are **not** renamed — predicates read as questions and stay long-form.
