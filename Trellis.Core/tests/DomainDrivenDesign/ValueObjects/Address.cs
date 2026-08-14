namespace Trellis.Core.Tests.DomainDrivenDesign.ValueObjects;

internal class Address : ValueObject
{
    public string Street { get; }
    public string City { get; }

    public Address(string street, string city)
    {
        Street = street;
        City = city;
    }

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Street);
        components.Add(City);
    }
}