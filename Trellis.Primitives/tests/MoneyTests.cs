using Trellis.Primitives;

namespace Trellis.Primitives.Tests;

using Trellis;
using System.Text.Json;
using System.Reflection;

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
        result.Value.Currency.Value.Should().Be(currency);
    }

    [Fact]
    public void Cannot_create_Money_with_negative_amount()
    {
        // Act
        var result = Money.TryCreate(-50.00m, "USD");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Amount cannot be negative.");
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
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("unitPrice");
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
        result.Value.Amount.Should().Be(expected);
    }

    #endregion

    #region Arithmetic Tests

    [Fact]
    public void Can_add_Money_with_same_currency()
    {
        // Arrange
        var left = Money.TryCreate(50.25m, "USD").Value;
        var right = Money.TryCreate(25.75m, "USD").Value;

        // Act
        var result = left.Add(right);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(76.00m);
        result.Value.Currency.Value.Should().Be("USD");
    }

    [Fact]
    public void Cannot_add_Money_with_different_currency()
    {
        // Arrange
        var left = Money.TryCreate(50.00m, "USD").Value;
        var right = Money.TryCreate(40.00m, "EUR").Value;

        // Act
        var result = left.Add(right);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Cannot add EUR to USD.");
    }

    [Fact]
    public void Can_subtract_Money_with_same_currency()
    {
        // Arrange
        var left = Money.TryCreate(100.00m, "USD").Value;
        var right = Money.TryCreate(35.50m, "USD").Value;

        // Act
        var result = left.Subtract(right);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(64.50m);
    }

    [Fact]
    public void Subtract_resulting_in_negative_amount_fails()
    {
        // Arrange
        var left = Money.TryCreate(40.00m, "USD").Value;
        var right = Money.TryCreate(100.00m, "USD").Value;

        // Act
        var result = left.Subtract(right);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Amount cannot be negative.");
    }

    [Fact]
    public void Cannot_subtract_Money_with_different_currency()
    {
        // Arrange
        var left = Money.TryCreate(100.00m, "USD").Value;
        var right = Money.TryCreate(35.50m, "GBP").Value;

        // Act
        var result = left.Subtract(right);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Cannot subtract GBP from USD.");
    }

    [Fact]
    public void Can_multiply_Money_by_decimal()
    {
        // Arrange
        var money = Money.TryCreate(19.99m, "USD").Value;

        // Act
        var result = money.Multiply(3.5m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(69.97m);
        result.Value.Currency.Value.Should().Be("USD");
    }

    [Fact]
    public void Can_multiply_Money_by_integer()
    {
        // Arrange
        var money = Money.TryCreate(12.50m, "USD").Value;

        // Act
        var result = money.Multiply(5);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(62.50m);
    }

    [Fact]
    public void Cannot_multiply_Money_by_negative()
    {
        // Arrange
        var money = Money.TryCreate(50.00m, "USD").Value;

        // Act
        var resultDecimal = money.Multiply(-2m);
        var resultInt = money.Multiply(-3);

        // Assert
        resultDecimal.IsFailure.Should().BeTrue();
        var validation = (ValidationError)resultDecimal.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Multiplier cannot be negative.");

        resultInt.IsFailure.Should().BeTrue();
        var validationInt = (ValidationError)resultInt.Error;
        validationInt.FieldErrors[0].Details[0].Should().Be("Quantity cannot be negative.");
    }

    [Fact]
    public void Can_divide_Money_by_decimal()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Value;

        // Act
        var result = money.Divide(3m);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(33.33m);
    }

    [Fact]
    public void Can_divide_Money_by_integer()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Value;

        // Act
        var result = money.Divide(4);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Amount.Should().Be(25.00m);
    }

    [Fact]
    public void Cannot_divide_Money_by_zero()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Value;

        // Act
        var result = money.Divide(0m);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Divisor must be positive.");
    }

    [Fact]
    public void Cannot_divide_Money_by_negative_integer()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Value;

        // Act
        var result = money.Divide(-2);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("Divisor must be positive.");
    }

    #endregion

    #region Allocation Tests

    [Fact]
    public void Can_allocate_Money_equally()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Value;

        // Act
        var result = money.Allocate(1, 1, 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value[0].Amount.Should().Be(33.34m); // Gets the remainder
        result.Value[1].Amount.Should().Be(33.33m);
        result.Value[2].Amount.Should().Be(33.33m);
        result.Value.Sum(m => m.Amount).Should().Be(100.00m); // No money lost
    }

    [Fact]
    public void Can_allocate_Money_by_ratio()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Value;

        // Act
        var result = money.Allocate(1, 2, 1); // 25%, 50%, 25%

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);
        result.Value[0].Amount.Should().Be(25.00m);
        result.Value[1].Amount.Should().Be(50.00m);
        result.Value[2].Amount.Should().Be(25.00m);
    }

    [Fact]
    public void Cannot_allocate_Money_with_empty_ratios()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Value;

        // Act
        var result = money.Allocate();

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("At least one ratio required.");
    }

    [Fact]
    public void Cannot_allocate_Money_with_negative_ratio()
    {
        // Arrange
        var money = Money.TryCreate(100.00m, "USD").Value;

        // Act
        var result = money.Allocate(1, -2, 1);

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].Details[0].Should().Be("All ratios must be positive.");
    }

    #endregion

    #region Comparison Tests

    [Fact]
    public void Can_compare_Money_IsGreaterThan()
    {
        // Arrange
        var left = Money.TryCreate(100.00m, "USD").Value;
        var right = Money.TryCreate(50.00m, "USD").Value;

        // Act & Assert
        left.IsGreaterThan(right).Should().BeTrue();
        right.IsGreaterThan(left).Should().BeFalse();
    }

    [Fact]
    public void Can_compare_Money_IsLessThan()
    {
        // Arrange
        var left = Money.TryCreate(25.00m, "USD").Value;
        var right = Money.TryCreate(50.00m, "USD").Value;

        // Act & Assert
        left.IsLessThan(right).Should().BeTrue();
        right.IsLessThan(left).Should().BeFalse();
    }

    [Fact]
    public void Can_compare_Money_IsGreaterThanOrEqual()
    {
        // Arrange
        var left = Money.TryCreate(100.00m, "USD").Value;
        var right = Money.TryCreate(50.00m, "USD").Value;
        var equal = Money.TryCreate(100.00m, "USD").Value;

        // Act & Assert
        left.IsGreaterThanOrEqual(right).Should().BeTrue();
        left.IsGreaterThanOrEqual(equal).Should().BeTrue();
        right.IsGreaterThanOrEqual(left).Should().BeFalse();
    }

    [Fact]
    public void Can_compare_Money_IsLessThanOrEqual()
    {
        // Arrange
        var left = Money.TryCreate(25.00m, "USD").Value;
        var right = Money.TryCreate(50.00m, "USD").Value;
        var equal = Money.TryCreate(25.00m, "USD").Value;

        // Act & Assert
        left.IsLessThanOrEqual(right).Should().BeTrue();
        left.IsLessThanOrEqual(equal).Should().BeTrue();
        right.IsLessThanOrEqual(left).Should().BeFalse();
    }

    [Fact]
    public void Cannot_compare_Money_with_different_currency()
    {
        // Arrange
        var left = Money.TryCreate(100.00m, "USD").Value;
        var right = Money.TryCreate(80.00m, "EUR").Value;

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
        var a = Money.TryCreate(50.00m, "USD").Value;
        var b = Money.TryCreate(50.00m, "USD").Value;

        // Assert
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Two_Money_with_different_amount_should_not_be_equal()
    {
        // Arrange
        var a = Money.TryCreate(50.00m, "USD").Value;
        var b = Money.TryCreate(75.00m, "USD").Value;

        // Assert
        (a != b).Should().BeTrue();
        a.Equals(b).Should().BeFalse();
    }

    [Fact]
    public void Two_Money_with_different_currency_should_not_be_equal()
    {
        // Arrange
        var a = Money.TryCreate(50.00m, "USD").Value;
        var b = Money.TryCreate(50.00m, "EUR").Value;

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
        var money = Money.TryCreate(99.99m, "USD").Value;

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
            .WithMessage("Currency is required.");
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
        result.Value.Amount.Should().Be(0m);
        result.Value.Currency.Value.Should().Be("EUR");
    }

    [Fact]
    public void Zero_defaults_to_USD()
    {
        // Act
        var result = Money.Zero();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Currency.Value.Should().Be("USD");
    }

    [Fact]
    public void ToString_returns_formatted_amount_with_currency()
    {
        // Arrange
        var money = Money.TryCreate(99.99m, "USD").Value;

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
}