// Cookbook Recipe 13 — EF Core: composite owned value object ([OwnedEntity] + OwnsOne not needed).
namespace CookbookSnippets.Recipe13;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
            .Map((id, name, shipping) => new Customer(id, name, shipping));
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

// FIX block — when you DO want to override the convention.
internal static class OverrideExample
{
    public static void Configure(EntityTypeBuilder<Customer> builder) =>
        builder.OwnsOne(c => c.ShippingAddress, owned =>
        {
            owned.Property(a => a.PostalCode).HasColumnName("PostalCode").HasMaxLength(20);
            owned.HasIndex(a => a.Country);
        });
}
[OwnedEntity]
public partial class LineItem : ValueObject
{
    public string Sku { get; private set; } = null!;
    public int Quantity { get; private set; }

    private LineItem(string sku, int quantity)
    {
        Sku = sku;
        Quantity = quantity;
    }

    public static Result<LineItem> TryCreate(string sku, int quantity) =>
        string.IsNullOrWhiteSpace(sku) || quantity <= 0
            ? Result.Fail<LineItem>(Error.InvalidInput.ForRule(code: "line.item.invalid"))
            : Result.Ok(new LineItem(sku.Trim(), quantity));

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Sku);
        components.Add(Quantity);
    }
}

public sealed partial class OrderId : RequiredGuid<OrderId>;

public sealed class PurchaseOrder : Aggregate<OrderId>
{
    private readonly List<LineItem> _lineItems = [];

    public IReadOnlyCollection<LineItem> LineItems => _lineItems.AsReadOnly();

    private PurchaseOrder(OrderId id) : base(id) { }
}

internal static class Recipe13OwnedValueObjectSurface
{
    public static void ApplyTrellisConventions_ReflectionOverload(ModelConfigurationBuilder configurationBuilder)
    {
        ModelConfigurationBuilder builder = configurationBuilder.ApplyTrellisConventions(typeof(Customer).Assembly);
        Type attributeType = typeof(OwnedEntityAttribute);

        _ = (builder, attributeType);
    }

    public static void OwnedCollection_TypedNavigationSurface(EntityTypeBuilder<PurchaseOrder> builder) =>
        builder.OwnsMany(order => order.LineItems, owned =>
        {
            owned.WithOwner();
            owned.Property(item => item.Sku).HasMaxLength(64);
        });
}