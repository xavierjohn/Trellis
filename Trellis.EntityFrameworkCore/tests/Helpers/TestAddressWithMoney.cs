namespace Trellis.EntityFrameworkCore.Tests.Helpers;

using Trellis.Primitives;

/// <summary>
/// Test composite value object containing a nested <see cref="Money"/> composite value object.
/// Used to verify that <c>Maybe&lt;T&gt;</c> optionality propagates to nested owned types.
/// </summary>
public class TestAddressWithMoney : ValueObject
{
    public string Street { get; private set; }
    public string City { get; private set; }
    public Money DeliveryFee { get; private set; }

    // ReSharper disable once UnusedMember.Local — used by EF Core for materialization
    private TestAddressWithMoney()
    {
        Street = null!;
        City = null!;
        DeliveryFee = null!;
    }

    public TestAddressWithMoney(string street, string city, Money deliveryFee)
    {
        Street = street;
        City = city;
        DeliveryFee = deliveryFee;
    }

    public static TestAddressWithMoney Create(string street, string city, decimal fee, string currency) =>
        new(street, city, Money.Create(fee, currency));

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return DeliveryFee;
    }
}
