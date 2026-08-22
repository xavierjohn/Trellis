---
title: Error Handling
package: Trellis.Core
topics: [error-handling, error-adt, fail, problem-details, hierarchy, validation, aggregate]
related_api_reference: [trellis-api-core.md]
last_verified: 2026-05-01
audience: [developer]
---
# Error Handling

`Error` is a closed discriminated union of typed records that lets you return failures as values and pattern-match them at the boundary, without falling back to exceptions for normal control flow.

## Patterns Index

| Goal | Use | See |
|---|---|---|
| Return a typed failure from a function | `Result.Fail<T>(new Error.X(payload) { Detail = "..." })` | [Creating errors](#creating-errors) |
| Fail but still persist staged work (worker handler) | `Result.FailAfterCommit<T>(error)` | [Persisting failure state from a worker handler](integration-mediator.md#persisting-failure-state-from-a-worker-handler) |
| Build a single-violation 422 from a property name | `Error.InvalidInput.ForField("email", "required", "...")` | [Validation failures](#validation-failures) |
| Build a single-violation 422 from an object-level rule | `Error.InvalidInput.ForRule("passwords_must_match", "...")` | [Validation failures](#validation-failures) |
| Aggregate per-field and cross-field violations | `new Error.InvalidInput(Fields: ..., Rules: ...)` | [Validation failures](#validation-failures) |
| Branch on the closed catalog at a boundary | `result.Match(value => ..., error => error switch { Error.NotFound nf => ..., ... })` | [Pattern matching](#pattern-matching) |
| Log without changing the result | `result.TapOnFailure(error => logger.LogWarning(...))` | [Propagating errors](#propagating-errors) |
| Translate one failure case into another | `result.MapOnFailure(error => error switch { ... })` | [Propagating errors](#propagating-errors) |
| Capture a thrown exception as a typed error | `Result.Try(() => ..., ex => new Error.X(...))` | [Capturing exceptions](#capturing-exceptions) |
| Merge multiple failures into one | `Result.Combine(r1, r2, r3)` or `left.Combine(right)` on `Error` | [Composition](#composition) |
| Map an `Error` to an HTTP / Problem Details response | `result.ToHttpResponse(...)` (in `Trellis.Asp`) | [Boundary mapping](#boundary-mapping) |

## Use this guide when

- You want expected failures (validation, not found, conflict, forbidden) to be values your callers can branch on, not exceptions.
- You need compile-time exhaustiveness across every failure case at a boundary.
- You are aggregating multiple validation failures and want to keep them all instead of throwing the first one away.
- You need a stable wire vocabulary (`Kind`, `Code`, payload) that survives refactors and renders consistently as Problem Details.

## Surface at a glance

`Error` is an `abstract record` with **12 nested `sealed record` cases**. The base type has a `private` constructor, so the catalog is closed and `switch` over an `Error` reference is exhaustive at the language level.

| Category | Cases |
|---|---|
| Input validation | `InvalidInput`, `InvariantViolation` |
| Resource access | `NotFound`, `Gone`, `Forbidden`, `AuthenticationRequired` |
| State | `Conflict` |
| Availability | `Unavailable`, `RateLimited` |
| Unexpected | `Unexpected` |
| Transport envelope | `TransportFault` |
| Composition | `Aggregate` |

Every case carries a strongly-typed payload, an init-only `Detail` property, and a structured (never `Exception`) `Cause` chain. Full per-case constructor signatures, payload notes, and the supporting types (`ResourceRef`, `InputPointer`, `FieldViolation`, `RuleViolation`, `RetryAdvice`, `ITransportFault`, `EquatableArray<T>`) live in [`trellis-api-core.md` → `Error`](../api_reference/trellis-api-core.md#public-abstract-record-error).

> [!NOTE]
> `Unexpected(code, faultId?)` covers both unhandled faults (set `FaultId` to correlate with telemetry) and internal invariant violations ("shouldn't happen"). The optional `FaultId` surfaces as a `faultId` problem-details extension at the ASP boundary when set. Use the `ReasonCode == FaultCodes.NotImplemented` convention if you need the boundary to map to HTTP 501; the default maps to 500.

## Installation

```bash
dotnet add package Trellis.Core
```

## Quick start

Return a typed failure, then pattern-match at the boundary.

```csharp
using Trellis;

public sealed record User(string Id, string Email);

public static class UserService
{
    public static Result<User> GetUser(string id) =>
        id == "42"
            ? Result.Ok(new User("42", "ada@example.com"))
            : Result.Fail<User>(new Error.NotFound(ResourceRef.For<User>(id)) { Detail = $"User {id} not found" });
}

var message = UserService.GetUser("42").Match(
    onSuccess: user  => $"200 OK: {user.Email}",
    onFailure: error => error switch
    {
        Error.NotFound nf  => $"404 Not Found: {nf.Detail}",
        Error.Forbidden f  => $"403 Forbidden: {f.PolicyId}",
        _                  => $"500 Internal: {error.Kind}",
    });
```

## Creating errors

There are no static factory methods on the base `Error` type. Every call site names the case it produces:

```csharp
new Error.NotFound(ResourceRef.For<Order>("42")) { Detail = "Order 42 not found" }
```

`Detail` is an init-only property on the base record; set it via object-initializer when you want to override the boundary renderer's default human-readable text. `Kind` is the stable domain slug and `Code` is the per-instance machine identifier exposed by that case.

For the common single-payload shapes, the resource-bearing cases (`NotFound`, `Gone`, `Conflict`, `Forbidden`, `InvariantViolation`) and `InvalidInput` also expose **case-scoped** convenience factories that bundle the typed payload while still naming the case — they only remove the `ResourceRef` / `InputPointer` construction ceremony:

```csharp
Error.NotFound.For<Order>(id, "Order not found")            // wraps id in ResourceRef.For<Order>(id)
Error.Conflict.ForReason("duplicate.key", "Email in use")   // resourceless; ForReason leads with the reason code
Error.Forbidden.ForPolicy("orders.write", "Admin required") // resourceless; or For<TResource>(policyId, id, detail)
Error.InvariantViolation.ForReason("cross_aggregate_rule")  // resourceless; or For<TResource>(reasonCode, id, detail)
Error.InvalidInput.ForField("email", "invalid", "Bad email")// or ForRule(code, detail)
```

| Pattern | Example |
|---|---|
| Resource not found | `new Error.NotFound(ResourceRef.For<Order>(id)) { Detail = $"Order {id} not found" }` |
| State conflict | `new Error.Conflict(ResourceRef.For<User>(userId), "duplicate.key") { Detail = "Email is already in use" }` |
| Domain rule conflict (no resource) | `new Error.Conflict(null, "cancel_after_ship") { Detail = "Cannot cancel after shipment" }` |
| Authentication missing | `new Error.AuthenticationRequired()` |
| Authenticated but not allowed | `new Error.Forbidden("orders.write") { Detail = "Administrator role required" }` |
| Soft-deleted resource | `new Error.Gone(ResourceRef.For<Document>(id))` |
| Wrong content type | `new Error.TransportFault(new HttpError.UnsupportedMediaType(EquatableArray.Create("application/json")))` |
| Body too large | `new Error.TransportFault(new HttpError.ContentTooLarge(10 * 1024 * 1024))` |
| Method not supported | `new Error.TransportFault(new HttpError.MethodNotAllowed(EquatableArray.Create("GET", "POST")))` |
| Rate limited | `new Error.RateLimited(new RetryAdvice(After: TimeSpan.FromSeconds(30)))` |
| Dependency unavailable | `new Error.Unavailable(new RetryAdvice(After: TimeSpan.FromSeconds(120))) { Code = "payment_gateway_offline" }` |
| Unhandled fault | `new Error.Unexpected("crm.timeout", faultId)` |
| Aggregate invariant | `new Error.InvariantViolation("cross_aggregate_rule", ResourceRef.For<Order>(orderId))` |

> [!TIP]
> Reach for `new Error.Conflict(resource, "domain.violation")` when state blocks an otherwise-valid request. `Error.InvalidInput` is for input the caller can fix. `Error.InvariantViolation` is for domain rules that aren't bound to a specific request field (e.g. a cross-aggregate rule, an internal precondition).

### Validation failures

`Error.InvalidInput` carries `EquatableArray<FieldViolation> Fields` and `EquatableArray<RuleViolation> Rules`. For single-violation cases prefer the static factories on the case itself; the boundary renderer surfaces a single field-violation's `Detail` as the response detail directly.

| Factory | Use when |
|---|---|
| `Error.InvalidInput.ForField(propertyName, reasonCode, detail?)` | Single property failure named by simple property (escaped via `InputPointer.ForProperty`). |
| `Error.InvalidInput.ForField(InputPointer field, reasonCode, detail?)` | Single field failure where you already have a pointer (nested / array / `InputPointer.Root`). |
| `Error.InvalidInput.ForRule(reasonCode, detail?)` | Single object-level invariant with no field pointer. |
| `new Error.InvalidInput(EquatableArray<FieldViolation> Fields, EquatableArray<RuleViolation> Rules = default)` | Aggregate multiple per-field and/or cross-field violations. |

```csharp
using System.Collections.Immutable;
using Trellis;

var single = Error.InvalidInput.ForField("email", "required", "Email is required");

var multiField = new Error.InvalidInput(EquatableArray.Create(
    new FieldViolation(InputPointer.ForProperty("email"),    "required") { Detail = "Email is required" },
    new FieldViolation(InputPointer.ForProperty("password"), "min_length",
        ImmutableDictionary<string, string>.Empty.Add("min", "8")) { Detail = "Password must be at least 8 characters" },
    new FieldViolation(InputPointer.ForProperty("age"),      "min",
        ImmutableDictionary<string, string>.Empty.Add("min", "18")) { Detail = "Must be 18 or older" }));

var crossField = new Error.InvalidInput(
    Fields: EquatableArray<FieldViolation>.Empty,
    Rules:  EquatableArray.Create(new RuleViolation(
        "passwords_must_match",
        Fields: EquatableArray.Create(
            InputPointer.ForProperty("password"),
            InputPointer.ForProperty("passwordConfirmation")))
        { Detail = "Passwords must match" }));
```

## Pattern matching

Use `Match` to fold a `Result<T>` into a value, or read `result.Error` (which is `Error?` and never throws) to perform side effects. The C# compiler verifies exhaustiveness against the closed catalog — adding a new case to `Error` lights up every `switch` that does not handle it.

```csharp
var message = LoadUser("42").Match(
    onSuccess: user  => $"Found {user.Email}",
    onFailure: error => error switch
    {
        Error.InvalidInput uc => $"Bad input: {uc.GetDisplayMessage()}",
        Error.NotFound nf             => $"Missing {nf.Resource.Type} {nf.Resource.Id}",
        Error.Forbidden f             => $"Not allowed by {f.PolicyId}",
        _                             => $"Fallback: {error.Kind}",
    });
```

`Match` has async overloads for `Task<Result<T>>` and `ValueTask<Result<T>>` (`MatchAsync`) and tuple overloads for arities 2–9. Full signatures: [`trellis-api-core.md` → Match family](../api_reference/trellis-api-core.md#match-family--matchextensions-matchextensionsasync-matchtupleextensions-matchtupleextensionsasync).

### `Kind` vs `Code`

- `Kind` is the **stable, low-cardinality domain slug** used for telemetry (e.g. `"invalid-input"`, `"invariant-violation"`, `"not-found"`). It is unaffected by payload values.
- `Code` defaults to the sentinel `ValidationCodes.Unspecified` (`"error.unspecified"`) — never to `Kind`. It is stored once on the base with an `init` accessor, so any case can name a reason the same way: `new Error.NotFound(resource) { Code = "order.archived" }`. Cases whose reason is required — `Conflict`, `Forbidden` (where `PolicyId` reads the same value), `InvariantViolation`, and `Unexpected` (which surfaces `FaultId` separately as a problem-details extension when set) — take it as a positional constructor parameter, so the compiler refuses one that says nothing. `Code` is what every boundary publishes, so a kind never reaches a consumer disguised as a reason.

At the ASP boundary, the wire `kind` can differ from the domain `Kind` for backward compatibility — most notably `Error.InvalidInput` / `Error.InvariantViolation` map to on-wire `kind = "unprocessable-content"`.

## Propagating errors

| Operator | Effect |
|---|---|
| `TapOnFailure(action)` | Side effect (log/metric) on failure; result passes through unchanged. |
| `MapOnFailure(error => newError)` | Translate one failure case into another (e.g. when crossing layers). |
| `RecoverOnFailure(error => Result<T>)` | Replace a failure with a fallback `Result` (success or different failure). |

```csharp
var result = LoadFromCrm(id)
    .TapOnFailure(error => logger.LogWarning("CRM call failed: {Kind} {Code}", error.Kind, error.Code))
    .MapOnFailure(error => error switch
    {
        Error.Unexpected => new Error.Unavailable(new RetryAdvice(After: TimeSpan.FromSeconds(60)))
            { Code = "crm_offline", Detail = "Customer service is temporarily unavailable" },
        _ => error,
    });

static Result<string> GetFromCache()    => Result.Fail<string>(new Error.NotFound(ResourceRef.For<string>("user:42")));
static Result<string> GetFromDatabase() => Result.Ok("Ada Lovelace");

var withFallback = GetFromCache().RecoverOnFailure(_ => GetFromDatabase());
```

All three operators have `Async` variants on `Task<Result<T>>` and `ValueTask<Result<T>>`. See [`trellis-api-core.md` → Tap, Map, Recover families](../api_reference/trellis-api-core.md#result-pipeline-extension-families).

## Capturing exceptions

Expected failures should be regular `Error` values. For code that genuinely throws, `Result.Try` and `Result.TryAsync` bridge the gap. Without a custom map, the default mapping wraps the exception as `new Error.Unexpected("unhandled-exception", faultId)` with a generated `FaultId`.

```csharp
using System.IO;
using Trellis;

static Result<string> LoadText(string path) =>
    Result.Try(() => File.ReadAllText(path));

static Task<Result<string>> LoadTextAsync(string path) =>
    Result.TryAsync(() => File.ReadAllTextAsync(path));

static Result<string> LoadConfig(string path) =>
    Result.Try(
        () => File.ReadAllText(path),
        exception => exception switch
        {
            FileNotFoundException       => new Error.NotFound(ResourceRef.For<FileInfo>(path)) { Detail = $"{path} was not found" },
            UnauthorizedAccessException => new Error.Forbidden("file.read") { Detail = "Access denied" },
            _                           => new Error.Unexpected("unhandled-exception", Guid.NewGuid().ToString("N")) { Detail = "An unexpected error occurred while loading the file." },
        });
```

`Result.Try` and `Result.TryAsync` also have parameterless-work overloads that return `Result<Unit>` for void/`Task` work — useful for command handlers, which return `Result<Unit>`.

## Composition

`Combine` merges multiple `Result<T>` failures into one. Two distinct shapes:

| Input failures | Result |
|---|---|
| All `Error.InvalidInput` | One merged `Error.InvalidInput` with concatenated `Fields` and `Rules`. |
| Heterogeneous (mixed cases) | One `Error.Aggregate` wrapping the children. Nested aggregates are flattened at construction. |

```csharp
var emailErr    = Result.Fail(Error.InvalidInput.ForField("email",    "required"));
var passwordErr = Result.Fail(Error.InvalidInput.ForField("password", "required"));
var ageErr      = Result.Fail(Error.InvalidInput.ForField("age",      "required"));

var combined = Result.Combine(emailErr, passwordErr, ageErr);
// combined.Error is one Error.InvalidInput with three Fields entries.
```

`Error.Combine(left, right)` (extension on `Error?`) does the same merging at the error level; passing a `null` left returns `right`.

```csharp
Error? acc = null;
acc = acc.Combine(new Error.NotFound(ResourceRef.For<Order>("1")));
acc = acc.Combine(new Error.Forbidden("orders.write"));
// acc is Error.Aggregate { Errors = [NotFound, Forbidden] }
```

`Error.Aggregate` exposes three constructor overloads (`EquatableArray<Error>`, `IEnumerable<Error>`, `params Error[]`); all three throw `ArgumentException` if no errors are supplied, and all flatten nested aggregates.

## Boundary mapping

Map an `Error` to an HTTP / Problem Details response with `result.ToHttpResponse(...)` from `Trellis.Asp`. The boundary decides HTTP status, companion headers, and the problem-details `type` / `kind`. That wire `kind` can differ from the domain `Kind` for backward compatibility — `Error.InvalidInput.Kind` is `invalid-input`, but the on-wire `kind` is `unprocessable-content`. For `Result<Unit>` the success branch emits `204 No Content`.

```csharp
using Trellis;
using Trellis.Asp;

public static IResult Get(string id, IUserService users) =>
    users.GetUser(id).ToHttpResponse();
```

Full mapping rules and per-case behaviour live in:

- API reference: [`trellis-api-asp.md` → `HttpResponseExtensions`](../api_reference/trellis-api-asp.md#httpresponseextensions)
- Article: [`asp-tohttpresponse.md` → Per-call error mapping](asp-tohttpresponse.md#per-call-error-mapping)

## Practical guidance

- **Pick the case the caller can act on.** `InvalidInput` when the request data can be fixed; `Conflict` when state or a business rule blocks an otherwise-valid request; `InvariantViolation` when the rule is real but not bound to a request field; `NotFound` when the resource does not exist; `Gone` for soft-deleted resources.
- **Use `Unexpected(code, faultId?)` for true surprises.** Set `FaultId` when you need telemetry correlation; use `Code == FaultCodes.NotImplemented` only when you intentionally want HTTP 501 at the boundary.
- **Set `Detail` only when it adds information.** Boundary renderers compute a usable default from `Kind`/`Code` and the typed payload; override `Detail` only when you have something more specific to say.
- **Prefer `Match` (or a property pattern on `result.Error`) at boundaries.** Inside pipelines, prefer `Bind`/`Map`/`Ensure` over inspecting `Error` directly.
- **Use `TapOnFailure` for logging and metrics.** It does not mutate the result.
- **Use `MapOnFailure` when crossing layers.** Translating a low-level `Unexpected` from a dependency into a domain-meaningful `Unavailable` is a typical use.
- **Use `Combine` to preserve every failure.** This is the difference between "first error wins" and "tell the caller everything that's wrong."
- **Equality.** Two errors compare equal when `Kind`, payload, and `Detail` match. `Cause` is excluded from equality so the same surface failure raised from different code paths still compares equal.

### InvalidInput vs InvariantViolation

`InvalidInput` carries field/rule violations bound to inbound request fields (per-field validation). `InvariantViolation` carries `(Code, ResourceRef?)` for domain rules that aren't tied to a request field — for example a cross-aggregate invariant or an internal precondition. Both map to HTTP 422 at the ASP boundary, but the wire `code` differs so consumers can tell domain-rule failures apart from field-validation failures.

## Migration from v2.x

| Removed | Use instead |
|---|---|
| `Error.BadRequest` | `Error.InvalidInput.ForRule(...)` (or `.ForField(...)` if anchored to a request field) |
| `Error.UnprocessableContent` | `Error.InvalidInput` |
| `Error.Unauthorized` | `Error.AuthenticationRequired` |
| `Error.TooManyRequests` | `Error.RateLimited` |
| `Error.ServiceUnavailable` | `Error.Unavailable` |
| `Error.InternalServerError` | `Error.Unexpected` |
| `Error.NotImplemented` | `Error.Unexpected` with `Code == FaultCodes.NotImplemented` |
| `Error.MethodNotAllowed` / `NotAcceptable` / `UnsupportedMediaType` / `RangeNotSatisfiable` / `ContentTooLarge` / `PreconditionFailed` / `PreconditionRequired` | `Error.TransportFault(new HttpError.X(...))` |

For the full upgrade narrative, including telemetry-slug changes and `AuthChallenge` removal, see the project CHANGELOG.

## Cross-references

- API surface (full per-case table, supporting types, `Result.Try`/`TryAsync`, Combine family, Match/Tap/Map/Recover families): [`trellis-api-core.md`](../api_reference/trellis-api-core.md)
- HTTP response mapping for `Result<T>` and standalone `Error`: [`trellis-api-asp.md`](../api_reference/trellis-api-asp.md), [`asp-tohttpresponse.md`](asp-tohttpresponse.md)
- FluentValidation → `Error.InvalidInput` adapter: [`integration-fluentvalidation.md`](integration-fluentvalidation.md)
- HTTP client failure mapping (turning upstream statuses into typed `Error` cases): [`integration-http.md`](integration-http.md)
- Mediator pipeline behaviours that operate on `Result`/`Error`: [`integration-mediator.md`](integration-mediator.md)
