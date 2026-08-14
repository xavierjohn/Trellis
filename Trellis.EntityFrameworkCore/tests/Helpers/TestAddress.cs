namespace Trellis.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Test composite value object for testing auto-owned convention support.
/// Has four string properties — not a scalar value object, not <see cref="Trellis.Primitives.Money"/>.
/// Uses [OwnedEntity] to auto-generate the private parameterless constructor.
/// </summary>
[OwnedEntity]
public partial class TestAddress : ValueObject
{
    public string Street { get; private set; }
    public string City { get; private set; }
    public string State { get; private set; }
    public string ZipCode { get; private set; }

    public TestAddress(string street, string city, string state, string zipCode)
    {
        Street = street;
        City = city;
        State = state;
        ZipCode = zipCode;
    }

    public static TestAddress Create(string street, string city, string state, string zipCode) =>
        new(street, city, state, zipCode);

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Street);
        components.Add(City);
        components.Add(State);
        components.Add(ZipCode);
    }
}