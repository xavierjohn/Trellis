namespace Trellis.Core.Tests.DomainDrivenDesign.ValueObjects;

internal class DerivedAddress : Address
{
    public string Country { get; }

    public DerivedAddress(string street, string city, string country) : base(street, city)
        => Country = country;

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        base.GetEqualityComponents(ref components);

        components.Add(Country);
    }
}