# Trellis.Results — Railway Oriented Programming

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Results.svg)](https://www.nuget.org/packages/Trellis.Results)

Railway Oriented Programming (ROP) is a functional approach to error handling that treats your code like a railway track. Operations either succeed (staying on the success track) or fail (switching to the error track). This library provides the core types and extension methods to implement ROP in C#.

## Table of Contents

- [Installation](#installation)
- [Core Concepts](#core-concepts)
  - [Result Type](#result-type)
  - [Maybe Type](#maybe-type)
  - [Error Types](#error-types)
- [Quick Start](#quick-start)
- [Core Operations](#core-operations)
  - [Bind](#bind)
  - [Map](#map)
  - [Tap](#tap)
  - [Ensure](#ensure)
  - [EnsureAll](#ensureall)
  - [Recover](#recover)
  - [RecoverOnFailure](#recoveronfailure)
  - [Combine](#combine)
  - [Check](#check)
  - [BindZip](#bindzip)
  - [MapIf](#mapif)
  - [EnsureNotNull](#ensurenotnull)
  - [GetValueOrDefault](#getvalueordefault)
- [Advanced Features](#advanced-features)
  - [LINQ Query Syntax](#linq-query-syntax)
  - [Maybe LINQ Query Syntax](#maybe-linq-query-syntax)
  - [ToMaybe — Convert Result to Maybe](#tomaybe--convert-result-to-maybe)
  - [Pattern Matching](#pattern-matching)
  - [Exception Capture](#exception-capture)
  - [Parallel Operations](#parallel-operations)
  - [Error Transformation](#error-transformation)
  - [Collection Helpers](#collection-helpers)
- [Common Patterns](#common-patterns)
- [Debugging](#debugging)
- [Best Practices](#best-practices)
- [Related Packages](#related-packages)

## Installation

Install via NuGet:

```bash
dotnet add package Trellis.Results
```

## Core Concepts

### Result Type

The `Result<TValue>` type represents either a successful computation (with a value) or a failure (with an error).

```csharp
public readonly struct Result<TValue>
{
    public TValue Value { get; }        // Throws if IsFailure
    public Error Error { get; }         // Throws if IsSuccess
    
    public bool IsSuccess { get; }
    public bool IsFailure { get; }

    // Implicit conversions
    public static implicit operator Result<TValue>(TValue value);
    public static implicit operator Result<TValue>(Error error);
}
```

**Basic Usage:**

```csharp
using Trellis;

// Success result
Result<int> success = Result.Success(42);
Result<int> alsoSuccess = 42; // Implicit conversion

// Failure result
Result<int> failure = Result.Failure<int>(Error.NotFound("Item not found"));
Result<int> alsoFailure = Error.NotFound("Item not found"); // Implicit conversion

// Checking state
if (success.IsSuccess)
{
    var value = success.Value; // 42
}

if (failure.IsFailure)
{
    var error = failure.Error; // Error object
}
```

### Maybe Type

The `Maybe<T>` type represents an optional value that may or may not exist.

```csharp
public readonly struct Maybe<T> : IEquatable<T>, IEquatable<Maybe<T>>
    where T : notnull
{
    public T Value { get; }
    public bool HasValue { get; }
    public bool HasNoValue { get; }
}
```

**Basic Usage:**

```csharp
// Create Maybe with value
Maybe<string> some = Maybe.From("hello");
Maybe<string> alsoSome = "hello"; // Implicit conversion

// Create Maybe without value
Maybe<string> none = Maybe<string>.None;
Maybe<string> alsoNone = null; // For reference types

// Check and use
if (some.HasValue)
{
    Console.WriteLine(some.Value); // "hello"
}

// Get value or default
string result = none.GetValueOrDefault("default"); // "default"
```

**Transformations:**

```csharp
// Map — transform the inner value (returns Maybe<TResult>)
Maybe<string> name = Maybe.From("hello");
Maybe<int> length = name.Map(s => s.Length);        // Maybe.From(5)

Maybe<string> empty = Maybe<string>.None;
Maybe<int> noLength = empty.Map(s => s.Length);     // Maybe.None

// Match — pattern match to extract a value
string greeting = name.Match(
    s => $"Hi, {s}!",       // HasValue
    () => "No name");        // HasNoValue
// → "Hi, hello!"

string fallback = empty.Match(
    s => $"Hi, {s}!",
    () => "No name");
// → "No name"
```

**Chaining & Filtering:**

```csharp
// Bind — flatMap for chaining optional lookups
Maybe<Order> order = Maybe.From(userId)
    .Bind(id => users.FindById(id))
    .Bind(user => orders.FindLatest(user.Id));
// None at any step → entire chain is None

// Or — provide a fallback when None
Maybe<string> name = GetNickname(userId).Or("Anonymous");
// Lazy: .Or(() => LookupDefault(userId))
// Maybe: .Or(GetDisplayName(userId))

// Where — filter by predicate
Maybe<int> even = Maybe.From(42).Where(x => x % 2 == 0);  // Some(42)
Maybe<int> nope = Maybe.From(3).Where(x => x % 2 == 0);   // None

// Tap — side effect without changing the Maybe
Maybe<User> user = FindUser(id)
    .Tap(u => Log.Info($"Found user {u.Name}"));
```

### Error Types

The library provides several built-in error types, each with a specific purpose and default HTTP status code mapping:

| Error Type | Factory Method | Use When | HTTP Status | Code |
|------------|---------------|----------|-------------|------|
| `ValidationError` | `Error.Validation()` | Input data fails validation rules | 400 Bad Request | `validation.error` |
| `BadRequestError` | `Error.BadRequest()` | Request is malformed or syntactically invalid | 400 Bad Request | `bad.request.error` |
| `UnauthorizedError` | `Error.Unauthorized()` | User is not authenticated (not logged in) | 401 Unauthorized | `unauthorized.error` |
| `ForbiddenError` | `Error.Forbidden()` | User lacks permission (authenticated but forbidden) | 403 Forbidden | `forbidden.error` |
| `NotFoundError` | `Error.NotFound()` | Requested resource doesn't exist | 404 Not Found | `not.found.error` |
| `ConflictError` | `Error.Conflict()` | Operation conflicts with current state | 409 Conflict | `conflict.error` |
| `DomainError` | `Error.Domain()` | Business rule or domain logic violation | 422 Unprocessable Entity | `domain.error` |
| `RateLimitError` | `Error.RateLimit()` | Too many requests (quota exceeded) | 429 Too Many Requests | `rate.limit.error` |
| `UnexpectedError` | `Error.Unexpected()` | Unexpected system error or exception | 500 Internal Server Error | `unexpected.error` |
| `ServiceUnavailableError` | `Error.ServiceUnavailable()` | Service temporarily unavailable | 503 Service Unavailable | `service.unavailable.error` |
| `AggregateError` | (created via `Combine()`) | Multiple non-validation errors combined | Varies | `aggregate.error` |

**Common Usage Examples:**

```csharp
// ValidationError - field-level validation failures
var validation = Error.Validation("Email format is invalid", "email");
var multiField = Error.Validation("Password too short", "password")
    .And("email", "Email is required");

// BadRequestError - malformed requests
var badRequest = Error.BadRequest("Invalid JSON payload");

// NotFoundError - resource not found
var notFound = Error.NotFound($"User {userId} not found", userId);

// ConflictError - state conflicts
var conflict = Error.Conflict("Email address already in use");

// UnauthorizedError - authentication required
var unauthorized = Error.Unauthorized("Login required to access this resource");

// ForbiddenError - insufficient permissions
var forbidden = Error.Forbidden("Admin access required");

// DomainError - business rule violations
var domain = Error.Domain("Cannot withdraw more than account balance");

// RateLimitError - quota exceeded
var rateLimit = Error.RateLimit("API rate limit exceeded. Retry in 60 seconds");

// ServiceUnavailableError - temporary unavailability
var unavailable = Error.ServiceUnavailable("Service under maintenance");

// UnexpectedError - system errors
var unexpected = Error.Unexpected("Database connection failed");
```

**Choosing the Right Error Type:**

- Use **ValidationError** for field-level input validation (e.g., invalid email format, missing required fields)
- Use **BadRequestError** for syntactic/structural issues (e.g., malformed JSON, invalid query parameters)
- Use **DomainError** for business logic violations (e.g., insufficient funds, order quantity limits)
- Use **ConflictError** for state-based conflicts (e.g., duplicate email, concurrent modification)
- Use **UnexpectedError** for infrastructure/system failures (e.g., database errors, network timeouts)

**Error Combining:**

When multiple errors occur, they are intelligently combined:
- Multiple `ValidationError` instances → Merged into a single `ValidationError` with all field errors
- Mixing `ValidationError` with other error types → Creates an `AggregateError`
- Multiple non-validation errors → Creates an `AggregateError`

```csharp
// Validation errors are merged
var error1 = Error.Validation("Email required", "email");
var error2 = Error.Validation("Password required", "password");
var combined = error1.Combine(error2); // Single ValidationError with both fields

// Mixed error types create AggregateError
var validation = Error.Validation("Invalid input", "field");
var notFound = Error.NotFound("Resource not found");
var aggregate = validation.Combine(notFound); // AggregateError with 2 errors
```

## Quick Start

Here's a simple example demonstrating the power of Railway Oriented Programming:

```csharp
public record User(string Id, string Email, bool IsActive);

public Result<User> GetActiveUser(string userId)
{
    return GetUserById(userId)
        .ToResult(Error.NotFound($"User {userId} not found"))
        .Ensure(user => user.IsActive, 
               Error.Validation("User account is not active"))
        .Tap(user => LogUserAccess(user.Id));
}

private User? GetUserById(string id) { /* ... */ }
private void LogUserAccess(string userId) { /* ... */ }
```

## Core Operations

### Bind

`Bind` chains operations that return `Result`. It calls the function only if the current result is successful.

**Use when:** You need to chain operations where each step can fail.

```csharp
// Basic bind
Result<int> ParseAge(string input) => 
    int.TryParse(input, out var age) 
        ? Result.Success(age) 
        : Error.Validation("Invalid age");

Result<string> ValidateAge(int age) =>
    age >= 18 
        ? Result.Success($"Age {age} is valid") 
        : Error.Validation("Must be 18 or older");

var result = ParseAge("25")
    .Bind(age => ValidateAge(age)); // Success("Age 25 is valid")

var invalid = ParseAge("15")
    .Bind(age => ValidateAge(age)); // Failure
```

**Async variant:**

```csharp
async Task<Result<User>> GetUserAsync(string id) { /* ... */ }
async Task<Result<Order>> GetLastOrderAsync(User user) { /* ... */ }

var result = await GetUserAsync("123")
    .BindAsync(user => GetLastOrderAsync(user));
```

**Async with CancellationToken:**

```csharp
async Task<Result<User>> GetUserAsync(string id, CancellationToken ct) { /* ... */ }
async Task<Result<Order>> GetLastOrderAsync(User user, CancellationToken ct) { /* ... */ }

var ct = cancellationToken;

// Single parameter
var result = await GetUserAsync("123", ct)
    .BindAsync(user => GetLastOrderAsync(user, ct));

// Works with tuples too
var complexResult = EmailAddress.TryCreate("user@example.com")
    .Combine(UserId.TryCreate("123"))
    .BindAsync((email, userId) => CreateUserAsync(email, userId, ct));
```

### Map

`Map` transforms the value inside a successful `Result`. Unlike `Bind`, the transformation function returns a plain value, not a `Result`.

**Use when:** You need to transform a value without introducing failure.

```csharp
var result = Result.Success(5)
    .Map(x => x * 2)           // Success(10)
    .Map(x => x.ToString());   // Success("10")

// With failure
var failure = Result.Failure<int>(Error.NotFound("Number not found"))
    .Map(x => x * 2);          // Still Failure, Map is not called
```

**Async variant:**

```csharp
var result = await GetUserAsync("123")
    .MapAsync(user => user.Email.ToLowerInvariant());
```

### Tap

`Tap` executes a side effect (like logging) on success without changing the result. It returns the same `Result`.

**Use when:** You need to perform side effects (logging, metrics, etc.) without transforming the value.

```csharp
var result = Result.Success(42)
    .Tap(x => Console.WriteLine($"Value: {x}"))  // Logs "Value: 42"
    .Tap(x => _metrics.IncrementCounter())       // Records metric
    .Map(x => x * 2);                            // Success(84)

// With failure - Tap is skipped
var failure = Result.Failure<int>(Error.NotFound("Not found"))
    .Tap(x => Console.WriteLine("This won't run"))
    .Map(x => x * 2);  // Still Failure
```

**Async variant:**

```csharp
var result = await GetUserAsync("123")
    .TapAsync(async user => await AuditLogAsync(user.Id))
    .TapAsync(user => SendWelcomeEmail(user.Email));
```

**Async with CancellationToken:**

```csharp
var ct = cancellationToken;

// Single parameter
var result = await GetUserAsync("123", ct)
    .TapAsync(user => AuditLogAsync(user.Id, ct))
    .TapAsync(user => SendWelcomeEmailAsync(user.Email, ct));
```

### Ensure

`Ensure` validates a condition on success. If the condition is false, it returns a failure with the specified error.

**Use when:** You need to validate business rules or conditions.

```csharp
Result<User> CreatePremiumUser(string name, int age)
{
    return User.Create(name, age)
        .Ensure(user => user.Age >= 18, 
               Error.Validation("Must be 18 or older"))
        .Ensure(user => !string.IsNullOrEmpty(user.Name), 
               Error.Validation("Name is required"))
        .Tap(user => user.GrantPremiumAccess());
}
```

**Multiple conditions:**

```csharp
var result = GetProduct(productId)
    .Ensure(p => p.Stock > 0, Error.Validation("Out of stock"))
    .Ensure(p => p.Price > 0, Error.Validation("Invalid price"))
    .Ensure(p => !p.IsDiscontinued, Error.Validation("Product discontinued"));
```

**Async variant:**

```csharp
var result = await GetUserAsync("123")
    .EnsureAsync(async user => await IsEmailVerifiedAsync(user.Email),
                Error.Validation("Email not verified"));
```

### EnsureAll

`EnsureAll` runs ALL validation checks and accumulates failures into a single error, instead of short-circuiting on the first failure like `Ensure`. Useful for form validation where users need to see all errors at once.

**Use when:** You need to report all validation errors simultaneously.

```csharp
var result = Result.Success(request)
    .EnsureAll(
        (r => r.Name.Length > 0, Error.Validation("Name required", "name")),
        (r => r.Age >= 18, Error.Validation("Must be 18+", "age")),
        (r => r.Email.Contains('@'), Error.Validation("Invalid email", "email")));
// If all three fail → ONE ValidationError with 3 field errors
```

Error merging uses `Error.Combine()` — `ValidationError` instances merge their field errors, mixed error types create an `AggregateError`.

### Recover

`Recover` converts any failure to success with a simple fallback value. Sugar for the most common `RecoverOnFailure` pattern.

**Use when:** You want a simple fallback value without needing a `Result`-returning function.

```csharp
// Simple value fallback
var maxRetries = configService.GetInt("max_retries").Recover(3);

// Lazy fallback (function not called on success)
var items = GetRecommendations(userId).Recover(() => Array.Empty<Product>());

// Fallback with error context
var message = TranslateKey(key).Recover(err => $"[{err.Code}] {key}");
```

**Async variant:**

```csharp
var result = await GetUserAsync(userId).RecoverAsync(defaultUser);
```

### RecoverOnFailure

`RecoverOnFailure` provides error recovery by calling a fallback function when a result fails. Useful for providing default values or alternative paths.

**Use when:** You need fallback behavior or error recovery.

**Basic recovery:**

```csharp
// RecoverOnFailure without accessing the error
Result<User> result = GetUser(userId)
    .RecoverOnFailure(() => CreateGuestUser());

// RecoverOnFailure with access to the error
Result<User> result = GetUser(userId)
    .RecoverOnFailure(error => CreateUserFromError(error));
```

**Conditional recovery with predicate:**

RecoverOnFailure only when specific error conditions are met:

```csharp
// RecoverOnFailure only for NotFound errors
Result<User> result = GetUser(userId)
    .RecoverOnFailure(
        predicate: error => error is NotFoundError,
        func: () => CreateDefaultUser()
    );

// RecoverOnFailure with error context
Result<User> result = GetUser(userId)
    .RecoverOnFailure(
        predicate: error => error is NotFoundError,
        func: error => CreateUserFromError(error)
    );

// RecoverOnFailure based on error code
Result<Data> result = FetchData(id)
    .RecoverOnFailure(
        predicate: error => error.Code == "not.found.error",
        func: () => GetCachedData(id)
    );

// RecoverOnFailure for multiple error types
Result<Config> result = LoadConfig()
    .RecoverOnFailure(
        predicate: error => error is NotFoundError or UnauthorizedError,
        func: () => GetDefaultConfig()
    );
```

**Async variant:**

```csharp
var result = await GetUserAsync(userId)
    .RecoverOnFailureAsync(async error => await GetFromCacheAsync(userId));
```

### Combine

`Combine` aggregates multiple `Result` objects. If all succeed, returns success with all values. If any fail, returns all errors combined.

**Use when:** You need to validate multiple independent operations before proceeding.

```csharp
// Combine multiple validations
var result = EmailAddress.TryCreate("user@example.com")
    .Combine(FirstName.TryCreate("John"))
    .Combine(LastName.TryCreate("Doe"))
    .Bind((email, firstName, lastName) => 
        User.Create(email, firstName, lastName));

// All validations must pass
if (result.IsSuccess)
{
    var user = result.Value; // All inputs were valid
}
else
{
    var errors = result.Error; // Contains all validation errors
}
```

**With optional values:**

In this scenario, `firstName` is optional. If provided, it will be validated; if not, it will be skipped. 
In other words, FirstName.TryCreate is only called if firstName is not null.

```csharp
string? firstName = null;  // Optional
string email = "user@example.com";
string? lastName = "Doe";

var result = EmailAddress.TryCreate(email)
    .Combine(Maybe.Optional(firstName, FirstName.TryCreate))
    .Combine(Maybe.Optional(lastName, LastName.TryCreate))
    .Bind((e, f, l) => CreateProfile(e, f, l));
```

### Check

`Check` runs a validation function on success but preserves the original value. Like `Bind`, the function must return a `Result`, but on success the original value is kept.

**Use when:** You need to validate a side condition without losing the current value.

```csharp
Result<Order> result = GetOrder(orderId)
    .Check(order => ValidateInventory(order.ProductId))
    .Check(order => ValidatePayment(order.PaymentInfo));
// On success, result still contains the original Order
// On failure, returns the first failing error
```

### BindZip

`BindZip` sequentially accumulates values into tuples. Each step can depend on all previously accumulated values, and the results are combined into a growing tuple.

**Use when:** You need sequential, dependent operations that each add a value (unlike `Combine`, which is parallel and independent).

```csharp
var result = GetUser(userId)
    .BindZip(user => GetSubscription(user.SubscriptionId))
    .BindZip((user, sub) => GetInvoices(user.Id, sub.Plan))
    .Map((user, sub, invoices) => new Dashboard(user, sub, invoices));
```

**Compared to Combine:** `Combine` runs independent validations and collects all errors. `BindZip` is sequential — each step can use values from previous steps, and it short-circuits on the first failure.

### MapIf

`MapIf` conditionally transforms the value when a condition or predicate is met. If the condition is false, the original value passes through unchanged.

**Use when:** You want to apply a transformation only under certain circumstances.

```csharp
// With a boolean condition
var result = GetPrice(productId)
    .MapIf(applyDiscount, price => price * 0.9m);

// With a predicate
var result = GetUser(userId)
    .MapIf(user => user.IsTrialExpired, user => user with { Plan = Plan.Free });
```

### EnsureNotNull

`EnsureNotNull` narrows `Result<T?>` to `Result<T>` by failing with the specified error when the value is null.

**Use when:** An upstream operation returns a nullable value and you need to guarantee non-null from that point on.

```csharp
Result<User> result = FindUserByEmail(email)     // Result<User?>
    .EnsureNotNull(Error.NotFound($"No user with email {email}"));
// Success with non-null User, or Failure with NotFoundError
```

### GetValueOrDefault

`GetValueOrDefault` is a terminal operator that extracts the success value or returns a fallback. This exits the `Result` pipeline.

**Use when:** You need to unwrap the value at the end of a chain with a safe fallback.

```csharp
// Static default
int retries = GetConfig("max_retries").Map(int.Parse).GetValueOrDefault(3);

// Lazy factory (only called on failure)
var user = GetUser(id).GetValueOrDefault(() => User.Guest);

// Factory with error context
var message = Translate(key).GetValueOrDefault(err => $"[{err.Code}] {key}");
```

## Advanced Features

### LINQ Query Syntax

You can use C# query expressions with `Result` via `Select`, `SelectMany`, and `Where`:

```csharp
// Chaining operations with query syntax
var total = from a in Result.Success(2)
            from b in Result.Success(3)
            from c in Result.Success(5)
            select a + b + c;  // Success(10)

// With failure
var result = from x in Result.Success(5)
             where x > 10  // Predicate fails -> UnexpectedError
             select x;

// Practical example
var userOrder = from user in GetUser(userId)
                from order in GetOrder(orderId)
                where order.UserId == user.Id
                select (user, order);
```

**Note:** `where` uses an `UnexpectedError` if the predicate fails. For domain-specific errors, prefer `Ensure`.

### Maybe LINQ Query Syntax

You can use C# query expressions with `Maybe<T>` via `Select` and `SelectMany`:

```csharp
// Compose optional values
Maybe<string> fullName =
    from first in firstName
    from last in lastName
    select $"{first} {last}";

// Chain optional lookups
Maybe<Email> managerEmail =
    from user in users.FindById(userId)
    from manager in users.FindById(user.ManagerId)
    from email in manager.Email
    select email;

// Any None short-circuits to None
Maybe<string> a = Maybe.From("A");
Maybe<string> b = Maybe<string>.None;
var result = from x in a
             from y in b    // None → entire expression is None
             select x + y;  // Never reached
```

### ToMaybe — Convert Result to Maybe

`ToMaybe` converts a `Result<T>` to a `Maybe<T>`: success→Some(value), failure→None. The error is intentionally discarded.

**Use when:** You don't care why an operation failed — you just want the value if it succeeded, or nothing.

```csharp
// Try to load avatar — if it fails, just don't show one
Maybe<Avatar> avatar = await avatarService.GetByUserId(userId).ToMaybeAsync();

// Sync
Maybe<User> user = GetUser(id).ToMaybe();
```

### Pattern Matching

Use `Match` to handle both success and failure cases inline:

```csharp
// Synchronous match
var description = GetUser("123").Match(
    onSuccess: user => $"User: {user.Name}",
    onFailure: error => $"Error: {error.Code}"
);

// Async match
await ProcessOrderAsync(order).MatchAsync(
    onSuccess: async order => await SendConfirmationAsync(order),
    onFailure: async error => await LogErrorAsync(error)
);

// With return value
var httpResult = SaveData(data).Match(
    onSuccess: data => Results.Ok(data),
    onFailure: error => error.ToErrorResult()
);
```

### Exception Capture

Use `Try` and `TryAsync` to safely capture exceptions and convert them to `Result`:

**Use when:** Integrating with code that throws exceptions.

```csharp
// Synchronous
Result<string> LoadFile(string path)
{
    return Result.Try(() => File.ReadAllText(path));
}

// Async
async Task<Result<User>> FetchUserAsync(string url)
{
    return await Result.TryAsync(async () => 
        await _httpClient.GetFromJsonAsync<User>(url));
}

// Usage
var content = LoadFile("config.json")
    .Ensure(c => !string.IsNullOrEmpty(c), 
           Error.Validation("File is empty"))
    .Bind(ParseConfig);
```

### Parallel Operations

Run multiple async operations in parallel and combine their results:

```csharp
var result = await Result.ParallelAsync(
        () => GetStudentInfoAsync(studentId),
        () => GetStudentGradesAsync(studentId),
        () => GetLibraryBooksAsync(studentId))
    .WhenAllAsync()
    .BindAsync((info, grades, books) => 
        PrepareReport(info, grades, books));
```

### Error Transformation

Transform errors while preserving success values:

```csharp
Result<int> GetUserPoints(string userId) { /* ... */ }

var apiResult = GetUserPoints(userId)
    .MapOnFailure(err => Error.NotFound($"Points for user {userId} not found"));

// Success values pass through unchanged
// Failure errors are replaced with the new error
```

### Collection Helpers

LINQ-style queries that return `Maybe<T>` instead of throwing, plus utilities for working with sequences of optionals.

**TryFirst / TryLast** — safe element access without exceptions:

```csharp
var first = users.TryFirst();                          // Maybe<User>
var admin = users.TryFirst(u => u.Role == "admin");    // Maybe<User>
var last  = orders.TryLast(o => o.Status == "open");   // Maybe<Order>

// Compare with LINQ: users.First() throws if empty
```

**Choose** — filter and unwrap a sequence of `Maybe<T>`:

```csharp
IEnumerable<Maybe<int>> parsed = inputs.Select(s => TryParseInt(s));
IEnumerable<int> valid = parsed.Choose();  // Only the values that exist

// With projection
IEnumerable<string> names = maybePeople.Choose(p => p.Name);
```

## Common Patterns

### Validation Pipeline

```csharp
public Result<Order> ProcessOrder(OrderRequest request)
{
    return ValidateRequest(request)
        .Bind(req => CheckInventory(req.ProductId, req.Quantity))
        .Bind(product => ValidatePayment(request.PaymentInfo))
        .Bind(payment => CreateOrder(request, payment))
        .Tap(order => SendConfirmationEmail(order))
        .TapOnFailure(error => LogOrderFailure(error));
}
```

### Error Recovery with Fallbacks

```csharp
public Result<Config> LoadConfiguration()
{
    return LoadFromFile("config.json")
        .RecoverOnFailure(error => error is NotFoundError, 
                   () => LoadFromEnvironment())
        .RecoverOnFailure(error => error is NotFoundError, 
                   () => GetDefaultConfig())
        .Ensure(cfg => cfg.IsValid, 
               Error.Validation("Invalid configuration"));
}
```

### Multi-Field Validation

```csharp
public Result<User> RegisterUser(string email, string firstName, string lastName, int age)
{
    return EmailAddress.TryCreate(email)
        .Combine(FirstName.TryCreate(firstName))
        .Combine(LastName.TryCreate(lastName))
        .Combine(Result.Ensure(age >= 18, 
                Error.Validation("Must be 18 or older", "age")))
        .Bind((e, f, l) => User.Create(e, f, l, age));
}
```

### Async Chain with Side Effects

```csharp
public async Task<Result<string>> PromoteCustomerAsync(string customerId)
{
    return await GetCustomerByIdAsync(customerId)
        .ToResultAsync(Error.NotFound($"Customer {customerId} not found"))
        .EnsureAsync(customer => customer.CanBePromoted,
                    Error.Validation("Customer has highest status"))
        .TapAsync(customer => customer.PromoteAsync())
        .BindAsync(customer => SendPromotionEmailAsync(customer.Email))
        .MatchAsync(
            onSuccess: _ => "Promotion successful",
            onFailure: error => error.Detail
        );
}
```

## Debugging

Debugging ROP chains can be tricky — when a chain fails, it's not always obvious which step caused it. Key techniques:

- **`Tap` / `TapOnFailure`** — Add logging at each step without changing the result
- **Break up chains** — Assign intermediate results to named variables for breakpoints
- **Descriptive errors** — Include IDs and context: `Error.NotFound($"User {userId} not found")`
- **Debug extensions** — Use `.Debug("label")` in development (excluded from RELEASE builds)
- **OpenTelemetry** — Built-in activity tracing for deep pipeline forensics; useful as a break-glass option, but often too noisy for routine production tracing

**[Full Debugging Guide](DEBUGGING.md)** — Comprehensive strategies, code samples, and checklist.

## Best Practices

1. **Use descriptive error messages** with context
   ```csharp
   Error.NotFound($"Order {orderId} not found for user {userId}")
   ```

2. **Include `fieldName` in validation errors** for easier debugging
   ```csharp
   Error.Validation("Email format is invalid", "email")
   ```

3. **Handle errors at boundaries** (controllers, entry points)
   ```csharp
   [HttpPost]
   public ActionResult<User> Register(RegisterRequest request) =>
       RegisterUser(request)
           .ToActionResult(this);
   ```

4. **Use `Try` / `TryAsync` for exception boundaries**
   ```csharp
   Result<Data> LoadData() =>
       Result.Try(() => File.ReadAllText(path))
           .Bind(json => ParseJson(json));
   ```

5. **Provide `CancellationToken`** with async operations
   ```csharp
   var ct = cancellationToken;
   var result = await GetUserAsync(id, ct)
       .BindAsync(user => GetOrderAsync(user.Id, ct))
       .TapAsync(order => LogOrderAsync(order, ct));
   ```

## Related Packages

- [Trellis.Primitives](https://www.nuget.org/packages/Trellis.Primitives) — Type-safe value objects (EmailAddress, Money, etc.)
- [Trellis.DomainDrivenDesign](https://www.nuget.org/packages/Trellis.DomainDrivenDesign) — Aggregate, Entity, ValueObject
- [Trellis.Asp](https://www.nuget.org/packages/Trellis.Asp) — ASP.NET Core integration (Result → HTTP responses)
- [Trellis.Http](https://www.nuget.org/packages/Trellis.Http) — HttpClient → Result\<T\> extensions
- [Trellis.Analyzers](https://www.nuget.org/packages/Trellis.Analyzers) — 19 Roslyn analyzers enforcing ROP best practices
- [Trellis.Testing](https://www.nuget.org/packages/Trellis.Testing) — FluentAssertions extensions for Result\<T\>

## License

MIT — see [LICENSE](../LICENSE) for details.
