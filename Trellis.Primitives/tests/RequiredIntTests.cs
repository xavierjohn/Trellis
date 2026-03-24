namespace Trellis.Primitives.Tests;

using System.Globalization;
using System.Text.Json;

public partial class TicketNumber : RequiredInt<TicketNumber>
{
}

internal partial class InternalTicketNumber : RequiredInt<InternalTicketNumber>
{
}

public class RequiredIntTests
{
    [Fact]
    public void Cannot_create_null_RequiredInt()
    {
        // Act
        var ticketNumber = TicketNumber.TryCreate((int?)null);

        // Assert
        ticketNumber.IsFailure.Should().BeTrue();
        ticketNumber.Error.Should().BeOfType<ValidationError>();
        var validation = (ValidationError)ticketNumber.Error;
        validation.FieldErrors[0].FieldName.Should().Be("ticketNumber");
        validation.FieldErrors[0].Details[0].Should().Be("Ticket Number cannot be empty.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(-5)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Can_create_RequiredInt(int value)
    {
        // Act
        var result = InternalTicketNumber.TryCreate(value);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<InternalTicketNumber>();
        result.Value.Value.Should().Be(value);
    }

    [Fact]
    public void Two_RequiredInt_with_same_value_should_be_equal() =>
        TicketNumber.TryCreate(12345)
            .Combine(TicketNumber.TryCreate(12345))
            .Tap((t1, t2) =>
            {
                (t1 == t2).Should().BeTrue();
                t1.Equals(t2).Should().BeTrue();
                t1.GetHashCode().Should().Be(t2.GetHashCode());
            })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Two_RequiredInt_with_different_value_should_be_not_equal() =>
        TicketNumber.TryCreate(12345)
            .Combine(TicketNumber.TryCreate(54321))
            .Tap((t1, t2) =>
            {
                (t1 != t2).Should().BeTrue();
                t1.Equals(t2).Should().BeFalse();
            })
            .IsSuccess.Should().BeTrue();

    [Fact]
    public void Can_implicitly_cast_to_int()
    {
        // Arrange
        TicketNumber ticketNumber = TicketNumber.TryCreate(12345).Value;

        // Act
        int intValue = ticketNumber;

        // Assert
        intValue.Should().Be(12345);
    }

    [Fact]
    public void Can_explicitly_cast_to_RequiredInt()
    {
        // Act
        TicketNumber ticketNumber = (TicketNumber)12345;

        // Assert
        ticketNumber.Should().Be(TicketNumber.TryCreate(12345).Value);
    }

    [Fact]
    public void Can_use_ToString()
    {
        // Arrange
        TicketNumber ticketNumber = TicketNumber.TryCreate(12345).Value;

        // Act
        var strValue = ticketNumber.ToString(CultureInfo.InvariantCulture);

        // Assert
        strValue.Should().Be("12345");
    }

    [Fact]
    public void Can_create_RequiredInt_from_try_parsing_valid_string()
    {
        // Arrange
        var strTicketNumber = "12345";

        // Act
        TicketNumber.TryParse(strTicketNumber, null, out var ticketNumber)
            .Should().BeTrue();

        // Assert
        ticketNumber.Should().BeOfType<TicketNumber>();
        ticketNumber!.Value.Should().Be(12345);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Cannot_create_RequiredInt_from_try_parsing_invalid_string(string? input)
    {
        // Act
        TicketNumber.TryParse(input, null, out var ticketNumber)
            .Should().BeFalse();

        // Assert
        ticketNumber.Should().BeNull();
    }

    [Fact]
    public void Can_create_RequiredInt_from_parsing_valid_string()
    {
        // Arrange
        var strTicketNumber = "12345";

        // Act
        var ticketNumber = TicketNumber.Parse(strTicketNumber, null);

        // Assert
        ticketNumber.Should().BeOfType<TicketNumber>();
        ticketNumber.Value.Should().Be(12345);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Cannot_create_RequiredInt_from_parsing_invalid_string(string? input)
    {
        // Act
        Action act = () => TicketNumber.Parse(input!, null);

        // Assert
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Can_use_Contains()
    {
        // Arrange
        var id1 = TicketNumber.TryCreate(1).Value;
        var id2 = TicketNumber.TryCreate(2).Value;
        IReadOnlyList<TicketNumber> ids = new List<TicketNumber> { id1, id2 };

        // Act
        var actual = ids.Contains(id1);

        // Assert
        actual.Should().BeTrue();
    }

    [Fact]
    public void ConvertToJson()
    {
        // Arrange
        var intValue = 12345;
        TicketNumber ticketNumber = TicketNumber.TryCreate(intValue).Value;

        // Act
        var actual = JsonSerializer.Serialize(ticketNumber);

        // Assert — numeric value objects serialize as JSON numbers
        actual.Should().Be($"{intValue}");
    }

    [Fact]
    public void ConvertFromJson()
    {
        // Arrange — deserialize from a JSON string token
        var intValue = 12345;
        var json = $"\"{intValue}\"";

        // Act
        TicketNumber actual = JsonSerializer.Deserialize<TicketNumber>(json)!;

        // Assert
        actual.Value.Should().Be(intValue);
    }

    [Fact]
    public void ConvertFromJson_NumericToken()
    {
        // Arrange — deserialize from a JSON number token
        var intValue = 12345;
        var json = $"{intValue}";

        // Act
        TicketNumber actual = JsonSerializer.Deserialize<TicketNumber>(json)!;

        // Assert
        actual.Value.Should().Be(intValue);
    }

    [Fact]
    public void TryCreate_with_custom_fieldName()
    {
        // Act
        var result = TicketNumber.TryCreate((int?)null, "myField");

        // Assert
        result.IsFailure.Should().BeTrue();
        var validation = (ValidationError)result.Error;
        validation.FieldErrors[0].FieldName.Should().Be("myField");
    }
}