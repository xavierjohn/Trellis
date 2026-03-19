# Advanced Features

Advanced Railway Oriented Programming patterns for complex scenarios.

## Table of Contents

- [Pattern Matching](#pattern-matching)
- [Tuple Destructuring](#tuple-destructuring)
- [Exception Capture](#exception-capture)
- [Parallel Operations](#parallel-operations)
- [LINQ Query Syntax](#linq-query-syntax)
- [Maybe Type](#maybe-type) *(see dedicated article)*

## Pattern Matching

`Match` handles both success and failure cases elegantly:

### Basic Pattern Matching

```csharp
var description = GetUser("123").Match(
    onSuccess: user => $"User: {user.Name}",
    onFailure: error => $"Error: {error.Code}"
);
```

### Async Pattern Matching

```csharp
await ProcessOrderAsync(order).MatchAsync(
    onSuccess: async order => await SendConfirmationAsync(order),
    onFailure: async error => await LogErrorAsync(error)
);
```

### With HTTP Results

```csharp
app.MapGet("/users/{id}", async (string id) =>
{
    return await GetUserAsync(id)
        .ToResultAsync(Error.NotFound($"User {id} not found"))
        .MatchAsync(
            onSuccess: user => Results.Ok(user),
            onFailure: error => error.ToHttpResult()
        );
});
```

## Tuple Destructuring

Automatically destructure tuples in Match and Bind operations:

### Automatic Destructuring

```csharp
var result = EmailAddress.TryCreate(email)
    .Combine(UserId.TryCreate(userId))
    .Combine(OrderId.TryCreate(orderId))
    .Match(
        // Tuple automatically destructured into named parameters
        onSuccess: (email, userId, orderId) => 
            $"Order {orderId} for user {userId} at {email}",
        onFailure: error => 
            $"Validation failed: {error.Detail}"
    );
```

### Tuple Destructuring in Bind

```csharp
var result = FirstName.TryCreate("John")
    .Combine(LastName.TryCreate("Smith"))
    .Combine(EmailAddress.TryCreate("john@example.com"))
    .Bind((firstName, lastName, email) => 
        CreateUser(firstName, lastName, email)
    );
```

### Support for 2-9 Parameters

Tuple destructuring supports 2 to 9 combined values:

```csharp
// Works with any number of combined results
var result = value1.TryCreate()
    .Combine(value2.TryCreate())
    .Combine(value3.TryCreate())
    .Combine(value4.TryCreate())
    // ... up to 9 values
    .Bind((v1, v2, v3, v4, /* ... */) => ProcessAll(...));
```

## Exception Capture

Convert exception-throwing code into Results using `Try` and `TryAsync`:

### Synchronous Exception Capture

```csharp
Result<string> LoadFile(string path)
{
    return Result.Try(() => File.ReadAllText(path));
}

// Usage
var content = LoadFile("config.json")
    .Ensure(c => !string.IsNullOrEmpty(c), 
           Error.Validation("File is empty"))
    .Bind(ParseConfig);
```

### Async Exception Capture

```csharp
async Task<Result<User>> FetchUserAsync(string url)
{
    return await Result.TryAsync(async () => 
        await _httpClient.GetFromJsonAsync<User>(url));
}

// Usage with chaining
var user = await FetchUserAsync(apiUrl)
    .EnsureAsync(u => u != null, Error.NotFound("User not found"))
    .TapAsync(u => LogUserAccessAsync(u.Id));
```

### Custom Exception Mapping

```csharp
Result<string> ReadFileWithCustomErrors(string path)
{
    return Result.Try(
        () => File.ReadAllText(path),
        exception => exception switch
        {
            FileNotFoundException => Error.NotFound($"File not found: {path}"),
            UnauthorizedAccessException => Error.Forbidden("Access denied"),
            _ => Error.Unexpected(exception.Message)
        }
    );
}
```

## Parallel Operations

Execute multiple async operations in parallel while maintaining ROP style using `Result.ParallelAsync`:

### Parallel Execution with Result.ParallelAsync

```csharp
// Execute multiple async operations in parallel
var result = await Result.ParallelAsync(
    () => GetUserAsync(userId, cancellationToken),
    () => GetOrdersAsync(userId, cancellationToken),
    () => GetPreferencesAsync(userId, cancellationToken)
)
.WhenAllAsync()
.BindAsync((user, orders, preferences, ct) => 
    CreateDashboard(user, orders, preferences, ct),
    cancellationToken
);
```

**How it works:**
1. `Result.ParallelAsync` accepts factory functions (`Func<Task<Result<T>>>`)
2. All operations **start immediately** and run **concurrently**
3. `.WhenAllAsync()` waits for all operations to complete
4. Returns `Result<(T1, T2, T3)>` tuple containing all values
5. If any operation fails, returns combined errors
6. Tuple is automatically destructured in `BindAsync`

### Multi-Stage Parallel Execution

Execute dependent operations in stages - parallel within stages, sequential between stages:

```csharp
// Stage 1: Fetch core data in parallel
var result = await Result.ParallelAsync(
    () => FetchUserAsync(userId, ct),
    () => CheckInventoryAsync(productId, ct),
    () => ValidatePaymentAsync(paymentId, ct)
)
.WhenAllAsync()  // Wait for Stage 1 to complete

// Stage 2: Use results from Stage 1 to run fraud & shipping in parallel
.BindAsync((user, inventory, payment, ct) =>
    Result.ParallelAsync(
        () => RunFraudDetectionAsync(user, payment, inventory, ct),
        () => CalculateShippingAsync(address, inventory, ct)
    )
    .WhenAllAsync()
    .BindAsync((fraudCheck, shipping, ct2) =>
        Result.Success(new CheckoutResult(user, inventory, payment, fraudCheck, shipping))
    ),
    ct
);
```

**Why multi-stage?**
- Stage 2 operations **depend on** Stage 1 results
- Each stage runs **in parallel** internally
- Stages run **sequentially** (Stage 2 waits for Stage 1)
- **2-3x performance** improvement over sequential execution

### Real-World Example: Fraud Detection

```csharp
public async Task<Result<Transaction>> ProcessTransactionAsync(
    Transaction transaction,
    CancellationToken ct)
{
    // Run all fraud checks in parallel
    var result = await Result.ParallelAsync(
        () => CheckBlacklistAsync(transaction.AccountId, ct),
        () => CheckVelocityLimitsAsync(transaction, ct),
        () => CheckAmountThresholdAsync(transaction, ct),
        () => CheckGeolocationAsync(transaction, ct)
    )
    .WhenAllAsync()
    .BindAsync((check1, check2, check3, check4, ct) => 
        ApproveTransactionAsync(transaction, ct), 
        ct
    );
    
    return result;
}
```

**Performance benefit:**
- **Sequential:** 4 checks × 50ms each = 200ms
- **Parallel:** max(50ms, 50ms, 50ms, 50ms) = 50ms
- **4x faster!**

### Error Handling in Parallel Operations

```csharp
// If ANY operation fails, the entire result fails
var result = await Result.ParallelAsync(
    () => GetUserAsync(userId, ct),           // ✅ Success
    () => GetOrdersAsync(userId, ct),          // ❌ Fails (user has no orders)
    () => GetPreferencesAsync(userId, ct)      // ✅ Success
).WhenAllAsync();

// result.IsFailure == true
// result.Error contains the "no orders" error
```

**Multiple failures:**
```csharp
var result = await Result.ParallelAsync(
    () => ValidateEmailAsync("invalid"),       // ❌ ValidationError
    () => ValidatePhoneAsync("bad"),           // ❌ ValidationError
    () => ValidateAgeAsync(-5)                 // ❌ ValidationError
).WhenAllAsync();

// result.Error is ValidationError with all 3 field errors combined
```

### Parallel vs Sequential Comparison

**Sequential (old way):**
```csharp
var user = await GetUserAsync(userId, ct);
if (user.IsFailure) return user.Error;

var orders = await GetOrdersAsync(userId, ct);
if (orders.IsFailure) return orders.Error;

var prefs = await GetPreferencesAsync(userId, ct);
if (prefs.IsFailure) return prefs.Error;

// Total time: ~150ms (50ms + 50ms + 50ms)
```

**Parallel (new way):**
```csharp
var result = await Result.ParallelAsync(
    () => GetUserAsync(userId, ct),
    () => GetOrdersAsync(userId, ct),
    () => GetPreferencesAsync(userId, ct)
).WhenAllAsync();

// Total time: ~50ms (all run concurrently)
// 3x faster!
```

### When to Use Parallel Execution

✅ **DO use `Result.ParallelAsync` when:**
- Operations are **independent** (no dependencies between them)
- Operations can run **concurrently** safely
- **Performance matters** (user-facing, high-throughput)
- Need to **aggregate errors** from multiple validations

❌ **DON'T use when:**
- Operations have **dependencies** (use `BindAsync` chain)
- Operations must run **sequentially**
- Operations **modify shared state** (need synchronization)

### Best Practices

1. **Use factory functions** - Ensures operations don't start until `ParallelAsync` is called
2. **Always call `.WhenAllAsync()`** - Waits for all operations to complete
3. **Short-circuit on failure** - If one fails, others may still complete but result will be failure
4. **Combine errors intelligently** - ValidationErrors merge field errors, different types create AggregateError
5. **Pass CancellationToken** - Allows graceful cancellation of all operations

## LINQ Query Syntax

Use C#'s LINQ query syntax for readable multi-step operations:

### Basic LINQ Query

```csharp
var result =
    from user in GetUser(userId)
    from order in GetLastOrder(user)
    from payment in ProcessPayment(order)
    select new OrderConfirmation(user, order, payment);
```

### LINQ with Where Clause

```csharp
var result =
    from email in EmailAddress.TryCreate(emailInput)
    from user in GetUserByEmail(email)
    where user.IsActive
    from orders in GetUserOrders(user.Id)
    select new UserSummary(user, orders);
```

**Note:** The `where` clause uses a generic "filtered out" error. For domain-specific error messages, use `Ensure` instead:

```csharp
// Better: Use Ensure for custom error messages
var result = EmailAddress.TryCreate(emailInput)
    .Bind(email => GetUserByEmail(email))
    .Ensure(user => user.IsActive, Error.Validation("User account is not active"))
    .Bind(user => GetUserOrders(user.Id))
    .Map(orders => new UserSummary(user, orders));
```

### LINQ with Async Operations

```csharp
var result = await (
    from userId in UserId.TryCreate(userIdInput)
    from user in GetUserAsync(userId)
    from permissions in GetPermissionsAsync(user.Id)
    select new UserWithPermissions(user, permissions)
).ConfigureAwait(false);
```

**Note:** LINQ query syntax works best with synchronous operations. For complex async workflows, consider using `BindAsync` for better readability and cancellation token support.

### Maybe LINQ Query Syntax

`Maybe<T>` also supports LINQ query syntax via `Select` and `SelectMany`, enabling composition of optional values:

```csharp
// Compose multiple optional values
Maybe<string> fullName =
    from first in firstName
    from last in lastName
    select $"{first} {last}";
// If either is None → result is None

// Chain optional lookups
Maybe<Email> managerEmail =
    from user in users.FindById(userId)
    from manager in users.FindById(user.ManagerId)
    from email in manager.Email
    select email;
```

### ToMaybe - Convert Result to Maybe

`ToMaybe` converts a `Result<T>` to a `Maybe<T>`: success→Some(value), failure→None. The error is intentionally discarded.

**Use when:** You don't care why something failed — you just want the value if it succeeded.

```csharp
// Try to load avatar — if it fails, just don't show one
Maybe<Avatar> avatar = await avatarService.GetByUserId(userId).ToMaybeAsync();

// Sync version
Maybe<User> user = GetUser(id).ToMaybe();
```

## Maybe Type

`Maybe<T>` represents domain-level optionality — a value that was either provided or intentionally omitted. It composes with `Result<T>` pipelines and provides type-safe handling of optional value objects.

For a complete guide on why `Maybe<T>` exists, when to use it vs. `T?`, and its full API, see the dedicated **[Why Maybe?](maybe-type.md)** article.

**Quick example:**

```csharp
// Optional value object on an entity
public class Customer : Entity<CustomerId>
{
    public Maybe<PhoneNumber> Phone { get; }
}

// Bridge to Result pipeline
var result = customer.Phone
    .ToResult(Error.NotFound("No phone on file"))
    .Map(phone => FormatForDisplay(phone))
    .Bind(formatted => SendSmsAsync(formatted));
```

## Best Practices

1. **Use Try for Third-Party Code**: Wrap exception-throwing code with `Result.Try` or `Result.TryAsync`
2. **Leverage Tuples**: Use tuple destructuring for combining multiple validations
3. **Parallel When Possible**: Use `Task.WhenAll` for independent async operations
4. **Choose Maybe vs Result Carefully**: Use Maybe for optional data, Result for operations that can fail
5. **LINQ for Readability**: Use LINQ query syntax for complex multi-step operations

## Next Steps

- Learn about [Error Handling](error-handling.md) for discriminated error matching
- See [Working with Async Operations](basics.md#working-with-async-operations) for CancellationToken support
- Check [Integration](integration.md) for ASP.NET and FluentValidation usage
