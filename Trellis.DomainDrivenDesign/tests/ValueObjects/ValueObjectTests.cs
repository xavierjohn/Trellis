namespace Trellis.DomainDrivenDesign.Tests.ValueObjects;

using FluentAssertions;
using Xunit;

public class ValueObjectTests
{
    #region Equality Tests

    [Fact]
    public void Two_ValueObject_of_the_same_content_are_equal()
    {
        var address1 = new Address("Street", "City");
        var address2 = new Address("Street", "City");

        address1.Equals(address2).Should().BeTrue();
        (address1 == address2).Should().BeTrue();
        (address1 != address2).Should().BeFalse();
        address1.GetHashCode().Equals(address2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void Two_ValueObject_of_different_content_are_not_equal()
    {
        var address1 = new Address("Street1", "City");
        var address2 = new Address("Street2", "City");

        address1.Equals(address2).Should().BeFalse();
        (address1 == address2).Should().BeFalse();
        (address1 != address2).Should().BeTrue();
    }

    [Fact]
    public void Derived_value_objects_are_not_equal()
    {
        var address = new Address("Street", "City");
        var derivedAddress = new DerivedAddress("Street", "City", "Country");

        address.Equals(derivedAddress).Should().BeFalse();
        derivedAddress.Equals(address).Should().BeFalse();
    }

    [Fact]
    public void ValueObject_compared_to_null_is_not_equal()
    {
        var address = new Address("Street", "City");

        address.Equals(null).Should().BeFalse();
        (address == null).Should().BeFalse();
        (address != null).Should().BeTrue();
    }

    [Fact]
    public void Null_ValueObject_compared_to_null_is_equal()
    {
        Address? address1 = null;
        Address? address2 = null;

        (address1 == address2).Should().BeTrue();
        (address1 != address2).Should().BeFalse();
    }

    [Fact]
    public void Null_ValueObject_compared_to_value_is_not_equal()
    {
        Address? nullAddress = null;
        var address = new Address("Street", "City");

        (nullAddress == address).Should().BeFalse();
        (nullAddress != address).Should().BeTrue();
    }

    [Fact]
    public void Custom_equality_comparison_with_rounding()
    {
        var money1 = new Money(2.2222m);
        var money2 = new Money(2.22m);

        money1.Equals(money2).Should().BeTrue();
        money1.GetHashCode().Equals(money2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void ValueObject_GetHashCode_is_cached()
    {
        var money = new Money(100m);

        var hash1 = money.GetHashCode();
        var hash2 = money.GetHashCode();

        hash1.Should().Be(hash2);
    }

    #endregion

    #region Comparison and Sorting Tests

    [Fact]
    public void ValueObject_is_sorted()
    {
        // Arrange
        var one = new Money(1);
        var two = new Money(2);
        var three = new Money(3);
        var moneys = new List<Money> { two, one, three };

        // Act
        moneys.Sort();

        // Assert
        moneys.Should().Equal(new List<Money> { one, two, three });
    }

    [Fact]
    public void ValueObject_supports_orderby()
    {
        // Arrange
        var one = new Money(1);
        var two = new Money(2);
        var three = new Money(3);
        var moneys = new[] { two, one, three };

        // Act
        var orderedMoney = moneys.OrderBy(r => r);

        // Assert
        orderedMoney.Should().Equal(new List<Money> { one, two, three });
    }

    [Fact]
    public void Comparing_less_than()
    {
        var money1 = new Money(2.1m);
        var money2 = new Money(2.2m);

        (money1 < money2).Should().BeTrue();
        (money2 < money1).Should().BeFalse();
    }

    [Fact]
    public void Comparing_greater_than()
    {
        var money1 = new Money(2.1m);
        var money2 = new Money(2.2m);

        (money2 > money1).Should().BeTrue();
        (money1 > money2).Should().BeFalse();
    }

    [Fact]
    public void Comparing_less_than_or_equal()
    {
        var money1 = new Money(2.1m);
        var money2 = new Money(2.2m);
        var money3 = new Money(2.1m);

        (money1 <= money2).Should().BeTrue();
        (money1 <= money3).Should().BeTrue();
        (money2 <= money1).Should().BeFalse();
    }

    [Fact]
    public void Comparing_greater_than_or_equal()
    {
        var money1 = new Money(2.1m);
        var money2 = new Money(2.2m);
        var money3 = new Money(2.2m);

        (money2 >= money1).Should().BeTrue();
        (money2 >= money3).Should().BeTrue();
        (money1 >= money2).Should().BeFalse();
    }

    [Fact]
    public void CompareTo_with_null_throws_ArgumentNullException()
    {
        var money = new Money(100m);

        var act = () => money.CompareTo(null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CompareTo_with_different_type_throws_ArgumentException()
    {
        var address = new Address("Street", "City");
        var money = new Money(100m);

        var act = () => address.CompareTo(money);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Cannot compare objects of different types*");
    }

    [Fact]
    public void CompareTo_with_equal_values_returns_zero()
    {
        var money1 = new Money(100m);
        var money2 = new Money(100m);

        money1.CompareTo(money2).Should().Be(0);
    }

    #endregion

    #region Null Comparison Operator Edge Cases

    [Fact]
    public void LessThan_null_on_left_with_value_on_right_is_false()
    {
        Address? nullAddress = null;
        var address = new Address("Street", "City");

        (nullAddress < address).Should().BeFalse();
    }

    [Fact]
    public void LessThan_null_on_both_sides_is_false()
    {
        Address? left = null;
        Address? right = null;

        (left < right).Should().BeFalse();
    }

    [Fact]
    public void GreaterThan_null_on_left_is_always_false()
    {
        Address? nullAddress = null;
        var address = new Address("Street", "City");

        (nullAddress > address).Should().BeFalse();
    }

    [Fact]
    public void LessThanOrEqual_null_on_left_with_value_on_right_is_false()
    {
        Address? nullAddress = null;
        var address = new Address("Street", "City");

        (nullAddress <= address).Should().BeFalse();
    }

    [Fact]
    public void GreaterThanOrEqual_null_on_both_sides_is_true()
    {
        Address? left = null;
        Address? right = null;

        (left >= right).Should().BeTrue();
    }

    [Fact]
    public void GreaterThanOrEqual_null_on_left_value_on_right_is_false()
    {
        Address? nullAddress = null;
        var address = new Address("Street", "City");

        (nullAddress >= address).Should().BeFalse();
    }

    [Fact]
    public void LessThan_value_on_left_null_on_right_should_not_throw()
    {
        var address = new Address("Street", "City");
        Address? nullAddress = null;

        var act = () => address < nullAddress;

        act.Should().NotThrow();
        (address < nullAddress).Should().BeFalse();
    }

    [Fact]
    public void GreaterThan_value_on_left_null_on_right_should_not_throw()
    {
        var address = new Address("Street", "City");
        Address? nullAddress = null;

        var act = () => address > nullAddress;

        act.Should().NotThrow();
        (address > nullAddress).Should().BeFalse();
    }

    [Fact]
    public void LessThanOrEqual_value_on_left_null_on_right_should_not_throw()
    {
        var address = new Address("Street", "City");
        Address? nullAddress = null;

        var act = () => address <= nullAddress;

        act.Should().NotThrow();
        (address <= nullAddress).Should().BeFalse();
    }

    [Fact]
    public void GreaterThanOrEqual_value_on_left_null_on_right_should_not_throw()
    {
        var address = new Address("Street", "City");
        Address? nullAddress = null;

        var act = () => address >= nullAddress;

        act.Should().NotThrow();
        (address >= nullAddress).Should().BeFalse();
    }

    #endregion

    #region HashSet and Dictionary Usage

    [Fact]
    public void HashSet_UsesByValue()
    {
        var address1 = new Address("Street", "City");
        var address2 = new Address("Street", "City");
        var set = new HashSet<Address> { address1 };

        set.Contains(address2).Should().BeTrue();
        set.Should().HaveCount(1);
    }

    [Fact]
    public void Dictionary_UsesValueAsKey()
    {
        var address1 = new Address("Street", "City");
        var address2 = new Address("Street", "City");
        var dict = new Dictionary<Address, string>
        {
            [address1] = "Test"
        };

        dict[address2].Should().Be("Test");
    }

    #endregion

    #region CompareComponents Edge Cases

    [Fact]
    public void CompareTo_with_null_component_in_both_objects()
    {
        // Address with null-like component behavior
        var address1 = new AddressWithNullable("Street", null);
        var address2 = new AddressWithNullable("Street", null);

        address1.CompareTo(address2).Should().Be(0);
        address1.Equals(address2).Should().BeTrue();
    }

    [Fact]
    public void CompareTo_with_null_component_on_left_only()
    {
        var address1 = new AddressWithNullable("Street", null);
        var address2 = new AddressWithNullable("Street", "City");

        address1.CompareTo(address2).Should().BeLessThan(0);
    }

    [Fact]
    public void CompareTo_with_null_component_on_right_only()
    {
        var address1 = new AddressWithNullable("Street", "City");
        var address2 = new AddressWithNullable("Street", null);

        address1.CompareTo(address2).Should().BeGreaterThan(0);
    }

    #endregion

    #region Composite ValueObject with ScalarValueObject components

    [Fact]
    public void Composite_ValueObject_with_ScalarVO_components_are_equal()
    {
        var addr1 = new CompositeAddress(StreetName.Create("123 Main St"), CityName.Create("Springfield"));
        var addr2 = new CompositeAddress(StreetName.Create("123 Main St"), CityName.Create("Springfield"));

        addr1.Should().Be(addr2);
    }

    [Fact]
    public void Composite_ValueObject_with_ScalarVO_components_are_not_equal()
    {
        var addr1 = new CompositeAddress(StreetName.Create("123 Main St"), CityName.Create("Springfield"));
        var addr2 = new CompositeAddress(StreetName.Create("456 Oak Ave"), CityName.Create("Springfield"));

        addr1.Should().NotBe(addr2);
    }

    #endregion

    #region IComparable null handling

    [Fact]
    public void IComparable_CompareTo_Null_Returns_Positive()
    {
        var addr = new CompositeAddress(StreetName.Create("123 Main St"), CityName.Create("Springfield"));
        var comparable = (IComparable)addr;

        // Per .NET convention, a non-null instance is greater than null
        comparable.CompareTo(null).Should().BePositive();
    }

    [Fact]
    public void IComparable_CompareTo_WrongType_Throws()
    {
        var addr = new CompositeAddress(StreetName.Create("123 Main St"), CityName.Create("Springfield"));
        var comparable = (IComparable)addr;

        var act = () => comparable.CompareTo("not a ValueObject");
        act.Should().Throw<ArgumentException>();
    }

    #endregion
}

/// <summary>
/// Value object that allows null components for testing.
/// </summary>
internal class AddressWithNullable : ValueObject
{
    public string Street { get; }
    public string? City { get; }

    public AddressWithNullable(string street, string? city)
    {
        Street = street;
        City = city;
    }

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Street;
        yield return City; // Allow null for testing
    }
}

/// <summary>
/// Composite ValueObject containing ScalarValueObject properties.
/// Tests that scalar VOs can be yielded in GetEqualityComponents.
/// </summary>
internal class CompositeAddress : ValueObject
{
    public StreetName Street { get; }
    public CityName City { get; }

    public CompositeAddress(StreetName street, CityName city)
    {
        Street = street;
        City = city;
    }

    protected override IEnumerable<IComparable?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
    }
}

internal class StreetName : ScalarValueObject<StreetName, string>, IScalarValue<StreetName, string>
{
    private StreetName(string value) : base(value) { }

    public static Result<StreetName> TryCreate(string value, string? fieldName = null) =>
        string.IsNullOrWhiteSpace(value)
            ? Result.Failure<StreetName>(Error.Validation("Street is required", fieldName ?? "street"))
            : Result.Success(new StreetName(value));
}

internal class CityName : ScalarValueObject<CityName, string>, IScalarValue<CityName, string>
{
    private CityName(string value) : base(value) { }

    public static Result<CityName> TryCreate(string value, string? fieldName = null) =>
        string.IsNullOrWhiteSpace(value)
            ? Result.Failure<CityName>(Error.Validation("City is required", fieldName ?? "city"))
            : Result.Success(new CityName(value));
}