---
name: recipe-13-composite-json-and-collections
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Covers a composite value object's JSON wire shape, owned collections and DTO mapping. Use when exposing, binding or persisting collections of multi-field value objects."
---
# Recipe 13 part 2 — Composite value objects: JSON wire shape, owned collections and DTO mapping

**Requires:** `Trellis.Core`, plus `Trellis.EntityFrameworkCore`, `Trellis.Primitives`.

## Contents

- [Composite value object JSON wire shape](#composite-value-object-json-wire-shape)
- [Owned collections with a private backing field](#owned-collections-with-a-private-backing-field)
- [Supported property shapes inside a composite VO — when to map to a DTO instead](#supported-property-shapes-inside-a-composite-vo--when-to-map-to-a-dto-instead)

Continues [Recipe 13](recipe-13-composite-value-object.md#recipe-13--composite-value-object-end-to-end-domain--api-json-binding--ef-core-ownership), which defines the composite value object, its typed factories and its EF Core persistence.

| Task within this recipe | Read |
|---|---|
| Choose the JSON boundary shape | [JSON wire shape](#composite-value-object-json-wire-shape), then [supported interiors and DTO seam](#supported-property-shapes-inside-a-composite-vo--when-to-map-to-a-dto-instead) |
| Map a read-only collection with a backing field | [Owned collections](#owned-collections-with-a-private-backing-field) |
| Define the composite value object or its typed factories | [Recipe 13](recipe-13-composite-value-object.md#recipe-13--composite-value-object-end-to-end-domain--api-json-binding--ef-core-ownership) |

## Composite value object JSON wire shape

The `[JsonConverter(typeof(CompositeValueObjectJsonConverter<T>))]` attribute on the value object controls the wire format. There is no auto-discovery — the attribute is required for the converter to engage on request bodies and response payloads.

| C# property | JSON request/response shape |
|---|---|
| `ShippingAddress ShippingAddress { get; private set; }` (required composite VO) | `"shippingAddress": { "street": "1 Main St", "city": "Redmond", "state": "WA", "postalCode": "98052", "country": "US" }` — every field present; an **invalid** inner field (fails `TryCreate`) or a **missing** inner field produces structured `Error.InvalidInput` with field path `/shippingAddress/<field>`. With `AddTrellisAspWithScalarValidation()` registered (see [Recipe 14](recipe-14-optional-dto-fields.md)), ASP collects it as a 422; with a bare `AddControllers()` the same payload is a 400 with `$.shippingAddress` and no per-field path. Standalone serialization rejects it. |
| `partial Maybe<ShippingAddress> BillingAddress { get; set; }` (optional composite VO on a domain model — **not** used directly on a request DTO; see [Recipe 14](recipe-14-optional-dto-fields.md)) | Domain model only. On the wire, request DTOs use a **nullable transport** (`ShippingAddress?`) and the controller adapts via `Maybe.From(...)`. Response DTOs project to `ShippingAddress?` for the same reason. |
| `Money Total { get; private set; }` (required composite VO with scalar inner properties — `decimal Amount`, `CurrencyCode Currency`) | `"total": { "amount": 49.99, "currency": "USD" }` — the inner field casing is camelCase (the `CompositeValueObjectJsonConverter` emits camelCase property names). Inner scalar VOs (e.g., `CurrencyCode : RequiredString<CurrencyCode>`) serialize as their underlying primitive (`"USD"`, not `{"value":"USD"}`). |
| Scalar VO (`OrderId : RequiredGuid<OrderId>`, `EmailAddress : RequiredString<EmailAddress>`) | Always serializes as the underlying primitive (`"550e8400-..."`, `"a@b.com"`). Never wrapped in `{ "value": ... }`. This is automatic via the source-generated `IScalarValue<T,P>` JSON converter. |

**Anti-pattern → fix.**

```csharp
// WRONG — explicit Property() on a composite owned VO. The convention has already
// registered an OwnsOne relationship; calling builder.Property() tries to map the
// composite as a single column, which fails at model validation with
// "The property 'Order.Total' could not be mapped because the database provider
//  does not support the type 'Money'."
internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.Property(o => o.Total).IsRequired();           // ❌ — runtime error
        builder.Property(o => o.ShippingAddress).IsRequired(); // ❌ — runtime error
    }
}

// FIX — say nothing about composite owned VOs in IEntityTypeConfiguration. The convention
// auto-registers them as OwnsOne. To override (rename column, add an index on an inner
// property, force table-splitting), use OwnsOne explicitly — explicit OwnsOne configuration
// takes precedence over the convention.

// WRONG — an empty OwnsOne after ApplyTrellisConventions adds nothing: the convention already owns
// the type, so it only duplicates the registration. Use OwnsOne only to override.
builder.OwnsOne(c => c.ShippingAddress, owned => { /* … */ });

// FIX — let the convention own the registration. Use OwnsOne only to override
// (e.g., to rename columns or add an index on an inner property):
builder.OwnsOne(c => c.ShippingAddress, owned =>
{
    owned.Property(a => a.PostalCode).HasColumnName("PostalCode").HasMaxLength(20);
    owned.HasIndex(a => a.Country);
});
```

```csharp
// WRONG — non-partial class (TRLS036) so the generator can't emit the parameterless ctor.
[OwnedEntity]
public class ShippingAddress : ValueObject { /* … */ }

// WRONG — a handwritten parameterless ctor (TRLS037, a warning) makes the generator skip its emission; remove it or drop [OwnedEntity].
[OwnedEntity]
public partial class ShippingAddress : ValueObject { public ShippingAddress() { } }

// WRONG — not a ValueObject (TRLS038), so equality and convention-based mapping break.
[OwnedEntity]
public partial class ShippingAddress { /* … */ }
```

## Owned collections with a private backing field

When an aggregate or composite value object exposes a collection as an `IReadOnlyList<T>` (or `IReadOnlyCollection<T>`) facade over a private `List<T>` field, **how you map it depends on whether the element `T` is a value object or an entity:**

- **`T` is a composite value object** (derives from `ValueObject`): **no mapping configuration is needed for discovery.** `CompositeValueObjectConvention` registers every composite VO as an owned type, and EF Core's navigation discovery binds the read-only facade to its `_camelCase` backing field automatically — including when the collection lives *inside another composite VO*, and whether the owner is required or `Maybe<T>`. Writing `Ignore` + `OwnsMany` for discovery is redundant. The synthesized per-row integer key is store-generated, though: it inserts on SQL Server and fails on SQLite (`NOT NULL constraint failed: <Table>.Id`). For SQLite tests, or provider-independent keys, map it: `OwnsMany(o => o.Items, w => { w.Property<Guid>("RowId"); w.HasKey("RowId"); })`.
- **`T` is an entity** (has its own identity, not a `ValueObject`): the element is **not auto-owned** — Trellis conventions auto-own value objects only. By convention EF treats an entity collection as a *separate, independently-tracked* relationship, so configure `OwnsMany` explicitly to make it an owned aggregate child, as shown below.

```csharp
// VALUE-OBJECT collection — zero configuration; the convention owns it.
[OwnedEntity]
public partial class Innings : ValueObject
{
    private readonly List<FallOfWicket> _fallOfWickets = [];            // FallOfWicket : ValueObject
    public IReadOnlyList<FallOfWicket> FallOfWickets => _fallOfWickets; // auto-mapped — no Ignore/OwnsMany
}
```

Map an **entity** collection explicitly with expression-based `OwnsMany`. EF binds the
read-only navigation to its conventionally named backing field, so the public facade
stays read-only and refactoring tools can follow the mapping:

```csharp
public sealed partial class Order : Aggregate<OrderId>
{
    private readonly List<LineItem> _lineItems = [];
    public IReadOnlyList<LineItem> LineItems => _lineItems;
    // ...
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);

        builder.OwnsMany(o => o.LineItems, li =>
        {
            li.ToTable("LineItems");
            li.HasKey(x => x.Id);
            // Inner composite VO properties (e.g., LineItem.UnitPrice : Money) are
            // discovered by EF Core's NavigationDiscoveryConvention because
            // CompositeValueObjectConvention registers each composite VO type as Owned
            // globally at model initialization — no extra OwnsOne needed here.
        });
    }
}
```

**Fallback: convention cannot bind the field.** Only then ignore the facade and map
the field explicitly. The field name is now part of the mapping contract and must
be updated if the field is renamed:

```csharp
builder.Ignore(o => o.LineItems);
builder.OwnsMany<LineItem>("_lineItems", li =>
{
    li.ToTable("LineItems");
    li.HasKey(x => x.Id);
});
```

Choose the mapping shape deliberately:

| Mitigation | Compile-time safety | Cost |
|---|---|---|
| `builder.OwnsMany(o => o.LineItems, cfg => cfg.HasKey(...))` directly against the facade | Refactor-safe — no magic string; renaming the field is transparent while convention can bind it. | **Preferred.** EF binds the read-only `IReadOnlyList<LineItem>` navigation to the backing `List<LineItem>` field by convention. The `cfg` callback still configures the owned type (`cfg.ToTable(...)`, `cfg.Property(...).HasColumnName(...)`, etc.). |
| Raw string `"_lineItems"` | None — typo or rename breaks at runtime model-validation. | **Fallback only** when convention cannot resolve the facade-to-field link. |
| `internal const string LineItemsField = "_lineItems";` on `Order`, then `builder.OwnsMany<LineItem>(Order.LineItemsField, …)` in the same assembly | Refactoring tools follow the constant. Still no compile check that the field actually exists. | Exposes a persistence field name through the aggregate; unlike expression mapping, the string still needs updating when the field is renamed. |

**Why there is no convention for _entity_ collections.** Composite **value-object** collections are already handled automatically (see the value-object case above) — `CompositeValueObjectConvention` registers each composite VO as owned, so EF Core's navigation discovery maps the `IReadOnlyList<VO>` facade with no extra configuration. An equivalent convention for **entity** collections would need to walk every aggregate, find `IReadOnlyList<T>` / `IReadOnlyCollection<T>` properties whose `T` is an entity, locate a matching `_camelCase` backing field, and register the `OwnsMany` against it. The manual pattern above is the supported approach for entity collections.

> **Testing on SQLite?** An `OwnsMany` whose per-row key is a store-generated integer inserts fine on SQL Server (`int IDENTITY`) but fails on SQLite with `NOT NULL constraint failed: <Table>.Id`, because SQLite only auto-increments a single-column `INTEGER PRIMARY KEY`. This is stock EF Core + SQLite behavior, not a Trellis issue. Give the collection a client-generated `Guid` key to make it provider-independent — see [trellis-api-efcore.md](../trellis-api-efcore.md#provider-specific-behavior-owned-collections-on-sqlite).

## Supported property shapes inside a composite VO — when to map to a DTO instead

`CompositeValueObjectJsonConverter<T>` is deliberately small. It supports a closed list of primitive types directly and routes Trellis scalar value objects through their underlying primitives. Anything else — including any `Maybe<T>`, arrays, collections, and nested composite VO properties — throws `TrellisJsonValidationException` on serialization, and on deserialization when the JSON contains the property (a missing property is reported as a missing-field violation). The converter never delegates property reads/writes back to `JsonSerializer`, so adding `[JsonConverter]` to an inner composite type does not rescue the nested case. **EF Core persistence is more permissive** (`Maybe<T>`, arrays, and nested composites work) so the JSON gap only surfaces when the composite VO crosses an HTTP boundary.

The intentional split is: keep the framework converter simple; route consumers with richer shapes through a wire-shape DTO at the controller/endpoint seam ([Recipe 14](recipe-14-optional-dto-fields.md) generalised).

| Property type on a composite VO interior | JSON via `CompositeValueObjectJsonConverter`? | Recommended path |
|---|---|---|
| `string` | ✅ Supported as-is | Use directly. |
| `decimal`, `int`, `long`, `short`, `byte`, `double`, `float`, `bool` | ✅ Supported as-is | Use directly. |
| `Guid`, `DateTime`, `DateTimeOffset` | ✅ Supported as-is | Use directly. |
| Trellis scalar VO (`RequiredString<>`, `RequiredInt<>`, `RequiredGuid<>`, `RequiredEnum<>`, `RequiredDateTime<>`, …) — and shipped concretes like `EmailAddress`, `PhoneNumber`, `Money.Currency` (`CurrencyCode`) | ✅ Flattens to underlying primitive on the wire | Use directly. The composite converter detects `IScalarValue<,>` and reads/writes the inner primitive. |
| **Nullable** Trellis scalar VO (`PropertyType? Optional` declared as a property on the composite VO) | ⚠️ **Write/read asymmetric.** Writing `null` produces JSON `null`; reading the same JSON throws a primitive-specific validation error (e.g. `Property '...' must be a string.` for a string-backed scalar like `RequiredString<>` / `RequiredEnum<>`; `must be an integer.` for `RequiredInt<>`; `must be a GUID.` for `RequiredGuid<>`). The converter doesn't propagate TryCreate parameter nullability through its metadata, so `TryReadPrimitive` cannot distinguish a required scalar from a nullable scalar interior. | **Map to a DTO.** Same pattern as `Maybe<T>` below — declare the optional field on the wire-shape DTO as nullable, lift to `Maybe<TScalar>` (preferred) or keep nullable on the domain VO and resolve at the seam. (A repository test in the Trellis source pins this asymmetry.) |
| Nested composite owned VO (e.g., a nested `Money` or `ShippingAddress` as a property of another composite VO) | ❌ **Not supported.** `CompositeValueObjectJsonConverter<TOuter>` does not delegate to `JsonSerializer` for property values — it only reads/writes its own primitive allowed list directly. Adding `[JsonConverter]` to the inner type does not help, because the outer converter never asks STJ for an inner converter; it tries to treat the nested type as one of its primitives and trips on the first access. | **Map to a DTO** — declare each nested composite as its own property on the wire-shape DTO. Nested composite VOs serialize correctly only when STJ sees them at the top of a property of a non-Trellis-composite-converter type (i.e., a plain record/class with `[JsonConverter]` on the nested VO type itself). |
| `Maybe<T>` for any T (primitive, scalar VO, composite VO, array) | ❌ **Not supported.** Throws `TrellisJsonValidationException` on write (``Unsupported primitive type 'Maybe`1...'``) regardless of value (`Some` or `None`); on read it throws when the JSON contains the property. | **Map to a wire-shape DTO.** See [Recipe 14](recipe-14-optional-dto-fields.md) — the same `T?` + adapt-at-the-seam pattern applies whether the optional is on the DTO itself or inside a composite VO. |
| Array (`T[]`), collection (`List<T>`, `IReadOnlyList<T>`, `IEnumerable<T>`) | ❌ Not supported. | **Map to a DTO** with the array on the wire-shape type. |

**When to use a DTO at the seam (the general rule).** If a composite VO's interior holds any property shape outside the supported list — most commonly `Maybe<TPrimitive>` or arrays — keep the VO clean as a domain type and declare a wire-shape DTO at the controller/endpoint:

```csharp
// Domain VO: clean, persists fine via EF Core, but cannot serialize as-is.
[OwnedEntity]
public partial class CustomerProfile : ValueObject
{
    public OrderStatus Status { get; private set; } = null!;
    public partial Maybe<int> AgeYears { get; private set; }             // not JSON-serializable on this VO
    public partial Maybe<string> Nickname { get; private set; }          // not JSON-serializable
    public partial Maybe<DateTime[]> LoginHistory { get; private set; }  // not JSON-serializable
    // ...TryCreate, GetEqualityComponents...
}

// Wire-shape DTO at the API seam: everything as nullable transports the converter can serialize.
public sealed record CustomerProfileDto(string Status, int? AgeYears, string? Nickname, DateTime[]? LoginHistory)
{
    public Result<CustomerProfile> ToDomain() =>
        CustomerProfile.TryCreate(
            Status,
            AgeYears.AsMaybe(),
            Maybe.From(Nickname),
            Maybe.From(LoginHistory));

    public static CustomerProfileDto From(CustomerProfile vo) =>
        new(vo.Status.Value,
            vo.AgeYears.AsNullable(),
            vo.Nickname.HasValue ? vo.Nickname.Value : null,
            vo.LoginHistory.HasValue ? vo.LoginHistory.Value : null);
}
```

The controller takes `CustomerProfileDto` on inbound requests, calls `.ToDomain()` to lift to the domain VO, and projects back via `CustomerProfileDto.From(...)` on responses. The Trellis scalar `Maybe<>` extensions (`AsMaybe` / `AsNullable`) and `Maybe.From` handle the lift cleanly.

**Last-resort escape hatch — write your own `JsonConverter<TComposite>`.** If a service genuinely cannot tolerate the DTO indirection (rare — usually a sign the design wants tightening), the language's standard mechanism still applies: declare `[JsonConverter(typeof(YourCustomConverter))]` on the composite VO and implement `Read`/`Write` directly. The framework does not provide help for this path; the trade-off is more code per-VO in exchange for putting the domain shape directly on the wire. This is not the recommended path — favour the DTO seam unless there is a concrete reason not to.
