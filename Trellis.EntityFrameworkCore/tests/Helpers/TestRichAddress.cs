namespace Trellis.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Test composite value object containing a scalar value object property.
/// Used to verify that scalar VO converters apply correctly inside auto-owned composite types.
/// Uses [OwnedEntity] to auto-generate the private parameterless constructor.
/// </summary>
[OwnedEntity]
public partial class TestRichAddress : ValueObject
{
    public string Street { get; private set; }
    public string City { get; private set; }
    public TestStateCode State { get; private set; }
    public string ZipCode { get; private set; }

    public TestRichAddress(string street, string city, TestStateCode state, string zipCode)
    {
        Street = street;
        City = city;
        State = state;
        ZipCode = zipCode;
    }

    public static TestRichAddress Create(string street, string city, string state, string zipCode) =>
        new(street, city, TestStateCode.Create(state), zipCode);

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return (string)State;
        yield return ZipCode;
    }
}