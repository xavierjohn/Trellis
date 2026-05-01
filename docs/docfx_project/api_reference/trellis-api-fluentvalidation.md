---
package: Trellis.FluentValidation
namespaces: [Trellis.FluentValidation]
types: [TrellisValidator<T>, ValidationResultExtensions]
version: v3
last_verified: 2026-05-01
audience: [llm, developer]
---
# Trellis.FluentValidation — API Reference

## Header

- **Package:** `Trellis.FluentValidation`
- **Namespace:** `Trellis.FluentValidation`
- **Purpose:** Two integration paths for FluentValidation in Trellis:
  1. **Mediator integration** — `AddTrellisFluentValidation()` plugs FluentValidation validators into the existing `ValidationBehavior<TMessage,TResponse>` via the open-generic `IMessageValidator<TMessage>` adapter. No additional pipeline behavior is added.
  2. **Standalone helpers** — `FluentValidationResultExtensions` converts a `ValidationResult` (or runs an `IValidator<T>` synchronously/asynchronously) into a `Result<T>` failure backed by `Error.UnprocessableContent`.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md) — recipes using this package.

## Use this file when

- You want FluentValidation validators to run inside the Trellis Mediator validation behavior.
- You need to convert a FluentValidation `ValidationResult` into `Result<T>` / `Error.UnprocessableContent`.
- You need exact JSON Pointer normalization behavior for FluentValidation property names.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Add the FluentValidation adapter without scanning | `services.AddTrellisFluentValidation()` plus explicit `IValidator<T>` registrations | [`FluentValidationServiceCollectionExtensions`](#fluentvalidationservicecollectionextensions) |
| Add the adapter and scan assemblies | `services.AddTrellisFluentValidation(typeof(SomeType).Assembly)` | [`FluentValidationServiceCollectionExtensions`](#fluentvalidationservicecollectionextensions) |
| Keep AOT/trim safety | Use the parameterless adapter overload and register validators explicitly | [`FluentValidationServiceCollectionExtensions`](#fluentvalidationservicecollectionextensions) |
| Convert `ValidationResult` to `Result<T>` | `validationResult.ToResult(value)` | [`FluentValidationResultExtensions`](#fluentvalidationresultextensions) |
| Validate a value outside Mediator | `validator.ValidateToResult(value)` / `ValidateToResultAsync(...)` | [`FluentValidationResultExtensions`](#fluentvalidationresultextensions) |
| Understand nested/indexed field paths | FluentValidation names are normalized to RFC 6901 JSON Pointers | [Pointer normalization](#pointer-normalization-rfc-6901) |

## Common traps

- `AddTrellisFluentValidation()` does not add a second mediator pipeline behavior; it registers `IMessageValidator<TMessage>` so the existing `ValidationBehavior` can aggregate failures.
- The assembly-scanning overload is intentionally not AOT/trim-safe. Use explicit registrations for AOT-sensitive apps.
- Keep primitive-to-value-object parsing at the transport seam; validators should normally validate already-shaped command/value-object inputs.

## Types

### `FluentValidationServiceCollectionExtensions`

**Declaration**

```csharp
public static class FluentValidationServiceCollectionExtensions
```

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddTrellisFluentValidation(this IServiceCollection services)` | `IServiceCollection` | Registers `FluentValidationMessageValidatorAdapter<TMessage>` as the open-generic `IMessageValidator<TMessage>` implementation. Every `IValidator<T>` registered for the message in DI then runs inside the existing `ValidationBehavior<TMessage,TResponse>` and contributes its failures to an aggregated `Error.UnprocessableContent`. **AOT/trim-safe**; uses open-generic DI registration with no reflection. Idempotent — repeated calls do not duplicate the adapter. Throws `ArgumentNullException` when `services` is `null`. Validators must be registered explicitly (e.g., `services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>()`). |
| `public static IServiceCollection AddTrellisFluentValidation(this IServiceCollection services, params Assembly[] assemblies)` | `IServiceCollection` | Calls the parameterless overload, then scans the supplied assemblies for concrete `IValidator<T>` implementations and registers each as a scoped service. **Not AOT or trim-compatible** — annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. Skips abstract/interface/open-generic types. Deduplicates so repeated calls (or overlapping assemblies) do not register the same validator twice. Throws `ArgumentNullException` for null `services`/`assemblies`, and `ArgumentException` when `assemblies` is empty or contains a `null` element. Tolerates `ReflectionTypeLoadException` by using only loadable types. |

### `FluentValidationMessageValidatorAdapter<TMessage>`

**Declaration**

```csharp
public sealed class FluentValidationMessageValidatorAdapter<TMessage>(
    IEnumerable<IValidator<TMessage>> validators)
    : IMessageValidator<TMessage>
    where TMessage : Mediator.IMessage
```

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public ValueTask<IResult> ValidateAsync(TMessage message, CancellationToken cancellationToken)` | `ValueTask<IResult>` | Runs every injected `IValidator<TMessage>` against `message`. Returns `Result.Ok()` when all validators pass (or none are registered — the empty injected sequence allocates no violations). Otherwise aggregates every `ValidationFailure` into a single `new Error.UnprocessableContent(EquatableArray.Create(violations))`, where `violations` is the collected `FieldViolation` set. Each FluentValidation failure becomes a `FieldViolation(new InputPointer(pointerPath), reasonCode) { Detail = failure.ErrorMessage }`. `pointerPath` is derived by `JsonPointerNormalizer.ToJsonPointer` from the FV property name; `reasonCode` defaults to `"validation.error"` when `failure.ErrorCode` is null/whitespace. Root-level failures (whitespace `PropertyName`) use `typeof(TMessage).Name`. |

### Pointer normalization (RFC 6901)

FluentValidation property names are converted to JSON Pointers so they round-trip through `InputPointer`:

| FluentValidation `PropertyName` | Resulting `InputPointer.RawValue` |
| --- | --- |
| `Email` | `/Email` |
| `Address.PostCode` | `/Address/PostCode` |
| `Items[0].Sku` | `/Items/0/Sku` |

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

### Mediator integration (`AddTrellisFluentValidation` + adapter)

- FluentValidation does **not** add an additional pipeline behavior. It plugs into the existing `ValidationBehavior<TMessage,TResponse>` via the open-generic `IMessageValidator<TMessage>` extension point.
- The adapter is registered scoped, matching the typical scoped lifetime of FluentValidation validators.
- When no `IValidator<TMessage>` is registered for a message type, `IEnumerable<IValidator<TMessage>>` is empty, the adapter returns `Result.Ok()`, and no allocations are performed.
- All validators are awaited sequentially; failures from every validator are aggregated into a single `Error.UnprocessableContent` rather than short-circuiting on the first failure.
- The adapter forwards the ambient `CancellationToken` to `validator.ValidateAsync`.
- `AddTrellisFluentValidation()` is **idempotent** — calling it multiple times (directly, or via the scanning overload) only registers the open-generic adapter once.
- The assembly-scan overload deduplicates `(serviceType, implementationType)` pairs against existing registrations, so calling it twice with overlapping assemblies will not register a validator more than once.

### Standalone helpers (`FluentValidationResultExtensions`)

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

### Wire FluentValidation into the Mediator pipeline (AOT-safe)

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Trellis.FluentValidation;
using Trellis.Mediator;

services.AddTrellisBehaviors();
services.AddTrellisFluentValidation();

// Register validators explicitly so the call site is AOT/trim-friendly.
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
services.AddScoped<IValidator<UpdateOrderCommand>, UpdateOrderCommandValidator>();
```

### Wire FluentValidation with assembly scanning (not AOT-compatible)

```csharp
using Trellis.FluentValidation;

services.AddTrellisBehaviors();
services.AddTrellisFluentValidation(typeof(CreateOrderCommandValidator).Assembly);
```

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
