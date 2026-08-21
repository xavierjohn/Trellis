namespace Trellis.Core.Tests.DomainDrivenDesign.ValueObjects;

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
    public void CompareTo_WithNull_ReturnsPositive()
    {
        var money = new Money(100m);

        money.CompareTo(null).Should().BePositive();
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

    [Fact]
    public void CompareTo_WithDifferentRuntimeComponentTypes_OrdersByComponentTypeName()
    {
        var number = new ConditionallyTypedComponentValueObject(1);
        var text = new ConditionallyTypedComponentValueObject("1");
        var expectedSign = Math.Sign(string.CompareOrdinal(
            typeof(int).AssemblyQualifiedName,
            typeof(string).AssemblyQualifiedName));

        Math.Sign(number.CompareTo(text)).Should().Be(expectedSign);
        Math.Sign(text.CompareTo(number)).Should().Be(-expectedSign);
    }

    #endregion

    #region Null Comparison Operator Edge Cases

    [Fact]
    public void LessThan_NullOnLeftWithValueOnRight_IsTrue()
    {
        Address? nullAddress = null;
        var address = new Address("Street", "City");

        (nullAddress < address).Should().BeTrue();
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
    public void LessThanOrEqual_NullOnLeftWithValueOnRight_IsTrue()
    {
        Address? nullAddress = null;
        var address = new Address("Street", "City");

        (nullAddress <= address).Should().BeTrue();
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
    public void LessThan_ValueOnLeftNullOnRight_IsFalse()
    {
        var address = new Address("Street", "City");
        Address? nullAddress = null;

        (address < nullAddress).Should().BeFalse();
    }

    [Fact]
    public void GreaterThan_ValueOnLeftNullOnRight_IsTrue()
    {
        var address = new Address("Street", "City");
        Address? nullAddress = null;

        (address > nullAddress).Should().BeTrue();
    }

    [Fact]
    public void LessThanOrEqual_ValueOnLeftNullOnRight_IsFalse()
    {
        var address = new Address("Street", "City");
        Address? nullAddress = null;

        (address <= nullAddress).Should().BeFalse();
    }

    [Fact]
    public void GreaterThanOrEqual_ValueOnLeftNullOnRight_IsTrue()
    {
        var address = new Address("Street", "City");
        Address? nullAddress = null;

        (address >= nullAddress).Should().BeTrue();
    }

    [Fact]
    public void LessThan_WithDifferentRuntimeTypes_ThrowsArgumentException()
    {
        var address = new Address("Street", "City");
        var money = new Money(100m);

        var act = () => address < money;

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Cannot compare objects of different types*");
    }

    [Fact]
    public void LessThanOrEqual_WithDifferentRuntimeTypes_ThrowsArgumentException()
    {
        var address = new Address("Street", "City");
        var money = new Money(100m);

        var act = () => address <= money;

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Cannot compare objects of different types*");
    }

    [Fact]
    public void GreaterThan_WithDifferentRuntimeTypes_ThrowsArgumentException()
    {
        var address = new Address("Street", "City");
        var money = new Money(100m);

        var act = () => address > money;

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Cannot compare objects of different types*");
    }

    [Fact]
    public void GreaterThanOrEqual_WithDifferentRuntimeTypes_ThrowsArgumentException()
    {
        var address = new Address("Street", "City");
        var money = new Money(100m);

        var act = () => address >= money;

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Cannot compare objects of different types*");
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

    #region Hash Cache

    [Fact]
    public void GetHashCode_is_stable_across_repeated_calls()
    {
        var address = new Address("Street", "City");

        var first = address.GetHashCode();

        for (var i = 0; i < 10; i++)
            address.GetHashCode().Should().Be(first);
    }

    [Fact]
    public void GetHashCode_collects_components_only_once_across_repeated_calls()
    {
        var address = new CountingComponentsValueObject("a", "b");

        for (var i = 0; i < 10; i++)
            address.GetHashCode();

        address.EnumerationCount.Should().Be(1, "the cached hash must short-circuit component collection");
    }

    [Fact]
    public void GetHashCode_under_concurrent_first_access_never_publishes_a_torn_value()
    {
        // The lazy hash cache is written without a lock. It must be a single atomic store so a
        // racing reader either sees "not computed" or the whole value - never half of it.
        const int Threads = 16;
        const int Rounds = 400;

        for (var round = 0; round < Rounds; round++)
        {
            var subject = new SlowHashingValueObject("street", "city");
            var expected = new SlowHashingValueObject("street", "city").GetHashCode();
            var observed = new int[Threads];

            using var gate = new Barrier(Threads);
            var workers = new Thread[Threads];
            for (var t = 0; t < Threads; t++)
            {
                var slot = t;
                workers[slot] = new Thread(() =>
                {
                    gate.SignalAndWait();
                    observed[slot] = subject.GetHashCode();
                });
                workers[slot].Start();
            }

            foreach (var worker in workers)
                worker.Join();

            observed.Should().AllBeEquivalentTo(expected, "round {0} observed a torn or inconsistent hash", round);
        }
    }

    [Fact]
    public void Equals_returns_true_for_the_same_instance_without_enumerating_components()
    {
        var address = new CountingComponentsValueObject("a", "b");

        address.Equals(address).Should().BeTrue();

        address.EnumerationCount.Should().Be(0, "reference equality must short-circuit before component enumeration");
    }

    [Fact]
    public void Equals_of_distinct_but_equal_instances_does_not_allocate()
    {
        var left = new Address("Street", "City");
        var right = new Address("Street", "City");

        Warm(() => left.Equals(right));

        MeasureAllocations(() => left.Equals(right)).Should().Be(0);
    }

    [Fact]
    public void Equals_of_unequal_instances_does_not_allocate()
    {
        var left = new Address("Street", "City");
        var right = new Address("Other", "City");

        Warm(() => left.Equals(right));

        MeasureAllocations(() => left.Equals(right)).Should().Be(0);
    }

    [Fact]
    public void CompareTo_does_not_allocate()
    {
        var left = new Address("Street", "City");
        var right = new Address("Other", "City");

        Warm(() => left.CompareTo(right));

        MeasureAllocations(() => left.CompareTo(right)).Should().Be(0);
    }

    [Fact]
    public void Value_objects_with_more_components_than_the_inline_buffer_still_compare_correctly()
    {
        var left = new WideValueObject(12);
        var right = new WideValueObject(12);
        var different = new WideValueObject(12, differentAtIndex: 11);

        left.Equals(right).Should().BeTrue();
        left.GetHashCode().Should().Be(right.GetHashCode());
        left.CompareTo(right).Should().Be(0);

        left.Equals(different).Should().BeFalse();
        left.CompareTo(different).Should().BeNegative();
    }

    /// <summary>Keeps measured results alive without boxing, so the JIT cannot elide the call.</summary>
    private static class Sink<T>
    {
        public static T? Value;
    }

    private static void Warm<T>(Func<T> operation)
    {
        for (var i = 0; i < 64; i++) Sink<T>.Value = operation();
    }

    private static long MeasureAllocations<T>(Func<T> operation)
    {
        const int Iterations = 200;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Iterations; i++) Sink<T>.Value = operation();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void CompareTo_returns_zero_for_the_same_instance_without_enumerating_components()
    {
        var address = new CountingComponentsValueObject("a", "b");

        address.CompareTo(address).Should().Be(0);

        address.EnumerationCount.Should().Be(0, "reference equality must short-circuit before component enumeration");
    }

    #endregion
}

/// <summary>
/// Value object with more components than the base class's inline buffer holds, so the
/// pooled-growth path is exercised.
/// </summary>
internal sealed class WideValueObject : ValueObject
{
    private readonly string[] parts;

    public WideValueObject(int count, int differentAtIndex = -1)
    {
        parts = new string[count];
        for (var i = 0; i < count; i++)
            parts[i] = i == differentAtIndex ? "zzz" : $"part-{i:D2}";
    }

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        foreach (var part in parts)
            components.Add(part);
    }
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

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Street);
        components.Add(City); // Allow null for testing
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

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        components.Add(Street);
        components.Add(City);
    }
}

internal class StreetName : ScalarValueObject<StreetName, string>, IScalarValue<StreetName, string>
{
    private StreetName(string value) : base(value) { }

    public static Result<StreetName> TryCreate(string? value, string? fieldName = null) =>
        string.IsNullOrWhiteSpace(value)
            ? Result.Fail<StreetName>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "street"), ValidationCodes.Unspecified) { Detail = "Street is required" })))
            : Result.Ok(new StreetName(value));
}

internal class CityName : ScalarValueObject<CityName, string>, IScalarValue<CityName, string>
{
    private CityName(string value) : base(value) { }

    public static Result<CityName> TryCreate(string? value, string? fieldName = null) =>
        string.IsNullOrWhiteSpace(value)
            ? Result.Fail<CityName>(new Error.InvalidInput(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(fieldName ?? "city"), ValidationCodes.Unspecified) { Detail = "City is required" })))
            : Result.Ok(new CityName(value));
}

internal sealed class ConditionallyTypedComponentValueObject(IComparable component) : ValueObject
{
    public IComparable Component { get; } = component;

    protected override void GetEqualityComponents(ref EqualityComponents components)
        => components.Add(Component);
}

/// <summary>
/// Value object whose equality-component enumeration is deliberately slow, widening the window
/// in which a concurrent reader could observe a partially-published lazy hash cache.
/// </summary>
internal sealed class SlowHashingValueObject(string a, string b) : ValueObject
{
    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        for (var i = 0; i < 40; i++) Thread.SpinWait(20);
        components.Add(a);
        components.Add(b);
    }
}

/// <summary>
/// Records how many times its equality components were enumerated so tests can assert that
/// reference-equality fast paths short-circuit before any component work happens.
/// </summary>
internal sealed class CountingComponentsValueObject(string a, string b) : ValueObject
{
    private int enumerationCount;

    public int EnumerationCount => enumerationCount;

    protected override void GetEqualityComponents(ref EqualityComponents components)
    {
        Interlocked.Increment(ref enumerationCount);
        components.Add(a);
        components.Add(b);
    }
}