# Trellis.FluentValidation — API Reference

> Part of the [Trellis API Reference](.). See also: trellis-api-results.md, trellis-api-mediator.md.

**Package:** `Trellis.FluentValidation` | **Namespace:** `Trellis.FluentValidation`

```csharp
// Convert ValidationResult to Result<T>
Result<T> ToResult<T>(this ValidationResult validationResult, T value)

// Direct validate-and-return
Result<T> ValidateToResult<T>(this IValidator<T> validator, T value)
Task<Result<T>> ValidateToResultAsync<T>(this IValidator<T> validator, T value, CancellationToken cancellationToken = default)
```

---
