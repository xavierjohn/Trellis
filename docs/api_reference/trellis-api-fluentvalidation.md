# Trellis.FluentValidation — API Reference

## Header

- **Package:** `Trellis.FluentValidation`
- **Namespace:** `Trellis.FluentValidation`
- **Purpose:** Converts FluentValidation results into Trellis `Result<T>` failures backed by `Error.UnprocessableContent` (one `FieldViolation` per FluentValidation failure, with the FluentValidation message in `Detail`).

## Types

### `FluentValidationResultExtensions`

**Declaration**

```csharp
public static class FluentValidationResultExtensions
```

**Constructors**

- None. This is a static class.

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| None | — | This static class exposes no public properties. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static Result<T> ToResult<T>(this ValidationResult validationResult, T value, [CallerArgumentExpression(nameof(value))] string paramName = "value")` | `Result<T>` | Returns `Result.Ok(value)` when `validationResult.IsValid` is `true`; otherwise groups `validationResult.Errors` by property name, substitutes `paramName` for root-level failures, and returns `Result.Fail<T>(new Error.UnprocessableContent(fieldViolations))` where each FluentValidation failure becomes a `FieldViolation(InputPointer.ForProperty(propName), reasonCode) { Detail = fvMessage }`. Throws `ArgumentNullException` when `validationResult` is `null`. |
| `public static Result<T> ValidateToResult<T>(this IValidator<T> validator, T value, [CallerArgumentExpression(nameof(value))] string paramName = "value", string? message = null)` | `Result<T>` | Throws `ArgumentNullException` when `validator` is `null`. If `value is null`, does **not** call `validator.Validate`; instead returns a validation failure for `paramName` using `message ?? $"'{paramName}' must not be empty."`. Otherwise calls `validator.Validate(value)` and forwards to `ToResult(value, paramName)`. |
| `public static async Task<Result<T>> ValidateToResultAsync<T>(this IValidator<T> validator, T value, [CallerArgumentExpression(nameof(value))] string paramName = "value", string? message = null, CancellationToken cancellationToken = default)` | `Task<Result<T>>` | Throws `ArgumentNullException` when `validator` is `null`. If `value is null`, does **not** call `validator.ValidateAsync`; instead returns the same validation failure shape as `ValidateToResult`. Otherwise awaits `validator.ValidateAsync(value, cancellationToken).ConfigureAwait(false)` and forwards to `ToResult(value, paramName)`. |

## Extension methods

### `FluentValidationResultExtensions`

```csharp
public static Result<T> ToResult<T>(
    this ValidationResult validationResult,
    T value,
    [CallerArgumentExpression(nameof(value))] string paramName = "value")

public static Result<T> ValidateToResult<T>(
    this IValidator<T> validator,
    T value,
    [CallerArgumentExpression(nameof(value))] string paramName = "value",
    string? message = null)

public static async Task<Result<T>> ValidateToResultAsync<T>(
    this IValidator<T> validator,
    T value,
    [CallerArgumentExpression(nameof(value))] string paramName = "value",
    string? message = null,
    CancellationToken cancellationToken = default)
```

## Behavioral notes

- The extension methods are stateless; they do not keep shared mutable state or add synchronization.
- Shared validator instances are only as concurrency-safe as the underlying `IValidator<T>` implementation; these helpers do not change that.
- `ToResult<T>` only null-checks `validationResult`; it does not independently reject a `null` `value`.
- Validation failures are converted into `Error.UnprocessableContent` whose `Fields` collection is built from one `FieldViolation` per FluentValidation failure (no grouping; multiple failures on the same property emit multiple violations).
- Grouping rule: `string.IsNullOrWhiteSpace(e.PropertyName) ? paramName : e.PropertyName`.
- `ValidateToResult<T>` and `ValidateToResultAsync<T>` short-circuit `null` input before invoking FluentValidation.
- Null-input failures are created as `new ValidationResult([new ValidationFailure(paramName, message ?? $"'{paramName}' must not be empty.")])`.
- `ValidateToResultAsync<T>` propagates cancellation through `validator.ValidateAsync(value, cancellationToken)`.
- Exceptions from FluentValidation itself are not caught, except for the explicit `ArgumentNullException.ThrowIfNull(...)` guards on `validationResult` and `validator`.

## Code examples

### Convert an existing `ValidationResult`

```csharp
using FluentValidation;
using FluentValidation.Results;
using Trellis;
using Trellis.FluentValidation;

public sealed record CreateUserRequest(string Email);

var validator = new InlineValidator<CreateUserRequest>();
validator.RuleFor(x => x.Email).NotEmpty().EmailAddress();

var request = new CreateUserRequest("invalid-email");
ValidationResult validation = validator.Validate(request);

Result<CreateUserRequest> result = validation.ToResult(request);
```

### Validate directly with sync and async helpers

```csharp
using System.Threading;
using FluentValidation;
using Trellis;
using Trellis.FluentValidation;

public sealed record CreateUserRequest(string Email);

var validator = new InlineValidator<CreateUserRequest>();
validator.RuleFor(x => x.Email).NotEmpty().EmailAddress();

var request = new CreateUserRequest("user@example.com");

Result<CreateUserRequest> syncResult = validator.ValidateToResult(request);
Result<CreateUserRequest> asyncResult =
    await validator.ValidateToResultAsync(request, cancellationToken: CancellationToken.None);
```

### Null input with caller-expression field naming

```csharp
using FluentValidation;
using Trellis;
using Trellis.FluentValidation;

string? alias = null;

var validator = new InlineValidator<string?>();
validator.RuleFor(x => x).NotEmpty();

Result<string?> result = validator.ValidateToResult(alias, message: "Alias is required.");
```

## Cross-references

- [trellis-api-core.md](trellis-api-core.md)
- [trellis-api-asp.md](trellis-api-asp.md)
- [trellis-api-mediator.md](trellis-api-mediator.md)
