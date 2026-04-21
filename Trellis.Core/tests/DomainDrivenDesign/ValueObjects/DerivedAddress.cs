namespace Trellis.Core.Tests.DomainDrivenDesign.ValueObjects;

internal class DerivedAddress : Address
{
    public string Country { get; }

    public DerivedAddress(string street, string city, string country) : base(street, city)
        => Country = country;

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        foreach (var s in base.GetEqualityComponents())
            yield return s;

        yield return Country;
    }
}