# Debugging Railway Oriented Programming

Chained operations can be challenging to debug. This guide provides strategies for identifying failures in ROP chains.

## Table of Contents

- [Understanding the Railway Track](#understanding-the-railway-track)
- [Common Debugging Challenges](#common-debugging-challenges)
- [Debugging Tools & Techniques](#debugging-tools--techniques)
- [Visual Studio Debugging Tips](#visual-studio-debugging-tips)
- [Common Error Messages](#common-error-messages)
- [Performance Debugging](#performance-debugging)
- [Best Practices](#best-practices)
- [Debugging Checklist](#debugging-checklist)

## Understanding the Railway Track

Railway Oriented Programming chains operations on two tracks:
- **Success Track**: Operations continue through the chain
- **Failure Track**: Once an error occurs, subsequent operations are skipped

**Key insight:** Only the first failure matters—everything after is bypassed.

```csharp
var result = Step1()      // ✅ Succeeds
    .Bind(Step2)          // ❌ Fails - switches to error track
    .Bind(Step3)          // ⏭️ Skipped - on error track
    .Bind(Step4)          // ⏭️ Skipped - on error track
    .Match(
        onSuccess: x => "Won't reach here",
        onFailure: e => "Error from Step2"
    );
```

## Common Debugging Challenges

### 1. Identifying the Failure Point

**Problem**: A long chain fails, but you don't know which operation caused the failure.

```csharp
// Which of these operations failed?
var result = await GetUserAsync(id)
    .ToResultAsync(Error.NotFound("User not found"))
    .EnsureAsync(u => u.IsActive, Error.Validation("User inactive"))
    .BindAsync(u => GetOrdersAsync(u.Id))
    .EnsureAsync(orders => orders.Any(), Error.NotFound("No orders"))
    .MapAsync(orders => orders.Sum(o => o.Total));
```

**Solution 1**: Use `Tap` or `TapOnFailure` for logging at each step:

```csharp
var result = await GetUserAsync(id)
    .Tap(u => _logger.LogDebug("Found user: {UserId}", u.Id))
    .ToResultAsync(Error.NotFound("User not found"))
    .TapOnFailure(err => _logger.LogWarning("Failed to find user: {Error}", err))
    .EnsureAsync(u => u.IsActive, Error.Validation("User inactive"))
    .Tap(u => _logger.LogDebug("User {UserId} is active", u.Id))
    .TapOnFailure(err => _logger.LogWarning("User validation failed: {Error}", err))
    .BindAsync(u => GetOrdersAsync(u.Id))
    .Tap(orders => _logger.LogDebug("Found {Count} orders", orders.Count));
```

**Solution 2**: Break the chain into smaller, named steps:

```csharp
var userResult = await GetUserAsync(id)
    .ToResultAsync(Error.NotFound("User not found"));
    
if (userResult.IsFailure)
{
    _logger.LogWarning("GetUser failed: {Error}", userResult.Error);
    return userResult;
}

var ordersResult = await GetOrdersAsync(userResult.Value.Id);
if (ordersResult.IsFailure)
{
    _logger.LogWarning("GetOrders failed: {Error}", ordersResult.Error);
    return ordersResult;
}

return ordersResult.Map(orders => orders.Sum(o => o.Total));
```

**Solution 3**: Use descriptive error messages with context:

```csharp
var result = await GetUserAsync(id)
    .ToResultAsync(Error.NotFound($"User {id} not found in database"))
    .EnsureAsync(u => u.IsActive, 
        Error.Validation($"User {id} account inactive since {u.DeactivatedAt}"))
    .BindAsync(u => GetOrdersAsync(u.Id))
    .EnsureAsync(orders => orders.Any(), 
        Error.NotFound($"No orders found for user {id}"));
```

### 2. Inspecting Values Mid-Chain

**Problem**: You want to see what value is flowing through the chain at a specific point.

**Solution 1**: Use `Tap` with a breakpoint:

```csharp
var result = await GetUserAsync(id)
    .Tap(user => 
    {
        // Set breakpoint here to inspect 'user'
        var debug = new { user.Id, user.Name, user.Email };
        _logger.LogDebug("User state: {@User}", debug);
    })
    .BindAsync(u => ProcessUserAsync(u));
```

**Solution 2**: Capture values in tests:

```csharp
[Fact]
public async Task Should_Process_Valid_User()
{
    User? capturedUser = null;
    
    var result = await GetUserAsync("123")
        .Tap(user => capturedUser = user)  // Capture for inspection
        .BindAsync(u => ProcessUserAsync(u));
    
    Assert.NotNull(capturedUser);
    Assert.Equal("123", capturedUser.Id);
    result.IsSuccess.Should().BeTrue();
}
```

**Solution 3**: Use `Map` to inspect without changing the value:

```csharp
var result = await GetOrdersAsync(userId)
    .Map(orders => 
    {
        _logger.LogDebug("Order count: {Count}, Total: {Total}", 
            orders.Count, orders.Sum(o => o.Total));
        return orders;  // Return unchanged
    })
    .BindAsync(orders => ProcessOrdersAsync(orders));
```

### 3. Debugging Async Chains

**Problem**: Async chains are harder to step through in the debugger.

**Solution**: Break async chains into named variables:

```csharp
// Instead of one long chain
var result = await GetUserAsync(id)
    .BindAsync(u => GetOrdersAsync(u.Id))
    .MapAsync(orders => ProcessOrders(orders));

// Break it up for debugging
var userResult = await GetUserAsync(id);  // Set breakpoint here
if (userResult.IsFailure) return userResult;

var ordersResult = await GetOrdersAsync(userResult.Value.Id);  // Breakpoint
if (ordersResult.IsFailure) return ordersResult;

var processed = ordersResult.Map(orders => ProcessOrders(orders));  // Breakpoint
return processed;
```

### 4. Debugging Aggregated Errors

**Problem**: When using `Combine`, all errors are collected. Which validations failed?

```csharp
var result = EmailAddress.TryCreate("invalid")
    .Combine(FirstName.TryCreate(""))
    .Combine(Age.TryCreate(-5));
// Might fail with 3 errors - which ones?
```

**Solution**: Use `TapOnFailure` to log aggregated errors:

```csharp
var result = EmailAddress.TryCreate(email)
    .Combine(FirstName.TryCreate(firstName))
    .Combine(Age.TryCreate(age))
    .TapOnFailure(error => 
    {
        if (error is AggregateError aggregated)
        {
            foreach (var err in aggregated.Errors)
            {
                _logger.LogWarning("Validation failed: {Message}", err.Detail);
            }
        }
    });
```

### 5. Testing Individual Steps

**Problem**: A complex chain makes it hard to test individual operations.

**Solution**: Extract operations into testable methods:

```csharp
// Instead of inline
public Result<User> ValidateAndProcessUser(string id)
{
    return GetUser(id)
        .Ensure(u => u.IsActive, Error.Validation("Inactive"))
        .Ensure(u => u.Email.Contains("@"), Error.Validation("Invalid email"))
        .Tap(u => u.LastLoginAt = DateTime.UtcNow);
}

// Extract testable pieces
public Result<User> GetActiveUser(string id) =>
    GetUser(id).Ensure(u => u.IsActive, Error.Validation("User inactive"));

public Result<User> ValidateUserEmail(User user) =>
    user.Email.Contains("@") 
        ? Result.Success(user)
        : Error.Validation("Invalid email");

public void UpdateLastLogin(User user) =>
    user.LastLoginAt = DateTime.UtcNow;

// Compose
public Result<User> ValidateAndProcessUser(string id) =>
    GetActiveUser(id)
        .Bind(ValidateUserEmail)
        .Tap(UpdateLastLogin);

// Easy to test
[Fact]
public void GetActiveUser_Should_Fail_For_Inactive_User()
{
    var result = GetActiveUser("inactive-id");
    result.IsFailure.Should().BeTrue();
}
```

## Debugging Tools & Techniques

### Built-in Debug Extension Methods

The library includes debug extension methods that are **automatically excluded from RELEASE builds** (no performance impact in production):

```csharp
// Basic debug output - prints success/failure and value/error
var result = GetUser(id)
    .Debug("After GetUser")
    .Ensure(u => u.IsActive, Error.Validation("Inactive"))
    .Debug("After Ensure")
    .Bind(ProcessUser)
    .Debug("After ProcessUser");

// Output in DEBUG mode:
// [DEBUG] After GetUser: Success(User { Id = "123", Name = "John" })
// [DEBUG] After Ensure: Success(User { Id = "123", Name = "John" })
// [DEBUG] After ProcessUser: Success(ProcessedUser { ... })
```

**Detailed debug output** (includes error properties and aggregated errors):

```csharp
var result = EmailAddress.TryCreate(email)
    .Combine(FirstName.TryCreate(firstName))
    .Combine(LastName.TryCreate(lastName))
    .DebugDetailed("After validation");

// Output shows:
// - Success/Failure state
// - Error type, code, detail, instance
// - For ValidationError: all field errors
// - For AggregateError: all nested errors
```

**Debug with stack trace**:

```csharp
var result = ProcessOrder(orderId)
    .DebugWithStack("Processing order", includeStackTrace: true);

// Includes full stack trace showing where the result originated
```

**Custom debug actions**:

```csharp
var result = GetUser(id)
    .DebugOnSuccess(user => 
    {
        Console.WriteLine($"User: {user.Id}, Email: {user.Email}");
        Console.WriteLine($"IsActive: {user.IsActive}");
    })
    .DebugOnFailure(error => 
    {
        Console.WriteLine($"Error Type: {error.GetType().Name}");
        Console.WriteLine($"Message: {error.Detail}");
    });
```

**Async variants**:

```csharp
var result = await GetUserAsync(id)
    .DebugAsync("After GetUser")
    .BindAsync(u => GetOrdersAsync(u.Id))
    .DebugDetailedAsync("After GetOrders");
```

**Note**: All `Debug*` methods are conditionally compiled with `#if DEBUG` and have **zero overhead** in Release builds.

### OpenTelemetry Distributed Tracing

Enable distributed tracing to automatically trace your ROP chains when you need full pipeline forensics.
This is powerful, but it can also get noisy because every ROP step creates a span. Treat it as a
break-glass debugging option rather than the default observability setting for routine production use.

If you want lower-noise traces for normal diagnostics, prefer primitive value object tracing and add
manual spans around critical business operations.

```csharp
// Startup configuration (Program.cs or Startup.cs)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerBuilder => tracerBuilder
        .AddResultsInstrumentation()  // Built-in ROP instrumentation!
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());  // Or .AddConsoleExporter() for development
```

**Your ROP chains are automatically traced** when you enable Results instrumentation:

```csharp
// Each operation creates a span in your trace
var result = await GetUserAsync(id)        // Span: "GetUserAsync"
    .BindAsync(u => GetOrdersAsync(u.Id))  // Span: "GetOrdersAsync"
    .MapAsync(ProcessOrders);              // Span: "ProcessOrders"

// Trace hierarchy in Application Insights/Jaeger/Zipkin:
// POST /api/users/123/orders
//   └─ GetUserAsync (42ms)
//   └─ GetOrdersAsync (156ms)
//   └─ ProcessOrders (23ms)
```

**Trace includes**:
- **Operation name** - Method being called
- **Duration** - How long each step took
- **Status** - Ok (success) or Error (failure)
- **Error details** - Error code and message for failures
- **Parent/child relationships** - Full call hierarchy

**View traces in**:
- Azure Application Insights
- Jaeger
- Zipkin
- Grafana Tempo
- Any OpenTelemetry-compatible backend

### Conditional Breakpoints

Set conditional breakpoints in `Tap` operations:

```csharp
var result = ProcessUsers(users)
    .Tap(user => 
    {
        // Set breakpoint here with condition: user.Id == "problem-id"
        if (user.Id == "problem-id")
        {
            var state = new { user.Id, user.Status, user.Email };
            Console.WriteLine($"Problem user state: {state}");
        }
    });
```

### FluentAssertions for Tests

Use FluentAssertions for readable test assertions:

```csharp
[Fact]
public void Should_Fail_With_Validation_Error()
{
    var result = ProcessOrder(invalidOrder);
    
    result.IsFailure.Should().BeTrue();
    result.Error.Should().BeOfType<ValidationError>();
    result.Error.Code.Should().Be("validation.error");
    result.Error.Detail.Should().Contain("invalid quantity");
}

[Fact]
public void Should_Return_Processed_User()
{
    var result = ProcessUser(validUserId);
    
    result.IsSuccess.Should().BeTrue();
    result.Value.Should().NotBeNull();
    result.Value.Status.Should().Be(UserStatus.Active);
}
```

### Logging Strategies

#### Application-Level Logging

```csharp
public async Task<Result<Order>> ProcessOrderAsync(OrderId orderId)
{
    _logger.LogInformation("Processing order {OrderId}", orderId);
    
    return await GetOrderAsync(orderId)
        .TapOnFailure(err => _logger.LogWarning(
            "Failed to get order {OrderId}: {Error}", orderId, err.Detail))
        .BindAsync(order => ValidateOrderAsync(order))
        .TapOnFailure(err => _logger.LogWarning(
            "Order {OrderId} validation failed: {Error}", orderId, err.Detail))
        .BindAsync(order => ProcessPaymentAsync(order))
        .Tap(order => _logger.LogInformation(
            "Successfully processed order {OrderId}", order.Id))
        .TapOnFailure(err => _logger.LogError(
            "Order {OrderId} processing failed: {Error}", orderId, err.Detail));
}
```

#### Structured Logging with Context

```csharp
public async Task<Result<User>> RegisterUserAsync(UserRegistration registration)
{
    using var scope = _logger.BeginScope(new Dictionary<string, object>
    {
        ["Email"] = registration.Email,
        ["RegistrationSource"] = registration.Source
    });
    
    return await ValidateEmailAsync(registration.Email)
        .Tap(_ => _logger.LogDebug("Email validated"))
        .BindAsync(email => CreateUserAsync(email, registration))
        .Tap(user => _logger.LogInformation("User created: {UserId}", user.Id))
        .TapOnFailure(err => _logger.LogWarning("Registration failed: {Error}", err));
}
```

## Visual Studio Debugging Tips

### Watch Window Tips

When stopped at a breakpoint with a `Result<T>` in scope:

| Expression | Value | Notes |
|------------|-------|-------|
| `result.IsSuccess` | `true`/`false` | Safe to evaluate |
| `result.IsFailure` | `true`/`false` | Safe to evaluate |
| `result.Value` | ⚠️ Value or **Exception** | Throws `InvalidOperationException` if `IsFailure`! |
| `result.Error` | ⚠️ Error or **Exception** | Throws `InvalidOperationException` if `IsSuccess`! |
| `result.TryGetValue(out var v)` | `true` + populates `v` | **Safe** - no exceptions |
| `result.TryGetError(out var e)` | `true` + populates `e` | **Safe** - no exceptions |

**Tip**: Use `TryGetValue` and `TryGetError` in the Watch window to safely inspect without exceptions.

### Quick Watch for Error Details

```csharp
// In Watch window or Quick Watch:
result.Error.Code          // "validation.error"
result.Error.Detail        // "Email is required"
result.Error.Instance      // "user-123" (if set)

// For ValidationError:
((ValidationError)result.Error).FieldErrors.Count  // Number of field errors
((ValidationError)result.Error).FieldErrors[0].FieldName  // "email"
((ValidationError)result.Error).FieldErrors[0].Details[0]  // "Email is required"
```

## Common Error Messages

### "No handler provided for error type"

```
InvalidOperationException: No handler provided for error type NotFoundError
```

**Cause**: Using `MatchError` without providing handlers for all error types and no `onError` fallback.

**Fix**: Add an `onError` fallback to catch all unhandled error types:

```csharp
.MatchError(
    onValidation: err => HandleValidation(err),
    onNotFound: err => HandleNotFound(err),
    onError: err => HandleOtherErrors(err),  // ✅ Catches all other types
    onSuccess: val => HandleSuccess(val)
)
```

### "Attempted to access Value for a failed result"

```
InvalidOperationException: Attempted to access the Value for a failed result. A failed result has no Value.
```

**Cause**: Accessing `result.Value` when `result.IsFailure == true`.

**Fix**: Always check state first or use safe alternatives:

```csharp
// ✅ Check first
if (result.IsSuccess)
    var value = result.Value;

// ✅ Use TryGetValue (recommended)
if (result.TryGetValue(out var value))
    Console.WriteLine(value);

// ✅ Use Match
result.Match(
    onSuccess: val => UseValue(val),
    onFailure: err => HandleError(err)
);
```

### "Attempted to access Error for a successful result"

```
InvalidOperationException: Attempted to access the Error property for a successful result. A successful result has no Error.
```

**Cause**: Accessing `result.Error` when `result.IsSuccess == true`.

**Fix**: Check state or use `TryGetError`:

```csharp
// ✅ Check first
if (result.IsFailure)
    var error = result.Error;

// ✅ Use TryGetError (recommended)
if (result.TryGetError(out var error))
    _logger.LogError(error.Detail);

// ✅ Use MatchError
result.MatchError(
    onError: err => LogError(err),
    onSuccess: val => ProcessValue(val)
);
```

## Performance Debugging

### Profiling ROP Chains

ROP adds **minimal overhead** (~11-16 nanoseconds per operation on .NET 10). If you're experiencing performance issues:

1. **Profile I/O operations first** - Database queries, HTTP calls, file I/O are typically **1000-10000x slower** than ROP overhead
2. **Check for N+1 queries** - Multiple `BindAsync` calls in a loop may indicate an N+1 problem
3. **Use parallel operations** - Independent async operations should use `ParallelAsync`

```csharp
// ❌ Sequential - slow (300ms total)
var user = await GetUserAsync(id);          // 100ms
var orders = await GetOrdersAsync(id);      // 100ms  
var prefs = await GetPreferencesAsync(id);  // 100ms

// ✅ Parallel - fast (100ms total)
var result = await GetUserAsync(id)
    .ParallelAsync(GetOrdersAsync(id))
    .ParallelAsync(GetPreferencesAsync(id))
    .WhenAllAsync();  // All 3 run concurrently
```

**Key insight**: The ROP overhead (16ns) is **0.000016%** of a 100ms database query. Focus on optimizing I/O, not ROP chains.

### Identifying N+1 Queries

```csharp
// ❌ N+1 problem - executes N database queries
var orderResults = new List<Result<Order>>();
foreach (var orderId in orderIds)  // If 100 IDs → 100 queries!
{
    var order = await GetOrderAsync(orderId);  // Database call in loop
    orderResults.Add(order);
}

// ✅ Single query - much faster
var orders = await GetOrdersAsync(orderIds);  // 1 query for all IDs
```

### Performance Tips

- **Use `ParallelAsync` for independent operations** - Runs operations concurrently
- **Batch database operations** - Fetch multiple records in one query
- **Profile with real tools** - Use dotnet-trace, PerfView, or Application Insights
- **Don't optimize ROP chains** - Focus on I/O (database, HTTP, files)

See [BENCHMARKS.md](BENCHMARKS.md) for detailed performance analysis showing ROP overhead is **negligible** compared to typical I/O operations.

## Best Practices

### 1. Use Descriptive Error Messages

```csharp
// ❌ Bad - Generic error
.Ensure(user => user.Age >= 18, Error.Validation("Invalid age"))

// ✅ Good - Specific, actionable error with context
.Ensure(user => user.Age >= 18, 
    Error.Validation(
        $"User {user.Id} must be 18 or older. Current age: {user.Age}",
        "age"
    ))
```

### 2. Add Context to Errors

```csharp
// ✅ Include relevant IDs in error detail and instance
return await GetOrderAsync(orderId)
    .ToResultAsync(Error.NotFound(
        $"Order {orderId} not found for user {userId}",
        $"order-{orderId}"
    ));
```

### 3. Use Tap for Side Effects, Not Logic

```csharp
// ❌ Bad - Logic in Tap (mutating state)
.Tap(user => user.IsActive = true)

// ✅ Good - Pure transformation with Map
.Map(user => user with { IsActive = true })

// ✅ Good - True side effect (logging, metrics, notifications)
.Tap(user => _logger.LogInformation("User activated: {UserId}", user.Id))
```

### 4. Break Long Chains When Debugging

```csharp
// ❌ Hard to debug - can't inspect intermediate steps
var result = Step1().Bind(Step2).Bind(Step3).Bind(Step4).Bind(Step5);

// ✅ Easier to debug - break at major boundaries
var validationResult = ValidateInput(input);  // Breakpoint
var dataResult = validationResult.Bind(FetchData);  // Breakpoint
var processedResult = dataResult.Bind(ProcessData);  // Breakpoint
var finalResult = processedResult.Bind(SaveData);  // Breakpoint

// Each variable can be inspected independently
```

**Note**: In production code, long chains are fine—only break them when actively debugging!

### 5. Name Your Lambdas for Better Stack Traces

```csharp
// ❌ Anonymous lambda - hard to see in call stack
.BindAsync(x => ProcessAsync(x))

// ✅ Named method - shows in call stack and exceptions
.BindAsync(ProcessOrderAsync)

async Task<Result<Order>> ProcessOrderAsync(Order order)
{
    // Implementation
}
```

## Debugging Checklist

When debugging a failing ROP chain, ask yourself:

- [ ] **Check the error message** - Does it tell you which operation failed?
- [ ] **Add `Tap` or `TapOnFailure`** - Log at each step to find the failure point
- [ ] **Use `Debug()` extension** - Add `.Debug("step name")` for quick debugging
- [ ] **Break the chain** - Split into smaller variables for inspection
- [ ] **Check aggregated errors** - Are multiple validations failing? Check `ValidationError.FieldErrors`
- [ ] **Verify async operations** - Is `CancellationToken` passed correctly?
- [ ] **Review error codes** - Are custom error codes being used consistently?
- [ ] **Test individual operations** - Extract and test each step separately
- [ ] **Check for null values** - Is `ToResult`/`ToResultAsync` being used for nullable types?
- [ ] **Inspect error metadata** - Does the error include the `instance` identifier?
- [ ] **Add structured logging** - Use correlation IDs and scopes
- [ ] **Enable OpenTelemetry** - Trace distributed operations across services
- [ ] **Use Watch window safely** - Use `TryGetValue`/`TryGetError` to avoid exceptions
- [ ] **Check performance** - Profile I/O operations, not ROP overhead

## Common Pitfalls

### Forgetting ToResult/ToResultAsync

```csharp
// ❌ Nullable<T> doesn't automatically convert to Result
User? user = await _repository.GetByIdAsync(userId);
return user.Bind(u => ProcessUser(u));  // Compile error!

// ✅ Convert nullable to Result first
return await _repository.GetByIdAsync(userId)
    .ToResultAsync(Error.NotFound($"User {userId} not found"))
    .BindAsync(ProcessUserAsync);
```

### Accessing Value on Failure

```csharp
// ❌ Throws InvalidOperationException if result is failure
var result = GetUser(userId);
var userName = result.Value.Name;  // Boom!

// ✅ Check state first
if (result.IsSuccess)
{
    var userName = result.Value.Name;
}

// ✅ Use Match (recommended)
var userName = result.Match(
    onSuccess: user => user.Name,
    onFailure: _ => "Unknown"
);

// ✅ Or use TryGetValue (safest)
if (result.TryGetValue(out var user))
{
    var userName = user.Name;
}
```

### Mixing Result and Exceptions

```csharp
// ❌ Don't throw exceptions in ROP chains
.Bind(x => 
{
    if (x.IsInvalid) 
        throw new InvalidOperationException();  // Breaks the railway!
    return Result.Success(x);
})

// ✅ Return Result instead
.Bind(x => 
    x.IsInvalid 
        ? Error.Validation("Invalid operation")
        : Result.Success(x)
)

// ✅ Or use Result.Try to wrap exception-throwing code
.Bind(x => Result.Try(() => RiskyOperation(x)))
```

## Next Steps

- See [Advanced Features](advanced-features.md) for LINQ query syntax and parallel operations
- Learn about [Error Handling](error-handling.md) for discriminated error matching
- Check [BENCHMARKS.md](BENCHMARKS.md) for detailed performance analysis
