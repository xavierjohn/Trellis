using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using System.Reflection;
using System.Text.Json;
using Trellis;
using Trellis.Testing;

public class MoneyTests
{
    #region Creation Tests

    [Theory]
    [InlineData(99.99, "USD")]
    [InlineData(85.50, "EUR")]
    [InlineData(10000, "JPY")]
    [InlineData(45.123, "BHD")]
    public void Can_create_valid_Money(decimal amount, string currency)
    {
        // Act
        var result = Money.TryCreate(amount, currency);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Currency.Value.Should().Be(currency);
    }

    [Fact]
    public void Cannot_create_Money_with_negative_amount()
    {
        // Act
        var result = Money.TryCreate(-50.00m, "USD");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Amount cannot be negative.");
    }

    [Fact]
    public void Cannot_create_Money_with_invalid_currency()
    {
        // Act
        var result = Money.TryCreate(100.00m, "INVALID");

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Cannot_create_Money_with_invalid_currency_uses_custom_field_name()
    {
        // Act
        var result = Money.TryCreate(100.00m, "INVALID", "unitPrice");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Field.Path.Should().Be("/unitPrice");
    }

    [Fact]
    public void Create_returns_Money_for_valid_input()
    {
        // Act
        var money = Money.Create(99.99m, "USD");

        // Assert
        money.Amount.Should().Be(99.99m);
        money.Currency.Value.Should().Be("USD");
    }

    [Fact]
    public void Create_throws_for_negative_amount()
    {
        // Act
        Action act = () => Money.Create(-50.00m, "USD");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create Money: Amount cannot be negative.");
    }

    [Fact]
    public void Create_throws_for_invalid_currency()
    {
        // Act
        Action act = () => Money.Create(100.00m, "INVALID");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Failed to create Money:*");
    }

    [Theory]
    [InlineData(19.995, "USD", 20.00)]  // USD rounds to 2 decimals
    [InlineData(10000.5, "JPY", 10001)] // JPY rounds to 0 decimals
    [InlineData(45.1235, "BHD", 45.124)] // BHD rounds to 3 decimals
    public void Money_rounds_to_currency_decimal_places(decimal input, string currency, decimal expected)
    {
        // Act
        var result = Money.TryCreate(input, currency);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(expected);
    }

    #endregion

    #region Arithmetic Tests

    [Fact]
    public void Can_add_Money_with_same_currency()
    {
        // Arrange
        var left = Money.TryCreate(50.25m, "USD").Unwrap();
        var right = Money.TryCreate(25.75m, "USD").Unwrap();

        // Act
        var result = left.Add(right);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(76.00m);
        result.Unwrap().Currency.Value.Should().Be("USD");
    }

    [Fact]
    public void Cannot_add_Money_with_different_currency()
    {
        // Arrange
        var left = Money.TryCreate(50.00m, "USD").Unwrap();
        var right = Money.TryCreate(40.00m, "EUR").Unwrap();

        // Act
        var result = left.Add(right);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Cannot add EUR to USD.");
    }

    [Fact]
    public void Can_subtract_Money_with_same_currency()
    {
        // Arrange
        var left = Money.TryCreate(100.00m, "USD").Unwrap();
        var right = Money.TryCreate(35.50m, "USD").Unwrap();

        // Act
        var result = left.Subtract(right);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(64.50m);
    }

    [Fact]
    public void Subtract_resulting_in_negative_amount_fails()
    {
        // Arrange
        var left = Money.TryCreate(40.00m, "USD").Unwrap();
        var right = Money.TryCreate(100.00m, "USD").Unwrap();

        // Act
        var result = left.Subtract(right);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Subtraction would result in a negative amount.");
    }

    [Fact]
    public void Cannot_subtract_Money_with_different_currency()
    {
        // Arrange
        var left = Money.TryCreate(100.00m, "USD").Unwrap();
        var right = Money.TryCreate(35.50m, "GBP").Unwrap();

        // Act
        var result = left.Subtract(right);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Cannot subtract GBP from USD.");
    }

    [Fact]
    public void Can_multiply_Money_by_decimal()
    {
        // Arrange
        var money = Money.TryCreate(19.99m, "USD").Unwrap();

        // Act
        var result = money.Multiply(3.5m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(69.97m);
        result.Unwrap().Currency.Value.Should().Be("USD");
    }

    [Fact]
    public void Can_multiply_Money_by_integer()
    {
        // Arrange
        var money = Money.TryCreate(12.50m, "USD").Unwrap();

        // Act
        var result = money.Multiply(5);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(62.50m);
    }

    [Fact]
    public void Cannot_multiply_Money_by_negative()
    {
        // Arrange
        var money = Money.TryCreate(50.00m, "USD").Unwrap();

        // Act
        var resultDecimal = money.Multiply(-2m);
        var resultInt = money.Multiply(-3);

        // Assert
        resultDecimal.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)resultDecimal.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Multiplier cannot be negative.");

        resultInt.IsFailure.Should().BeTrue();
        var validationInt = (Error.UnprocessableContent)resultInt.UnwrapError();
        validationInt.Fields[0].Detail.Should().Be("Quantity cannot be negative.");
    }

    [Fact]
    public void Can_divide_Money_by_decimal()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Unwrap();

        // Act
        var result = money.Divide(3m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(33.33m);
    }

    [Fact]
    public void Can_divide_Money_by_integer()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Unwrap();

        // Act
        var result = money.Divide(4);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(25.00m);
    }

    [Fact]
    public void Cannot_divide_Money_by_zero()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Unwrap();

        // Act
        var result = money.Divide(0m);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Divisor must be positive.");
    }

    [Fact]
    public void Cannot_divide_Money_by_negative_integer()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Unwrap();

        // Act
        var result = money.Divide(-2);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("Divisor must be positive.");
    }

    [Fact]
    public void Add_Overflow_ReturnsFailure()
    {
        var a = Money.Create(decimal.MaxValue, "USD");
        var b = Money.Create(1m, "USD");

        var result = a.Add(b);

        result.IsFailure.Should().BeTrue();
    }

    #endregion

    #region Allocation Tests

    [Fact]
    public void Can_allocate_Money_equally()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Unwrap();

        // Act
        var result = money.Allocate(1, 1, 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().HaveCount(3);
        result.Unwrap()[0].Amount.Should().Be(33.34m); // Gets the remainder
        result.Unwrap()[1].Amount.Should().Be(33.33m);
        result.Unwrap()[2].Amount.Should().Be(33.33m);
        result.Unwrap().Sum(m => m.Amount).Should().Be(100.00m); // No money lost
    }

    [Fact]
    public void Can_allocate_Money_by_ratio()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Unwrap();

        // Act
        var result = money.Allocate(1, 2, 1); // 25%, 50%, 25%

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Should().HaveCount(3);
        result.Unwrap()[0].Amount.Should().Be(25.00m);
        result.Unwrap()[1].Amount.Should().Be(50.00m);
        result.Unwrap()[2].Amount.Should().Be(25.00m);
    }

    [Fact]
    public void Cannot_allocate_Money_with_empty_ratios()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Unwrap();

        // Act
        var result = money.Allocate();

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("At least one ratio required.");
    }

    [Fact]
    public void Cannot_allocate_Money_with_negative_ratio()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Unwrap();

        // Act
        var result = money.Allocate(1, -2, 1);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (Error.UnprocessableContent)result.UnwrapError();
        validation.Fields[0].Detail.Should().Be("All ratios must be positive.");
    }

    #endregion

    #region Comparison Tests

    [Fact]
    public void Can_compare_Money_IsGreaterThan()
    {
        // Arrange
        var left = Money.TryCreate(100.00m, "USD").Unwrap();
        var right = Money.TryCreate(50.00m, "USD").Unwrap();

        // Act & Assert
        left.IsGreaterThan(right).Should().BeTrue();
        right.IsGreaterThan(left).Should().BeFalse();
    }

    [Fact]
    public void Can_compare_Money_IsLessThan()
    {
        // Arrange
        var left = Money.TryCreate(25.00m, "USD").Unwrap();
        var right = Money.TryCreate(50.00m, "USD").Unwrap();

        // Act & Assert
        left.IsLessThan(right).Should().BeTrue();
        right.IsLessThan(left).Should().BeFalse();
    }

    [Fact]
    public void Can_compare_Money_IsGreaterThanOrEqual()
    {
        // Arrange
        var left = Money.TryCreate(100.00m, "USD").Unwrap();
        var right = Money.TryCreate(50.00m, "USD").Unwrap();
        var equal = Money.TryCreate(100.00m, "USD").Unwrap();

        // Act & Assert
        left.IsGreaterThanOrEqual(right).Should().BeTrue();
        left.IsGreaterThanOrEqual(equal).Should().BeTrue();
        right.IsGreaterThanOrEqual(left).Should().BeFalse();
    }

    [Fact]
    public void Can_compare_Money_IsLessThanOrEqual()
    {
        // Arrange
        var left = Money.TryCreate(25.00m, "USD").Unwrap();
        var right = Money.TryCreate(50.00m, "USD").Unwrap();
        var equal = Money.TryCreate(25.00m, "USD").Unwrap();

        // Act & Assert
        left.IsLessThanOrEqual(right).Should().BeTrue();
        left.IsLessThanOrEqual(equal).Should().BeTrue();
        right.IsLessThanOrEqual(left).Should().BeFalse();
    }

    [Fact]
    public void Cannot_compare_Money_with_different_currency()
    {
        // Arrange
        var left = Money.TryCreate(100.00m, "USD").Unwrap();
        var right = Money.TryCreate(80.00m, "EUR").Unwrap();

        // Act & Assert
        left.IsGreaterThan(right).Should().BeFalse();
        left.IsLessThan(right).Should().BeFalse();
        left.IsGreaterThanOrEqual(right).Should().BeFalse();
        left.IsLessThanOrEqual(right).Should().BeFalse();
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void Two_Money_with_same_amount_and_currency_should_be_equal()
    {
        // Arrange
        var a = Money.TryCreate(50.00m, "USD").Unwrap();
        var b = Money.TryCreate(50.00m, "USD").Unwrap();

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_Money_with_different_amount_should_not_be_equal()
    {
        // Arrange
        var a = Money.TryCreate(50.00m, "USD").Unwrap();
        var b = Money.TryCreate(75.00m, "USD").Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Two_Money_with_different_currency_should_not_be_equal()
    {
        // Arrange
        var a = Money.TryCreate(50.00m, "USD").Unwrap();
        var b = Money.TryCreate(50.00m, "EUR").Unwrap();

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var money = Money.TryCreate(99.99m, "USD").Unwrap();

        // Act
        var json = JsonSerializer.Serialize(money);
        var deserialized = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!["amount"].GetDecimal().Should().Be(99.99m);
        deserialized["currency"].GetString().Should().Be("USD");
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange
        var json = "{\"amount\":85.50,\"currency\":\"EUR\"}";

        // Act
        var money = JsonSerializer.Deserialize<Money>(json)!;

        // Assert
        money.Amount.Should().Be(85.50m);
        money.Currency.Value.Should().Be("EUR");
    }

    [Fact]
    public void Cannot_deserialize_Money_with_invalid_currency()
    {
        // Arrange
        var json = "{\"amount\":100.00,\"currency\":\"INVALID\"}";

        // Act
        Action act = () => JsonSerializer.Deserialize<Money>(json);

        // Assert
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Cannot_deserialize_Money_without_currency()
    {
        // Arrange
        var json = "{\"amount\":100.00}";

        // Act
        Action act = () => JsonSerializer.Deserialize<Money>(json);

        // Assert
        act.Should().Throw<JsonException>()
            .WithMessage("Required property 'currency' is missing.");
    }

    #region Taxonomy Tests

    [Fact]
    public void Money_IsStructuredValueObject_NotScalarValueObject()
    {
        typeof(Money).IsAssignableTo(typeof(IScalarValue<,>)).Should().BeFalse();
        typeof(Money).BaseType.Should().Be<ValueObject>();
        typeof(Money).GetProperty("Value", BindingFlags.Instance | BindingFlags.Public)
            .Should().BeNull("Money exposes Amount and Currency as structured components instead of one canonical scalar Value");
    }

    [Theory]
    [MemberData(nameof(ScalarValueObjectAuditCases))]
    public void BuiltInScalarValueObjects_FollowScalarContract(Type valueObjectType, Type primitiveType)
    {
        valueObjectType.BaseType.Should().NotBeNull();
        valueObjectType.BaseType!.IsGenericType.Should().BeTrue();
        (valueObjectType.BaseType!.GetGenericTypeDefinition() == typeof(ScalarValueObject<,>)).Should().BeTrue();
        valueObjectType.BaseType!.GetGenericArguments().Should().Equal([valueObjectType, primitiveType]);

        valueObjectType.GetInterfaces().Should().Contain(interfaceType =>
            interfaceType.IsGenericType
            && interfaceType.GetGenericTypeDefinition() == typeof(IScalarValue<,>)
            && interfaceType.GetGenericArguments()[0] == valueObjectType
            && interfaceType.GetGenericArguments()[1] == primitiveType);

        var valueProperty = valueObjectType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        valueProperty.Should().NotBeNull();
        valueProperty!.PropertyType.Should().Be(primitiveType);

        valueObjectType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Should().Contain(method => method.Name == nameof(Money.TryCreate)
                && method.ReturnType == typeof(Result<>).MakeGenericType(valueObjectType));

        valueObjectType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Should().Contain(method => method.Name == nameof(Money.Create)
                && method.ReturnType == valueObjectType);
    }

    public static TheoryData<Type, Type> ScalarValueObjectAuditCases =>
        new()
        {
            { typeof(EmailAddress), typeof(string) },
            { typeof(PhoneNumber), typeof(string) },
            { typeof(Url), typeof(string) },
            { typeof(Slug), typeof(string) },
            { typeof(CurrencyCode), typeof(string) },
            { typeof(CountryCode), typeof(string) },
            { typeof(LanguageCode), typeof(string) },
            { typeof(IpAddress), typeof(string) },
            { typeof(Hostname), typeof(string) },
            { typeof(Age), typeof(int) },
            { typeof(Percentage), typeof(decimal) },
        };

    #endregion
    #endregion

    #region Utility Tests

    [Fact]
    public void Zero_creates_zero_Money()
    {
        // Act
        var result = Money.Zero("EUR");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(0m);
        result.Unwrap().Currency.Value.Should().Be("EUR");
    }

    [Fact]
    public void Zero_defaults_to_USD()
    {
        // Act
        var result = Money.Zero();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Currency.Value.Should().Be("USD");
    }

    [Fact]
    public void ToString_returns_formatted_amount_with_currency()
    {
        // Arrange
        var money = Money.TryCreate(99.99m, "USD").Unwrap();

        // Act
        var str = money.ToString();

        // Assert
        str.Should().Be("99.99 USD");
    }

    #endregion

    #region FieldName Edge Cases

    [Fact]
    public void TryCreate_with_empty_string_fieldName_should_not_throw()
    {
        var act = () => Money.TryCreate(-1m, "USD", fieldName: "");

        act.Should().NotThrow();
    }

    #endregion

    #region Sum Tests

    [Fact]
    public void Sum_SingleItem_ReturnsThatItem()
    {
        var items = new[] { Money.Create(10.00m, "USD") };

        var result = Money.Sum(items);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(10.00m);
    }

    [Fact]
    public void Sum_MultipleItems_SameCurrency_ReturnsTotal()
    {
        var items = new[]
        {
            Money.Create(10.00m, "USD"),
            Money.Create(20.50m, "USD"),
            Money.Create(5.25m, "USD"),
        };

        var result = Money.Sum(items);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(35.75m);
        result.Unwrap().Currency.Should().Be(CurrencyCode.Create("USD"));
    }

    [Fact]
    public void Sum_MixedCurrencies_ReturnsFailure()
    {
        var items = new[]
        {
            Money.Create(10.00m, "USD"),
            Money.Create(20.00m, "EUR"),
        };

        var result = Money.Sum(items);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Sum_EmptyCollection_ReturnsFailure()
    {
        var result = Money.Sum(Array.Empty<Money>());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Sum_NullCollection_ThrowsArgumentNull()
    {
        var act = () => Money.Sum(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Sum_CollectionWithNullElement_ThrowsArgumentException()
    {
        // Inspection finding New-3: Sum<Money> should reject null elements explicitly
        // rather than NRE on element.Currency.
        var items = new[]
        {
            Money.Create(10m, "USD"),
            null!,
            Money.Create(5m, "USD"),
        };

        var act = () => Money.Sum(items);

        act.Should().Throw<ArgumentException>()
            .Where(ex => ex.ParamName == "values");
    }

    #endregion

    #region Inspection regression tests (Trellis.Primitives M-1, M-3, i-6, New-1)

    [Fact]
    public void Add_NullOther_ThrowsArgumentNullException()
    {
        // Inspection finding M-1: arithmetic methods must reject null other.
        var money = Money.Create(10m, "USD");

        FluentActions.Invoking(() => money.Add(null!))
            .Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "other");
    }

    [Fact]
    public void Subtract_NullOther_ThrowsArgumentNullException()
    {
        var money = Money.Create(10m, "USD");

        FluentActions.Invoking(() => money.Subtract(null!))
            .Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "other");
    }

    [Fact]
    public void IsGreaterThan_NullOther_ThrowsArgumentNullException()
    {
        var money = Money.Create(10m, "USD");

        FluentActions.Invoking(() => money.IsGreaterThan(null!))
            .Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "other");
    }

    [Fact]
    public void IsGreaterThanOrEqual_NullOther_ThrowsArgumentNullException()
    {
        var money = Money.Create(10m, "USD");

        FluentActions.Invoking(() => money.IsGreaterThanOrEqual(null!))
            .Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "other");
    }

    [Fact]
    public void IsLessThan_NullOther_ThrowsArgumentNullException()
    {
        var money = Money.Create(10m, "USD");

        FluentActions.Invoking(() => money.IsLessThan(null!))
            .Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "other");
    }

    [Fact]
    public void IsLessThanOrEqual_NullOther_ThrowsArgumentNullException()
    {
        var money = Money.Create(10m, "USD");

        FluentActions.Invoking(() => money.IsLessThanOrEqual(null!))
            .Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "other");
    }

    [Fact]
    public void Allocate_NullRatios_ThrowsArgumentNullException()
    {
        // Inspection finding M-3: Allocate must null-guard ratios (the params syntactic
        // sugar makes a direct null-array call possible from non-params call sites).
        var money = Money.Create(100m, "USD");

        FluentActions.Invoking(() => money.Allocate(null!))
            .Should().Throw<ArgumentNullException>()
            .Where(ex => ex.ParamName == "ratios");
    }

    [Fact]
    public void Allocate_RatiosOverflow_ReturnsFailure()
    {
        // Inspection finding M-3: ratios.Sum() can OverflowException on int overflow;
        // wrap in Result.Fail with field "ratios" rather than letting the exception propagate.
        var money = Money.Create(100m, "USD");

        var result = money.Allocate(int.MaxValue, int.MaxValue);

        result.IsFailure.Should().BeTrue();
        var err = (Error.UnprocessableContent)result.UnwrapError();
        err.Fields[0].Field.Path.Should().Be("/ratios", "ratio-sum overflow is a ratios-side failure");
        err.Fields[0].Detail.Should().Contain("overflow");
    }

    [Fact]
    public void Multiply_DecimalOverflow_ReturnsFailure()
    {
        // Inspection finding i-6: Multiply must wrap OverflowException to Result.Fail
        // for parity with Add (which already does this).
        var money = Money.Create(decimal.MaxValue / 2, "USD");

        var result = money.Multiply(3m);

        result.IsFailure.Should().BeTrue();
        var err = (Error.UnprocessableContent)result.UnwrapError();
        err.Fields[0].Detail.Should().Contain("overflow");
    }

    [Fact]
    public void Multiply_IntOverflow_ReturnsFailure()
    {
        var money = Money.Create(decimal.MaxValue / 2, "USD");

        var result = money.Multiply(3);

        result.IsFailure.Should().BeTrue();
        var err = (Error.UnprocessableContent)result.UnwrapError();
        err.Fields[0].Detail.Should().Contain("overflow");
    }

    [Fact]
    public void Divide_DecimalDivisorTinyOverflow_ReturnsFailure()
    {
        // Inspection finding i-6: Divide by a tiny positive divisor can overflow decimal.
        var money = Money.Create(decimal.MaxValue / 2, "USD");

        var result = money.Divide(0.0000000000000000000000000001m);

        result.IsFailure.Should().BeTrue();
        var err = (Error.UnprocessableContent)result.UnwrapError();
        err.Fields[0].Detail.Should().Contain("overflow");
    }

    [Theory]
    // Inspection finding New-1: Money's ISO 4217 minor-unit table was incomplete.
    // 0-decimal currencies beyond JPY/KRW must round to 0 decimals on creation.
    [InlineData("BIF", 100.99, 101)]    // Burundian Franc
    [InlineData("CLP", 12345.67, 12346)] // Chilean Peso
    [InlineData("DJF", 50.5, 51)]       // Djiboutian Franc
    [InlineData("GNF", 1000.49, 1000)]  // Guinean Franc
    [InlineData("ISK", 999.99, 1000)]   // Icelandic Króna
    [InlineData("KMF", 250.4, 250)]     // Comorian Franc
    [InlineData("PYG", 50000.5, 50001)] // Paraguayan Guaraní
    [InlineData("RWF", 1500.25, 1500)]  // Rwandan Franc
    [InlineData("UGX", 5000.75, 5001)]  // Ugandan Shilling
    [InlineData("UYI", 1500.5, 1501)]   // Uruguay Peso en Unidades Indexadas
    [InlineData("VND", 25000.4, 25000)] // Vietnamese Đồng
    [InlineData("VUV", 1000.5, 1001)]   // Vanuatu Vatu
    [InlineData("XAF", 500.7, 501)]     // CFA Franc BEAC
    [InlineData("XOF", 750.3, 750)]     // CFA Franc BCEAO
    [InlineData("XPF", 1200.6, 1201)]   // CFP Franc
    public void TryCreate_ZeroMinorUnitCurrencies_RoundToWholeUnits(string currency, decimal input, decimal expected)
    {
        var result = Money.TryCreate(input, currency);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(expected, $"{currency} has 0 minor units per ISO 4217");
    }

    [Theory]
    // 3-decimal currencies beyond BHD/KWD/OMR/TND.
    [InlineData("IQD", 100.12345, 100.123)] // Iraqi Dinar
    [InlineData("JOD", 50.6789, 50.679)]    // Jordanian Dinar
    [InlineData("LYD", 75.4321, 75.432)]    // Libyan Dinar
    public void TryCreate_ThreeMinorUnitCurrencies_RoundToThreeDecimals(string currency, decimal input, decimal expected)
    {
        var result = Money.TryCreate(input, currency);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(expected, $"{currency} has 3 minor units per ISO 4217");
    }

    [Theory]
    // 4-decimal currencies (CLF / UYW are unidad-de-fomento types).
    [InlineData("CLF", 1.23456789, 1.2346)]  // Chilean Unidad de Fomento
    [InlineData("UYW", 2.34567, 2.3457)]     // Unidad Previsional (Uruguay)
    public void TryCreate_FourMinorUnitCurrencies_RoundToFourDecimals(string currency, decimal input, decimal expected)
    {
        var result = Money.TryCreate(input, currency);

        result.IsSuccess.Should().BeTrue();
        result.Unwrap().Amount.Should().Be(expected, $"{currency} has 4 minor units per ISO 4217");
    }

    [Fact]
    public void Allocate_ShareArithmeticOverflow_ReturnsFailure()
    {
        // Inspection finding refuted by GPT-5.5: amountInMinorUnits * ratios[i] is
        // unchecked long*int and silently wraps for extreme amount + large ratio
        // combinations. Force a checked context so overflow is caught and surfaced
        // as Result.Fail. This is an amount-side failure (the amount * ratio product
        // overflows because the amount is large), so the field should be "amount".
        var money = Money.Create(50_000_000_000m, "USD");

        // amountInMinorUnits = 5e12; share-mul: 5e12 * 1e9 = 5e21 > long.MaxValue (9.2e18).
        var result = money.Allocate(1_000_000_000, 1_000_000_000);

        result.IsFailure.Should().BeTrue();
        var err = (Error.UnprocessableContent)result.UnwrapError();
        err.Fields[0].Field.Path.Should().Be("/amount", "share-arithmetic overflow is an amount-side failure");
        err.Fields[0].Detail.Should().Contain("overflow");
    }

    #endregion
}