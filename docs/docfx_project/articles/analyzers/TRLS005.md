# TRLS005: Consider using MatchError for error type discrimination

## Cause

Using switch statements or if/else chains to discriminate between error types when `MatchError` provides a better, type-safe alternative.

## Rule Description

When you need to handle different error types differently, `MatchError` provides type-safe pattern matching with compile-time checking and a required fallback for unhandled types.

## How to Fix Violations

Replace switch/if statements with `MatchError`:

```csharp
// ❌ Less ideal - Manual switch
if (result.IsFailure)
{
    switch (result.Error)
    {
        case ValidationError ve:
            return BadRequest(ve.Detail);
        case NotFoundError nfe:
            return NotFound(nfe.Detail);
        default:
            return StatusCode(500, result.Error.Detail);
    }
}

// ✅ Better - Type-safe MatchError
return result.MatchError(
    onValidationError: ve => BadRequest(ve.Detail),
    onNotFoundError: nfe => NotFound(nfe.Detail),
    onOtherError: error => StatusCode(500, error.Detail));
```

## Benefits of MatchError

1. **Type Safety**: Each error handler gets the correctly typed error
2. **Exhaustiveness**: Fallback handler is required
3. **Readability**: Clear intent for error handling
4. **Maintainability**: Easy to add new error type handlers

## Example

```csharp
public IActionResult UpdateCustomer(Guid id, UpdateCustomerDto dto)
{
    return customerService.UpdateCustomer(id, dto)
        .Match(
            onSuccess: customer => Ok(customer.ToDto()),
            onFailure: error => error.ToHttpResult());
}

public static class ErrorExtensions
{
    public static IActionResult ToHttpResult(this Error error) =>
        error.MatchError(
            onValidationError: ve => new BadRequestObjectResult(ve.Detail),
            onNotFoundError: nfe => new NotFoundObjectResult(nfe.Detail),
            onUnauthorizedError: ue => new UnauthorizedObjectResult(ue.Detail),
            onOtherError: e => new ObjectResult(e.Detail) { StatusCode = 500 });
}
```

## When to Suppress Warnings

This is a suggestion-level diagnostic (Info severity). Suppress it if:
- You prefer explicit switch statements
- You're only checking for one specific error type
- The error handling logic is complex and doesn't fit the `MatchError` pattern

## Related Rules

- [TRLS004](TRLS004.md) - Unsafe access to Result.Error
- [TRLS010](TRLS010.md) - Use specific error type instead of base Error class
