# TRLS001: Result return value is not handled

## Cause

A method returns a `Result<T>`, but the return value is not assigned to a variable or used in a chain operation. This means error information may be lost.

## Rule Description

`Result<T>` is designed to enforce error handling through the type system. When a method returns a `Result<T>`, it signals that the operation might fail and the caller must handle both success and failure cases.

Ignoring the return value defeats the purpose of using `Result<T>` and can lead to silent failures.

## How to Fix Violations

Handle the returned `Result<T>` in one of these ways:

### Option 1: Assign to a Variable
```csharp
// ❌ Bad - Result is ignored
customer.UpdateEmail(newEmail);

// ✅ Good - Result is captured
var result = customer.UpdateEmail(newEmail);
if (result.IsFailure)
    return result.Error;
```

### Option 2: Use in a Chain
```csharp
// ✅ Good - Result is chained
return customer.UpdateEmail(newEmail)
    .Bind(c => c.UpdatePhone(newPhone))
    .Map(c => c.ToDto());
```

### Option 3: Use Match
```csharp
// ✅ Good - Result is matched
customer.UpdateEmail(newEmail)
    .Match(
        onSuccess: c => Console.WriteLine("Updated!"),
        onFailure: error => Console.WriteLine($"Failed: {error}"));
```

## When to Suppress Warnings

Do not suppress this warning. If you truly want to ignore the result, use `Discard()` to make the intent explicit:

```csharp
customer.UpdateEmail(newEmail).Discard(); // Explicit — intentionally ignoring the outcome
```

For async pipelines, use `DiscardAsync()`:

```csharp
await GetCustomerAsync(id)
    .BindAsync(c => c.UpdateEmail(newEmail))
    .DiscardAsync(); // Explicit — best-effort operation
```

Alternatively, you can use the discard operator:

```csharp
_ = customer.UpdateEmail(newEmail); // Explicit discard
```

## Related Rules

- [TRLS003](TRLS003.md) - Unsafe access to Result.Value
- [TRLS004](TRLS004.md) - Unsafe access to Result.Error
