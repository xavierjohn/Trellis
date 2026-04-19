# Error Handling

Errors are where Trellis becomes practical. They let you keep business rules, validation, and HTTP concerns explicit **without** falling back to exceptions for normal control flow.

> [!TIP]
> In Trellis, an error is data. That means you can log it, transform it, combine it, test it, and map it to HTTP responses predictably.

## Start Here: the Everyday Flow

Most applications need the same shape:

1. Do work in a `Result<T>` pipeline
2. Return a specific `Error` when something goes wrong
3. Convert that error at the boundary

```csharp
using Trellis;

public record User(string Id, string Email);

static Result<User> GetUser(string id) =>
    id == "42"
        ? Result.Ok(new User("42", "ada@example.com"))
        : Result.Fail<User>(Error.NotFound($"User {id} not found", id));

var response = GetUser("42").MatchError(
    onSuccess: user => $"200 OK: {user.Email}",
    onNotFound: error => $"404 Not Found: {error.Detail}",
    onError: error => $"500 Internal Server Error: {error.Code}"
);
```

That is the core mental model for this article.

## Why Trellis Uses Explicit Errors

The problem with raw exceptions is that they mix two very different things:

- **expected failures** like validation, not found, or conflict
- **unexpected failures** like I/O faults or bugs

Trellis separates those concerns:

- use `Error` values for **expected** failures
- use `Result.Try(...)` / `Result.TryAsync(...)` to convert **unexpected** exceptions into failures when needed

> [!NOTE]
> Default error codes end in `.error`. Examples: `validation.error`, `not.found.error`, `domain.error`.

## Built-in Error Types

Start with the most specific error you can. That makes your pipelines easier to read and your API mapping more precise.

| Error type | Factory method | Default code | Typical HTTP status | Use when... |
| --- | --- | --- | --- | --- |
| `ValidationError` | `Error.Validation(...)` | `validation.error` | 400 | Input is invalid |
| `BadRequestError` | `Error.BadRequest(...)` | `bad.request.error` | 400 | The request itself is malformed |
| `UnauthorizedError` | `Error.Unauthorized(...)` | `unauthorized.error` | 401 | Authentication is missing or invalid |
| `ForbiddenError` | `Error.Forbidden(...)` | `forbidden.error` | 403 | The caller is authenticated but not allowed |
| `NotFoundError` | `Error.NotFound(...)` | `not.found.error` | 404 | A resource does not exist |
| `ConflictError` | `Error.Conflict(...)` | `conflict.error` | 409 | Current state prevents the operation |
| `GoneError` | `Error.Gone(...)` | `gone.error` | 410 | The resource existed before and is now intentionally removed |
| `PreconditionFailedError` | `Error.PreconditionFailed(...)` | `precondition.failed.error` | 412 | An `If-Match` or similar condition failed |
| `ContentTooLargeError` | `Error.ContentTooLarge(...)` | `content.too.large.error` | 413 | The request body is too large |
| `UnsupportedMediaTypeError` | `Error.UnsupportedMediaType(...)` | `unsupported.media.type.error` | 415 | The content type is not supported |
| `RangeNotSatisfiableError` | `Error.RangeNotSatisfiable(...)` | `range.not.satisfiable.error` | 416 | The requested range cannot be served |
| `DomainError` | `Error.Domain(...)` | `domain.error` | commonly 422 | A business rule was violated |
| `MethodNotAllowedError` | `Error.MethodNotAllowed(...)` | `method.not.allowed.error` | 405 | The HTTP method is not supported |
| `NotAcceptableError` | `Error.NotAcceptable(...)` | `not.acceptable.error` | 406 | No acceptable representation can be produced |
| `RateLimitError` | `Error.RateLimit(...)` | `rate.limit.error` | 429 | A quota or rate limit was exceeded |
| `ServiceUnavailableError` | `Error.ServiceUnavailable(...)` | `service.unavailable.error` | 503 | A dependency or service is temporarily unavailable |
| `UnexpectedError` | `Error.Unexpected(...)` | `unexpected.error` | 500 | Something unexpected happened |
| `AggregateError` | constructor / `Combine(...)` | `aggregate.error` | varies | Multiple errors were collected |

> [!TIP]
> `DomainError` is usually a better fit than `ValidationError` when the input is structurally valid but violates a business rule.

## The Error Shape

Every Trellis error has the same three core pieces:

```csharp
using Trellis;

var error = Error.NotFound("User 42 not found", "42");

Console.WriteLine(error.Code);     // not.found.error
Console.WriteLine(error.Detail);   // User 42 not found
Console.WriteLine(error.Instance); // 42
```

- `Code` is for machines
- `Detail` is for humans
- `Instance` is optional context, usually a resource identifier

## Creating the Common Errors

### Validation: input is wrong

Use this when the caller can fix the request.

```csharp
using Trellis;

var error = Error.Validation("Email is required", "email");
```

For multi-field validation, `ValidationError` has a fluent builder:

```csharp
using Trellis;

var error = ValidationError.For("email", "Email is required")
    .And("password", "Password must be at least 8 characters")
    .And("password", "Password must contain a number")
    .And("age", "Must be 18 or older");
```

### Not found: the thing is missing

```csharp
using Trellis;

var error = Error.NotFound("Order 123 was not found", "123");
```

### Conflict: the state says no

```csharp
using Trellis;

var error = Error.Conflict("Email address is already in use");
```

### Domain: the request is valid, but the rule is broken

```csharp
using Trellis;

var error = Error.Domain("Cannot cancel an order after shipment has started");
```

### Authorization: unauthenticated vs unauthorized

```csharp
using Trellis;

var unauthorized = Error.Unauthorized("Authentication token is missing");
var forbidden = Error.Forbidden("Administrator role is required");
```

### Operational HTTP errors

These are useful when your boundary needs more precise HTTP semantics.

```csharp
using Trellis;

var gone = Error.Gone("Document 42 was permanently removed", "42");
var unsupportedMediaType = Error.UnsupportedMediaType("Only application/json is supported");
var tooLarge = Error.ContentTooLarge("Upload exceeds the 10 MB limit");
var notAcceptable = Error.NotAcceptable("The requested response format is not available");
var methodNotAllowed = Error.MethodNotAllowed(
    "PATCH is not supported for this endpoint",
    ["GET", "POST"]);
var rangeNotSatisfiable = Error.RangeNotSatisfiable(
    "Requested range exceeds file length",
    completeLength: 4096);
```

### Rate limit and service availability

```csharp
using Trellis;

var rateLimit = Error.RateLimit("Too many login attempts. Try again later.");
var unavailable = Error.ServiceUnavailable("Payment gateway is temporarily unavailable.");
```

### Unexpected failures

Use this when something truly unexpected escapes normal domain flow.

```csharp
using Trellis;

var error = Error.Unexpected("Database connection failed");
```

## Matching Errors at the Boundary

The problem at the edge of your system is simple: you need one place where Trellis errors become HTTP responses, UI messages, or log entries.

### `MatchError`: return a value

```csharp
using Trellis;

public record User(string Id, string Email);

static Result<User> LoadUser(string id) =>
    id == "42"
        ? Result.Ok(new User(id, "ada@example.com"))
        : Result.Fail<User>(Error.NotFound($"User {id} not found", id));

var message = LoadUser("42").MatchError(
    onSuccess: user => $"Found {user.Email}",
    onValidation: error => $"Bad input: {error.Detail}",
    onNotFound: error => $"Missing: {error.Detail}",
    onError: error => $"Fallback: {error.Code}"
);
```

### `SwitchError`: perform side effects only

```csharp
using Trellis;

Result<string> result = Result.Fail<string>(Error.Conflict("Email address is already in use"));

result.SwitchError(
    onConflict: error => Console.WriteLine($"Conflict: {error.Detail}"),
    onSuccess: value => Console.WriteLine($"Saved {value}"),
    onError: error => Console.WriteLine($"Unexpected: {error.Code}")
);
```

> [!NOTE]
> `MatchError` and `SwitchError` have dedicated handlers for the most common error families: validation, not found, conflict, bad request, unauthorized, forbidden, domain, rate limit, service unavailable, unexpected, and aggregate. Other error types fall through to `onError`.

## Side Effects Without Breaking the Pipeline

Sometimes you want to log, increment metrics, or notify another system **without** changing the result.

### `TapOnFailure`

```csharp
using Trellis;

var result = Result.Fail<string>(Error.ServiceUnavailable("Search is temporarily offline"))
    .TapOnFailure(error => Console.WriteLine($"Log: {error.Code} - {error.Detail}"));
```

### `TapOnFailureAsync`

```csharp
using Trellis;

var result = await Result.Fail<string>(Error.RateLimit("Too many requests"))
    .TapOnFailureAsync(error =>
    {
        Console.WriteLine($"Audit: {error.Code}");
        return Task.CompletedTask;
    });
```

## Transforming Errors

As results move across layers, you may want to translate infrastructure failures into domain-friendly ones.

### `MapOnFailure`

```csharp
using Trellis;

var result = Result.Fail<string>(Error.Unexpected("Timeout while calling CRM"))
    .MapOnFailure(error => error switch
    {
        UnexpectedError => Error.ServiceUnavailable("Customer service is temporarily unavailable"),
        _ => error
    });
```

### `RecoverOnFailure`

Use recovery when a fallback path is legitimate, not when you are hiding a bug.

```csharp
using Trellis;

static Result<string> GetFromCache() =>
    Result.Fail<string>(Error.NotFound("Cache miss"));

static Result<string> GetFromDatabase() =>
    Result.Ok("Ada Lovelace");

var result = GetFromCache()
    .RecoverOnFailure(error => GetFromDatabase());
```

## Combining and Aggregating Errors

Real workflows often validate several things at once. The question is: how do you keep **all** the useful failures?

### Validation errors merge automatically

```csharp
using Trellis;

var email = Error.Validation("Email is required", "email");
var password = Error.Validation("Password is required", "password");
var age = Error.Validation("Must be 18 or older", "age");

var combined = email.Combine(password).Combine(age);
```

The result is a **single** `ValidationError` with all field messages.

### Mixed error types produce `AggregateError`

```csharp
using Trellis;

var validation = Error.Validation("Email is invalid", "email");
var conflict = Error.Conflict("Email address is already in use");

var combined = validation.Combine(conflict);
```

The result is an `AggregateError` because the failures are different kinds of problems.

### Extract only the validation pieces

```csharp
using Trellis;

var combinedError = Error.Validation("Email is invalid", "email")
    .Combine(Error.Conflict("Email address is already in use"));

var combinedResult = Result.Fail<string>(combinedError);

var validationOnly = combinedResult.FlattenValidationErrors();
```

`FlattenValidationErrors()` is useful when you want field-level feedback even after aggregation.

## Async Error Handling

When your pipeline is async, the goal is the same: keep the failure explicit and deal with it once.

### `MatchErrorAsync`

```csharp
using Trellis;

public record User(string Id, string Email);

static Task<Result<User>> GetUserAsync(string id) =>
    Task.FromResult(id == "42"
        ? Result.Ok(new User(id, "ada@example.com"))
        : Result.Fail<User>(Error.NotFound($"User {id} not found", id)));

var message = await GetUserAsync("42").MatchErrorAsync(
    onSuccess: user => $"Loaded {user.Email}",
    onNotFound: error => $"Missing: {error.Detail}",
    onError: error => $"Fallback: {error.Code}");
```

### `SwitchErrorAsync`

```csharp
using Trellis;

await Task.FromResult(Result.Fail<string>(Error.Validation("Email is required", "email")))
    .SwitchErrorAsync(
        onValidation: (error, ct) =>
        {
            Console.WriteLine(error.Detail);
            return Task.CompletedTask;
        },
        onSuccess: (value, ct) => Task.CompletedTask);
```

## Exception Capture with `Try` and `TryAsync`

Expected failures should be regular `Error` values. But for code that can throw, Trellis gives you a bridge.

```csharp
using Trellis;

static Result<string> LoadText(string path) =>
    Result.Try(() => File.ReadAllText(path));

static Task<Result<string>> LoadTextAsync(string path) =>
    Result.TryAsync(() => File.ReadAllTextAsync(path));
```

With custom mapping:

```csharp
using Trellis;

var result = Result.Try(
    () => File.ReadAllText("settings.json"),
    exception => exception switch
    {
        FileNotFoundException => Error.NotFound("settings.json was not found"),
        UnauthorizedAccessException => Error.Forbidden("Access denied"),
        _ => Error.Unexpected(exception.Message)
    });
```

## Equality: the Surprising Rule

This is the accuracy detail most people miss.

> [!WARNING]
> `Error.Equals` compares **only `Code`**. It does **not** compare type, detail, or instance.

```csharp
using Trellis;

var first = Error.NotFound("User 42 not found", "42");
var second = Error.NotFound("Order 99 not found", "99");

Console.WriteLine(first.Equals(second)); // True
```

That behavior is intentional: Trellis treats errors with the same code as equivalent for programmatic handling.

## Practical Rules of Thumb

- Use **`ValidationError`** when the caller can fix the request data
- Use **`DomainError`** when the input is valid but the rule is not
- Use **`ConflictError`** when the current state blocks the operation
- Use **`NotFoundError`** when the resource does not exist
- Use **`UnexpectedError`** for true surprises, not business logic
- Use **`MatchError`** or **`MatchErrorAsync`** at system boundaries
- Use **`TapOnFailure`** for logging and metrics
- Use **`MapOnFailure`** when crossing layers
- Let **`Combine(...)`** preserve multiple failures instead of throwing the first one away

## Next Steps

- Read [Advanced Features](advanced-features.md) for `Try`, `ParallelAsync`, tuple destructuring, and LINQ flows
- Read [Why Maybe?](maybe-type.md) for optional values that compose with `Result<T>`
