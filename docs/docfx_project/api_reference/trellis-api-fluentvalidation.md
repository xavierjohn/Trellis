---
package: Trellis.FluentValidation
namespaces: [Trellis.FluentValidation]
types: [FluentValidationResultExtensions, JsonPointerNormalizer]
version: v3
last_verified: 2026-06-04
audience: [llm]
---
# Trellis.FluentValidation — API Reference

## Header

- **Package:** `Trellis.FluentValidation`
- **Namespace:** `Trellis.FluentValidation`
- **Purpose:** Mediator-agnostic FluentValidation helpers for Trellis:
  1. **Standalone helpers** — `FluentValidationResultExtensions` converts a `ValidationResult` (or runs an `IValidator<T>` synchronously/asynchronously) into a `Result<T>` failure backed by `Error.InvalidInput`.
  2. **Pointer normalization** — `JsonPointerNormalizer.ToJsonPointer(...)` projects FluentValidation member-chain property names (`Address.PostCode`, `Items[0].Sku`) into camelCase RFC 6901 JSON Pointers (`/address/postCode`, `/items/0/sku`) so they round-trip through Trellis `InputPointer` values.

> **v3 package split.** The Mediator integration (`AddTrellisFluentValidation()` + `FluentValidationMessageValidatorAdapter<TMessage>`) moved to the new `Trellis.Mediator.FluentValidation` package so consumers of these standalone helpers do not have to take a Mediator dependency. See [trellis-api-mediator-fluentvalidation.md](trellis-api-mediator-fluentvalidation.md#header) for the adapter API.

See also: [trellis-api-cookbook.md](trellis-api-cookbook.md#recipe-2--command--handler--fluentvalidation--ef-persistence) — recipes using these helpers.

## Use this file when

- You need to convert a FluentValidation `ValidationResult` into `Result<T>` / `Error.InvalidInput` outside the Mediator pipeline.
- You need the exact JSON Pointer normalization rules for FluentValidation property names (e.g., for a custom adapter that produces `InputPointer` values from FluentValidation failures).
- You want to use FluentValidation in a domain or worker project that does not reference `Trellis.Mediator`.

For wiring FluentValidation validators into the Trellis Mediator validation stage, see [trellis-api-mediator-fluentvalidation.md](trellis-api-mediator-fluentvalidation.md#use-this-file-when).

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Convert `ValidationResult` to `Result<T>` | `validationResult.ToResult(value)` | [`FluentValidationResultExtensions`](#fluentvalidationresultextensions) |
| Validate a value outside Mediator | `validator.ValidateToResult(value)` / `ValidateToResultAsync(...)` | [`FluentValidationResultExtensions`](#fluentvalidationresultextensions) |
| Normalize a FluentValidation property name into a JSON pointer | `JsonPointerNormalizer.ToJsonPointer(propertyName)` | [`JsonPointerNormalizer`](#jsonpointernormalizer) |
| Wire FluentValidation into the Mediator pipeline | `services.AddTrellisFluentValidation()` from `Trellis.Mediator.FluentValidation` | [trellis-api-mediator-fluentvalidation.md](trellis-api-mediator-fluentvalidation.md#fluentvalidationservicecollectionextensions) |

## Common traps

- Keep primitive-to-value-object parsing at the transport seam; validators should normally validate already-shaped command/value-object inputs.
- `ToResult<T>` only null-checks `validationResult`; it does not independently reject a `null` `value`.
- `ValidateToResultAsync<T>` observes `cancellationToken` BEFORE the null-value short-circuit, so a cancelled token always wins over the synchronous null-input fallback.
- `JsonPointerNormalizer.ToJsonPointer` splits FluentValidation dotted chains (`Address.City` → `/address/city`). The general-purpose `InputPointer.ForProperty(string)` does **not** split on `.` (it only escapes `~` → `~0` and `/` → `~1` per RFC 6901 §3). The dotted-chain normalization is FluentValidation-specific.

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
| `public static Result<T> ToResult<T>(this ValidationResult validationResult, T value, [CallerArgumentExpression(nameof(value))] string paramName = "value")` | `Result<T>` | Returns `Result.Ok(value)` when `validationResult.IsValid` is `true` (does **not** independently reject `null` values). Otherwise emits one `FieldViolation` per `validationResult.Errors` entry and returns `Result.Fail<T>(new Error.InvalidInput(fieldViolations))`. Each FluentValidation failure becomes a `FieldViolation(new InputPointer(JsonPointerNormalizer.ToJsonPointer(rawName)), reasonCode) { Detail = fvMessage }`, where `rawName = string.IsNullOrWhiteSpace(failure.PropertyName) ? paramName : failure.PropertyName` and `reasonCode = string.IsNullOrWhiteSpace(failure.ErrorCode) ? "validation.error" : failure.ErrorCode`. Multiple failures on the same property produce multiple `FieldViolation` entries (no grouping). Throws `ArgumentNullException` when `validationResult` is `null`. |
| `public static Result<T> ValidateToResult<T>(this IValidator<T> validator, T value, [CallerArgumentExpression(nameof(value))] string paramName = "value", string? message = null)` | `Result<T>` | Throws `ArgumentNullException` when `validator` is `null`. If `value is null`, does **not** call `validator.Validate`; instead returns a validation failure for `paramName` using `message ?? $"'{paramName}' must not be empty."`. Otherwise calls `validator.Validate(value)` and forwards to `ToResult(value, paramName)`. |
| `public static async Task<Result<T>> ValidateToResultAsync<T>(this IValidator<T> validator, T value, [CallerArgumentExpression(nameof(value))] string paramName = "value", string? message = null, CancellationToken cancellationToken = default)` | `Task<Result<T>>` | Throws `ArgumentNullException` when `validator` is `null`. Observes `cancellationToken` BEFORE the null-value short-circuit, so a cancelled token always wins over the synchronous fallback path. If `value is null`, does **not** call `validator.ValidateAsync`; instead returns the same validation failure shape as `ValidateToResult`. Otherwise awaits `validator.ValidateAsync(value, cancellationToken).ConfigureAwait(false)` and forwards to `ToResult(value, paramName)`. |

### `JsonPointerNormalizer`

**Declaration**

```csharp
public static class JsonPointerNormalizer
```

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static string ToJsonPointer(string? propertyName)` | `string` | Converts a FluentValidation `PropertyName` (e.g., `Address.PostCode`, `Items[0].Sku`) into a camelCase RFC 6901 JSON Pointer (`/address/postCode`, `/items/0/sku`) — each name segment's first character is lower-cased; indexer segments are unchanged. Returns `""` for `null` or empty input. Inputs that already start with `/` are assumed to already be pointers and are returned unchanged. Inside each segment, `~` is escaped to `~0` and `/` to `~1` per RFC 6901 §3. Indexer contents (`[...]`) are treated as standalone segments — `Items[0]` becomes `/items/0`. |

**Pointer normalization (RFC 6901) — examples**

| FluentValidation `PropertyName` | `ToJsonPointer` result |
| --- | --- |
| `""` or `null` | `""` |
| `Email` | `/email` |
| `Address.PostCode` | `/address/postCode` |
| `Items[0].Sku` | `/items/0/sku` |
| `/already/a/pointer` | `/already/a/pointer` (returned unchanged) |
| `Field~Name` | `/field~0Name` |
| `Path/With/Slash` | `/path~1With~1Slash` |

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

### Standalone helpers (`FluentValidationResultExtensions`)

- The extension methods are stateless; they do not keep shared mutable state or add synchronization.
- Shared validator instances are only as concurrency-safe as the underlying `IValidator<T>` implementation; these helpers do not change that.
- `ToResult<T>` only null-checks `validationResult`; it does not independently reject a `null` `value`.
- Validation failures are converted into `Error.InvalidInput` whose `Fields` collection is built from one `FieldViolation` per FluentValidation failure (no grouping; multiple failures on the same property emit multiple violations).
- Field-name selection rule: `string.IsNullOrWhiteSpace(e.PropertyName) ? paramName : e.PropertyName` (FluentValidation root-level failures fall back to the caller-captured `paramName`).
- `ValidateToResult<T>` and `ValidateToResultAsync<T>` short-circuit `null` input before invoking FluentValidation.
- Null-input failures are created as `new ValidationResult([new ValidationFailure(paramName, message ?? $"'{paramName}' must not be empty.")])`.
- `ValidateToResultAsync<T>` observes `cancellationToken` BEFORE the null-value short-circuit (so a cancelled token always wins over the synchronous fallback) AND propagates cancellation through `validator.ValidateAsync(value, cancellationToken)`.
- Exceptions from FluentValidation itself are not caught, except for the explicit `ArgumentNullException.ThrowIfNull(...)` guards on `validationResult` and `validator`.

### `JsonPointerNormalizer`

- `ToJsonPointer` is a pure, allocation-light projection. It does not validate that the input is a syntactically well-formed FluentValidation property chain — malformed inputs simply produce the most permissive segmentation the loop can derive.
- For inputs that already look like JSON pointers (start with `/`), the method short-circuits and returns the input unchanged so a pre-formed pointer (e.g., one produced by `InputPointer.ForProperty(...)`) is preserved verbatim.

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

### Project a FluentValidation property name into an `InputPointer`

```csharp
using Trellis;
using Trellis.FluentValidation;

// Custom FluentValidation projection that needs to build an InputPointer
// without going through the Mediator adapter.
var pointer = new InputPointer(JsonPointerNormalizer.ToJsonPointer("Items[0].Sku"));
// pointer.RawValue == "/items/0/sku"
```

## Cross-references

- [trellis-api-mediator-fluentvalidation.md](trellis-api-mediator-fluentvalidation.md#header) — the Mediator integration (`AddTrellisFluentValidation` + adapter) that builds on `JsonPointerNormalizer`.
- [trellis-api-core.md](trellis-api-core.md#public-abstract-record-error) — `Error.InvalidInput` shape and `InputPointer` semantics.
- [trellis-api-asp.md](trellis-api-asp.md#domain--http-boundary-mapping) — how `Error.InvalidInput` lands on the wire.
- [trellis-api-mediator.md](trellis-api-mediator.md#validationbehaviortmessage-tresponse) — the pipeline stage the Mediator adapter participates in.
