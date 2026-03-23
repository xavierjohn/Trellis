# TRLS003: Unsafe access to Result.Value

## Cause

Accessing `Result.Value` without first checking `IsSuccess` or using proper guards.

## Rule Description

`Result.Value` throws an `InvalidOperationException` if the `Result` is in a failure state. Accessing it without checking `IsSuccess` first can cause runtime exceptions.

This diagnostic ensures that `Result.Value` is only accessed in safe contexts where the success state has been verified.

## How to Fix Violations

### Option 1: Check IsSuccess First
```csharp
// ❌ Bad - Unsafe access
var customer = result.Value;

// ✅ Good - Guarded access
if (result.IsSuccess)
{
    var customer = result.Value;
    // Use customer...
}

// ✅ Good - Ternary guard is also recognized
var defaultCustomer = GetDefaultCustomer();
var customer = result.IsSuccess ? result.Value : defaultCustomer;
```

### Option 2: Use Match
```csharp
// ✅ Good - Pattern matching
return result.Match(
    onSuccess: customer => customer.ToDto(),
    onFailure: error => DefaultDto());
```

### Option 3: Use TryGetValue
```csharp
// ✅ Good - Safe extraction
if (result.TryGetValue(out var customer))
{
    // Use customer...
}
```

### Option 4: Use GetValueOrDefault
```csharp
// ✅ Good - With fallback
var customer = result.GetValueOrDefault(defaultCustomer);
```

## Code Fix

This diagnostic offers an automatic code fix that wraps the unsafe access in an `if (result.IsSuccess)` guard. The code fix intelligently:
- Wraps all consecutive statements that use `result.Value`
- Tracks variables derived from `result.Value`
- Stops at unrelated statements

### Example Code Fix Transformation

**Before:**
```csharp
var result = GetCustomer();
result.IsSuccess.Should().BeTrue();
Customer customer = result.Value;
customer.Name.Should().Be("John");
customer.Email.Should().Contain("@");
SomeOtherMethod();
```

**After (automatic):**
```csharp
var result = GetCustomer();
result.IsSuccess.Should().BeTrue();
if (result.IsSuccess)
{
    Customer customer = result.Value;
    customer.Name.Should().Be("John");
    customer.Email.Should().Contain("@");
}
SomeOtherMethod();
```

## When to Suppress Warnings

This warning can be suppressed in test code where you're explicitly testing success scenarios and expect the operation to succeed.

```csharp
[Fact]
public void Should_CreateValidCustomer()
{
    var result = Customer.Create("test@example.com");
    #pragma warning disable TRLS003
    var customer = result.Value; // Safe in test - we're verifying success
    #pragma warning restore TRLS003
    customer.Email.Should().Be("test@example.com");
}
```

However, even in tests, using `Match` or checking `IsSuccess` is preferred.

## Special Behavior

**Note:** This diagnostic is suppressed for invocation patterns like `TryCreate().Value`. Those are handled by [TRLS007](TRLS007.md) instead, which suggests using `Create()` for better error messages.

## Related Rules

- [TRLS004](TRLS004.md) - Unsafe access to Result.Error
- [TRLS006](TRLS006.md) - Unsafe access to Maybe.Value
- [TRLS007](TRLS007.md) - Use Create instead of TryCreate().Value
- [TRLS013](TRLS013.md) - Use GetValueOrDefault or Match instead of ternary
