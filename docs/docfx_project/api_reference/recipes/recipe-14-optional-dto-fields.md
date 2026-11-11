---
name: recipe-14-optional-dto-fields
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Compares Maybe of a scalar with nullable transport for optional DTO fields. Use when adding optional request or response fields."
---
# Recipe 14 — Optional fields in request DTOs: `Maybe<TScalar>` vs nullable transport

**Requires:** `Trellis.Core`, plus `Trellis.Asp`, `Trellis.Mediator`, `Trellis.Primitives`. `ShippingAddress` is the composite from [Recipe 13](recipe-13-composite-value-object.md), whose `[OwnedEntity]` attribute needs `Trellis.EntityFrameworkCore`; `CreateCustomerCommand` and `CustomerResponse` are application-owned.

## Contents

- [Pattern A — scalar `Maybe<T>` directly on the DTO](#pattern-a--scalar-maybet-directly-on-the-dto)
- [Pattern B — composite owned VO, nullable transport + controller-seam adapter](#pattern-b--composite-owned-vo-nullable-transport--controller-seam-adapter)
- [Anti-pattern → fix](#anti-pattern--fix)

**Problem.** A request body has an optional field — say `phoneNumber` on `CreateCustomerRequest`. The domain models it as `Maybe<PhoneNumber>` (the canonical Trellis pattern). What does the DTO declare it as?

The answer depends on whether the inner type is a **scalar** (single-primitive) value object or a **composite** owned value object. Trellis ships a JSON converter + model binder for the scalar case but not the composite case.

| Inner type | Pattern | Why |
|---|---|---|
| `Maybe<TScalar>` where `TScalar : class, IScalarValue<TScalar, TPrimitive>` (e.g., `Maybe<EmailAddress>`, `Maybe<PhoneNumber>`) | **Use `Maybe<T>` directly on the DTO.** | `AddScalarValueValidation()` (or the convenience `AddTrellisAspWithScalarValidation()`) registers `MaybeScalarValueJsonConverterFactory` (JSON) and `MaybeModelBinder<T,P>` (route/query/header); MVC child-validation suppression for `None` scalar-maybe values is handled internally by the Trellis MVC integration. Register it with the other services; its order relative to `AddControllers()` does not matter. `null`/missing → `None`; valid → `Maybe.From(validated)`; invalid → ProblemDetails with the same field path the domain emits. Custom struct scalar wrappers require an application-owned converter or a DTO adapter; Core's ability to hold them in `Maybe<T>` does not remove the transport converters' class constraint. |
| `Maybe<TComposite>` where `TComposite : ValueObject` with multiple fields (e.g., `Maybe<ShippingAddress>`) | **Use a nullable transport (`TComposite?`) and adapt at the controller seam.** | No JSON converter exists for `Maybe<TComposite>`: the factory only matches scalar value objects, so System.Text.Json treats the struct as a plain object and reads an object payload or an omitted property as `None`, bypassing `TryCreate` (an explicit JSON `null` is a 400). Wrap with `Maybe.From(...)` inside the controller. |
| `Maybe<TPrimitive>` (e.g., `Maybe<int>`, `Maybe<long>`, `Maybe<string>`, `Maybe<Guid>`, `Maybe<DateTime>`) | **Use `Maybe<T>` directly on the DTO.** | `AddScalarValueValidation()` (or the convenience `AddTrellisAspWithScalarValidation()`) registers `MaybePrimitiveJsonConverterFactory` (JSON) and `MaybePrimitiveModelBinder<T>` (route/query/header). Same closed-primitive allowed list as `CompositeValueObjectJsonConverter` (`string`, `decimal`, `int`, `long`, `short`, `byte`, `double`, `float`, `bool`, `Guid`, `DateTime`, `DateTimeOffset`). `null`/missing → `None`; valid primitive → `Maybe.From(value)`. If the primitive carries domain meaning, you may still prefer wrapping it in a scalar value object (e.g., `Age : RequiredInt<Age>`) for the wire-time validation `TryCreate` provides; both shapes are factory-handled. |
| `Maybe<TUnsupportedPrimitive>` (e.g., `Maybe<DateOnly>`, `Maybe<TimeOnly>`, `Maybe<uint>`) | **Use `TUnsupportedPrimitive?` on the DTO and `.AsMaybe()` at the seam.** | These types are outside both the composite-VO converter allowed list and the `Maybe<TPrimitive>` factory allowed list. The wire-shape DTO + adapter at the controller seam is the same pattern as `Maybe<TComposite>`. |

> **The same DTO pattern applies inside a composite VO.** If a *composite value object's interior* contains `Maybe<TPrimitive>` / arrays / collections, `CompositeValueObjectJsonConverter` rejects them too (see [Recipe 13](recipe-13-composite-json-and-collections.md) §"Supported property shapes inside a composite VO"). Keep the composite VO clean as a domain type and declare a wire-shape DTO with nullable transports, then lift on inbound (`.AsMaybe()` / `Maybe.From(...)`) and project on outbound (`.AsNullable()`).

> **Nested collections in DTOs need `TraverseAll`, not inline `.Match`.** When a DTO carries a list of items each of which becomes a value object — for example `IReadOnlyList<MenuSectionDto>` → `IReadOnlyList<MenuSection>` — use [Recipe 20](recipe-20-fail-fast-vs-accumulating.md) (`TraverseAll` / `SequenceAll`) to accumulate per-item validation failures into one `Error.InvalidInput`. Inlining `.Select(s => s.ToDomain().Match(c => c, e => throw …))` throws on the first invalid row and surfaces as HTTP 500 instead of HTTP 422 with per-field violations.

## Pattern A — scalar `Maybe<T>` directly on the DTO

```csharp
using Trellis;
using Trellis.Primitives;

// EmailAddress and PhoneNumber are the shipped, validating value objects from Trellis.Primitives
// (PhoneNumber validates E.164, EmailAddress validates format). Use them — do NOT redeclare bare
// RequiredString subclasses, which are lenient (reject null only) and would skip format validation.

public sealed record CreateCustomerRequest(
    EmailAddress         Email,           // required
    Maybe<PhoneNumber>   PhoneNumber);    // optional — null/missing JSON → Maybe.None

[ApiController]
[Route("customers")]
public sealed class CustomersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public ValueTask<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request, CancellationToken ct) =>
        sender.Send(new CreateCustomerCommand(request.Email, request.PhoneNumber), ct)
              .ToHttpResponseAsync(CustomerResponse.From)
              .AsActionResultAsync<CustomerResponse>();
}
```

`AddTrellisAspWithScalarValidation()` registers the converters and binders, and `app.UseScalarValueValidation()` collects the field errors so their pointers are not doubled:

```csharp
services.AddTrellisAspWithScalarValidation();   // MaybeScalarValueJsonConverterFactory + MaybePrimitiveJsonConverterFactory + MaybeModelBinder + MaybePrimitiveModelBinder + ValidationVisitor patch
services.AddControllers();

// after building the app, before MapControllers():
app.UseScalarValueValidation();
```

Without the middleware an invalid phone still returns 422, but the pointer is `/phoneNumber/phoneNumber` (and `/email/emailAddress` for a bad top-level scalar).

Send `{"email":"a@b.com","phoneNumber":null}` (or omit `phoneNumber` entirely) → handler receives `Maybe<PhoneNumber>.None`. Send `{"email":"a@b.com","phoneNumber":"not a phone"}` → 422 with field path `/phoneNumber` and the validation message produced by `PhoneNumber.TryCreate`.

## Pattern B — composite owned VO, nullable transport + controller-seam adapter

```csharp
public sealed record CreateCustomerRequest(
    EmailAddress       Email,
    ShippingAddress?   ShippingAddress);   // nullable transport — NOT Maybe<ShippingAddress>

public sealed class CustomersController(ISender sender) : ControllerBase
{
    [HttpPost]
    public ValueTask<ActionResult<CustomerResponse>> Create(
        [FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var shipping = Maybe.From(request.ShippingAddress);

        return sender.Send(new CreateCustomerCommand(request.Email, shipping), ct)
                     .ToHttpResponseAsync(CustomerResponse.From)
                     .AsActionResultAsync<CustomerResponse>();
    }
}
```

The composite VO must still carry `[JsonConverter(typeof(CompositeValueObjectJsonConverter<ShippingAddress>))]` (see [Recipe 13](recipe-13-composite-json-and-collections.md)) so its inner fields round-trip through `TryCreate`. The seam adapter only handles the optionality.

**Why not just declare `Maybe<ShippingAddress>` on the DTO?** `MaybeScalarValueJsonConverterFactory.CanConvert` checks for `IScalarValue<,>` on the inner type. Composite VOs do not implement `IScalarValue`, so the factory returns false, and `Maybe<ShippingAddress>` falls back to default System.Text.Json handling of the `Maybe<T>` struct. Deserializing an object payload or an omitted property then yields `Maybe.None` without ever calling `TryCreate`, so an address the client sent is silently dropped; an explicit JSON `null` is rejected with a `JsonException` (HTTP 400), while the nullable-transport DTO accepts it (200). Serializing a `Some` writes the struct's own shape (`{"Value":{…},"HasValue":true,"HasNoValue":false}`), and serializing a `None` throws `InvalidOperationException`. That's a correctness bug, not just an ergonomics one. **`TRLS020` enforces this rule at compile time**: any DTO property typed `Maybe<TComposite>` where `TComposite` is `[OwnedEntity]` is flagged as a warning, even when `TComposite` itself carries `[JsonConverter(typeof(CompositeValueObjectJsonConverter<TComposite>))]` — the converter operates on `TComposite`, not on `Maybe<TComposite>`, so the inner attribute does not rescue the wrapper shape.

## Anti-pattern → fix

```csharp
// WRONG — composite Maybe<T> on DTO. It compiles, but System.Text.Json uses the generic struct shape:
// an object payload or omitted property deserializes to Maybe.None, TryCreate never runs and the
// client's address is silently dropped; an explicit JSON null is rejected with a 400 (TRLS020).
public sealed record CreateCustomerRequest(EmailAddress Email, Maybe<ShippingAddress> ShippingAddress);

// FIX — nullable transport + controller-seam adapter (Pattern B above).
public sealed record CreateCustomerRequest(EmailAddress Email, ShippingAddress? ShippingAddress);

// WRONG — bypassing AddScalarValueValidation() (e.g., raw services.AddControllers().AddJsonOptions(...) in isolation)
// drops the Maybe converters AND the MaybeSuppressChildValidationMetadataProvider. Omitting the property
// then reaches model validation with a None and MVC throws InvalidOperationException
// ("Maybe<PhoneNumber>.Value was accessed when HasValue is false"); null or valid values fail with a 400
// because the Maybe converters are missing.
services.AddControllers();   // missing scalar-value validation wiring

// FIX — call AddTrellisAspWithScalarValidation() (or AddScalarValueValidation() explicitly);
// both call paths configure both pipelines, and their order relative to AddControllers() does
// not matter. Call it once per host: repeated calls register the JSON converters again (4 becomes 8).
services.AddTrellisAspWithScalarValidation();
services.AddControllers();
```

> Nullable transport plus controller-seam adaptation is the supported pattern.
