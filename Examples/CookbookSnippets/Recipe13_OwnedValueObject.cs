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
            ? Result.Fail<ShippingAddress>(new Error.UnprocessableContent(EquatableArray.Create(violations.ToArray())))
            : Result.Ok(new ShippingAddress(street.Trim(), city.Trim(), state.Trim(), postalCode.Trim(), country.Trim()));
    }

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
        yield return PostalCode;
        yield return Country;
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

    public static Result<Customer> Create(CustomerId id, string name, ShippingAddress shipping) =>
        string.IsNullOrWhiteSpace(name)
            ? Result.Fail<Customer>(new Error.UnprocessableContent(EquatableArray.Create(
                new FieldViolation(InputPointer.ForProperty("name"), "required") { Detail = "Name is required." })))
            : Result.Ok(new Customer(id, name, shipping));
}

// CONFIGURATION — note the absence of OwnsOne(c => c.ShippingAddress).
// CompositeValueObjectConvention picks up [OwnedEntity] types automatically
// from the assemblies passed to ApplyTrellisConventions.
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
        configurationBuilder.ApplyTrellisConventions(typeof(Customer).Assembly);

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