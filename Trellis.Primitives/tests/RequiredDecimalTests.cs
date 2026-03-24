namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;

public partial class UnitPrice : RequiredDecimal<UnitPrice>
{
}

internal partial class InternalUnitPrice : RequiredDecimal<InternalUnitPrice>
{
}

public class RequiredDecimalTests
{
    [Fact]
    public void Cannot_create_null_RequiredDecimal()
    {
        // Act
        var unitPrice = UnitPrice.TryCreate((decimal?)null);

        // Assert
        unitPrice.IsFailure.Should().BeTrue();
        unitPrice.Error.Should().BeOfType<ValidationError>();
        var validation = (ValidationError)unitPrice.Error;
        validation.FieldErrors[0].FieldName.Should().Be("unitPrice");
        validation.FieldErrors[0].Details[0].Should().Be("Unit Price cannot be empty.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    [InlineData(19.99)]
    [InlineData(-5.50)]
    [InlineData(1000000.00)]
    public void Can_create_RequiredDecimal(decimal value)
    {
        // Act
        var result = InternalUnitPrice.TryCreate(value);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<InternalUnitPrice>();
        result.Value.Value.Should().Be(value);
    }

    [Fact]
    public void Two_RequiredDecimal_with_same_value_should_be_equal() =>
        UnitPrice.TryCreate(19.99m)
            .Combine(UnitPrice.TryCreate(19.99m))
            .Tap((t1, t2) =>
            {
                (t1 == t2).Should().BeTrue();
                t1.Equals(t2).Should().BeTrue();
                t1.GetHashCode().Should().Be(t2.GetHashCode());
            })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Two_RequiredDecimal_with_different_value_should_be_not_equal() =>
        UnitPrice.TryCreate(19.99m)
            .Combine(UnitPrice.TryCreate(29.99m))
            .Tap((t1, t2) =>
            {
                (t1 != t2).Should().BeTrue();
                t1.Equals(t2).Should().BeFalse();
            })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Can_implicitly_cast_to_decimal()
    {
        // Arrange
        UnitPrice unitPrice = UnitPrice.TryCreate(19.99m).Value;

        // Act
        decimal decimalValue = unitPrice;

        // Assert
        decimalValue.Should().Be(19.99m);
    }

    [Fact]
    public void Can_explicitly_cast_to_RequiredDecimal()
    {
        // Act
        UnitPrice unitPrice = (UnitPrice)19.99m;

        // Assert
        unitPrice.Should().Be(UnitPrice.TryCreate(19.99m).Value);
    }

    [Fact]
    public void Can_use_ToString()
    {
        // Arrange
        UnitPrice unitPrice = UnitPrice.TryCreate(19.99m).Value;

        // Act
        var strValue = unitPrice.ToString(CultureInfo.InvariantCulture);

        // Assert
        strValue.Should().Be("19.99");
    }

    [Fact]
    public void Can_create_RequiredDecimal_from_try_parsing_valid_string()
    {
        // Arrange
        var strUnitPrice = "19.99";

        // Act
        UnitPrice.TryParse(strUnitPrice, CultureInfo.InvariantCulture, out var unitPrice)
            .Should().BeTrue();

        // Assert
        unitPrice.Should().BeOfType<UnitPrice>();
        unitPrice!.Value.Should().Be(19.99m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Cannot_create_RequiredDecimal_from_try_parsing_invalid_string(string? input)
    {
        // Act
        UnitPrice.TryParse(input, CultureInfo.InvariantCulture, out var unitPrice)
            .Should().BeFalse();

        // Assert
        unitPrice.Should().BeNull();
    }

    [Fact]
    public void Can_create_RequiredDecimal_from_parsing_valid_string()
    {
        // Arrange
        var strUnitPrice = "19.99";

        // Act
        var unitPrice = UnitPrice.Parse(strUnitPrice, CultureInfo.InvariantCulture);

        // Assert
        unitPrice.Should().BeOfType<UnitPrice>();
        unitPrice.Value.Should().Be(19.99m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Cannot_create_RequiredDecimal_from_parsing_invalid_string(string? input)
    {
        // Act
        Action act = () => UnitPrice.Parse(input!, CultureInfo.InvariantCulture);

        // Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Can_use_Contains()
    {
        // Arrange
        var p1 = UnitPrice.TryCreate(10.00m).Value;
        var p2 = UnitPrice.TryCreate(20.00m).Value;
        IReadOnlyList<UnitPrice> prices = new List<UnitPrice> { p1, p2 };

        // Act
        var actual = prices.Contains(p1);

        // Assert
        actual.Should().BeTrue();
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var decimalValue = 19.99m;
        UnitPrice unitPrice = UnitPrice.TryCreate(decimalValue).Value;

        // Act
        var actual = JsonSerializer.Serialize(unitPrice);

        // Assert — numeric value objects serialize as JSON numbers
        actual.Should().Be("19.99");
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange — deserialize from a JSON string token
        var decimalValue = 19.99m;
        var json = "\"19.99\"";

        // Act
        UnitPrice actual = JsonSerializer.Deserialize<UnitPrice>(json)!;

        // Assert
        actual.Value.Should().Be(decimalValue);
    }

    [Fact]
    public void ConvertFromJson_NumericToken()
    {
        // Arrange — deserialize from a JSON number token
        var decimalValue = 19.99m;
        var json = "19.99";

        // Act
        UnitPrice actual = JsonSerializer.Deserialize<UnitPrice>(json)!;

        // Assert
        actual.Value.Should().Be(decimalValue);
    }

    [Fact]
    public void TryCreate_with_custom_fieldName()
    {
        // Act
        var result = UnitPrice.TryCreate((decimal?)null, "myField");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("myField");
    }
}