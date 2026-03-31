# Error Handling

This guide covers error types, discriminated matching, and transformation patterns in Railway Oriented Programming.

```csharp
using Trellis;
using System.Collections.Immutable;
```

## Table of Contents

- [Error Types](#error-types)
- [Creating Errors](#creating-errors)
- [Discriminated Error Matching](#discriminated-error-matching)
- [Error Side Effects](#error-side-effects)
- [Error Transformation](#error-transformation)
- [Aggregate Errors](#aggregate-errors)
- [ValidationError Fluent API](#validationerror-fluent-api)
- [Async Error Handling](#async-error-handling)
- [Custom Error Types](#custom-error-types)
- [Error Equality](#error-equality)

## Error Types

Built-in error types map to HTTP status codes and common business scenarios:

| Error Type | HTTP Status | Use Case | Example |
|------------|-------------|----------|---------|
| `ValidationError` | 400 Bad Request | Input validation failures | Invalid email format, required field missing |
| `BadRequestError` | 400 Bad Request | General request errors | Malformed request |
| `UnauthorizedError` | 401 Unauthorized | Authentication required | Missing token, invalid credentials |
| `ForbiddenError` | 403 Forbidden | Insufficient permissions | User cannot access resource |
| `NotFoundError` | 404 Not Found | Resource doesn't exist | User not found, order not found |
| `ConflictError` | 409 Conflict | Resource state conflict | Duplicate email, concurrent update |
| `PreconditionFailedError` | 412 Precondition Failed | ETag mismatch (RFC 9110) | If-Match header doesn't match current ETag |
| `PreconditionRequiredError` | 428 Precondition Required | Missing If-Match (RFC 6585) | Mutating request without required If-Match header |
| `DomainError` | 422 Unprocessable Entity | Business rule violation | Cannot withdraw more than balance |
| `RateLimitError` | 429 Too Many Requests | Rate limit exceeded | Too many login attempts |
| `UnexpectedError` | 500 Internal Server Error | System errors | Database connection failed |
| `ServiceUnavailableError` | 503 Service Unavailable | Service temporarily down | Service under maintenance |
| `AggregateError` | Varies | Multiple errors combined | Multiple validation failures or mixed error types |

### Error Structure

All errors share a common structure:

```csharp
public class Error
{
    public string Code { get; }        // Machine-readable error code
    public string Detail { get; }      // Human-readable error description
    public string? Instance { get; }   // Optional resource identifier
}

// Factory methods create specific error types:
Error.Validation(...)     // ValidationError
Error.NotFound(...)       // NotFoundError
Error.Conflict(...)       // ConflictError
Error.PreconditionFailed(...)  // PreconditionFailedError
Error.PreconditionRequired(...)  // PreconditionRequiredError
Error.BadRequest(...)     // BadRequestError
Error.Unauthorized(...)   // UnauthorizedError
Error.Forbidden(...)      // ForbiddenError
Error.Unexpected(...)     // UnexpectedError
Error.Domain(...)         // DomainError
Error.RateLimit(...)      // RateLimitError
Error.ServiceUnavailable(...) // ServiceUnavailableError
```

## Creating Errors

### Validation Errors

```csharp
// Simple validation error for a single field
var error = Error.Validation("Email is required", "email");

// Results in:
// Code: "validation.error"
// Detail: "Email is required"
// FieldErrors: [{ FieldName: "email", Details: ["Email is required"] }]

// Multiple validation errors using fluent API (see ValidationError Fluent API section)
var error = ValidationError.For("email", "Email is required")
    .And("password", "Password must be at least 8 characters")
    .And("age", "Must be 18 or older");

// Multiple errors for the same field
var error = Error.Validation("Invalid email format", "email");
var error2 = Error.Validation("Email domain not allowed", "email");
var combined = error.Combine(error2);
// Results in single ValidationError with multiple details for "email" field
```

### Not Found Errors

```csharp
// Simple not found
var error = Error.NotFound($"User {userId} not found");
// Code: "not.found.error"
// Detail: "User {userId} not found"
// Instance: null

// With instance identifier
var error = Error.NotFound(
    $"User with ID {userId} does not exist",
    userId.ToString()
);
// Code: "not.found.error"
// Detail: "User with ID {userId} does not exist"
// Instance: "{userId}"
```

### Authorization Errors

```csharp
// Unauthorized (authentication required - user not logged in)
var error = Error.Unauthorized("Authentication token missing");
// Code: "unauthorized.error"
// Maps to HTTP 401

// Forbidden (insufficient permissions - user logged in but lacks access)
var error = Error.Forbidden("User does not have permission to delete orders");
// Code: "forbidden.error"
// Maps to HTTP 403
```

### Conflict Errors

```csharp
// Resource conflict
var error = Error.Conflict($"Email {email} is already registered");

// Concurrent update conflict
var error = Error.Conflict("Resource was modified by another user");
```

### Precondition Failed Errors

```csharp
// RFC 9110 — If-Match ETag mismatch
var error = Error.PreconditionFailed("Resource has been modified. Please reload and retry.");

// Automatically returned by OptionalETag/RequireETag when aggregate ETag doesn't match
return await _repo.GetByIdAsync(id, ct)
    .OptionalETag(command.IfMatchETags)  // returns PreconditionFailedError if stale
    .BindAsync(agg => agg.Update(...));

// RequireETag returns PreconditionRequiredError (428) when If-Match is missing
return await _repo.GetByIdAsync(id, ct)
    .RequireETag(command.IfMatchETags)   // returns PreconditionRequiredError if null
    .BindAsync(agg => agg.Update(...));
```

### Domain Errors

```csharp
// Business rule violations
var error = Error.Domain("Cannot withdraw more than account balance");

// With instance identifier
var error = Error.Domain(
    "Order quantity exceeds available inventory",
    orderId.ToString()
);
```

### Rate Limit Errors

```csharp
// Rate limit exceeded
var error = Error.RateLimit("Too many login attempts. Try again in 60 seconds");

// With retry information
var error = Error.RateLimit("API rate limit exceeded. Retry after 60 seconds");
```

### Service Unavailable Errors

```csharp
// Temporary service unavailability
var error = Error.ServiceUnavailable("Payment service is temporarily unavailable");

// With maintenance window
var error = Error.ServiceUnavailable("System under maintenance until 2:00 AM UTC");
```

### Unexpected Errors

```csharp
// System errors
var error = Error.Unexpected("Database connection failed");

// From exception
try
{
    // risky operation
}
catch (Exception ex)
{
    return Result.Failure<Data>(
        Error.Unexpected($"Unexpected error: {ex.Message}")
    );
}

// Or use Result.Try to automatically convert exceptions
var result = Result.Try(() => RiskyOperation());
```

## Discriminated Error Matching

The `MatchError` method allows you to handle different error types with specific logic:

### Basic Error Matching

```csharp
var httpResult = ProcessOrder(order)
    .MatchError(
        onValidation: validationErr => 
            Results.BadRequest(new { 
                errors = validationErr.FieldErrors
                    .ToDictionary(f => f.FieldName, f => f.Details.ToArray())
            }),
        onNotFound: notFoundErr => 
            Results.NotFound(new { message = notFoundErr.Detail }),
        onConflict: conflictErr => 
            Results.Conflict(new { message = conflictErr.Detail }),
        onSuccess: order => 
            Results.Ok(order)
    );
```

### Complete Error Matching

Handle all error types explicitly:

```csharp
return await ProcessTransactionAsync(transaction)
    .MatchError(
        onValidation: err => 
            Results.BadRequest(new { 
                message = err.Detail,
                errors = err.FieldErrors
                    .ToDictionary(f => f.FieldName, f => f.Details.ToArray())
            }),
        onBadRequest: err =>
            Results.BadRequest(new { message = err.Detail }),
        onNotFound: err => 
            Results.NotFound(new { message = err.Detail }),
        onUnauthorized: err => 
            Results.Unauthorized(),
        onForbidden: err => 
            Results.StatusCode(403),
        onConflict: err => 
            Results.Conflict(new { message = err.Detail }),
        onDomain: err =>
            Results.UnprocessableEntity(new { message = err.Detail }),
        onRateLimit: err =>
            Results.StatusCode(429),
        onServiceUnavailable: err =>
            Results.StatusCode(503),
        onUnexpected: err => 
            Results.StatusCode(500),
        onSuccess: transaction => 
            Results.Ok(new { transactionId = transaction.Id })
    );
```

### Partial Error Matching

You don't need to handle every error type - provide an `onError` fallback for unhandled types:

```csharp
var result = CreateUser(userData)
    .MatchError(
        onValidation: err => Results.BadRequest(err.FieldErrors),
        onConflict: err => Results.Conflict(err.Detail),
        onError: err => Results.StatusCode(500), // Fallback for all other error types
        onSuccess: user => Results.Created($"/users/{user.Id}", user)
    );
```

**Note:** If you don't provide handlers for some error types and no `onError` fallback, `MatchError` will throw `InvalidOperationException` when it encounters an unhandled error type.

### Switch Error Matching (Side Effects Only)

Use `SwitchError` when you only need side effects without returning a value:

```csharp
ProcessOrder(order)
    .SwitchError(
        onValidation: err => _logger.LogWarning("Validation failed: {Errors}", err.FieldErrors),
        onNotFound: err => _logger.LogWarning("Order not found: {Detail}", err.Detail),
        onConflict: err => _logger.LogWarning("Order conflict: {Detail}", err.Detail),
        onSuccess: order => _logger.LogInformation("Order processed: {OrderId}", order.Id)
    );
```

## Error Side Effects

### TapOnFailure - Execute Side Effects on Failure

Use `TapOnFailure` to perform side effects (like logging) when an error occurs without changing the result:

```csharp
var result = ProcessOrder(order)
    .TapOnFailure(error => _logger.LogError("Order processing failed: {Error}", error.Detail))
    .TapOnFailure(error => _metrics.RecordFailure(error.Code))
    .TapOnFailure(error => _notificationService.NotifyAdmin(error));

// TapError only executes on failure
// On success, TapError is skipped
```

### Combined Tap and TapError

```csharp
var result = ProcessPayment(order)
    .Tap(payment => _logger.LogInformation("Payment succeeded: {Id}", payment.Id))
    .TapOnFailure(error => _logger.LogError("Payment failed: {Error}", error.Detail))
    .TapOnFailure(error => SendFailureNotification(error))
    .Tap(payment => SendSuccessEmail(payment));
```

### Async TapError

```csharp
var result = await ProcessOrderAsync(order)
    .TapOnFailureAsync(async error => 
        await _auditLog.LogFailureAsync(error, cancellationToken))
    .TapOnFailureAsync(async error => 
        await _notificationService.NotifyAsync(error, cancellationToken),
        cancellationToken);
```

## Error Transformation

Transform errors as they flow through your pipeline:

### MapOnFailure - Transform Error Types

```csharp
var result = GetUserFromExternalApi(userId)
    .MapOnFailure(error => error switch
    {
        NotFoundError => Error.NotFound(
            "User not found in our system",
            userId
        ),
        UnexpectedError => Error.ServiceUnavailable(
            "External service is temporarily unavailable"
        ),
        _ => error
    });
```

### Add Context to Errors

```csharp
var result = ProcessPayment(order)
    .MapOnFailure(error => Error.Unexpected(
        $"Payment processing failed for order {order.Id}: {error.Detail}",
        $"order-{order.Id}"
    ));
```

### RecoverOnFailure - Error Recovery

```csharp
var result = GetUserFromCache(userId)
    .RecoverOnFailure(cacheError => 
        GetUserFromDatabase(userId)
            .MapOnFailure(dbError => Error.NotFound(
                $"User {userId} not found. Cache: {cacheError.Detail}, DB: {dbError.Detail}",
                userId
            ))
    );
```

## Aggregate Errors

When combining multiple Results, errors are intelligently aggregated based on their types:

### Validation Error Merging

When combining multiple `ValidationError` instances, they are **merged** into a single `ValidationError` with all field errors:

```csharp
var emailError = Error.Validation("Email is required", "email");
var passwordError = Error.Validation("Password is required", "password");
var ageError = Error.Validation("Must be 18 or older", "age");

var result = emailError.Combine(passwordError).Combine(ageError);

// Result: Single ValidationError with 3 field errors
// FieldErrors:
//   - email: ["Email is required"]
//   - password: ["Password is required"]
//   - age: ["Must be 18 or older"]
```

### Mixed Error Types Create AggregateError

When combining `ValidationError` with other error types (or combining different non-validation error types), an `AggregateError` is created:

```csharp
var validationError = Error.Validation("Invalid email", "email");
var notFoundError = Error.NotFound("User not found");
var conflictError = Error.Conflict("Email already exists");

var result = validationError.Combine(notFoundError).Combine(conflictError);

// Result: AggregateError containing 3 separate errors
// Errors:
//   - ValidationError: Invalid email (email field)
//   - NotFoundError: User not found
//   - ConflictError: Email already exists
```

### Automatic Aggregation with Combine

```csharp
var result = EmailAddress.TryCreate(email)
    .Combine(FirstName.TryCreate(firstName))
    .Combine(LastName.TryCreate(lastName));

// If all succeed: Result<(EmailAddress, FirstName, LastName)>
// If any fail with ValidationError: Single merged ValidationError
// If failures include non-validation errors: AggregateError

// Handling aggregated errors
if (result.IsFailure)
{
    if (result.Error is ValidationError validation)
    {
        // All errors were validation errors - merged into one
        foreach (var fieldError in validation.FieldErrors)
        {
            Console.WriteLine($"{fieldError.FieldName}: {string.Join(", ", fieldError.Details)}");
        }
    }
    else if (result.Error is AggregateError aggregate)
    {
        // Mixed error types or multiple non-validation errors
        foreach (var error in aggregate.Errors)
        {
            Console.WriteLine($"{error.GetType().Name}: {error.Detail}");
        }
    }
    else
    {
        // Single error type
        Console.WriteLine($"{result.Error.Detail}");
    }
}
```

### Extracting Validation Errors from AggregateError

Use `FlattenValidationErrors()` to extract a merged `ValidationError` from an `AggregateError`. This is useful when you know the aggregate contains validation errors and you want to present them as a single set of field-level errors:

```csharp
// After Combine produces an AggregateError
var combined = result1.Combine(result2);

// Extract all validation errors
var validationError = combined.FlattenValidationErrors();
```

### Handling AggregateError in MatchError

The `onAggregate` handler in `MatchError` and `SwitchError` lets you handle `AggregateError` specifically, separating it from the generic `onError` fallback:

```csharp
combined.MatchError(
    onValidation: err => /* field-level errors */,
    onAggregate: err => /* unwrap err.Errors */,
    onError: err => /* fallback */
);
```

### Manual Error Aggregation

```csharp
var errors = new List<Error>();

if (string.IsNullOrEmpty(email))
    errors.Add(Error.Validation("Email is required", "email"));

if (age < 18)
    errors.Add(Error.Validation("Must be 18 or older", "age"));

if (errors.Any())
{
    // Combine all errors into one
    var combinedError = errors.Aggregate((acc, err) => acc.Combine(err));
    return Result.Failure<User>(combinedError);
}

return Result.Success(new User(email, age));
```

## ValidationError Fluent API

`ValidationError` provides a fluent API for building multi-field validation errors:

### Building Multi-Field Validation Errors

```csharp
// Start with one field, then chain with And()
var error = ValidationError.For("email", "Email is required")
    .And("password", "Password must be at least 8 characters")
    .And("password", "Password must contain a number")  // Same field, multiple errors
    .And("age", "Must be 18 or older");

// Results in single ValidationError with field errors:
// - email: ["Email is required"]
// - password: ["Password must be at least 8 characters", "Password must contain a number"]
// - age: ["Must be 18 or older"]
```

### Adding Multiple Messages to One Field

```csharp
// Add multiple validation messages for a single field at once
var error = ValidationError.For("email", "Email is required")
    .And("password", 
        "Must be at least 8 characters",
        "Must contain a number",
        "Must contain a special character");

// Results in:
// - email: ["Email is required"]
// - password: ["Must be at least 8 characters", "Must contain a number", "Must contain a special character"]
```

### Merging Validation Errors

```csharp
var emailValidation = ValidationError.For("email", "Invalid format");
var passwordValidation = ValidationError.For("password", "Too short")
    .And("password", "Not complex enough");

var merged = emailValidation.Merge(passwordValidation);

// Results in single ValidationError:
// - email: ["Invalid format"]
// - password: ["Too short", "Not complex enough"]
```

### Using Combine for Automatic Merging

```csharp
// Combine automatically merges ValidationErrors
var error1 = Error.Validation("Email required", "email");
var error2 = Error.Validation("Password required", "password");
var error3 = Error.Validation("Password too short", "password");

var combined = error1.Combine(error2).Combine(error3);

// Results in single ValidationError:
// - email: ["Email required"]
// - password: ["Password required", "Password too short"]
```

## Async Error Handling

Handle errors in async workflows with full cancellation support:

### Async MatchError

```csharp
return await ProcessOrderAsync(orderId, cancellationToken)
    .MatchErrorAsync(
        onValidation: async (err, ct) =>
        {
            await LogValidationFailureAsync(err, ct);
            return Results.BadRequest(err.FieldErrors);
        },
        onNotFound: async (err, ct) =>
        {
            await NotifyNotFoundAsync(err, ct);
            return Results.NotFound(err.Detail);
        },
        onSuccess: async (order, ct) =>
        {
            await SendConfirmationAsync(order, ct);
            return Results.Ok(order);
        },
        cancellationToken: cancellationToken
    );
```

### Async SwitchError

```csharp
await ProcessPaymentAsync(payment, cancellationToken)
    .SwitchErrorAsync(
        onValidation: async (err, ct) => 
            await LogErrorAsync("Validation failed", err, ct),
        onUnexpected: async (err, ct) =>
            await NotifyAdminAsync("Payment system error", err, ct),
        onSuccess: async (result, ct) =>
            await AuditSuccessAsync(result, ct),
        cancellationToken: cancellationToken
    );
```

### Async TapError with CancellationToken

```csharp
var result = await GetUserAsync(userId, cancellationToken)
    .TapOnFailureAsync(
        async (error, ct) => await LogErrorAsync(error, ct),
        cancellationToken
    )
    .TapOnFailureAsync(
        async (error, ct) => await NotifyAdminAsync(error, ct),
        cancellationToken
    );
```

### Async MapError

```csharp
var result = await FetchDataAsync(id, cancellationToken)
    .MapOnFailureAsync(
        async (error, ct) =>
        {
            await LogErrorDetailsAsync(error, ct);
            return Error.ServiceUnavailable("External service unavailable");
        },
        cancellationToken
    );
```

## Custom Error Types

While the built-in error types cover most scenarios, you can extend the system:

### Custom Error Factory Methods

```csharp
public static class CustomErrors
{
    public static RateLimitError RateLimitExceeded(int retryAfterSeconds)
    {
        return Error.RateLimit(
            $"Too many requests. Please try again after {retryAfterSeconds} seconds."
        );
    }

    public static DomainError PaymentDeclined(string reason)
    {
        return Error.Domain(
            $"Payment declined: {reason}"
        );
    }
    
    public static ValidationError InvalidCreditCard(string fieldName)
    {
        return Error.Validation(
            "Credit card number is invalid",
            fieldName
        );
    }
}

// Usage
if (requestCount > limit)
    return Result.Failure<Response>(
        CustomErrors.RateLimitExceeded(retryAfterSeconds: 60)
    );
```

### Domain-Specific Errors

```csharp
public static class OrderErrors
{
    public static Error InsufficientInventory(ProductId productId, int requested, int available)
    {
        return Error.Conflict(
            $"Product {productId} has insufficient inventory. Requested: {requested}, Available: {available}",
            productId.Value
        );
    }

    public static Error OrderAlreadyShipped(OrderId orderId)
    {
        return Error.Conflict(
            $"Order {orderId} has already been shipped and cannot be modified",
            orderId.Value
        );
    }
    
    public static Error PaymentAmountMismatch(decimal expected, decimal actual)
    {
        return Error.Domain(
            $"Payment amount mismatch. Expected: {expected:C}, Received: {actual:C}"
        );
    }
}

// Usage
if (inventory.Available < order.Quantity)
{
    return Result.Failure<Order>(
        OrderErrors.InsufficientInventory(
            order.ProductId, 
            order.Quantity, 
            inventory.Available
        )
    );
}
```

## Best Practices

1. **Use Specific Error Types**: Choose the most specific error type (NotFound vs Validation vs Domain)
2. **Include Context in Instance**: Use the `instance` parameter for resource identifiers
3. **Consistent Error Codes**: Use consistent, meaningful error codes across your app
4. **Handle Errors at Boundaries**: Use MatchError at API boundaries to convert to HTTP responses
5. **Don't Swallow Errors**: Always propagate or handle errors explicitly
6. **Use Aggregate for Multiple Errors**: Return all validation errors at once, not just the first one
7. **Use TapError for Logging**: Add `TapOnFailure` calls to log failures without breaking the chain
8. **Leverage Fluent API**: Use `ValidationError.For().And()` for building multi-field validations
9. **Add Tracing IDs**: Include correlation IDs in error instance for distributed tracing
10. **Use MapError Sparingly**: Only transform errors when you need to add context or change error types

## Error Equality

`Error` follows DDD Value Object equality semantics. `Error.Equals` is virtual, so each subtype compares all of its components:

- Two errors are equal only if they have the **same type**, `Code`, `Detail`, and `Instance`
- `ValidationError` also compares `FieldErrors`
- `AggregateError` also compares its inner `Errors`

This means you can use standard equality checks and assertions in tests:

```csharp
var error1 = Error.NotFound("User not found", "user-42");
var error2 = Error.NotFound("User not found", "user-42");
Assert.Equal(error1, error2); // ✅ Same type, Code, Detail, and Instance

var error3 = Error.NotFound("User not found", "user-99");
Assert.NotEqual(error1, error3); // ❌ Different Instance
```

## Next Steps

- Learn about async operations in [Working with Async Operations](basics.md#working-with-async-operations)
- See [Integration](integration.md) for converting errors to HTTP responses
- Check [Advanced Features](advanced-features.md) for pattern matching and error recovery
