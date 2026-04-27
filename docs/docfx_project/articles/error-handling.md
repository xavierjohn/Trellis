# Error Handling

Errors are where Trellis becomes practical. They let you keep business rules, validation, and HTTP concerns explicit **without** falling back to exceptions for normal control flow.

> [!TIP]
> In Trellis V2, an error is a **closed discriminated union of typed records**. You can log it, transform it, combine it, test it, and `switch` over it with compile-time exhaustiveness checking.

## Start Here: the Everyday Flow

Most applications need the same shape:

1. Do work in a `Result<T>` pipeline.
2. Return a specific `Error` case when something goes wrong.
3. `switch` on the case at the boundary to convert it.

```csharp
using Trellis;

public record User(string Id, string Email);

static Result<User> GetUser(string id) =>
    id == "42"
        ? Result.Ok(new User("42", "ada@example.com"))
        : Result.Fail<User>(new Error.NotFound(new ResourceRef("User", id)) { Detail = $"User {id} not found" });

var response = GetUser("42").Match(
    onSuccess: user => $"200 OK: {user.Email}",
    onFailure: error => error switch
    {
        Error.NotFound nf      => $"404 Not Found: {nf.Detail}",
        Error.Forbidden f      => $"403 Forbidden: {f.PolicyId}",
        _                      => $"500 Internal: {error.Kind}"
    });
```

That is the core mental model.

## Why Trellis Uses Explicit Errors

The problem with raw exceptions is that they mix two very different things:

- **expected failures** — validation, not found, conflict, forbidden
- **unexpected failures** — I/O faults, programmer bugs

Trellis separates them:

- expected failures are `Error.X` cases inside `Result<T>` failures
- unexpected exceptions become `Error.InternalServerError(faultId)` via `Result.Try` / `Result.TryAsync`

## The Error ADT

`Error` is an `abstract record` with **18 nested `sealed record` cases**. The catalog is closed (the base type has a `private` constructor) so every `switch` is exhaustive at compile time.

| Case | Payload | HTTP | Use when... |
| --- | --- | --- | --- |
| `Error.BadRequest` | `(string ReasonCode, InputPointer? At = null)` | 400 | Request shape itself is malformed |
| `Error.Unauthorized` | `(EquatableArray<AuthChallenge> Challenges = default)` | 401 | Authentication missing or invalid |
| `Error.Forbidden` | `(string PolicyId, ResourceRef? Resource = null)` | 403 | Authenticated but not allowed |
| `Error.NotFound` | `(ResourceRef Resource)` | 404 | A resource does not exist |
| `Error.MethodNotAllowed` | `(EquatableArray<string> Allow)` | 405 | HTTP method not supported here |
| `Error.NotAcceptable` | `(EquatableArray<string> Available)` | 406 | No acceptable representation |
| `Error.Conflict` | `(ResourceRef? Resource, string ReasonCode)` | 409 | State or business rule conflict |
| `Error.Gone` | `(ResourceRef Resource)` | 410 | Resource intentionally removed |
| `Error.PreconditionFailed` | `(ResourceRef, PreconditionKind)` | 412 | `If-Match`/etc. failed |
| `Error.ContentTooLarge` | `(long? MaxBytes = null)` | 413 | Request body too large |
| `Error.UnsupportedMediaType` | `(EquatableArray<string> Supported)` | 415 | Content-Type not supported |
| `Error.RangeNotSatisfiable` | `(long CompleteLength, string Unit = "bytes")` | 416 | Requested range invalid |
| `Error.UnprocessableContent` | `(EquatableArray<FieldViolation> Fields, EquatableArray<RuleViolation> Rules = default)` | 422 | Domain-validation failures (replaces the pre-V2 `ValidationError` class) |
| `Error.PreconditionRequired` | `(PreconditionKind Condition)` | 428 | Required precondition header missing |
| `Error.TooManyRequests` | `(RetryAfter? RetryAfter = null)` | 429 | Rate-limited |
| `Error.InternalServerError` | `(string FaultId)` | 500 | Unhandled failure; rich diagnostics live in your log indexed by `FaultId` |
| `Error.NotImplemented` | `(string Feature)` | 501 | Feature not implemented |
| `Error.ServiceUnavailable` | `(RetryAfter? RetryAfter = null)` | 503 | Dependency unavailable |
| `Error.Aggregate` | `(EquatableArray<Error> Errors)` | varies | Multiple failures collected; auto-flattens nested aggregates |

> [!TIP]
> `Error.Conflict(resource, "domain.violation")` is usually a better fit than `Error.UnprocessableContent` when the input is structurally valid but violates a business rule.

## Construction syntax

There are **no static factory methods**. Every call site names the case it produces:

```csharp
new Error.NotFound(new ResourceRef("Order", "42")) { Detail = "Order 42 not found" }
```

The free-form `Detail` is an init-only property on the base type — set it via object initializer when you want to override the renderer's default human-readable text.

### Common patterns

**Single-field validation**

```csharp
var error = new Error.UnprocessableContent(EquatableArray.Create(
    new FieldViolation(InputPointer.ForProperty("email"), "required") { Detail = "Email is required" }));
```

**Multi-field validation**

```csharp
var error = new Error.UnprocessableContent(EquatableArray.Create(
    new FieldViolation(InputPointer.ForProperty("email"),    "required") { Detail = "Email is required" },
    new FieldViolation(InputPointer.ForProperty("password"), "min_length", ImmutableDictionary<string, string>.Empty.Add("min", "8")) { Detail = "Password must be at least 8 characters" },
    new FieldViolation(InputPointer.ForProperty("age"),      "min", ImmutableDictionary<string, string>.Empty.Add("min", "18")) { Detail = "Must be 18 or older" }));
```

**Cross-field business rule**

```csharp
var error = new Error.UnprocessableContent(
    Fields: EquatableArray<FieldViolation>.Empty,
    Rules:  EquatableArray.Create(new RuleViolation(
        "passwords_must_match",
        Fields: EquatableArray.Create(InputPointer.ForProperty("password"), InputPointer.ForProperty("passwordConfirmation")))
        { Detail = "Passwords must match" }));
```

**Not found**

```csharp
var error = new Error.NotFound(new ResourceRef("Order", "123")) { Detail = "Order 123 was not found" };
```

**Conflict (state vs domain)**

```csharp
var stateConflict  = new Error.Conflict(new ResourceRef("User", "ada"), "duplicate.key")     { Detail = "Email address is already in use" };
var domainConflict = new Error.Conflict(null,                            "cancel_after_ship")  { Detail = "Cannot cancel an order after shipment has started" };
```

**Authorization (authenticated vs not)**

```csharp
var unauth     = new Error.Unauthorized()              { Detail = "Authentication token is missing" };
var forbidden  = new Error.Forbidden("orders.write")   { Detail = "Administrator role is required" };
```

**Operational HTTP cases**

```csharp
var gone        = new Error.Gone(new ResourceRef("Document", "42"))                          { Detail = "Document 42 was permanently removed" };
var media       = new Error.UnsupportedMediaType(EquatableArray.Create("application/json"))  { Detail = "Only application/json is supported" };
var tooLarge    = new Error.ContentTooLarge(MaxBytes: 10 * 1024 * 1024)                      { Detail = "Upload exceeds the 10 MB limit" };
var notAccept   = new Error.NotAcceptable(EquatableArray.Create("application/json"))         { Detail = "The requested response format is not available" };
var notAllowed  = new Error.MethodNotAllowed(EquatableArray.Create("GET", "POST"))           { Detail = "PATCH is not supported for this endpoint" };
var rangeBad    = new Error.RangeNotSatisfiable(CompleteLength: 4096)                        { Detail = "Requested range exceeds file length" };
```

**Rate limit and availability**

```csharp
var rate = new Error.TooManyRequests(new RetryAfter(Delta: TimeSpan.FromSeconds(30)));
var down = new Error.ServiceUnavailable(new RetryAfter(Delta: TimeSpan.FromMinutes(2))) { Detail = "Payment gateway is temporarily unavailable" };
```

**Unexpected**

```csharp
var error = new Error.InternalServerError(faultId: "f-7c2") { Detail = "Database connection failed" };
```

## Matching at the Boundary

Use `Match` to fold a `Result<T>` into a value, or check `result.Error` directly to perform side effects.

### `Match` with a `switch` expression

```csharp
var message = LoadUser("42").Match(
    onSuccess: user  => $"Found {user.Email}",
    onFailure: error => error switch
    {
        Error.UnprocessableContent uc => $"Bad input: {uc.GetDisplayMessage()}",
        Error.NotFound nf             => $"Missing {nf.Resource.Type} {nf.Resource.Id}",
        Error.Forbidden f             => $"Not allowed by {f.PolicyId}",
        _                              => $"Fallback: {error.Kind}"
    });
```

The C# compiler verifies exhaustiveness against the closed catalog. Add a new case to `Error` and every `switch` that doesn't handle it lights up.

> [!NOTE]
> The pre-V2 `MatchError` / `SwitchError` extensions and `FlattenValidationErrors` were removed. Use `switch` patterns and `Combine` instead.

## Side Effects Without Breaking the Pipeline

```csharp
var result = Result.Fail<string>(new Error.ServiceUnavailable() { Detail = "Search is temporarily offline" })
    .TapOnFailure(error => Console.WriteLine($"Log: {error.Kind} - {error.Detail}"));
```

`TapOnFailureAsync` works the same way for `Task<Result<T>>` / `ValueTask<Result<T>>`.

## Transforming Errors

```csharp
var result = Result.Fail<string>(new Error.InternalServerError("f-1") { Detail = "Timeout while calling CRM" })
    .MapOnFailure(error => error switch
    {
        Error.InternalServerError => new Error.ServiceUnavailable() { Detail = "Customer service is temporarily unavailable" },
        _                          => error
    });
```

`RecoverOnFailure` lets you fall back to an alternate path when failure is legitimate (not when you are hiding a bug):

```csharp
static Result<string> GetFromCache()    => Result.Fail<string>(new Error.NotFound(new ResourceRef("CacheEntry", "user:42")));
static Result<string> GetFromDatabase() => Result.Ok("Ada Lovelace");

var result = GetFromCache().RecoverOnFailure(_ => GetFromDatabase());
```

## Combining Errors

`Combine` merges multiple `Result<T>` failures into one:

- All failures are `Error.UnprocessableContent` → merged into a single `UnprocessableContent` with concatenated `Fields` and `Rules`.
- Heterogeneous failures → wrapped in an `Error.Aggregate` (which auto-flattens nested aggregates at construction).

```csharp
var emailErr    = Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("email"),    "required"))));
var passwordErr = Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("password"), "required"))));
var ageErr      = Result.Fail(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty("age"),      "required"))));

var combined = Result.Combine(emailErr, passwordErr, ageErr);
// combined.Error is one Error.UnprocessableContent with three Fields entries.
```

## Async Error Handling

`Match`, `TapOnFailure`, `MapOnFailure`, and `Combine` all have async overloads on `Task<Result<T>>` and `ValueTask<Result<T>>`. The patterns are identical; just `await` the chain.

## Exception Capture with `Try` and `TryAsync`

Expected failures should be regular `Error` values. For code that genuinely throws, Trellis bridges the gap:

```csharp
static Result<string> LoadText(string path) => Result.Try(() => File.ReadAllText(path));

static Task<Result<string>> LoadTextAsync(string path) => Result.TryAsync(() => File.ReadAllTextAsync(path));
```

With custom mapping:

```csharp
var result = Result.Try(
    () => File.ReadAllText("settings.json"),
    exception => exception switch
    {
        FileNotFoundException        => new Error.NotFound(new ResourceRef("File", "settings.json")) { Detail = "settings.json was not found" },
        UnauthorizedAccessException  => new Error.Forbidden("file.read") { Detail = "Access denied" },
        _                             => new Error.InternalServerError(Guid.NewGuid().ToString("N")) { Detail = exception.Message }
    });
```

## Equality

Errors are records — two errors compare equal when their **`Kind` and payload are equal**. The `Cause` chain is excluded from equality (so two semantically identical errors raised from different code paths compare equal).

```csharp
var a = new Error.NotFound(new ResourceRef("User", "42")) { Detail = "x" };
var b = new Error.NotFound(new ResourceRef("User", "42")) { Detail = "x" };
var c = new Error.NotFound(new ResourceRef("User", "99")) { Detail = "x" };

Console.WriteLine(a.Equals(b)); // True  — same case, same payload
Console.WriteLine(a.Equals(c)); // False — payload differs
```

`Detail` participates in equality because it's an init-only property on the base record. If you want to compare semantically regardless of human-readable text, compare the typed payload directly (e.g. `nf.Resource.Equals(other.Resource)`).

## `Kind` vs `Code`

- `Kind` is the **stable, low-cardinality slug** the OTel `error.type` attribute is set from (e.g. `"not-found"`, `"unprocessable-content"`, `"conflict"`).
- `Code` defaults to `Kind` and is overridden when the payload carries a per-instance `ReasonCode` (`Conflict`, `Forbidden`, `BadRequest`) or a `FaultId` (`InternalServerError`).

Boundary renderers (ProblemDetails / gRPC / GraphQL) read `Kind` for `type` and `Code` for the per-instance identifier.

## Practical Rules of Thumb

- `Error.UnprocessableContent` when the caller can fix the request data
- `Error.Conflict(_, "domain.violation")` when the input is valid but a business rule blocks it
- `Error.Conflict(_, "duplicate.key")` (or similar) when current state blocks the operation
- `Error.NotFound` when the resource does not exist
- `Error.PreconditionFailed` for optimistic-concurrency mismatches
- `Error.Gone` for soft-deleted resources
- `Error.InternalServerError(faultId)` only for true surprises — never for business logic
- `Match` (or a property pattern on `result.Error`) at system boundaries
- `TapOnFailure` for logging and metrics
- `MapOnFailure` when crossing layers
- `Combine` to preserve multiple failures instead of throwing the first one away

## Next Steps

- Read [Advanced Features](advanced-features.md) for `Try`, `ParallelAsync`, tuple destructuring, and LINQ flows
- Read [Why Maybe?](maybe-type.md) for optional values that compose with `Result<T>`
- Read [ADR-001](../adr/ADR-001-result-api-surface.md) for the Result/Error API design rationale
