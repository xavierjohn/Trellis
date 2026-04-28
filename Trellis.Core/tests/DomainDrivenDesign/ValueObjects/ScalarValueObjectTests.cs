namespace Trellis.Core.Tests.DomainDrivenDesign.ValueObjects;

using System.Collections.Generic;
using System.Globalization;

public class ScalarValueObjectTests
{
    #region Test Value Objects

    internal class PasswordSimple : ScalarValueObject<PasswordSimple, string>, IScalarValue<PasswordSimple, string>
    {
        public PasswordSimple(string value) : base(value) { }

        public static Result<PasswordSimple> TryCreate(string? value, string? fieldName = null) =>
            Result.Ok(new PasswordSimple(value!));
    }

    internal class DerivedPasswordSimple : PasswordSimple
    {
        public DerivedPasswordSimple(string value) : base(value) { }

        public static new Result<DerivedPasswordSimple> TryCreate(string value, string? fieldName = null) =>
            Result.Ok(new DerivedPasswordSimple(value));
    }

    internal class MoneySimple : ScalarValueObject<MoneySimple, decimal>, IScalarValue<MoneySimple, decimal>
    {
        public MoneySimple(decimal value) : base(value) { }

        public static Result<MoneySimple> TryCreate(decimal value, string? fieldName = null) =>
            Result.Ok(new MoneySimple(value));

        public static Result<MoneySimple> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();

        protected override IEnumerable<IComparable?> GetEqualityComponents()
        {
            yield return Math.Round(Value, 2);
        }
    }

    internal class CustomerId : ScalarValueObject<CustomerId, Guid>, IScalarValue<CustomerId, Guid>
    {
        public CustomerId(Guid value) : base(value) { }

        public static Result<CustomerId> TryCreate(Guid value, string? fieldName = null) =>
            Result.Ok(new CustomerId(value));

        public static Result<CustomerId> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    internal class Quantity : ScalarValueObject<Quantity, int>, IScalarValue<Quantity, int>
    {
        public Quantity(int value) : base(value) { }

        public static Result<Quantity> TryCreate(int value, string? fieldName = null) =>
            Result.Ok(new Quantity(value));

        public static Result<Quantity> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    internal class CharWrapper : ScalarValueObject<CharWrapper, char>, IScalarValue<CharWrapper, char>
    {
        public CharWrapper(char value) : base(value) { }

        public static Result<CharWrapper> TryCreate(char value, string? fieldName = null) =>
            Result.Ok(new CharWrapper(value));

        public static Result<CharWrapper> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    internal class DateTimeWrapper : ScalarValueObject<DateTimeWrapper, DateTime>, IScalarValue<DateTimeWrapper, DateTime>
    {
        public DateTimeWrapper(DateTime value) : base(value) { }

        public static Result<DateTimeWrapper> TryCreate(DateTime value, string? fieldName = null) =>
            Result.Ok(new DateTimeWrapper(value));

        public static Result<DateTimeWrapper> TryCreate(string? value, string? fieldName = null) =>
            throw new NotImplementedException();
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void Two_ScalarValueObject_of_the_same_content_are_equal()
    {
        var password1 = new PasswordSimple("Password");
        var password2 = new PasswordSimple("Password");

        password1.Equals(password2).Should().BeTrue();
        (password1 == password2).Should().BeTrue();
        (password1 != password2).Should().BeFalse();
        password1.GetHashCode().Equals(password2.GetHashCode()).Should().BeTrue();
    }

    [Fact]
    public void Two_ScalarValueObject_of_the_different_content_are_not_equal()
    {
        var password1 = new PasswordSimple("Password1");
        var password2 = new PasswordSimple("Password2");

        password1.Equals(password2).Should().BeFalse();
        (password1 == password2).Should().BeFalse();
        (password1 != password2).Should().BeTrue();
        password1.GetHashCode().Equals(password2.GetHashCode()).Should().BeFalse();
    }

    [Fact]
    public void Derived_ScalarValueObject_are_not_equal()
    {
        var password = new PasswordSimple("Password");
        var derivedPassword = new DerivedPasswordSimple("Password");

        password.Equals(derivedPassword).Should().BeFalse();
        derivedPassword.Equals(password).Should().BeFalse();
        (password == derivedPassword).Should().BeFalse();
    }

    [Fact]
    public void NullAble_ScalarValueObject_can_be_compared_to_null()
    {
        PasswordSimple? password = null;

        (password == null).Should().BeTrue();
        (password != null).Should().BeFalse();
        (null == password).Should().BeTrue();
        (null != password).Should().BeFalse();
    }

    [Fact]
    public void Custom_equality_with_rounding()
    {
        var money1 = new MoneySimple(2.2222m);
        var money2 = new MoneySimple(2.22m);

        money1.Equals(money2).Should().BeTrue();
        money1.GetHashCode().Equals(money2.GetHashCode()).Should().BeTrue();
    }

    #endregion

    #region Sorting Tests

    [Fact]
    public void ScalarValueObject_is_sorted()
    {
        var one = new MoneySimple(1);
        var two = new MoneySimple(2);
        var three = new MoneySimple(3);
        var moneys = new List<MoneySimple> { two, one, three };

        moneys.Sort();

        moneys.Should().Equal([one, two, three]);
    }

    #endregion

    #region Value Access and Implicit Conversion

    [Fact]
    public void Value_ReturnsWrappedValue()
    {
        var guid = Guid.NewGuid();
        var customerId = new CustomerId(guid);

        customerId.Value.Should().Be(guid);
    }

    [Fact]
    public void ImplicitConversion_ToUnderlyingType_ReturnsValue()
    {
        var guid = Guid.NewGuid();
        var customerId = new CustomerId(guid);

        Guid convertedGuid = customerId;

        convertedGuid.Should().Be(guid);
    }

    [Fact]
    public void ToString_ReturnsValueAsString()
    {
        var password = new PasswordSimple("secret123");

        password.ToString(System.Globalization.CultureInfo.InvariantCulture).Should().Be("secret123");
    }

    [Fact]
    public void ToString_WithProvider_ReturnsValueAsString()
    {
        var quantity = new Quantity(1234);

        quantity.ToString(System.Globalization.CultureInfo.InvariantCulture).Should().Be("1234");
    }

    #endregion

    #region IConvertible Implementation

    [Fact]
    public void GetTypeCode_ReturnsCorrectTypeCode()
    {
        var quantity = new Quantity(42);

        ((IConvertible)quantity).GetTypeCode().Should().Be(TypeCode.Int32);
    }

    [Fact]
    public void ToBoolean_ConvertsCorrectly()
    {
        var zero = new Quantity(0);
        var one = new Quantity(1);

        ((IConvertible)zero).ToBoolean(null).Should().BeFalse();
        ((IConvertible)one).ToBoolean(null).Should().BeTrue();
    }

    [Fact]
    public void ToByte_ConvertsCorrectly()
    {
        var quantity = new Quantity(255);

        ((IConvertible)quantity).ToByte(null).Should().Be(255);
    }

    [Fact]
    public void ToChar_ConvertsCorrectly()
    {
        var charWrapper = new CharWrapper('A');

        ((IConvertible)charWrapper).ToChar(null).Should().Be('A');
    }

    [Fact]
    public void ToDateTime_ConvertsCorrectly()
    {
        var date = new DateTime(2024, 1, 15, 10, 30, 0);
        var dateWrapper = new DateTimeWrapper(date);

        ((IConvertible)dateWrapper).ToDateTime(null).Should().Be(date);
    }

    [Fact]
    public void ToDecimal_ConvertsCorrectly()
    {
        var money = new MoneySimple(98.6m);

        ((IConvertible)money).ToDecimal(null).Should().Be(98.6m);
    }

    [Fact]
    public void ToDouble_ConvertsCorrectly()
    {
        var quantity = new Quantity(42);

        ((IConvertible)quantity).ToDouble(null).Should().Be(42.0);
    }

    [Fact]
    public void ToInt16_ConvertsCorrectly()
    {
        var quantity = new Quantity(100);

        ((IConvertible)quantity).ToInt16(null).Should().Be(100);
    }

    [Fact]
    public void ToInt32_ConvertsCorrectly()
    {
        var quantity = new Quantity(42);

        ((IConvertible)quantity).ToInt32(null).Should().Be(42);
    }

    [Fact]
    public void ToInt64_ConvertsCorrectly()
    {
        var quantity = new Quantity(42);

        ((IConvertible)quantity).ToInt64(null).Should().Be(42L);
    }

    [Fact]
    public void ToSByte_ConvertsCorrectly()
    {
        var quantity = new Quantity(127);

        ((IConvertible)quantity).ToSByte(null).Should().Be(127);
    }

    [Fact]
    public void ToSingle_ConvertsCorrectly()
    {
        var quantity = new Quantity(42);

        ((IConvertible)quantity).ToSingle(null).Should().Be(42f);
    }

    [Fact]
    public void ToUInt16_ConvertsCorrectly()
    {
        var quantity = new Quantity(1000);

        ((IConvertible)quantity).ToUInt16(null).Should().Be(1000);
    }

    [Fact]
    public void ToUInt32_ConvertsCorrectly()
    {
        var quantity = new Quantity(42);

        ((IConvertible)quantity).ToUInt32(null).Should().Be(42u);
    }

    [Fact]
    public void ToUInt64_ConvertsCorrectly()
    {
        var quantity = new Quantity(42);

        ((IConvertible)quantity).ToUInt64(null).Should().Be(42ul);
    }

    [Fact]
    public void ToType_ConvertsCorrectly()
    {
        var quantity = new Quantity(42);

        var result = ((IConvertible)quantity).ToType(typeof(long), null);

        result.Should().BeOfType<long>().Which.Should().Be(42L);
    }

    #endregion

    #region HashSet and Dictionary Usage

    [Fact]
    public void HashSet_UsesByValue()
    {
        var guid = Guid.NewGuid();
        var set = new HashSet<CustomerId> { new(guid) };

        set.Contains(new CustomerId(guid)).Should().BeTrue();
        set.Should().HaveCount(1);
    }

    [Fact]
    public void Dictionary_UsesAsKey()
    {
        var guid = Guid.NewGuid();
        var dict = new Dictionary<CustomerId, string>
        {
            [new CustomerId(guid)] = "Test Value"
        };

        dict[new CustomerId(guid)].Should().Be("Test Value");
    }

    #endregion

    #region IFormattable — Error factory integration

    [Fact]
    public void CustomerId_AsErrorInstance_FormatsToInvariantGuidString()
    {
        var guid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var customerId = new CustomerId(guid);

        var error = new Error.NotFound(new ResourceRef("Resource", customerId.ToString(CultureInfo.InvariantCulture))) { Detail = "Customer not found." };

        error.Resource.Id.Should().Be("01234567-89ab-cdef-0123-456789abcdef");
    }

    [Fact]
    public void Quantity_AsErrorInstance_FormatsToInvariantString()
    {
        var quantity = new Quantity(42);

        var error = new Error.Conflict(null, quantity.ToString(CultureInfo.InvariantCulture)) { Detail = "Insufficient quantity." };

        error.ReasonCode.Should().Be("42");
    }

    [Fact]
    public void MoneySimple_AsErrorInstance_FormatsToInvariantDecimalString()
    {
        var money = new MoneySimple(1234.56m);

        var error = new Error.Conflict(null, money.ToString(CultureInfo.InvariantCulture)) { Detail = "Amount mismatch." };

        error.ReasonCode.Should().Be("1234.56");
    }

    #endregion
}