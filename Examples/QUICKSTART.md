# Quick Start Guide - Real-World Examples

Get started quickly with these practical examples from the repository.

## Choose Your Starting Point

### "I'm New to Railway Oriented Programming"
**Start Here**: [Validation Example](./Xunit/ValidationExample.cs)

Learn the basics:
```csharp
// Combine multiple validations
var result = FirstName.TryCreate("John")
    .Combine(LastName.TryCreate("Doe"))
    .Combine(EmailAddress.TryCreate("john@example.com"))
    .Bind((first, last, email) => CreateProfile(first, last, email));

// All validations pass → Success
// Any validation fails → Aggregated errors
```

**Next**: Try [Maybe Examples](./Xunit/MaybeExamples.cs) for optional values

---

### "I Want to Build Web APIs"
**Start Here**: [User Controller](./SampleWebApplication/src/Controllers/UsersController.cs)

See how Result maps to HTTP:
```csharp
[HttpPost("register")]
public ActionResult<User> Register([FromBody] RegisterRequest request) =>
    FirstName.TryCreate(request.firstName)
        .Combine(LastName.TryCreate(request.lastName))
        .Combine(EmailAddress.TryCreate(request.email))
        .Bind((first, last, email) => User.TryCreate(first, last, email, request.password))
        .ToActionResult(this);
// Success → 200 OK
// Validation → 400 Bad Request
// NotFound → 404 Not Found
```

**For Minimal API**: Check [User Routes](./SampleMinimalApi/API/UserRoutes.cs)

---

### "I Need Complex Business Logic"
**Start Here**: [Order Workflow](./EcommerceExample/Workflows/OrderWorkflow.cs)

See a complete order processing flow:
```csharp
public async Task<Result<Order>> ProcessOrderAsync(
    CustomerId customerId,
    List<OrderLineRequest> items,
    PaymentInfo paymentInfo)
{
    return await Order.TryCreate(customerId)
        .BindAsync(order => AddItemsAsync(order, items))
        .BindAsync(order => ReserveInventoryAsync(order))
        .RecoverOnFailureAsync(
            predicate: error => error is ValidationError,
            func: async () => await SuggestAlternativesAsync()
        )
        .Bind(order => order.Submit())
        .BindAsync(order => ProcessPaymentAsync(order, paymentInfo))
        .TapAsync(order => SendConfirmationEmailAsync(order));
}
```

**Key Pattern**: Each step validates, next step only runs if previous succeeded

---

### "I'm Building Financial Software"
**Start Here**: [Banking Workflow](./BankingExample/Workflows/BankingWorkflow.cs)

See fraud detection and secure transactions:
```csharp
public async Task<Result<BankAccount>> ProcessSecureWithdrawalAsync(
    BankAccount account,
    Money amount,
    string verificationCode)
{
    return await account.ToResult()
        .EnsureAsync(
            async acc => await _fraudDetection.AnalyzeTransactionAsync(acc, amount),
            Error.Validation("Fraud check failed")
        )
        .BindAsync(acc => VerifyMFAIfLargeAmountAsync(acc, amount, verificationCode))
        .Bind(acc => acc.Withdraw(amount))
        .RecoverOnFailureAsync(
            predicate: error => error.Code == "fraud",
            func: async error => {
                await account.Freeze("Suspicious activity");
                return error;
            }
        );
}
```

**Key Pattern**: Security checks, MFA, recovery on fraud detection

---

## Common Patterns Cheat Sheet

### Pattern 1: Validate Multiple Inputs
```csharp
Email.TryCreate(email)
    .Combine(Name.TryCreate(name))
    .Combine(Age.TryCreate(age))
    .Bind((e, n, a) => CreateUser(e, n, a))
```
**Use When**: Multiple independent validations needed

### Pattern 2: Async Workflow
```csharp
await GetUserAsync(id)
    .ToResultAsync(Error.NotFound("User not found"))
    .EnsureAsync(u => u.IsActive, Error.Validation("Inactive"))
    .TapAsync(u => LogAccessAsync(u))
    .BindAsync(u => GetOrdersAsync(u))
```
**Use When**: Chaining async operations

### Pattern 3: recovery (Fallback/Cleanup)
```csharp
.RecoverOnFailureAsync(
    predicate: error => error is UnexpectedError,
    func: async () => await RetryOperationAsync()
)
```
**Use When**: Need retry logic or cleanup on specific errors

### Pattern 4: Parallel Operations
```csharp
var result = await Result.ParallelAsync(
    () => GetStudentInfoAsync(studentId),
    () => GetStudentGradesAsync(studentId),
    () => GetLibraryBooksAsync(studentId)
)
.WhenAllAsync()
.BindAsync((info, grades, books) => 
    PrepareReport(info, grades, books));
```
**Use When**: Multiple independent async operations need to run concurrently

### Pattern 5: State Machine
```csharp
public Result<Order> Submit()
{
    return this.ToResult()
        .Ensure(_ => Status == OrderStatus.Draft, Error.Validation("Wrong status"))
        .Ensure(_ => Lines.Count > 0, Error.Validation("Empty order"))
        .Tap(_ => Status = OrderStatus.Pending);
}
```
**Use When**: Object has states with validation between transitions

---

## Operation Quick Reference

| Operation | Purpose | Returns | Use When |
|-----------|---------|---------|----------|
| `Bind` | Chain operations that can fail | `Result<TOut>` | Next operation needs Result value |
| `Map` | Transform value | `Result<TOut>` | Simple transformation, no failure |
| `Ensure` | Validate condition | `Result<T>` | Business rule validation |
| `Tap` | Side effect on success | `Result<T>` | Logging, metrics, notifications |
| `Combine` | Merge multiple Results | `Result<(T1,T2,...)>` | Multiple independent validations |
| `RecoverOnFailure` | Fallback on failure | `Result<T>` | Retry, cleanup, alternative path |
| `Match` | Unwrap Result | `TOut` | End of chain, handle both success and failure |

**Async Variants**: `BindAsync`, `MapAsync`, `EnsureAsync`, `TapAsync`, `RecoverOnFailureAsync`, `MatchAsync`

---

## Testing Examples

### Test Success Path
```csharp
[Fact]
public void Order_Creation_Success()
{
    var result = Order.TryCreate(customerId)
        .Bind(order => order.AddLine(productId, "Item", price, 1))
        .Bind(order => order.Submit());
    
    result.IsSuccess.Should().BeTrue();
    result.Value.Status.Should().Be(OrderStatus.Pending);
}
```

### Test Failure Path
```csharp
[Fact]
public void Order_Creation_Fails_With_Invalid_Email()
{
    var result = EmailAddress.TryCreate("invalid-email")
        .Bind(email => CreateOrder(email));
    
    result.IsFailure.Should().BeTrue();
    result.Error.Should().BeOfType<ValidationError>();
}
```

### Test recovery
```csharp
[Fact]
public async Task Payment_Failure_Triggers_Inventory_Release()
{
    // Arrange: Create order with reserved inventory
    var order = CreateOrderWithReservedInventory();
    
    // Act: Process payment (will fail)
    var result = await workflow.ProcessOrderAsync(order, invalidPaymentInfo);
    
    // Assert: Inventory was released
    result.IsFailure.Should().BeTrue();
    inventoryService.GetStock(productId).Should().Be(originalStock);
}
```

---

## Error Handling Best Practices

### ✅ DO
```csharp
// Be specific with errors
return Error.Validation("Email format is invalid", "email");

// Provide context
return Error.NotFound($"Order {orderId} not found");

// Use appropriate error types
if (unauthorized) return Error.Unauthorized("Login required");
if (forbidden) return Error.Forbidden("Insufficient permissions");
```

### ❌ DON'T
```csharp
// Don't use generic errors
return Error.Unexpected("Something went wrong");

// Don't swallow errors
try { /* ... */ } catch { return Error.Unexpected("Error"); }

// Don't throw exceptions in ROP code
if (invalid) throw new Exception(); // Use Result.Fail instead
```

---

## Next Steps

1. **Read the README**: Start with [Examples README](./README.md) for full overview
2. **Pick a Learning Path**: Follow the [Complexity Guide](./README.md#complexity-guide)
3. **Run Examples**: Execute example code and experiment
4. **Read the Docs**: Check [Railway Oriented Programming](../Trellis.Results/README.md)

## Need Help?

- **Concepts**: Read [The Basics](../docs/docfx_project/articles/basics.md)
- **API Reference**: See [Railway Oriented Programming README](../Trellis.Results/README.md)
- **Integration**: Check [ASP.NET Integration](../Trellis.Asp/README.md)
- **Issues**: Open a GitHub issue
