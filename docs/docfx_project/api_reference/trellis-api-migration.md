---
package: Trellis (FunctionalDDD migration)
namespaces: [Trellis, Trellis.Asp]
types: [migration]
related_docs: [trellis-api-core.md, trellis-api-primitives.md, trellis-api-asp.md, trellis-start-here.md]
version: v3
last_verified: 2026-10-07
audience: [llm]
agent_usage: onDemand
agent_description: "Open when migrating a FunctionalDDD 2.x application to Trellis: package and namespace changes, Result/Error APIs, value objects and HTTP mapping."
---
# Migrating from FunctionalDDD to Trellis

## Use this file when

- You are replacing the released `FunctionalDdd.*` packages with `Trellis.*`.
- You need the net API changes from FunctionalDDD 2.x to the current Trellis surface.

FunctionalDDD is the released predecessor; Trellis has not had a stable release.
This guide compares FunctionalDDD with current Trellis APIs, not successive Trellis
alpha builds or internally labelled development versions. Alpha-to-alpha changes
belong in the [changelog](https://github.com/xavierjohn/Trellis/blob/main/CHANGELOG.md).

The source baseline was checked against the published FunctionalDDD RailwayOrientedProgramming
2.1.10 package and its [source commit](https://github.com/xavierjohn/Trellis/tree/62c2157639d0db37028d9c20fd9a768a164c73a7).
Read the destination package reference for complete current signatures. The
[developer guide](https://github.com/xavierjohn/Trellis/blob/main/docs/docfx_project/articles/migration.md#patterns-index)
gives the recommended migration order. For new Trellis code, use the [router](trellis-start-here.md#patterns-index).

## Core and package migration

### Packages and namespaces

| FunctionalDDD package | Trellis replacement |
|---|---|
| `<PackageReference Include="FunctionalDdd.RailwayOrientedProgramming" />` | `Trellis.Core` |
| `<PackageReference Include="FunctionalDdd.DomainDrivenDesign" />` | `Trellis.Core` |
| `<PackageReference Include="FunctionalDdd.CommonValueObjects" />` | `Trellis.Primitives`; custom scalar base classes now live in `Trellis.Core` |
| `<PackageReference Include="FunctionalDdd.CommonValueObjectGenerator" />` | Remove the separate reference; the generator is bundled in `Trellis.Core` |
| `<PackageReference Include="FunctionalDdd.Asp" />` | `Trellis.Asp` |
| `<PackageReference Include="FunctionalDdd.FluentValidation" />` | `Trellis.FluentValidation` |

Update package references and centrally managed versions together. Replace `using FunctionalDdd;`
and global imports with `using Trellis;`. FunctionalDDD's response extensions also lived in
the root namespace; add `using Trellis.Asp;` where the new response-mapping APIs are used.
Trellis targets .NET 10, so update the consuming project's target framework and SDK.
Packages such as `Trellis.Mediator`, `Trellis.Authorization`, `Trellis.EntityFrameworkCore`,
`Trellis.Http`, and `Trellis.StateMachine` are optional additions, not renamed FunctionalDDD packages.

### Result factories and access

| FunctionalDDD API / behavior | Current Trellis API / migration |
|---|---|
| `Result.Success(value)` / `Result.Success()` | `Result.Ok(value)` / `Result.Ok()` | <!-- stale-doc-ok: FunctionalDDD factory comparison -->
| `Result.Failure<T>(error)` / `Result.Failure(error)` | `Result.Fail<T>(error)` / `Result.Fail(error)` | <!-- stale-doc-ok: FunctionalDDD factory comparison -->
| Deferred `Result.Success(Func<T>)` / `Result.Failure<T>(Func<Error>)` | Invoke the factory explicitly: `Result.Ok(funcOk())` / `Result.Fail<T>(errorFactory())` | <!-- stale-doc-ok: FunctionalDDD factory comparison -->
| `Result.SuccessIf(...)` / `Result.FailureIf(...)`, including tuple and async forms | Use a ternary with `Result.Ok` / `Result.Fail<T>`; await an async predicate before branching | <!-- stale-doc-ok: FunctionalDDD conditional-factory comparison -->
| `Result.FromException` / `Result.FromException<T>` | Use `Result.Try` / `Result.TryAsync` around the operation, or log an already-caught exception and return an appropriate failure; never copy its message into public error detail |
| Implicit value/error conversion to `Result<T>` | Wrap explicitly with `Result.Ok(value)` / `Result.Fail<T>(error)` |
| `value.ToResult(error)` on a nullable reference or struct | `Result.EnsureNotNull(value, error)`; Task/ValueTask receivers use `EnsureNotNullAsync(error)` |
| Throwing `result.Value` getter | Use `TryGetValue`, `Match`, guarded deconstruction, or `Map` / `Bind` inside a chain | <!-- stale-doc-ok: FunctionalDDD accessor comparison -->
| Throwing `result.Error` getter on success | `Error` is nullable and never throws; use a null pattern or `TryGetError` |
| `default(Result<T>)` reports success | Default is a failure; construct every intended success explicitly |

**No-payload results keep their shape.** FunctionalDDD already returned `Result<Unit>` from
its parameterless success and failure factories. In Trellis, `Result.Ok()` and
`Result.Fail(error)` also return `Result<Unit>`; there is no non-generic instance-result
migration to perform. `IsSuccess` and `IsFailure` retain their names.

For ordinary required fields, prefer the lazy field/detail null guard; construct custom
errors inside its factory overload. `Maybe<T>.ToResult` remains an absence-to-failure
conversion; do not replace it with a nullable guard.
See [choosing a Result entry point](trellis-api-core.md#choosing-a-result-entry-point).

### Railway operations

| FunctionalDDD operation | Current Trellis operation |
|---|---|
| `TapError(...)` / `TapErrorAsync(...)` | `TapOnFailure` / `TapOnFailureAsync` |
| `MapError(...)` / `MapErrorAsync(...)` | `MapOnFailure` / `MapOnFailureAsync` |
| `Compensate(...)` / `CompensateAsync(...)` | `RecoverOnFailure` / `RecoverOnFailureAsync` |
| Two-branch `Finally(...)` / `FinallyAsync(...)` | `Match` / `MatchAsync` | <!-- stale-doc-ok: FunctionalDDD terminal-operation comparison -->

For a `Finally` callback that accepts the whole result rather than separate success/error
callbacks, invoke that callback explicitly; do not invent a one-callback `Match` overload.
See [current Result extension families](trellis-api-core.md#result-pipeline-extension-families).

### Error model

FunctionalDDD's open `Error` class, public constructors, subclasses, and base factories
become Trellis's closed `Error` record catalog. Choose a case by its domain meaning rather
than recreating a custom subclass.

| FunctionalDDD shape | Current Trellis shape |
|---|---|
| `ValidationError` / `Error.Validation(...)` | `Error.InvalidInput.ForField(code, field, detail: detail)` or `ForRule(code, detail: detail)`; preserve all violations | <!-- v1-stale-ok: historical FunctionalDDD API comparison -->
| `NotFoundError` / `Error.NotFound(...)` | `Error.NotFound.For<TResource>(id: id, detail: detail)`; preserve an application code with the named `code:` argument | <!-- v1-stale-ok: historical FunctionalDDD API comparison -->
| `new ConflictError(...)` | `Error.Conflict.For<TResource>(code, id: id, detail: detail)` or `ForReason(code, detail: detail)` | <!-- stale-doc-ok: FunctionalDDD error comparison -->
| `new UnauthorizedError(...)` | `new Error.AuthenticationRequired() { Code = code, Detail = detail }` | <!-- stale-doc-ok: FunctionalDDD error comparison -->
| `new ForbiddenError(...)` | `Error.Forbidden.ForPolicy(code, detail: detail)` or a resource-qualified `For` factory | <!-- stale-doc-ok: FunctionalDDD error comparison -->
| `UnexpectedError` | `new Error.Unexpected(code, faultId) { Detail = safeDetail }`; keep exception details in logs | <!-- stale-doc-ok: FunctionalDDD error comparison -->
| `validationError.FieldErrors` with field names and detail lists | `Error.InvalidInput.Fields` / `.Rules` with input pointers, reason codes, arguments, and detail | <!-- stale-doc-ok: FunctionalDDD validation comparison -->
| `Error.Instance` | ASP owns `ProblemDetails.Instance`; use `ResourceRef` for domain resource identity |
| Base `Error` equality compares only `Code` | Trellis errors compare their typed value payload and detail; compare `Code` explicitly when only the reason matters |

Case-scoped factories are **code-first**. Preserve application codes and details, but
reconsider clients/tests that depend on the previous error classes, JSON fields, or
HTTP mappings. For a simple property, `ForField(code, "email", ...)` produces `/email`;
use an explicit `InputPointer` for nested paths and input locations.
See [current error cases and factories](trellis-api-core.md#construction-and-case-scoped-factories).

### Scalar value objects

Custom partial types deriving from FunctionalDDD's `RequiredString` / `RequiredGuid`
now derive from `RequiredString<TSelf>` / `RequiredGuid<TSelf>`. Keep the partial declaration;
reuse inherited value/equality members and generated factories rather than redeclaring them.
Replace generated `NewUnique()` calls with `NewUniqueV4()` to retain random-GUID generation,
or choose `NewUniqueV7()` deliberately for time-ordered IDs.

FunctionalDDD rejected blank required strings and `Guid.Empty`. Trellis's generated
Required types reject only null by default: `[NotDefault]` rejects the sentinel, while
`[Trim]` enables string trimming. `[NotDefault]` alone does not reject whitespace-only
strings; combining `[Trim, NotDefault]` rejects them but also changes stored text by trimming.
Use `ValidateAdditional` when preserving the old nonblank-but-untrimmed string behavior.
See [Required defaults and opt-ins](trellis-api-primitives.md#required-defaults-and-opt-ins).

### ASP.NET Core and observability

Replace FunctionalDDD's `ToHttpResult(...)` / `ToActionResult(...)` and async forms with
`ToHttpResponse` / `ToHttpResponseAsync`. For typed MVC signatures, chain
`AsActionResult<T>` / `AsActionResultAsync<T>`. Register `AddTrellisAsp` and use the
[ASP reference](trellis-api-asp.md#patterns-index) for Created locations and failure mapping.
Do not serialize a raw `Result<T>`; Trellis requires explicit HTTP mapping or payload extraction.

Telemetry subscriptions change too: `"Functional DDD ROP"` becomes `"Trellis.Results"`,
and `"Functional DDD CVO"` becomes `"Trellis.Primitives"`. Replace
`AddFunctionalDddRopInstrumentation()` with `AddTrellisResultsInstrumentation` and
`AddFunctionalDddCvoInstrumentation()` with `AddTrellisPrimitivesInstrumentation`.
Update collectors, filters, and tests that use the old source names.
