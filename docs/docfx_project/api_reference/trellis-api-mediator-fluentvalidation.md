---
package: Trellis.Mediator.FluentValidation
namespaces: [Trellis.Mediator.FluentValidation]
types: [FluentValidationServiceCollectionExtensions, FluentValidationMessageValidatorAdapter<TMessage>]
version: v3
last_verified: 2026-09-12
audience: [llm]
agent_usage: onDemand
agent_description: "Open when running FluentValidation validators inside the Trellis Mediator validation behavior, by assembly scanning or explicit registration."
---
# Trellis.Mediator.FluentValidation — API Reference

## Header

- **Package:** `Trellis.Mediator.FluentValidation`
- **Namespace:** `Trellis.Mediator.FluentValidation`
- **Purpose:** Plugs FluentValidation validators into the Trellis Mediator validation stage. Provides one DI extension class and one open-generic `IMessageValidator<TMessage>` adapter; no additional pipeline behavior is added.
- **Depends on:** `Trellis.Mediator` (for `IMessageValidator<TMessage>` and the `IMessage` constraint) and `Trellis.FluentValidation` (for `JsonPointerNormalizer` and the standalone `Result<T>` helpers).

> **Why this is a separate package.** `Trellis.FluentValidation` carries the Mediator-agnostic helpers — the `ValidationResult → Result<T>` extensions and the `JsonPointerNormalizer`. The Mediator-specific bits (the adapter and its DI extension) live here so an application that only wants the standalone helpers does not pull in `Trellis.Mediator`. The adapter's behavior, idempotency guarantees, and AOT/trim contract are unchanged from prior versions; only the package and namespace moved.

See also: [recipe-02-command-handler.md](recipes/recipe-02-command-handler.md) — recipes using this package.

## Use this file when

- You want FluentValidation validators to run inside the Trellis Mediator validation behavior.
- You need to register validators by assembly scanning (non-AOT) or explicitly (AOT/trim-safe).
- You need the wire-up rules for combining FluentValidation failures with `IValidate.Validate()` failures into a single `Error.InvalidInput`.

## Patterns Index

| Goal | Canonical API / pattern | See |
|---|---|---|
| Add the FluentValidation adapter without scanning | `services.AddTrellisFluentValidation()` plus explicit `IValidator<T>` registrations | [`FluentValidationServiceCollectionExtensions`](#fluentvalidationservicecollectionextensions) |
| Add the adapter and scan assemblies | `services.AddTrellisFluentValidation(typeof(SomeType).Assembly)` | [`FluentValidationServiceCollectionExtensions`](#fluentvalidationservicecollectionextensions) |
| Keep AOT/trim safety | Use the parameterless adapter overload and register validators explicitly | [`FluentValidationServiceCollectionExtensions`](#fluentvalidationservicecollectionextensions) |
| Understand nested/indexed field paths | FluentValidation names are normalized to RFC 6901 JSON Pointers via [`JsonPointerNormalizer`](trellis-api-fluentvalidation.md#jsonpointernormalizer) | [Pointer normalization](#pointer-normalization-rfc-6901) |

## Common traps

- `AddTrellisFluentValidation()` does not add a second mediator pipeline behavior; it registers `IMessageValidator<TMessage>` so the existing `ValidationBehavior` can aggregate failures.
- The assembly-scanning overload is intentionally not AOT/trim-safe. Use explicit registrations for AOT-sensitive apps.
- Keep primitive-to-value-object parsing at the transport seam; validators should normally validate already-shaped command/value-object inputs.
- The diagnostic log category emitted by the scanning overload is still `"Trellis.FluentValidation"` so existing logging filters continue to work after the package split.

## Types

### `FluentValidationServiceCollectionExtensions`

**Declaration**

```csharp
public static class FluentValidationServiceCollectionExtensions
```

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public static IServiceCollection AddTrellisFluentValidation(this IServiceCollection services)` | `IServiceCollection` | Registers `FluentValidationMessageValidatorAdapter<TMessage>` as the open-generic `IMessageValidator<TMessage>` implementation and calls `AddOptions<ValidationArgsOptions>()` so the adapter receives the application's configured argument allowlist. Every `IValidator<T>` registered for the message in DI then runs inside the existing `ValidationBehavior<TMessage,TResponse>` and contributes its failures to an aggregated `Error.InvalidInput`. **AOT/trim-safe**; uses open-generic DI registration with no reflection. Idempotent — repeated calls do not duplicate the adapter. Throws `ArgumentNullException` when `services` is `null`. Validators must be registered explicitly (e.g., `services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>()`). |
| `public static IServiceCollection AddTrellisFluentValidation(this IServiceCollection services, params Assembly[] assemblies)` | `IServiceCollection` | Calls the parameterless overload, then scans the supplied assemblies for concrete `IValidator<T>` implementations and registers each as a scoped service. **Not AOT or trim-compatible** — annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. Skips abstract/interface/open-generic types. Deduplicates so repeated calls (or overlapping assemblies) do not register the same validator twice. Throws `ArgumentNullException` for null `services`/`assemblies`, and `ArgumentException` when `assemblies` is empty or contains a `null` element. Tolerates `ReflectionTypeLoadException` by using only loadable types and emits a single Warning per affected assembly via `ILoggerFactory` (when one is registered). The diagnostic log category remains `"Trellis.FluentValidation"` for log-filter compatibility. |

### `FluentValidationMessageValidatorAdapter<TMessage>`

**Declaration**

```csharp
public sealed class FluentValidationMessageValidatorAdapter<TMessage>
    : IMessageValidator<TMessage>
    where TMessage : Mediator.IMessage
{
    // Uses ValidationArgsOptions.Default; throws when validators is null.
    public FluentValidationMessageValidatorAdapter(IEnumerable<IValidator<TMessage>> validators);

    // DI selects this overload; throws when validators or argsOptions is null.
    public FluentValidationMessageValidatorAdapter(
        IEnumerable<IValidator<TMessage>> validators,
        Microsoft.Extensions.Options.IOptions<Trellis.FluentValidation.ValidationArgsOptions> argsOptions);
}
```

**Methods**

| Signature | Returns | Description |
| --- | --- | --- |
| `public ValueTask<IResult> ValidateAsync(TMessage message, CancellationToken cancellationToken)` | `ValueTask<IResult>` | Returns `Result.Ok()` when all validators pass or none are registered. Otherwise returns one `Error.InvalidInput` with a `FieldViolation` per failure, preserving order and duplicates. `Detail` carries the FluentValidation message; the sections below describe the pointer, reason code, and args. |

### Pointer normalization (RFC 6901)

FluentValidation property names are converted to JSON Pointers via [`JsonPointerNormalizer`](trellis-api-fluentvalidation.md#jsonpointernormalizer) so they round-trip through `InputPointer`:

| FluentValidation `PropertyName` | Resulting `InputPointer.Path` |
| --- | --- |
| `Email` | `/email` |
| `"Address.PostCode"` | `/address/postCode` |
| `Items[0].Sku` | `/items/0/sku` |

Dotted FluentValidation paths split into separate JSON-pointer segments; bracketed indexers become numeric segments. Other producers (e.g., the ASP integration) build `InputPointer` values directly via `InputPointer.ForProperty(...)`, which does **not** split on `.`, so the normalizer is FluentValidation-specific.

Root-level failures (blank `PropertyName`) use the normalized `typeof(TMessage).Name`.

### Reason-code projection

Each `ValidationFailure.ErrorCode` is projected through [`ValidationCodeProjection.Project`](trellis-api-fluentvalidation.md#validationcodeprojection) before it reaches `FieldViolation.ReasonCode`, so a `MaximumLengthValidator` failure arriving through the Mediator pipeline reports the same `string.max-length` a generated `TryCreate` would. `NotEmptyValidator` is refined against the rejected value — `null` becomes `value.not-null`, a string or collection becomes `value.not-empty`, and a value type left at its default such as `Guid.Empty` or `0` becomes `value.not-default` — because those are three failures a client acts on differently. Custom `WithErrorCode` values pass through verbatim; uncoded `Must(...)` predicates, blank codes, and legacy `validation.error` project to `error.unspecified`.

### Validation argument configuration

`FieldViolation.Args` is projected through `ValidationArgsProjection.Project` with the configured
[`ValidationArgsOptions`](trellis-api-fluentvalidation.md#validationargsoptions). Widen the allowlist
with `services.Configure<ValidationArgsOptions>(options => options.AllowArgs("MinimumAge", "MinAge"))`.
For manually constructed adapters, pass `IOptions<ValidationArgsOptions>` to use the same configuration;
the one-argument constructor intentionally uses the immutable default allowlist.

## Behavioral notes

- FluentValidation does **not** add an additional pipeline behavior. It plugs into the existing `ValidationBehavior<TMessage,TResponse>` via the open-generic `IMessageValidator<TMessage>` extension point.
- The adapter is registered scoped, matching the typical scoped lifetime of FluentValidation validators.
- All validators are awaited sequentially; failures from every validator are aggregated into a single `Error.InvalidInput` rather than short-circuiting on the first failure.
- The adapter forwards the ambient `CancellationToken` to `validator.ValidateAsync`.
- `AddTrellisFluentValidation()` is **idempotent** — calling it multiple times (directly, or via the scanning overload) only registers the open-generic adapter once.
- The assembly-scan overload deduplicates `(serviceType, implementationType)` pairs against existing registrations, so calling it twice with overlapping assemblies will not register a validator more than once.

## Code examples

### Wire FluentValidation into the Mediator pipeline (AOT-safe)

```csharp
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Trellis.Mediator;
using Trellis.Mediator.FluentValidation;

services.AddTrellisBehaviors();
services.AddTrellisFluentValidation();

// Register validators explicitly so the call site is AOT/trim-friendly.
services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderCommandValidator>();
services.AddScoped<IValidator<UpdateOrderCommand>, UpdateOrderCommandValidator>();
```

### Wire FluentValidation with assembly scanning (not AOT-compatible)

```csharp
using Trellis.Mediator.FluentValidation;

services.AddTrellisBehaviors();
services.AddTrellisFluentValidation(typeof(CreateOrderCommandValidator).Assembly);
```

## Cross-references

- [trellis-api-fluentvalidation.md](trellis-api-fluentvalidation.md#header) — the standalone `ValidationResult → Result<T>` helpers and `JsonPointerNormalizer` that this package builds on.
- [trellis-api-mediator.md](trellis-api-mediator.md#validationbehaviortmessage-tresponse) — the pipeline stage this adapter participates in.
- [trellis-api-core.md](trellis-api-core.md#public-closed-record-error) — `Error.InvalidInput` shape.
- [trellis-api-asp.md](trellis-api-asp.md#domain--http-boundary-mapping) — how `Error.InvalidInput` lands on the wire.
