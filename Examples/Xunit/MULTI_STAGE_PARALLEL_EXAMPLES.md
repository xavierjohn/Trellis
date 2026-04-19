# Multi-Stage ParallelAsync Examples - Real World Usage

This document demonstrates **advanced ParallelAsync patterns** showing how to chain multiple stages of parallel execution, where later stages depend on results from earlier stages.

## Quick Summary

| Pattern | Description | Performance | Use Case |
|---------|-------------|-------------|----------|
| **Single-Stage Parallel** | Run N services in parallel | 1x → ~1/N time | Independent operations |
| **Multi-Stage Parallel** | Stage 1 → Stage 2 (both parallel) | Sequential → Parallel | Dependent operations |
| **3-Stage Pipeline** | Stage 1 → Stage 2 → Stage 3 | Sequential → Parallel | Complex workflows |

## Pattern 1: Two-Stage Parallel Execution

**Scenario:** E-commerce checkout with dependent stages

### Stage 1: Fetch Core Data (3 parallel)
- User details
- Inventory check
- Payment validation

### Stage 2: Process Results (2 parallel, depends on Stage 1)
- Fraud detection (uses user + payment + inventory)
- Shipping calculation (uses inventory)

### Code Example

```csharp
var result = await Result.ParallelAsync(
    () => FetchUserAsync(userId),
    () => CheckInventoryAsync(productId),
    () => ValidatePaymentAsync(paymentId)
)
.WhenAllAsync()  // ✅ Wait for Stage 1 to complete

// Stage 2: Now we have (user, inventory, payment)
.BindAsync((user, inventory, payment) =>
    Result.ParallelAsync(
        () => RunFraudDetectionAsync(user, payment, inventory),
        () => CalculateShippingWithWeightAsync(address, inventory)
    )
    .WhenAllAsync()
    .BindAsync((fraudCheck, shipping) =>
        Result.Ok(new CheckoutResult(
            user, 
            inventory, 
            payment, 
            fraudCheck, 
            shipping
        ))
    )
);
```

### Performance Comparison

**Sequential Execution:**
```
User (50ms) → Inventory (50ms) → Payment (50ms) 
  → Fraud (30ms) → Shipping (40ms)
Total: 220ms
```

**Multi-Stage Parallel:**
```
Stage 1: max(50, 50, 50) = 50ms
Stage 2: max(30, 40) = 40ms
Total: 90ms (2.4x faster!)
```

## Pattern 2: Short-Circuit on Failure

**Key Behavior:** If Stage 1 fails, Stage 2 never executes

```csharp
var stage2Executed = false;

var result = await Result.ParallelAsync(
    () => FetchUserAsync("nonexistent-user"),  // ❌ Fails
    () => CheckInventoryAsync(productId),
    () => ValidatePaymentAsync(paymentId)
)
.WhenAllAsync()

.BindAsync((user, inventory, payment) =>  // ❌ Never executes
{
    stage2Executed = true;
    return Result.ParallelAsync(/*...*/);
});

// stage2Executed == false ✅
// result.IsFailure == true
// result.Error == NotFoundError
```

**Why this matters:**
- ✅ **Prevents wasted work** - Don't call fraud detection if user doesn't exist
- ✅ **Fail fast** - Return error immediately
- ✅ **Type safe** - Can't access `user` if Stage 1 failed

## Pattern 3: Three-Stage Pipeline

**Scenario:** Complex order processing with cascading dependencies

### Visual Flow
```
Stage 1 (2 parallel)
├─ FetchUser (50ms)
└─ CheckInventory (50ms)
        ↓ (50ms total)
        
Stage 2 (2 parallel, depends on Stage 1)
├─ ValidatePayment (needs user)
└─ RunFraudDetection (needs user + inventory)
        ↓ (40ms total)
        
Stage 3 (2 parallel, depends on Stage 2)
├─ CalculateShipping (needs inventory)
└─ ReserveInventory (needs fraud check pass)
        ↓ (40ms total)
        
Total: 130ms vs 220ms sequential (1.7x faster)
```

### Code Example

```csharp
var result = await Result.ParallelAsync(
    () => FetchUserAsync(userId),
    () => CheckInventoryAsync(productId)
)
.WhenAllAsync()  // Stage 1 done

.BindAsync((user, inventory) =>
    Result.ParallelAsync(
        () => ValidatePaymentAsync(paymentId),
        () => RunFraudDetectionAsync(user, payment, inventory)
    )
    .WhenAllAsync()  // Stage 2 done
    
    .BindAsync((payment, fraudCheck) =>
        Result.ParallelAsync(
            () => CalculateShippingAsync(address, inventory),
            () => ReserveInventoryAsync(inventory)
        )
        .WhenAllAsync()  // Stage 3 done
        
        .BindAsync((shipping, reservation) =>
            Result.Ok(new OrderConfirmation(/*...*/))
        )
    )
);
```

## Pattern 4: Error Handling at Each Stage

### Stage 1 Failure
```csharp
// User not found → Stage 2 & 3 never execute
FetchUser: ❌ NotFoundError
CheckInventory: ✅ (cancelled)
  → Result: NotFoundError
```

### Stage 2 Failure
```csharp
// Fraud detected → Stage 3 never executes
Stage 1: ✅ (user, inventory)
Stage 2: ❌ ForbiddenError (fraud)
  → Result: ForbiddenError
```

### Stage 3 Failure
```csharp
// Inventory reservation fails
Stage 1: ✅
Stage 2: ✅
Stage 3: ❌ ConflictError (already reserved)
  → Result: ConflictError
```

## Real-World Use Cases

### 1. E-Commerce Checkout
```csharp
Stage 1: User + Inventory + Payment (independent)
Stage 2: Fraud Detection + Shipping (depend on Stage 1)
Stage 3: Tax Calculation + Discount Application (depend on Stage 2)
```

### 2. Social Media Feed
```csharp
Stage 1: User Profile + Friend List + Settings
Stage 2: Posts (filtered by settings) + Notifications (from friends)
Stage 3: Engagement Stats + Recommended Content
```

### 3. Banking Transaction
```csharp
Stage 1: Account Balance + Transaction History + Risk Profile
Stage 2: Fraud Check + Compliance Check (depend on Stage 1)
Stage 3: Execute Transfer + Update Balances (only if Stage 2 passes)
```

### 4. Microservices Fanout
```csharp
Stage 1: Auth Service + User Service
Stage 2: Order Service + Inventory Service (need user context)
Stage 3: Notification Service + Analytics Service (need order result)
```

## Best Practices

### ✅ DO Use Multi-Stage When:
- Later operations **depend on** earlier results
- Operations within a stage are **independent**
- You need **performance** (parallel) + **correctness** (dependencies)
- Each stage represents a **logical boundary** (e.g., validate → process → finalize)

### ❌ DON'T Use When:
- All operations are **completely independent** (use single-stage)
- Operations must run **strictly sequentially** (use BindAsync chain)
- Stages have **circular dependencies** (redesign workflow)

### Performance Tips
1. **Minimize stages** - Each stage adds ~10ms overhead for WhenAllAsync
2. **Balance parallelism** - Aim for 2-4 operations per stage
3. **Short-circuit early** - Put validation in Stage 1
4. **Profile in production** - Measure actual latencies

## Testing Strategy

### Test All Paths
```csharp
✅ All stages succeed (happy path)
✅ Stage 1 fails → Stage 2 never runs
✅ Stage 2 fails → Stage 3 never runs
✅ Stage N fails → return appropriate error
```

### Test Performance
```csharp
✅ Parallel faster than sequential
✅ Each stage executes in parallel
✅ Stages execute sequentially (not all at once)
```

### Test Error Composition
```csharp
✅ Multiple errors in Stage 1 → AggregateError
✅ Stage 2 error type preserved
✅ No Stage 3 errors if Stage 2 failed
```

## Common Mistakes to Avoid

### ❌ Mistake 1: Over-Nesting
```csharp
// Too many stages (hard to read)
Stage1.WhenAllAsync()
  .BindAsync(s1 => Stage2.WhenAllAsync()
    .BindAsync(s2 => Stage3.WhenAllAsync()
      .BindAsync(s3 => Stage4.WhenAllAsync()
        .BindAsync(s4 => /*...*/)))) // 😵 Pyramid of doom
```

**Better:**
```csharp
// Extract to helper methods
var stage1 = await ExecuteStage1();
if (stage1.IsFailure) return stage1.Error;

var stage2 = await ExecuteStage2(stage1.Value);
// ...
```

### ❌ Mistake 2: False Parallelism
```csharp
// This is NOT parallel! (sequential)
var user = await FetchUserAsync(userId);
var inventory = await CheckInventoryAsync(productId);
var payment = await ValidatePaymentAsync(paymentId);
```

**Better:**
```csharp
// This IS parallel
var result = await Result.ParallelAsync(
    () => FetchUserAsync(userId),
    () => CheckInventoryAsync(productId),
    () => ValidatePaymentAsync(paymentId)
).WhenAllAsync();
```

### ❌ Mistake 3: Ignoring Dependencies
```csharp
// Fraud detection needs user + payment, but they're in different stages!
Stage 1: FetchUser
Stage 2: ValidatePayment + RunFraudDetection // ❌ Can't access user here
```

**Better:**
```csharp
Stage 1: FetchUser + ValidatePayment
Stage 2: RunFraudDetection(user, payment) // ✅ Both available
```

## Debugging Tips

### Add Logging Between Stages
```csharp
.WhenAllAsync()
.TapAsync(results => _logger.LogInformation("Stage 1 complete: {Results}", results))
.BindAsync((user, inventory, payment) => 
    Result.ParallelAsync(/*...*/))
```

### Track Execution Times
```csharp
var sw = Stopwatch.StartNew();
var stage1 = await StageOne().WhenAllAsync();
_logger.LogInformation("Stage 1: {Ms}ms", sw.ElapsedMilliseconds);

sw.Restart();
var stage2 = await StageTwo(stage1).WhenAllAsync();
_logger.LogInformation("Stage 2: {Ms}ms", sw.ElapsedMilliseconds);
```

### Use Descriptive Variable Names
```csharp
// ❌ Bad
var r1 = await stage1.WhenAllAsync();
var r2 = await stage2(r1.Value).WhenAllAsync();

// ✅ Good
var coreData = await FetchCoreDataInParallel().WhenAllAsync();
var validationResults = await ValidateInParallel(coreData.Value).WhenAllAsync();
```

## Summary

Multi-stage `ParallelAsync` is **extremely useful** for real-world applications with dependent operations:

✅ **2.4x performance improvement** (2-stage example)
✅ **Type-safe composition** (compiler enforces dependencies)
✅ **Automatic error handling** (short-circuits on failure)
✅ **Clean, readable code** (declarative style)
✅ **Built-in observability** (tracing shows stage execution)

**Key Insight:** Use `ParallelAsync` + `BindAsync` to get the best of both worlds:
- **Parallel** within stages (performance)
- **Sequential** between stages (correctness)

This is the **choreography pattern** in microservices architecture, implemented with Railway-Oriented Programming!
