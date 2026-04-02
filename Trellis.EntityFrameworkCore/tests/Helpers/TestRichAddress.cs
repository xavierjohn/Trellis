namespace Trellis.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Test composite value object containing a scalar value object property.
/// Used to verify that scalar VO converters apply correctly inside auto-owned composite types.
/// </summary>
public class TestRichAddress : ValueObject
{
    public string Street { get; private set; }
    public string City { get; private set; }
    public TestStateCode State { get; private set; }
    public string ZipCode { get; private set; }

    // ReSharper disable once UnusedMember.Local — used by EF Core for materialization
    private TestRichAddress()
    {
        Street = null!;
        City = null!;
        State = null!;
        ZipCode = null!;
    }

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
