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
    .MapAsync((fraudCheck, shipping) =>
        new CheckoutResult(
            user, 
            inventory, 
            payment, 
            fraudCheck, 
            shipping
        )
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
    return Result.ParallelAsync(
        () => RunFraudDetectionAsync(user, payment, inventory),
        () => CalculateShippingWithWeightAsync(address, inventory))
        .WhenAllAsync();
});

// stage2Executed == false ✅
// result.IsFailure == true
// result.Error == Error.NotFound
```

**Why this matters:**
- ✅ **Prevents wasted work** - Don't call fraud detection if user doesn't exist
- ✅ **Skip later stages** - Finish the already-started operations, combine their failures, and do not start Stage 2
- ✅ **Type safe** - Can't access `user` if Stage 1 failed

## Pattern 3: Three-Stage Pipeline

**Scenario:** Complex order processing with cascading dependencies

### Visual Flow
```
Stage 1 (3 parallel)
├─ FetchUser (50ms)
├─ CheckInventory (50ms)
└─ ValidatePayment (50ms)
        ↓ (50ms total)
        
Stage 2 (2 parallel, depends on Stage 1)
├─ RunFraudDetection (needs user + payment + inventory)
└─ CalculateShipping (needs inventory)
        ↓ (40ms total)
        
Stage 3 (2 parallel, depends on Stage 2)
├─ CalculateTax (needs shipping quote)
└─ FindDiscount (needs user + inventory)
        ↓ (40ms total)
        
Illustrative total: 130ms; actual latency depends on the independent services.
```

### Code Example

```csharp
var result = await Result.ParallelAsync(
    () => FetchUserAsync(userId),
    () => CheckInventoryAsync(productId),
    () => ValidatePaymentAsync(paymentId)
)
.WhenAllAsync()  // Stage 1 done

.BindAsync((user, inventory, payment) =>
    Result.ParallelAsync(
        () => RunFraudDetectionAsync(user, payment, inventory),
        () => CalculateShippingWithWeightAsync(address, inventory)
    )
    .WhenAllAsync()  // Stage 2 done
    
    .BindAsync((fraudCheck, shipping) =>
        Result.ParallelAsync(
            () => CalculateTaxAsync(shipping),
            () => FindDiscountAsync(user, inventory)
        )
        .WhenAllAsync()  // Stage 3 done
        
        .MapAsync((tax, discount) =>
            new CheckoutQuote(user, inventory, payment, fraudCheck, shipping, tax, discount)
        )
    )
);
```

## Pattern 4: Error Handling at Each Stage

### Stage 1 Failure
```csharp
// User not found → Stage 2 & 3 never execute
FetchUser: ❌ Error.NotFound
CheckInventory: ✅ (already started; awaited, not automatically cancelled)
  → Result: Error.NotFound
```

### Stage 2 Failure
```csharp
// Fraud detected → Stage 3 never executes
Stage 1: ✅ (user, inventory)
Stage 2: ❌ Error.Forbidden (fraud)
  → Result: Error.Forbidden
```

### Stage 3 Failure
```csharp
// Tax calculation fails
Stage 1: ✅
Stage 2: ✅
Stage 3: ❌ Error.InvalidInput
  → Result: Error.InvalidInput
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
Stage 3: Execute transfer sequentially after all checks pass; do not parallelize dependent balance mutations
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
- Operations share a scoped `DbContext`, or mutate related state
- Stages have **circular dependencies** (redesign workflow)

### Performance Tips
1. **Minimize dependency depth** - Latency is approximately the sum of each stage's slowest operation; there is no fixed 10ms cost per stage
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
✅ Multiple errors in Stage 1 → Error.Aggregate
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
var result = await ExecuteStage1()
    .BindAsync(ExecuteStage2)
    .BindAsync(ExecuteStage3);
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
// Fraud detection needs a payment result that is still being produced in the same stage.
Stage 1: FetchUser
Stage 2: ValidatePayment + RunFraudDetection // ❌ Payment is not available yet
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
    Result.ParallelAsync(
        () => RunFraudDetectionAsync(user, payment, inventory),
        () => CalculateShippingWithWeightAsync(address, inventory))
        .WhenAllAsync())
```

### Track Execution Times
```csharp
var sw = Stopwatch.StartNew();
var stage1 = await StageOne().WhenAllAsync();
_logger.LogInformation("Stage 1: {Ms}ms", sw.ElapsedMilliseconds);

sw.Restart();
var stage2 = await stage1.BindAsync(values => StageTwo(values).WhenAllAsync());
_logger.LogInformation("Stage 2: {Ms}ms", sw.ElapsedMilliseconds);
```

### Use Descriptive Variable Names
```csharp
// ❌ Bad
var r1 = await stage1.WhenAllAsync();
if (!r1.TryGetValue(out var r1Value, out var r1Error)) return Result.Fail<Stage2Result>(r1Error);
var r2 = await stage2(r1Value).WhenAllAsync();

// ✅ Good
var validationResults = await FetchCoreDataInParallel().WhenAllAsync()
    .BindAsync(coreData => ValidateInParallel(coreData).WhenAllAsync());
```

## Summary

Multi-stage `ParallelAsync` is **extremely useful** for real-world applications with dependent operations:

✅ **Lower latency for independent I/O** (2.4x in the illustrative 2-stage timings, not a guarantee)
✅ **Type-safe composition** (compiler enforces dependencies)
✅ **Automatic error handling** (short-circuits on failure)
✅ **Clean, readable code** (declarative style)
✅ **Built-in observability** (tracing shows stage execution)

**Key Insight:** Use `ParallelAsync` + `BindAsync` to get the best of both worlds:
- **Parallel** within stages (performance)
- **Sequential** between stages (correctness)

This is the **choreography pattern** in microservices architecture, implemented with Railway-Oriented Programming!
