# Railway Oriented Programming

[![NuGet Package](https://img.shields.io/nuget/v/Trellis.Results.svg)](https://www.nuget.org/packages/Trellis.Results)

Composable error handling for .NET using `Result<T>`, `Maybe<T>`, and 10 discriminated error types. No exceptions, no null — just clean pipelines that read like English.

## Installation

```bash
dotnet add package Trellis.Results
```

## Quick Start

```csharp
using Trellis;

// Chain operations — errors short-circuit automatically
Result<string> result = GetUser(userId)
    .Ensure(user => user.IsActive, Error.Validation("User is not active"))
    .Tap(user => _logger.LogInformation("Accessed {Id}", user.Id))
    .Map(user => user.Email);

// Pattern match the result
result.Match(
    onSuccess: email => Console.WriteLine($"Email: {email}"),
    onFailure: error => Console.WriteLine($"Error: {error.Detail}")
);
```

## Core Types

### Result\<T\>

Represents success (with a value) or failure (with an error).

```csharp
Result<int> success = 42;                                    // Implicit conversion
Result<int> failure = Error.NotFound("Item not found");      // Implicit conversion

if (success.IsSuccess)
    Console.WriteLine(success.Value);  // 42
```

### Maybe\<T\>

Domain-level optionality — replaces nullable references.

```csharp
Maybe<string> some = Maybe.From("hello");
Maybe<string> none = Maybe.None<string>();

string greeting = some.Match(
    s => $"Hi, {s}!",
    () => "No name");
```

### Error Types

| Error Type | Factory | HTTP Status |
|------------|---------|-------------|
| `ValidationError` | `Error.Validation()` | 400 |
| `BadRequestError` | `Error.BadRequest()` | 400 |
| `UnauthorizedError` | `Error.Unauthorized()` | 401 |
| `ForbiddenError` | `Error.Forbidden()` | 403 |
| `NotFoundError` | `Error.NotFound()` | 404 |
| `ConflictError` | `Error.Conflict()` | 409 |
| `DomainError` | `Error.Domain()` | 422 |
| `RateLimitError` | `Error.RateLimit()` | 429 |
| `UnexpectedError` | `Error.Unexpected()` | 500 |
| `ServiceUnavailableError` | `Error.ServiceUnavailable()` | 503 |

## Pipeline Operations

| Method | Purpose | Example |
|--------|---------|---------|
| **Bind** | Chain operations that return `Result` | `.Bind(user => GetOrder(user.Id))` |
| **Map** | Transform value, keep `Result` wrapper | `.Map(user => user.Email)` |
| **Tap** | Side effects on success | `.Tap(user => Log(user.Id))` |
| **TapOnFailure** | Side effects on failure | `.TapOnFailure(err => Log(err))` |
| **Ensure** | Validate conditions | `.Ensure(u => u.IsActive, error)` |
| **EnsureAll** | Validate all, accumulate errors | `.EnsureAll((u => u.IsActive, err1), ...)` |
| **Match** | Pattern match success/failure | `.Match(onSuccess, onFailure)` |
| **MatchError** | Pattern match on specific error types | `.MatchError(onSuccess, onNotFound, ...)` |
| **Combine** | Merge multiple Results | `Result.Combine(r1, r2, r3)` |
| **Recover** | Simple fallback on failure | `.Recover(defaultValue)` |
| **RecoverOnFailure** | Fallback on failure | `.RecoverOnFailure(err => default)` |
| **ToMaybe** | Convert Result to Maybe | `.ToMaybe()` — success→Some, failure→None |
| **MapOnFailure** | Transform errors | `.MapOnFailure(err => Error.Domain(...))` |
| **When** | Conditional operations | `.When(u => u.IsPremium, ApplyDiscount)` |
| **Traverse** | Map collection, short-circuit on first failure | `items.Traverse(i => Validate(i))` |

All operations have **async variants** (`BindAsync`, `MapAsync`, etc.) with `CancellationToken` support.
LINQ query syntax is also supported (`from`, `select`, `where`) for both `Result<T>` and `Maybe<T>`.

## Real-World Example

```csharp
public Result<Order> PlaceOrder(OrderRequest request)
{
    return ProductId.TryCreate(request.ProductId)
        .Combine(Quantity.TryCreate(request.Quantity))
        .Bind((productId, qty) => _inventory.Reserve(productId, qty))
        .Ensure(reservation => reservation.IsConfirmed, 
            Error.Conflict("Item is out of stock"))
        .Map(reservation => Order.FromReservation(reservation))
        .Tap(order => _events.Publish(new OrderPlaced(order.Id)));
}
```

## Combine — Collect All Errors

```csharp
// Validates ALL fields and returns ALL errors at once
var result = FirstName.TryCreate(input.FirstName)
    .Combine(LastName.TryCreate(input.LastName))
    .Combine(EmailAddress.TryCreate(input.Email))
    .Bind((first, last, email) => User.TryCreate(first, last, email));
```

## Performance

**11–16 nanoseconds** per operation — **0.002%** of a typical database query. Zero extra allocations on Combine.

## Related Packages

- [Trellis.Primitives](https://www.nuget.org/packages/Trellis.Primitives) — Type-safe value objects (EmailAddress, Money, etc.)
- [Trellis.DomainDrivenDesign](https://www.nuget.org/packages/Trellis.DomainDrivenDesign) — Aggregate, Entity, ValueObject
- [Trellis.Asp](https://www.nuget.org/packages/Trellis.Asp) — ASP.NET Core integration (Result → HTTP responses)
- [Trellis.Http](https://www.nuget.org/packages/Trellis.Http) — HttpClient → Result\<T\> extensions
- [Trellis.Analyzers](https://www.nuget.org/packages/Trellis.Analyzers) — 19 Roslyn analyzers enforcing ROP best practices
- [Trellis.Testing](https://www.nuget.org/packages/Trellis.Testing) — FluentAssertions extensions for Result\<T\>

## License

MIT — see [LICENSE](https://github.com/xavierjohn/Trellis/blob/main/LICENSE) for details.
