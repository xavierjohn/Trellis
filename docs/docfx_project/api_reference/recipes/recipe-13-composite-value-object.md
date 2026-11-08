---
name: recipe-13-composite-value-object
package: Trellis (cross-package recipes)
namespaces: [Trellis, Trellis.Asp, Trellis.EntityFrameworkCore, Trellis.Mediator]
types: [recipes]
related_docs: [trellis-start-here.md, trellis-api-core.md, trellis-api-asp.md, trellis-api-efcore.md, trellis-api-mediator.md]
version: v3
last_verified: 2026-10-22
audience: [llm]
agent_usage: onDemand
agent_description: "Shows a composite value object's domain model, typed factories and EF Core ownership. Use when creating or persisting a multi-field value object such as an address or weekly schedule."
---
# Recipe 13 — Composite value object end-to-end (Domain + API JSON binding + EF Core ownership)

**Requires:** `Trellis.Core`, plus `Trellis.EntityFrameworkCore`, `Trellis.Primitives`.

## Contents

- [Composite value object domain and persistence contract](#composite-value-object-domain-and-persistence-contract)
  - [Typed composite factories and property order](#typed-composite-factories-and-property-order)
  - [EF ownership and materialization](#ef-ownership-and-materialization)

| Task within this recipe | Read |
|---|---|
| Define and persist the composite value object | [Domain and persistence contract](#composite-value-object-domain-and-persistence-contract), including the solution and storage rules |
| Use typed interior fields and choose the JSON or domain factory without swapping values | [Typed factories and property order](#typed-composite-factories-and-property-order) |
| Choose the JSON boundary shape, map a read-only collection, or decide when to use a DTO | [Recipe 13 part 2](recipe-13-composite-json-and-collections.md#recipe-13-part-2--composite-value-objects-json-wire-shape-owned-collections-and-dto-mapping) |
| Only require a nonblank string | [Core string guard](../trellis-api-core.md#required-nonblank-strings); the composite recipe is not needed |

## Composite value object domain and persistence contract

**Problem.** Persist a `ShippingAddress` inside a `Customer`, with typed street/city/state/postalCode/country fields. Every field is required, construction returns a `Result<ShippingAddress>`, and JSON must reuse the domain validation while remaining a flat object of primitive fields.

### Typed composite factories and property order

Use a string-backed scalar value object for each component and write **both** composite factories:

| Factory | Caller | Responsibility |
|---|---|---|
| Primitive-input `TryCreate` | JSON converter or code with unvalidated strings | Validate every scalar, accumulate field failures with `Combine`, then `Bind` to the typed factory. |
| Typed-input `TryCreate` | Domain code with validated scalar instances | Reject missing references with `Result.EnsureNotNull`, then construct the composite. Put additional cross-field rules here so both entry paths use them. |

`RequiredString<TSelf>` supplies the scalar factories; you write the two `ShippingAddress.TryCreate` overloads. `[Trim, NotDefault]` opts into trimming and blank rejection: these example types require nonblank text, not postal/ISO-format validity. `ValueObject` supplies equality, hashing, and comparison; the composite supplies only its ordered `GetEqualityComponents`.

> [!WARNING]
> **Property declaration order and primitive-factory parameter order are one positional contract.** The converter reduces scalar properties to their primitive types and selects `TryCreate` by parameter types and positions, **not parameter names**. All five components below become `string`, so swapping city/state in only one place can compile and silently exchange values.
>
> Keep `Street, City, State, PostalCode, Country` aligned with `street, city, state, postalCode, country`. Keep the typed overload, constructor assignments, and equality components in that order too. Named arguments at a handwritten call site do not change the converter's positional call. JSON members may arrive in any order: they are looked up by JSON name before invocation. After a refactor, check every field with distinct values in a JSON round trip.

The distinct-value serialization and JSON member-order assertions in `Examples/CookbookSnippets.Tests/Recipe13FactoryTests.cs` in the Trellis source repository (not shipped in the NuGet package) demonstrate this check.

The converter needs exactly one unambiguous primitive-prefix `TryCreate` returning `Result<ShippingAddress>`; trailing parameters must be optional. The typed overload does not match that primitive prefix and is not called directly by the converter.

```csharp
using System.Text.Json.Serialization;
using Trellis;
using Trellis.EntityFrameworkCore;
using Trellis.Primitives;

[Trim, NotDefault]
public sealed partial class StreetLine : RequiredString<StreetLine>;

[Trim, NotDefault]
public sealed partial class CityName : RequiredString<CityName>;

[Trim, NotDefault]
public sealed partial class StateCode : RequiredString<StateCode>;

[Trim, NotDefault]
public sealed partial class PostalCodeText : RequiredString<PostalCodeText>;

[Trim, NotDefault]
public sealed partial class CountryCodeText : RequiredString<CountryCodeText>;

[OwnedEntity]
[JsonConverter(typeof(CompositeValueObjectJsonConverter<ShippingAddress>))]
public partial class ShippingAddress : ValueObject
{
    public StreetLine Street { get; private set; } = null!;
    public CityName City { get; private set; } = null!;
    public StateCode State { get; private set; } = null!;
    public PostalCodeText PostalCode { get; private set; } = null!;
    public CountryCodeText Country { get; private set; } = null!;

    private ShippingAddress(
        StreetLine street, CityName city, StateCode state, PostalCodeText postalCode, CountryCodeText country)
    {
        Street = street;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
    }

    public static Result<ShippingAddress> TryCreate(
        string? street, string? city, string? state, string? postalCode, string? country, string? fieldName = null) =>
        StreetLine.TryCreate(street, ComponentField(fieldName, nameof(Street)))
            .Combine(CityName.TryCreate(city, ComponentField(fieldName, nameof(City))))
            .Combine(StateCode.TryCreate(state, ComponentField(fieldName, nameof(State))))
            .Combine(PostalCodeText.TryCreate(postalCode, ComponentField(fieldName, nameof(PostalCode))))
            .Combine(CountryCodeText.TryCreate(country, ComponentField(fieldName, nameof(Country))))
            .Bind((street, city, state, postalCode, country) =>
                TryCreate(street, city, state, postalCode, country, fieldName));

    public static Result<ShippingAddress> TryCreate(
        StreetLine? street, CityName? city, StateCode? state,
        PostalCodeText? postalCode, CountryCodeText? country, string? fieldName = null) =>
        Result.EnsureNotNull(street, ComponentField(fieldName, nameof(Street)), "Street is required.")
            .Combine(Result.EnsureNotNull(city, ComponentField(fieldName, nameof(City)), "City is required."))
            .Combine(Result.EnsureNotNull(state, ComponentField(fieldName, nameof(State)), "State is required."))
            .Combine(Result.EnsureNotNull(postalCode, ComponentField(fieldName, nameof(PostalCode)), "Postal code is required."))
            .Combine(Result.EnsureNotNull(country, ComponentField(fieldName, nameof(Country)), "Country is required."))
            .Map(static (street, city, state, postalCode, country) =>
                new ShippingAddress(street, city, state, postalCode, country));

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Street);
        components.Add(City);
        components.Add(State);
        components.Add(PostalCode);
        components.Add(Country);
    }

    private static string ComponentField(string? fieldName, string component) =>
        InputPointer.ForProperty(fieldName.NormalizeFieldName(string.Empty))
            .AppendProperty(component.ToCamelCase()).Path;
}
```

The typed factory preserves supplied scalar instances instead of reparsing them. Its null-only guards emit `ValidationCodes.ValueNotNull` only for missing values. The primitive factory emits `ValidationCodes.ValueNotNull` for null inputs and `ValidationCodes.ValueNotEmpty` for empty/whitespace inputs after trimming. Neither creates violations on success. The private pointer helper appends escaped literal property names to a root, simple owner name, or existing pointer: `fieldName: "/customers/0/shippingAddress"` gives `/customers/0/shippingAddress/state`, not a re-escaped parent.

For JSON calls, trailing optional parameters receive their declared defaults (`fieldName: null` here), not the live JSON parent path. The ASP collecting adapter rebases the resulting component pointers to the live path once. Keep the factory's default root-relative; manual callers can supply a parent explicitly.

### EF ownership and materialization

| Concern | Contract |
|---|---|
| Ownership discovery | `CompositeValueObjectConvention` discovers composites by **inheritance from `ValueObject`**, not `[OwnedEntity]`. The generated `ApplyTrellisConventionsFor<AppDbContext>()` follows reachable types from the context's `DbSet<T>` roots and registers scalar converters too. No manual `OwnsOne` or scalar `HasConversion` is needed for this address. |
| Composite materialization | `[OwnedEntity]` generates the private parameterless constructor. Its class must be `partial` (`TRLS036`), inherit `ValueObject` (`TRLS038`), and declare no parameterless constructor (`TRLS037`). Keep `{ get; private set; }`, not `init` (`TRLS022`). A mapped composite with neither a generated nor handwritten parameterless constructor fails at model-build with `TrellisPersistenceMappingException`. |
| Aggregate materialization | EF cannot bind an owned navigation in a constructor. The separate `Customer(CustomerId id)` constructor below provides a materialization path. |
| JSON validation | `[JsonConverter(typeof(CompositeValueObjectJsonConverter<ShippingAddress>))]` selects the primitive factory. Without the converter, JSON binding does not enforce this `TryCreate` contract. |

**EF-free domain alternative.** `[OwnedEntity]` lives in `Trellis.EntityFrameworkCore`, so applying it introduces that dependency. To keep the value object EF-free, omit the attribute and write a private parameterless constructor yourself, as [Recipe 1](recipe-01-crud-aggregate.md)'s custom `Money` does. Convention-based ownership still works. Referencing the package analyzer-only (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`) supplies generators but does **not** make a layer applying `[OwnedEntity]` EF-free. See the [EF storage and migration guidance](../trellis-api-efcore.md#maybet-storage-owned-types-and-migrations).

In the same namespace, add the owning aggregate and context:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Trellis;
using Trellis.EntityFrameworkCore;

public sealed partial class CustomerId : RequiredGuid<CustomerId>;

public sealed partial class Customer : Aggregate<CustomerId>
{
    public string Name { get; private set; } = null!;

    public ShippingAddress ShippingAddress { get; private set; } = null!;

    public partial Maybe<ShippingAddress> BillingAddress { get; set; }

    private Customer(CustomerId id) : base(id) { }

    private Customer(CustomerId id, string name, ShippingAddress shipping)
        : base(id)
    {
        Name = name;
        ShippingAddress = shipping;
    }

    public static Result<Customer> TryCreate(CustomerId? id, string? name, ShippingAddress? shipping) =>
        Result.EnsureNotNull(id, "id", "Customer id is required.")
            .Combine(name.EnsureNotNullOrWhiteSpace("name", "Name is required."))
            .Combine(Result.EnsureNotNull(shipping, "shipping", "Shipping address is required."))
            .Map(static (id, name, shipping) => new Customer(id, name, shipping));
}

// CONFIGURATION — note the absence of OwnsOne(c => c.ShippingAddress).
// CompositeValueObjectConvention discovers ValueObject types by inheritance
// through the source-generated ApplyTrellisConventionsFor<TContext>() entry point.
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired();
    }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.ApplyTrellisConventionsFor<AppDbContext>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

**What it shows.**

- Null-only required-field guards create errors only for missing values. `EnsureNotNullOrWhiteSpace("name", "Name is required.")` reports `value.not-null` for null names and `value.not-empty` for empty/whitespace names, without creating violations on success or trimming valid text.
- The primitive and typed factories converge on one construction path. Scalar validation and missing-reference checks accumulate all field failures; additional composite rules belong in the typed factory, not a second transport-only implementation.
- `CompositeValueObjectJsonConverter<T>` calls the primitive `TryCreate`, so an invalid `state` produces the same structured `Error.InvalidInput` the domain emits. Missing/null inner JSON fields fail *before* `TryCreate` runs. ASP collecting adapters collect these failures without throwing; standalone deserialization rejects them with `TrellisJsonValidationException`.
- `ApplyTrellisConventions` removes the boilerplate `OwnsOne` call. You only need `OwnsOne` when you want to **override** the convention (custom column names, table splitting, indexes on inner properties).

**Storage shape.**

The scalar interior properties are converted to primitive string columns, not nested owned navigations. Typing the fields does not change this address's storage or JSON shape.

| Aggregate property | Storage |
|---|---|
| Required `ShippingAddress` (non-nullable) | Table-split: 5 columns on the `Customers` table — `ShippingAddress_Street`, `ShippingAddress_City`, `ShippingAddress_State`, `ShippingAddress_PostalCode`, `ShippingAddress_Country` (all `NOT NULL`). |
| Optional `Maybe<ShippingAddress>` | `CompositeValueObjectConvention` **table-splits** it into the `Customers` table as **nullable** columns (`BillingAddress_Street`, …, all `NULL`-able); absence is encoded as all-null. It uses a **separate table** `{Owner}_{Property}` when the composite has **nested owned navigations** *or* a **non-nullable value-type inner property** — table-splitting can't represent either for an optional dependent (all-null columns would make existence ambiguous, and EF Core rejects making a non-nullable value-type column optional), so row existence encodes presence instead. See the storage rules in [trellis-api-efcore.md](../trellis-api-efcore.md#maybet-storage-owned-types-and-migrations) for the full decision matrix. |

