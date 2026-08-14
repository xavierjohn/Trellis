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

[OwnedEntity]
[JsonConverter(typeof(CompositeValueObjectJsonConverter<ShippingAddress>))]
public partial class ShippingAddress : ValueObject
{
    public string Street { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public string PostalCode { get; private set; } = null!;
    public string Country { get; private set; } = null!;

    private ShippingAddress(string street, string city, string state, string postalCode, string country)
    {
        Street = street;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
    }

    public static Result<ShippingAddress> TryCreate(
        string street, string city, string state, string postalCode, string country, string? fieldName = null)
    {
        var violations = new List<FieldViolation>(5);
        AddIfBlank(violations, street, fieldName, nameof(Street));
        AddIfBlank(violations, city, fieldName, nameof(City));
        AddIfBlank(violations, state, fieldName, nameof(State));
        AddIfBlank(violations, postalCode, fieldName, nameof(PostalCode));
        AddIfBlank(violations, country, fieldName, nameof(Country));
        return violations.Count > 0
            ? Result.Fail<ShippingAddress>(new Error.InvalidInput(EquatableArray.Create(violations.ToArray())))
            : Result.Ok(new ShippingAddress(street.Trim(), city.Trim(), state.Trim(), postalCode.Trim(), country.Trim()));
    }

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Street);
        components.Add(City);
        components.Add(State);
        components.Add(PostalCode);
        components.Add(Country);
    }

    private static void AddIfBlank(List<FieldViolation> v, string value, string? owner, string part)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return;
        var leaf = char.ToLowerInvariant(part[0]) + part[1..];
        var pointer = string.IsNullOrWhiteSpace(owner)
            ? InputPointer.ForProperty(leaf)
            : new InputPointer($"/{owner}/{leaf}");
        v.Add(new FieldViolation(pointer, "required") { Detail = $"{part} is required." });
    }
}

public sealed partial class CustomerId : RequiredGuid<CustomerId>;

public sealed partial class Customer : Aggregate<CustomerId>
{
    public string Name { get; private set; } = null!;

    public ShippingAddress ShippingAddress { get; private set; } = null!;

    public partial Maybe<ShippingAddress> BillingAddress { get; set; }

    private Customer(CustomerId id, string name, ShippingAddress shipping)
        : base(id)
    {
        Name = name;
        ShippingAddress = shipping;
    }

    public static Result<Customer> TryCreate(CustomerId? id, string? name, ShippingAddress? shipping) =>
        id.ToResult(Error.InvalidInput.ForField("id", "validation.error", "Customer id is required."))
            .Combine(name.EnsureNotNullOrWhiteSpace(Error.InvalidInput.ForField("name", "required", "Name is required.")))
            .Combine(shipping.ToResult(Error.InvalidInput.ForField("shipping", "validation.error", "Shipping address is required.")))
            .Map((id, name, shipping) => new Customer(id, name, shipping));
}

// CONFIGURATION — note the absence of OwnsOne(c => c.ShippingAddress).
// CompositeValueObjectConvention picks up [OwnedEntity] types automatically
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
            ? Result.Fail<LineItem>(Error.InvalidInput.ForRule("line.item.invalid"))
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
    internal const string LineItemsField = nameof(_lineItems);
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

    public static void OwnedCollection_BackFieldSurface(EntityTypeBuilder<PurchaseOrder> builder) =>
        builder.OwnsMany<LineItem>(PurchaseOrder.LineItemsField, owned =>
        {
            owned.WithOwner();
            owned.Property(item => item.Sku).HasMaxLength(64);
        });
}
