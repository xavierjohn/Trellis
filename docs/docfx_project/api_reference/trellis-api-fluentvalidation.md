---
package: Trellis.FluentValidation
namespaces: [Trellis.FluentValidation]
types: [FluentValidationResultExtensions, JsonPointerNormalizer, ValidationArgsProjection, ValidationArgsOptions, ValidationCodeProjection]
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
| A custom `Must()` rule emits no machine-readable `args` | Opt the placeholder in with `ValidationArgsOptions.AllowArgs(...)` | [`ValidationArgsOptions`](#validationargsoptions) |

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
| `public static Result<T> ToResult<T>(this ValidationResult validationResult, T value, [CallerArgumentExpression(nameof(value))] string paramName = "value")` | `Result<T>` | Returns `Result.Ok(value)` when `validationResult.IsValid` is `true` (does **not** independently reject `null` values). Otherwise emits one `FieldViolation` per `validationResult.Errors` entry and returns `Result.Fail<T>(new Error.InvalidInput(fieldViolations))`. Each FluentValidation failure becomes a `FieldViolation(new InputPointer(JsonPointerNormalizer.ToJsonPointer(rawName)), reasonCode) { Detail = fvMessage }`, where `rawName = string.IsNullOrWhiteSpace(failure.PropertyName) ? paramName : failure.PropertyName` and `reasonCode = ValidationCodeProjection.Project(failure.ErrorCode, failure.AttemptedValue)`, which maps a reserved FluentValidation validator name to its Trellis code, and maps both a blank code and the legacy `validation.error` placeholder to `error.unspecified` — so `validation.error` is never emitted. Multiple failures on the same property produce multiple `FieldViolation` entries (no grouping). Throws `ArgumentNullException` when `validationResult` is `null`. |
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

### `ValidationCodeProjection`

**Declaration**

```csharp
public static class ValidationCodeProjection
```

Translates a FluentValidation `ErrorCode` into the Trellis reason-code vocabulary, so a client keying on `value.not-empty` gets the same code from a FluentValidation rule as from a generated `TryCreate`. Without this, a caller would have to branch on `NotEmptyValidator` for one producer and `value.not-empty` for the other.

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `Project(string? errorCode)` | `string` | The mapped Trellis code for a reserved FluentValidation validator name; `error.unspecified` for a blank code or the legacy `validation.error` placeholder; otherwise `errorCode` **unchanged**. |
| `Project(string? errorCode, object? attemptedValue)` | `string` | As above, but refines `NotEmptyValidator` against the rejected value. Both adapters call this overload. |

**`NotEmpty()` is one rule spanning three codes.** FluentValidation's `NotEmpty()` fails for `null`, for a blank string or empty collection, and for a value type left at its default — three failures the vocabulary deliberately keeps apart. Mapping the rule to a single code would make `RuleFor(x => x.Id).NotEmpty()` report `value.not-empty` for `Guid.Empty` while a Trellis primitive reports `value.not-default` for the same input, which is exactly the producer divergence the vocabulary exists to remove. The code describes the failure, not the rule that caught it:

| `AttemptedValue` | Code |
| --- | --- |
| `null` | `value.not-null` |
| a `string` (including blank) | `value.not-empty` |
| any other `IEnumerable` | `value.not-empty` |
| anything else — `Guid.Empty`, `0`, `default(DateTime)` | `value.not-default` |


The table is keyed on the **error-code string, never the CLR validator type**. The two disagree in practice — `AspNetCoreCompatibleEmailValidator` reports `Name = "EmailValidator"` — so a type-keyed lookup would miss it. Keying on the string also means a caller's `WithErrorCode("EmailValidator")` maps identically to the built-in rule.

| FluentValidation `ErrorCode` | Trellis code |
| --- | --- |
| `NotNullValidator` | `value.not-null` |
| `NotEmptyValidator` | `value.not-null`, `value.not-empty` or `value.not-default` — see below |
| `NullValidator` | `value.must-be-null` |
| `EmptyValidator` | `value.must-be-empty` |
| `EqualValidator` | `value.must-equal` |
| `NotEqualValidator` | `value.must-not-equal` |
| `GreaterThanValidator` | `value.greater-than` |
| `GreaterThanOrEqualValidator` | `value.greater-than-or-equal` |
| `LessThanValidator` | `value.less-than` |
| `LessThanOrEqualValidator` | `value.less-than-or-equal` |
| `InclusiveBetweenValidator` | `value.between-inclusive` |
| `ExclusiveBetweenValidator` | `value.between-exclusive` |
| `LengthValidator` | `string.length` |
| `MinimumLengthValidator` | `string.min-length` |
| `MaximumLengthValidator` | `string.max-length` |
| `ExactLengthValidator` | `string.exact-length` |
| `RegularExpressionValidator` | `string.pattern` |
| `EmailValidator`, `AspNetCoreCompatibleEmailValidator` | `string.email` |
| `CreditCardValidator` | `string.credit-card` |
| `EnumValidator` | `enum.undefined` |
| `ScalePrecisionValidator` | `number.precision` |
| `PredicateValidator`, `AsyncPredicateValidator` | `error.unspecified` |

**A custom `WithErrorCode` passes through verbatim.** A caller who wrote `WithErrorCode("order.too-large")` means it; rewriting it would make `WithErrorCode` useless.

**`Must(...)` maps to the sentinel.** A predicate can express any condition, so its validator name says only "some custom predicate failed" — which is exactly what `error.unspecified` means. Give a `Must(...)` rule a real code with `WithErrorCode`, or a client has nothing to branch on. `TRLS063` reports an uncoded `Must`/`MustAsync` when the project references `Trellis.Analyzers`; see [trellis-api-anti-patterns.md](trellis-api-anti-patterns.md#trls063--fluentvalidation-must-rule-with-no-witherrorcode) for the WRONG/FIX shapes, including where a trailing `WithErrorCode` stops applying to an earlier rule. `TRLS064` covers the other half of the same question — the code you pass to `WithErrorCode` — reporting one that restates a frozen framework code or claims a framework namespace.

### `ValidationArgsProjection`

**Declaration**

```csharp
public static class ValidationArgsProjection
```

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static ImmutableDictionary<string, ValidationArgValue>? Project(ValidationFailure failure, ValidationArgsOptions? options = null)` | `ImmutableDictionary<string,ValidationArgValue>?` | Projects a failure's `FormattedMessagePlaceholderValues` onto the `Args` carried by `FieldViolation`, applying the allowlist, the containment gate and the encoding rules below. Returns `null` when nothing survives. Both adapters call this — `FluentValidationResultExtensions.ToResult` and, in `Trellis.Mediator.FluentValidation`, `FluentValidationMessageValidatorAdapter`. |

`Args` is what lets a client render its own localized message instead of displaying the server's English prose. Blanket camelCase pass-through of FluentValidation's placeholders is unsafe on two independent counts, so two controls apply.

**Correctness — the per-validator allowlist.** FluentValidation populates placeholders its message never uses, with sentinel values: a `MinimumLength(50)` failure carries `MaxLength = -1`, and a `MaximumLength(2)` failure carries `MinLength = 0`. A client rendering *"must be between 50 and -1 characters"* from those is a real bug.

| Validator | Args |
| --- | --- |
| `Length` | `minLength`, `maxLength`, `totalLength` |
| `MinimumLength` | `minLength`, `totalLength` |
| `MaximumLength` | `maxLength`, `totalLength` |
| `ExactLength` | `maxLength`, `totalLength` |
| `Equal`, `NotEqual`, `LessThan`, `LessThanOrEqual`, `GreaterThan`, `GreaterThanOrEqual` | `comparisonValue`, `comparisonProperty` |
| `InclusiveBetween`, `ExclusiveBetween` | `from`, `to` |
| `ScalePrecision` | `expectedPrecision`, `expectedScale`, `actualScale`, `digits` |
| `RegularExpression` | `regularExpression` |
| all others | none |

`ExactLength` allows `maxLength` and **not** `minLength`, which looks arbitrary because `ExactLengthValidator(n)` calls `base(n, n)` and so populates both with the same correct value. The allowlist does not act alone — it composes with the gate below, and the pinned template names `{MaxLength}`. Allowlisting `minLength` would gate it out for being absent from the template while `maxLength` was dropped for not being allowlisted, and the expected length would vanish from the wire entirely, leaving a client with the length it sent and no bound to compare it against. **The allowlist tracks the template, not merely the populated fields.**

**Disclosure — the containment gate.** An arg is emitted only when the **culture-active message template names that placeholder** *and* **its rendered value already appears in the `ErrorMessage`** the client receives anyway. Both halves are required:

- Without the template check, containment is fooled by coincidence — `Matches("A")` on a property named `A` renders *"'A' is not in the correct format"*, in which the pattern is trivially a substring. The collision gets likelier the shorter the arg, which is to say likeliest for numeric thresholds.
- Without the message check, the template check alone still passes after the application replaced the message: `.WithMessage("bad")` leaves the default template and its `{MinLength}` untouched.

The consequences fall out uniformly, with no per-arg judgement:

| Case | Result |
| --- | --- |
| default message | the templated args emit — they are already in today's `errors` string, so nothing new is disclosed |
| `.WithMessage("bad")` | nothing emits — the app took the values out of its prose |
| `AppendArgument("Secret", …)` | nothing emits — in no template, and Trellis cannot classify it |
| localized message (e.g. `culture es`) | the templated args emit, because `GetString` returns the *culture-active* template |
| an app-supplied `ErrorCode` | nothing emits — an unrecognized code resolves to no template, which is fail-safe and consistent with a user-set code always winning |
| `Matches(...)`, any message | `regularExpression` never emits — it is in no default template, so emitting it would disclose an internal format |

`PropertyValue`, `PropertyPath` and `PropertyName` are denied unconditionally, on app-authored placeholders too: `PropertyValue` carries the user's submitted input and *is* rendered into some default messages, so containment alone would let it through. `PropertyName` is redundant with the violation's own location.

**Bounding.** Every string-valued arg is capped at 64 characters (with a `...` marker) and control characters are escaped as `\uXXXX`. The bound is universal rather than targeted because no structural rule identifies which args can carry submitted input: `Equal(x => x.Other + "!")` carries the full submitted value with an **empty** `ComparisonProperty`, byte-for-byte indistinguishable from a safe literal comparison.

**Encoding.** Placeholders arrive boxed rather than pre-stringified, which is what makes per-type encoding implementable.

| Type | Encoding |
| --- | --- |
| `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly` | round-trip `"O"` |
| numerics | invariant culture |
| `TimeSpan` | `"c"` |
| enum | the **name**, not the numeric value |

Naive conversion is not acceptable: `Convert.ToString(dateTime, InvariantCulture)` yields a month-first US format, not ISO 8601.

The gate compares FluentValidation's **rendered** form, because that is what the message contains — but what Trellis publishes is the **encoded** form, and the two differ for dates. So an arg is emitted only when the encoded value is *also* reconcilable with the message: identical to the rendered form, identical to it after bounding, or present in the message verbatim. Otherwise it is **suppressed**.

In practice that makes temporal args a standing false negative, since a culture-rendered date and a round-trip one essentially never coincide. That direction is deliberate — a false negative hides a safe arg and stays recoverable through an explicit opt-in, while a false positive discloses and cannot be taken back. Bounding and escaping are reconciled rather than treated as a mismatch: `Sanitize` derives every character it emits from a character of the value the gate already accepted — truncation omits, escaping re-encodes — so it cannot introduce content the message lacked, even though its output is not byte-for-byte present there. Demanding verbatim presence would suppress exactly the long and control-bearing values the bound exists to serve.

**Wire representation.** The gate above runs entirely on strings and still does, because it decides by comparing against the message FluentValidation rendered, and that message is text. Only after an arg has passed is it lifted onto [`ValidationArgValue`](trellis-api-core.md#validationargs-and-validationargvalue): a placeholder whose CLR type is numeric becomes `ValidationArgValue.Number` and reaches the client as a JSON number, everything else becomes `ValidationArgValue.Text`.

> [!NOTE]
> The lift cannot admit an arg the gate rejected — it runs only on values that already passed. What changes is representation, not eligibility. `maxLength` now arrives as `4`, not `"4"`.

An enum is deliberately **not** numeric for this purpose: it is encoded by name, and a client matching on the name would otherwise be handed an ordinal it cannot interpret. A numeric value whose invariant encoding will not round-trip through `decimal` — a `double` beyond decimal's range, or one rendered in exponent form — stays text rather than losing precision or throwing.

### `ValidationArgsOptions`

**Declaration**

```csharp
public sealed class ValidationArgsOptions
```

**Properties**

| Name | Type | Description |
| --- | --- | --- |
| `Default` | `ValidationArgsOptions` | The static configuration used when the application registered none. Widens nothing, and rejects `AllowArgs` with `InvalidOperationException` because it is shared process-wide. |

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public ValidationArgsOptions AllowArgs(string errorCode, params string[] placeholderNames)` | `ValidationArgsOptions` | Allows the named placeholders — spelled as FluentValidation spells them, in PascalCase — to be emitted for failures carrying `errorCode`. Returns the same instance so calls chain. Throws `ArgumentException` for a blank error code or name, or for a placeholder that can never be allowed, and `InvalidOperationException` when called on `Default`. |

This is the explicit opt-in the two controls above defer to. The default allowlist carries only the operands whose meaning Trellis can vouch for across every validator that populates them, so a rule Trellis did not write — a `Must()` with `context.MessageFormatter.AppendArgument(...)`, or a validator behind a custom `WithErrorCode` — emits no args at all. `AllowArgs` is how the application supplies the knowledge Trellis lacks.

```csharp
services.Configure<ValidationArgsOptions>(options =>
    options.AllowArgs("MinimumAge", "MinAge"));
```

`AddTrellisFluentValidation()` calls `AddOptions<ValidationArgsOptions>()`, so the Mediator pipeline adapter always resolves the configured instance. The standalone `ToResult` / `ValidateToResult` / `ValidateToResultAsync` helpers have no container to read from, so they take the same object through an optional `argsOptions` parameter.

Three properties are worth stating, because each is load-bearing:

- **It only ever widens.** There is no remove operation. The default set is the conservative one, so removing could only narrow a client contract something already depends on — and an application that wants fewer args can stop reading them.
- **An explicit opt-in satisfies the template half of the containment gate without consulting the template.** The template check defends against a placeholder *Trellis guessed* was safe, which is why coincidence can fool it; an application naming its own validator's placeholder is not guessing, and a custom validator has no language-manager entry for the check to consult. Leaving it in force would make the opt-in inert. **The message half still holds**, so an opted-in arg still cannot carry anything the client's own message did not.
- **`PropertyValue` and `PropertyPath` can never be re-admitted.** `AllowArgs` throws rather than silently dropping them, because an application that asked for `PropertyValue` has misunderstood what args are for and a silent drop would leave it waiting for an arg that is never coming. The two are denied for different reasons, and the exception says which: `PropertyValue` carries the submitted input verbatim, a disclosure and PII hazard; `PropertyPath` carries the traversal path the violation's own location already reports.

- **`Default` cannot be widened.** It is shared process-wide, so `ValidationArgsOptions.Default.AllowArgs(...)` throws `InvalidOperationException` rather than applying a global effect from what reads like a local one. Register through `services.Configure<ValidationArgsOptions>(...)`, which hands out an instance of your own.


## Extension methods

### `FluentValidationResultExtensions`

```csharp
public static Result<T> ToResult<T>(
    this ValidationResult validationResult,
    T value,
    [CallerArgumentExpression(nameof(value))] string paramName = "value",
    ValidationArgsOptions? argsOptions = null)

public static Result<T> ValidateToResult<T>(
    this IValidator<T> validator,
    T value,
    [CallerArgumentExpression(nameof(value))] string paramName = "value",
    string? message = null,
    ValidationArgsOptions? argsOptions = null)

public static async Task<Result<T>> ValidateToResultAsync<T>(
    this IValidator<T> validator,
    T value,
    [CallerArgumentExpression(nameof(value))] string paramName = "value",
    string? message = null,
    ValidationArgsOptions? argsOptions = null,
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
