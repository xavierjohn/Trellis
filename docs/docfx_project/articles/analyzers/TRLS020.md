# TRLS020 — Composite value object DTO property is not safely deserializable

- **Severity:** Warning
- **Category:** Trellis

## What it detects

Flags request/response DTO properties whose type is a `[OwnedEntity]`-decorated composite value object (inherits from `ValueObject`) when the property surface is not safely deserializable via `System.Text.Json`:

- A bare composite VO property exposed by a DTO (`public ShippingAddress Address { get; init; }`) that is **not** decorated with `[JsonConverter(typeof(CompositeValueObjectJsonConverter<ShippingAddress>))]` on the type.
- A `Maybe<TComposite>` property on a DTO. Trellis ships no `MaybeCompositeValueObjectJsonConverterFactory`, so STJ has no path to round-trip the optional-wrapped composite, even when the inner composite is correctly converter-decorated.

The rule is scoped to `[OwnedEntity]` composites — scalar value objects, plain `ValueObject`-derived types without `[OwnedEntity]`, and non-VO types are out of scope (they use a different transport).

## Why it matters

Composite value objects validate their invariants in `TryCreate`. If STJ falls back to default construction (parameterless constructor + property setters), every `TryCreate` rule is bypassed and an invalid composite slips into the domain. The two safe shapes are:

- **Bare composite on a DTO** → decorate the *type* with `[JsonConverter(typeof(CompositeValueObjectJsonConverter<T>))]`. The converter routes deserialization through `T.TryCreate` and surfaces validation failures as `JsonException`.
- **Optional composite on a DTO** → use `TComposite?` plus `Maybe.From(...)` at the endpoint / API seam (cookbook Recipe 14). Trellis does not support `Maybe<TComposite>` directly on DTOs.

## Bad examples

```csharp
// Composite VO must be [OwnedEntity]; this is the canonical Trellis composite-VO shape.
[OwnedEntity]
public sealed partial class ShippingAddress : ValueObject { /* ... TryCreate, properties ... */ }

// Bare composite without the type-level converter → STJ default-constructs and bypasses TryCreate.
public sealed record CreateCustomerRequest(string Email, ShippingAddress Address);       // TRLS020

// Optional composite as Maybe<T> on a DTO → no factory, silent failure mode.
public sealed record UpdateCustomerRequest(Maybe<ShippingAddress> Address);              // TRLS020
```

## Good examples

```csharp
// Bare composite — converter on the type, applies to every DTO that surfaces it.
[OwnedEntity]
[JsonConverter(typeof(CompositeValueObjectJsonConverter<ShippingAddress>))]
public sealed partial class ShippingAddress : ValueObject { /* ... */ }

public sealed record CreateCustomerRequest(string Email, ShippingAddress Address);

// Optional composite — nullable transport at the seam, lift to Maybe inside the handler.
public sealed record UpdateCustomerRequest(ShippingAddress? Address);

public ValueTask<Result<Customer>> Handle(UpdateCustomerCommand c, ...) =>
    /* ... command construction lifts `Maybe.From(request.Address)` */;
```

## Code fix available

No. The right transport depends on whether the composite is optional. See [cookbook Recipe 14](../../api_reference/skills/recipe-14-optional-dto-fields.md) for the canonical optional-field pattern at the API seam.

## Configuration

Standard Roslyn configuration applies.

```ini
dotnet_diagnostic.TRLS020.severity = none
```
